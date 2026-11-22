using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SMSModForge.ViewModel;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        // Whatever edits the pack says so here - and so does much that does not (a selection, a list of
        // choices): undo looks at the pack the next time an edit could have ended only when something was said
        // (Services.UndoService.CheckpointIfTouched, 1.10.0).
        if (TouchesPack(name)) Services.UndoService.Touch();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>Whether a change of <paramref name="name"/> could be an edit of the pack. True unless a view
    /// model knows better: saying so costs undo a look at the pack, not saying so when it was one joins that
    /// edit to the next step.</summary>
    protected virtual bool TouchesPack(string? name) => true;
}
