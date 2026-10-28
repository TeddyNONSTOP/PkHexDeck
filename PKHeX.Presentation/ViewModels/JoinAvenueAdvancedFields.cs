using PKHeX.Core;

namespace PKHeX.Presentation.ViewModels;

/// <summary>Compile-checked bindings for the documented Gen 5 Core advanced surface.</summary>
internal static class JoinAvenueAdvancedFields
{
    public static (IReadOnlyList<JoinAvenueNumberField>, IReadOnlyList<JoinAvenueFlagField>, IReadOnlyList<JoinAvenueDateField>) Create(IJoinAvenueEntity5 entity, Action<Action> apply)
    {
        var numbers = new List<JoinAvenueNumberField>();
        var flags = new List<JoinAvenueFlagField>();
        var dates = new List<JoinAvenueDateField>();
        switch (entity)
        {
            case JoinAvenueVisitor5 e:
                numbers.Add(new("Unknown22", "JoinAvenueAdvanced_RawField", 15L, () => e.Unknown22, value => apply(() => e.Unknown22 = (byte)value), "Unknown22"));
                numbers.Add(new("Unused23", "JoinAvenueAdvanced_RawField", 255L, () => e.Unused23, value => apply(() => e.Unused23 = (byte)value), "Unused23"));
                numbers.Add(new("Unknown26", "JoinAvenueAdvanced_RawField", 255L, () => e.Unknown26, value => apply(() => e.Unknown26 = (byte)value), "Unknown26"));
                numbers.Add(new("Unknown27", "JoinAvenueAdvanced_RawField", 255L, () => e.Unknown27, value => apply(() => e.Unknown27 = (byte)value), "Unknown27"));
                numbers.Add(new("PlayedTime", "JoinAvenueAdvanced_PlayedTime", 65535L, () => e.PlayedTime, value => apply(() => e.PlayedTime = (ushort)value)));
                flags.Add(new("IsFlag2C", "JoinAvenueAdvanced_RawFlag", () => e.IsFlag2C, value => apply(() => e.IsFlag2C = value), "IsFlag2C"));
                numbers.Add(new("Unused2D", "JoinAvenueAdvanced_RawField", 255L, () => e.Unused2D, value => apply(() => e.Unused2D = (byte)value), "Unused2D"));
                numbers.Add(new("DesiredShopType", "JoinAvenueAdvanced_DesiredShopType", 65535L, () => e.DesiredShopType, value => apply(() => e.DesiredShopType = (ushort)value)));
                numbers.Add(new("ShopCountRaffle", "JoinAvenueAdvanced_ShopCountRaffle", 15L, () => e.ShopCountRaffle, value => apply(() => e.ShopCountRaffle = (byte)value)));
                numbers.Add(new("ShopCountSalon", "JoinAvenueAdvanced_ShopCountSalon", 15L, () => e.ShopCountSalon, value => apply(() => e.ShopCountSalon = (byte)value)));
                numbers.Add(new("ShopCountMarket", "JoinAvenueAdvanced_ShopCountMarket", 15L, () => e.ShopCountMarket, value => apply(() => e.ShopCountMarket = (byte)value)));
                numbers.Add(new("ShopCountFlorist", "JoinAvenueAdvanced_ShopCountFlorist", 15L, () => e.ShopCountFlorist, value => apply(() => e.ShopCountFlorist = (byte)value)));
                numbers.Add(new("ShopCountDojo", "JoinAvenueAdvanced_ShopCountDojo", 15L, () => e.ShopCountDojo, value => apply(() => e.ShopCountDojo = (byte)value)));
                numbers.Add(new("ShopCountNurse", "JoinAvenueAdvanced_ShopCountNurse", 15L, () => e.ShopCountNurse, value => apply(() => e.ShopCountNurse = (byte)value)));
                numbers.Add(new("ShopCountAntique", "JoinAvenueAdvanced_ShopCountAntique", 15L, () => e.ShopCountAntique, value => apply(() => e.ShopCountAntique = (byte)value)));
                numbers.Add(new("ShopCountCafe", "JoinAvenueAdvanced_ShopCountCafe", 15L, () => e.ShopCountCafe, value => apply(() => e.ShopCountCafe = (byte)value)));
                dates.Add(new("Date1", "JoinAvenueAdvanced_Date1", () => e.Date1, value => apply(() => e.Date1 = value)));
                dates.Add(new("DateAdventureStart", "JoinAvenueAdvanced_DateAdventureStart", () => e.DateAdventureStart, value => apply(() => e.DateAdventureStart = value)));
                dates.Add(new("DateHallOfFame", "JoinAvenueAdvanced_DateHallOfFame", () => e.DateHallOfFame, value => apply(() => e.DateHallOfFame = value)));
                numbers.Add(new("UnusedA2", "JoinAvenueAdvanced_RawField", 255L, () => e.UnusedA2, value => apply(() => e.UnusedA2 = (byte)value), "UnusedA2"));
                numbers.Add(new("MetHour", "JoinAvenueAdvanced_MetHour", 255L, () => e.MetHour, value => apply(() => e.MetHour = (byte)value)));
                numbers.Add(new("MetMinute", "JoinAvenueAdvanced_MetMinute", 255L, () => e.MetMinute, value => apply(() => e.MetMinute = (byte)value)));
                numbers.Add(new("UnknownA8", "JoinAvenueAdvanced_RawField", 255L, () => e.UnknownA8, value => apply(() => e.UnknownA8 = (byte)value), "UnknownA8"));
                flags.Add(new("IsShopChangeAllowed", "JoinAvenueAdvanced_IsShopChangeAllowed", () => e.IsShopChangeAllowed, value => apply(() => e.IsShopChangeAllowed = value)));
                flags.Add(new("IsFlagA9_1", "JoinAvenueAdvanced_RawFlag", () => e.IsFlagA9_1, value => apply(() => e.IsFlagA9_1 = value), "IsFlagA9_1"));
                flags.Add(new("IsFlagA9_2", "JoinAvenueAdvanced_RawFlag", () => e.IsFlagA9_2, value => apply(() => e.IsFlagA9_2 = value), "IsFlagA9_2"));
                flags.Add(new("IsFlagAA", "JoinAvenueAdvanced_RawFlag", () => e.IsFlagAA, value => apply(() => e.IsFlagAA = value), "IsFlagAA"));
                numbers.Add(new("UnknownAC", "JoinAvenueAdvanced_RawField", 255L, () => e.UnknownAC, value => apply(() => e.UnknownAC = (byte)value), "UnknownAC"));
                numbers.Add(new("IsInventory", "JoinAvenueAdvanced_IsInventory", 4294967295L, () => e.IsInventory, value => apply(() => e.IsInventory = (uint)value)));
                numbers.Add(new("ShopType", "JoinAvenueAdvanced_ShopType", 65535L, () => e.ShopType, value => apply(() => e.ShopType = (ushort)value)));
                numbers.Add(new("ShopWork", "JoinAvenueAdvanced_ShopWork", 65535L, () => e.ShopWork, value => apply(() => e.ShopWork = (ushort)value)));
                numbers.Add(new("UnusedB8", "JoinAvenueAdvanced_RawField", 4294967295L, () => e.UnusedB8, value => apply(() => e.UnusedB8 = (uint)value), "UnusedB8"));
                numbers.Add(new("UnknownBits0_8", "JoinAvenueAdvanced_RawField", 511L, () => e.UnknownBits0_8, value => apply(() => e.UnknownBits0_8 = (ushort)value), "UnknownBits0_8"));
                flags.Add(new("IsUnknownBits9", "JoinAvenueAdvanced_RawFlag", () => e.IsUnknownBits9, value => apply(() => e.IsUnknownBits9 = value), "IsUnknownBits9"));
                numbers.Add(new("UnknownBits10", "JoinAvenueAdvanced_RawField", 7L, () => e.UnknownBits10, value => apply(() => e.UnknownBits10 = (byte)value), "UnknownBits10"));
                numbers.Add(new("UnknownBits13_20", "JoinAvenueAdvanced_RawField", 255L, () => e.UnknownBits13_20, value => apply(() => e.UnknownBits13_20 = (byte)value), "UnknownBits13_20"));
                numbers.Add(new("UnknownBits21_27", "JoinAvenueAdvanced_RawField", 127L, () => e.UnknownBits21_27, value => apply(() => e.UnknownBits21_27 = (byte)value), "UnknownBits21_27"));
                numbers.Add(new("UnknownBits28_31", "JoinAvenueAdvanced_RawField", 15L, () => e.UnknownBits28_31, value => apply(() => e.UnknownBits28_31 = (byte)value), "UnknownBits28_31"));
                for (var i = 0; i < (int)JoinAvenueRecordIndex5.COUNT_MAX; i++)
                {
                    var index = (JoinAvenueRecordIndex5)i;
                    numbers.Add(new($"Record{i}", $"JoinAvenueAdvanced_Record{i}", uint.MaxValue, () => e.GetRecord(index), value => apply(() => e.SetRecord(index, (uint)value))));
                }
                for (var i = 0; i < JoinAvenueVisitor5.TriviaCount; i++)
                {
                    var index = i;
                    numbers.Add(new($"Trivia{i}", "JoinAvenueAdvanced_Trivia", byte.MaxValue, () => e.GetTrivia(index), value => apply(() => e.SetTrivia(index, (byte)value)), index + 1));
                }
                for (var i = 0; i < JoinAvenueVisitor5.ActivityCount; i++)
                {
                    var index = i;
                    numbers.Add(new($"Activity{i}", "JoinAvenueAdvanced_Activity", byte.MaxValue, () => e.GetActivity(index), value => apply(() => e.SetActivity(index, (byte)value)), index + 1));
                    dates.Add(new($"ActivityDate{i}", "JoinAvenueAdvanced_ActivityDate", () => e.GetActivityDate(index), value => apply(() => e.SetActivityDate(index, value)), index + 1));
                }
                break;
            case JoinAvenueFan5 e:
                numbers.Add(new("Unknown22", "JoinAvenueAdvanced_RawField", 15L, () => e.Unknown22, value => apply(() => e.Unknown22 = (byte)value), "Unknown22"));
                numbers.Add(new("Unused23", "JoinAvenueAdvanced_RawField", 255L, () => e.Unused23, value => apply(() => e.Unused23 = (byte)value), "Unused23"));
                numbers.Add(new("Unknown26", "JoinAvenueAdvanced_RawField", 255L, () => e.Unknown26, value => apply(() => e.Unknown26 = (byte)value), "Unknown26"));
                numbers.Add(new("Unknown27", "JoinAvenueAdvanced_RawField", 255L, () => e.Unknown27, value => apply(() => e.Unknown27 = (byte)value), "Unknown27"));
                numbers.Add(new("PlayedTime", "JoinAvenueAdvanced_PlayedTime", 65535L, () => e.PlayedTime, value => apply(() => e.PlayedTime = (ushort)value)));
                numbers.Add(new("Unknown4C", "JoinAvenueAdvanced_RawField", 255L, () => e.Unknown4C, value => apply(() => e.Unknown4C = (byte)value), "Unknown4C"));
                numbers.Add(new("Unknown4D", "JoinAvenueAdvanced_RawField", 255L, () => e.Unknown4D, value => apply(() => e.Unknown4D = (byte)value), "Unknown4D"));
                numbers.Add(new("Unknown4F", "JoinAvenueAdvanced_RawField", 255L, () => e.Unknown4F, value => apply(() => e.Unknown4F = (byte)value), "Unknown4F"));
                numbers.Add(new("Unknown52", "JoinAvenueAdvanced_RawField", 65535L, () => e.Unknown52, value => apply(() => e.Unknown52 = (ushort)value), "Unknown52"));
                numbers.Add(new("Unknown54", "JoinAvenueAdvanced_RawField", 255L, () => e.Unknown54, value => apply(() => e.Unknown54 = (byte)value), "Unknown54"));
                numbers.Add(new("Unknown56", "JoinAvenueAdvanced_RawField", 255L, () => e.Unknown56, value => apply(() => e.Unknown56 = (byte)value), "Unknown56"));
                numbers.Add(new("Unknown5A", "JoinAvenueAdvanced_RawField", 65535L, () => e.Unknown5A, value => apply(() => e.Unknown5A = (ushort)value), "Unknown5A"));
                break;
            case JoinAvenueAssistant5 e:
                numbers.Add(new("Unknown22", "JoinAvenueAdvanced_RawField", 15L, () => e.Unknown22, value => apply(() => e.Unknown22 = (byte)value), "Unknown22"));
                numbers.Add(new("Unused23", "JoinAvenueAdvanced_RawField", 255L, () => e.Unused23, value => apply(() => e.Unused23 = (byte)value), "Unused23"));
                numbers.Add(new("Unknown26", "JoinAvenueAdvanced_RawField", 255L, () => e.Unknown26, value => apply(() => e.Unknown26 = (byte)value), "Unknown26"));
                numbers.Add(new("Unknown27", "JoinAvenueAdvanced_RawField", 255L, () => e.Unknown27, value => apply(() => e.Unknown27 = (byte)value), "Unknown27"));
                numbers.Add(new("PlayedTime", "JoinAvenueAdvanced_PlayedTime", 65535L, () => e.PlayedTime, value => apply(() => e.PlayedTime = (ushort)value)));
                numbers.Add(new("PositionUnused", "JoinAvenueAdvanced_RawField", 255L, () => e.PositionUnused, value => apply(() => e.PositionUnused = (byte)value), "PositionUnused"));
                break;
        }
        return (numbers, flags, dates);
    }
}
