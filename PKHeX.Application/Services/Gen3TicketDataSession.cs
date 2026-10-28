using PKHeX.Core;

namespace PKHeX.Application.Services;

public enum Gen3TicketCommitError { None, InventoryConflict }

/// <summary>Stages named and raw flags plus key items, preflighting every affected item slot before writes.</summary>
public sealed class Gen3TicketDataSession
{
    private readonly SAV3 _source;
    private InventoryPouch3 _pouch;
    // PlayerBag3RS/E/FRLG public constructors define these offsets within LargeBlock.Inventory.
    private int KeyItemOffset => _source switch { SAV3RS => 0x118, SAV3E => 0x140, SAV3FRLG => 0x120, _ => throw new NotSupportedException() };
    private (int Id, int Count)[] _originalItems = [];
    private bool[] _originalFlags = [];
    private readonly HashSet<int> _forcedFlags = [];
    private readonly HashSet<int> _requiredTickets = [];
    public SAV3 Staged { get; private set; }
    public IReadOnlyList<Gen3TicketDefinition> Definitions { get; }
    public int FlagCount => Staged.EventFlagCount;
    private Span<byte> SourceInventory => _source switch
    {
        SAV3RS save => save.LargeBlock.Inventory,
        SAV3E save => save.LargeBlock.Inventory,
        SAV3FRLG save => save.LargeBlock.Inventory,
        _ => throw new NotSupportedException(),
    };

    public Gen3TicketDataSession(SAV3 source)
    {
        _source = source; Definitions = Gen3TicketDefinitions.For(source);
        Staged = CloneBuffers(source);
        _pouch = (InventoryPouch3)Staged.Inventory.GetPouch(InventoryType.KeyItems);
        Snapshot();
    }
    private static SAV3 CloneBuffers(SAV3 source)
    {
        // Core's SAV3.Clone serializes the live sector buffers first. Clone semantic buffers
        // directly so opening/resetting this editor never updates live sectors or checksums.
        SAV3 clone = source switch
        {
            SAV3RS => new SAV3RS(source.Japanese), SAV3E => new SAV3E(source.Japanese),
            SAV3FRLG => new SAV3FRLG(source.Japanese), _ => throw new NotSupportedException(),
        };
        source.Small.CopyTo(clone.Small); source.Large.CopyTo(clone.Large); source.Storage.CopyTo(clone.Storage);
        clone.Version = source.Version; clone.Language = source.Language;
        return clone;
    }
    private void Snapshot()
    {
        _originalItems = _pouch.Items.Select(item => (item.Index, item.Count)).ToArray();
        _originalFlags = Enumerable.Range(0, FlagCount).Select(Staged.GetEventFlag).ToArray();
        _forcedFlags.Clear(); _requiredTickets.Clear();
    }
    public void Reset()
    {
        Staged = CloneBuffers(_source);
        _pouch = (InventoryPouch3)Staged.Inventory.GetPouch(InventoryType.KeyItems);
        Snapshot();
    }
    public bool HasTicket(Gen3TicketDefinition definition) => _pouch.Items.Any(item => item.Index == definition.ItemId && item.Count > 0);
    public bool GetFlag(int index) => Staged.GetEventFlag(index);
    public void SetFlag(int index, bool value) => Staged.SetEventFlag(index, value);

    public bool SetTicket(Gen3TicketDefinition definition, bool present)
    {
        if (!Definitions.Contains(definition)) throw new ArgumentException(nameof(definition));
        if (HasTicket(definition) == present) return true;
        int id = definition.ItemId;
        bool originalPresence = _originalItems.Any(item => item.Id == id && item.Count > 0);
        var originalSlots = Enumerable.Range(0, _originalItems.Length).Where(i => _originalItems[i].Id == id).ToArray();
        if (present == originalPresence && originalSlots.All(i => _pouch.Items[i].Index == id || (_pouch.Items[i].Index == 0 && _pouch.Items[i].Count == 0)))
        {
            foreach (var item in _pouch.Items.Where(item => item.Index == id)) { item.Index = 0; item.Count = 0; }
            foreach (int i in originalSlots) { _pouch.Items[i].Index = _originalItems[i].Id; _pouch.Items[i].Count = _originalItems[i].Count; }
            return true;
        }
        if (!present)
        {
            foreach (var item in _pouch.Items.Where(item => item.Index == id)) { item.Index = 0; item.Count = 0; }
            return true;
        }
        var slot = _pouch.Items.FirstOrDefault(item => item.Index == id)
            ?? _pouch.Items.FirstOrDefault(item => item.Index == 0 && item.Count == 0);
        if (slot is null) return false;
        slot.Index = id; slot.Count = 1;
        return true;
    }
    public bool StageTicketAndRoute(Gen3TicketDefinition definition)
    {
        if (!SetTicket(definition, true)) return false;
        SetFlag(definition.TravelFlag, true);
        _forcedFlags.Add(definition.TravelFlag); _requiredTickets.Add(definition.ItemId);
        return true;
    }

    public bool TryCommit(out Gen3TicketCommitError error)
    {
        var current = (InventoryPouch3)_source.Inventory.GetPouch(InventoryType.KeyItems);
        var dirty = Enumerable.Range(0, _pouch.Items.Length).Where(i =>
            (_pouch.Items[i].Index, _pouch.Items[i].Count) != _originalItems[i]
            || _requiredTickets.Contains(_pouch.Items[i].Index)).ToArray();
        foreach (int i in dirty)
        {
            var actual = (current.Items[i].Index, current.Items[i].Count);
            var desired = (_pouch.Items[i].Index, _pouch.Items[i].Count);
            if (actual != _originalItems[i] && actual != desired)
            { error = Gen3TicketCommitError.InventoryConflict; return false; }
        }
        // Core's pouch encoder handles the current Emerald/FRLG count key. Only dirty slot words are copied.
        var encoded = SourceInventory.ToArray();
        _pouch.SecurityKey = _source is SAV3RS ? 0 : _source.SmallBlock.SecurityKey;
        _pouch.SetPouch(encoded);
        bool changed = false;
        foreach (int i in dirty)
        {
            int offset = KeyItemOffset + i * 4;
            var desired = encoded.AsSpan(offset, 4);
            if (desired.SequenceEqual(SourceInventory.Slice(offset, 4))) continue;
            desired.CopyTo(SourceInventory.Slice(offset, 4)); changed = true;
        }
        for (int i = 0; i < FlagCount; i++)
        {
            bool desired = GetFlag(i);
            if (desired == _originalFlags[i] && !_forcedFlags.Contains(i) || _source.GetEventFlag(i) == desired) continue;
            _source.SetEventFlag(i, desired); changed = true;
        }
        if (changed) _source.State.Edited = true;
        error = Gen3TicketCommitError.None; Reset(); return true;
    }
}
