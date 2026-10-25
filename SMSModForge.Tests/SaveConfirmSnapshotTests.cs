using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using SMSModForge.Model;
using SMSModForge.Services;
using SMSModForge.View;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Pictures of the save prompt after an ordinary editing session, for a person
/// to look at (the author, 1.8.0: "find a way to visualize how it looks").
/// Off unless asked for, like <see cref="ThemeSnapshotTests"/>:
/// <c>SMSMODFORGE_SNAPSHOTS=&lt;folder&gt;</c> writes <c>save-prompt-&lt;theme&gt;.png</c>
/// (Light and Dark, or <c>SMSMODFORGE_SNAPSHOT_THEMES</c>) and the rows as
/// text. <c>SMSMODFORGE_SNAPSHOT_PACK</c> edits a copy of a real pack instead
/// of the built-in one.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class SaveConfirmSnapshotTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-saveshot-" + Guid.NewGuid().ToString("N"));

    public SaveConfirmSnapshotTests(ITestOutputHelper o) => _out = o;

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    /// <summary>
    /// What an author does in a sitting, on whatever pack is open: a
    /// dialogue renamed, a line reworded and its face changed, a line added
    /// and one taken out, a sound played on start, a scene's sound, a new
    /// scene, a variable's starting value, a sound's volume.
    /// </summary>
    internal static void EditASession(ModPack pack)
    {
        var d = pack.Dialogues.FirstOrDefault(x => x.Nodes.Count >= 3) ?? pack.Dialogues.FirstOrDefault();
        if (d != null)
        {
            d.DisplayName = (d.DisplayName ?? d.Key) + " (evening)";
            var first = d.Nodes.FirstOrDefault(n => !string.IsNullOrEmpty(n.Text)) ?? d.Nodes.FirstOrDefault();
            if (first != null)
            {
                first.Text = (first.Text ?? "") + " Right?";
                first.Expression = "Sad";
                int id = d.Nodes.Max(n => n.Id) + 1;
                d.Nodes.Add(new DialogueNodeDef { Id = id, Actor = first.Actor, Text = "One more thing before you go." });
                first.Children.Add(id);
                if (pack.Sfx.FirstOrDefault() is { } sound)
                    first.ActionsOnStart.Add(new NodeActionDef { Type = NodeActionTypes.PlaySFX, Params = { ["clip"] = sound.Key } });
                var setActive = d.Nodes.SelectMany(n => n.ActionsOnStart).FirstOrDefault(a => a.Type == NodeActionTypes.SetGameObjectActive);
                if (setActive != null && pack.Scenes.Count > 1) setActive.Params["target"] = pack.Scenes[1].Key;
            }
            var last = d.Nodes.LastOrDefault(n => n.Children.Count == 0 && n != d.Nodes[^1]);
            if (last != null)
            {
                d.Nodes.Remove(last);
                foreach (var n in d.Nodes) n.Children.Remove(last.Id);
                d.RootNodeIds.Remove(last.Id);
            }
        }
        if (pack.Scenes.FirstOrDefault() is { } scene) scene.Sound = scene.Sound == SceneSoundMode.Kiss ? SceneSoundMode.Silent : SceneSoundMode.Kiss;
        pack.Scenes.Add(new SceneDef { Key = "beach_wave", DisplayName = "Beach wave" });
        if (pack.SceneFolders.FirstOrDefault() is { } folder) folder.Items.Add("beach_wave");
        if (pack.Variables.FirstOrDefault() is { } variable) variable.DefaultValue = variable.DefaultValue == "1" ? "2" : "1";
        if (pack.Sfx.FirstOrDefault() is { } sfx) sfx.DefaultVolume = 0.65f;
    }

    [Fact]
    public void TheSavePromptAfterASession()
    {
        string? outDir = Environment.GetEnvironmentVariable("SMSMODFORGE_SNAPSHOTS");
        if (string.IsNullOrWhiteSpace(outDir)) return;
        Directory.CreateDirectory(outDir);

        string packDir = Path.Combine(_dir, "pack");
        string? real = Environment.GetEnvironmentVariable("SMSMODFORGE_SNAPSHOT_PACK");
        if (!string.IsNullOrWhiteSpace(real)) CopyTree(real, packDir);
        else PackRepository.Save(ThemeSnapshotTests.Fixture(), packDir);

        var wanted = (Environment.GetEnvironmentVariable("SMSMODFORGE_SNAPSHOT_THEMES") ?? "Light,Dark")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(packDir);
            WindowHarness.Pump();
            EditASession(vm.Pack);
            var changes = vm.ChangesToSave();
            File.WriteAllLines(Path.Combine(outDir, "save-prompt-rows.txt"),
                changes.Select(c => $"{c.Group}\t{c.Glyph}\t{c.Text}\t{c.Detail}"));
            _out.WriteLine($"{changes.Count} changes");

            var before = ThemeManager.Current;
            try
            {
                foreach (var theme in ThemeManager.All.Where(t => wanted.Any(w => string.Equals(w, t.Name, StringComparison.OrdinalIgnoreCase))))
                {
                    ThemeManager.Apply(theme);
                    var prompt = SaveConfirmWindow.Build(changes, packDir);
                    prompt.Left = -10000;
                    prompt.Top = -10000;
                    prompt.ShowInTaskbar = false;
                    prompt.Show();
                    prompt.UpdateLayout();
                    WindowHarness.Pump();
                    string file = Path.Combine(outDir, $"save-prompt-{theme.Name}.png");
                    ThemeSnapshotTests.Save(ThemeSnapshotTests.Render((FrameworkElement)prompt.Content, prompt.Background), file);
                    prompt.Close();
                    _out.WriteLine("written " + file);
                }
            }
            finally { ThemeManager.Apply(before); }
        });
    }

    /// <summary>The manifest and the translations - what a save compares -
    /// not the art and sound, which can run to gigabytes.</summary>
    private static void CopyTree(string from, string to)
    {
        Directory.CreateDirectory(to);
        File.Copy(Path.Combine(from, PackRepository.ManifestFileName), Path.Combine(to, PackRepository.ManifestFileName), overwrite: true);
        string translations = Path.Combine(from, "translations");
        if (!Directory.Exists(translations)) return;
        Directory.CreateDirectory(Path.Combine(to, "translations"));
        foreach (var file in Directory.GetFiles(translations))
            File.Copy(file, Path.Combine(to, "translations", Path.GetFileName(file)), overwrite: true);
    }
}
