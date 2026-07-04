using System.Windows;
using System.Windows.Controls;
using SMSModForge.ViewModel;

namespace SMSModForge.View;

/// <summary>
/// Picks the leaf vs group <see cref="DataTemplate"/> for a
/// <see cref="NodeConditionViewModel"/>. The group template's inner items
/// reference this same selector instance, which is what makes the condition
/// editor recurse — arbitrarily nested <c>All</c>/<c>Any</c> groups render as
/// nested boxes. Both templates are wired in MainWindow.xaml where the selector
/// is instantiated (same pattern as <see cref="ParamTypeTemplateSelector"/>).
/// </summary>
public sealed class ConditionTemplateSelector : DataTemplateSelector
{
    public DataTemplate? LeafTemplate { get; set; }
    public DataTemplate? GroupTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        if (item is not NodeConditionViewModel vm)
            return base.SelectTemplate(item, container);
        return vm.IsGroup ? GroupTemplate : LeafTemplate;
    }
}
