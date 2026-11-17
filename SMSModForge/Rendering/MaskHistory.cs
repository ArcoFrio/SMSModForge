using System;
using System.Collections.Generic;

namespace SMSModForge.Rendering;

/// <summary>
/// Per-stroke undo/redo for a <see cref="MaskBuffer"/>. Each snapshot stores
/// the pre-stroke state of one channel AND the alpha plane (since strokes can
/// bump alpha as a side effect), and that channel as it was before the soften
/// brush (1.9.1), so an edge undone and softened again is softened from where
/// it was. At ~192 KB per slot, depth 30 fits comfortably.
/// </summary>
public sealed class MaskHistory
{
    private const int MaxDepth = 30;

    private readonly record struct Entry(int Channel, byte[] Data, byte[] Alpha, byte[]? Unsoftened);

    private readonly LinkedList<Entry> _undo = new();
    private readonly LinkedList<Entry> _redo = new();

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>Call once at stroke start, before any pixel is touched.</summary>
    public void Snapshot(MaskBuffer mask, int channelIdx)
    {
        _undo.AddLast(Take(mask, channelIdx));
        _redo.Clear();
        while (_undo.Count > MaxDepth) _undo.RemoveFirst();
    }

    public bool Undo(MaskBuffer mask) => Swap(_undo, _redo, mask);

    public bool Redo(MaskBuffer mask) => Swap(_redo, _undo, mask);

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }

    private static Entry Take(MaskBuffer mask, int channelIdx)
        => new(channelIdx, (byte[])mask.Channel(channelIdx).Clone(), (byte[])mask.A.Clone(),
               (byte[]?)mask.Unsoftened(channelIdx)?.Clone());

    /// <summary>The newest state of <paramref name="from"/> back onto the mask,
    /// the mask's state as it was going onto <paramref name="to"/>.</summary>
    private static bool Swap(LinkedList<Entry> from, LinkedList<Entry> to, MaskBuffer mask)
    {
        if (from.Count == 0) return false;
        var entry = from.Last!.Value;
        from.RemoveLast();
        to.AddLast(Take(mask, entry.Channel));
        Array.Copy(entry.Data, mask.Channel(entry.Channel), entry.Data.Length);
        Array.Copy(entry.Alpha, mask.A, entry.Alpha.Length);
        // The entry has left the history, so its copy can be the mask's own.
        mask.SetUnsoftened(entry.Channel, entry.Unsoftened);
        return true;
    }
}
