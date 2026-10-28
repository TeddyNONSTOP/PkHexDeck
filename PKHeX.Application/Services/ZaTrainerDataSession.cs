using PKHeX.Core;
using PKHeX.Application.Abstractions;

namespace PKHeX.Application.Services;

/// <summary>Stages trainer fields and cross-block collection actions without touching the live save.</summary>
public sealed class ZaTrainerDataSession
{
    private readonly SAV9ZA _source;
    private Dictionary<uint, byte[]> _original;
    private readonly Stack<SAV9ZA> _undo = new();
    private readonly Dictionary<uint, (uint Width, uint Height)> _imageDependencies = new();
    public SAV9ZA Staged { get; private set; }
    public bool CanUndo => _undo.Count != 0;

    public ZaTrainerDataSession(SAV9ZA source)
    {
        _source = source;
        Staged = (SAV9ZA)source.Clone();
        _original = Snapshot(Staged);
    }

    private static Dictionary<uint, byte[]> Snapshot(SAV9ZA save) =>
        save.AllBlocks.ToDictionary(block => block.Key, block => block.Data.ToArray());

    public void Reset()
    {
        Staged = (SAV9ZA)_source.Clone();
        _original = Snapshot(Staged);
        _undo.Clear();
        _imageDependencies.Clear();
    }

    public void ApplyCollection(bool technicalMachines)
    {
        var before = (SAV9ZA)Staged.Clone();
        try
        {
            if (technicalMachines) TechnicalMachine9a.SetAllTechnicalMachines(Staged, true);
            else ColorfulScrew9a.CollectScrews(Staged);
        }
        catch { Staged = before; throw; }
        if (Staged.AllBlocks.Any(block => !block.Data.SequenceEqual(before.Blocks.GetBlock(block.Key).Data)))
            _undo.Push(before);
    }

    public void Undo()
    {
        if (_undo.TryPop(out var previous)) Staged = previous;
    }

    public void ImportImage(uint dataKey, uint widthKey, uint heightKey, PixelImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        var data = GetImageStorage(dataKey, widthKey, heightKey, image.Width, image.Height);
        var encoded = Dxt1ImageEncoder.Encode(image);
        // Re-importing an unchanged exported PNG must not recompress its original bytes.
        if (DXT1.Decompress(data.Data, image.Width, image.Height).AsSpan().SequenceEqual(image.Bgra)) return;
        ImportCompressedImage(dataKey, widthKey, heightKey, image.Width, image.Height, encoded);
    }

    public void ImportCompressedImage(uint dataKey, uint widthKey, uint heightKey, int width, int height, ReadOnlySpan<byte> encoded)
    {
        var data = GetImageStorage(dataKey, widthKey, heightKey, width, height);
        if (encoded.Length != width * height / 2) throw new ArgumentException("Invalid compressed image length.", nameof(encoded));
        if (data.Data[..encoded.Length].SequenceEqual(encoded)) return;
        _imageDependencies[dataKey] = (widthKey, heightKey);
        _undo.Push((SAV9ZA)Staged.Clone());
        encoded.CopyTo(data.Data); // Preserve unused trailing bytes and original dimensions.
    }

    private SCBlock GetImageStorage(uint dataKey, uint widthKey, uint heightKey, int imageWidth, int imageHeight)
    {
        // Only the three known trainer picture blocks may be changed by this operation.
        bool supported = (dataKey, widthKey, heightKey) switch
        {
            (SaveBlockAccessor9ZA.KPictureCurrentData, SaveBlockAccessor9ZA.KPictureCurrentWidth, SaveBlockAccessor9ZA.KPictureCurrentHeight) => true,
            (SaveBlockAccessor9ZA.KPictureSBCData, SaveBlockAccessor9ZA.KPictureSBCWidth, SaveBlockAccessor9ZA.KPictureSBCHeight) => true,
            (SaveBlockAccessor9ZA.KPictureInitialData, SaveBlockAccessor9ZA.KPictureInitialWidth, SaveBlockAccessor9ZA.KPictureInitialHeight) => true,
            _ => false,
        };
        if (!supported || imageWidth is <= 0 or > 2048 || imageHeight is <= 0 or > 2048 || imageWidth % 4 != 0 || imageHeight % 4 != 0 ||
            !Staged.Blocks.TryGetBlock(dataKey, out var data) || data.Type != SCTypeCode.Object ||
            !Staged.Blocks.TryGetBlock(widthKey, out var width) || width.Type != SCTypeCode.UInt32 ||
            !Staged.Blocks.TryGetBlock(heightKey, out var height) || height.Type != SCTypeCode.UInt32 ||
            (uint)width.GetValue() != imageWidth || (uint)height.GetValue() != imageHeight || data.Data.Length < imageWidth * imageHeight / 2)
            throw new ArgumentException("Unsupported trainer picture metadata or capacity.");
        return data;
    }

    public bool TryCommit()
    {
        foreach (var (dataKey, metadata) in _imageDependencies)
        {
            if (Staged.Blocks.GetBlock(dataKey).Data.SequenceEqual(_original[dataKey])) continue;
            foreach (var key in new[] { metadata.Width, metadata.Height })
                if (!_source.Blocks.TryGetBlock(key, out var live) || live.Type != Staged.Blocks.GetBlock(key).Type ||
                    !live.Data.SequenceEqual(_original[key])) return false;
        }
        var writes = new List<(SCBlock Live, byte[] Data)>();
        foreach (var block in Staged.AllBlocks)
        {
            var original = _original[block.Key];
            if (block.Data.SequenceEqual(original)) continue;
            var live = _source.Blocks.GetBlock(block.Key);
            if (live.Type != block.Type || live.Data.Length != block.Data.Length) return false;
            if (live.Data.SequenceEqual(block.Data)) continue;
            // Treat each changed block as a transaction unit: reject a concurrent
            // update rather than accidentally combine bytes of a scalar/string.
            if (!live.Data.SequenceEqual(original)) return false;
            writes.Add((live, block.Data.ToArray()));
        }
        foreach (var (live, data) in writes) data.CopyTo(live.Data);
        if (writes.Count != 0) _source.State.Edited = true;
        Reset();
        return true;
    }

    public bool TryCommit(Action<SAV9ZA> applyFields)
    {
        var before = (SAV9ZA)Staged.Clone();
        try
        {
            applyFields(Staged);
            if (TryCommit()) return true;
        }
        catch { Staged = before; throw; }
        Staged = before;
        return false;
    }
}
