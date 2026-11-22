using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using SMSModForge.ViewModel;

namespace SMSModForge;

/// <summary>
/// Everything a tab or a page needs, built before it is asked for (1.10.0, the author: "the little delay after
/// clicking a tab or view for the first time and actually seeing it, can we get everything the UI needs to load
/// loaded as soon as the app opens or a modpack is loaded instead?").
/// <para/>
/// Measured (UiDelayTests, the practice pack): a tab's first visit cost up to a second - Dialogues 1064 ms, Places
/// 813, the Tutorials page 708 - and its second visit almost nothing, because the cost is a view's first build: its
/// controls' templates, its lists' rows. So each tab and page the person has not opened yet is laid out once, off
/// screen, while nothing else is happening: one per idle moment, so a click in between is answered at once. Done
/// when the window opens, again when a pack opens (its lists are new), and after a change of look (its templates are).
/// </summary>
public partial class MainWindow
{
    private int _warmRound;

    /// <summary>True once every view has been built for the pack and look now shown (tests wait for it).</summary>
    internal bool WarmedUp { get; private set; }

    /// <summary>The longest one view took to build ahead: the most a click made meanwhile can wait.</summary>
    internal double LongestWarmStepMs { get; private set; }

    private void WarmUpSoon()
    {
        int round = ++_warmRound;
        WarmedUp = false;
        if (System.Environment.GetEnvironmentVariable("SMSMODFORGE_NO_WARMUP") == "1") { WarmedUp = true; return; }
        // The places' art decoded on a worker first - the one the Places tab will show, then the others - so the tab's
        // page, built below at idle, finds it ready rather than decoding 2048-wide pictures on this thread.
        if (DataContext is MainViewModel { PackRoot: { Length: > 0 } root } vm)
        {
            var places = vm.Places.ToList();
            if (vm.SelectedPlace is { } shown) { places.Remove(shown); places.Insert(0, shown); }
            View.Controls.PlacePreview.PrefetchArt(places.SelectMany(p => new[] { p.BaseSprite, p.SecondarySprite })
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => System.IO.Path.Combine(root, s.Replace('/', System.IO.Path.DirectorySeparatorChar))));
        }
        var todo = new Queue<Action>();
        foreach (var item in MainTabs.Items.OfType<TabItem>())
        {
            var tab = item;
            todo.Enqueue(() => WarmTab(tab));
        }
        foreach (var page in new FrameworkElement[] { HomePageTutorials, HomePageGuide, HomePageIssues, HomePageHome })
        {
            var p = page;
            todo.Enqueue(() => WarmPage(p));
        }
        void Next()
        {
            if (round != _warmRound) return;   // a newer round started: it does the work
            if (todo.Count == 0) { WarmedUp = true; return; }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try { todo.Dequeue()(); }
            catch (Exception) { /* warming is a kindness; a view that cannot be built ahead is built when shown */ }
            LongestWarmStepMs = Math.Max(LongestWarmStepMs, sw.Elapsed.TotalMilliseconds);
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, (Action)Next);
        }
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, (Action)Next);
    }

    /// <summary>The size a tab's page is shown at: the tabs' content area now.</summary>
    private Size TabPageSize()
    {
        var host = MainTabs.Template?.FindName("PART_SelectedContentHost", MainTabs) as FrameworkElement;
        double w = host?.ActualWidth > 0 ? host.ActualWidth : MainTabs.ActualWidth;
        double h = host?.ActualHeight > 0 ? host.ActualHeight : MainTabs.ActualHeight;
        return new Size(Math.Max(w, 600), Math.Max(h, 400));
    }

    /// <summary>A tab not on screen, laid out where it will be: its controls' templates applied and its lists' rows
    /// made, so showing it later only places what is already built.</summary>
    private void WarmTab(TabItem tab)
    {
        if (tab.IsSelected || tab.Content is not FrameworkElement page) return;
        if (VisualTreeHelper.GetParent(page) != null) return;   // on screen already
        var size = TabPageSize();
        page.Measure(size);
        page.Arrange(new Rect(size));
    }

    /// <summary>A page of the ModForge tab not shown: laid out hidden, then put back as its binding says.</summary>
    private void WarmPage(FrameworkElement page)
    {
        if (page.Visibility == Visibility.Visible) return;
        page.SetCurrentValue(VisibilityProperty, Visibility.Hidden);
        try
        {
            // The ModForge tab on screen lays it out in place; off screen, its whole page is measured as a tab is.
            var tab = MainTabs.Items.OfType<TabItem>().FirstOrDefault(t => t.Content is DependencyObject root && IsWithin(page, root));
            if (tab is { IsSelected: false, Content: FrameworkElement root } && VisualTreeHelper.GetParent(root) == null)
            {
                var size = TabPageSize();
                root.Measure(size);
                root.Arrange(new Rect(size));
            }
            else page.UpdateLayout();
        }
        finally
        {
            if (BindingOperations.GetBindingExpression(page, VisibilityProperty) is { } binding) binding.UpdateTarget();
            else page.SetCurrentValue(VisibilityProperty, Visibility.Collapsed);
        }
    }

    private static bool IsWithin(DependencyObject child, DependencyObject root)
    {
        for (DependencyObject? d = child; d != null; d = LogicalTreeHelper.GetParent(d) ?? VisualTreeHelper.GetParent(d))
            if (ReferenceEquals(d, root)) return true;
        return false;
    }
}
