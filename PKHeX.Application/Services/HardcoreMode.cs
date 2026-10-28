using System;
using System.Collections.Generic;
using PKHeX.Application.Abstractions;
using PKHeX.Core;

namespace PKHeX.Application.Services;

/// <summary>
/// What an action does to a save, in Hardcore mode's terms. <see cref="Move"/>, <see cref="Release"/>,
/// <see cref="Export"/> and <see cref="Backup"/> never change a Pokémon's data; every other value edits
/// data or creates it.
/// </summary>
public enum SaveAction
{
    /// <summary>Changing an existing Pokémon: the editor, legalizing, rerolls.</summary>
    EditPokemon,

    /// <summary>Creating a Pokémon that did not exist: generators, Showdown/QR/file imports, encounters, gifts.</summary>
    CreatePokemon,

    /// <summary>Copying a Pokémon while the original stays: clone, paste, copy to another save.</summary>
    Duplicate,

    /// <summary>Editing many Pokémon at once: batch editor, box modifications.</summary>
    BatchEdit,

    /// <summary>Anything that is not a Pokémon: trainer, bag, Pokédex, records, events, world state, raw bytes.</summary>
    EditSaveData,

    /// <summary>Changing where a Pokémon lives: box moves, sorting, Bank deposits and withdrawals.</summary>
    Move,

    /// <summary>Releasing a Pokémon or clearing a slot.</summary>
    Release,

    /// <summary>Writing a copy out to a file; the save itself is untouched.</summary>
    Export,

    /// <summary>Restore points.</summary>
    Backup,
}

/// <summary>
/// Hardcore mode, for challenge runs and RetroAchievements-style play. While it is on, a save may be
/// browsed, organised, moved, released, exported and backed up, but nothing that edits, creates or
/// copies data may be written: a Pokémon can be moved, never copied. Off by default. Hardcore wins over
/// HaX: HaX only widens what the editors offer, and the editors are closed while Hardcore is on.
/// </summary>
/// <remarks>
/// Two layers enforce it. The UI asks <see cref="Allows"/> and disables what is refused. Behind that,
/// as <see cref="ISaveWriteGuard"/> this service checks every save write with
/// <see cref="HardcoreSaveVerifier"/> against the last saved state, so an edit that slipped past the
/// UI still never reaches the disk.
/// </remarks>
public sealed class HardcoreMode : ISaveWriteGuard
{
    private readonly AppSettings _settings;
    private readonly object _lock = new();
    private readonly List<PKM> _arrivals = [];
    private SaveFile? _live;
    private SaveFile? _baseline;

    public HardcoreMode(AppSettings settings) => _settings = settings;

    public bool IsEnabled => _settings.Hardcore.Enabled;

    /// <summary>Raised after <see cref="IsEnabled"/> changes.</summary>
    public event Action? Changed;

    /// <summary>True when the current setting allows <paramref name="action"/>.</summary>
    public bool Allows(SaveAction action) => !IsEnabled
        || action is SaveAction.Move or SaveAction.Release or SaveAction.Export or SaveAction.Backup;

    public bool Blocks(SaveAction action) => !Allows(action);

    /// <summary>
    /// Turns the mode on or off. Turning it on with a save open starts the check from that save's
    /// current state; changes made before then are not questioned.
    /// </summary>
    public void SetEnabled(bool enabled)
    {
        if (IsEnabled == enabled)
            return;

        _settings.Hardcore.Enabled = enabled;
        _settings.ApplySlotWriteOptions();
        lock (_lock)
        {
            _arrivals.Clear();
            _baseline = enabled ? _live?.Clone() : null;
        }
        Changed?.Invoke();
    }

    /// <summary>
    /// Records a Pokémon that was moved into <paramref name="save"/> from outside it (a Bank withdrawal),
    /// as it was written. Pass the slot as read back after the write.
    /// </summary>
    public void RecordArrival(SaveFile save, PKM written)
    {
        lock (_lock)
        {
            if (ReferenceEquals(save, _live) && _baseline is not null)
                _arrivals.Add(written.Clone());
        }
    }

    /// <summary>Checks the live save the way the next write will be checked.</summary>
    public SaveWriteRefusal? Verify(SaveFile live) => CheckWrite(live, live);

    public void OnSaveOpened(SaveFile? save)
    {
        lock (_lock)
        {
            _live = save;
            _arrivals.Clear();
            _baseline = IsEnabled ? save?.Clone() : null;
        }
    }

    public SaveWriteRefusal? CheckWrite(SaveFile live, SaveFile snapshot)
    {
        if (!IsEnabled)
            return null;

        SaveFile? baseline;
        PKM[] arrivals;
        lock (_lock)
        {
            baseline = ReferenceEquals(live, _live) ? _baseline : null;
            arrivals = [.. _arrivals];
        }

        // Fail closed: a save Hardcore has no baseline for cannot be shown to contain only moves.
        return baseline is null
            ? new SaveWriteRefusal(SaveWriteRefusalKind.CannotVerify)
            : HardcoreSaveVerifier.Verify(baseline, snapshot, arrivals);
    }

    public void OnSaveWritten(SaveFile live, SaveFile written)
    {
        lock (_lock)
        {
            if (!ReferenceEquals(live, _live) || !IsEnabled)
                return;
            _baseline = written.Clone();
            _arrivals.Clear();
        }
    }
}
