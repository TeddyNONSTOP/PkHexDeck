using PKHeX.Core;

namespace PKHeX.Application.Services;

/// <summary>Stages BDSP trainer edits and rejects conflicting bytes before any source write.</summary>
public sealed class BdspTrainerDataSession
{
    private readonly SAV8BS _source;
    private byte[] _original;
    private SAV8BS _baseline;
    public SAV8BS Staged { get; private set; }
    public BdspTrainerDataSession(SAV8BS source)
    { _source = source; Staged = (SAV8BS)source.Clone(); _baseline = (SAV8BS)source.Clone(); _original = source.Data.ToArray(); }
    public void Reset() { Staged = (SAV8BS)_source.Clone(); _baseline = (SAV8BS)_source.Clone(); _original = _source.Data.ToArray(); }
    private static bool Compatible(ReadOnlySpan<byte> original, ReadOnlySpan<byte> desired, ReadOnlySpan<byte> current) =>
        desired.SequenceEqual(original) || current.SequenceEqual(original) || current.SequenceEqual(desired);
    public bool TryCommit(Action<SAV8BS> apply)
    {
        var before = (SAV8BS)Staged.Clone();
        try
        {
            apply(Staged);
            if (Staged.Data.Length != _original.Length || _source.Data.Length != _original.Length) { Staged = before; return false; }
            if (!Compatible(_baseline.MyStatus.Data, Staged.MyStatus.Data, _source.MyStatus.Data) ||
                !Compatible(_baseline.Config.Data, Staged.Config.Data, _source.Config.Data) ||
                !Compatible(_baseline.Played.Data, Staged.Played.Data, _source.Played.Data) ||
                !Compatible(_baseline.FlagWork.Data, Staged.FlagWork.Data, _source.FlagWork.Data) ||
                !Compatible(_baseline.System.Data, Staged.System.Data, _source.System.Data) ||
                !Compatible(_baseline.Records.Data, Staged.Records.Data, _source.Records.Data) ||
                !Compatible(_baseline.BattleTower.Data, Staged.BattleTower.Data, _source.BattleTower.Data) ||
                !Compatible(_baseline.RivalNameTrash, Staged.RivalNameTrash, _source.RivalNameTrash) ||
                Staged.ZoneID != _baseline.ZoneID && _source.ZoneID != _baseline.ZoneID && _source.ZoneID != Staged.ZoneID ||
                Staged.HasFirstSaveFileExpansion && !Compatible(_baseline.RecordAdd.Data, Staged.RecordAdd.Data, _source.RecordAdd.Data))
            { Staged = before; return false; }
            var writes = new List<(int Offset, byte Value)>();
            for (int i = 0; i < _original.Length; i++)
            {
                byte desired = Staged.Data[i], original = _original[i], current = _source.Data[i];
                if (desired == original || desired == current) continue;
                if (current != original) { Staged = before; return false; }
                writes.Add((i, desired));
            }
            foreach (var (offset, value) in writes) _source.Data[offset] = value;
            if (writes.Count != 0) _source.State.Edited = true;
            Reset(); return true;
        }
        catch { Staged = before; throw; }
    }
}
