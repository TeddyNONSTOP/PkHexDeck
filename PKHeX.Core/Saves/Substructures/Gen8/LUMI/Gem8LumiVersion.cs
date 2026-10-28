using System;

namespace PKHeX.Core;

/// <summary>
/// Indicates various <see cref="SAV8BSLuminescent"/> revision values for the Luminescent Platinum ROM hack (built on Brilliant Diamond &amp; Shining Pearl).
/// </summary>
/// <remarks>
/// Stored in the same offset-0 header field as <see cref="Gem8Version"/>, but with the upper 16 bits forced to <c>0xFFFF</c>
/// (see <see cref="SAV8BSLuminescent.SaveRevision"/>) so a Luminescent save can never be misread as a same-size vanilla BDSP save.
/// </remarks>
public enum Gem8LumiVersion
{
    /// <remarks><see cref="SaveUtil.SIZE_G8BDSP_1"/></remarks>
    V1_1 = 0x2C, // 44

    /// <remarks><see cref="SaveUtil.SIZE_G8BDSP_3"/></remarks>
    V1_3 = 0x34, // 52

    /// <summary>
    /// Same declared version as <see cref="V1_3"/>, but with an internal layout revision (notably: Pokédex language/form
    /// flags were not extended to the wider species range introduced by this revision).
    /// </summary>
    /// <remarks><see cref="SaveUtil.SIZE_G8BDSP_3"/></remarks>
    V1_3rv1 = 0x0134,
}

public static class Gem8LumiVersionExtensions
{
    /// <summary>
    /// Returns a string to append to the savedata type info, indicating the revision of the player save data.
    /// </summary>
    /// <param name="version">Stored version value in the save data (lower 16 bits only; see <see cref="SAV8BSLuminescent.SaveRevision"/>).</param>
    public static string GetSuffixString(this Gem8LumiVersion version) => version switch
    {
        Gem8LumiVersion.V1_1 => "-1.1.0-Luminescent",
        Gem8LumiVersion.V1_3 => "-1.3.0-Luminescent",
        Gem8LumiVersion.V1_3rv1 => "-1.3.0-Luminescent-rv1",
        _ => throw new ArgumentOutOfRangeException(nameof(version)),
    };
}
