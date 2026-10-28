using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using PKHeX.Application.Abstractions;
using PKHeX.Application.Services;
using PKHeX.Avalonia.Services;
using PKHeX.Avalonia.Tests.Harness;
using PKHeX.Core;
using PKHeX.Infrastructure;
using PKHeX.Infrastructure.Bank;
using PKHeX.Presentation.Localization;
using PKHeX.Presentation.Models;
using PKHeX.Presentation.ViewModels;

namespace PKHeX.Avalonia.Tests;

/// <summary>ViewModel-level coverage for the Bank window, its wallpaper picker and the bundled box-art catalog.</summary>
public sealed class BankViewModelTests : IDisposable
{
    private readonly FakeAppPaths _paths = new();

    public void Dispose()
    {
        try { Directory.Delete(Path.GetDirectoryName(_paths.DataDirectory)!, recursive: true); }
        catch { /* best effort */ }
    }

    private static PKM Mon(ushort species, string nickname)
    {
        var pk = (PK9)new SAV9SV().BlankPKM;
        pk.Species = species;
        pk.Nickname = nickname;
        pk.IsNicknamed = true;
        pk.CurrentLevel = 12;
        return pk;
    }

    private static Guid Deposit(IBankService bank, PKM pk, int? box = null, int? slot = null) =>
        bank.Add(BankEntryInfoFactory.GetRawData(pk), BankEntryInfoFactory.CreateInfo(pk, "Scarlet (Ann)"), box, slot).Entry!.Id;

    private static BankViewModel CreateVm(IBankService bank, RecordingDialogService? dialogs = null, ISaveBackupService? backups = null,
        IWindowService? windows = null, IBoxWallpaperCatalog? catalog = null, BankHoldTracker? tracker = null) =>
        new(bank, new Mock<ISpriteRenderer>().Object, dialogs ?? new RecordingDialogService(), backups, windows, catalog, tracker);

    [Fact]
    public void Grid_ShowsTheBoxesEntries_AndWrapsNavigation()
    {
        var bank = new FileBankService(_paths, slotsPerBox: 30);
        Deposit(bank, Mon(25, "Sparky"), 0, 3);
        bank.AddBox();
        Deposit(bank, Mon(1, "Bulby"), 1, 0);

        var vm = CreateVm(bank);

        Assert.Equal(30, vm.Slots.Count);
        Assert.Equal("Sparky", vm.Slots[3].Nickname);
        Assert.True(vm.Slots[0].IsEmpty);
        Assert.Equal("1 / 30", vm.OccupancyText);

        vm.NextBoxCommand.Execute(null);
        Assert.Equal(1, vm.CurrentBox);
        Assert.Equal("Bulby", vm.Slots[0].Nickname);

        vm.NextBoxCommand.Execute(null);
        Assert.Equal(0, vm.CurrentBox);
        vm.PreviousBoxCommand.Execute(null);
        Assert.Equal(1, vm.CurrentBox);
    }

    [Fact]
    public void AddBox_And_Rename_PersistAcrossInstances()
    {
        var bank = new FileBankService(_paths);
        var vm = CreateVm(bank);

        vm.AddBoxCommand.Execute(null);
        Assert.Equal(1, vm.CurrentBox);
        Assert.Equal(2, vm.BoxCount);

        vm.EditableBoxName = "  Legends  ";
        vm.RenameBoxCommand.Execute(null);
        Assert.Equal("Legends", vm.BoxName);

        var reopened = new FileBankService(_paths);
        Assert.Equal(2, reopened.BoxCount);
        Assert.Equal("Legends", reopened.GetBoxName(1));
    }

    [Fact]
    public void ColorWallpaper_IsExposedAsAHexColor()
    {
        var bank = new FileBankService(_paths);
        var vm = CreateVm(bank);

        vm.SetWallpaperCommand.Execute(BankWallpaper.Color(6));

        Assert.Equal(BankWallpaperPalette.Colors[6], vm.WallpaperColor);
        Assert.True(vm.HasWallpaperColor);
        Assert.False(vm.HasWallpaperImage);
        Assert.Equal(BankWallpaper.Color(6), new FileBankService(_paths).GetBoxWallpaper(0));
    }

    [Fact]
    public async Task PickingArtWallpaper_UsesTheCatalog_AndAppliesTheResult()
    {
        var bank = new FileBankService(_paths);
        var catalog = new AvaloniaBoxWallpaperCatalog();
        var windows = new PickingWindowService(picker => picker.SelectCommand.Execute(picker.ArtGroups[0].Items[1]), apply: true);
        var vm = CreateVm(bank, windows: windows, catalog: catalog);

        await vm.PickWallpaperCommand.ExecuteAsync(null);

        var chosen = bank.GetBoxWallpaper(0);
        Assert.Equal(BankWallpaperKind.Art, chosen.Kind);
        Assert.True(vm.HasWallpaperImage);
        Assert.Equal(catalog.GetImageBytes(chosen.Value), vm.WallpaperImage);
    }

