using System;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SMSModForge.Model;
using SMSModForge.Shared;
using SMSModForge.Validation;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// An animated scene, from the file an author picks to what the manifest says.
/// <para/>
/// The parts are tested separately elsewhere — decoding, timing, reading a
/// container. These check they are actually joined up, which is the thing that
/// silently is not.
/// </summary>
public sealed class AnimatedSceneTests
{
    private readonly ITestOutputHelper _out;
    public AnimatedSceneTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void SavingAPackDecodesEveryGifItsScenesUse()
    {
        using var dir = new Scratch();
        Directory.CreateDirectory(Path.Combine(dir.Path, "Scenes"));
        WriteGif(Path.Combine(dir.Path, "Scenes", "dance.gif"), 6);

        var pack = PackRepository.CreateEmpty("anim.pack");
        pack.Scenes.Add(new SceneDef { Key = "dance", SceneSprite = "Scenes/dance.gif" });
        pack.Scenes.Add(new SceneDef { Key = "still", SceneSprite = "Scenes/plain.png" });

        PackRepository.Save(pack, dir.Path);

        // The GIF's frames are written beside it, ready for the game.
        string frames = Path.Combine(dir.Path, "Scenes", "dance.frames");
        Assert.True(Directory.Exists(frames), "no frames folder was written");
        Assert.Equal(6, Directory.GetFiles(frames, "*.png").Length);
        Assert.True(File.Exists(Path.Combine(frames, MediaKinds.FramesManifest)));

        // And nothing is invented for a still.
        Assert.False(Directory.Exists(Path.Combine(dir.Path, "Scenes", "plain.frames")));

        _out.WriteLine(string.Join(", ", Directory.GetFiles(frames).Select(Path.GetFileName)));
    }

    [Fact]
    public void TheManifestStaysQuietAboutWhatWasNotChosen()
    {
        // Loop and volume are defaults for most scenes, and a still has no use
        // for either. A manifest that wrote them on every scene would be
        // asserting settings nobody picked.
        var pack = PackRepository.CreateEmpty("anim.pack");
        pack.Scenes.Add(new SceneDef { Key = "still", SceneSprite = "a.png" });
        pack.Scenes.Add(new SceneDef { Key = "loops", SceneSprite = "b.gif" });

        string json = PackRepository.SerializeAsSaved(pack);
        Assert.DoesNotContain("\"loop\"", json);
        Assert.DoesNotContain("\"volume\"", json);

        // But a real choice IS written.
        pack.Scenes[1].Loop = false;
        pack.Scenes.Add(new SceneDef { Key = "quiet", SceneSprite = "c.mp4", Volume = 0.25f });

        json = PackRepository.SerializeAsSaved(pack);
        Assert.Contains("\"loop\": false", json);
        Assert.Contains("\"volume\": 0.25", json);
    }

    [Fact]
    public void AStillSceneIsNotTreatedAsAnimated()
    {
        // The control for the whole feature: the old behaviour has to be
        // exactly the old behaviour.
        var still = new SceneDef { Key = "s", SceneSprite = "Scenes/a.png" };
        Assert.False(still.IsAnimated);
        Assert.False(still.IsVideo);

        var gif = new SceneDef { Key = "g", SceneSprite = "Scenes/a.gif" };
        Assert.True(gif.IsAnimated);
        Assert.False(gif.IsVideo);      // a GIF cannot carry sound

        var video = new SceneDef { Key = "v", SceneSprite = "Scenes/a.mp4" };
        Assert.True(video.IsAnimated);
        Assert.True(video.IsVideo);
    }

    [Fact]
    public void TheVolumeSliderFollowsTheFileRatherThanTheAuthor()
    {
        var scene = new SceneViewModel(new SceneDef { Key = "v" });

        // A still: nothing to turn down.
        scene.SceneSprite = "a.png";
        Assert.False(scene.HasAudioTrack);
        Assert.False(scene.IsAnimated);

        // A GIF: moves, but is silent by nature.
        scene.SceneSprite = "a.gif";
        Assert.True(scene.IsAnimated);
        Assert.False(scene.HasAudioTrack);

        // A video whose file cannot be read - not there, or a pack not saved
        // anywhere yet: the slider is OFFERED, because hiding it for a video
        // that turns out to be loud leaves an author nothing to turn down.
        scene.SceneSprite = "missing.mp4";
        Assert.True(scene.HasAudioTrack);
        _out.WriteLine("unknown video -> " + scene.MediaSummary);
        Assert.Contains("could not read", scene.MediaSummary);
    }

    [Fact]
    public void AnAnimationOutsideTheScenesCategoryIsRefusedOutLoud()
    {
        // The runtime refuses it too, but an author who only finds out from a
        // log has already shipped.
        var pack = PackRepository.CreateEmpty("anim.pack");
        var dialogue = new DialogueDef { Key = "d" };
        var node = new DialogueNodeDef { Id = 1, Text = "hi" };

        node.ActionsOnStart.Add(Swap(kind: "Bust", sprite: "art/wave.gif"));
        dialogue.Nodes.Add(node);
        dialogue.RootNodeIds.Add(1);
        pack.Dialogues.Add(dialogue);

        var issues = PackValidator.Validate(pack, "")
            .Where(i => i.Code == "action.animatedSpriteCategory").ToList();
        foreach (var i in issues) _out.WriteLine($"{i.Severity} {i.Where}: {i.Message}");

        var one = Assert.Single(issues);
        Assert.Equal(Severity.Error, one.Severity);
        Assert.Contains("Scenes category", one.Message);
    }

    [Fact]
    public void TheSameAnimationOnASceneIsFine()
    {
        // The other half of the pair. Without this the rule above could be
        // rejecting everything and still pass.
        var pack = PackRepository.CreateEmpty("anim.pack");
        var dialogue = new DialogueDef { Key = "d" };
        var node = new DialogueNodeDef { Id = 1, Text = "hi" };

        node.ActionsOnStart.Add(Swap(kind: "Scene", sprite: "art/wave.gif"));
        dialogue.Nodes.Add(node);
        dialogue.RootNodeIds.Add(1);
        pack.Dialogues.Add(dialogue);

        Assert.DoesNotContain(PackValidator.Validate(pack, ""),
                              i => i.Code == "action.animatedSpriteCategory");

        // And a STILL sprite on a bust stays fine, which it always was.
        node.ActionsOnStart[0] = Swap(kind: "Bust", sprite: "art/wave.png");
        Assert.DoesNotContain(PackValidator.Validate(pack, ""),
                              i => i.Code == "action.animatedSpriteCategory");
    }

    private static NodeActionDef Swap(string kind, string sprite)
    {
        var action = new NodeActionDef { Type = NodeActionTypes.SetSprite };
        action.Params["kind"] = kind;
        action.Params["target"] = "whatever";
        action.Params["sprite"] = sprite;
        return action;
    }

    private static void WriteGif(string path, int frames)
    {
        var enc = new GifBitmapEncoder();
        for (int i = 0; i < frames; i++)
        {
            var bmp = new WriteableBitmap(24, 24, 96, 96, PixelFormats.Bgra32, null);
            enc.Frames.Add(BitmapFrame.Create(bmp));
        }
        using var fs = File.Create(path);
        enc.Save(fs);
    }

    private sealed class Scratch : IDisposable
    {
        public string Path { get; }
        public Scratch()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                                          "smsmodforge-anim-" + Guid.NewGuid());
            Directory.CreateDirectory(Path);
        }
        public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
    }
}
