using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using SMSModForge.Model;
using SMSModForge.Tutorials;
using SMSModForge.ViewModel;

namespace SMSModForge.Tests;

/// <summary>
/// Walks a tutorial the way an author would, and says where it stops.
/// <para/>
/// The editor's view model runs perfectly well with no window: nothing here
/// opens one. What each step ASKS for is written out as a solution below, and
/// the walk asserts two things per step — that it is not already satisfied when
/// the author arrives, and that doing what it says satisfies it.
/// <para/>
/// Those are the two ways a tutorial has actually failed. A step that is
/// already true on arrival flicks past and teaches nothing; a step that cannot
/// be satisfied strands whoever is following it, and until now the only way to
/// find either was for a person to sit and click.
/// </summary>
internal sealed class TutorialWalker : IDisposable
{
    public MainViewModel Vm { get; }

    /// <summary>A pack folder of its own, thrown away afterwards. Several steps
    /// only make sense against a pack that exists on disk.</summary>
    public string Root { get; } = Path.Combine(
        Path.GetTempPath(), "SMSModForgeTutorialTest", Guid.NewGuid().ToString("N"));

    public TutorialWalker() : this(new MainViewModel()) { }

    /// <summary>Walk with the view model of a real window, so what the steps do
    /// is drawn - see <see cref="TutorialsOnScreenTests"/>.</summary>
    public TutorialWalker(MainViewModel vm)
    {
        Vm = vm;
        Directory.CreateDirectory(Root);
        // A pack a tutorial makes for the reader goes in the walker's own folder.
        TutorialDef.PracticeFolder = () => Root;
    }

    public void Dispose()
    {
        try { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
        catch (IOException) { /* a temp dir that outlives the run is not a failure */ }
    }

    /// <summary>Save the pack where the walker put it, as the author would.</summary>
    public void SaveToRoot()
    {
        Vm.PackRoot = Root;
        PackRepository.Save(Vm.Pack, Root);
    }

    /// <summary>Put the practice art in the pack, which several steps rely on.</summary>
    public void CopyAssets() => TutorialAssets.EnsureCopied(Root);

    /// <summary>What the tutorials before this one made, brought in as the reader's "Bring in what came before"
    /// does (<see cref="StoryPack.BringIn"/>, 1.10.0) - when the tutorial would offer it.</summary>
    public void BringInStory(TutorialDef tutorial)
    {
        if (StoryPack.Missing(Vm, tutorial).Count > 0) StoryPack.BringIn(Vm, tutorial);
    }

    /// <summary>
    /// Write a file the pack can point at, for a step that needs art present
    /// without caring what it looks like: a real PNG of the size asked for,
    /// clear all over - the dimension checks read its size, and a real window
    /// (<see cref="TutorialsOnScreenTests"/>) has to be able to draw it.
    /// </summary>
    public string WritePng(string rel, int w = 256, int h = 256)
    {
        string abs = Path.Combine(Root, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
        var clear = System.Windows.Media.Imaging.BitmapSource.Create(
            w, h, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, new byte[w * h * 4], w * 4);
        var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
        png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(clear));
        using (var file = File.Create(abs)) png.Save(file);
        return rel;
    }
}

/// <summary>
/// The way through each step: its own <see cref="TutorialStep.Solve"/> since 1.10.0 - the very code "Do it for me"
/// runs in the editor, so a step's way through and its check are proved against each other (a test seam shares the
/// real path). A solution acts through the view model, as the buttons do, and never pokes the step's own check:
/// the point is to reach the state honestly and let the check judge it.
/// </summary>
internal static class TutorialSolutions
{
    public static string Key(string tutorialId, string stepTitle) => tutorialId + " / " + stepTitle;

    /// <summary>
    /// The way through a step: its own <see cref="TutorialStep.Solve"/> - what "Do it for me" does in the editor
    /// (1.10.0), so the tests walk the tutorials through the very code a reader can press - or, for a step not
    /// moved over yet, this table's. False when it has neither.
    /// </summary>
    public static bool TrySolve(string tutorialId, TutorialStep step, TutorialWalker w, TutorialScratch scratch)
    {
        if (step.Solve is { } own)
        {
            own(w.Vm, scratch);
            return true;
        }
        if (!All.TryGetValue(Key(tutorialId, step.Title), out var solve)) return false;
        solve(w);
        return true;
    }

    /// <summary>Whether a step has a way through it at all.</summary>
    public static bool Has(string tutorialId, TutorialStep step) => step.Solve != null || All.ContainsKey(Key(tutorialId, step.Title));

    /// <summary>
    /// Ways through steps that have no <see cref="TutorialStep.Solve"/> of their own. Every tutorial carries its
    /// own since 1.10.0 ("Do it for me"), and what an earlier tutorial leaves is brought in by
    /// <see cref="StoryPack"/>: only the diagnostic walkthrough, which is not for authors, is left here.
    /// </summary>
    public static readonly Dictionary<string, Action<TutorialWalker>> All = new(StringComparer.Ordinal)
    {
        ["smoke / Now do something"] = w => w.Vm.AddCharacterCommand.Execute(null),
    };
}
