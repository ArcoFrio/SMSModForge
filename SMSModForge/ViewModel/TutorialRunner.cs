using System;
using System.Windows.Threading;
using SMSModForge.Tutorials;
using SMSModForge.Localization;

namespace SMSModForge.ViewModel;

/// <summary>
/// Drives a running tutorial: which step is showing, whether it is satisfied,
/// and moving between steps. Knows nothing about drawing — the overlay watches
/// this and redraws itself.
/// <para/>
/// A <see cref="StepKind.Do"/> step's check is polled rather than hooked to a
/// specific event. The things a step waits on are spread across the whole
/// view-model — a collection gaining an item, a field being filled, a file
/// being chosen — and subscribing to each would tie every step to the internals
/// of whatever it happens to ask for. A quarter-second poll is imperceptible
/// to someone typing, and it means a step's check is just a question about the
/// pack, which is what makes them readable.
/// </summary>
public sealed class TutorialRunner : ObservableObject
{
    private readonly MainViewModel _vm;
    private readonly DispatcherTimer _poll;
    private DateTime _stepShownAt = DateTime.MinValue;

    /// <summary>Per-run baselines. Cleared on every Start, so a second run of
    /// the same tutorial does not begin holding the first run's answers.</summary>
    private readonly TutorialScratch _scratch = new();

    /// <summary>How long a Do step waits before offering its hint.</summary>
    private static readonly TimeSpan HintAfter = TimeSpan.FromSeconds(12);

    public TutorialRunner(MainViewModel vm)
    {
        _vm = vm;
        _poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _poll.Tick += (_, _) => Poll();

        NextCommand = new RelayCommand(Next, () => CanAdvance);
        BackCommand = new RelayCommand(Back, () => Index > 0);
        ExitCommand = new RelayCommand(Stop);
        DoItForMeCommand = new RelayCommand(DoItForMe, () => CanDoItForMe);
        PrepareCommand = new RelayCommand(PrepareStory, () => NeedsPreparing);
        StartNextCommand = new RelayCommand(StartNext, () => NextTutorial != null);
        GoToCommand = new RelayCommand(p => { if (p is int i) GoTo(i); else if (p is StepItem item) GoTo(item.Index); });
    }

    public RelayCommand NextCommand { get; }
    public RelayCommand BackCommand { get; }
    public RelayCommand ExitCommand { get; }

    /// <summary>The step done for the reader (TutorialStep.Solve, 1.10.0).</summary>
    public RelayCommand DoItForMeCommand { get; }

    /// <summary>What the tutorials before this one made, brought in (TutorialDef.Prepare, 1.10.0).</summary>
    public RelayCommand PrepareCommand { get; }

    /// <summary>The tutorial after this one, from its last step (1.10.0).</summary>
    public RelayCommand StartNextCommand { get; }

    /// <summary>Any step, from the step list (parameter: its index or its row).</summary>
    public RelayCommand GoToCommand { get; }

    /// <summary>Which tutorial follows another: given by the catalog's order (MainViewModel).</summary>
    public Func<TutorialDef, TutorialDef?>? FollowingTutorial { get; set; }

    /// <summary>One row of the step list: its number and title, and whether it is done or showing.</summary>
    public sealed record StepItem(int Index, string Title, bool Done, bool Current)
    {
        public string Label => (Index + 1) + ".  " + Title;
        public int Number => Index + 1;
        public string Mark => Current ? "▶" : Done ? "✓" : "";
    }

    /// <summary>The steps of the tutorial, to jump to any of them.</summary>
    public System.Collections.Generic.IReadOnlyList<StepItem> StepItems
    {
        get
        {
            var list = new System.Collections.Generic.List<StepItem>();
            if (_tutorial == null) return list;
            for (int i = 0; i < _tutorial.Steps.Count; i++)
                list.Add(new StepItem(i, _tutorial.Steps[i].Title, _done.Contains(i), i == _index));
            return list;
        }
    }

    /// <summary>The steps seen through (a Read step read, a Do step done), for the step list's ticks.</summary>
    private readonly System.Collections.Generic.HashSet<int> _done = new();

    /// <summary>A Do step with a way through it, not done yet.</summary>
    public bool CanDoItForMe => Step is { Kind: not StepKind.Read, Solve: not null } && !IsSatisfied;

    /// <summary>The tutorial carries on from earlier ones whose work the open pack has not got - offered on its
    /// first step (<see cref="StoryPack"/>).</summary>
    public bool NeedsPreparing
    {
        get
        {
            if (_tutorial == null || _index != 0) return false;
            try { return StoryPack.Missing(_vm, _tutorial).Count > 0; }
            catch { return false; }
        }
    }

