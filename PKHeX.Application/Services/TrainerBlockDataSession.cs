using PKHeX.Core;

namespace PKHeX.Application.Services;

/// <summary>Stages a BEEF-layout trainer workflow and atomically commits changed Core blocks.</summary>
public sealed class TrainerBlockDataSession<T> where T : SAV_BEEF
{
    private readonly T _source;
    private byte[] _original;
    private readonly Stack<T> _undo = new();
    public T Staged { get; private set; }
    public bool CanUndo => _undo.Count != 0;

    public TrainerBlockDataSession(T source)
    {
        _source = source;
        Staged = (T)source.Clone();
        _original = source.Data.ToArray();
    }

    public void Reset()
    {
        Staged = (T)_source.Clone();
        _original = _source.Data.ToArray();
        _undo.Clear();
    }

    public void ApplyAction(Action<T> action)
    {
        var before = (T)Staged.Clone();
        try { action(Staged); }
        catch { Staged = before; throw; }
        if (!Staged.Data.SequenceEqual(before.Data)) _undo.Push(before);
    }

    public void Undo()
    {
        if (_undo.TryPop(out var before)) Staged = before;
    }

    public bool TryCommit(Action<T>? applyFields = null)
    {
        var before = (T)Staged.Clone();
        try
        {
            applyFields?.Invoke(Staged);
            if (TryCommitBlocks()) { Reset(); return true; }
        }
        catch { Staged = before; throw; }
        Staged = before;
        return false;
    }

    private bool TryCommitBlocks()
    {
        if (_source.Data.Length != _original.Length || Staged.Data.Length != _original.Length) return false;
        var original = _original.AsSpan(); var desired = Staged.Data; var current = _source.Data;
        var writes = new List<(int Offset, byte[] Data)>();
        var covered = new bool[desired.Length];
        foreach (var block in _source.AllBlocks)
        {
            if (block.Offset < 0 || block.Length < 0 || block.Offset > desired.Length - block.Length) return false;
            int offset = block.Offset, length = block.Length;
            Array.Fill(covered, true, offset, length);
            var edited = desired.Slice(offset, length);
            if (edited.SequenceEqual(original.Slice(offset, length))) continue;
            var live = current.Slice(offset, length);
            if (live.SequenceEqual(edited)) continue;
            // A Core block is one conflict unit; do not splice independently
            // changed bytes of scalar values, names or GO Park records.
            if (!live.SequenceEqual(original.Slice(offset, length))) return false;
            writes.Add((offset, edited.ToArray()));
        }
        // An action must not silently edit unsupported footer/header storage.
        for (int offset = 0; offset < covered.Length; offset++)
            if (!covered[offset] && desired[offset] != original[offset]) return false;
        foreach (var (offset, data) in writes) data.CopyTo(current[offset..]);
        if (writes.Count != 0) _source.State.Edited = true;
        return true;
    }
}
