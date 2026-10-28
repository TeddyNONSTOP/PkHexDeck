using CommunityToolkit.Mvvm.Messaging;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Moq;
using PKHeX.Avalonia.Tests.Harness;
using PKHeX.Avalonia.Views;
using PKHeX.Core;
using PKHeX.Presentation.Localization;
using PKHeX.Presentation.ViewModels;

namespace PKHeX.Avalonia.Tests;

public class DonutTransactionLayoutTests
{
    [AvaloniaFact]
    public async Task LocalFileDropStagesSelectedRecordWithoutChangingSource()
    {
        using var app = new HeadlessAppFixture();
        var path = Path.GetTempFileName(); Window? window = null;
        try
        {
            var data = new byte[Donut9a.Size]; data[0] = 1; data[^1] = 0xA5;
            await File.WriteAllBytesAsync(path, data);
            var save = DonutFixtureWorkflowTests.CreateSave(); var before = save.Donuts.Data.ToArray();
            using var vm = new DonutEditorViewModel(save);
            var view = new DonutEditor { DataContext = vm };
            window = new Window { Content = view, Width = 720, Height = 460 };
            window.Show(); Pump(window);
            var file = new Mock<IStorageFile>(); file.SetupGet(f => f.Path).Returns(new Uri(path));
            var transfer = new DataTransfer(); transfer.Add(DataTransferItem.CreateFile(file.Object));
            var point = view.FindControl<ListBox>("DonutList")!.TranslatePoint(new Point(20, 20), window)!.Value;
            bool routed = false;
            view.AddHandler(DragDrop.DropEvent, (_, _) => routed = true, handledEventsToo: true);
            var imported = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(vm.SelectedDonut) && vm.SelectedDonut is { IsOccupied: true })
                {
                    // A published import must already have released its local file handle.
                    using var reopened = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                    Assert.Equal(Donut9a.Size, reopened.Length);
                    imported.TrySetResult(true);
                }
                if (args.PropertyName == nameof(vm.Error) && vm.HasError) imported.TrySetResult(false);
            };
            Assert.True(DragDrop.GetAllowDrop(view));
            Assert.True(point.X >= 0 && point.Y >= 0 && point.X < window.Bounds.Width && point.Y < window.Bounds.Height);
            // As with the composed harness Click helper, exercise the production routed handler
            // without depending on the asynchronous compositor's raw-input hit-test scene.
            var over = new DragEventArgs(DragDrop.DragOverEvent, transfer, view, default, KeyModifiers.None);
            view.RaiseEvent(over); Assert.True(over.Handled); Assert.Equal(DragDropEffects.Copy, over.DragEffects);
            var multiple = new DataTransfer(); multiple.Add(DataTransferItem.CreateFile(file.Object)); multiple.Add(DataTransferItem.CreateFile(file.Object));
            var rejected = new DragEventArgs(DragDrop.DragOverEvent, multiple, view, default, KeyModifiers.None);
            view.RaiseEvent(rejected); Assert.Equal(DragDropEffects.None, rejected.DragEffects);
            view.RaiseEvent(new DragEventArgs(DragDrop.DropEvent, transfer, view, default, KeyModifiers.None));
            Assert.True(routed, "The drop must reach the actual editor's routed handler.");
            await Task.WhenAny(imported.Task, Task.Delay(TimeSpan.FromSeconds(10)));
            Assert.True(imported.Task.IsCompletedSuccessfully && imported.Task.Result, $"Local import did not finish: {vm.Error}");
            Assert.True(vm.SelectedDonut!.IsOccupied);
            Assert.Equal(before, save.Donuts.Data.ToArray()); Assert.False(save.State.Edited);
            vm.SaveCommand.Execute(null);
            Assert.Equal(data, save.Donuts.GetDonut(0).Data.ToArray());
        }
        finally { window?.Close(); File.Delete(path); }
    }

    [AvaloniaFact]
    public void LiveLanguageChangeAndRawUnknownIdKeepStagedRecord()
    {
        var previous = LocalizedStrings.Instance.CurrentLanguage;
        using var app = new HeadlessAppFixture(); Window? window = null;
        try
        {
            var save = new SAV9ZA(); var record = save.Donuts.GetDonut(0); record.MillisecondsSince1970 = 1;
            record.Donut = 65000; record.Berry1 = 65001;
            var before = save.Donuts.Data.ToArray();
            using var vm = new DonutEditorViewModel(save);
            var view = new DonutEditor { DataContext = vm }; window = new Window { Content = view, Width = 720, Height = 460 };
            window.Show(); Pump(window);
            vm.SelectedDonut!.Berry1 = 65002; vm.SelectedDonut.DonutType = 65003; Pump(window);
            Assert.Contains(vm.SelectedDonut.BerryOptions, option => option.Value == 65002);
            Assert.Contains(vm.SelectedDonut.DonutOptions, option => option.Value == 65003);
            LocalizedStrings.Instance.SetLanguage("de");
            CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.Send(new LanguageChangedMessage("de")); Pump(window);
            Assert.Equal(65002, vm.SelectedDonut.Berry1); Assert.Equal(65003, vm.SelectedDonut.DonutType);
            Assert.Equal(before, save.Donuts.Data.ToArray());
        }
        finally { window?.Close(); LocalizedStrings.Instance.SetLanguage(previous); }
    }

    public static IEnumerable<object[]> Languages => LocalizedStrings.SupportedLanguages.Select(language => new object[] { language });
    [AvaloniaTheory] [MemberData(nameof(Languages))]
    public void AllLocalesPreserveUnknownIdsAndHashesWhileKeepingSaveReachable(string language)
    {
        var previous = LocalizedStrings.Instance.CurrentLanguage;
        using var app = new HeadlessAppFixture(); Window? window = null;
        try
        {
            LocalizedStrings.Instance.SetLanguage(language);
            var save = new SAV9ZA(); var record = save.Donuts.GetDonut(0);
            record.MillisecondsSince1970 = 1; record.Donut = 65000; record.Berry1 = 65001;
            record.Stars = 255; record.Calories = 65535; record.Flavor0 = ulong.MaxValue; record.Reserved = 12345;
            var before = save.Donuts.Data.ToArray(); save.State.Edited = false;
            using var vm = new DonutEditorViewModel(save);
            var view = new DonutEditor { DataContext = vm }; window = new Window { Content = view, Width = 720, Height = 460 }; window.Show();
            Pump(window);
            Assert.Equal(65000, vm.SelectedDonut!.DonutType); Assert.Equal(65001, vm.SelectedDonut.Berry1);
            Assert.Equal(255, vm.SelectedDonut.Stars); Assert.Equal(65535, vm.SelectedDonut.Calories);
            Assert.Equal("FFFFFFFFFFFFFFFF", vm.SelectedDonut.Flavor0Text);
            view.FindControl<ScrollViewer>("DonutDetailsScroll")!.ScrollToEnd(); Pump(window);
            var saveButton = view.FindControl<Button>("DonutSave")!;
            var point = saveButton.TranslatePoint(default, view)!.Value;
            Assert.True(point.Y >= 0 && point.Y + saveButton.Bounds.Height <= view.Bounds.Height);
            Assert.True(point.X >= 0 && point.X + saveButton.Bounds.Width <= view.Bounds.Width);
            Assert.True(vm.CanSave); vm.SaveCommand.Execute(null);
            Assert.Equal(before, save.Donuts.Data.ToArray()); Assert.False(save.State.Edited);
        }
        finally { window?.Close(); LocalizedStrings.Instance.SetLanguage(previous); }
    }
    private static void Pump(Window window)
    { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
}
