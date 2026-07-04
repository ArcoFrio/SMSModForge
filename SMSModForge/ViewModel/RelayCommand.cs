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

    public void Execute(object? parameter)
    {
        Executing?.Invoke();
        _execute(parameter);
    }

    public event EventHandler? CanExecuteChanged
    {
        add { CommandManager.RequerySuggested += value; }
        remove { CommandManager.RequerySuggested -= value; }
    }

    public void Raise() => CommandManager.InvalidateRequerySuggested();
}
