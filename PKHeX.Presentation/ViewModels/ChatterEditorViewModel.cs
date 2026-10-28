using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Core;
using PKHeX.Presentation.Localization;

namespace PKHeX.Presentation.ViewModels;

public partial class ChatterEditorViewModel : ViewModelBase, ICloseableDialog
{
    private readonly SaveFile _source;
    private readonly IChatter? _chatter;
    private readonly IDialogService? _dialogs;
    private readonly IAudioPlaybackService? _audio;

    public Action? CloseRequested { get; set; }
    public bool IsSupported => _chatter is not null;
    public bool IsAudioAvailable => _audio?.IsAvailable == true;
    public int ConfusionChance => _chatter?.ConfusionChance ?? 0;

    [ObservableProperty] private bool _initialized;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RecordingStatus))]
    private bool _hasRecording;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayRecordingCommand))]
    private bool _isPlaying;

    private bool CanUseRecording => _chatter is not null && HasRecording;
    private bool CanPlayRecording => CanUseRecording && IsAudioAvailable && !IsPlaying;
    public string RecordingStatus => LocalizedStrings.Instance[HasRecording
        ? "ChatterEditor_RecordingPresent" : "ChatterEditor_RecordingAbsent"];

    public ChatterEditorViewModel(SaveFile sav, IDialogService? dialogs = null, IAudioPlaybackService? audio = null)
    {
        _source = sav;
        _dialogs = dialogs;
        _audio = audio;
        if (GetChatter(sav) is null) return;

        var editedBeforeClone = sav.State.Edited;
        var working = sav.Clone();
        sav.State.Edited = editedBeforeClone;
        _chatter = GetChatter(working);
        LoadData();
    }

    partial void OnInitializedChanged(bool value)
    {
        if (_chatter is null) return;
        _chatter.Initialized = value;
        OnPropertyChanged(nameof(ConfusionChance));
        HasRecording = value || !IsRecordingEmpty();
    }

    partial void OnHasRecordingChanged(bool value)
    {
        PlayRecordingCommand.NotifyCanExecuteChanged();
        ExportPcmCommand.NotifyCanExecuteChanged();
        ExportWavCommand.NotifyCanExecuteChanged();
    }

    private void LoadData()
    {
        if (_chatter is null) return;
        Initialized = _chatter.Initialized;
        HasRecording = _chatter.Initialized || !IsRecordingEmpty();
        OnPropertyChanged(nameof(ConfusionChance));
    }

    private bool IsRecordingEmpty()
    {
        if (_chatter is null) return true;
        foreach (var value in _chatter.Recording)
            if (value != 0) return false;
        return true;
    }

    [RelayCommand]
    private void ClearRecording()
    {
        if (_chatter is null) return;
        _chatter.Recording.Clear();
        _chatter.Initialized = false;
        LoadData();
    }

    [RelayCommand]
    private void Refresh() => LoadData();

    [RelayCommand]
    private async Task ImportPcmAsync()
    {
        if (_chatter is null || _dialogs is null) return;
        var path = await _dialogs.OpenFileAsync(LocalizedStrings.Instance["ChatterEditor_ImportPcm"], ["*.pcm"]);
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            var bytes = await File.ReadAllBytesAsync(path);
            if (bytes.Length != IChatter.SIZE_PCM)
            {
                await _dialogs.ShowErrorAsync(LocalizedStrings.Instance["Common_Error"],
                    LocalizedStrings.Instance.Format("ChatterEditor_InvalidPcmSize", bytes.Length, IChatter.SIZE_PCM));
                return;
            }
            bytes.CopyTo(_chatter.Recording);
            _chatter.Initialized = true;
            LoadData();
            await _dialogs.ShowInformationAsync(LocalizedStrings.Instance["ChatterEditor_Title"],
                LocalizedStrings.Instance["ChatterEditor_Imported"]);
        }
        catch (Exception ex)
        {
            await _dialogs.ShowErrorAsync(LocalizedStrings.Instance["Common_Error"], ex.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseRecording))]
    private async Task ExportPcmAsync()
    {
        if (_chatter is null || _dialogs is null) return;
        await ExportAsync(LocalizedStrings.Instance["ChatterEditor_ExportPcm"], "Recording.pcm", ["*.pcm"],
            _chatter.Recording.ToArray());
    }

    [RelayCommand(CanExecute = nameof(CanUseRecording))]
    private async Task ExportWavAsync()
    {
        if (_chatter is null || _dialogs is null) return;
        await ExportAsync(LocalizedStrings.Instance["ChatterEditor_ExportWav"], "Recording.wav", ["*.wav"],
            ChatterAudioCodec.ToWav(_chatter.Recording));
    }

    private async Task ExportAsync(string title, string suggestedName, string[] filters, byte[] bytes)
    {
        var path = await _dialogs!.SaveFileAsync(title, suggestedName, filters);
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            await File.WriteAllBytesAsync(path, bytes);
            await _dialogs.ShowInformationAsync(LocalizedStrings.Instance["ChatterEditor_Title"],
                LocalizedStrings.Instance["ChatterEditor_Exported"]);
        }
        catch (Exception ex)
        {
            await _dialogs.ShowErrorAsync(LocalizedStrings.Instance["Common_Error"], ex.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(CanPlayRecording))]
    private async Task PlayRecordingAsync()
    {
        if (_chatter is null || _audio is null || _dialogs is null) return;
        IsPlaying = true;
        try
        {
            if (!await _audio.PlayWavAsync(ChatterAudioCodec.ToWav(_chatter.Recording)))
                await _dialogs.ShowErrorAsync(LocalizedStrings.Instance["Common_Error"],
                    LocalizedStrings.Instance["ChatterEditor_PlaybackFailed"]);
        }
        catch (Exception)
        {
            await _dialogs.ShowErrorAsync(LocalizedStrings.Instance["Common_Error"],
                LocalizedStrings.Instance["ChatterEditor_PlaybackFailed"]);
        }
        finally
        {
            IsPlaying = false;
        }
    }

    [RelayCommand]
    private void Save()
    {
        var target = GetChatter(_source);
        if (_chatter is not null && target is not null &&
            (target.Initialized != _chatter.Initialized || !target.Recording.SequenceEqual(_chatter.Recording)))
        {
            _chatter.Recording.CopyTo(target.Recording);
            target.Initialized = _chatter.Initialized;
            _source.State.Edited = true;
        }
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke();

    private static IChatter? GetChatter(SaveFile sav)
    {
        try
        {
            return sav switch
            {
                SAV4 sav4 => sav4.Chatter,
                SAV5 sav5 => sav5.Chatter,
                _ => null,
            };
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
