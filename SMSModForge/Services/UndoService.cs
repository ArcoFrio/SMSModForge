using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SMSModForge.Model;

namespace SMSModForge.Services;

/// <summary>
/// Undo and redo for the whole editor: steps of the pack as it stood, taken
/// at the moments an edit is finished - a box is left, a command has run, a
/// drag is let go - so every edit anywhere is a step without each edit having
/// to say how it is taken back.
/// <para/>
/// Reworked in 1.10.0 (the author: "ModForge's undo/redo controls are very
/// clunky and non-responsive ... every single action I take within the modpack
/// can be undone and redone, while prioritizing responsiveness and
/// performance"). Until then a step was the whole pack written out as one
/// text, and asking "did anything change?" wrote it out again: measured on a
/// large pack (2,700 lines), 63 ms every time a box was left, 126 ms and more
/// for every button (asked before and after), and 1.1 to 1.4 s to put a step
/// back, every tab built again. Now:
/// <list type="bullet">
/// <item><b>Nothing touched, nothing asked.</b> Whatever edits the pack says so
/// through the editor's view models (<see cref="Touch"/>, from
/// ViewModel.ObservableObject); a box left or a key let go with nothing
/// said since the last step costs nothing (<see cref="CheckpointIfTouched"/>).
/// A place that knows it edited (<see cref="Checkpoint"/>) always looks - a
/// command that has run does, since adding a row to a list says nothing - and
/// so do <see cref="Undo"/> and <see cref="Redo"/> before they move, so an edit
/// nobody announced is still a step of its own before the one before it is
/// undone: never lost, at worst joined to its neighbour.</item>
/// <item><b>A step is the pack in pieces</b> (<see cref="PackPieces"/>), an
/// entry of each list a piece, written out on every core at once and shared
/// between steps: what did not change is kept once, and what a step holds of
/// its own is packed small in the background.</item>
/// <item><b>Putting a step back names what differs</b>
/// (<see cref="Restore"/>): the host puts back those entries and rebuilds what
/// shows them, not the editor. It says how it went with
/// <see cref="AbsorbCurrentAsBaseline"/>, as before.</item>
/// <item><b>A step remembers where the editor was</b> (<see cref="CaptureView"/>):
/// what was picked when the edit was made, and what was picked once it had
/// been - so an undo lands on the record the change was made to, and a redo of
/// something added lands on what was added, wherever the author has been
/// since.</item>
/// </list>
/// </summary>
public sealed class UndoService
{
    /// <summary>A state of the pack in the history, with what the editor was showing as it was left by the
    /// edit after it (<see cref="ViewLeaving"/>) and as it was reached by the edit before it
    /// (<see cref="ViewArriving"/>).</summary>
    private sealed class Step
    {
        public readonly PackPieces Pieces;
        public object? ViewLeaving, ViewArriving;
        public Step(PackPieces pieces) => Pieces = pieces;
    }

    private readonly Func<PackPieces?, PackPieces> _take;
    private Step _baseline = new(PackPieces.Whole(""));
    private readonly Stack<Step> _undo = new();
    private readonly Stack<Step> _redo = new();

    // What has been said to edit the pack, and how much of it the last look had seen.
    private static long _touches;
    private long _seen = -1;

    /// <summary>What the editor was showing before the command now running began, when one is.</summary>
    private object? _viewBefore;
    private bool _hasViewBefore;

    /// <summary>Something may have edited a pack: the next <see cref="CheckpointIfTouched"/> looks. Called for
    /// every change a view model announces, from any thread.</summary>
    public static void Touch() => System.Threading.Interlocked.Increment(ref _touches);

    /// <param name="take">The pack as it is now, sharing the pieces that are the same with the step given.</param>
    public UndoService(Func<PackPieces?, PackPieces> take) => _take = take;

    /// <summary>With a pack that is one text - for tests of the steps themselves.</summary>
    public UndoService(Func<string> serialize) : this(like => PackPieces.WholeLike(serialize(), like)) { }

    /// <summary>
    /// What the editor is showing - what is picked on each tab - as the host wants it remembered with a step
    /// and handed back with it (<see cref="Restore.View"/>). Asked when a step is taken and before a command
    /// runs, so it has to be cheap. Null: no view is kept, and the host keeps whatever is picked when a step
    /// is put back.
    /// </summary>
    public Func<object?>? CaptureView { get; set; }

