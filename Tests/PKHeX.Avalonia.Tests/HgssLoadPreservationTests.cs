using PKHeX.Core;

namespace PKHeX.Avalonia.Tests;

public class HgssLoadPreservationTests
{
    [Fact]
    public void LoadingExistingSavePreservesEveryInputByte()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../savefiles/gen4_heartgold.sav"));
        var data = File.ReadAllBytes(path);
        var before = data.ToArray();

        var save = new SAV4HGSS(data);

        Assert.Equal(before, data);
        Assert.Equal(before, save.Data.ToArray());
    }

    [Fact]
    public void BlankSaveInitializesVersionAtTheTrainerOffset()
    {
        var save = new SAV4HGSS();
        Assert.Equal(GameVersion.HGSS, save.Version);
    }
}
