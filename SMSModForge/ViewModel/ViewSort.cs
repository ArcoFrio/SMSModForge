using System.Collections;
using System.ComponentModel;
using System.Windows.Data;

namespace SMSModForge.ViewModel;

/// <summary>
/// Applies an alphabetical sort to a collection's <em>default view</em> —
/// the layer every <c>ItemsSource="{Binding SomeCollection}"</c> binding
/// actually goes through. The underlying ObservableCollection (and
/// therefore the model lists and the saved JSON order) is untouched;
/// only what the user sees in the left-hand record lists is reordered.
/// <para/>
/// Live sorting is enabled so renaming a record re-slots it immediately
/// rather than on the next reload. Call once per collection instance —
/// the editor's collections are created once and only ever
/// Clear()+Add()ed during rebinds, so the view (and its sort) persists
/// for the app's lifetime.
/// </summary>
public static class ViewSort
{
    public static void Alphabetical(IEnumerable collection, string property)
    {
        var view = CollectionViewSource.GetDefaultView(collection);
        if (view == null) return;
        view.SortDescriptions.Add(new SortDescription(property, ListSortDirection.Ascending));
        if (view is ListCollectionView lcv)
        {
            lcv.IsLiveSorting = true;
            lcv.LiveSortingProperties.Add(property);
        }
    }
}
