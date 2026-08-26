using System;
using System.Windows.Threading;
using SMSModForge.Tutorials;

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
    }

    public RelayCommand NextCommand { get; }
    public RelayCommand BackCommand { get; }
    public RelayCommand ExitCommand { get; }

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
        _tutorial == null ? "" : $"Step {_index + 1} of {_tutorial.Steps.Count}";

    public bool IsLastStep => _tutorial != null && _index == _tutorial.Steps.Count - 1;

    /// <summary>Label on the forward button: the last step finishes rather than continues.</summary>
    public string NextLabel => IsLastStep ? "Finish" : "Next";

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
        Step is { Kind: not StepKind.Read } && !IsSatisfied ? "Waiting for this step…" : "";

    private bool _showHint;
    /// <summary>True once a Do step has gone unsatisfied long enough to warrant
    /// its hint. Nudging immediately would be answering a question nobody asked.</summary>
    public bool ShowHint
    {
        get => _showHint;
        private set => SetField(ref _showHint, value);
    }

    public void Start(TutorialDef tutorial)
    {
        _scratch.Clear();
        Tutorial = tutorial;
        Index = 0;
        EnterStep();
        _poll.Start();
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
            Services.EditorPrefs.MarkTutorialComplete(_tutorial.Id);
            Stop();
            return;
        }
        Index++;
        EnterStep();
    }

    private void Back()
    {
        if (_tutorial == null || _index == 0) return;
        Index--;
        EnterStep();
    }

    private void EnterStep()
    {
        _stepShownAt = DateTime.UtcNow;
        ShowHint = false;
        // Before the first evaluation: a step that measures a change needs its
        // baseline taken while the step is still unsatisfied.
        if (Step is TutorialStep entering) { try { entering.OnEnter?.Invoke(_vm, _scratch); } catch { } }
        // Evaluate once on arrival: a step can already be satisfied — going
        // Back, or an author who did the work early — and making them redo it
        // to move forward would be nonsense.
        IsSatisfied = Evaluate();
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        if (Step is TutorialStep s) StepChanged?.Invoke(s);
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
    }
}
