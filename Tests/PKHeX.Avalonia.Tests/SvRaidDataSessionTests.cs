using System.Buffers.Binary;
using PKHeX.Application.Services;
using PKHeX.Core;

namespace PKHeX.Avalonia.Tests;

public class SvRaidDataSessionTests
{
    public static SAV9SV CreateSave(int revision = 2, int? paldeaLength = null)
    {
        var allocated = new SAV9SV { Version = GameVersion.SL };
        var remove = revision switch
        {
            0 => new uint[] { 0x100B93DA, 0x66A33824, 0xA4BA4848 },
            1 => new uint[] { 0x66A33824 },
            _ => [],
        };
        var blocks = allocated.AllBlocks.Where(b => !remove.Contains(b.Key)).Select(b =>
        {
            var data = b.Data.ToArray();
            if (b.Key == 0xCAAC8800 && paldeaLength.HasValue) Array.Resize(ref data, paldeaLength.Value);
            // Public parser gives the synthetic fixture serializable object headers;
            // Core's default allocated blocks deliberately use None types.
            var xor = new SCXorShift32(b.Key);
            var encoded = new byte[data.Length == 0 ? 1 : data.Length + 5];
            encoded[0] = (byte)((byte)(data.Length == 0 ? SCTypeCode.Bool1 : SCTypeCode.Object) ^ xor.Next());
            if (data.Length != 0)
            {
                BinaryPrimitives.WriteInt32LittleEndian(encoded.AsSpan(1), data.Length ^ xor.Next32());
                for (var index = 0; index < data.Length; index++) encoded[index + 5] = (byte)(data[index] ^ xor.Next());
            }
            var offset = 0;
            return SCBlock.ReadFromOffset(encoded, b.Key, ref offset);
        }).ToArray();
        var save = new SAV9SV(SwishCrypto.Encrypt(blocks));
        save.State.Edited = false;
        return save;
    }
    private static Dictionary<uint, byte[]> Snapshot(SAV9SV save) => save.AllBlocks.ToDictionary(b => b.Key, b => b.Data.ToArray());
    private static void AssertUnchanged(SAV9SV save, Dictionary<uint, byte[]> expected)
    {
        foreach (var block in save.AllBlocks) Assert.Equal(expected[block.Key], block.Data.ToArray());
    }

    [Fact]
    public void Commit_StagesPaldeaFieldsAndPreservesUnrelatedLiveDataAndUnusedWords()
    {
        var save = CreateSave();
        var before = Snapshot(save);
        var session = new SvRaidDataSession(save);
        var row = session.WorkingSave.RaidPaldea.GetRaid(0);
        row.AreaID = 7; row.LotteryGroup = 8; row.SpawnPointID = 9;
        row.Seed = 0x12345678; row.Content = TeraRaidContentType.Might7;
        row.IsEnabled = true; row.IsClaimedLeaguePoints = true; row.Unused = 999;
        session.WorkingSave.RaidPaldea.CurrentSeed = 0x1234567890ABCDEF;
        AssertUnchanged(save, before);
        save.TID16 = 31234;
        save.RaidPaldea.TomorrowSeed = 0x8877665544332211;
        save.RaidPaldea.GetRaid(0).Unused = 0xCAFEBABE;
        var live = Snapshot(save);
        Assert.True(session.TryCommit(out var changed)); Assert.True(changed);
        var actual = save.RaidPaldea.GetRaid(0);
        Assert.Equal(7u, actual.AreaID); Assert.Equal(8u, actual.LotteryGroup);
        Assert.Equal(9u, actual.SpawnPointID); Assert.Equal(0x12345678u, actual.Seed);
        Assert.Equal(TeraRaidContentType.Might7, actual.Content);
        Assert.True(actual.IsEnabled); Assert.True(actual.IsClaimedLeaguePoints);
        Assert.Equal(0xCAFEBABEu, actual.Unused);
        Assert.Equal(0x1234567890ABCDEFUL, save.RaidPaldea.CurrentSeed);
        Assert.Equal(0x8877665544332211UL, save.RaidPaldea.TomorrowSeed);
        Assert.Equal((ushort)31234, save.TID16);
        foreach (var block in save.AllBlocks.Where(b => b.Key != 0xCAAC8800))
            Assert.Equal(live[block.Key], block.Data.ToArray());
    }

