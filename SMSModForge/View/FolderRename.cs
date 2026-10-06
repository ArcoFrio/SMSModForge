using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using SMSModForge.ViewModel;

namespace SMSModForge.View;

/// <summary>
/// The box a folder's name turns into while it is renamed in place
/// (<see cref="IRenamableFolder"/>). Set <c>FolderRename.Box="True"</c> on a
/// TextBox in a folder's template, shown while the folder
/// <see cref="IRenamableFolder.IsRenaming"/>.
/// <para/>
/// Enter or leaving the box keeps the name, Escape puts the old one back - the
/// way a file is renamed in Explorer, which is what an author will reach for.
/// </summary>
public static class FolderRename
{
    public static readonly DependencyProperty BoxProperty =
        DependencyProperty.RegisterAttached("Box", typeof(bool), typeof(FolderRename),
            new PropertyMetadata(false, OnBoxChanged));

    public static bool GetBox(DependencyObject d) => (bool)d.GetValue(BoxProperty);
    public static void SetBox(DependencyObject d, bool value) => d.SetValue(BoxProperty, value);

    private static void OnBoxChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox box || e.NewValue is not true) return;
        box.IsVisibleChanged += (_, _) =>
        {
            if (!box.IsVisible || box.DataContext is not IRenamableFolder folder) return;
            box.Text = folder.Name;
            // Once it is on screen and laid out: focusing a box that is still
            // collapsed does nothing.
            box.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
            {
                box.Focus();
                Keyboard.Focus(box);
                box.SelectAll();
            });
        };
        box.PreviewKeyDown += (_, k) =>
        {
            if (k.Key == Key.Enter) { Done(box, keep: true); k.Handled = true; }
            else if (k.Key == Key.Escape) { Done(box, keep: false); k.Handled = true; }
        };
        box.LostKeyboardFocus += (_, _) => Done(box, keep: true);
    }

    private static void Done(TextBox box, bool keep)
    {
        if (box.DataContext is not IRenamableFolder { IsRenaming: true } folder) return;
        folder.IsRenaming = false;
        if (keep && Window.GetWindow(box)?.DataContext is MainViewModel vm)
            vm.RenameFolder(folder, box.Text);
    }
}
