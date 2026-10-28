using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using PKHeX.Avalonia.Tests.Harness;
using PKHeX.Avalonia.Views;
using PKHeX.Core;
using PKHeX.Presentation.Localization;
using PKHeX.Presentation.ViewModels;

namespace PKHeX.Avalonia.Tests;

public class Gen3TicketLayoutTests
{
    public static IEnumerable<object[]> Cases => LocalizedStrings.SupportedLanguages.SelectMany(language =>
        new[] { GameVersion.R, GameVersion.S, GameVersion.E, GameVersion.FR, GameVersion.LG }.Select(version => new object[] { language, version }));
    private static void Pump(Window window)
    { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
    [AvaloniaTheory] [MemberData(nameof(Cases))]
    public void FiveGamesAndAllLocalesShowNamedStatesAndReachRawControlsAndApply(string language, GameVersion version)
    {
        var previous = LocalizedStrings.Instance.CurrentLanguage;
        using var app = new HeadlessAppFixture(); Window? window = null;
        try
        {
            LocalizedStrings.Instance.SetLanguage(language);
            var save = Gen3TicketWorkflowTests.CreateSave(version); var before = Gen3TicketWorkflowTests.Snapshot(save);
            using var vm = new Gen3TicketEditorViewModel(save, app.Services.GetRequiredService<IDialogService>());
            var view = new Gen3TicketEditor { DataContext = vm };
            window = new Window { Content = view, Width = 620, Height = 440 }; window.Show(); Pump(window);
            Assert.Equal(language, LocalizedStrings.Instance.CurrentLanguage);
            Assert.All(vm.Tickets, row => { Assert.NotEmpty(row.Name); Assert.NotEmpty(row.Destination); });
            vm.Tickets[0].StageTicketAndRouteCommand.Execute(null); Pump(window);
            Assert.True(vm.Tickets[0].HasTicket); Assert.True(vm.Tickets[0].RouteEnabled);
            view.FindControl<Expander>("Ticket3Advanced")!.IsExpanded = true;
            view.FindControl<ScrollViewer>("Ticket3Scroll")!.ScrollToEnd(); Pump(window);
            vm.SelectedFlagIndex = 19; vm.SelectedFlagValue = true; Pump(window);
            var apply = view.FindControl<Button>("Ticket3Apply")!;
            var point = apply.TranslatePoint(default, view)!.Value;
            Assert.True(point.X >= 0 && point.X + apply.Bounds.Width <= view.Bounds.Width);
            Assert.True(point.Y >= 0 && point.Y + apply.Bounds.Height <= view.Bounds.Height);
            Assert.True(vm.CanApply); Gen3TicketWorkflowTests.AssertUnchanged(save, before); Assert.False(save.State.Edited);
            vm.ResetCommand.Execute(null); Pump(window);
            Assert.False(vm.Tickets[0].HasTicket); Assert.False(vm.SelectedFlagValue);
            Gen3TicketWorkflowTests.AssertUnchanged(save, before);
        }
        finally { window?.Close(); LocalizedStrings.Instance.SetLanguage(previous); }
    }
    [AvaloniaTheory]
    [InlineData(GameVersion.R)] [InlineData(GameVersion.S)] [InlineData(GameVersion.E)] [InlineData(GameVersion.FR)] [InlineData(GameVersion.LG)]
    public async Task RealMenuOpensMappedViewAndCancelContract(GameVersion version)
    {
        using var app = new HeadlessAppFixture(); var save = Gen3TicketWorkflowTests.CreateSave(version);
        app.LoadSaveInstance(save);
        await app.ViewModel.OpenGen3TicketsCommand.ExecuteAsync(null);
        using var vm = Assert.IsType<Gen3TicketEditorViewModel>(app.Windows.ShownDialogs.Last().ViewModel);
        Assert.IsType<Gen3TicketEditor>(ViewLocator.Build(vm));
        bool closed = false; vm.CloseRequested = () => closed = true; vm.CancelCommand.Execute(null);
        Assert.True(closed); Assert.False(vm.CanApply);
    }
}
