using CommunityToolkit.Mvvm.ComponentModel;
using PKHeX.Core;
using PKHeX.Presentation.Localization;

namespace PKHeX.Presentation.ViewModels;

public partial class FacilityViewModel : ViewModelBase
{
    private readonly FestaFacility _facility;
    private readonly Action _changed;
    private readonly bool _ultra;
    public int Index { get; }
    public string DisplayName => $"{LocalizedStrings.Instance["FestivalPlazaEditor_Facilities"]} {Index + 1}";
    public IReadOnlyList<ComboItem> TypeChoices { get; private set; }
    public IReadOnlyList<ComboItem> ColorChoices => PlazaCatalog.ColorChoices(Type, Color);
    public IReadOnlyList<ComboItem> NpcChoices => PlazaCatalog.NpcChoices(Npc);
    public IReadOnlyList<ComboItem> GenderChoices => PlazaCatalog.GenderChoices(Gender);
    public IReadOnlyList<PlazaNumberRow> Messages { get; }
    public IReadOnlyList<PlazaNumberRow> Usage { get; }
    public bool IsValid => FestivalIdError.Length == 0 && Messages.All(row => row.IsValid) && Usage.All(row => row.IsValid);
    public bool IsExchange => _ultra && Type is >= 125 and <= 127;
    public FacilityViewModel(int index, FestaFacility facility, bool ultra = false, Action? changed = null)
    {
        Index = index; _facility = facility; _changed = changed ?? (() => { }); _ultra = ultra;
        _type = facility.Type; _color = facility.Color; _ownerName = facility.OriginalTrainerName;
        _isIntroduced = facility.IsIntroduced; _npc = facility.NPC; _gender = facility.Gender;
        _festivalId = Convert.ToHexString(facility.TrainerFesID);
        TypeChoices = PlazaCatalog.TypeChoices(ultra, Type);
        Messages = Enumerable.Range(0, 4).Select(i => new PlazaNumberRow($"PlazaSession_Message{i}", facility.GetMessage(i),
            ushort.MaxValue, value => facility.SetMessage(i, (ushort)value), _changed)).ToArray();
        Usage = [
            new("PlazaSession_LuckyRank", facility.UsedLuckyRank, byte.MaxValue, value => facility.UsedLuckyRank = (int)value, _changed),
            new("PlazaSession_LuckyPlace", facility.UsedLuckyPlace, byte.MaxValue, value => facility.UsedLuckyPlace = (int)value, _changed),
            new("PlazaSession_UsedFlags", facility.UsedFlags, uint.MaxValue, value => facility.UsedFlags = (uint)value, _changed),
            new("PlazaSession_UsedState", facility.UsedRandStat, uint.MaxValue, value => facility.UsedRandStat = (uint)value, _changed),
            new("PlazaSession_ExchangeLeft", facility.ExchangeLeftCount, byte.MaxValue, value => facility.ExchangeLeftCount = (int)value, _changed),
        ];
    }
    [ObservableProperty] private int _type;
    partial void OnTypeChanged(int value)
    {
        if (value is < 0 or > byte.MaxValue) return;
        _facility.Type = value;
        OnPropertyChanged(nameof(ColorChoices)); OnPropertyChanged(nameof(IsExchange));
    }
    [ObservableProperty] private int _color;
    partial void OnColorChanged(int value) { if (value is >= 0 and <= byte.MaxValue) _facility.Color = value; }
    [ObservableProperty] private string _ownerName;
    partial void OnOwnerNameChanged(string value) => _facility.OriginalTrainerName = value;
    [ObservableProperty] private bool _isIntroduced;
    partial void OnIsIntroducedChanged(bool value) => _facility.IsIntroduced = value;
    [ObservableProperty] private int _npc;
    partial void OnNpcChanged(int value) { if (value >= 0) _facility.NPC = value; }
    [ObservableProperty] private int _gender;
    partial void OnGenderChanged(int value) { if (value is >= 0 and <= byte.MaxValue) _facility.Gender = (byte)value; }
    [ObservableProperty] private string _festivalId;
    [ObservableProperty] private string _festivalIdError = string.Empty;
    partial void OnFestivalIdChanged(string value)
    {
        if (value.Length == 24 && value.All(Uri.IsHexDigit))
        {
            Convert.FromHexString(value).CopyTo(_facility.TrainerFesID);
            FestivalIdError = string.Empty;
        }
        else FestivalIdError = LocalizedStrings.Instance["PlazaSession_IdError"];
        _changed();
    }
    public void RefreshLanguage()
    {
        TypeChoices = PlazaCatalog.TypeChoices(_ultra, Type);
        OnPropertyChanged(nameof(TypeChoices)); OnPropertyChanged(nameof(ColorChoices));
        OnPropertyChanged(nameof(NpcChoices)); OnPropertyChanged(nameof(GenderChoices)); OnPropertyChanged(nameof(DisplayName));
        if (FestivalIdError.Length != 0) FestivalIdError = LocalizedStrings.Instance["PlazaSession_IdError"];
        foreach (var row in Messages.Concat(Usage)) row.RefreshLanguage();
    }
    public void DeleteVisitor()
    {
        // Match upstream's deletion boundaries: retain the shop and its usage/exchange state.
        if (IsIntroduced) FestivalId = new string('0', 24);
        IsIntroduced = false; OwnerName = string.Empty; Gender = 0;
        foreach (var row in Messages) row.Value = 0;
    }
}
