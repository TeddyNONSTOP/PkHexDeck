using System.IO;
using PKHeX.Infrastructure.Configuration;

namespace PKHeX.Avalonia.Tests;

/// <summary>
/// Backups kept per save: 20 by default, a stored legacy default of 10 is upgraded once, and the count
/// is a Settings choice that later loads keep.
/// </summary>
public sealed class BackupRetentionSettingsTests
{
    [Fact]
    public void NewSettings_KeepTwentyBackups()
        => Assert.Equal(20, new AppSettings().SaveBackup.MaxBackupsPerSave);

    [Theory]
    [InlineData(null, 10, 20)]  // the old default, never offered in Settings
    [InlineData(null, 7, 7)]    // not the old default: kept
    [InlineData(1, 10, 10)]     // chosen in Settings after the upgrade: kept
    [InlineData(null, 0, 1)]
    [InlineData(1, 500, 100)]
    public void UpgradeDefaults_MovesOnlyTheLegacyDefault(int? version, int stored, int expected)
    {
        var backup = new AppSettings.SaveBackupSettings { DefaultsVersion = version, MaxBackupsPerSave = stored };
        backup.UpgradeDefaults();

        Assert.Equal(expected, backup.MaxBackupsPerSave);
        Assert.Equal(1, backup.DefaultsVersion);
        Assert.False(backup.UpgradeDefaults()); // applies once
    }

    [Fact]
    public void SettingsStore_UpgradesAnOldFileAndKeepsALaterChoice()
    {
        var paths = new FakeAppPaths();
        Directory.CreateDirectory(paths.ConfigDirectory);
        File.WriteAllText(paths.ConfigFilePath, """{ "SaveBackup": { "MaxBackupsPerSave": 10 } }""");
        var store = new SettingsStore(paths);

        var settings = store.Load();
        Assert.Equal(20, settings.SaveBackup.MaxBackupsPerSave);

        settings.SaveBackup.MaxBackupsPerSave = 10;
        store.Save(settings);
        Assert.Equal(10, store.Load().SaveBackup.MaxBackupsPerSave);
    }

    [Fact]
    public void SettingsView_SavesTheBackupCountWithinItsRange()
    {
        var settings = new AppSettings();
        var vm = new PKHeX.Presentation.ViewModels.SettingsViewModel(settings, new FakeSettingsStore(),
            new Moq.Mock<IThemeService>().Object, new Moq.Mock<IUiDensityService>().Object,
            new LanguageService(), UpdateTestDoubles.Coordinator());
        Assert.Equal(20, vm.MaxBackupsPerSave);

        vm.MaxBackupsPerSave = 35;
        vm.SaveCommand.Execute(null);
        Assert.Equal(35, settings.SaveBackup.MaxBackupsPerSave);

        vm.MaxBackupsPerSave = 0;
        vm.SaveCommand.Execute(null);
        Assert.Equal(1, settings.SaveBackup.MaxBackupsPerSave);
    }
}
