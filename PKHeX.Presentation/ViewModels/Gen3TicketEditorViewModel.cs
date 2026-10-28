using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using PKHeX.Core;
using PKHeX.Presentation.Localization;

namespace PKHeX.Presentation.ViewModels;

public partial class Gen3TicketEditorViewModel : ViewModelBase, ICloseableDialog, IDisposable
{
    private readonly Gen3TicketDataSession _session;
    private readonly IDialogService _dialogs;
    private bool _closed;
    public Action? CloseRequested { get; set; }
    public IReadOnlyList<Gen3TicketRow> Tickets { get; }
    public int MaxFlagIndex => _session.FlagCount - 1;
    public bool CanApply => !_closed;
    public bool HasError => Error.Length != 0;
    partial void OnErrorChanged(string value) => OnPropertyChanged(nameof(HasError));
    [ObservableProperty] private string _error = string.Empty;
    [ObservableProperty] private int _selectedFlagIndex;
    public bool SelectedFlagValue
    {
        get => _session.GetFlag(SelectedFlagIndex);
        set { if (_closed) return; _session.SetFlag(SelectedFlagIndex, value); Refresh(); }
    }
    public Gen3TicketEditorViewModel(SAV3 save, IDialogService dialogs)
    {
        _session = new Gen3TicketDataSession(save); _dialogs = dialogs;
        Tickets = _session.Definitions.Select(definition => new Gen3TicketRow(_session, definition, () => !_closed, Refresh, FailBag)).ToArray();
        WeakReferenceMessenger.Default.Register<LanguageChangedMessage>(this, static (recipient, _) => ((Gen3TicketEditorViewModel)recipient).Refresh());
    }
    partial void OnSelectedFlagIndexChanged(int value)
    {
        if (value < 0 || value > MaxFlagIndex) { SelectedFlagIndex = Math.Clamp(value, 0, MaxFlagIndex); return; }
        OnPropertyChanged(nameof(SelectedFlagValue));
    }
    private void FailBag() { Error = LocalizedStrings.Instance["Ticket3Flow_BagFull"]; RefreshRows(); }
    private void RefreshRows() { foreach (var row in Tickets) row.Refresh(); OnPropertyChanged(nameof(SelectedFlagValue)); }
    private void Refresh() { Error = string.Empty; RefreshRows(); }
    [RelayCommand] private void Reset() { if (_closed) return; _session.Reset(); Refresh(); }
    [RelayCommand(CanExecute = nameof(CanApply))] private async Task ApplyAsync()
    {
        if (_closed) return;
        var loc = LocalizedStrings.Instance;
        if (!await _dialogs.ShowConfirmationAsync(loc["Ticket3Flow_Title"], loc["Ticket3Flow_ApplyBackup"], loc["MysteryGiftEditor_Apply"], loc["Common_Cancel"])) return;
        if (!_session.TryCommit(out _))
        { Error = loc["Ticket3Flow_Conflict"]; return; }
        _closed = true; ApplyCommand.NotifyCanExecuteChanged(); CloseRequested?.Invoke(); Dispose();
    }
    [RelayCommand] private void Cancel() { if (_closed) return; _closed = true; ApplyCommand.NotifyCanExecuteChanged(); CloseRequested?.Invoke(); Dispose(); }
    public void Dispose() { _closed = true; WeakReferenceMessenger.Default.UnregisterAll(this); }
}

public partial class Gen3TicketRow : ViewModelBase
{
    private readonly Gen3TicketDataSession _session;
    private readonly Gen3TicketDefinition _definition;
    private readonly Func<bool> _canEdit;
    private readonly Action _changed, _fail;
    public string Id => _definition.Id;
    public string Name => GameInfo.Strings.GetItemStrings(_session.Staged.Context, _session.Staged.Version)[_definition.ItemId];
    public string Destination => LocalizedStrings.Instance["Ticket3Flow_Destination_" + Id];
    public bool HasShownFlag => _definition.ShownFlag.HasValue;
    public bool HasReceivedFlag => _definition.ReceivedFlag.HasValue;
    public bool EncounterCompleted => _definition.CompletedFlag is { } flag && _session.GetFlag(flag);
    public string FlagDetails => LocalizedStrings.Instance.Format("Ticket3Flow_FlagDetails", $"0x{_definition.TravelFlag:X3}",
        _definition.ShownFlag is { } shown ? $"0x{shown:X3}" : LocalizedStrings.Instance["Ticket3Flow_Unavailable"],
        _definition.ReceivedFlag is { } received ? $"0x{received:X3}" : LocalizedStrings.Instance["Ticket3Flow_Unavailable"]);
    public bool HasTicket
    {
        get => _session.HasTicket(_definition);
        set { if (!_canEdit()) return; if (!_session.SetTicket(_definition, value)) _fail(); else _changed(); }
    }
    public bool RouteEnabled
    {
        get => _session.GetFlag(_definition.TravelFlag);
        set { if (!_canEdit()) return; _session.SetFlag(_definition.TravelFlag, value); _changed(); }
    }
    public bool Shown
    {
        get => _definition.ShownFlag is { } flag && _session.GetFlag(flag);
        set { if (!_canEdit() || _definition.ShownFlag is not { } flag) return; _session.SetFlag(flag, value); _changed(); }
    }
    public bool Received
    {
        get => _definition.ReceivedFlag is { } flag && _session.GetFlag(flag);
        set { if (!_canEdit() || _definition.ReceivedFlag is not { } flag) return; _session.SetFlag(flag, value); _changed(); }
    }
    public Gen3TicketRow(Gen3TicketDataSession session, Gen3TicketDefinition definition, Func<bool> canEdit, Action changed, Action fail)
    { _session = session; _definition = definition; _canEdit = canEdit; _changed = changed; _fail = fail; }
    [RelayCommand] private void StageTicketAndRoute()
    {
        if (!_canEdit()) return;
        if (!_session.StageTicketAndRoute(_definition)) _fail(); else _changed();
    }
    public void Refresh()
    {
        OnPropertyChanged(nameof(Name)); OnPropertyChanged(nameof(Destination)); OnPropertyChanged(nameof(FlagDetails));
        OnPropertyChanged(nameof(HasTicket)); OnPropertyChanged(nameof(RouteEnabled)); OnPropertyChanged(nameof(Shown));
        OnPropertyChanged(nameof(Received)); OnPropertyChanged(nameof(EncounterCompleted));
    }
}
