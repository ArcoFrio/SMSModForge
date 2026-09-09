using System;
using System.Windows.Input;

namespace SMSModForge.ViewModel;

public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute == null ? null : new Func<object?, bool>(_ => canExecute())) { }

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    /// <summary>
    /// Fires immediately before any command's <see cref="Execute"/> body runs.
    /// The undo system subscribes to this to snapshot the pre-command state, so
    /// every command-driven mutation (add/remove/toggle across all view-models)
    /// becomes its own undo step without per-command instrumentation.
    /// </summary>
    public static event Action? Executing;

    /// <summary>
    /// Fires immediately AFTER a command's body has run.
    /// <para/>
    /// The undo system needs both edges. <see cref="Executing"/> captures a
    /// field edit that had not been committed yet; this captures the command's
    /// own change. With only the first, the FIRST change of a session left the
    /// undo stack empty - there was nothing before it to push - so Ctrl+Z was
    /// disabled, and the step that would have made it available never happened.
    /// <para/>
    /// Checkpointing is a no-op when nothing changed, so firing on both edges
    /// still leaves exactly one step per command.
    /// </summary>
    public static event Action? Executed;

    public void Execute(object? parameter)
    {
        Executing?.Invoke();
        _execute(parameter);
        Executed?.Invoke();
    }

    public event EventHandler? CanExecuteChanged
    {
        add { CommandManager.RequerySuggested += value; }
        remove { CommandManager.RequerySuggested -= value; }
    }

    public void Raise() => CommandManager.InvalidateRequerySuggested();
}