    /// <summary>What an undo or redo is moving between: the step the editor shows (<see cref="From"/>) and the one
    /// to put back (<see cref="To"/>).</summary>
    public sealed class Restore
    {
        public PackPieces From { get; }
        public PackPieces To { get; }

        /// <summary>What the editor was showing at the step being put back (see
        /// <see cref="CaptureView"/>): where the edit being undone was made, or where the edit being redone
        /// left the editor. Null when none was kept.</summary>
        public object? View { get; }

        internal Restore(PackPieces from, PackPieces to, object? view) { From = from; To = to; View = view; }

        /// <summary>Where the two differ, section by section.</summary>
        public List<PackPieces.SectionChange> Changes => _changes ??= PackPieces.Diff(From, To);
        private List<PackPieces.SectionChange>? _changes;

        /// <summary>The step to put back as one text, for reading the whole pack from.</summary>
        public string ToJson() => To.ToJson();
    }

    /// <summary>Raised by <see cref="Undo"/>/<see cref="Redo"/> with what the host should put back.</summary>
    public event Action<Restore>? RestoreRequested;

    /// <summary>Raised whenever the undo/redo availability changes (for command CanExecute refresh).</summary>
    public event Action? StateChanged;

    public bool CanUndo => _undo.Count > 0;

    /// <summary>
    /// Whether something has been said to edit the pack since it was last looked at: an edit that is not a
    /// step yet. Undo takes it back all the same (it looks first), so Undo is offered while there is one - it
    /// was not, and Ctrl+Z straight after the first tick, list or slider of a session did nothing at all
    /// (1.10.0, found by the walk over every control).
    /// </summary>
    public bool Pending => !Suspended && System.Threading.Interlocked.Read(ref _touches) != _seen;

    /// <summary>Raised as an undo or redo is about to put a step back, and once it has: for the window to keep
    /// the keyboard where it was.</summary>
    public event Action? Moving;
    public event Action? Moved;

    /// <summary>How many steps back are available. For tests that need to see
    /// how many steps an interaction actually produced - one edit that lands as
    /// four is invisible from CanUndo alone.</summary>
    public int Depth => _undo.Count;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>How many steps forward are available - for tests.</summary>
    public int RedoDepth => _redo.Count;

    /// <summary>When true, the checkpoints are no-ops — set during a restore + rebind so focus churn from
    /// rebuilding the UI can't snapshot a half-applied state.</summary>
    public bool Suspended { get; set; }

    /// <summary>How many times the pack has been looked at (written out and compared) - for tests: what a
    /// checkpoint costs is this.</summary>
    public int Looks { get; private set; }

    /// <summary>Clear all history and set the baseline to the current state.
    /// Called after a load / save so undo never crosses pack boundaries.</summary>
    public void Reset()
    {
        _seen = System.Threading.Interlocked.Read(ref _touches);
        _baseline = new Step(_take(null));
        Looks++;
        _undo.Clear();
        _redo.Clear();
        _hasViewBefore = false;
        _viewBefore = null;
        StateChanged?.Invoke();
    }

    /// <summary>
    /// Commit the current state as a new undo step if it differs from the last committed one - looking at the
    /// pack whatever has or has not been said. For a place that knows an edit just ended (a drag let go, a
    /// picker closed, a command run) and for edits that change the pack without a view model saying so.
    /// </summary>
    public void Checkpoint()
    {
        if (Suspended) return;
        Look();
    }

    /// <summary>
    /// As <see cref="Checkpoint"/>, when anything has been said to edit the pack since the last look; nothing at
    /// all otherwise. For the moments that come whether or not anything was edited: a box left, a key or the
    /// mouse let go, a command about to run.
    /// </summary>
    public void CheckpointIfTouched()
    {
        if (Suspended) return;
        if (System.Threading.Interlocked.Read(ref _touches) == _seen) return;
        Look();
    }

    /// <summary>
    /// A command is about to run: what the editor is showing now is where its edit, if it makes one, was made
    /// from - what an undo of it goes back to. (A command that adds a record picks it; one that removes the
    /// record picked picks its neighbour.) Edits made any other way leave what is picked alone, and are
    /// remembered by what is picked once they are found.
    /// </summary>
    public void NoteViewBeforeCommand()
    {
        if (Suspended || CaptureView == null) return;
        _viewBefore = CaptureView();
        _hasViewBefore = true;
    }

    /// <summary>The command has run: what was showing before it is of no more use.</summary>
    public void CommandEnded()
    {
        _hasViewBefore = false;
        _viewBefore = null;
    }

