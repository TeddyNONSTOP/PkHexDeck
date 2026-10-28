using PKHeX.Application.Abstractions;
using PKHeX.Application.Services;
using PKHeX.Core;
using System.Threading;

namespace PKHeX.Infrastructure;

public sealed class SaveFileService : ISaveFileGateway
{
    private readonly ISaveBackupService _backupService;
    private readonly AppSettings _settings;
    private readonly ISaveWriteGuard? _writeGuard;
    private readonly object _stateLock = new();
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private readonly SemaphoreSlim _fileGate = new(1, 1);

    private SaveFile? _currentSave;
    private string? _currentPath;
    private bool _isReadOnly;
    private long _sessionId;

    public SaveFile? CurrentSave
    {
        get
        {
            lock (_stateLock)
                return _currentSave;
        }
    }

    public bool HasSave => CurrentSave is not null;
    public string? CurrentPath
    {
        get
        {
            lock (_stateLock)
                return _currentPath;
        }
    }

    public bool IsReadOnly
    {
        get
        {
            lock (_stateLock)
                return _currentSave is not null && _isReadOnly;
        }
    }

    public bool IsReadOnlySource(string path)
    {
        lock (_stateLock)
            return _currentSave is not null && _isReadOnly && IsSameFile(path, _currentPath);
    }

    public event Action<SaveFile?>? SaveFileChanged;
    public event Action<SaveFileWrittenEventArgs>? SaveWritten;
    public event Action<SaveFile>? SaveWriteStarted;
    public event Action<SaveFile>? SaveWriteFailed;

    public SaveFileService(ISaveBackupService backupService, AppSettings settings, ISaveWriteGuard? writeGuard = null)
    {
        _backupService = backupService;
        _settings = settings;
        _writeGuard = writeGuard;
    }

    public Task<bool> LoadSaveFileAsync(string path) => LoadSaveFileAsync(path, readOnly: false);

    public async Task<bool> LoadSaveFileAsync(string path, bool readOnly)
    {
        await _loadGate.WaitAsync();
        await _fileGate.WaitAsync();
        try
        {
            var sav = await Task.Run(() =>
            {
                try
                {
                    var obj = FileUtil.GetSupportedFile(path);
                    return obj as SaveFile;
                }
                catch
                {
                    return null;
                }
            });

            if (sav is null)
                return false;

            PublishSave(sav, path, readOnly);
            return true;
        }
        finally
        {
            _fileGate.Release();
            _loadGate.Release();
        }
    }

    public void OpenLoadedSave(SaveFile sav, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(sav);
        PublishSave(sav, path ?? sav.Metadata.FilePath, readOnly: false);
    }

    public async Task<bool> SaveFileAsync(string? path = null)
    {
        SaveFile save;
        SaveFile originalSave;
        string savePath;
        long sessionId;

        try
        {
            lock (_stateLock)
            {
                if (_currentSave is null)
                    return false;

                savePath = path ?? _currentPath ?? string.Empty;
                if (string.IsNullOrEmpty(savePath))
                    return false;
                // A read-only save may only be written to a different file (Save As).
                if (_isReadOnly && IsSameFile(savePath, _currentPath))
                    return false;

                // Clone on the caller's thread, then serialize the detached copy on the worker.
                // The live save can be switched or edited while backup/file I/O runs, but the
                // worker can no longer observe a later session's mutable buffer.
                originalSave = _currentSave;
                save = originalSave.Clone();
                sessionId = _sessionId;
            }

            // Refused before anything is reported as started: no backup is made and nothing is written.
            if (_writeGuard?.CheckWrite(originalSave, save) is not null)
                return false;
        }
        catch
        {
            return false;
        }

        RaiseWriteEvent(SaveWriteStarted, originalSave);
        var result = await Task.Run(() => SaveSnapshot(save, originalSave, savePath, path is not null, sessionId));
        if (!result.Success)
        {
            RaiseWriteEvent(SaveWriteFailed, originalSave);
            return false;
        }

        // The await returns to the caller's synchronization context (the UI dispatcher in the
        // desktop app). Compare there so the worker never reads the mutable live save.
        lock (_stateLock)
        {
            if (_sessionId == sessionId && ReferenceEquals(_currentSave, originalSave))
            {
                try
                {
                    if (originalSave.Write().Span.SequenceEqual(result.Data))
                        originalSave.State.Edited = false;
                }
                catch
                {
                    // The disk write succeeded, but the live state could not be compared. Keep
                    // the warning active rather than falsely declaring the save clean.
                }
            }
        }

        try
        {
            _writeGuard?.OnSaveWritten(originalSave, save);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning($"The save write guard failed after a write: {ex.Message}");
        }

        try
        {
            SaveWritten?.Invoke(new SaveFileWrittenEventArgs(originalSave, save, savePath));
        }
        catch (Exception ex)
        {
            // The file is already safely on disk; a listener failure must not report the save as failed.
            System.Diagnostics.Trace.TraceWarning($"A SaveWritten listener failed: {ex.Message}");
        }
        return true;
    }

