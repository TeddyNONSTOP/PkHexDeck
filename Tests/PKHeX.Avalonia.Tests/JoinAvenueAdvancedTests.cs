using System.Globalization;
using System.Reflection;
using PKHeX.Core;
using PKHeX.Presentation.ViewModels;

namespace PKHeX.Avalonia.Tests;

public class JoinAvenueAdvancedTests
{
    private static IEnumerable<JoinAvenueEntityViewModel> Entities(JoinAvenueEditorViewModel vm) =>
        new[] { vm.Self }.Concat<JoinAvenueEntityViewModel>(vm.Visitors).Concat(vm.Occupants).Concat(vm.Fans).Concat(vm.Assistants);

    [Fact]
    public void StoredCounts_AreIndependentFromRememberedPlayers_AndDoNotLimitSlotNavigation()
    {
        var save = new SAV5B2W2(); save.JoinAvenue.CountVisitor = 8; save.JoinAvenue.CountFan = 12;
        save.JoinAvenue.Settings.VisitingPlayerDatabaseCount = 3;
        var vm = new JoinAvenueEditorViewModel(save);
        Assert.Equal(8, vm.CountVisitor); Assert.Equal(12, vm.CountFan); Assert.Equal(3, vm.VisitingPlayerCount);
        vm.CountVisitor = uint.MaxValue; vm.CountFan = 0;
        Assert.Equal(8, vm.Visitors.Count); Assert.Equal(12, vm.Fans.Count);
        var reload = new SAV5B2W2(save.Write());
        Assert.Equal(uint.MaxValue, reload.JoinAvenue.CountVisitor); Assert.Equal(0u, reload.JoinAvenue.CountFan);
        Assert.Equal(3, reload.JoinAvenue.Settings.VisitingPlayerDatabaseCount);
    }
    [Fact]
    public void EveryNewField_RoundTripsAcrossSelfVisitorsOccupantsFansAndAssistants()
    {
        var save = new SAV5B2W2(); var vm = new JoinAvenueEditorViewModel(save);
        foreach (var entry in Entities(vm))
        {
            foreach (var row in entry.AdvancedNumbers) row.Value = row.Maximum;
            foreach (var row in entry.AdvancedFlags) row.Value = true;
            foreach (var row in entry.AdvancedDates) row.DateText = "2024-02-29";
        }
        var reload = new JoinAvenueEditorViewModel(new SAV5B2W2(save.Write()));
        foreach (var (before, after) in Entities(vm).Zip(Entities(reload)))
        {
            foreach (var row in before.AdvancedNumbers)
            {
                var stored = Assert.Single(after.AdvancedNumbers, other => other.Id == row.Id);
                Assert.Equal(row.Maximum, stored.Value);
                Assert.Equal(row.Maximum, ReadNumber(after.Entity, row.Id));
            }
            foreach (var row in after.AdvancedFlags)
            {
                Assert.True(row.Value);
                Assert.True((bool)after.Entity.GetType().GetProperty(row.Id)!.GetValue(after.Entity)!);
            }
            foreach (var row in after.AdvancedDates)
            {
                Assert.Equal("2024-02-29", row.DateText);
                Assert.Equal(new DateOnly(2024, 2, 29), ReadDate(after.Entity, row.Id).Date);
            }
        }
    }
    [Fact]
    public void InvalidNumberAndDateInputs_DoNotMutateAnyBytes()
    {
        var save = new SAV5B2W2(); var vm = new JoinAvenueEditorViewModel(save); save.State.Edited = false;
        var before = save.Data.ToArray();
        foreach (var row in vm.Self.AdvancedNumbers)
        {
            row.Value = row.Maximum + 1;
            Assert.NotEmpty(row.Error); Assert.Equal(before, save.Data.ToArray());
            row.Refresh();
        }
        foreach (var invalid in new[] { "2128-01-01", "1999-12-31", "2024-02-30", "not a date" })
        {
            vm.Self.AdvancedDates[0].DateText = invalid;
            Assert.NotEmpty(vm.Self.AdvancedDates[0].Error); Assert.Equal(before, save.Data.ToArray());
        }
        Assert.False(save.State.Edited);
    }
    [Fact]
    public void RawAndPrimaryRepresentations_StaySynchronized_AndPreserveAdjacentPackedBits()
    {
        var save = new SAV5B2W2(); var raw = save.JoinAvenue.Self.Write().ToArray(); raw[0x22] = 0x7A;
        save.JoinAvenue.Self.CopyFrom(new JoinAvenueVisitor5(raw));
        var vm = new JoinAvenueEditorViewModel(save);
        Assert.Equal(7, vm.Self.Gender);
        vm.Self.AdvancedNumbers.Single(r => r.Id == "Unknown22").Value = 15;
        Assert.Equal(7, save.JoinAvenue.Self.Gender);
        vm.Self.AdvancedNumbers.Single(r => r.Id == "PlayedTime").Value = 65535;
        Assert.Equal(1023, vm.Self.PlayedHours); Assert.Equal(63, vm.Self.PlayedMinutes);
        vm.Self.PlayedMinutes = 25;
        Assert.Equal(save.JoinAvenue.Self.PlayedTime, vm.Self.AdvancedNumbers.Single(r => r.Id == "PlayedTime").Value);
        Assert.Equal(1023, save.JoinAvenue.Self.PlayedHours);
        var assistant = save.JoinAvenue.GetAssistant(0); raw = assistant.Write().ToArray(); raw[0x33] = 0xAA;
        assistant.CopyFrom(new JoinAvenueAssistant5(raw));
        vm.Assistants[0].Reload(); vm.Assistants[0].IsInteractedToday = true;
        Assert.Equal(0xAB, assistant.Write()[0x33]);
        vm.Assistants[0].IsInteractedToday = false; Assert.Equal(0xAA, assistant.Write()[0x33]);
    }
    [Fact]
    public void AllWritableCoreScalarFields_HavePrimaryOrAdvancedCoverage()
    {
        var vm = new JoinAvenueEditorViewModel(new SAV5B2W2());
        var primary = new HashSet<string>
        {
            "Name", "Country", "Subregion", "Shout", "Version", "Language", "Gender", "TID16", "PlayedHours", "PlayedMinutes", "Sprite",
            "Greeting", "Farewell", "MetYear", "MetMonth", "MetDay", "IsInteractedToday", "Seed", "JoinAvenueLevel", "DexSeen", "FavoriteSpecies",
            "MedalRank", "MedalHint", "MedalCount", "Origin", "JoinAvenueRank", "ShopRank", "ShopExperience", "Species", "BubbleTarget", "Position0", "Position1", "Position2",
        };
        foreach (var entry in new JoinAvenueEntityViewModel[] { vm.Self, vm.Fans[0], vm.Assistants[0] })
        {
            var advanced = entry.AdvancedNumbers.Select(r => r.Id).Concat(entry.AdvancedFlags.Select(r => r.Id)).Concat(entry.AdvancedDates.Select(r => r.Id)).ToHashSet();
            var missing = entry.Entity.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(p => p.CanWrite && (p.PropertyType == typeof(byte) || p.PropertyType == typeof(ushort) || p.PropertyType == typeof(uint)
                    || p.PropertyType == typeof(bool) || p.PropertyType == typeof(JoinAvenueDate5)))
                .Select(p => p.Name).Where(name => !primary.Contains(name) && !advanced.Contains(name));
            Assert.Empty(missing);
        }
        Assert.Equal(8, vm.Self.AdvancedNumbers.Count(r => r.Id.StartsWith("Record", StringComparison.Ordinal)));
        Assert.Equal(16, vm.Self.AdvancedNumbers.Count(r => r.Id.StartsWith("Trivia", StringComparison.Ordinal)));
        Assert.Equal(4, vm.Self.AdvancedNumbers.Count(r => r.Id.StartsWith("Activity", StringComparison.Ordinal)));
        Assert.Equal(7, vm.Self.AdvancedDates.Count);
    }
    private static long ReadNumber(IJoinAvenueEntity5 entity, string id)
    {
        if (entity.GetType().GetProperty(id) is { } property) return Convert.ToInt64(property.GetValue(entity), CultureInfo.InvariantCulture);
        var visitor = Assert.IsType<JoinAvenueVisitor5>(entity);
        if (id.StartsWith("Record", StringComparison.Ordinal)) return visitor.GetRecord((JoinAvenueRecordIndex5)int.Parse(id[6..], CultureInfo.InvariantCulture));
        if (id.StartsWith("Trivia", StringComparison.Ordinal)) return visitor.GetTrivia(int.Parse(id[6..], CultureInfo.InvariantCulture));
        return visitor.GetActivity(int.Parse(id[8..], CultureInfo.InvariantCulture));
    }
    private static JoinAvenueDate5 ReadDate(IJoinAvenueEntity5 entity, string id)
    {
        if (entity.GetType().GetProperty(id) is { } property) return (JoinAvenueDate5)property.GetValue(entity)!;
        return Assert.IsType<JoinAvenueVisitor5>(entity).GetActivityDate(int.Parse(id[12..], CultureInfo.InvariantCulture));
    }
}
