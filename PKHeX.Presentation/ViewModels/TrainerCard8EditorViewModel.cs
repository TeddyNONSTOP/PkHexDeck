using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using PKHeX.Application.Abstractions;
using PKHeX.Application.Services;
using PKHeX.Core;
using PKHeX.Presentation.Localization;

namespace PKHeX.Presentation.ViewModels;

public partial class TrainerCard8EditorViewModel : ViewModelBase, ICloseableDialog, IDisposable
{
    private readonly TrainerCard8DataSession? _session;
    private readonly ISpriteRenderer? _sprites;
    private readonly IDialogService? _dialogs;
    private byte[] _original = [];
    private string _originalName = string.Empty, _originalNumber = string.Empty, _originalDate = string.Empty;
    private bool _loading, _closed;
    public Action? CloseRequested { get; set; }
    public bool IsSupported => _session is not null;
    private readonly int _nameLimit;
    public int TrainerNameMaxLength => Math.Max(_nameLimit, _originalName.Length);
    private TrainerCard8? Card => _session?.Staged.TrainerCard;

    public TrainerCard8EditorViewModel(SaveFile sav, ISpriteRenderer? sprites = null, IDialogService? dialogs = null)
    {
        _sprites = sprites; _dialogs = dialogs; _nameLimit = sav.MaxStringLengthTrainer;
        if (sav is SAV8SWSH swsh) { _session = new TrainerCard8DataSession(swsh); LoadData(); }
        WeakReferenceMessenger.Default.Register<LanguageChangedMessage>(this, static (recipient, _) => ((TrainerCard8EditorViewModel)recipient).RefreshLanguage());
    }

    [ObservableProperty] private string _trainerName = string.Empty;
    [ObservableProperty] private string _number = string.Empty;
    [ObservableProperty] private string _startedDateText = string.Empty;
    [ObservableProperty] private string _dateError = string.Empty;
    [ObservableProperty] private int _starter;
    [ObservableProperty] private bool _pokeDexComplete;
    [ObservableProperty] private bool _armorDexComplete;
    [ObservableProperty] private bool _crownDexComplete;
    [ObservableProperty] private ObservableCollection<Card8NumberField> _statistics = [];
    [ObservableProperty] private ObservableCollection<Card8NumberField> _metadata = [];
    [ObservableProperty] private ObservableCollection<Card8NumberField> _appearance = [];
    [ObservableProperty] private ObservableCollection<Card8TeamPokemon> _cardTeam = [];
    [ObservableProperty] private ObservableCollection<Card8TeamPokemon> _titleTeam = [];
    [ObservableProperty] private Card8TeamPokemon? _selectedCardPokemon;
    [ObservableProperty] private Card8TeamPokemon? _selectedTitlePokemon;
    [ObservableProperty] private IReadOnlyList<ComboItem> _starterOptions = [];
    public bool CanSave => !_closed && IsSupported && DateError.Length == 0 && NameError.Length == 0 && NumberError.Length == 0
        && Statistics.Concat(Metadata).Concat(Appearance).All(row => !row.HasError)
        && CardTeam.Concat(TitleTeam).All(row => !row.HasError);
    public string NameError => TrainerName != _originalName && TrainerName.Length > _nameLimit ? LocalizedStrings.Instance["Card8Flow_NameError"] : string.Empty;
    public string NumberError => Number != _originalNumber && (Number.Length > 3 || Number.Any(c => !char.IsAsciiDigit(c)))
        ? LocalizedStrings.Instance["Card8Flow_NumberTextError"] : string.Empty;
    public bool HasNameError => NameError.Length != 0;
    public bool HasNumberError => NumberError.Length != 0;
    public bool HasDateError => DateError.Length != 0;
    public bool HasInvalidStoredDate => Card is { } card && (card.StartedYear != 0 || card.StartedMonth != 0 || card.StartedDay != 0) && GetDate(card).Length == 0;

