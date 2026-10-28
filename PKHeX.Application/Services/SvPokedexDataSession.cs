using PKHeX.Core;

namespace PKHeX.Application.Services;

/// <summary>Stages the active Scarlet/Violet Pokédex without writing the retired DLC Paldea block.</summary>
public sealed class SvPokedexDataSession
{
    private readonly TrainerScBlockDataSession<SAV9SV> _session;
    public SAV9SV Staged => _session.Staged;
    public bool UsesDlcFormat => Staged.Blocks.Zukan.GetRevision() != 0;
    public bool CanUndo => _session.CanUndo;
    public ushort MaxSpecies => Staged.MaxSpeciesID;
    public SvPokedexDataSession(SAV9SV source) => _session = new(source);
    public void Reset() => _session.Reset();
    public void Undo() => _session.Undo();
    public void ApplyAction(Action<SAV9SV> action) => _session.ApplyAction(action);
    public bool TryCommit() => _session.TryCommit();

    public IEnumerable<ushort> Species()
    {
        for (ushort species = 1; species <= MaxSpecies; species++)
            if (Staged.Personal.IsSpeciesInGame(species)) yield return species;
    }
    public bool IsFormSupported(ushort species, byte form)
    {
        if (form >= 32 || !Staged.Personal.IsPresentInGame(species, form)) return false;
        return Staged.Personal.GetFormEntry(species, form).DexGroup <= Staged.SaveRevision + 1;
    }
    public bool IsRegionalFormSupported(ushort species, byte form, int region)
    {
        if (!IsFormSupported(species, form)) return false;
        var entry = Staged.Personal.GetFormEntry(species, form);
        return region switch
        {
            1 => entry.DexPaldea != 0,
            2 => Staged.SaveRevision >= 1 && entry.DexKitakami != 0,
            3 => Staged.SaveRevision >= 2 && entry.DexBlueberry != 0,
            _ => false,
        };
    }
    public (ushort Paldea, ushort Kitakami, ushort Blueberry) Identifiers(ushort species)
    {
        ushort paldea = 0, kitakami = 0, blueberry = 0;
        int count = Math.Max(1, (int)Staged.Personal[species].FormCount);
        for (byte form = 0; form < Math.Min(count, 32); form++)
        {
            if (!IsFormSupported(species, form)) continue;
            var entry = Staged.Personal.GetFormEntry(species, form);
            if (entry.DexPaldea != 0) paldea = entry.DexPaldea;
            if (Staged.SaveRevision >= 1 && entry.DexKitakami != 0) kitakami = entry.DexKitakami;
            if (Staged.SaveRevision >= 2 && entry.DexBlueberry != 0) blueberry = entry.DexBlueberry;
        }
        return (paldea, kitakami, blueberry);
    }
    public (bool Obtained, bool Seen, bool Heard, bool Viewed) ReadForm(ushort species, byte form)
    {
        if (UsesDlcFormat)
        {
            var entry = Staged.Blocks.Zukan.DexKitakami.Get(species);
            return (entry.GetObtainedForm(form), entry.GetSeenForm(form), entry.GetHeardForm(form), entry.GetCheckedForm(form));
        }
        var legacy = Staged.Blocks.Zukan.DexPaldea.Get(species);
        return (legacy.IsCaught && legacy.GetIsFormSeen(form), legacy.GetIsFormSeen(form), legacy.IsKnown, !legacy.GetDisplayIsNew());
    }
    public void WriteForm(ushort species, byte form, bool obtained, bool seen, bool heard, bool viewed)
    {
        var old = ReadForm(species, form);
        if (UsesDlcFormat)
        {
            var entry = Staged.Blocks.Zukan.DexKitakami.Get(species);
            if (obtained != old.Obtained) entry.SetObtainedForm(form, obtained);
            if (seen != old.Seen) entry.SetSeenForm(form, seen);
            if (heard != old.Heard) entry.SetHeardForm(form, heard);
            if (viewed != old.Viewed) entry.SetCheckedForm(form, viewed);
        }
        else
        {
            var entry = Staged.Blocks.Zukan.DexPaldea.Get(species);
            if (seen != old.Seen) entry.SetIsFormSeen(form, seen);
        }
    }
    public bool ReadCaught(ushort species) => Staged.Blocks.Zukan.GetCaught(species);
    public (uint Form, int Gender, bool Shiny) ReadDisplay(ushort species, int region)
    {
        if (!UsesDlcFormat)
        {
            var entry = Staged.Blocks.Zukan.DexPaldea.Get(species);
            return (entry.GetDisplayForm(), (int)entry.GetDisplayGender(), entry.GetDisplayIsShiny());
        }
        var dlc = Staged.Blocks.Zukan.DexKitakami.Get(species);
        return region switch
        {
            1 => (dlc.DisplayedPaldeaForm, dlc.DisplayedPaldeaGender, dlc.DisplayedPaldeaShiny != 0),
            2 => (dlc.DisplayedKitakamiForm, dlc.DisplayedKitakamiGender, dlc.DisplayedKitakamiShiny != 0),
            3 => (dlc.DisplayedBlueberryForm, dlc.DisplayedBlueberryGender, dlc.DisplayedBlueberryShiny != 0),
            _ => throw new ArgumentOutOfRangeException(nameof(region)),
        };
    }
    public void WriteDisplay(ushort species, int region, uint form, int gender, bool shiny)
    {
        var old = ReadDisplay(species, region);
        if (form != old.Form && (form > byte.MaxValue || !IsRegionalFormSupported(species, (byte)form, region))) throw new ArgumentOutOfRangeException(nameof(form));
        if (!UsesDlcFormat)
        {
            var entry = Staged.Blocks.Zukan.DexPaldea.Get(species);
            if (form != old.Form) entry.SetDisplayForm(form);
            if (gender != old.Gender) entry.SetDisplayGender(gender);
            if (shiny != old.Shiny) entry.SetDisplayIsShiny(shiny);
            return;
        }
        if (form != old.Form && form > byte.MaxValue || gender != old.Gender && gender is < 0 or > 2) throw new ArgumentOutOfRangeException(nameof(form));
        var dlc = Staged.Blocks.Zukan.DexKitakami.Get(species);
        switch (region)
        {
            case 1:
                if (form != old.Form) dlc.DisplayedPaldeaForm = (byte)form;
                if (gender != old.Gender) dlc.DisplayedPaldeaGender = (byte)gender;
                if (shiny != old.Shiny) dlc.DisplayedPaldeaShiny = shiny ? (byte)1 : (byte)0;
                break;
            case 2:
                if (form != old.Form) dlc.DisplayedKitakamiForm = (byte)form;
                if (gender != old.Gender) dlc.DisplayedKitakamiGender = (byte)gender;
                if (shiny != old.Shiny) dlc.DisplayedKitakamiShiny = shiny ? (byte)1 : (byte)0;
                break;
            case 3:
                if (form != old.Form) dlc.DisplayedBlueberryForm = (byte)form;
                if (gender != old.Gender) dlc.DisplayedBlueberryGender = (byte)gender;
                if (shiny != old.Shiny) dlc.DisplayedBlueberryShiny = shiny ? (byte)1 : (byte)0;
                break;
            default: throw new ArgumentOutOfRangeException(nameof(region));
        }
    }
    public void WriteLegacyCaught(ushort species, bool value)
    {
        if (UsesDlcFormat) throw new InvalidOperationException("Legacy caught state is unavailable for DLC format.");
        var entry = Staged.Blocks.Zukan.DexPaldea.Get(species);
        if (entry.IsCaught != value) entry.SetCaught(value);
    }
}