    /// <summary>The tutorial after this one, offered on the last step.</summary>
    public TutorialDef? NextTutorial => _tutorial != null && IsLastStep ? FollowingTutorial?.Invoke(_tutorial) : null;

    public string NextTutorialLabel => NextTutorial is { } next ? Loc.F("tutorialOverlay.nextTutorial", "title", next.Title) : "";

    private string _problem = "";
    /// <summary>What went wrong doing a step for the reader, said in the callout (cleared on the next step).</summary>
    public string Problem { get => _problem; private set { if (SetField(ref _problem, value)) OnPropertyChanged(nameof(HasProblem)); } }

    /// <summary>Raised when the step changes, so the overlay can re-anchor and
    /// flash the new target. Carries the step that is now showing.</summary>
    public event Action<TutorialStep>? StepChanged;

    /// <summary>Raised when a tutorial ends, whether finished or exited.</summary>
    public event Action? Ended;

    private TutorialDef? _tutorial;
    public TutorialDef? Tutorial
    {
        get => _tutorial;
        private set { _tutorial = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsRunning)); }
    }

    public bool IsRunning => _tutorial != null;

    private int _index;
    public int Index
    {
        get => _index;
        private set { _index = value; OnPropertyChanged(); RaiseStepProperties(); }
    }

    public TutorialStep? Step =>
        _tutorial != null && _index >= 0 && _index < _tutorial.Steps.Count
            ? _tutorial.Steps[_index] : null;

    /// <summary>"Step 2 of 7" — position is reassuring when the end is not visible.</summary>
    public string Progress =>
        _tutorial == null ? "" : Loc.F("tutorials.progress", "step", _index + 1, "steps", _tutorial.Steps.Count);

    public bool IsLastStep => _tutorial != null && _index == _tutorial.Steps.Count - 1;

    /// <summary>Label on the forward button: the last step finishes rather than
    /// continues. In the editor's language: until 1.9.0 these two were English in
    /// every language, written over the button's own translated text.</summary>
    public string NextLabel => IsLastStep ? Loc.T("tutorialOverlay.finish") : Loc.T("tutorialOverlay.next");

    private bool _satisfied;
    /// <summary>Whether the current step's check passes. Always true for a step
    /// that has no check to fail.</summary>
    public bool IsSatisfied
    {
        get => _satisfied;
        private set
        {
            if (!SetField(ref _satisfied, value)) return;
            OnPropertyChanged(nameof(CanAdvance));
            OnPropertyChanged(nameof(WaitingLabel));
            OnPropertyChanged(nameof(HasWaiting));
            OnPropertyChanged(nameof(CanDoItForMe));
            OnPropertyChanged(nameof(IsDone));
            OnPropertyChanged(nameof(ShowsDone));
            // RelayCommand refreshes off CommandManager.RequerySuggested, which
            // fires on user input — and this change comes from a timer, not a
            // click. Without the nudge the button stays greyed until the author
            // happens to move the mouse.
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }

    /// <summary>Whether the forward button is available.</summary>
    public bool CanAdvance => Step != null && (Step.Kind == StepKind.Read || IsSatisfied);

    /// <summary>Shown instead of the button while a step is waiting on work.</summary>
    public string WaitingLabel =>
        Step is { Kind: not StepKind.Read } && !IsSatisfied ? Loc.T("tutorials.waiting") : "";

    private bool _showHint;
    /// <summary>True once a Do step has gone unsatisfied long enough to warrant
    /// its hint. Nudging immediately would be answering a question nobody asked.</summary>
    public bool ShowHint
    {
        get => _showHint;
        private set { if (SetField(ref _showHint, value)) OnPropertyChanged(nameof(ShowHintBox)); }
    }

    // ── What the coach panel shows (1.10.0) ──────────────────────────────────

    /// <summary>"Tutorial 4 · Places": where this tutorial sits.</summary>
    public string Heading => _tutorial == null ? "" : Loc.F("tutorialCoach.heading", "level", _tutorial.Level, "group", _tutorial.Group);

    /// <summary>How far through the tutorial the step showing is, 0 to 1.</summary>
    public double ProgressFraction => _tutorial == null || _tutorial.Steps.Count == 0 ? 0 : (double)(_index + 1) / _tutorial.Steps.Count;

    /// <summary>What kind of step this is, in a word: to read, to do, or the reader's call.</summary>
    /// <summary>A step that asked for something has it: the coach marks it "Done" (a Read step is never marked -
    /// there was nothing to do).</summary>
    public bool ShowsDone => Step is { Kind: not StepKind.Read } && IsSatisfied;

    public string DoneLabel => "✓ " + Loc.T("tutorialCoach.done");

    public string KindLabel => Step == null ? "" : Loc.T(Step.Kind switch
    {
        StepKind.Read => "tutorialCoach.kind.read",
        StepKind.Do => "tutorialCoach.kind.do",
        _ => "tutorialCoach.kind.free",
    });

    public bool HasMore => Step is { More.Length: > 0 };
    public bool ShowHintBox => ShowHint && Step is { Hint.Length: > 0 };
    public bool HasProblem => Problem.Length > 0;
    public bool HasWaiting => WaitingLabel.Length > 0;
    public bool CanGoBack => _index > 0;

    /// <summary>The step showing is done: a Read step read, a Do step's work there.</summary>
    public bool IsDone => Step != null && (Step.Kind == StepKind.Read || IsSatisfied);

    /// <summary>The tutorial's picture for the coach's header (Resources/TutorialHeaders: the tutorial's own, else
    /// its group's), or null for the theme's own.</summary>
    public System.Windows.Media.ImageSource? HeaderArt => _tutorial == null ? null : HeaderArtFor(_tutorial);

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, System.Windows.Media.ImageSource?> _headerArt = new();

    /// <summary>
    /// Every tutorial's picture decoded on a worker thread as the editor starts (1.10.0): the Tutorials page decoded
    /// them one by one on the UI thread the first time it showed, most of its 700 ms (UiDelayTests). A frozen
    /// picture can be handed across threads; a name asked for before its turn is decoded where it is asked.
    /// </summary>
    internal static void PreloadArt(System.Collections.Generic.IEnumerable<string> names)
    {
        var list = System.Linq.Enumerable.ToList(names);
        System.Threading.Tasks.Task.Run(() =>
        {
            foreach (string n in list)
            {
                try { ArtNamed(n); } catch (Exception) { }
            }
        });
    }

    internal static System.Windows.Media.ImageSource? HeaderArtFor(TutorialDef t)
    {
        foreach (string name in new[] { t.Id, t.GroupKey.Replace("tutorial.group.", "") })
            if (ArtNamed(name) is { } art) return art;
        return null;
    }

    /// <summary>A picture of Resources/TutorialHeaders by its name (.jpg or .png), read once; null when none.</summary>
    internal static System.Windows.Media.ImageSource? ArtNamed(string name)
    {
        {
            if (_headerArt.TryGetValue(name, out var known)) return known;
            System.Windows.Media.ImageSource? found = null;
            foreach (string ext in new[] { ".jpg", ".png" })
            {
                string path = System.IO.Path.Combine(AppContext.BaseDirectory, "Resources", "TutorialHeaders", name + ext);
                if (!System.IO.File.Exists(path)) continue;
                try
                {
                    var bmp = new System.Windows.Media.Imaging.BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bmp.UriSource = new Uri(path);
                    bmp.EndInit();
                    bmp.Freeze();
                    found = bmp;
                }
                catch (Exception) { found = null; }
                break;
            }
            _headerArt.TryAdd(name, found);
            return _headerArt[name];
        }
    }

    public void Start(TutorialDef tutorial) => Start(tutorial, 0);

    /// <summary>Start at a step (a search hit on one, 1.10.0).</summary>
    public void Start(TutorialDef tutorial, int step)
    {
        _scratch.Clear();
        _done.Clear();
        Tutorial = tutorial;
        Index = Math.Clamp(step, 0, Math.Max(0, tutorial.Steps.Count - 1));
        EnterStep();
        _poll.Start();
    }

    /// <summary>Any step: what it needs that is not there yet, "Do it for me" puts there.</summary>
    public void GoTo(int index)
    {
        if (_tutorial == null || index < 0 || index >= _tutorial.Steps.Count || index == _index) return;
        if (IsSatisfied || Step?.Kind == StepKind.Read) _done.Add(_index);
        Index = index;
        EnterStep();
    }

    private void DoItForMe()
    {
        if (Step is not { Solve: { } solve }) return;
        try { solve(_vm, _scratch); }
        catch (Exception ex) { Problem = Loc.F("tutorials.doItFailed", "error", ex.Message); }
        // What it changed is a step of the pack's history like any other edit (Ctrl+Z takes it back).
        _vm.Undo.Checkpoint();
        IsSatisfied = Evaluate();
        OnPropertyChanged(nameof(CanDoItForMe));
        if (Step is TutorialStep s) StepChanged?.Invoke(s);
    }

    private void PrepareStory()
    {
        if (_tutorial == null) return;
        try { StoryPack.BringIn(_vm, _tutorial); }
        catch (Exception ex) { Problem = Loc.F("tutorials.doItFailed", "error", ex.Message); }
        _vm.Undo.Checkpoint();
        OnPropertyChanged(nameof(NeedsPreparing));
        // The pack may be another one now: the step's baselines are taken again from it.
        string problem = Problem;
        EnterStep();
        Problem = problem;
    }

    private void StartNext()
    {
        if (_tutorial == null || NextTutorial is not { } next) return;
        Services.EditorPrefs.MarkTutorialComplete(_tutorial.Id, _tutorial.Revision);
        _poll.Stop();
        Ended?.Invoke();
        Start(next);
    }

    public void Stop()
    {
        _poll.Stop();
        Tutorial = null;
        Index = 0;
        IsSatisfied = false;
        ShowHint = false;
        Ended?.Invoke();
    }

    private void Next()
    {
        if (_tutorial == null) return;
        // Guard here as well as on the button. The overlay calls this command
        // directly from a Click handler, which never consults CanExecute, so a
        // disabled-looking button was still able to advance a gated step.
        if (!CanAdvance) return;
        if (IsLastStep)
        {
            // Reaching the end is what counts as done; Exit is not.
            Services.EditorPrefs.MarkTutorialComplete(_tutorial.Id, _tutorial.Revision);
            Stop();
            return;
        }
        _done.Add(_index);
        Index++;
        EnterStep();
    }

    private void Back()
    {
        if (_tutorial == null || _index == 0) return;
        if (IsSatisfied || Step?.Kind == StepKind.Read) _done.Add(_index);
        Index--;
        EnterStep();
    }

    private void EnterStep()
    {
        _stepShownAt = DateTime.UtcNow;
        Problem = "";
        ShowHint = false;
        // Before the first evaluation: a step that measures a change needs its
        // baseline taken while the step is still unsatisfied.
        if (Step is TutorialStep entering) { try { entering.OnEnter?.Invoke(_vm, _scratch); } catch { } }
        // Evaluate once on arrival: a step can already be satisfied — going
        // Back, or an author who did the work early — and making them redo it
        // to move forward would be nonsense.
        IsSatisfied = Evaluate();
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        if (Step is TutorialStep s)
        {
            StepChanged?.Invoke(s);
            // Once its tab has come up and been laid out (StepChanged switches to it).
            if (s.OnShown is { } shown)
                System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
                {
                    if (!ReferenceEquals(Step, s)) return;
                    try { shown(_vm); } catch { }
                    IsSatisfied = Evaluate();
                }), System.Windows.Threading.DispatcherPriority.ContextIdle);
        }
    }

    private void Poll()
    {
        if (Step is not TutorialStep s) return;
        if (s.Kind != StepKind.Read) IsSatisfied = Evaluate();
        if (!IsSatisfied && s.Hint.Length > 0 && DateTime.UtcNow - _stepShownAt > HintAfter)
            ShowHint = true;
    }

    private bool Evaluate()
    {
        if (Step is not TutorialStep s) return false;
        if (s.IsDone == null) return s.Kind == StepKind.Read;
        try { return s.IsDone(_vm, _scratch); }
        catch { return false; }   // a check that throws is a broken step, not a blocked author
    }

    private void RaiseStepProperties()
    {
        OnPropertyChanged(nameof(Step));
        OnPropertyChanged(nameof(Progress));
        OnPropertyChanged(nameof(IsLastStep));
        OnPropertyChanged(nameof(NextLabel));
        OnPropertyChanged(nameof(CanAdvance));
        OnPropertyChanged(nameof(WaitingLabel));
        OnPropertyChanged(nameof(CanDoItForMe));
        OnPropertyChanged(nameof(NeedsPreparing));
        OnPropertyChanged(nameof(NextTutorial));
        OnPropertyChanged(nameof(NextTutorialLabel));
        OnPropertyChanged(nameof(StepItems));
        OnPropertyChanged(nameof(Heading));
        OnPropertyChanged(nameof(ProgressFraction));
        OnPropertyChanged(nameof(KindLabel));
        OnPropertyChanged(nameof(ShowsDone));
        OnPropertyChanged(nameof(HasMore));
        OnPropertyChanged(nameof(ShowHintBox));
        OnPropertyChanged(nameof(HasWaiting));
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(IsDone));
        OnPropertyChanged(nameof(HeaderArt));
    }
}
