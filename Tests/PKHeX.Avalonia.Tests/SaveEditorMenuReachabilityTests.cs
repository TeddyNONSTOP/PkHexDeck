using System.Windows.Input;
using Avalonia.Headless.XUnit;
using PKHeX.Avalonia.Tests.Harness;
using PKHeX.Core;
using PKHeX.Presentation.ViewModels;

namespace PKHeX.Avalonia.Tests;

public sealed class SaveEditorMenuReachabilityTests
{
    [AvaloniaFact]
    public void PreviouslyUnreachableEditorsFollowActualSaveCapabilities()
    {
        using var app = new HeadlessAppFixture();
        var vm = app.ViewModel;
        var commands = new ICommand[]
        {
            vm.OpenGroupViewerCommand, vm.OpenApricornCommand, vm.OpenUndergroundCommand,
            vm.OpenSimpleTrainerCommand, vm.OpenGearBRCommand, vm.OpenSecretBase3Command,
            vm.OpenPokepuffCommand, vm.OpenPokeBlockCommand, vm.OpenBerryFieldCommand,
            vm.OpenMisc7bCommand, vm.OpenMisc8Command, vm.OpenMisc8aCommand,
            vm.OpenRTCCommand,
        };

        Check(new SAV3E(), vm.OpenSimpleTrainerCommand, vm.OpenSecretBase3Command, vm.OpenRTCCommand);
        Check(new SAV4HGSS(), vm.OpenSimpleTrainerCommand, vm.OpenApricornCommand);
        Check(new SAV4Pt(), vm.OpenSimpleTrainerCommand, vm.OpenUndergroundCommand);
        Check(new SAV4BR(), vm.OpenGearBRCommand);
        Check(new SAV6XY(), vm.OpenPokepuffCommand);
        Check(new SAV6AO(), vm.OpenPokepuffCommand, vm.OpenPokeBlockCommand, vm.OpenBerryFieldCommand);
        Check(new SAV7b(), vm.OpenMisc7bCommand);
        Check(new SAV8SWSH(), vm.OpenMisc8Command);
        Check(new SAV8LA(), vm.OpenMisc8aCommand);
        Check(new SAV1Stadium(), vm.OpenGroupViewerCommand);

        void Check(SaveFile save, params ICommand[] expected)
        {
            app.LoadSaveInstance(save);
            var entries = vm.ToolMenuGroups.SelectMany(group => group.Items)
                .Where(item => commands.Contains(item.Command))
                .ToList();
            Assert.Equal(commands.Length, entries.Count);
            foreach (var entry in entries)
                Assert.Equal(expected.Contains(entry.Command), entry.IsAvailable);
        }
    }

    [AvaloniaFact]
    public void FolderListRemainsReachableWithoutASave()
    {
        using var app = new HeadlessAppFixture();
        var vm = app.ViewModel;
        var entry = Assert.Single(vm.ToolMenuGroups.SelectMany(group => group.Items),
            item => ReferenceEquals(item.Command, vm.OpenFolderListCommand));
        Assert.True(entry.IsAvailable);
    }
}
