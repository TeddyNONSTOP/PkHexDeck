using System.Buffers.Binary;
using PKHeX.Core;

namespace PKHeX.Application.Services;

/// <summary>
/// Stages SV raid and seven-star data together. Commit copies only edited domain fields,
/// including both regions of the shared DLC block, and preserves untouched source bytes.
/// </summary>
public sealed class SvRaidDataSession
{
    private const uint PaldeaKey = 0xCAAC8800;
    private const uint DlcKey = 0x100B93DA;
    private const uint CapturedKey = 0x8B14392F;
    private const uint DefeatedKey = 0xA4BA4848;
    private const int DlcRegionSize = 0xC80;
    private readonly SAV9SV _source;
    private Dictionary<uint, byte[]> _original = [];
    public SAV9SV WorkingSave { get; private set; } = null!;

    public SvRaidDataSession(SAV9SV source)
    {
        _source = source;
        Reset();
    }

    public void Reset()
    {
        var edited = _source.State.Edited;
        WorkingSave = (SAV9SV)_source.Clone();
        _source.State.Edited = edited;
        _original = WorkingSave.AllBlocks.Where(b => IsRaidKey(b.Key)).ToDictionary(b => b.Key, b => b.Data.ToArray());
    }

    private static bool IsRaidKey(uint key) => key is PaldeaKey or DlcKey or CapturedKey or DefeatedKey;

    /// <summary>Preflights every block before any write; false leaves the source untouched.</summary>
    public bool TryCommit(out bool changed)
    {
        changed = false;
        var source = _source.AllBlocks.Where(b => IsRaidKey(b.Key)).ToDictionary(b => b.Key);
        var staged = WorkingSave.AllBlocks.Where(b => IsRaidKey(b.Key)).ToDictionary(b => b.Key);
        foreach (var (key, before) in _original)
        {
            if (!source.TryGetValue(key, out var target) || !staged.TryGetValue(key, out var working)
                || target.Data.Length != before.Length || working.Data.Length != before.Length)
                return false;
        }
        if (staged.TryGetValue(PaldeaKey, out var paldeaData)
            && paldeaData.Data.Length < 16 + RaidSpawnList9.RaidCountLegal_T0 * TeraRaidDetail.SIZE)
            return false;
        if (staged.TryGetValue(DlcKey, out var dlcData) && WorkingSave.SaveRevision >= 1
            && dlcData.Data.Length < DlcRegionSize * 2)
            return false;
        if (staged.TryGetValue(CapturedKey, out var captureData)
            && staged.TryGetValue(DefeatedKey, out var defeatData) && defeatData.Type != SCTypeCode.None
            && defeatData.Data.Length < 4 + (captureData.Data.Length / SevenStarRaidCapturedDetail.SIZE) * SevenStarRaidDefeatedDetail.SIZE)
            return false;
        if (staged.ContainsKey(PaldeaKey))
        {
            CopyField(PaldeaKey, 0, 8, ref changed);
            CopyField(PaldeaKey, 8, 8, ref changed);
            CopyRegion(PaldeaKey, 16, RaidSpawnList9.RaidCountLegal_T0, ref changed);
        }
        if (staged.ContainsKey(DlcKey) && WorkingSave.SaveRevision >= 1)
            CopyRegion(DlcKey, 0, RaidSpawnList9.RaidCountLegal_T1, ref changed);
        if (staged.ContainsKey(DlcKey) && WorkingSave.SaveRevision >= 2)
            CopyRegion(DlcKey, DlcRegionSize, RaidSpawnList9.RaidCountLegal_T2, ref changed);

        if (staged.TryGetValue(CapturedKey, out var capture))
        {
            var separateDefeat = staged.TryGetValue(DefeatedKey, out var defeat) && defeat.Type != SCTypeCode.None;
            for (var index = 0; index < capture.Data.Length / SevenStarRaidCapturedDetail.SIZE; index++)
            {
                var offset = index * SevenStarRaidCapturedDetail.SIZE;
                if (!_original[CapturedKey].AsSpan(offset, 4).SequenceEqual(capture.Data.Slice(offset, 4)))
                {
                    CopyField(CapturedKey, offset, 4, ref changed);
                    // Identifier is one logical field shared by both records. An explicit edit
                    // synchronizes the pair, even if the new ID matched the old defeat ID.
                    if (separateDefeat) WriteField(DefeatedKey, offset + 4, 4, ref changed);
                }
                CopyBoolean(CapturedKey, offset + 4, 1, true, ref changed);
                if (separateDefeat) CopyBoolean(DefeatedKey, offset + 8, 1, true, ref changed);
                else CopyBoolean(CapturedKey, offset + 5, 1, true, ref changed);
            }
        }
        if (changed) _source.State.Edited = true;
        return true;

        void CopyRegion(uint key, int start, int count, ref bool anyChanged)
        {
            for (var index = 0; index < count; index++)
            {
                var offset = start + index * TeraRaidDetail.SIZE;
                CopyBoolean(key, offset, 4, false, ref anyChanged);
                foreach (var fieldOffset in new[] { 4, 8, 12, 16, 24 })
                    CopyField(key, offset + fieldOffset, 4, ref anyChanged);
                // +0x14 is unused; preserve it even if another live editor changes it.
                CopyBoolean(key, offset + 28, 4, false, ref anyChanged);
            }
        }
        void CopyField(uint key, int offset, int length, ref bool anyChanged)
        {
            var value = staged[key].Data.Slice(offset, length);
            if (value.SequenceEqual(_original[key].AsSpan(offset, length))) return;
            WriteField(key, offset, length, ref anyChanged);
        }
        void WriteField(uint key, int offset, int length, ref bool anyChanged)
        {
            var value = staged[key].Data.Slice(offset, length);
            var target = source[key].Data.Slice(offset, length);
            if (value.SequenceEqual(target)) return;
            value.CopyTo(target);
            anyChanged = true;
        }
        void CopyBoolean(uint key, int offset, int length, bool exactOne, ref bool anyChanged)
        {
            var original = _original[key].AsSpan(offset, length);
            var value = staged[key].Data.Slice(offset, length);
            var before = length == 1 ? original[0] : BinaryPrimitives.ReadUInt32LittleEndian(original);
            var after = length == 1 ? value[0] : BinaryPrimitives.ReadUInt32LittleEndian(value);
            // Reverting a checkbox preserves its original noncanonical encoding.
            if ((exactOne ? before == 1 : before != 0) == (exactOne ? after == 1 : after != 0)) return;
            var target = source[key].Data.Slice(offset, length);
            if (target.SequenceEqual(value)) return;
            value.CopyTo(target);
            anyChanged = true;
        }
    }
}
