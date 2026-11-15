using System;
using System.Collections.Generic;

namespace SMSModForge.Services;

/// <summary>
/// Snapshot-based undo/redo for the whole editor. A "snapshot" is the pack
/// serialized to JSON (via the supplied serializer — the same one the on-disk
/// manifest uses), so every edit anywhere in the editor is captured without
/// instrumenting individual operations.
/// <para/>
/// The host calls <see cref="Checkpoint"/> at natural commit boundaries (a
/// field loses focus, a command runs, just before an undo). Each checkpoint
/// that actually changed the serialized pack becomes one undo step — so a
/// field's keystrokes collapse into a single step committed when focus leaves.
/// <see cref="Undo"/>/<see cref="Redo"/> raise <see cref="RestoreRequested"/>
/// with the snapshot to load; the host deserializes it, rebinds, and calls
/// <see cref="AbsorbCurrentAsBaseline"/> so any idempotent load-time mutation
/// doesn't register as a fresh change.
/// </summary>
public sealed class UndoService
{
    private readonly Func<string> _serialize;
    private string _baseline = "";
    // The steps kept as UTF-8 (1.9.0): each is the whole pack written out, and
    // a long session held hundreds of them at two bytes a character. Strict
    // UTF-8 gives back exactly the text it was given; a text it cannot hold
    // (a lone half of a pair) is kept as it is.
    private readonly Stack<object> _undo = new();
    private readonly Stack<object> _redo = new();
    private static readonly System.Text.UTF8Encoding Utf8 = new(false, throwOnInvalidBytes: true);

    private static object Pack(string text)
    {
        try { return Utf8.GetBytes(text); }
        catch (System.Text.EncoderFallbackException) { return text; }
    }

    private static string Unpack(object kept) => kept is byte[] bytes ? Utf8.GetString(bytes) : (string)kept;

    public UndoService(Func<string> serialize) => _serialize = serialize;

    /// <summary>The snapshot an undo or redo is leaving, while
    /// <see cref="RestoreRequested"/> runs: what the editor shows until the
    /// host puts the other one back - so the host can tell how little changed.</summary>
    public string Leaving { get; private set; } = "";

    /// <summary>Raised by <see cref="Undo"/>/<see cref="Redo"/> with the snapshot
    /// JSON the host should load.</summary>
    public event Action<string>? RestoreRequested;

    /// <summary>Raised whenever the undo/redo availability changes (for command
    /// CanExecute refresh).</summary>
    public event Action? StateChanged;

    public bool CanUndo => _undo.Count > 0;

    /// <summary>How many steps back are available. For tests that need to see
    /// how many steps an interaction actually produced - one edit that lands as
    /// four is invisible from CanUndo alone.</summary>
    public int Depth => _undo.Count;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>When true, <see cref="Checkpoint"/> is a no-op — set during a
    /// restore + rebind so focus churn from rebuilding the UI can't snapshot a
    /// half-applied state.</summary>
    public bool Suspended { get; set; }

    /// <summary>Clear all history and set the baseline to the current state.
    /// Called after a load / save so undo never crosses pack boundaries.</summary>
    public void Reset()
    {
        _baseline = _serialize();
        _undo.Clear();
        _redo.Clear();
        StateChanged?.Invoke();
    }

    /// <summary>Commit the current state as a new undo step if it differs from
    /// the last committed one. No-op when nothing changed.</summary>
    public void Checkpoint()
    {
        if (Suspended) return;
        var current = _serialize();
        if (current == _baseline) return;
        _undo.Push(Pack(_baseline));
        _baseline = current;
        _redo.Clear();
        StateChanged?.Invoke();
    }

    public void Undo()
    {
        if (_undo.Count == 0) return;
        _redo.Push(Pack(_baseline));
        Leaving = _baseline;
        _baseline = Unpack(_undo.Pop());
        RestoreRequested?.Invoke(_baseline);
        StateChanged?.Invoke();
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;
        _undo.Push(Pack(_baseline));
        Leaving = _baseline;
        _baseline = Unpack(_redo.Pop());
        RestoreRequested?.Invoke(_baseline);
        StateChanged?.Invoke();
    }

    /// <summary>Re-sync the baseline to the actual current serialization after a
    /// restore + rebind, so an idempotent load-time mutation can't trip the next
    /// <see cref="Checkpoint"/>.</summary>
    public void AbsorbCurrentAsBaseline() => _baseline = _serialize();
}
