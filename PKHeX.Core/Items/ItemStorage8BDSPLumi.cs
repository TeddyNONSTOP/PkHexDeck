using System;

namespace PKHeX.Core;

/// <summary>
/// Item storage for <see cref="EntityContext.Gen8bLumi"/> (Luminescent Platinum, ROM hack of Brilliant Diamond &amp; Shining Pearl).
/// </summary>
/// <remarks>
/// Reuses vanilla BDSP's item pouches unchanged (item IDs 1808-1822, once believed Luminescent-exclusive by the
/// reference fork, are actually vanilla BDSP items already present in <see cref="ItemStorage8BDSP"/> in this Core
/// version - see <see cref="Legal.MaxItemID_8b"/>). Only IDs 1823-1836 are genuinely new to this ROM hack.
/// </remarks>
public sealed class ItemStorage8BDSPLumi : IItemStorage
{
    public static readonly ItemStorage8BDSPLumi Instance = new();

    private static ReadOnlySpan<ushort> NewGeneral =>
    [
        1825, 1834, 1836,
    ];

    private static ReadOnlySpan<ushort> NewKey =>
    [
        1823, 1824, 1826, 1827, 1828, 1829, 1830, 1831, 1832, 1833, 1835,
    ];

    public static ushort[] General { get; } = [.. ItemStorage8BDSP.General, .. NewGeneral];
    public static ushort[] Key { get; } = [.. ItemStorage8BDSP.Key, .. NewKey];

    public int GetMax(InventoryType type) => ItemStorage8BDSP.Instance.GetMax(type);

    public ReadOnlySpan<ushort> GetItems(InventoryType type) => GetLegal(type);

    public static ReadOnlySpan<ushort> GetLegal(InventoryType type) => type switch
    {
        InventoryType.Items => General,
        InventoryType.KeyItems => Key,
        InventoryType.TMHMs => ItemStorage8BDSP.Machine,
        InventoryType.Medicine => ItemStorage8BDSP.Medicine,
        InventoryType.Berries => ItemStorage8BDSP.Berry,
        InventoryType.Balls => ItemStorage8BDSP.Balls,
        InventoryType.BattleItems => ItemStorage8BDSP.Battle,
        InventoryType.Treasure => ItemStorage8BDSP.Treasure,
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    private static ReadOnlySpan<InventoryType> ValidTypes =>
    [
        InventoryType.Items, InventoryType.KeyItems, InventoryType.TMHMs, InventoryType.Medicine,
        InventoryType.Berries, InventoryType.Balls, InventoryType.BattleItems, InventoryType.Treasure,
    ];

    public static ushort[] GetAllHeld() =>
    [
        .. General, .. ItemStorage8BDSP.Balls, .. ItemStorage8BDSP.Battle,
        .. ItemStorage8BDSP.Berry, .. ItemStorage8BDSP.Machine, .. ItemStorage8BDSP.Medicine,
        .. ItemStorage8BDSP.Treasure,
    ];

    public static InventoryType GetInventoryPouch(ushort itemIndex)
    {
        foreach (var type in ValidTypes)
        {
            if (GetLegal(type).Contains(itemIndex))
                return type;
        }
        return InventoryType.None;
    }

    public bool IsLegal(InventoryType type, int itemIndex, int itemCount)
    {
        if (type is InventoryType.KeyItems)
            return true;

        return itemCount != 0 && ItemStorage8BDSP.Unreleased.BinarySearch((ushort)itemIndex) < 0;
    }
}
