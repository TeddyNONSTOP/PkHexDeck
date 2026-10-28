using System.Buffers.Binary;
using PKHeX.Core;

namespace PKHeX.Application.Models.Events;

public enum ZaEventFieldKind { Boolean, Unsigned64, Signed64 }

/// <summary>Fixed-shape event fields edited on a disposable ZA clone.</summary>
public sealed class ZaEventDataSession
{
    private readonly SAV9ZA _source;
    private readonly SAV9ZA _snapshot;
    public IReadOnlyList<ZaEventCategory> Categories { get; }
    public ZaEventDataSession(SAV9ZA source)
    {
        _source = source;
        var edited = source.State.Edited;
        try { _snapshot = (SAV9ZA)source.Clone(); }
        finally { source.State.Edited = edited; }
        var live = Definitions(source);
        var staged = Definitions(_snapshot);
        Categories = live.Zip(staged).Select(pair => new ZaEventCategory(pair.First.Name,
            FindBlock(source, pair.First.Storage), FindBlock(_snapshot, pair.Second.Storage),
            pair.First.KeyParts, pair.First.Tuple, pair.First.Boolean)).ToArray();
    }
    private static SCBlock FindBlock(SAV9ZA save, IDataIndirect storage) => save.AllBlocks.Single(block => storage.Equals(block.Raw));
    private static (string Name, IDataIndirect Storage, int KeyParts, bool Tuple, bool Boolean)[] Definitions(SAV9ZA save) =>
    [
        ("Flags", save.Blocks.Flags, 1, false, true),
        ("Event", save.Blocks.Event, 1, false, true),
        ("Work", save.Blocks.Work, 1, false, false),
        ("Quest", save.Blocks.Quest, 1, false, false),
        ("WorkMable", save.Blocks.WorkMable, 1, false, false),
        ("CountMable", save.Blocks.CountMable, 1, false, false),
        ("CountTitle", save.Blocks.CountTitle, 1, false, false),
        ("Report", save.Blocks.Report, 2, true, false),
        ("WorkSpawn", save.Blocks.WorkSpawn, 1, false, false),
        ("InfiniteRank", save.Blocks.InfiniteRank, 1, false, false),
        ("Spawner2", save.Blocks.Spawner2, 2, true, false),
        ("Spawner4", save.Blocks.Spawner4, 2, false, false),
        ("Obstruction", save.Blocks.Obstruction, 2, true, false),
        ("FieldItems", save.Blocks.FieldItems, 1, false, true),
        ("FieldObjectInteractable", save.Blocks.FieldObjectInteractable, 3, false, false),
    ];
    public int Commit()
    {
        int count = 0;
        foreach (var category in Categories)
        {
            foreach (var record in category.Records)
            {
                foreach (var field in record.Fields)
                {
                    if (!field.IsChanged) continue;
                    field.CommitToSource(); count++;
                }
            }
        }
        if (count != 0) _source.State.Edited = true;
        return count;
    }
    public void Reset()
    {
        foreach (var category in Categories)
        {
            category.Source.Data.CopyTo(category.Staged.Data);
            foreach (var field in category.Records.SelectMany(record => record.Fields)) field.AcceptChanges();
        }
    }
}
public sealed class ZaEventCategory
{
    public string Name { get; }
    public SCBlock Source { get; }
    public SCBlock Staged { get; }
    public IReadOnlyList<ZaEventRecord> Records { get; }
    public int RecordSize { get; }
    public bool IsValid => Staged.Data.Length % RecordSize == 0;
    public ZaEventCategory(string name, SCBlock source, SCBlock staged, int keyParts, bool tuple, bool boolean)
    {
        Name = name; Source = source; Staged = staged; RecordSize = (keyParts + 1) * 8;
        Records = Enumerable.Range(0, staged.Data.Length / RecordSize)
            .Select(index => new ZaEventRecord(source, staged, index, RecordSize, keyParts, tuple, boolean)).ToArray();
    }
}
public sealed class ZaEventRecord
{
    private readonly SCBlock _block;
    private readonly int _offset;
    private readonly int _hashParts;
    public int Index { get; }
    public IReadOnlyList<ZaEventField> Fields { get; }
    public ulong PrimaryHash => BinaryPrimitives.ReadUInt64LittleEndian(_block.Data[_offset..]);
    public string HashText => string.Join(":", Enumerable.Range(0, _hashParts)
        .Select(part => BinaryPrimitives.ReadUInt64LittleEndian(_block.Data[(_offset + part * 8)..]).ToString("X16")));
    public bool IsEmpty => Enumerable.Range(0, _hashParts).All(part =>
        BinaryPrimitives.ReadUInt64LittleEndian(_block.Data[(_offset + part * 8)..]) == FnvHash.HashEmpty);
    public ZaEventRecord(SCBlock source, SCBlock staged, int index, int recordSize, int keyParts, bool tuple, bool boolean)
    {
        _block = staged; Index = index; _offset = index * recordSize; _hashParts = tuple ? 1 : keyParts;
        var fields = new List<ZaEventField>();
        if (tuple) fields.Add(new ZaEventField(source, staged, _offset + 8, ZaEventFieldKind.Signed64));
        fields.Add(new ZaEventField(source, staged, _offset + keyParts * 8, boolean ? ZaEventFieldKind.Boolean : ZaEventFieldKind.Unsigned64));
        Fields = fields;
    }
}
public sealed class ZaEventField
{
    private readonly SCBlock _source;
    private readonly SCBlock _staged;
    private readonly int _offset;
    private ulong _original;
    public ZaEventFieldKind Kind { get; }
    public ulong RawValue => BinaryPrimitives.ReadUInt64LittleEndian(_staged.Data[_offset..]);
    public bool IsChanged => RawValue != _original;
    public bool BooleanValue => RawValue != 0;
    public long SignedValue => unchecked((long)RawValue);
    public ZaEventField(SCBlock source, SCBlock staged, int offset, ZaEventFieldKind kind)
    { _source = source; _staged = staged; _offset = offset; Kind = kind; _original = RawValue; }
    public void SetRaw(ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(_staged.Data[_offset..], value);
    public void SetBoolean(bool value)
    {
        if (Kind != ZaEventFieldKind.Boolean) throw new InvalidOperationException();
        if (BooleanValue == value) return; // Retain noncanonical true values on no-op edits.
        SetRaw(value ? 1UL : 0UL);
    }
    public void AcceptChanges() => _original = RawValue;
    public void CommitToSource()
    {
        BinaryPrimitives.WriteUInt64LittleEndian(_source.Data[_offset..], RawValue);
        AcceptChanges();
    }
}
