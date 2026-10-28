using PKHeX.Core;

namespace PKHeX.Application.Services;

/// <summary>Stages ZA dex data and atomically commits only changed documented fields.</summary>
public sealed class Pokedex9aDataSession
{
    private readonly SAV9ZA _source;
    private byte[] _original;
    private readonly Stack<byte[]> _undo = new();
    public SAV9ZA Staged { get; private set; }
    public Zukan9a Dex => Staged.Zukan;
    public bool CanUndo => _undo.Count != 0;

    public Pokedex9aDataSession(SAV9ZA source)
    {
        _source = source;
        Staged = (SAV9ZA)source.Clone();
        _original = Dex.Data.ToArray();
    }

    public void Undo()
    {
        if (_undo.TryPop(out var data)) data.CopyTo(Dex.Data);
    }

    public void ApplyBulk(Dex9aBulkAction action, ushort? currentSpecies, bool shinyToo)
    {
        var before = Dex.Data.ToArray();
        try
        {
            int start = currentSpecies ?? 1, end = currentSpecies ?? Staged.MaxSpeciesID;
            for (int value = start; value <= end; value++)
            {
                ushort species = (ushort)value;
                if (species == 0 || species > Staged.MaxSpeciesID || !Staged.Personal.IsSpeciesInGame(species)) continue;
                var entry = Dex.GetEntry(species);
                uint mask = Pokedex9aCapabilities.GetFormMask(species);
                if (action == Dex9aBulkAction.Complete)
                {
                    Dex.SetDexEntryAll(species, shinyToo);
                    continue;
                }
                for (byte form = 0; form < 32; form++)
                {
                    if ((mask & (1u << form)) == 0) continue;
                    if (action is Dex9aBulkAction.SeenNone or Dex9aBulkAction.SeenAll)
                    {
                        bool seen = action == Dex9aBulkAction.SeenAll;
                        entry.SetIsFormSeen(form, seen);
                        if (shinyToo) entry.SetIsShinySeen(form, seen);
                    }
                    else
                    {
                        bool caught = action == Dex9aBulkAction.CaughtAll;
                        entry.SetIsFormCaught(form, caught);
                        if (shinyToo) entry.SetIsShinySeen(form, caught);
                    }
                }
            }
        }
        catch { before.CopyTo(Dex.Data); throw; }
        if (!before.AsSpan().SequenceEqual(Dex.Data)) _undo.Push(before);
    }

    public void Reset()
    {
        Staged = (SAV9ZA)_source.Clone();
        _original = Dex.Data.ToArray();
        _undo.Clear();
    }

    public bool TryCommit()
    {
        var desired = Dex.Data;
        var current = _source.Zukan.Data;
        if (current.Length != _original.Length || desired.Length != _original.Length) return false;
        var writes = new List<(int Offset, byte Value)>();
        for (int offset = 0; offset < desired.Length; offset++)
        {
            int field = offset % PokeDexEntry9a.SIZE;
            if (field is >= 0x12 and < 0x5A or > 0x5C) continue;
            byte original = _original[offset], target = desired[offset], live = current[offset];
            if (field is 0x0A or 0x11 or 0x5C)
            {
                // Core represents these as nonzero booleans. A semantic revert must
                // retain a noncanonical original/live byte rather than normalize it.
                if ((original != 0) == (target != 0) || (live != 0) == (target != 0)) continue;
                writes.Add((offset, target));
            }
            else if (field is 0x5A or 0x5B)
            {
                if (original == target || live == target) continue;
                if (live != original) return false;
                writes.Add((offset, target));
            }
            else
            {
                byte mask = field switch { 0x09 => 0x03, 0x0B or 0x10 => 0x07, _ => 0xFF };
                byte changed = (byte)((original ^ target) & mask);
                if (changed == 0) continue;
                byte merged = (byte)((live & ~changed) | (target & changed));
                if (merged != live) writes.Add((offset, merged));
            }
        }
        foreach (var (offset, value) in writes) current[offset] = value;
        if (writes.Count != 0) _source.State.Edited = true;
        Reset();
        return true;
    }
}
