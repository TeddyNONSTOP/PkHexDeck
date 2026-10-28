using System.Buffers.Binary;
using PKHeX.Application.UseCases;
using PKHeX.Core;

namespace PKHeX.Avalonia.Tests;

public class ExportBattleVideo5Tests
{
    [Theory]
    [InlineData(false, 0)] [InlineData(false, 1)] [InlineData(false, 2)] [InlineData(false, 3)]
    [InlineData(true, 0)] [InlineData(true, 1)] [InlineData(true, 2)] [InlineData(true, 3)]
    public void DecryptedExport_RoundTripsEverySlot_WithoutMutatingSave(bool sequel, int slot)
    {
        SAV5 save = sequel ? new SAV5B2W2() : new SAV5BW();
        var bytes = new byte[BattleVideo5.SIZE];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(0xCC), LCRNG64.Mult);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(0xD4), LCRNG64.Add);
        var video = new BattleVideo5(bytes) { IsDecrypted = true, VideoName = "Test", EndSentinel = 0xE281 };
        video.RefreshChecksums(); var expected = bytes.ToArray(); video.Encrypt();
        Assert.False(BattleVideo5.GetIsDecrypted(bytes));
        bytes.CopyTo(save.GetBattleVideo(slot).Span); save.State.Edited = false;
        var before = save.Data.ToArray();
        var result = new ExportBattleVideo5UseCase().Execute(save, slot);
        Assert.Equal(expected, result); Assert.True(BattleVideo5.GetIsDecrypted(result));
        Assert.Equal(before, save.Data.ToArray()); Assert.False(save.State.Edited);
        result[0] ^= 0xFF; Assert.Equal(before, save.Data.ToArray());
    }
    [Theory]
    [InlineData(-1)] [InlineData(4)]
    public void InvalidSlot_IsRejected(int slot) => Assert.Throws<ArgumentOutOfRangeException>(() => new ExportBattleVideo5UseCase().Execute(new SAV5BW(), slot));
    [Fact]
    public void AlreadyDecryptedAndEmptyData_AreCopiedWithoutReprocessing()
    {
        var save = new SAV5BW(); var data = save.GetBattleVideo(0).Span;
        BinaryPrimitives.WriteUInt64LittleEndian(data[0xCC..], LCRNG64.Mult);
        BinaryPrimitives.WriteUInt64LittleEndian(data[0xD4..], LCRNG64.Add);
        var expected = data.ToArray();
        Assert.Equal(expected, new ExportBattleVideo5UseCase().Execute(save, 0));
        Assert.Equal(save.GetBattleVideo(1).ToArray(), new ExportBattleVideo5UseCase().Execute(save, 1));
    }
}
