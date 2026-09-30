using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SMSModForge.Shared;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Files a pack names have to be inside its folder: players get nothing else
/// (the author, 1.6.3). A path leading outside is shown as such, and a file
/// chosen from outside is offered a copy inside.
/// </summary>
public sealed class PackFolderPathTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "smsmodforge-folder-" + Guid.NewGuid().ToString("N"));
    private readonly string _elsewhere = Path.Combine(Path.GetTempPath(), "smsmodforge-elsewhere-" + Guid.NewGuid().ToString("N"));

    public PackFolderPathTests()
    {
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_elsewhere);
    }

    public void Dispose()
    {
        foreach (var d in new[] { _root, _elsewhere }) { try { Directory.Delete(d, true); } catch (IOException) { } }
    }

    [Theory]
    [InlineData("Art/Elf.png", false)]
    [InlineData("Art/../Elf.png", false)]
    [InlineData("../Elf.png", true)]
    [InlineData("Art/../../Elf.png", true)]
    [InlineData("./Art/Elf.png", false)]
    [InlineData("C:/Art/Elf.png", false)]      // a full path: told apart by IsFullPath instead
    public void APathThatClimbsOutOfThePack(string path, bool leaves)
        => Assert.Equal(leaves, PackPaths.LeavesThePack(path));

    [Fact]
    public void WhereAPathLeads()
    {
        Assert.Equal(OutsideLook.Where.Fine, OutsideLook.Of("Art/Elf.png", _root));
        Assert.Equal(OutsideLook.Where.Outside, OutsideLook.Of("../Elf.png", _root));
        Assert.Equal(OutsideLook.Where.Outside, OutsideLook.Of(Path.Combine(_elsewhere, "Elf.png"), _root));
        Assert.Equal(OutsideLook.Where.FullPathInPack, OutsideLook.Of(Path.Combine(_root, "Art", "Elf.png"), _root));
        // Nothing to say: no pack folder yet, nothing typed, or the game's own art.
        Assert.Equal(OutsideLook.Where.Fine, OutsideLook.Of(Path.Combine(_elsewhere, "Elf.png"), null));
        Assert.Equal(OutsideLook.Where.Fine, OutsideLook.Of("", _root));
        Assert.Equal(OutsideLook.Where.Fine, OutsideLook.Of(GameArt.From("Anna_Default"), _root));
    }

    [Fact]
    public void AFileFromOutsideIsCopiedIn_BesideWhatTheFieldNamed_OrIntoImported()
    {
        string file = Path.Combine(_elsewhere, "Elf.png");
        File.WriteAllBytes(file, new byte[] { 1, 2, 3 });

        Assert.Equal("Imported/Elf.png", PathPickerBox.Destination(_root, file, ""));
        Assert.Equal("Art/Busts/Elf.png", PathPickerBox.Destination(_root, file, "Art/Busts/Old.png"));
        // A field pointing outside already is no guide to where things go.
        Assert.Equal("Imported/Elf.png", PathPickerBox.Destination(_root, file, "../Old.png"));

        string to = PathPickerBox.Destination(_root, file, "");
        Assert.Equal("Imported/Elf.png", PathPickerBox.BringIntoPack(_root, file, to));
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(_root, "Imported", "Elf.png")));

        // The same file again: the copy already there is used, not a second one.
        Assert.Equal("Imported/Elf.png", PathPickerBox.Destination(_root, file, ""));

        // A different file by that name: numbered.
        string other = Path.Combine(_elsewhere, "sub", "Elf.png");
        Directory.CreateDirectory(Path.GetDirectoryName(other)!);
        File.WriteAllBytes(other, new byte[] { 9 });
        Assert.Equal("Imported/Elf (2).png", PathPickerBox.Destination(_root, other, ""));
    }
}

/// <summary>The same, as a field draws it.</summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class PackFolderFieldLookTests
{
    private readonly ITestOutputHelper _out;
    public PackFolderFieldLookTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void AFieldLeadingOutsideThePackIsRed_AndSaysWhy()
    {
        string root = Path.Combine(Path.GetTempPath(), "smsmodforge-fieldlook-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            WindowHarness.Run(_ =>
            {
                var box = new PathPickerBox { PackRoot = root, PathText = "Art/Elf.png" };
                var host = new Window { Content = box, Width = 400, Height = 60, Left = -32000, Top = -32000,
                                        WindowStartupLocation = WindowStartupLocation.Manual, ShowInTaskbar = false };
                host.Show();
                try
                {
                    WindowHarness.Pump();
                    var text = Inner(box);
                    var danger = (Brush)Application.Current.Resources["Theme.Danger"];
                    Assert.NotSame(danger, text.BorderBrush);

                    box.PathText = @"D:\Somewhere\Else\Elf.png";
                    WindowHarness.Pump();
                    _out.WriteLine($"outside: border {text.BorderBrush}, tip '{text.ToolTip}'");
                    Assert.Same(danger, text.BorderBrush);
                    Assert.Equal(SMSModForge.Localization.Loc.T("picker.outside.tip"), text.ToolTip);

                    box.PathText = "../Elf.png";
                    WindowHarness.Pump();
                    Assert.Same(danger, text.BorderBrush);

                    box.PathText = "Art/Elf.png";
                    WindowHarness.Pump();
                    Assert.NotSame(danger, text.BorderBrush);
                }
                finally { host.Close(); }
            });
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    private static TextBox Inner(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is TextBox t) return t;
            try { return Inner(child); } catch (InvalidOperationException) { }
        }
        throw new InvalidOperationException("no text box");
    }
}
