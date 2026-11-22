using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace SMSModForge.View;

/// <summary>The editor with no pack open (1.10.0): New, Open, and the packs opened lately. See StartPage.xaml.</summary>
public partial class StartPage : UserControl
{
    public StartPage()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is INotifyPropertyChanged old) old.PropertyChanged -= Watch;
            if (e.NewValue is INotifyPropertyChanged now) now.PropertyChanged += Watch;
            ShowWhetherAnyRecent();
        };
    }

    private void Watch(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is "RecentPacks" or "") ShowWhetherAnyRecent();
    }

    /// <summary>"Nothing opened yet" in place of an empty list.</summary>
    private void ShowWhetherAnyRecent()
    {
        bool any = DataContext is ViewModel.MainViewModel vm && vm.RecentFiles.Count > 0;
        StartRecentList.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        StartNoRecent.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
    }
}
