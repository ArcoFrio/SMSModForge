using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SMSModForge.Setup;

/// <summary>
/// Taking ModForge off again, from Settings > Apps: the editor's folder, its
/// shortcuts and its Apps entry - and, if asked, its plugin from the game
/// (always, for an install of only the plugin). Packs, the Mods folder,
/// BepInEx and the editor's settings stay.
/// </summary>
public sealed class UninstallWindow : Window
{
    private readonly SetupTexts _t;
    private readonly string _folder;
    private readonly IWindowsPlaces _windows;
    public readonly CheckBox Plugin;
    public readonly Button Go, Cancel;
    private readonly TextBlock _heading, _text;

    public UninstallWindow(SetupTexts texts, string folder, IWindowsPlaces windows)
    {
        _t = texts;
        _folder = folder;
        _windows = windows;
        SetupLook.OwnStyles(this);
        Title = _t.T("setup.uninstall.title");
        Width = 560;
        Height = 300;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = SetupLook.Back;
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;

        // An install of only the plugin has no editor to take away: what it
        // takes is the plugin, and that is not a question.
        bool editor = Manifest.Read(folder)?.HasEditor ?? true;
        _heading = SetupLook.Heading(_t.T("setup.uninstall.heading"));
        _text = SetupLook.Line(editor ? _t.F("setup.uninstall.help", ("folder", folder)) : _t.T("setup.uninstall.pluginOnly"));
        Plugin = SetupLook.Tick(_t.T("setup.uninstall.plugin"), !editor);
        if (!editor) Plugin.Visibility = Visibility.Collapsed;
        Go = SetupLook.Button(_t.T("setup.uninstall.go"), primary: true);
        Cancel = SetupLook.Button(_t.T("setup.cancel"));
        Go.Click += (_, _) => Uninstall();
        Cancel.Click += (_, _) => Close();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        buttons.Children.Add(Go);
        buttons.Children.Add(Cancel);
        var page = new StackPanel { Margin = new Thickness(24, 20, 24, 16) };
        page.Children.Add(_heading);
        page.Children.Add(_text);
        page.Children.Add(Plugin);
        page.Children.Add(buttons);
        Content = page;
    }

    public void Uninstall()
    {
        try
        {
            Uninstaller.Run(_folder, Plugin.IsChecked == true, _windows);
            _heading.Text = _t.T("setup.uninstall.done");
            _text.Text = "";
        }
        catch (Exception e)
        {
            _heading.Text = _t.T("setup.failedHeading");
            _text.Text = _t.F("setup.failed", ("problem", e.Message));
        }
        Plugin.Visibility = Visibility.Collapsed;
        Go.Visibility = Visibility.Collapsed;
        SetupLook.SetText(Cancel, _t.T("setup.close"));
    }
}