    private void LoadData()
    {
        if (Card is not { } card || _session is null) return;
        _loading = true; _original = card.Data.ToArray();
        TrainerName = _originalName = card.OT; Number = _originalNumber = card.Number.TrimEnd('\0');
        StartedDateText = _originalDate = GetDate(card); DateError = string.Empty; Starter = card.Starter;
        PokeDexComplete = card.PokeDexComplete; ArmorDexComplete = card.ArmorDexComplete; CrownDexComplete = card.CrownDexComplete;
        Statistics =
        [
            Field("PokedexOwned", 0x20, 2, card.PokeDexOwned, ushort.MaxValue, v => card.PokeDexOwned = (ushort)v, sentinel: ushort.MaxValue, validMax: ushort.MaxValue - 1),
            Field("ShinyFound", 0x22, 2, card.ShinyPokemonFound, ushort.MaxValue, v => card.ShinyPokemonFound = (ushort)v, sentinel: ushort.MaxValue, validMax: ushort.MaxValue - 1),
            Field("Caught", 0x2C, 4, card.CaughtPokemon, int.MaxValue, v => card.CaughtPokemon = (int)v, int.MinValue, -1, TrainerCard8.MaxPokemonCaught, 0),
            Field("Curry", 0x26, 2, card.CurryTypesOwned, ushort.MaxValue, v => card.CurryTypesOwned = (ushort)v, sentinel: ushort.MaxValue, validMax: 151),
            Field("Rally", 0x28, 4, card.RotoRallyScore, int.MaxValue, v => card.RotoRallyScore = (int)v, int.MinValue, null, TrainerCard8.RotoRallyScoreMax, 0),
        ];
        Metadata =
        [
            Field("Language", 0x1B, 1, card.Language, byte.MaxValue, v => card.Language = (byte)v),
            Field("TrainerId", 0x1C, 4, card.TrainerID, int.MaxValue, v => card.TrainerID = (int)v, int.MinValue),
            Field("Game", 0x24, 1, card.Game, byte.MaxValue, v => card.Game = (byte)v),
            Field("Gender", 0x38, 1, card.Gender, byte.MaxValue, v => card.Gender = (byte)v),
            Field("StartedYear", 0x170, 2, card.StartedYear, ushort.MaxValue, v => { card.StartedYear = (ushort)v; UpdateDateDisplay(); }),
            Field("StartedMonth", 0x172, 1, card.StartedMonth, byte.MaxValue, v => { card.StartedMonth = (byte)v; UpdateDateDisplay(); }),
            Field("StartedDay", 0x173, 1, card.StartedDay, byte.MaxValue, v => { card.StartedDay = (byte)v; UpdateDateDisplay(); }),
            Field("Printed", 0x1A8, 4, card.TimestampPrinted, uint.MaxValue, v => card.TimestampPrinted = (uint)v),
        ];
        Appearance =
        [
            Field("Skin", 0x40, 8, card.Skin, ulong.MaxValue, v => card.Skin = (ulong)v),
            Field("Hair", 0x48, 8, card.Hair, ulong.MaxValue, v => card.Hair = (ulong)v),
            Field("Brow", 0x50, 8, card.Brow, ulong.MaxValue, v => card.Brow = (ulong)v),
            Field("Lashes", 0x58, 8, card.Lashes, ulong.MaxValue, v => card.Lashes = (ulong)v),
            Field("Contacts", 0x60, 8, card.Contacts, ulong.MaxValue, v => card.Contacts = (ulong)v),
            Field("Lips", 0x68, 8, card.Lips, ulong.MaxValue, v => card.Lips = (ulong)v),
            Field("Glasses", 0x70, 8, card.Glasses, ulong.MaxValue, v => card.Glasses = (ulong)v),
            Field("Hat", 0x78, 8, card.Hat, ulong.MaxValue, v => card.Hat = (ulong)v),
            Field("Jacket", 0x80, 8, card.Jacket, ulong.MaxValue, v => card.Jacket = (ulong)v),
            Field("Top", 0x88, 8, card.Top, ulong.MaxValue, v => card.Top = (ulong)v),
            Field("Bag", 0x90, 8, card.Bag, ulong.MaxValue, v => card.Bag = (ulong)v),
            Field("Gloves", 0x98, 8, card.Gloves, ulong.MaxValue, v => card.Gloves = (ulong)v),
            Field("Bottom", 0xA0, 8, card.BottomOrDress, ulong.MaxValue, v => card.BottomOrDress = (ulong)v),
            Field("Socks", 0xA8, 8, card.Sock, ulong.MaxValue, v => card.Sock = (ulong)v),
            Field("Shoes", 0xB0, 8, card.Shoe, ulong.MaxValue, v => card.Shoe = (ulong)v),
            Field("MomSkin", 0xC0, 8, card.MomSkin, ulong.MaxValue, v => card.MomSkin = (ulong)v),
        ];
        LoadTeam(false); LoadTeam(true); RefreshStarterOptions(); _loading = false; ValidationChanged();
    }

