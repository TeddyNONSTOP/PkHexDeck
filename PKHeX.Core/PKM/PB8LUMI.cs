using System;

namespace PKHeX.Core;

/// <summary> Generation 8 <see cref="PKM"/> format used by Luminescent Platinum (ROM hack of Brilliant Diamond &amp; Shining Pearl). </summary>
/// <remarks>
/// Identical binary layout to <see cref="PB8"/>; only the Personal Table (wider species range) and format caps differ.
/// </remarks>
public sealed class PB8LUMI : PB8
{
    public override PersonalInfo8BDSP PersonalInfo => PersonalTable.BDSPLUMI.GetFormEntry(Species, Form);
    public override IPermitRecord Permit => PersonalInfo;
    public override EntityContext Context => EntityContext.Gen8bLumi;

    public PB8LUMI() { }
    public PB8LUMI(Memory<byte> data) : base(data) { }
    public override PB8LUMI Clone() => new(Data.ToArray());

    public override ushort MaxMoveID => Legal.MaxMoveID_8b;
    public override ushort MaxSpeciesID => Legal.MaxSpeciesID_9_T0;
    public override int MaxAbilityID => Legal.MaxAbilityID_8b;
    public override int MaxItemID => Legal.MaxItemID_8bLumi;
    public override int MaxBallID => Legal.MaxBallID_8b;
    public override GameVersion MaxGameID => Legal.MaxGameID_HOME;
}
