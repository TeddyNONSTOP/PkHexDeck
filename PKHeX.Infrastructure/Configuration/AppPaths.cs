using System;
using System.IO;
using PKHeX.Application.Abstractions;

namespace PKHeX.Infrastructure.Configuration;

/// <summary>
/// Default <see cref="IAppPaths"/> implementation backed by <see cref="AppPathResolver"/> and the
/// live OS/environment. Directories are computed lazily and are not created here — writers create
/// them on demand.
/// </summary>
public sealed class AppPaths : IAppPaths
{
    private const string AppName = PKHeX.Application.AppIdentity.Name;
    private const string ConfigFileName = "config.json";

    /// <summary>Where PKHeX-Avalonia kept its config; read once to bring settings across.</summary>
    public string LegacyAppConfigDirectory { get; }

    /// <summary>Where PKHeX-Avalonia kept its Bank, backups and other data; read once to bring them across.</summary>
    public string LegacyAppDataDirectory { get; }

    public string ConfigDirectory { get; }
    public string DataDirectory { get; }
    public string ConfigFilePath { get; }
    public string LegacyConfigFilePath { get; }

    public AppPaths()
    {
        var os = AppPathResolver.DetectOS();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Func<string, string?> getEnv = Environment.GetEnvironmentVariable;

        ConfigDirectory = AppPathResolver.ResolveConfigDirectory(os, home, getEnv, AppName);
        DataDirectory = AppPathResolver.ResolveDataDirectory(os, home, getEnv, AppName);
        ConfigFilePath = Path.Combine(ConfigDirectory, ConfigFileName);
        LegacyAppConfigDirectory = AppPathResolver.ResolveConfigDirectory(os, home, getEnv, PKHeX.Application.AppIdentity.LegacyFolderName);
        LegacyAppDataDirectory = AppPathResolver.ResolveDataDirectory(os, home, getEnv, PKHeX.Application.AppIdentity.LegacyFolderName);
        LegacyConfigFilePath = Path.Combine(AppContext.BaseDirectory, ConfigFileName);
    }
}
