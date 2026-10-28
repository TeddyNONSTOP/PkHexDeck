using PKHeX.Core;

namespace PKHeX.Application.Abstractions;

/// <summary>
/// Policy consulted by <see cref="ISaveFileGateway"/> around every save-to-disk write. The gateway
/// reports the save lifecycle (opened, written, closed) and asks before each write; a refusal makes
/// <see cref="ISaveFileGateway.SaveFileAsync"/> return <see langword="false"/> without touching the disk.
/// </summary>
public interface ISaveWriteGuard
{
    /// <summary>A save became the current one (or <see langword="null"/> when the save was closed).</summary>
    void OnSaveOpened(SaveFile? save);

    /// <summary>
    /// Returns <see langword="null"/> when <paramref name="snapshot"/> may be written, otherwise the
    /// reason it may not. Called on the caller's thread with a detached copy of the live save.
    /// </summary>
    SaveWriteRefusal? CheckWrite(SaveFile live, SaveFile snapshot);

    /// <summary><paramref name="written"/> (a detached snapshot of <paramref name="live"/>) reached the disk.</summary>
    void OnSaveWritten(SaveFile live, SaveFile written);
}

/// <summary>Why a save write was refused.</summary>
/// <param name="Kind">What was found.</param>
/// <param name="Box">Box of the first offending Pokémon, or -1 for the party or when no slot applies.</param>
/// <param name="Slot">Box or party slot of the first offending Pokémon, or -1 when no slot applies.</param>
/// <param name="Species">Species of the first offending Pokémon, or 0 when no slot applies.</param>
public sealed record SaveWriteRefusal(SaveWriteRefusalKind Kind, int Box = -1, int Slot = -1, ushort Species = 0);

public enum SaveWriteRefusalKind
{
    /// <summary>A Pokémon in the save is not one that was there before (edited, created or copied).</summary>
    PokemonChanged,

    /// <summary>Data outside the boxes and party changed (trainer, bag, Pokédex, flags...).</summary>
    SaveDataChanged,

    /// <summary>The save could not be compared with its last saved state.</summary>
    CannotVerify,
}
