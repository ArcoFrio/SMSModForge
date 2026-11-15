using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using SMSModForge.Localization;
using SMSModForge.Services;

namespace SMSModForge.View.Controls;

/// <summary>
/// The live previews' quality, on the preview itself (the author, 1.9.0):
/// "Quality" and one button per preset (Options > Preview quality has the
/// same four), the one in use marked. One setting for every preview, as the
/// menu's is - pressing one here changes all of them, and the menu's tick.
/// <para/>
/// Buttons rather than toggles: a toggle's own IsChecked would be set by the
/// click before the preset changed and would then stop following it. Each
/// button reads <see cref="PreviewQualityDef.IsCurrent"/> instead, so every
/// bar in the window shows the same answer.
/// </summary>
public sealed class PreviewQualityBar : StackPanel
{
    public PreviewQualityBar()
    {
        Orientation = Orientation.Horizontal;
        VerticalAlignment = VerticalAlignment.Center;

        var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), FontSize = 11 };
        LocText.Bind(label, TextBlock.TextProperty, "previewQuality.bar");
        LocText.Bind(label, ToolTipProperty, "previewQuality.bar.tip");
        label.SetResourceReference(TextBlock.ForegroundProperty, "Theme.TextMuted");
        Children.Add(label);

        foreach (var preset in PreviewQualityManager.All)
        {
            var button = new Button
            {
                DataContext = preset,
                Padding = new Thickness(7, 1, 7, 1),
                Margin = new Thickness(0, 0, 2, 0),
                MinWidth = 0,
                FontSize = 11,
                Focusable = false,
                Tag = preset.Name,
            };
            button.SetBinding(ContentControl.ContentProperty, new Binding(nameof(PreviewQualityDef.DisplayName)));
            button.SetBinding(ToolTipProperty, new Binding(nameof(PreviewQualityDef.Description)));
            button.Style = CurrentMarked;
            button.Click += (_, _) => PreviewQualityManager.Apply(preset);
            Children.Add(button);
        }
    }

    /// <summary>The theme's button, with the preset in use drawn in the accent:
    /// its soft fill, the accent's edge, bold. Through the theme's resources,
    /// so Normal and EX each draw it their own way.</summary>
    private static readonly Style CurrentMarked = MakeStyle();

    private static Style MakeStyle()
    {
        var style = new Style(typeof(Button), Application.Current?.TryFindResource(typeof(Button)) as Style);
        var current = new DataTrigger { Binding = new Binding(nameof(PreviewQualityDef.IsCurrent)), Value = true };
        current.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("Theme.AccentSoft")));
        current.Setters.Add(new Setter(Control.BorderBrushProperty, new DynamicResourceExtension("Theme.Accent")));
        current.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.Bold));
        style.Triggers.Add(current);
        return style;
    }

    /// <summary>The preset a bar marks as in use, read off its buttons as
    /// drawn - for tests.</summary>
    internal string Marked()
    {
        foreach (var child in Children)
            if (child is Button { DataContext: PreviewQualityDef def } b && def.IsCurrent && b.FontWeight == FontWeights.Bold)
                return def.Name;
        return "";
    }
}
