using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SMSModForge.Model;
using SMSModForge.Services;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>Pictures of the start page and the New window, for a person to look at (SMSMODFORGE_SHOT_DIR names
/// the folder; without it nothing runs). Holds nothing.</summary>
[Trait("Speed", "Slow")]
public sealed class StartPagePictures
{
    private static void Save(Visual what, double w, double h, string file)
    {
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(w), (int)Math.Ceiling(h), 96, 96, PixelFormats.Pbgra32);
        bmp.Render(what);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bmp));
        using var f = File.Create(file);
        png.Save(f);
    }

    [Fact]
    public void TakeThem()
    {
        string? dir = Environment.GetEnvironmentVariable("SMSMODFORGE_SHOT_DIR");
        if (string.IsNullOrWhiteSpace(dir)) return;
        Directory.CreateDirectory(dir);
        string theme = Environment.GetEnvironmentVariable("SMSMODFORGE_TEST_THEME") ?? "Light";
        string packs = Path.Combine(Path.GetTempPath(), "smsmodforge-startshots-" + Guid.NewGuid().ToString("N"));
        string here = Path.Combine(packs, "Dummy Days");
        Directory.CreateDirectory(here);
        PackRepository.Save(PackRepository.CreateEmpty("Dummy Days"), here);
        RecentFilesService.Add(Path.Combine(packs, "Old pack on a USB stick"));
        RecentFilesService.Add(here);
        TestMode.StartsWithNoPack = true;
        try
        {
            WindowHarness.Run(window =>
            {
                window.Width = 1280; window.Height = 760;
                window.UpdateLayout();
                WindowHarness.Pump();
                var root = (FrameworkElement)VisualTreeHelper.GetChild(window, 0);
                Save(root, root.ActualWidth, root.ActualHeight, Path.Combine(dir, $"start-{theme}.png"));

                var newWindow = new SMSModForge.View.NewPackWindow { Owner = window, Left = -30000, Top = -30000, WindowStartupLocation = WindowStartupLocation.Manual, ShowInTaskbar = false };
                newWindow.FolderBox.Text = packs;
                newWindow.NameBox.Text = "Dummy Days";
                newWindow.Show();
                WindowHarness.Pump();
                newWindow.UpdateLayout();
                var nroot = (FrameworkElement)VisualTreeHelper.GetChild(newWindow, 0);
                Save(nroot, nroot.ActualWidth, nroot.ActualHeight, Path.Combine(dir, $"new-{theme}.png"));
                newWindow.Close();
            });
        }
        finally
        {
            TestMode.StartsWithNoPack = false;
            try { Directory.Delete(packs, true); } catch (IOException) { }
        }
    }
}
