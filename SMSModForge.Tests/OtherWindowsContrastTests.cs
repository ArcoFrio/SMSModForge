using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using SMSModForge.Model;
using SMSModForge.Services;
using SMSModForge.View;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Every window the editor opens besides the main one, read in every theme -
/// every enabled word against what is really behind it (ContrastAudit). The
/// contrast checks only ever looked at the main window, so the mask editor's
/// Brush and Eraser went out light on light in every dark theme (the author,
/// after 1.8.0: "Do the contrast tests encompass the mask editor UI?").
/// <para/>
/// Each window is built bare - nothing its caller would fill in - which is
/// still every label, button and box it draws for itself. The browser
/// translator is left out: its page is a web view the audit cannot read.
/// </summary>
[Trait("Speed", "Slow")]   // builds windows in every theme; see CLAUDE.md
public sealed class OtherWindowsContrastTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-windows-" + Guid.NewGuid().ToString("N"));

    public OtherWindowsContrastTests(ITestOutputHelper o)
    {
        _out = o;
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    public static IEnumerable<object[]> Windows()
        => new[] { "ColorPicker", "CustomThemes", "ExportSize", "MaskEditor", "PackTranslations", "SaveConfirm",
                   "SpritePicker", "TextPrompt", "TextReport", "TranslatePack", "UiTextEdit", "Update" }
           .Select(n => new object[] { n });

    /// <summary>Every window of the View namespace but the main one and the
    /// browser translator is in <see cref="Windows"/> - a new window is
    /// measured the day it is added, or this says it is not.</summary>
    [Fact]
    public void Every_window_is_measured()
    {
        var measured = Windows().Select(w => (string)w[0] + "Window").ToHashSet();
        var all = typeof(MaskEditorWindow).Assembly.GetTypes()
            .Where(t => t.Namespace == "SMSModForge.View" && typeof(Window).IsAssignableFrom(t) && !t.IsAbstract)
            .Select(t => t.Name).Where(n => n != "BrowserTranslatorWindow").ToList();
        Assert.Empty(all.Where(n => !measured.Contains(n)));
    }

    private Window Make(string name)
    {
        switch (name)
        {
            case "MaskEditor":
                return new MaskEditorWindow(new OutfitViewModel(new OutfitDef { Key = "o", GameObjectName = "O" }), _dir);
            case "PackTranslations":
                return new PackTranslationsWindow(Pack(), _dir);
            case "TranslatePack":
                return new TranslatePackWindow(Pack(), _dir);
            case "ColorPicker":
            {
                // Opened on a colour, as Pick opens it: its was/now labels sit
                // on that colour, so a dark one is the hard case.
                var picker = new ColorPickerWindow();
                picker.Seed("#202040");
                return picker;
            }
            case "UiTextEdit":
                return (Window)Activator.CreateInstance(typeof(UiTextEditWindow), BindingFlags.NonPublic | BindingFlags.Instance, null,
                                                        new object[] { Array.Empty<UiTextMarks.Found>() }, null)!;
            default:
                var type = typeof(MaskEditorWindow).Assembly.GetType("SMSModForge.View." + name + "Window", throwOnError: true)!;
                return (Window)Activator.CreateInstance(type, nonPublic: true)!;
        }
    }

    private ModPack Pack()
    {
        var pack = PackRepository.CreateEmpty("contrast.pack");
        PackRepository.Save(pack, _dir);
        return pack;
    }

    [Theory]
    [MemberData(nameof(Windows))]
    public void The_window_reads_in_every_theme(string name)
    {
        var failures = new List<string>();
        int words = 0;
        WindowHarness.Run(_ =>
        {
            var was = ThemeManager.Current;
            try
            {
                foreach (var theme in ThemeManager.All)
                {
                    ThemeManager.Apply(theme);
                    var window = Make(name);
                    window.Left = -10000;
                    window.Top = -10000;
                    window.ShowInTaskbar = false;
                    window.ShowActivated = false;
                    window.WindowStartupLocation = WindowStartupLocation.Manual;
                    try
                    {
                        window.Show();
                        WindowHarness.Pump();
                        window.UpdateLayout();
                        WindowHarness.Pump();
                        var found = ContrastAudit.Walk((Visual)window.Content);
                        words += found.Count;
                        foreach (var f in found.Where(f => f.Enabled && f.Ratio < ContrastAudit.Needed(f.Large)))
                            failures.Add($"{theme.Name}: '{f.Text}' {ContrastAudit.Hex(f.Foreground)} on {ContrastAudit.Hex(f.Background)} = {f.Ratio:0.00} ({f.Kind}, {f.Path})");
                    }
                    finally { window.Close(); }
                }
            }
            finally { ThemeManager.Apply(was); }
        });
        _out.WriteLine($"{name}: {words} words measured over {ThemeManager.All.Count} themes");
        foreach (var f in failures.Take(60)) _out.WriteLine(f);
        Assert.True(words > 0, name + " measured nothing - the audit is not reaching it");
        Assert.Empty(failures);
    }
}
