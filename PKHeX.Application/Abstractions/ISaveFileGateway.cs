using PKHeX.Core;

namespace PKHeX.Application.Abstractions;

public interface ISaveFileGateway
{
    SaveFile? CurrentSave { get; }
    bool HasSave { get; }

    /// <summary>Path the current save was loaded from / last written to, or <see langword="null"/> if unknown (e.g. not yet saved).</summary>
    string? CurrentPath { get; }

    /// <summary>
    /// True when the current save was opened read-only: <see cref="SaveFileAsync"/> refuses to write over the
    /// file it came from. Saving to a different path writes a copy, which then becomes the writable current file.
    /// </summary>
    bool IsReadOnly { get; }

    Task<bool> LoadSaveFileAsync(string path);

    /// <summary>Loads a save like <see cref="LoadSaveFileAsync(string)"/>, optionally marking it read-only.</summary>
    Task<bool> LoadSaveFileAsync(string path, bool readOnly);

    /// <summary>True when the current save is read-only and <paramref name="path"/> is the file it was opened from.</summary>
    bool IsReadOnlySource(string path);

    /// <summary>
    /// Adopts an already-constructed <see cref="SaveFile"/> as the current save, bypassing path-based
    /// auto-detection. Used by the Save Handler Troubleshooter to open a save that the normal loader
    /// could not recognize.
    /// </summary>
    /// <param name="sav">The save file to make current.</param>
    /// <param name="path">Optional originating path, recorded so a later Save writes back to the right file.</param>
    void OpenLoadedSave(SaveFile sav, string? path = null);

    Task<bool> SaveFileAsync(string? path = null);
    void CloseSave();

    event Action<SaveFile?>? SaveFileChanged;

    /// <summary>
    /// Raised after <see cref="SaveFileAsync"/> has successfully written a save to disk — the single
    /// point at which edits to an open save become durable. Raised on the caller's synchronization
    /// context, after the write completed, even if the user switched saves while it ran.
    /// </summary>
    event Action<SaveFileWrittenEventArgs>? SaveWritten;

    /// <summary>
    /// Raised on the caller's thread when a write of the given live save has been snapshotted and is
    /// about to run. Every start is followed by exactly one <see cref="SaveWritten"/> or
    /// <see cref="SaveWriteFailed"/> for the same save.
    /// </summary>
    event Action<SaveFile>? SaveWriteStarted;

    /// <summary>Raised on the caller's synchronization context when a started write did not reach the disk.</summary>
    event Action<SaveFile>? SaveWriteFailed;
}

/// <summary>Details of a completed save-to-disk write (see <see cref="ISaveFileGateway.SaveWritten"/>).</summary>
/// <param name="Original">The live save object the user was editing when the write began.</param>
/// <param name="Written">
/// The detached snapshot that was actually serialized to disk. Inspect this, not <paramref name="Original"/>:
/// the live save may have been edited again while the write ran.
/// </param>
/// <param name="Path">The file that was written.</param>
public sealed record SaveFileWrittenEventArgs(SaveFile Original, SaveFile Written, string Path);
