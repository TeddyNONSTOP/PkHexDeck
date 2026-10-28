using System.Globalization;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using PKHeX.Avalonia.Tests.Harness;
using PKHeX.Core;
using PKHeX.Infrastructure.Configuration;
using PKHeX.Presentation.Localization;
using PKHeX.Presentation.ViewModels;

namespace PKHeX.Avalonia.Tests;

public sealed class LanguageLifecycleTests
{
    // Discover the shipped selector options: adding a UI-only language enables these same
    // lifecycle checks without maintaining another independent list of supported languages.
    public static IEnumerable<object[]> Languages() => new LanguageService().AvailableLanguages
        .Select(option => new object[] { option.Code });

    public static IEnumerable<object[]> RoundTrips() =>
        from source in new[] { "de", "ja" }
        from option in new LanguageService().AvailableLanguages
        where option.Code != source
        select new object[] { source, option.Code };

    [AvaloniaTheory]
    [MemberData(nameof(Languages))]
    public void PersistedLanguageInitializesCoreAndShellBeforeTheFirstWindow(string language)
    {
        using var state = new LocalizationState();
        using var paths = new TemporaryPaths();
        new SettingsStore(paths).Save(new AppSettings { DisplayLanguage = language });

        // Start from another data language so stale Core strings cannot accidentally pass.
        new LanguageService().SetLanguage(language == "de" ? "ja" : "de");
        var services = App.BuildServiceProvider(paths: paths, settingsStore: new SettingsStore(paths));
        using var disposableServices = (IDisposable)services;
        var settings = services.GetRequiredService<AppSettings>();
        Assert.Equal(language, settings.DisplayLanguage);
        Assert.Equal(DataLanguage(language), GameInfo.CurrentLanguage);

        // Follow the production startup order in App.OnFrameworkInitializationCompleted:
        // persisted settings -> LanguageService -> MainWindowViewModel -> first window.
        var service = services.GetRequiredService<LanguageService>();
        service.SetLanguage(settings.DisplayLanguage);
        _ = services.GetRequiredService<MainWindowViewModel>();
        AssertLanguage(service, language);
        Assert.Equal(language, new SettingsStore(paths).Load().DisplayLanguage);
    }

    [AvaloniaTheory]
    [MemberData(nameof(RoundTrips))]
    public void LiveLanguageRoundTripPersistsSelectionAndPreservesLoadedData(string source, string target)
    {
        using var state = new LocalizationState();
        using var app = new HeadlessAppFixture();
        var save = new SAV6XY { OT = "Language test", Language = (int)LanguageID.Japanese };
        var pokemon = new PK6 { Species = 1, Language = (int)LanguageID.Japanese, Nickname = "Test" };
        save.SetBoxSlotAtIndex(pokemon, 0);
        app.LoadSaveInstance(save);
        app.ViewModel.CurrentPokemonEditor!.LoadPKM(save.GetBoxSlotAtIndex(0));
        var originalSave = save.Data.ToArray();
        var originalPokemon = app.ViewModel.CurrentPokemonEditor.TargetPKM.Data.ToArray();
        var settingsStore = app.Services.GetRequiredService<ISettingsStore>();

        foreach (var originalEdited in new[] { false, true })
        {
            save.State.Edited = originalEdited;
            foreach (var language in new[] { source, target, source })
            {
                app.ViewModel.LanguageService.SetLanguage(language);
                app.Pump();
                AssertLanguage(app.ViewModel.LanguageService, language);
                Assert.Equal(language, app.Services.GetRequiredService<AppSettings>().DisplayLanguage);
                Assert.Equal(language, Assert.IsType<FakeSettingsStore>(settingsStore).Saved!.DisplayLanguage);
                Assert.Equal(originalSave, save.Data.ToArray());
                Assert.Equal(originalPokemon, app.ViewModel.CurrentPokemonEditor.TargetPKM.Data.ToArray());
                Assert.Equal(originalEdited, save.State.Edited);
            }
        }
    }

    // Expected policy is independent of the production mapper, so a missing UI-only mapping fails.
    private static string DataLanguage(string language) => language == "pt-BR" ? "en" : language;

    private static void AssertLanguage(LanguageService service, string language)
    {
        Assert.Equal(language, service.CurrentLanguage);
        Assert.Equal(language, service.CurrentLanguageOption!.Code);
        Assert.Equal(language, Assert.Single(service.AvailableLanguages, option => option.IsCurrent).Code);
        Assert.Equal(language, LocalizedStrings.Instance.CurrentLanguage);
        Assert.Equal(DataLanguage(language), GameInfo.CurrentLanguage);
        // Bulbasaur differs in German/Japanese, unlike Pikachu used in the initial pt-BR test.
        Assert.Equal(GameInfo.GetStrings(DataLanguage(language)).specieslist[1], GameInfo.Strings.specieslist[1]);
        Assert.Equal(language, CultureInfo.CurrentCulture.Name);
        Assert.Equal(language, CultureInfo.CurrentUICulture.Name);
        Assert.Equal(language, CultureInfo.DefaultThreadCurrentCulture!.Name);
        Assert.Equal(language, CultureInfo.DefaultThreadCurrentUICulture!.Name);

        using var resource = typeof(LocalizedStrings).Assembly.GetManifestResourceStream(
            $"PKHeX.Presentation.Localization.Strings.{language}.json");
        Assert.NotNull(resource);
        using var document = JsonDocument.Parse(resource);
        Assert.Equal(document.RootElement.GetProperty("Common_Save").GetString(), LocalizedStrings.Instance["Common_Save"]);
    }

    private sealed class LocalizationState : IDisposable
    {
        private readonly string _uiLanguage = LocalizedStrings.Instance.CurrentLanguage;
        private readonly string _dataLanguage = GameInfo.CurrentLanguage;
        private readonly GameStrings _strings = GameInfo.Strings;
        private readonly FilteredGameDataSource _filteredSources = GameInfo.FilteredSources;
        private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
        private readonly CultureInfo _uiCulture = CultureInfo.CurrentUICulture;
        private readonly CultureInfo? _defaultCulture = CultureInfo.DefaultThreadCurrentCulture;
        private readonly CultureInfo? _defaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;

        public void Dispose()
        {
            GameInfo.CurrentLanguage = _dataLanguage;
            GameInfo.Strings = _strings;
            GameInfo.FilteredSources = _filteredSources;
            LocalizedStrings.Instance.SetLanguage(_uiLanguage);
            CultureInfo.DefaultThreadCurrentCulture = _defaultCulture;
            CultureInfo.DefaultThreadCurrentUICulture = _defaultUiCulture;
            CultureInfo.CurrentCulture = _culture;
            CultureInfo.CurrentUICulture = _uiCulture;
        }
    }

    private sealed class TemporaryPaths : IAppPaths, IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "pkhex-language-" + Guid.NewGuid().ToString("N"));
        public string ConfigDirectory => Path.Combine(_root, "config");
        public string DataDirectory => Path.Combine(_root, "data");
        public string ConfigFilePath => Path.Combine(ConfigDirectory, "config.json");
        public string LegacyConfigFilePath => Path.Combine(_root, "legacy", "config.json");
        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}
