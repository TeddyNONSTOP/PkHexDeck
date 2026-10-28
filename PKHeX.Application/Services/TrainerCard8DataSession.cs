using PKHeX.Core;

namespace PKHeX.Application.Services;

/// <summary>Stages both SWSH teams and commits changed public fields without rewriting unknown bytes.</summary>
public sealed class TrainerCard8DataSession
{
    private readonly SAV8SWSH _source;
    private byte[] _originalCard = [];
    private byte[] _originalTitle = [];
    private bool _copiedCard, _copiedTitle;
    public SAV8SWSH Staged { get; private set; }

    public TrainerCard8DataSession(SAV8SWSH source)
    {
        _source = source;
        Staged = (SAV8SWSH)source.Clone();
        Snapshot();
    }

    public void Reset()
    {
        Staged = (SAV8SWSH)_source.Clone();
        Snapshot();
    }

    private void Snapshot()
    {
        _originalCard = Staged.TrainerCard.Data.ToArray();
        _originalTitle = Staged.TitleScreen.Data.ToArray();
        _copiedCard = false; _copiedTitle = false;
    }

    public void CopyFromParty(bool titleScreen)
    {
        var data = titleScreen ? Staged.TitleScreen.Data : Staged.TrainerCard.Data;
        var before = data.ToArray();
        var party = _source.PartyData;
        if (titleScreen) { Staged.TitleScreen.LoadTeamData(party); _copiedTitle = true; }
        else { Staged.TrainerCard.LoadTeamData(party); _copiedCard = true; }
        // Core clears empty slots and its narrow-ID setters also normalize padding words.
        // Copy From Party owns only the documented Pokémon fields, never unknown words.
        for (int i = 0; i < 6; i++)
        {
            int start = titleScreen ? i * TitleScreen8Poke.SIZE : TrainerCard8.GetPokeOffset(i);
            int size = titleScreen ? TitleScreen8Poke.SIZE : TrainerCard8Poke.SIZE;
            var fields = TeamFields(titleScreen).ToArray();
            if (before[start + 0xC] != 0 && data[start + 0xC] != 0)
                data[start + 0xC] = before[start + 0xC];
            for (int b = 0; b < size; b++)
                if (!fields.Any(field => b >= field.Offset && b < field.Offset + field.Length))
                    data[start + b] = before[start + b];
        }
    }

    public bool Commit()
    {
        var card = Staged.TrainerCard.Data;
        bool rallyChanged = !card.Slice(0x28, 4).SequenceEqual(_originalCard.AsSpan(0x28, 4));
        if (rallyChanged && _source.Blocks.GetBlock(SaveBlockAccessor8SWSH.KRotoRally).Type != SCTypeCode.UInt32)
            throw new InvalidOperationException("The Roto Rally value block has an unsupported type.");
        bool changed = rallyChanged && _source.GetValue<uint>(SaveBlockAccessor8SWSH.KRotoRally) != (uint)Staged.TrainerCard.RotoRallyScore;
        if (changed)
        {
            _source.SetValue(SaveBlockAccessor8SWSH.KRotoRally, (uint)Staged.TrainerCard.RotoRallyScore);
            _source.State.Edited = true;
        }
        changed |= CommitFields(card, _source.TrainerCard.Data, _originalCard, CardFields(), offset => _copiedCard && offset is >= 0xC8 and < 0x170);
        changed |= CommitFields(Staged.TitleScreen.Data, _source.TitleScreen.Data, _originalTitle, TitleFields(), _ => _copiedTitle);
        Reset();
        return changed;
    }

    private bool CommitFields(ReadOnlySpan<byte> staged, Span<byte> source, byte[] original,
        IEnumerable<(int Offset, int Length)> fields, Func<int, bool> force)
    {
        bool changed = false;
        foreach (var (offset, length) in fields)
        {
            if (offset + length > staged.Length || offset + length > source.Length) continue;
            var desired = staged.Slice(offset, length);
            if ((!force(offset) && desired.SequenceEqual(original.AsSpan(offset, length))) || desired.SequenceEqual(source.Slice(offset, length))) continue;
            _source.SetData(source.Slice(offset, length), desired);
            changed = true;
        }
        return changed;
    }

    private static IEnumerable<(int Offset, int Length)> TeamFields(bool title)
    {
        yield return (0, 2); yield return (4, 1); yield return (8, 1); yield return (0xC, 1);
        yield return (0x10, 4); yield return (title ? 0x14 : 0x18, 4);
    }

    private static IEnumerable<(int Offset, int Length)> TitleFields()
    {
        for (int i = 0; i < 6; i++)
            foreach (var field in TeamFields(true)) yield return (i * TitleScreen8Poke.SIZE + field.Offset, field.Length);
    }

    private static IEnumerable<(int Offset, int Length)> CardFields()
    {
        (int, int)[] metadata = [(0, 0x1A), (0x1B, 1), (0x1C, 4), (0x20, 2), (0x22, 2),
            (0x24, 1), (0x25, 1), (0x26, 2), (0x28, 4), (0x2C, 4), (0x30, 1), (0x38, 1),
            (0x39, 3), (0xC0, 8), (0x170, 4), (0x1A8, 4), (0x1B4, 1), (0x1B5, 1)];
        foreach (var field in metadata) yield return field;
        for (int offset = 0x40; offset <= 0xB0; offset += 8) yield return (offset, 8);
        for (int i = 0; i < 6; i++)
            foreach (var field in TeamFields(false)) yield return (TrainerCard8.GetPokeOffset(i) + field.Offset, field.Length);
    }
}
