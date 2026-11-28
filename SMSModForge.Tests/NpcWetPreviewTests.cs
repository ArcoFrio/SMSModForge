using System;
using System.IO;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Media;
using SMSModForge.Model;
using SMSModForge.Services;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// An NPC's own wet settings (after 1.10.0, the author: "I see the wet particle configs on Characters, but I don't see
/// them on NPCs, and setting Enabled and Start Active also doesn't have the preview behaving accordingly"; then "NPC
/// sets them, placements can change"): the NPCs tab's preview draws the NPC's drops with its Wet particles on, faint
/// when they do not start on, none when off - alone and over the room - in the kind it has; a placement shows its
/// NPC's kind until it picks its own, and Reset takes it back to the NPC's. Measured on the picture drawn: the
/// frame's pixels above the NPC (where the drops fall and nothing else moves), against the same frame dry.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class NpcWetPreviewTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-npcwet-" + Guid.NewGuid().ToString("N"));

    public NpcWetPreviewTests(ITestOutputHelper o) => _out = o;

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    /// <summary>What a placement takes from its NPC and what it sets over it: the look the NPC's, a placement's own
    /// value over it; the box and the shower's top and floor the placement's alone.</summary>
    [Fact]
    public void A_placements_wet_effect_is_its_NPCs_with_its_own_values_over_it()
    {
        var npc = new NpcWetDef { Enabled = true, Kind = WetEffectDef.KindShower, Tint = "#FF0000", Amount = 2f, Size = 0.5f };
        var placed = new WetEffectDef { Amount = 0.25f, Width = 4f, Floor = true };
        var merged = WetEffectDef.Merged(npc, placed);
        Assert.Equal(WetEffectDef.KindShower, merged.Kind);
        Assert.Equal("#FF0000", merged.Tint);
        Assert.Equal(0.25f, merged.Amount);
        Assert.Equal(0.5f, merged.Size);
        Assert.Null(merged.Fall);
        Assert.Equal(4f, merged.Width);
        Assert.True(merged.Floor);
        // The placement's kind over the NPC's.
        Assert.Equal(WetEffectDef.KindWet, WetEffectDef.Merged(npc, new WetEffectDef { Kind = WetEffectDef.KindWet }).Kind);
        // Nothing set anywhere: the game's.
        Assert.True(WetEffectDef.Merged(null, null).Empty);
        Assert.True(WetEffectDef.Merged(new NpcWetDef(), null).Empty);
    }

    [Fact]
    public void The_NPCs_tab_preview_draws_its_drops_as_Enabled_and_Start_active_say_and_placements_follow_its_kind()
    {
        FakeSceneCatalog.WritePlain(Path.Combine(_dir, "Npcs", "guard.png"), 200, 400, Color.FromRgb(0x70, 0x60, 0x50));
        var pack = PackRepository.CreateEmpty("npcwet.pack");
        pack.Npcs.Add(new NpcDef { Key = "guard", DisplayName = "Guard", Sprite = "Npcs/guard.png", Wet = new NpcWetDef { Enabled = true } });
        PackRepository.Save(pack, _dir);
        Assert.NotNull(PreviewBackdropManager.All.First(d => !d.IsNone).SceneWaited());
        var wasBackdrop = PreviewBackdropManager.Current;
        try
        {
            WindowHarness.Run(window =>
            {
                var vm = (MainViewModel)window.DataContext;
                vm.OpenPackFromPath(_dir);
                ((TabControl)window.FindName("MainTabs")).SelectedIndex = MainTab.Npcs;
                WindowHarness.Pump();
                vm.SelectedNpc = vm.Npcs.First();
                WindowHarness.Pump();
                var npc = vm.SelectedNpc!;
                var preview = (NpcPreview)window.FindName("NpcPreviewControl");

                // A frame drawn after the change: how many drops, and what is on screen above the NPC (over a backdrop
                // the frame's rows above it, alone the drops' own picture).
                (int drops, byte[]? above) Drawn()
                {
                    int from = preview.FramesShown;
                    Assert.True(WindowHarness.Until(() => preview.FramesShown >= from + 3, TimeSpan.FromSeconds(30)), "the NPC preview drew no frame");
                    if (!preview.OverBackdrop) return (preview.DropsDrawn, preview.DropsAloneShown?.ToArray());
                    var shown = preview.Shown!;
                    int w = shown.PixelWidth, rows = shown.PixelHeight * 30 / 100;   // the NPC is in the middle: its top below
                    var px = new byte[w * rows * 4];
                    shown.CopyPixels(new System.Windows.Int32Rect(0, 0, w, rows), px, w * 4, 0);
                    return (preview.DropsDrawn, px);
                }
                static long Apart(byte[]? a, byte[]? b)
                {
                    if (a == null && b == null) return 0;
                    if (a == null || b == null) return long.MaxValue;
                    long sum = 0;
                    for (int i = 0; i < a.Length; i++) sum += Math.Abs(a[i] - b[i]);
                    return sum;
                }

                foreach (var backdrop in new[] { PreviewBackdropManager.All.First(d => d.IsNone), PreviewBackdropManager.All.First(d => d.Name == "MyRoom") })
                {
                    PreviewBackdropManager.Apply(backdrop);
                    npc.WetEnabled = true; npc.WetStartActive = true;
                    var (wetDrops, wet) = Drawn();
                    npc.WetStartActive = false;
                    var (faintDrops, faint) = Drawn();
                    npc.WetEnabled = false;
                    var (dryDrops, dry) = Drawn();
                    var (_, dryAgain) = Drawn();
                    // Alone there is nothing behind the drops: their own picture against none.
                    byte[]? none = preview.OverBackdrop ? dry : new byte[wet?.Length ?? 0];
                    long wetApart = Apart(wet, none), faintApart = Apart(faint, none), control = Apart(dry, dryAgain);
                    _out.WriteLine($"{backdrop.Name}: {wetDrops} drops, {wetApart} levels from the dry picture; not starting on {faintDrops}, "
                                   + $"{faintApart}; off {dryDrops}; two dry frames {control} apart");
                    Assert.True(wetDrops >= 1 && wetApart > 500, $"{backdrop.Name}: the NPC's wet drops are not on the picture");
                    Assert.Equal(0, dryDrops);
                    Assert.True(control < wetApart / 20, $"control: {backdrop.Name}'s dry frames are as far apart as the wet one is from them");
                    if (!preview.OverBackdrop) Assert.Null(dry);
                    // Not on at the start: there, faint (as the place preview's ghosts).
                    Assert.Equal(wetDrops, faintDrops);
                    Assert.True(faintApart > wetApart / 10 && faintApart < wetApart * 0.7,
                                $"{backdrop.Name}: drops that do not start on are {faintApart} from the dry picture, the wet {wetApart}");

                    // The NPC's kind: a shower's hundreds of fine drops.
                    npc.WetKindIndex = 1;
                    npc.WetEnabled = true; npc.WetStartActive = true;
                    var (showerDrops, _) = Drawn();
                    var showerAt = preview.LastEmitter;
                    npc.WetKindIndex = 0;
                    npc.WetEnabled = false;
                    _out.WriteLine($"{backdrop.Name}: {showerDrops} drops under the NPC's shower");
                    Assert.Equal(Rendering.WetDrops.Kind.Shower, showerAt?.Of);
                    Assert.True(showerDrops > 100, $"{backdrop.Name}: the shower drew {showerDrops} drops");
                }

                // A placement shows its NPC's kind until it picks its own; Reset gives it back the NPC's.
                var placed = new NpcPlacementViewModel(new NpcPlacementDef { Npc = "guard" });
                Assert.Equal(0, placed.WetKindIndex);
                Assert.False(placed.WetShower);
                npc.WetKindIndex = 1;
                Assert.True(placed.WetShower, "a placement with no kind of its own does not show its NPC's shower");
                Assert.Equal(WetEffectDef.ShowerWidth, placed.WetAreaWidth);
                Assert.Null(placed.Model.WetEffect);
                placed.WetKindIndex = 1;
                Assert.False(placed.WetShower);
                Assert.Equal(WetEffectDef.KindWet, placed.Model.WetEffect?.Kind);
                placed.WetAmount = 2f;
                Assert.Equal(2f, placed.EffectiveWet(npc.Model.Wet).Amount);
                placed.ResetWetEffect();
                Assert.Null(placed.Model.WetEffect);
                Assert.True(placed.WetShower);
                npc.WetKindIndex = 0;
            });
        }
        finally { PreviewBackdropManager.Apply(wasBackdrop); }
    }
}
