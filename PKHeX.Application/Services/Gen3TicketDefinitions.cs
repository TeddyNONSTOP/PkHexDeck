using PKHeX.Core;

namespace PKHeX.Application.Services;

/// <summary>Verified per-game travel gates; item IDs use Gen 3 numbering.</summary>
public sealed record Gen3TicketDefinition(string Id, int ItemId, int TravelFlag,
    int? ShownFlag = null, int? ReceivedFlag = null, int? CompletedFlag = null);

/// <summary>Mappings are documented with pinned primary sources in docs/gen3-ticket-state-mappings.md.</summary>
public static class Gen3TicketDefinitions
{
    public static IReadOnlyList<Gen3TicketDefinition> For(SAV3 save) => save switch
    {
        SAV3RS => [new("Eon", 275, 0x853, CompletedFlag: 0xCE)],
        SAV3E =>
        [
            new("Eon", 275, 0x8B3, 0x1AE),
            new("Mystic", 370, 0x8E0, 0x1DB, 0x13B),
            new("Aurora", 371, 0x8D5, 0x1AF, 0x13A),
            new("SeaMap", 376, 0x8D6, 0x1B0, 0x13C),
        ],
        SAV3FRLG =>
        [
            new("Mystic", 370, 0x84A, 0x2F0, 0x2A8),
            new("Aurora", 371, 0x84B, 0x2F1, 0x2A7),
        ],
        _ => [],
    };
}
