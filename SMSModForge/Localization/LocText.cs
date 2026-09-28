using System.Windows;
using System.Windows.Data;

namespace SMSModForge.Localization;

/// <summary>
/// <c>{l:T key}</c> for code: a property set to a text that follows the
/// language, rather than to the words the language had when the line ran.
/// <para/>
/// For a control that builds itself in code - a tooltip on a splitter, the
/// hint on an empty preview. <c>ToolTip = Loc.T(key)</c> is right until the
/// language changes, and then it is the one word on the screen still in the
/// old one.
/// </summary>
public static class LocText
{
    /// <summary>A binding to the text for <paramref name="key"/>.</summary>
    public static Binding Of(string key)
        => new("[" + key + "]") { Source = LocSource.Instance, Mode = BindingMode.OneWay };

    /// <summary>Set <paramref name="property"/> of <paramref name="target"/> to
    /// the text for <paramref name="key"/>, from now on.</summary>
    public static void Bind(DependencyObject target, DependencyProperty property, string key)
        => BindingOperations.SetBinding(target, property, Of(key));

    /// <summary>
    /// Have <paramref name="element"/> draw itself again whenever the language
    /// changes - for a control that works its words out as it draws (a
    /// preview's "set a sprite to preview", a list of what is under the
    /// pointer), where there is no one property to bind.
    /// <para/>
    /// Only while it is loaded, so a control that has gone is not kept alive
    /// by the language. One on a tab that was not showing at the time catches
    /// up when it next loads.
    /// </summary>
    public static void Follow(FrameworkElement element, System.Action redraw)
    {
        string drawnIn = Loc.Current.Code;
        void Again()
        {
            drawnIn = Loc.Current.Code;
            redraw();
        }
        void Changed()
        {
            if (element.Dispatcher.CheckAccess()) Again();
            else element.Dispatcher.BeginInvoke((System.Action)Again);
        }
        element.Loaded += (_, _) =>
        {
            Loc.Changed -= Changed;
            Loc.Changed += Changed;
            if (drawnIn != Loc.Current.Code) Again();
        };
        element.Unloaded += (_, _) => Loc.Changed -= Changed;
        if (element.IsLoaded) Loc.Changed += Changed;
    }
}
