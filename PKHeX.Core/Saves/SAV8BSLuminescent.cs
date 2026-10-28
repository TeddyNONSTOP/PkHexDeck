using System;
using static System.Buffers.Binary.BinaryPrimitives;

namespace PKHeX.Core;

/// <summary>
/// Generation 8 <see cref="SaveFile"/> object for Luminescent Platinum (ROM hack of Brilliant Diamond &amp; Shining Pearl), <see cref="EntityContext.Gen8bLumi"/> games.
/// </summary>
/// <remarks>
/// All block offsets are identical to <see cref="SAV8BS"/> except <see cref="Zukan"/> and <see cref="BattleTrainer"/>,
/// which use bit/nibble-repacked layouts to fit a far larger roster into the same declared block sizes - see
/// <see cref="Zukan8bLumi"/> and <see cref="BattleTrainerStatus8bLumi"/>. Every unchanged block is inherited as-is
/// from <see cref="SAV8BS"/>'s own constructor; only the two repacked blocks are reconstructed here.
/// </remarks>
public sealed class SAV8BSLuminescent : SAV8BS
{
    public SAV8BSLuminescent() : this(new byte[SaveUtil.SIZE_G8BDSP_3], false) => SaveRevision = (int)Gem8LumiVersion.V1_3rv1;

    public SAV8BSLuminescent(Memory<byte> Raw, bool exportable = true) : base(Raw, exportable)
    {
        // On-disk offsets are unchanged from vanilla BDSP; only the block *types* differ for these two.
        Zukan = new Zukan8bLumi(this, Raw.Slice(0x7A328, 0x30B8));
        BattleTrainer = new BattleTrainerStatus8bLumi(this, Raw.Slice(0x7D3E0, 0x1618));
    }

    // Configuration
    public override PB8LUMI BlankPKM => new();
    public override Type PKMType => typeof(PB8LUMI);

    public override EntityContext Context => EntityContext.Gen8bLumi;
    public override PersonalTable8BDSP Personal => PersonalTable.BDSPLUMI;
    public override ushort MaxSpeciesID => Legal.MaxSpeciesID_9_T0;
    public override int MaxItemID => Legal.MaxItemID_8bLumi;
    public override ReadOnlySpan<ushort> HeldItems => Legal.HeldItems_BSLumi;

    /// <summary>
    /// Stored in the same offset-0 header field as <see cref="SAV8BS.SaveRevision"/>, but a genuine Luminescent save
    /// always has the upper 16 bits set to <c>0xFFFF</c> - this is what disambiguates it from a same-size vanilla
    /// BDSP save during format detection (see <see cref="SaveUtil"/>), since the lower-16-bit revision values
    /// themselves numerically collide with <see cref="Gem8Version"/>.
    /// </summary>
    public new int SaveRevision
    {
        // Matches PKLumiHeX's reverse-engineered header layout verbatim: observed real saves carry 0x0000 in the
        // lower 16 bits for the initial V1_1/V1_3 releases (disambiguated by file size alone), with only the later
        // "rv1" internal revision setting a distinguishing non-zero low half.
        get => ReadUInt16LittleEndian(Data) switch
        {
            0x00 when Data.Length == SaveUtil.SIZE_G8BDSP_1 => (int)Gem8LumiVersion.V1_1,
            0x00 or (int)Gem8LumiVersion.V1_3 when Data.Length == SaveUtil.SIZE_G8BDSP_3 => (int)Gem8LumiVersion.V1_3,
            _ => (int)Gem8LumiVersion.V1_3rv1,
        };
        init => WriteUInt32LittleEndian(Data, (uint)(0xFFFF0000 | value));
    }

    public new string SaveRevisionString => ((Gem8LumiVersion)SaveRevision).GetSuffixString();
    public new bool HasFirstSaveFileExpansion => true; // Every known Luminescent revision is at least equivalent to vanilla's v1.1 structural expansion.
    public new bool HasSecondSaveFileExpansion => (Gem8LumiVersion)SaveRevision >= Gem8LumiVersion.V1_3;

    protected override SAV8BSLuminescent CloneInternal() => new(Data.ToArray());

    protected override PB8LUMI GetPKM(Memory<byte> data) => new(data);

    #region Blocks
    public new Zukan8bLumi Zukan { get; }
    public new BattleTrainerStatus8bLumi BattleTrainer { get; }
    #endregion

    // SAV8BS's own SetDex/GetCaught/GetSeen redirect through its *base-typed* Zukan property, which would silently
    // read/write through the wrong (non-repacked) block if left inherited here - these overrides redirect through
    // the shadowed Lumi-aware Zukan above instead.
    protected override void SetDex(PKM pk) => Zukan.SetDex(pk);
    public override bool GetCaught(ushort species) => Zukan.GetCaught(species);
    public override bool GetSeen(ushort species) => Zukan.GetSeen(species);
}
