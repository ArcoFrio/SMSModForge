using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using SMSModForge.Model;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// A scene the pack marks as a Starmaker photo.
/// <para/>
/// Showing one puts it through the game's own payout — Anna's mood deciding
/// whether the shot came out, the photography traits, the camera and lens, the
/// adverts, the skill tree, the day's income and the photo counts — by
/// switching on the object the game's own photo triggers switch on. None of
/// that is reproduced, so none of it can drift.
/// <para/>
/// Checked through a real save, because the runtime reads this off the written
/// manifest: a field the view model holds and the file never carries would work
/// perfectly in the editor and do nothing in the game.
/// </summary>
public sealed class StarmakerPhotoSceneTests
{
    private readonly ITestOutputHelper _out;
    public StarmakerPhotoSceneTests(ITestOutputHelper o) => _out = o;

    private JObject SaveAndRead(ModPack pack, string root)
    {
        PackRepository.Save(pack, root);
        string json = File.ReadAllText(Path.Combine(root, PackManifestName));
        return JObject.Parse(json);
    }

    private const string PackManifestName = "modpack.json";

    private static JObject SceneIn(JObject manifest, string key)
        => ((JArray)manifest["scenes"]!).OfType<JObject>().Single(s => (string?)s["key"] == key);

    [Fact]
    public void TheTickReachesTheFileTheGameReads()
    {
        using var dir = new Scratch();
        var pack = PackRepository.CreateEmpty("photo.pack");
        pack.Scenes.Add(new SceneDef { Key = "selfie", StarmakerPhoto = true });
        pack.Scenes.Add(new SceneDef { Key = "story" });

        var manifest = SaveAndRead(pack, dir.Path);
        _out.WriteLine(manifest["scenes"]!.ToString());

        Assert.True((bool)SceneIn(manifest, "selfie")["starmakerPhoto"]!);
    }

    [Fact]
    public void AnOrdinarySceneSaysNothingAtAll()
    {
        // The control, and the reason the field is written only when set: every
        // pack ever made has scenes that are not photographs, and a manifest
        // that spelled "starmakerPhoto": false on each of them would be a diff
        // across every pack in existence the first time its author saved.
        using var dir = new Scratch();
        var pack = PackRepository.CreateEmpty("photo.pack");
        pack.Scenes.Add(new SceneDef { Key = "story" });

        var scene = SceneIn(SaveAndRead(pack, dir.Path), "story");
        _out.WriteLine(scene.ToString());
        Assert.Null(scene["starmakerPhoto"]);
    }

    [Fact]
    public void APackWrittenBeforeThisExistedIsNotAPhoto()
    {
        // Off is the answer for every scene of every pack already out there,
        // which is what makes this safe to add without a migration.
        using var dir = new Scratch();
        var pack = PackRepository.CreateEmpty("photo.pack");
        pack.Scenes.Add(new SceneDef { Key = "old" });
        PackRepository.Save(pack, dir.Path);

        var reloaded = PackRepository.Load(dir.Path);
        Assert.False(reloaded.Scenes.Single(s => s.Key == "old").StarmakerPhoto);
    }

    /// <summary>A folder of its own, thrown away afterwards - a committed test
    /// must not write into anybody's pack.</summary>
    private sealed class Scratch : IDisposable
    {
        public string Path { get; }
        public Scratch()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                                          "smsmodforge-photo-" + Guid.NewGuid());
            Directory.CreateDirectory(Path);
        }
        public void Dispose() { try { Directory.Delete(Path, true); } catch (IOException) { } }
    }

    [Fact]
    public void ItSurvivesASaveAndAReload()
    {
        using var dir = new Scratch();
        var pack = PackRepository.CreateEmpty("photo.pack");
        pack.Scenes.Add(new SceneDef { Key = "selfie", StarmakerPhoto = true });
        pack.Scenes.Add(new SceneDef { Key = "story" });
        PackRepository.Save(pack, dir.Path);

        var reloaded = PackRepository.Load(dir.Path);
        Assert.True(reloaded.Scenes.Single(s => s.Key == "selfie").StarmakerPhoto);
        Assert.False(reloaded.Scenes.Single(s => s.Key == "story").StarmakerPhoto);
    }
}
