using CommunityToolkit.Mvvm.ComponentModel;
using PKHeX.Core;
using PKHeX.Presentation.Localization;
using System.Collections.ObjectModel;

namespace PKHeX.Presentation.ViewModels;

public partial class PokedexGen9EditorViewModel
{
    public ObservableCollection<SvDexFormViewModel> FormStates { get; } = [];
    public ObservableCollection<SvDexDisplayViewModel> RegionalDisplays { get; } = [];
    private void LoadDlc(ushort species)
    {
        _baseline = null; FormStates.Clear(); RegionalDisplays.Clear();
        int generation = _generation;
        var names = FormConverter.GetFormList(species, GameInfo.Strings.Types, GameInfo.Strings.forms, GameInfo.GenderSymbolASCII, EntityContext.Gen9);
        int count = Math.Max(1, (int)_sav.Personal[species].FormCount);
        for (byte form = 0; form < Math.Min(count, 32); form++)
        {
            if (!_session.IsFormSupported(species, form)) continue;
            byte index = form; var state = _session.ReadForm(species, form);
            string name = form < names.Length && names[form].Length != 0 ? names[form] : LocalizedStrings.Instance["Dex9a_BaseForm"];
            FormStates.Add(new(index, name, state, row =>
            {
                if (_closed || generation != _generation) return;
                _session.WriteForm(species, index, row.Obtained, row.Seen, row.Heard, row.Viewed);
                IsCaught = _session.ReadCaught(species);
            }));
        }
        var ids = _session.Identifiers(species);
        foreach (int region in new[] { 1, 2, 3 })
        {
            ushort id = region == 1 ? ids.Paldea : region == 2 ? ids.Kitakami : ids.Blueberry;
            if (id == 0) continue;
            var state = _session.ReadDisplay(species, region);
            RegionalDisplays.Add(new(region, state, FormStates.Where(form => _session.IsRegionalFormSupported(species, form.Form, region)).Select(form => new ComboItem(form.Name, form.Form)), value =>
            {
                if (!_closed && generation == _generation && value.IsValid) _session.WriteDisplay(species, value.Region, (uint)value.Form, value.Gender, value.Shiny);
            }));
        }
        var entry = _zukan.DexKitakami.Get(species);
        IsSeenMale = entry.GetIsGenderSeen(0); IsSeenFemale = entry.GetIsGenderSeen(1); IsSeenGenderless = entry.GetIsGenderSeen(2);
        IsSeenShiny = entry.GetIsModelSeen(true);
        LangJPN = entry.GetLanguageFlag(1); LangENG = entry.GetLanguageFlag(2); LangFRE = entry.GetLanguageFlag(3);
        LangITA = entry.GetLanguageFlag(4); LangGER = entry.GetLanguageFlag(5); LangSPA = entry.GetLanguageFlag(7);
        LangKOR = entry.GetLanguageFlag(8); LangCHS = entry.GetLanguageFlag(9); LangCHT = entry.GetLanguageFlag(10);
        IsCaught = _session.ReadCaught(species);
        _baseline = CaptureLegacy();
    }
    private void FlushDlcShared()
    {
        if (_baseline is not { } old) return;
        var entry = _zukan.DexKitakami.Get(_loadedSpecies);
        if (IsSeenMale != old.Male) entry.SetIsGenderSeen(0, IsSeenMale);
        if (IsSeenFemale != old.Female) entry.SetIsGenderSeen(1, IsSeenFemale);
        if (IsSeenGenderless != old.Genderless) entry.SetIsGenderSeen(2, IsSeenGenderless);
        if (IsSeenShiny != old.SeenShiny) entry.SetIsModelSeen(true, IsSeenShiny);
        bool[] languages = [LangJPN, LangENG, LangFRE, LangITA, LangGER, LangSPA, LangKOR, LangCHS, LangCHT];
        int[] ids = [1, 2, 3, 4, 5, 7, 8, 9, 10];
        for (int i = 0; i < ids.Length; i++) if (languages[i] != old.Languages[i]) entry.SetLanguageFlag(ids[i], languages[i]);
        _baseline = CaptureLegacy();
    }
}

public partial class SvDexFormViewModel : ViewModelBase
{
    public byte Form { get; }
    [ObservableProperty] private string _name = string.Empty;
    private readonly Action<SvDexFormViewModel> _changed;
    [ObservableProperty] private bool _obtained;
    [ObservableProperty] private bool _seen;
    [ObservableProperty] private bool _heard;
    [ObservableProperty] private bool _viewed;
    public SvDexFormViewModel(byte form, string name, (bool Obtained, bool Seen, bool Heard, bool Viewed) state, Action<SvDexFormViewModel> changed)
    {
        Form = form; Name = name; _changed = changed;
        _obtained = state.Obtained; _seen = state.Seen; _heard = state.Heard; _viewed = state.Viewed;
    }
    partial void OnObtainedChanged(bool value) => _changed(this);
    partial void OnSeenChanged(bool value) => _changed(this);
    partial void OnHeardChanged(bool value) => _changed(this);
    partial void OnViewedChanged(bool value) => _changed(this);
}

public partial class SvDexDisplayViewModel : ViewModelBase
{
    public int Region { get; }
    public string Name => LocalizedStrings.Instance["SvDex_Region" + Region];
    [ObservableProperty] private IReadOnlyList<ComboItem> _formChoices = [];
    public IReadOnlyList<ComboItem> GenderChoices => PokedexGen9EditorViewModel.GenderOptions(Gender);
    private readonly Action<SvDexDisplayViewModel> _changed;
    private bool _refreshing;
    private readonly (uint Form, int Gender, bool Shiny) _original;
    public bool IsValid => (Form == _original.Form || FormChoices.Any(item => item.Value == Form) && Form is >= 0 and < 32) && (Gender == _original.Gender || Gender is >= 0 and <= 2);
    [ObservableProperty] private int _form;
    [ObservableProperty] private int _gender;
    [ObservableProperty] private bool _shiny;
    public SvDexDisplayViewModel(int region, (uint Form, int Gender, bool Shiny) state, IEnumerable<ComboItem> choices, Action<SvDexDisplayViewModel> changed)
    {
        Region = region; _changed = changed; _original = state; _form = (int)state.Form; _gender = state.Gender; _shiny = state.Shiny;
        var forms = choices.ToList(); if (forms.All(item => item.Value != Form)) forms.Add(new(LocalizedStrings.Instance.Format("RaidSession_UnknownType", state.Form), Form)); FormChoices = forms;
    }
    public void RefreshLabels(IEnumerable<ComboItem> choices)
    {
        int form = Form, gender = Gender;
        _refreshing = true;
        try
        {
            var values = choices.ToList(); if (values.All(item => item.Value != form)) values.Add(new(LocalizedStrings.Instance.Format("RaidSession_UnknownType", form), form));
            FormChoices = values; OnPropertyChanged(nameof(GenderChoices)); OnPropertyChanged(nameof(Name)); Form = form; Gender = gender;
        }
        finally { _refreshing = false; }
    }
    partial void OnFormChanged(int value) { if (!_refreshing) _changed(this); }
    partial void OnGenderChanged(int value) { if (!_refreshing) _changed(this); }
    partial void OnShinyChanged(bool value) { if (!_refreshing) _changed(this); }
}
