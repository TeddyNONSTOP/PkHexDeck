using System.Text.Json;

namespace PKHeX.Infrastructure.Bank;

/// <summary>
/// A tiny JSON side-file keyed by box index (box name or wallpaper choice today). This is
/// <b>decoration, never data</b>: a missing or corrupt file just means every box falls back to its
/// default, and a failed write is swallowed rather than propagated — it must never be able to take
/// down a Bank mutation that otherwise succeeded. Uses the same atomic tmp-write-then-rename pattern
/// as the Bank's main index, just without a <c>.bak</c> copy (losing a decoration file costs nothing
/// worth a second recovery path).
/// </summary>
internal sealed class BoxKeyedFile<T>
{
    private readonly string _path;
    private readonly object _gate = new();
    private Dictionary<int, T> _values;

    public BoxKeyedFile(string path)
    {
        _path = path;
        _values = Load(path);
    }

    public T? Get(int box) => _values.TryGetValue(box, out var value) ? value : default;

    public bool TryGet(int box, out T value)
    {
        if (_values.TryGetValue(box, out var found))
        {
            value = found!;
            return true;
        }
        value = default!;
        return false;
    }

    public void Set(int box, T value)
    {
        lock (_gate)
        {
            // Re-read first so another app instance's decoration change is not overwritten.
            _values = Load(_path);
            _values[box] = value;
            TrySave();
        }
    }

    public void Remove(int box)
    {
        lock (_gate)
        {
            _values = Load(_path);
            if (_values.Remove(box))
                TrySave();
        }
    }

    /// <summary>Re-reads from disk, discarding any unsaved in-memory state (there is none — every Set/Remove saves immediately).</summary>
    public void Reload()
    {
        lock (_gate)
            _values = Load(_path);
    }

    private static Dictionary<int, T> Load(string path)
    {
        try
        {
            if (!File.Exists(path))
                return new Dictionary<int, T>();

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<Dictionary<int, T>>(json) ?? new Dictionary<int, T>();
        }
        catch
        {
            // Decoration, never data: a corrupt/unreadable side file just means defaults everywhere.
            return new Dictionary<int, T>();
        }
    }

    private void TrySave()
    {
        try
        {
            var json = JsonSerializer.Serialize(_values);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, _path, overwrite: true);
        }
        catch
        {
            // Best-effort — never let a decoration write failure surface as a Bank error.
        }
    }
}
