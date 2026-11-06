using System;
using System.IO;
using System.Linq;
using SMSModForge.Model;
using SMSModForge.Rendering;
using SMSModForge.Services;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The eye in the UI tab's tree: an object out of the way in the preview, and
/// nowhere else (the author, 2026-11-06). What it must never do is change a pack.
/// </summary>
[Collection("UiPreviewHiding")]   // one memory per process
public sealed class UiPreviewHidingTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "smsmf-hiding-" + Guid.NewGuid().ToString("N"));

    public UiPreviewHidingTests(ITestOutputHelper output)
    {
        _out = output;
        Directory.CreateDirectory(_folder);
        UiPreviewHiding.Open(_folder);
        UiPreviewHiding.Reload();
    }

    public void Dispose()
    {
        UiPreviewHiding.Open(null);
        Directory.Delete(_folder, recursive: true);
    }

    private const int W = 800, H = 600;

    private static UiNodeDef Box(string name, float w, float h, string tint, float x = 0f) => new()
    {
        Name = name,
        Rect = new UiRectDef { Size = new[] { w, h }, Position = new[] { x, 0f } },
        Image = new UiImageDef { Sprite = "Semi Rounded", Type = "Sliced", Tint = tint },
    };

    /// <summary>A UI with a big red panel in front of a small blue one.</summary>
    private static UiViewModel Stacked()
    {
        var root = new UiNodeDef
        {
            Name = "Root",
            Rect = new UiRectDef { AnchorMin = new[] { 0f, 0f }, AnchorMax = new[] { 1f, 1f }, Size = new[] { 0f, 0f } },
        };
        root.Children.Add(Box("Behind", 100, 100, "#0000FFFF"));
        var front = Box("Front", 400, 400, "#FF0000FF");
        front.Children.Add(Box("Inside", 50, 50, "#00FF00FF", x: 120f));
        root.Children.Add(front);
        return new UiViewModel(new UiDef { Id = "stacked", Name = "Stacked", Nodes = { root } });
    }

    private static UiNodeViewModel Row(UiViewModel ui, string name)
    {
        UiNodeViewModel? Walk(UiNodeViewModel n) => n.Name == name ? n : n.Children.Select(Walk).FirstOrDefault(f => f != null);
        return ui.Nodes.Select(Walk).First(f => f != null)!;
    }

    private static (byte B, byte G, byte R, byte A) Pixel(UiViewModel ui, int x, int y)
    {
        var pixels = UiAuthoredRenderer.Render(ui.Nodes[0].Model, W, H, VanillaUiLibrary.Assets);
        int i = (y * W + x) * 4;
        return (pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]);
    }

    [Fact]
    public void A_hidden_object_is_not_drawn_nor_anything_inside_it_and_what_is_behind_shows()
    {
        if (!VanillaUiLibrary.IsAvailable) return;
        var ui = Stacked();
        const int middleX = W / 2, middleY = H / 2;

        var before = Pixel(ui, middleX, middleY);
        Assert.Equal(255, before.R);     // the control: the panel in front covers it
        Assert.Equal(0, before.B);
        var inside = Pixel(ui, middleX + 120, middleY);
        Assert.Equal(255, inside.G);

        Row(ui, "Front").IsHiddenInPreview = true;

        var after = Pixel(ui, middleX, middleY);
        _out.WriteLine($"before {before}, after {after}");
        Assert.Equal(255, after.B);      // the one behind it
        Assert.Equal(0, after.R);
        Assert.Equal(0, Pixel(ui, middleX + 120, middleY).A);   // and what was inside it went too
    }

    [Fact]
    public void A_click_goes_through_a_hidden_object_to_what_is_behind_it()
    {
        var ui = Stacked();
        var root = ui.Nodes[0].Model;
        var roots = new[] { root };

        // The control: selecting from the picture gets the one in front.
        Assert.Equal("Front", UiGeometry.Pick(roots, W / 2, H / 2, W, H)!.Name);

        Row(ui, "Front").IsHiddenInPreview = true;
        Assert.Equal("Behind", UiGeometry.Pick(roots, W / 2, H / 2, W, H)!.Name);

        // Where only what was inside it was, nothing that draws is there now.
        var there = UiGeometry.Pick(roots, W / 2 + 120, H / 2, W, H);
        Assert.NotEqual("Inside", there?.Name);
    }

    [Fact]
    public void Hiding_one_of_a_row_moves_nothing_else()
    {
        var root = new UiNodeDef
        {
            Name = "Row",
            Rect = new UiRectDef { Size = new[] { 600f, 100f } },
            Layout = new UiLayoutDef { Kind = UiLayoutKinds.Horizontal, Spacing = new[] { 10f, 0f } },
        };
        root.Children.Add(Box("One", 100, 50, "#FFFFFFFF"));
        root.Children.Add(Box("Two", 100, 50, "#FFFFFFFF"));
        var ui = new UiViewModel(new UiDef { Id = "row", Name = "Row", Nodes = { root } });

        var two = root.Children[1];
        var before = UiGeometry.ScreenRectOf(root, two, W, H)!.Value;
        Row(ui, "One").IsHiddenInPreview = true;
        var after = UiGeometry.ScreenRectOf(root, two, W, H)!.Value;
        Assert.Equal(before, after);

        // The control: switching it off in earnest does close the row up, which
        // is the difference between this and hiding it.
        Row(ui, "One").IsHiddenInPreview = false;
        root.Children[0].StartActive = false;
        Assert.NotEqual(before, UiGeometry.ScreenRectOf(root, two, W, H)!.Value);
    }

    [Fact]
    public void It_never_reaches_the_pack()
    {
        var ui = Stacked();
        var pack = PackRepository.CreateEmpty("hiding.check");
        pack.Uis.Add(ui.Model);

        string Saved()
        {
            PackRepository.Save(pack, _folder);
            return File.ReadAllText(Path.Combine(_folder, "modpack.json"));
        }

        string shown = Saved();
        Row(ui, "Front").IsHiddenInPreview = true;
        Row(ui, "Behind").IsHiddenInPreview = true;
        Assert.Equal(shown, Saved());
        Assert.DoesNotContain("hidden", shown, StringComparison.OrdinalIgnoreCase);

        // The control: a real change does change what is saved.
        Row(ui, "Front").PositionX = 5;
        Assert.NotEqual(shown, Saved());
    }

    [Fact]
    public void Hiding_one_of_the_games_objects_is_not_a_change_to_it()
    {
        if (!VanillaUiLibrary.IsAvailable) return;
        var ui = new UiViewModel(new UiDef { Source = "vanillaui:9_MainCanvas/Quitagme" });
        if (ui.Nodes.Count == 0) return;
        var label = ui.Nodes[0].Children[0];

        label.IsHiddenInPreview = true;
        Assert.False(ui.HasChanges(label.Model));
        Assert.True(label.IsUntouched);

        label.Model.StartActive = !label.Model.StartActive;
        Assert.True(ui.HasChanges(label.Model));    // the control
    }

    [Fact]
    public void What_is_inside_a_hidden_object_says_so_and_comes_back_with_it()
    {
        var ui = Stacked();
        var front = Row(ui, "Front");
        var inside = Row(ui, "Inside");
        Assert.True(inside.ShownInPreview);

        front.IsHiddenInPreview = true;
        Assert.True(inside.IsHiddenByParent);
        Assert.False(inside.IsHiddenInPreview);
        Assert.False(inside.ShownInPreview);
        Assert.NotEqual(front.EyeTip, inside.EyeTip);

        front.IsHiddenInPreview = false;
        Assert.True(inside.ShownInPreview);
    }

    [Fact]
    public void One_that_starts_off_has_no_eye_to_click()
    {
        var ui = Stacked();
        var front = Row(ui, "Front");
        Assert.True(front.ToggleHiddenCommand.CanExecute(null));   // the control

        front.StartActive = false;
        Assert.False(front.ToggleHiddenCommand.CanExecute(null));
        Assert.False(front.ShownInPreview);
        Assert.False(Row(ui, "Inside").ToggleHiddenCommand.CanExecute(null));

        // The top of a tree is drawn whatever its flag says, so it keeps its eye.
        ui.Nodes[0].StartActive = false;
        Assert.True(ui.Nodes[0].ToggleHiddenCommand.CanExecute(null));
    }

    [Fact]
    public void The_editor_remembers_it_for_that_pack_and_no_other()
    {
        var ui = Stacked();
        Row(ui, "Front").IsHiddenInPreview = true;

        // Read back from the file, as the next time the editor runs.
        UiPreviewHiding.Reload();
        var reopened = Stacked();
        Assert.True(Row(reopened, "Front").IsHiddenInPreview);
        Assert.False(Row(reopened, "Behind").IsHiddenInPreview);   // the control
        Assert.True(reopened.HasHiddenInPreview);

        // The same UI in another pack's folder is another matter.
        UiPreviewHiding.Open(Path.Combine(_folder, "another"));
        Assert.False(Row(Stacked(), "Front").IsHiddenInPreview);
    }

    [Fact]
    public void A_rename_keeps_it_and_show_all_clears_it()
    {
        var ui = Stacked();
        var front = Row(ui, "Front");
        Row(ui, "Inside").IsHiddenInPreview = true;
        front.IsHiddenInPreview = true;
        Assert.Contains("2", ui.PreviewHiddenNote);

        front.Name = "Renamed";
        var memory = UiPreviewHiding.Of("stacked");
        Assert.Contains("Root/Renamed", memory);
        Assert.Contains("Root/Renamed/Inside", memory);
        Assert.DoesNotContain("Root/Front", memory);

        ui.ShowAllInPreviewCommand.Execute(null);
        Assert.False(ui.HasHiddenInPreview);
        Assert.False(Row(ui, "Renamed").IsHiddenInPreview);
        Assert.Empty(UiPreviewHiding.Of("stacked"));
    }

    [Fact]
    public void A_copy_of_a_hidden_object_is_on_show()
    {
        var ui = Stacked();
        var front = Row(ui, "Front");
        front.IsHiddenInPreview = true;
        ui.SelectedNode = front;

        ui.DuplicateNodeCommand.Execute(null);
        var copy = ui.SelectedNode!;
        Assert.NotSame(front, copy);
        Assert.False(copy.IsHiddenInPreview);
        Assert.True(front.IsHiddenInPreview);       // the control
    }
}

[CollectionDefinition("UiPreviewHiding", DisableParallelization = true)]
public sealed class UiPreviewHidingCollection { }
