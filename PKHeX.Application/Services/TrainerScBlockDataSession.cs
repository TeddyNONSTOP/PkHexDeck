using PKHeX.Core;

namespace PKHeX.Application.Services;

/// <summary>Stages SCBlock-backed trainer changes and preflights every dirty block before committing.</summary>
public sealed class TrainerScBlockDataSession<T> where T : SaveFile, ISCBlockArray
{
    private readonly T _source;
    private Dictionary<uint, (SCTypeCode Type, byte[] Data)> _original;
    private readonly Stack<T> _undo = new();
    public T Staged { get; private set; }
    public bool CanUndo => _undo.Count != 0;
    public TrainerScBlockDataSession(T source)
    { _source = source; Staged = (T)source.Clone(); _original = Snapshot(Staged); }
    private static Dictionary<uint, (SCTypeCode Type, byte[] Data)> Snapshot(T save) =>
        save.AllBlocks.ToDictionary(block => block.Key, block => (block.Type, block.Data.ToArray()));
    public void Reset()
    { Staged = (T)_source.Clone(); _original = Snapshot(Staged); _undo.Clear(); }
    public void ApplyAction(Action<T> action)
    {
        var before = (T)Staged.Clone();
        try { action(Staged); } catch { Staged = before; throw; }
        var original = before.AllBlocks.ToDictionary(block => block.Key);
        if (Staged.AllBlocks.Any(block => !original.TryGetValue(block.Key, out var old) || block.Type != old.Type ||
            !block.Data.SequenceEqual(old.Data))) _undo.Push(before);
    }
    public void Undo() { if (_undo.TryPop(out var before)) Staged = before; }
    public bool TryCommit(Action<T>? applyFields = null)
    {
        var before = (T)Staged.Clone();
        try
        {
            applyFields?.Invoke(Staged);
            var current = _source.AllBlocks.ToDictionary(block => block.Key);
            var writes = new List<(SCBlock Live, SCTypeCode Type, byte[] Data)>();
            if (Staged.AllBlocks.Count != _original.Count) { Staged = before; return false; }
            foreach (var block in Staged.AllBlocks)
            {
                if (!_original.TryGetValue(block.Key, out var original) ||
                    (block.Type != original.Type && !IsBooleanPair(block.Type, original.Type)) || block.Data.Length != original.Data.Length)
                { Staged = before; return false; }
                if (block.Type == original.Type && block.Data.SequenceEqual(original.Data)) continue;
                if (!current.TryGetValue(block.Key, out var live) ||
                    (live.Type != original.Type && !IsBooleanPair(live.Type, original.Type)) || live.Data.Length != original.Data.Length)
                { Staged = before; return false; }
                if (live.Type == block.Type && live.Data.SequenceEqual(block.Data)) continue;
                if (live.Type != original.Type || !live.Data.SequenceEqual(original.Data)) { Staged = before; return false; }
                writes.Add((live, block.Type, block.Data.ToArray()));
            }
            foreach (var (live, type, data) in writes)
            { if (live.Type != type) live.ChangeBooleanType(type); data.CopyTo(live.Data); }
            if (writes.Count != 0) _source.State.Edited = true;
            Reset(); return true;
        }
        catch { Staged = before; throw; }
    }
    private static bool IsBooleanPair(SCTypeCode a, SCTypeCode b) =>
        a is SCTypeCode.Bool1 or SCTypeCode.Bool2 && b is SCTypeCode.Bool1 or SCTypeCode.Bool2;
}
