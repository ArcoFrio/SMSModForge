using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SMSModForge.ViewModel;

namespace SMSModForge.View.Controls;

/// <summary>
/// The tutorial's coach (1.10.0, the author: "change the tutorial UI entirely, to make it look much better and less
/// minimalistic ... different versions for Classic and Ex"): a panel docked to the window's right edge while a
/// tutorial runs, so it never covers the work - the tutorial's picture and progress, the step with its fold-out,
/// the buttons, and every step as a list to go to. It replaced the floating box, which covered the very labels its
/// steps named (found by the review of every step's picture).
/// <para/>
/// Bound to the <see cref="TutorialRunner"/> and drawn by <c>Theme.TutorialCoachTemplate</c>: App.xaml's for Classic,
/// Ex.xaml's for EX - the same bindings and parts, drawn differently. What moves is here: the panel comes in from
/// the edge, a new step rises into place, and the progress fills, each with <see cref="Services.Motion"/>'s timing
/// and only while animations are on.
/// </summary>
public sealed class TutorialCoach : Control
{
    private TutorialRunner? _runner;
    private bool _shown;

    public TutorialCoach()
    {
        SetResourceReference(TemplateProperty, "Theme.TutorialCoachTemplate");
        Focusable = false;
        Visibility = Visibility.Collapsed;
        RenderTransform = new TranslateTransform();
    }

    /// <summary>The runner the coach shows (MainWindow).</summary>
    public void Attach(TutorialRunner runner)
    {
        _runner = runner;
        DataContext = runner;
        runner.StepChanged += _ => OnStep();
        runner.Ended += OnEnded;
        runner.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TutorialRunner.ProgressFraction)) Fill(animate: true);
        };
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        Fill(animate: false);
    }

    private void OnStep()
    {
        bool arriving = !_shown;
        _shown = true;
        Visibility = Visibility.Visible;
        if (arriving) SlideIn();
        else if (GetTemplateChild("PART_Step") is FrameworkElement step) Services.Motion.Enter(step);
        if (GetTemplateChild("PART_More") is Expander more) more.IsExpanded = false;
        Fill(animate: !arriving);
        // The step showing, in view in the list of every step - and then the step's own card, which wins when both
        // cannot be: on a long tutorial's last steps the list scrolled the card's kind and title out of sight
        // (found by the review of every step, 1.10.0).
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (GetTemplateChild("PART_Steps") is ItemsControl list && _runner != null &&
                list.ItemContainerGenerator.ContainerFromIndex(_runner.Index) is FrameworkElement row)
                row.BringIntoView();
            if (GetTemplateChild("PART_Step") is FrameworkElement card) card.BringIntoView();
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void OnEnded()
    {
        _shown = false;
        Visibility = Visibility.Collapsed;
    }

    /// <summary>In from the window's edge as a tutorial starts.</summary>
    private void SlideIn()
    {
        if (!Services.Motion.Enabled) { Opacity = 1; return; }
        var d = new Duration(Services.Motion.Duration + Services.Motion.Duration);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        ((TranslateTransform)RenderTransform).BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(48, 0, d) { EasingFunction = ease });
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, d) { EasingFunction = ease });
    }

    /// <summary>The progress bar's fill to how far through the tutorial the step is.</summary>
    private void Fill(bool animate)
    {
        if (GetTemplateChild("PART_Fill") is not FrameworkElement fill || _runner == null) return;
        if (fill.RenderTransform is not ScaleTransform scale)
        {
            scale = new ScaleTransform(0, 1);
            fill.RenderTransform = scale;
            fill.RenderTransformOrigin = new Point(0, 0.5);
        }
        double to = _runner.ProgressFraction;
        if (!animate || !Services.Motion.Enabled)
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.ScaleX = to;
            return;
        }
        scale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(to, new Duration(Services.Motion.Duration + Services.Motion.Duration + Services.Motion.Duration))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } });
    }
}