    [Fact]
    public void PendingEntries_AreMarked_AndCannotBeWithdrawn()
    {
        var bank = new FileBankService(_paths);
        var id = Deposit(bank, Mon(133, "Eevee"));
        bank.MarkPendingWithdrawal(id, new BankPendingWithdrawal(Guid.NewGuid(), "Scarlet (Ann)", null, 0, 0, DateTimeOffset.UtcNow));
        var target = new FakeTarget();
        var vm = CreateVm(bank);
        vm.Target = target;
        vm.SelectSlotByClickCommand.Execute(vm.Slots[0]);

        Assert.True(vm.Slots[0].IsPending);
        Assert.Contains(LocalizedStrings.Instance["Bank_SlotPendingShort"], vm.Slots[0].AccessibleName);
        Assert.Contains("Scarlet (Ann)", vm.Slots[0].ToolTipSummary);
        Assert.False(vm.WithdrawSelectedCommand.CanExecute(null));
    }

    [Fact]
    public async Task Withdraw_DelegatesToTheTarget_OnlyWhenASaveCanReceiveIt()
    {
        var bank = new FileBankService(_paths);
        var id = Deposit(bank, Mon(25, "Pika"));
        var target = new FakeTarget { CanReceiveWithdrawal = false };
        var vm = CreateVm(bank);
        vm.Target = target;
        vm.SelectSlotByClickCommand.Execute(vm.Slots[0]);

        Assert.False(vm.WithdrawSelectedCommand.CanExecute(null));
        Assert.Equal(LocalizedStrings.Instance["Bank_NoSaveOpen"], vm.TargetText);

        target.CanReceiveWithdrawal = true;
        target.WithdrawTargetDescription = "Scarlet (Ann), Box 1";
        target.Raise();
        Assert.True(vm.WithdrawSelectedCommand.CanExecute(null));
        Assert.Contains("Scarlet (Ann), Box 1", vm.TargetText);

        await vm.WithdrawSelectedCommand.ExecuteAsync(null);
        Assert.Equal(new[] { id }, target.Withdrawn);
    }

    [Fact]
    public void LocalizedSlotText_FollowsTheLanguage()
    {
        var previous = LocalizedStrings.Instance.CurrentLanguage;
        try
        {
            LocalizedStrings.Instance.SetLanguage("de");
            var empty = new BankSlotData { Slot = 4 };
            Assert.Equal(LocalizedStrings.Instance.Format("Bank_SlotAccessibleEmpty", 5), empty.AccessibleName);
            Assert.StartsWith("Platz 5", empty.AccessibleName);
            Assert.Equal("Leer", empty.ToolTipSummary);
        }
        finally
        {
            LocalizedStrings.Instance.SetLanguage(previous);
        }
    }

