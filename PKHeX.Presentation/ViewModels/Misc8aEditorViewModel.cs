using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Core;
using CommunityToolkit.Mvvm.Messaging;
using PKHeX.Presentation.Localization;

namespace PKHeX.Presentation.ViewModels;

/// <summary>
/// Misc editor for Legends Arceus (SAV8LA) saves.
/// </summary>
public partial class Misc8aEditorViewModel : ViewModelBase, IDisposable
{
    private SAV8LA _sav;
    private SAV8LA _baseline;
    private readonly TrainerScBlockDataSession<SAV8LA> _session;
    private bool _closed;
    [ObservableProperty] private string _error = string.Empty;
    public bool HasError => Error.Length != 0;

    public Misc8aEditorViewModel(SAV8LA sav)
    {
        _session = new(sav);
        _sav = _session.Staged;
        _baseline = (SAV8LA)_sav.Clone();
        LoadData();
        LoadTrainerFields();
        WeakReferenceMessenger.Default.Register<LanguageChangedMessage>(this, static (recipient, _) => ((Misc8aEditorViewModel)recipient).RefreshLanguage());
    }

    #region Currency

    [ObservableProperty] private uint _money;
    [ObservableProperty] private uint _meritCurrent;
    [ObservableProperty] private uint _meritEarned;

    private void LoadData()
    {
        Money = ReadProgress(_sav, SaveBlockAccessor8LA.KMoney);
        MeritCurrent = ReadProgress(_sav, SaveBlockAccessor8LA.KMeritCurrent);
        MeritEarned = ReadProgress(_sav, SaveBlockAccessor8LA.KMeritEarnedTotal);
        Rank = ReadProgress(_sav, SaveBlockAccessor8LA.KExpeditionTeamRank);
        SatchelUpgrades = ReadProgress(_sav, SaveBlockAccessor8LA.KSatchelUpgrades);
    }

    private void SaveData()
    {
        if (Money != ReadProgress(_baseline, SaveBlockAccessor8LA.KMoney)) _sav.Money = Money;
        foreach (var (key, value) in new[] { (SaveBlockAccessor8LA.KMeritCurrent, MeritCurrent), (SaveBlockAccessor8LA.KMeritEarnedTotal, MeritEarned), (SaveBlockAccessor8LA.KExpeditionTeamRank, Rank), (SaveBlockAccessor8LA.KSatchelUpgrades, SatchelUpgrades) })
            if (value != ReadProgress(_baseline, key)) _sav.Blocks.SetBlockValue(key, value);
    }

    [RelayCommand]
    private void MaxMoney()
    {
        Money = (uint)_sav.MaxMoney;
    }

    #endregion

    #region Expedition

    [ObservableProperty] private uint _rank;
    [ObservableProperty] private uint _satchelUpgrades;

    [RelayCommand]
    private void MaxRank()
    {
        Rank = (uint)(PokedexConstants8a.ResearchPointsForRank.Length - 1);
    }

    // Core supplies a UInt32 storage field, without a certified gameplay maximum.
    // Keep the full supported storage range, including the valid fixture value 40.
    public uint SatchelMaximum => _baseline.Blocks.GetBlock(SaveBlockAccessor8LA.KSatchelUpgrades).Type == SCTypeCode.UInt32
        ? uint.MaxValue : 0;
    private static uint ReadProgress(SAV8LA save, uint key) => save.Blocks.GetBlock(key).Type == SCTypeCode.UInt32
        ? (uint)save.Blocks.GetBlockValue(key) : 0;
    private bool ValidProgress(uint key, uint value) => value == ReadProgress(_baseline, key) || _baseline.Blocks.GetBlock(key).Type == SCTypeCode.UInt32;

    #endregion

    #region Commands

    [RelayCommand]
    private void Save()
    {
        if (!CanSave) { Error = Localization.LocalizedStrings.Instance["Trainer7_InvalidValues"]; return; }
        if (!_session.TryCommit(staged => { _sav = staged; SaveTrainerFields(); SaveData(); }))
        { _sav = _session.Staged; Error = Localization.LocalizedStrings.Instance["LgpeTrainer_Conflict"]; return; }
        _sav = _session.Staged; _baseline = (SAV8LA)_sav.Clone(); LoadData(); LoadTrainerFields(); Error = string.Empty;
    }

    [RelayCommand] private void Reset()
    {
        if (_closed) return;
        _session.Reset(); _sav = _session.Staged; _baseline = (SAV8LA)_sav.Clone(); LoadData(); LoadTrainerFields(); Error = string.Empty;
    }
    partial void OnErrorChanged(string value) => OnPropertyChanged(nameof(HasError));
    public void Dispose() { _closed = true; WeakReferenceMessenger.Default.UnregisterAll(this); }

    #endregion
}
