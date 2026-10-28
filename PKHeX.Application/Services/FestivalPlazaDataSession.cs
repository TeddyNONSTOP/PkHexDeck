using PKHeX.Core;

namespace PKHeX.Application.Services;

/// <summary>One staged Festival Plaza session, including its separate Used FC record.</summary>
public sealed class FestivalPlazaDataSession
{
    private readonly SAV7 _source;
    private byte[] _original = [];
    private int _originalCurrent;
    private int _originalUsed;
    public SAV7 WorkingSave { get; private set; } = null!;
    public FestivalPlazaDataSession(SAV7 source) { _source = source; Reset(); }
    public void Reset()
    {
        var edited = _source.State.Edited;
        WorkingSave = (SAV7)_source.Clone();
        _source.State.Edited = edited;
        _original = WorkingSave.Festa.Data.ToArray();
        _originalCurrent = WorkingSave.Festa.FestaCoins;
        _originalUsed = WorkingSave.GetRecord(38);
    }
    public bool TryCommit(out bool changed)
    {
        changed = false;
        var current = WorkingSave.Festa.FestaCoins;
        var used = WorkingSave.GetRecord(38);
        if (_source.Festa.Data.Length != _original.Length || WorkingSave.Festa.Data.Length != _original.Length
            || current is < 0 or > 9999999 || used < 0 || used > _source.GetRecordMax(38)) return false;
        var target = _source.Festa.Data;
        var staged = WorkingSave.Festa.Data;
        // A timestamp edit expresses one date/time. Copy all six components together while
        // preserving the unused word at 0x2FC and independently edited dates on a no-op session.
        var dateFields = new[] { 0x2F0, 0x2F4, 0x2F8, 0x300, 0x304, 0x308 };
        var dateChanged = false;
        foreach (var offset in dateFields)
            dateChanged |= !staged.Slice(offset, 4).SequenceEqual(_original.AsSpan(offset, 4));
        if (dateChanged)
            foreach (var offset in dateFields)
            {
                if (staged.Slice(offset, 4).SequenceEqual(target.Slice(offset, 4))) continue;
                staged.Slice(offset, 4).CopyTo(target.Slice(offset, 4)); changed = true;
            }
        // Commit whole domain fields, avoiding mixed multi-byte values if another editor wrote
        // the same field while this dialog was open. Unknown/padding bytes are never copied.
        foreach (var (offset, length) in GetFields())
        {
            var value = staged.Slice(offset, length);
            var original = _original.AsSpan(offset, length);
            if (value.SequenceEqual(original)) continue;
            var equivalentBoolean = length == 1 && (offset is >= 0x2A50 and <= 0x2ABA
                || offset is >= 0x310 and < 0x508 && (offset - 0x310) % FestaFacility.SIZE == 2);
            if (equivalentBoolean && (value[0] != 0) == (original[0] != 0)) continue;
            var name = offset == 0x510 || offset is >= 0x310 and < 0x508 && (offset - 0x310) % FestaFacility.SIZE == 4;
            if (name && StringConverter7.GetString(value) == StringConverter7.GetString(original)) continue;
            if (target.Slice(offset, length).SequenceEqual(value)) continue;
            value.CopyTo(target.Slice(offset, length)); changed = true;
        }
        var currentChanged = current != _originalCurrent;
        var usedChanged = used != _originalUsed;
        if (usedChanged && used != _source.GetRecord(38)) { _source.SetRecord(38, used); changed = true; }
        if (currentChanged && current != _source.Festa.FestaCoins) { _source.Festa.FestaCoins = current; changed = true; }
        if (currentChanged || usedChanged)
        {
            // Use the live, unedited counterpart when another tool changed currency independently.
            var total = (int)Math.Min(9999999L, (long)_source.Festa.FestaCoins + _source.GetRecord(38));
            if (_source.Festa.TotalFestaCoins != total) { _source.Festa.TotalFestaCoins = total; changed = true; }
        }
        if (changed) _source.State.Edited = true;
        return true;
    }
    private static IEnumerable<(int Offset, int Length)> GetFields()
    {
        for (var i = 0; i < 4; i++) yield return (i * 2, 2);
        for (var i = 0; i < JoinFesta7.FestaFacilityCount; i++)
        {
            var start = 0x310 + i * FestaFacility.SIZE;
            foreach (var offset in new[] { 0, 1, 2, 3, 0x26, 0x27, 0x40 }) yield return (start + offset, 1);
            yield return (start + 4, 0x1A);
            for (var j = 0; j < 4; j++) yield return (start + 0x1E + j * 2, 2);
            foreach (var offset in new[] { 0x28, 0x2C, 0x30 }) yield return (start + offset, 4);
            yield return (start + 0x34, 12);
        }
        yield return (0x510, 0x2A);
        yield return (0x53A, 2);
        for (var i = 0; i < 11; i++) yield return (0x53C + i, 1);
        for (var i = 0; i < 107; i++) yield return (0x2A50 + i, 1);
    }

}
