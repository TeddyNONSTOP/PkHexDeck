using PKHeX.Core;

namespace PKHeX.Application.Services;

/// <summary>Stages the full ZA donut pocket with record-level commit preflight and bulk undo.</summary>
public sealed class DonutDataSession
{
    private readonly SAV9ZA _source;
    private byte[] _original = [];
    private readonly Stack<(byte[] Data, bool Whole)> _undo = new();
    private bool _wholePocket;
    public SAV9ZA Staged { get; private set; }
    public DonutPocket9a Pocket => Staged.Donuts;
    public bool IsSupported => Pocket.Data.Length >= DonutPocket9a.MaxCount * Donut9a.Size;
    public bool CanUndo => _undo.Count != 0;
    public DonutDataSession(SAV9ZA source)
    { _source = source; Staged = (SAV9ZA)source.Clone(); _original = Pocket.Data.ToArray(); }
    public static bool IsOccupied(Donut9a donut) => donut.MillisecondsSince1970 != 0;
    public void Reset()
    { Staged = (SAV9ZA)_source.Clone(); _original = Pocket.Data.ToArray(); _undo.Clear(); _wholePocket = false; }
    public void ResetRecord(int index)
    {
        ValidateIndex(index);
        _original.AsSpan(index * Donut9a.Size, Donut9a.Size).CopyTo(Pocket.GetDonut(index).Data);
    }
    public bool ImportRecord(int index, ReadOnlySpan<byte> bytes)
    {
        ValidateIndex(index);
        if (bytes.Length != Donut9a.Size) return false;
        bytes.CopyTo(Pocket.GetDonut(index).Data); return true;
    }
    public byte[] ExportRecord(int index) { ValidateIndex(index); return Pocket.GetDonut(index).Data.ToArray(); }
    public void ApplyBulk(Action<DonutPocket9a> edit)
    {
        if (!IsSupported) throw new InvalidOperationException();
        var before = Pocket.Data.ToArray(); bool previousScope = _wholePocket;
        try { edit(Pocket); }
        catch { before.CopyTo(Pocket.Data); throw; }
        if (before.AsSpan().SequenceEqual(Pocket.Data)) return;
        _undo.Push((before, previousScope));
        _wholePocket = true;
    }
    public void Undo()
    {
        if (!_undo.TryPop(out var before)) return;
        before.Data.CopyTo(Pocket.Data); _wholePocket = before.Whole;
    }
    public void Compress()
    {
        ApplyBulk(pocket =>
        {
            // Core Donut9a.IsEmpty currently has its timestamp predicate inverted.
            int write = 0;
            for (int read = 0; read < DonutPocket9a.MaxCount; read++)
            {
                var record = pocket.GetDonut(read);
                if (!IsOccupied(record)) continue;
                if (write != read) record.CopyTo(pocket.GetDonut(write));
                write++;
            }
            for (int i = write; i < DonutPocket9a.MaxCount; i++) pocket.GetDonut(i).Clear();
        });
    }
    public bool TryCommit()
    {
        if (!IsSupported) return false;
        var desired = Pocket.Data;
        var current = _source.Donuts.Data;
        var dirty = new List<int>();
        for (int i = 0; i < DonutPocket9a.MaxCount; i++)
        {
            int offset = i * Donut9a.Size;
            var record = desired.Slice(offset, Donut9a.Size);
            if (!_wholePocket && record.SequenceEqual(_original.AsSpan(offset, Donut9a.Size))) continue;
            if (!current.Slice(offset, Donut9a.Size).SequenceEqual(_original.AsSpan(offset, Donut9a.Size))
                && !current.Slice(offset, Donut9a.Size).SequenceEqual(record)) return false;
            dirty.Add(offset);
        }
        foreach (int offset in dirty)
        {
            var record = desired.Slice(offset, Donut9a.Size);
            if (record.SequenceEqual(current.Slice(offset, Donut9a.Size))) continue;
            record.CopyTo(current.Slice(offset, Donut9a.Size)); _source.State.Edited = true;
        }
        Reset(); return true;
    }
    private void ValidateIndex(int index)
    {
        if (!IsSupported || (uint)index >= DonutPocket9a.MaxCount) throw new ArgumentOutOfRangeException(nameof(index));
    }
}