    [Fact]
    public void ReadOnlyBank_IsSurfaced_AndDisablesMutations()
    {
        var root = Path.Combine(_paths.DataDirectory, "bank");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "index.json"), "{corrupt");

        var vm = CreateVm(new FileBankService(_paths));

        Assert.True(vm.IsReadOnly);
        Assert.True(vm.HasWarnings);
        Assert.False(vm.AddBoxCommand.CanExecute(null));
        Assert.False(vm.ImportArchiveCommand.CanExecute(null));
    }

    [Fact]
    public async Task RestoreBackup_ListsBankBackups_ConfirmsAndRestores()
    {
        var backups = new SaveBackupService(_paths);
        var bank = new FileBankService(_paths, backups);
        var first = Deposit(bank, Mon(1, "One"));
        var second = Deposit(bank, Mon(2, "Two"));
        var dialogs = new RecordingDialogService { ConfirmResult = true };
        var vm = CreateVm(bank, dialogs, backups);

        Assert.True(vm.IndexBackups.Count >= 2);
        // The backup taken right after the first deposit (it does not list the second one yet).
        vm.SelectedBackup = vm.IndexBackups.First(r =>
            !System.Text.Encoding.UTF8.GetString(backups.ReadBackup(r.Entry)).Contains(second.ToString(), StringComparison.Ordinal));
        await vm.RestoreSelectedBackupCommand.ExecuteAsync(null);

        Assert.Single(dialogs.Confirmations);
        Assert.Empty(dialogs.Errors);
        Assert.NotNull(bank.Find(first));
        Assert.Null(bank.Find(second));
        Assert.Equal(LocalizedStrings.Instance["Bank_Restored"], vm.StatusText);
    }

    [Fact]
    public async Task RestoreBackup_IsRefused_WhileAWithdrawalIsHeld()
    {
        var backups = new SaveBackupService(_paths);
        var bank = new FileBankService(_paths, backups);
        var id = Deposit(bank, Mon(1, "One"));
        var tracker = new BankHoldTracker(bank);
        Assert.True(tracker.Reserve(id, new SAV9SV(), SlotLocation.FromBox(0, 0), "Scarlet", null, out _).Success);
        var dialogs = new RecordingDialogService { ConfirmResult = true };
        var vm = CreateVm(bank, dialogs, backups, tracker: tracker);
        vm.SelectedBackup = vm.IndexBackups.First();

        await vm.RestoreSelectedBackupCommand.ExecuteAsync(null);

        Assert.Equal(LocalizedStrings.Instance["Bank_RestoreBlockedByPending"], Assert.Single(dialogs.Errors).Message);
        Assert.Empty(dialogs.Confirmations);
        Assert.NotNull(bank.Find(id)!.Pending);
    }

    [Fact]
    public void WallpaperCatalog_ExposesTheBundledBoxArt_AsPngs()
    {
        var catalog = new AvaloniaBoxWallpaperCatalog();
        var assets = catalog.GetCatalog();

        Assert.True(assets.Count >= 200, $"only {assets.Count} wallpapers found");
        Assert.Equal(13, assets.Select(a => a.GroupName).Distinct().Count());
        Assert.Contains(assets, a => a.Id == "swsh:box_wp01swsh");

        var png = catalog.GetImageBytes("swsh:box_wp01swsh");
        Assert.NotNull(png);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png![..4]);
        Assert.Null(catalog.GetImageBytes("nope:missing"));
    }

    [Fact]
    public void WallpaperPicker_SelectsTheCurrentWallpaper_AndReportsTheChoice()
    {
        var catalog = new AvaloniaBoxWallpaperCatalog();
        var picker = new WallpaperPickerViewModel(catalog, BankWallpaper.Color(2));

        Assert.Same(picker.Colors[2], picker.Selected);
        Assert.True(picker.Colors[2].IsSelected);
        Assert.Equal(BankWallpaperPalette.Colors.Count, picker.Colors.Count);

        var art = picker.ArtGroups.Last().Items.First();
        picker.SelectCommand.Execute(art);
        Assert.False(picker.Colors[2].IsSelected);
        var closed = 0;
        picker.CloseRequested = () => closed++;
        picker.ApplyCommand.Execute(null);
        Assert.Equal(art.Wallpaper, picker.Result);
        Assert.Equal(1, closed);

        picker.UseDefaultCommand.Execute(null);
        Assert.Equal(BankWallpaper.Default, picker.Result);
        picker.CancelCommand.Execute(null);
        Assert.Null(picker.Result);
    }

    private sealed class FakeTarget : IBankSaveTarget
    {
        public string? WithdrawTargetDescription { get; set; }
        public bool CanReceiveWithdrawal { get; set; } = true;
        public List<Guid> Withdrawn { get; } = [];
        public event Action? TargetChanged;
        public void Raise() => TargetChanged?.Invoke();

        public Task WithdrawAsync(Guid entryId)
        {
            Withdrawn.Add(entryId);
            return Task.CompletedTask;
        }

        public List<IReadOnlyList<Guid>> BulkRequests { get; } = [];

        /// <summary>Ids the fake reports as withdrawn; null means "all requested".</summary>
        public Func<IReadOnlyList<Guid>, IReadOnlyCollection<Guid>>? BulkResult { get; set; }

        public Task<IReadOnlyCollection<Guid>> WithdrawManyAsync(IReadOnlyList<Guid> entryIds)
        {
            BulkRequests.Add(entryIds);
            return Task.FromResult(BulkResult?.Invoke(entryIds) ?? entryIds.ToList());
        }
    }

    /// <summary>Simulates the user operating the wallpaper picker dialog.</summary>
    private sealed class PickingWindowService(Action<WallpaperPickerViewModel> pick, bool apply) : IWindowService
    {
        public Task ShowDialogAsync(object viewModel, string title)
        {
            if (viewModel is WallpaperPickerViewModel picker)
            {
                pick(picker);
                if (apply)
                    picker.ApplyCommand.Execute(null);
            }
            return Task.CompletedTask;
        }

        public void ShowTool(object viewModel, string title) { }
        public void CloseAllTools() { }
        public void ShowPersistent(object viewModel, string title) { }
    }
}
