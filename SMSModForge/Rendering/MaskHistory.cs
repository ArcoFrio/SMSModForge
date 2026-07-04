using System;
using System.Collections.Generic;

namespace SMSModForge.Rendering;

/// <summary>
/// Per-stroke undo/redo for a <see cref="MaskBuffer"/>. Each snapshot stores
/// the pre-stroke state of one channel AND the alpha plane (since strokes can
/// bump alpha as a side effect). At ~128 KB per slot, depth 30 fits comfortably.
/// </summary>
public sealed class MaskHistory
{
    private const int MaxDepth = 30;

    private readonly LinkedList<(int channel, byte[] data, byte[] alpha)> _undo = new();
    private readonly LinkedList<(int channel, byte[] data, byte[] alpha)> _redo = new();

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>Call once at stroke start, before any pixel is touched.</summary>
    public void Snapshot(int channelIdx, byte[] beforeData, byte[] beforeAlpha)
    {
        _undo.AddLast((channelIdx, (byte[])beforeData.Clone(), (byte[])beforeAlpha.Clone()));
        _redo.Clear();
        while (_undo.Count > MaxDepth) _undo.RemoveFirst();
    }

    public bool Undo(MaskBuffer mask)
    {
        if (_undo.Count == 0) return false;
        var node = _undo.Last!;
        _undo.RemoveLast();
        var (ch, data, alpha) = node.Value;
        var cur = mask.Channel(ch);
        _redo.AddLast((ch, (byte[])cur.Clone(), (byte[])mask.A.Clone()));
        Array.Copy(data, cur, data.Length);
        Array.Copy(alpha, mask.A, alpha.Length);
        return true;
    }

    public bool Redo(MaskBuffer mask)
    {
        if (_redo.Count == 0) return false;
        var node = _redo.Last!;
        _redo.RemoveLast();
        var (ch, data, alpha) = node.Value;
        var cur = mask.Channel(ch);
        _undo.AddLast((ch, (byte[])cur.Clone(), (byte[])mask.A.Clone()));
        Array.Copy(data, cur, data.Length);
        Array.Copy(alpha, mask.A, alpha.Length);
        return true;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}