    [Theory]
    [InlineData(1)] [InlineData(2)]
    public void SharedDlcBlock_PreservesUnavailableAndUnusedBlueberryRecords(int revision)
    {
        var save = CreateSave(revision);
        Assert.Equal(revision, save.SaveRevision);
        var session = new SvRaidDataSession(save);
        session.WorkingSave.RaidKitakami.GetRaid(0).Seed = 0x12345678;
        session.WorkingSave.RaidBlueberry.GetRaid(0).Seed = 0x98765432;
        session.WorkingSave.RaidBlueberry.GetRaid(80).Seed = 0xFFFFFFFF;
        Assert.True(session.TryCommit(out var changed)); Assert.True(changed);
        Assert.Equal(0x12345678u, save.RaidKitakami.GetRaid(0).Seed);
        Assert.Equal(revision == 2 ? 0x98765432u : 0u, save.RaidBlueberry.GetRaid(0).Seed);
        Assert.Equal(0u, save.RaidBlueberry.GetRaid(80).Seed);
    }

    [Theory]
    [InlineData(0)] [InlineData(2)]
    public void SevenStarFlags_RoundTripLegacyAndSeparateStorage_PreservingPadding(int revision)
    {
        var save = CreateSave(revision);
        save.RaidSevenStar.Captured.Data[6] = 0xA7;
        if (revision == 2) save.RaidSevenStar.Defeated.Data[9] = 0xB8;
        var session = new SvRaidDataSession(save);
        var record = session.WorkingSave.RaidSevenStar.GetRaid(0);
        record.Identifier = 20260930; record.Captured = true; record.Defeated = true;
        if (revision == 2) save.RaidSevenStar.Defeated.Data[0] = 0xC9;
        Assert.True(session.TryCommit(out var changed)); Assert.True(changed);
        var actual = save.RaidSevenStar.GetRaid(0);
        Assert.Equal(20260930u, actual.Identifier); Assert.True(actual.Captured); Assert.True(actual.Defeated);
        Assert.Equal(0xA7, save.RaidSevenStar.Captured.Data[6]);
        if (revision == 2)
        {
            Assert.Equal(20260930u, save.RaidSevenStar.Defeated.GetRaid(0)!.Identifier);
            Assert.Equal(0xB8, save.RaidSevenStar.Defeated.Data[9]);
            Assert.Equal(0xC9, save.RaidSevenStar.Defeated.Data[0]);
        }
        var reloaded = new SAV9SV(save.Write());
        Assert.True(reloaded.RaidSevenStar.GetRaid(0).Captured);
        Assert.True(reloaded.RaidSevenStar.GetRaid(0).Defeated);
    }

    [Fact]
    public void CheckboxRevertAndNoOpCommit_PreserveNoncanonicalEncodingAndEditedState()
    {
        var save = CreateSave();
        BinaryPrimitives.WriteUInt32LittleEndian(save.RaidPaldea.Data[16..], 2);
        save.RaidSevenStar.Captured.Data[4] = 2;
        save.State.Edited = true;
        var before = Snapshot(save);
        var session = new SvRaidDataSession(save);
        session.WorkingSave.RaidPaldea.GetRaid(0).IsEnabled = false;
        session.WorkingSave.RaidPaldea.GetRaid(0).IsEnabled = true;
        session.WorkingSave.RaidSevenStar.GetRaid(0).Captured = true;
        session.WorkingSave.RaidSevenStar.GetRaid(0).Captured = false;
        Assert.True(session.TryCommit(out var changed)); Assert.False(changed);
        AssertUnchanged(save, before); Assert.True(save.State.Edited);
    }

    [Fact]
    public void MalformedStorage_FailsBeforeAnySourceWrite()
    {
        var save = CreateSave(paldeaLength: 16);
        var before = Snapshot(save);
        var session = new SvRaidDataSession(save);
        session.WorkingSave.RaidPaldea.CurrentSeed = 123;
        session.WorkingSave.RaidSevenStar.GetRaid(0).Captured = true;
        Assert.False(session.TryCommit(out var changed)); Assert.False(changed);
        AssertUnchanged(save, before); Assert.False(save.State.Edited);
    }

    [Fact]
    public void ExplicitIdentifierEdit_SynchronizesBothRecords_WhenOldIdentifiersDiffer()
    {
        var save = CreateSave();
        save.RaidSevenStar.Captured.GetRaid(0).Identifier = 1;
        save.RaidSevenStar.Defeated.GetRaid(0)!.Identifier = 2;
        var session = new SvRaidDataSession(save);
        session.WorkingSave.RaidSevenStar.GetRaid(0).Identifier = 2;
        save.RaidSevenStar.Defeated.GetRaid(0)!.Identifier = 4;
        Assert.True(session.TryCommit(out var changed)); Assert.True(changed);
        Assert.Equal(2u, save.RaidSevenStar.Captured.GetRaid(0).Identifier);
        Assert.Equal(2u, save.RaidSevenStar.Defeated.GetRaid(0)!.Identifier);
    }
}
