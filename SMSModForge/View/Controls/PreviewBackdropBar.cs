using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using SMSModForge.Localization;
using SMSModForge.Services;

namespace SMSModForge.View.Controls;

/// <summary>
/// What the bust preview draws a bust over, under the preview itself, at the
/// bottom (1.9.1, the author: "it should still appear in the main tabs but
/// just be maybe at the bottom" - few will change it). "Backdrop" and one
/// button per backdrop, the one in use marked, as the quality bar does; the
/// same setting as Options > Preview backdrop.
/// </summary>
public sealed class PreviewBackdropBar : WrapPanel
{
    public PreviewBackdropBar()
    {
        Orientation = Orientation.Horizontal;
        VerticalAlignment = VerticalAlignment.Center;

        var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), FontSize = 11 };
        LocText.Bind(label, TextBlock.TextProperty, "previewBackdrop.bar");
        LocText.Bind(label, ToolTipProperty, "previewBackdrop.bar.tip");
        label.SetResourceReference(TextBlock.ForegroundProperty, "Theme.TextMuted");
        Children.Add(label);

        foreach (var backdrop in PreviewBackdropManager.All)
        {
            var button = new Button
            {
                DataContext = backdrop,
                Padding = new Thickness(7, 1, 7, 1),
                Margin = new Thickness(0, 0, 2, 0),
                MinWidth = 0,
                FontSize = 11,
                Focusable = false,
                Tag = backdrop.Name,
            };
            button.SetBinding(ContentControl.ContentProperty, new Binding(nameof(PreviewBackdropDef.DisplayName)));
            button.SetBinding(ToolTipProperty, new Binding(nameof(PreviewBackdropDef.Description)));
            button.Style = CurrentMarked;
            button.Click += (_, _) => PreviewBackdropManager.Apply(backdrop);
            Children.Add(button);
        }
    }

    private static readonly Style CurrentMarked = MakeStyle();

    private static Style MakeStyle()
    {
        var style = new Style(typeof(Button), Application.Current?.TryFindResource(typeof(Button)) as Style);
        var current = new DataTrigger { Binding = new Binding(nameof(PreviewBackdropDef.IsCurrent)), Value = true };
        current.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("Theme.AccentSoft")));
        current.Setters.Add(new Setter(Control.BorderBrushProperty, new DynamicResourceExtension("Theme.Accent")));
        current.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.Bold));
        style.Triggers.Add(current);
        return style;
    }

    /// <summary>The backdrop a bar marks as in use, read off its buttons as drawn - for tests.</summary>
    internal string Marked()
    {
        foreach (var child in Children)
            if (child is Button { DataContext: PreviewBackdropDef def } b && def.IsCurrent && b.FontWeight == FontWeights.Bold)
                return def.Name;
        return "";
    }
}