    private void Look()
    {
        // Read before the pack is: what is said while it is being written out is for the next look.
        long said = System.Threading.Interlocked.Read(ref _touches);
        var current = _take(_baseline.Pieces);
        Looks++;
        _seen = said;
        if (current.SameAs(_baseline.Pieces)) return;
        object? view = CaptureView?.Invoke();
        var was = _baseline;
        // What was showing before the command that made this edit, for the one step it is. (Kept through looks
        // that find nothing: a command that asks for a step before it edits would have used it up.)
        was.ViewLeaving = _hasViewBefore ? _viewBefore : view;
        _hasViewBefore = false;
        _viewBefore = null;
        _undo.Push(was);
        _baseline = new Step(current) { ViewArriving = view };
        _redo.Clear();
        Keep(was.Pieces);
        StateChanged?.Invoke();
    }

    public void Undo() => Move(_undo, _redo, back: true);

    public void Redo() => Move(_redo, _undo, back: false);

    private void Move(Stack<Step> from, Stack<Step> onto, bool back)
    {
        // An edit not yet a step is one before anything moves: it is what this press takes back (undo), or what
        // makes going forward meaningless (redo: the steps ahead are gone, as after any edit).
        if (!Suspended) Look();
        if (from.Count == 0) return;
        var leaving = _baseline;
        onto.Push(leaving);
        _baseline = from.Pop();
        Moving?.Invoke();
        try { RestoreRequested?.Invoke(new Restore(leaving.Pieces, _baseline.Pieces, back ? _baseline.ViewLeaving : _baseline.ViewArriving)); }
        finally { Moved?.Invoke(); }
        Keep(leaving.Pieces);
        StateChanged?.Invoke();
    }

    /// <summary>Re-sync the baseline to the actual current serialization after a restore + rebind, so an
    /// idempotent load-time mutation can't trip the next checkpoint.</summary>
    public void AbsorbCurrentAsBaseline()
    {
        long said = System.Threading.Interlocked.Read(ref _touches);
        Absorb(_take(_baseline.Pieces), said);
        Looks++;
    }

    /// <summary>The pack as the host has just taken it (to hold a restore to the step it put back) is where the
    /// editor is: the next look is held against it. <paramref name="saidBefore"/>: <see cref="Said"/> as it was
    /// before the pack was taken.</summary>
    public void Absorb(PackPieces taken, long saidBefore)
    {
        // The same place in the history: what the editor was showing at it stays remembered.
        _baseline = new Step(taken) { ViewLeaving = _baseline.ViewLeaving, ViewArriving = _baseline.ViewArriving };
        _seen = saidBefore;
    }

    /// <summary>Everything said so far has been seen: for the host, once it has put a step back and picked
    /// again what was picked - which says things, and edits nothing.</summary>
    public void SeenAll() => _seen = System.Threading.Interlocked.Read(ref _touches);

    /// <summary>How much has been said to edit a pack so far (see <see cref="Touch"/>).</summary>
    public static long Said => System.Threading.Interlocked.Read(ref _touches);

    /// <summary>The step the editor is at, as last looked at - for the host to check a restore against.</summary>
    public PackPieces Baseline => _baseline.Pieces;

    /// <summary>
    /// Pack small, off this thread, the pieces <paramref name="set"/> holds that the step the editor is at does
    /// not: what only the history reads, and only when a step is put back. Held against the step the editor is
    /// at when the packing runs, not when it was asked for - an undo straight after would otherwise have its
    /// own pieces packed under it.
    /// </summary>
    private void Keep(PackPieces set)
    {
        if (PackOnThisThreadForTests) { PackApart(set); return; }
        Task.Run(() => PackApart(set));
    }

    /// <summary>For tests: packing done before the call returns.</summary>
    internal static bool PackOnThisThreadForTests;

    private void PackApart(PackPieces set)
    {
        var current = System.Threading.Volatile.Read(ref _baseline).Pieces;
        var live = new HashSet<PackPiece>(ReferenceEqualityComparer.Instance);
        // By what holds text: an entry kept in parts shares most of its parts with the step the editor is at.
        foreach (var section in current.Entries)
            foreach (var piece in section)
                foreach (var leaf in piece.Leaves()) live.Add(leaf);
        foreach (var section in set.Entries)
            foreach (var piece in section)
                foreach (var leaf in piece.Leaves())
                    if (!live.Contains(leaf)) leaf.Pack();
    }
}