    private Card8NumberField Field(string id, int offset, int length, decimal initial, decimal max,
        Action<decimal> set, decimal min = 0, decimal? sentinel = null, decimal? validMax = null, decimal? validMin = null) =>
        new(id, initial, min, max, sentinel, validMax, validMin, value =>
        {
            if (_closed || _loading || Card is null) return;
            if (value == initial) _original.AsSpan(offset, length).CopyTo(Card.Data.Slice(offset, length));
            else set(value);
            if (id is "StartedYear" or "StartedMonth" or "StartedDay") UpdateDateDisplay();
        }, ValidationChanged);

    private void LoadTeam(bool title)
    {
        if (_session is null) return;
        int index = (title ? SelectedTitlePokemon : SelectedCardPokemon)?.Index ?? 0;
        var rows = new ObservableCollection<Card8TeamPokemon>(Enumerable.Range(0, 6).Select(i => new Card8TeamPokemon(_session.Staged, i, title, _sprites, () => !_closed, ValidationChanged)));
        if (title) { TitleTeam = rows; SelectedTitlePokemon = rows[index]; }
        else { CardTeam = rows; SelectedCardPokemon = rows[index]; }
    }

    partial void OnTrainerNameChanged(string value)
    {
        if (!_loading && !_closed && Card is { } card && NameError.Length == 0)
        { if (value == _originalName) _original.AsSpan(0, 0x1A).CopyTo(card.Data); else card.OT = value; }
        ValidationChanged();
    }
    partial void OnNumberChanged(string value)
    {
        if (!_loading && !_closed && Card is { } card && NumberError.Length == 0)
        { if (value == _originalNumber) _original.AsSpan(0x39, 3).CopyTo(card.Data[0x39..]); else card.Number = value; }
        ValidationChanged();
    }
    partial void OnStarterChanged(int value)
    {
        if (!_loading && !_closed && Card is { } card && value is >= 0 and <= byte.MaxValue) card.Starter = (byte)value;
    }
    partial void OnPokeDexCompleteChanged(bool value) => SetFlag(0x30, value);
    partial void OnArmorDexCompleteChanged(bool value) => SetFlag(0x1B4, value);
    partial void OnCrownDexCompleteChanged(bool value) => SetFlag(0x1B5, value);
    private void SetFlag(int offset, bool value)
    {
        if (_loading || _closed || Card is null) return;
        Card.Data[offset] = value == (_original[offset] == 1) ? _original[offset] : value ? (byte)1 : (byte)0;
    }
    private static string GetDate(TrainerCard8 card)
    {
        try { return new DateOnly(card.StartedYear, card.StartedMonth, card.StartedDay).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); }
        catch (ArgumentOutOfRangeException) { return string.Empty; }
    }
    partial void OnStartedDateTextChanged(string value)
    {
        if (_loading || _closed || Card is not { } card) return;
        DateError = string.Empty;
        if (value == _originalDate) _original.AsSpan(0x170, 4).CopyTo(card.Data[0x170..]);
        else if (value.Length == 0) { card.StartedYear = 0; card.StartedMonth = 0; card.StartedDay = 0; }
        else if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        { card.StartedYear = (ushort)date.Year; card.StartedMonth = (byte)date.Month; card.StartedDay = (byte)date.Day; }
        else DateError = LocalizedStrings.Instance["Card8Flow_DateError"];
        foreach (var row in Metadata.Where(row => row.Id.StartsWith("Started", StringComparison.Ordinal)))
            row.RefreshValue(row.Id switch { "StartedYear" => card.StartedYear, "StartedMonth" => card.StartedMonth, _ => card.StartedDay });
        OnPropertyChanged(nameof(HasInvalidStoredDate)); ValidationChanged();
    }
    private void UpdateDateDisplay()
    {
        if (Card is not { } card) return;
        bool wasLoading = _loading; _loading = true;
        StartedDateText = GetDate(card); DateError = string.Empty;
        _loading = wasLoading;
        OnPropertyChanged(nameof(HasInvalidStoredDate));
    }
    [RelayCommand] private void ClearDate()
    {
        if (_closed || Card is not { } card) return;
        card.StartedYear = 0; card.StartedMonth = 0; card.StartedDay = 0; UpdateDateDisplay();
        foreach (var row in Metadata.Where(row => row.Id.StartsWith("Started", StringComparison.Ordinal))) row.RefreshValue(0);
        ValidationChanged();
    }
    [RelayCommand] private void SetPartyToCard() { if (_closed || _session is null) return; _session.CopyFromParty(false); LoadTeam(false); ValidationChanged(); }
    [RelayCommand] private void SetPartyToTitle() { if (_closed || _session is null) return; _session.CopyFromParty(true); LoadTeam(true); ValidationChanged(); }
    [RelayCommand] private void Refresh() { if (_closed || _session is null) return; _session.Reset(); LoadData(); }
    [RelayCommand(CanExecute = nameof(CanSave))] private async Task SaveAsync()
    {
        if (!CanSave || _session is null) return;
        try { _session.Commit(); }
        catch (InvalidOperationException)
        {
            if (_dialogs is not null) await _dialogs.ShowErrorAsync(LocalizedStrings.Instance["TrainerCard8EditorView_Title"], LocalizedStrings.Instance["Card8Flow_CommitError"]);
            return;
        }
        _closed = true; ValidationChanged(); CloseRequested?.Invoke(); Dispose();
    }
    [RelayCommand] private void Cancel() { if (_closed) return; _closed = true; ValidationChanged(); CloseRequested?.Invoke(); Dispose(); }
    public void Dispose() { _closed = true; WeakReferenceMessenger.Default.UnregisterAll(this); }
    private void ValidationChanged()
    { OnPropertyChanged(nameof(CanSave)); OnPropertyChanged(nameof(NameError)); OnPropertyChanged(nameof(NumberError)); OnPropertyChanged(nameof(HasNameError)); OnPropertyChanged(nameof(HasNumberError)); OnPropertyChanged(nameof(HasDateError)); SaveCommand.NotifyCanExecuteChanged(); }
    private void RefreshStarterOptions()
    {
        var names = GameInfo.Strings.Species;
        var options = new List<ComboItem> { new(names[810], 0), new(names[813], 1), new(names[816], 2) };
        if (Starter > 2) options.Add(new(Starter == byte.MaxValue ? LocalizedStrings.Instance["Card8Flow_NotSet"] : LocalizedStrings.Instance.Format("Card8Flow_UnknownValue", Starter), Starter));
        StarterOptions = options;
    }
    private void RefreshLanguage()
    {
        RefreshStarterOptions(); foreach (var row in Statistics.Concat(Metadata).Concat(Appearance)) row.RefreshLanguage();
        foreach (var row in CardTeam.Concat(TitleTeam)) row.RefreshLanguage();
        if (DateError.Length != 0) DateError = LocalizedStrings.Instance["Card8Flow_DateError"];
        ValidationChanged();
    }
}