    private static void RaiseWriteEvent(Action<SaveFile>? handler, SaveFile save)
    {
        try
        {
            handler?.Invoke(save);
        }
        catch (Exception ex)
        {
            // Listener failures must not change the outcome of the save itself.
            System.Diagnostics.Trace.TraceWarning($"A save-write listener failed: {ex.Message}");
        }
    }

    private (bool Success, byte[] Data) SaveSnapshot(SaveFile save, SaveFile originalSave, string savePath, bool updatePath, long sessionId)
    {
        try
        {
            var data = save.Write().ToArray();
            _fileGate.Wait();
            try
            {
                // Automatic backup: snapshot the bytes currently on disk before overwriting them.
                if (_settings.Backup.BAKEnabled && File.Exists(savePath))
                {
                    try
                    {
                        var existing = File.ReadAllBytes(savePath);
                        var identity = SaveIdentity.Compute(savePath);
                        _backupService.CreateBackup(identity, existing, _settings.SaveBackup.MaxBackupsPerSave);
                    }
                    catch
                    {
                        // Backing up must never prevent the user from saving.
                    }
                }

                AtomicFileWriter.WriteAllBytes(savePath, data);

                lock (_stateLock)
                {
                    if (_sessionId == sessionId && ReferenceEquals(_currentSave, originalSave))
                    {
                        if (updatePath)
                        {
                            // The copy just written is a new, ordinary file: further saves go there.
                            _currentPath = savePath;
                            _isReadOnly = false;
                        }
                    }
                }

                return (true, data);
            }
            finally
            {
                _fileGate.Release();
            }
        }
        catch
        {
            return (false, []);
        }
    }

    private void PublishSave(SaveFile sav, string? path, bool readOnly)
    {
        lock (_stateLock)
        {
            _currentSave = sav;
            _currentPath = path;
            _isReadOnly = readOnly;
            _sessionId++;
        }

        _writeGuard?.OnSaveOpened(sav);
        SaveFileChanged?.Invoke(sav);
    }

    public void CloseSave()
    {
        lock (_stateLock)
        {
            _currentSave = null;
            _currentPath = null;
            _isReadOnly = false;
            _sessionId++;
        }

        _writeGuard?.OnSaveOpened(null);
        SaveFileChanged?.Invoke(null);
    }

    // Compares the files two paths end up at, following a symlink at the final component so a
    // link to the read-only source cannot be used to write over it.
    private static bool IsSameFile(string path, string? other)
    {
        if (string.IsNullOrEmpty(other))
            return false;
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(ResolvePath(path), ResolvePath(other), comparison);
    }

    private static string ResolvePath(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            return new FileInfo(full).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? full;
        }
        catch
        {
            return path;
        }
    }
}
