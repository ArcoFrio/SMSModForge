using System;
using System.IO;
using SMSModForge.Model;
using SMSModForge.Shared;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The editor and the game move a bust by the same numbers.
/// <para/>
/// They were two copies of six constants — the editor's in
/// <c>JiggleParams</c>'s field initialisers, the runtime's in
/// <c>BustFactory.ApplyJiggle</c>'s per-field fallbacks — agreeing only because
/// nobody had changed one of them. There is one copy now, in
/// <see cref="JiggleDefaults"/>, compiled into both projects, so they cannot
/// drift; what is left to check is that neither end has quietly gone back to
/// writing its own.
/// <para/>
/// This matters more than it did. Where a pack replaces one of the game's busts'
/// MASK, both the preview and the runtime move that bust by the pack's numbers
/// — so these constants are the whole of the agreement between what an author
/// sees and what a player gets.
/// </summary>
public sealed class JiggleDefaultsAgreeTests
{
    private readonly ITestOutputHelper _out;
    public JiggleDefaultsAgreeTests(ITestOutputHelper o) => _out = o;

    private static string? PluginFile(string name) => Beside("SMSModForge.PackPlugin", name);

    private static string? EditorFile(params string[] parts)
        => Beside("SMSModForge", Path.Combine(parts));

    /// <summary>A source file, found by walking up from the test binary.</summary>
    private static string? Beside(string project, string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, project, relative);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    [Fact]
    public void AFreshOutfitStartsOnTheSharedDefaults()
    {
        var fresh = new JiggleParams();
        _out.WriteLine($"speed {fresh.Speed}, strength {fresh.Strength}, "
                       + $"frequency {fresh.Frequency}, noise {fresh.NoiseScale}/"
                       + $"{fresh.NoiseSpeed}/{fresh.NoiseStrength}");

        Assert.Equal(JiggleDefaults.Speed, fresh.Speed);
        Assert.Equal(JiggleDefaults.Strength, fresh.Strength);
        Assert.Equal(JiggleDefaults.Frequency, fresh.Frequency);
        Assert.Equal(JiggleDefaults.NoiseScale, fresh.NoiseScale);
        Assert.Equal(JiggleDefaults.NoiseSpeed, fresh.NoiseSpeed);
        Assert.Equal(JiggleDefaults.NoiseStrength, fresh.NoiseStrength);
        Assert.Equal(JiggleDefaults.PixelSnap, fresh.PixelSnap);
    }

    [Fact]
    public void TheRuntimeFallsBackToTheSameOnes()
    {
        // Read out of the plugin's source because there is no assembly here to
        // ask - it targets a different framework and is not referenced. What is
        // checked is that the fallbacks NAME the shared constants rather than
        // repeating their values, which is the only form that cannot drift.
        string? src = PluginFile("BustFactory.cs");
        if (src == null) { _out.WriteLine("plugin source not found - skipping"); return; }

        string text = File.ReadAllText(src);
        foreach (string field in new[] { "Speed", "Strength", "Frequency",
                                         "NoiseScale", "NoiseSpeed", "NoiseStrength", "PixelSnap" })
            Assert.Contains("JiggleDefaults." + field, text);

        // ...and the literals it used to carry are gone. A fallback left behind
        // beside a shared constant is the drift this is here to stop.
        foreach (string literal in new[] { "?? 3.0f", "?? -0.02f", "?? 4.0f",
                                           "?? 5.0f", "?? 0.06f" })
        {
            _out.WriteLine($"{literal}: {(text.Contains(literal) ? "STILL THERE" : "gone")}");
            Assert.DoesNotContain(literal, text);
        }
    }

    [Fact]
    public void TheGamesOwnBustsAreGivenThePacksJiggle()
    {
        // The runtime half of the alignment, for the busts it covers: those
        // whose MASK the pack replaces. Without it the game would go on moving
        // them by its own numbers and the picture would be a promise the game
        // did not keep.
        string? src = PluginFile("VanillaBustOverrides.cs");
        if (src == null) { _out.WriteLine("plugin source not found - skipping"); return; }

        string text = File.ReadAllText(src);
        Assert.Contains("ApplyJiggle(bust, outfit, logger)", text);
        Assert.Contains("BustFactory.ApplyJiggle(mat,", text);

        // One material per bust, written to by both the mask and the uniforms.
        // Cloning twice would leave the second clone without what the first
        // wrote, and the mask is usually written first.
        Assert.Contains("OwnMaterial(mBase)", text);
        Assert.DoesNotContain("new Material(sr.sharedMaterial)", text);
    }

    [Fact]
    public void OnlyABustWhoseMaskThePackReplacedIsRemoved()
    {
        // The boundary, and the whole reason this is not a sweep of the cast.
        //
        // It was briefly ANY successful replacement, which meant swapping a
        // single mouth frame changed how the whole bust moved - a motion change
        // the author never asked for and could not see coming, since the jiggle
        // sliders are hidden on a borrowed bust. The mask is the jiggle's input,
        // so replacing it is the one edit that says "the motion is mine now".
        string? src = PluginFile("VanillaBustOverrides.cs");
        if (src == null) { _out.WriteLine("plugin source not found - skipping"); return; }

        string text = File.ReadAllText(src);
        int nothingDone = text.IndexOf("if (done == 0) continue;", StringComparison.Ordinal);
        int noMask = text.IndexOf("if (!maskReplaced) continue;", StringComparison.Ordinal);
        int applied = text.IndexOf("ApplyJiggle(bust, outfit, logger)", StringComparison.Ordinal);

        _out.WriteLine($"nothing-done gate {nothingDone}, mask gate {noMask}, jiggle {applied}");

        Assert.True(nothingDone > 0,
                    "nothing stops a bust whose art all failed from being re-jiggled");
        Assert.True(noMask > 0,
                    "nothing narrows the jiggle to the busts whose mask the pack replaced");
        Assert.True(applied > noMask,
                    "the jiggle is applied before the gate that should stop it");

        // ...and the flag is raised in one place only, so no other slot can
        // reach it.
        int raises = 0;
        for (int at = text.IndexOf("maskReplaced = true", StringComparison.Ordinal); at >= 0;
             at = text.IndexOf("maskReplaced = true", at + 1, StringComparison.Ordinal))
            raises++;

        _out.WriteLine($"maskReplaced is set in {raises} place(s)");
        Assert.Equal(1, raises);
        Assert.Contains("SpriteSlotNames.Mask)", text);
    }

    [Fact]
    public void ThePreviewDrawsTheLineInTheSamePlace()
    {
        // Both ends, one rule. The editor is where an author decides whether to
        // replace a mask; if the two disagreed about what that decision means,
        // the preview would be describing a bust the game will not produce.
        string? preview = EditorFile("View", "Controls", "JigglePreview.cs");
        if (preview == null)
        { _out.WriteLine("editor source not found - skipping"); return; }

        string text = File.ReadAllText(preview);
        Assert.Contains("_packReplacedMask", text);
        Assert.Contains("if (!_packReplacedMask && _vanillaJiggle != null) return _vanillaJiggle;",
                        text);
    }
}
