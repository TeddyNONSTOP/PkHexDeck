using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PKHeX.Core;

namespace PKHeX.Presentation.ViewModels;

/// <summary>
/// Misc editor for Luminescent Platinum saves (ROM hack of BDSP).
/// Provides event unlocks for mythical Pokémon and other features.
/// </summary>
/// <remarks>
/// Deliberately takes the concrete <see cref="SAV8BSLuminescent"/> type, not <see cref="SAV8BS"/> - the trainer
/// battle commands below access <c>_clone.BattleTrainer</c>, which must resolve to the shadowed
/// <see cref="BattleTrainerStatus8bLumi"/> block (bit-packed for a far wider trainer roster), not the base
/// <see cref="BattleTrainerStatus8b"/> block that a <see cref="SAV8BS"/>-typed reference would resolve to instead.
/// <see cref="EventUnlocker8b"/> is safe to reuse unchanged here: it only ever reads/writes
/// <c>SAV.Zukan.HasNationalDex</c>, a single global flag whose offset and encoding is identical between
/// <see cref="Zukan8b"/> and <see cref="Zukan8bLumi"/> (only the per-species state/gender sub-regions differ) -
/// verified directly against both implementations.
/// </remarks>
public partial class Misc8bLumiEditorViewModel : ViewModelBase
{
    private readonly SAV8BSLuminescent _sav;
    private readonly SAV8BSLuminescent _clone;
    private readonly EventUnlocker8b _unlocker;

    [ObservableProperty]
    private bool _canUnlockSpiritomb;

    [ObservableProperty]
    private bool _canUnlockDarkrai;

    [ObservableProperty]
    private bool _canUnlockShaymin;

    [ObservableProperty]
    private bool _canUnlockArceus;

    [ObservableProperty]
    private bool _canUnlockBoxLegend;

    [ObservableProperty]
    private bool _canRespawnRoamer;

    [ObservableProperty]
    private bool _canRebattle;

    [ObservableProperty]
    private bool _canDefeatAll;

    [ObservableProperty]
    private bool _canUnlockZones = true;

    [ObservableProperty]
    private bool _canUnlockFashion = true;

    public Misc8bLumiEditorViewModel(SAV8BSLuminescent sav)
    {
        _sav = sav;
        _clone = (SAV8BSLuminescent)sav.Clone();
        _unlocker = new EventUnlocker8b(_clone);

        RefreshStates();
    }

    private void RefreshStates()
    {
        CanUnlockSpiritomb = _unlocker.UnlockReadySpiritomb;
        CanUnlockDarkrai = _unlocker.UnlockReadyDarkrai;
        CanUnlockShaymin = _unlocker.UnlockReadyShaymin;
        CanUnlockArceus = _unlocker.UnlockReadyArceus;
        CanUnlockBoxLegend = _unlocker.UnlockReadyBoxLegend;
        CanRespawnRoamer = _unlocker.ResetReadyRoamerMesprit || _unlocker.ResetReadyRoamerCresselia;
        CanRebattle = _clone.BattleTrainer.AnyDefeated;
        CanDefeatAll = _clone.BattleTrainer.AnyUndefeated;
    }

    [RelayCommand]
    private void UnlockSpiritomb()
    {
        _unlocker.UnlockSpiritomb();
        CanUnlockSpiritomb = _unlocker.UnlockReadySpiritomb;
    }

    [RelayCommand]
    private void UnlockDarkrai()
    {
        _unlocker.UnlockDarkrai();
        CanUnlockDarkrai = _unlocker.UnlockReadyDarkrai;
    }

    [RelayCommand]
    private void UnlockShaymin()
    {
        _unlocker.UnlockShaymin();
        CanUnlockShaymin = _unlocker.UnlockReadyShaymin;
    }

    [RelayCommand]
    private void UnlockArceus()
    {
        _unlocker.UnlockArceus();
        CanUnlockArceus = _unlocker.UnlockReadyArceus;
    }

    [RelayCommand]
    private void UnlockBoxLegend()
    {
        _unlocker.UnlockBoxLegend();
        CanUnlockBoxLegend = _unlocker.UnlockReadyBoxLegend;
    }

    [RelayCommand]
    private void RespawnRoamers()
    {
        _unlocker.RespawnRoamer();
        CanRespawnRoamer = _unlocker.ResetReadyRoamerMesprit || _unlocker.ResetReadyRoamerCresselia;
    }

    [RelayCommand]
    private void UnlockZones()
    {
        _unlocker.UnlockZones();
        CanUnlockZones = false;
    }

    [RelayCommand]
    private void UnlockFashion()
    {
        _unlocker.UnlockFashion();
        CanUnlockFashion = false;
    }

    [RelayCommand]
    private void DefeatAllTrainers()
    {
        _clone.BattleTrainer.DefeatAll();
        CanDefeatAll = false;
        CanRebattle = true;
    }

    [RelayCommand]
    private void RebattleAllTrainers()
    {
        _clone.BattleTrainer.RebattleAll();
        CanRebattle = false;
        CanDefeatAll = true;
    }

    [RelayCommand]
    private void Save()
    {
        _sav.CopyChangesFrom(_clone);
    }
}
