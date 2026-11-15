using System;
using System.IO;
using Newtonsoft.Json.Linq;
using SMSModForge.Model;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// A pack last saved by a newer ModForge (1.9.0): the editor says so when it opens
/// it and asks before saving it, because saving with this version drops whatever the
/// newer one added. Opened and saved through the real window, from a real folder.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class NewerPackTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _root;

    public NewerPackTests(ITestOutputHelper o)
    {
        _out = o;
        _root = Path.Combine(Path.GetTempPath(), "smsmodforge-newer-" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }

    /// <summary>A pack on disk whose manifest says the given ModForge saved it last
    /// (or carries no stamp at all, for null), with a field this ModForge does not know.</summary>
    private string PackStamped(string name, string? forge)
    {
        string dir = Path.Combine(_root, name);
        PackRepository.Save(PackRepository.CreateEmpty(name), dir);
        string manifest = Path.Combine(dir, "modpack.json");
        var json = JObject.Parse(File.ReadAllText(manifest));
        if (forge == null) json.Remove("forgeVersion"); else json["forgeVersion"] = forge;
        json["somethingFromTheFuture"] = "kept?";
        File.WriteAllText(manifest, json.ToString());
        return dir;
    }

    [Fact]
    public void APackANewerModForgeSaved_IsNamedAsSuch_AndSavingItHereMakesItThisVersions()
    {
        string newer = PackStamped("newer", "99.0.0");
        string same = PackStamped("same", Shared.ForgeVersion.Current);
        string older = PackStamped("older", "1.0.0");
        string unstamped = PackStamped("unstamped", null);
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;

            vm.OpenPackFromPath(newer);
            WindowHarness.Pump();
            Assert.Equal("99.0.0", vm.NewerForgeVersion);

            // Controls: the same version, an older one, and a pack from before stamps.
            foreach (var dir in new[] { same, older, unstamped })
            {
                vm.OpenPackFromPath(dir);
                WindowHarness.Pump();
                Assert.Null(vm.NewerForgeVersion);
            }

            // Saved anyway (the harness answers yes): the pack is this version's then,
            // and the question is not asked again.
            vm.OpenPackFromPath(newer);
            WindowHarness.Pump();
            Assert.True(vm.SavePack());
            Assert.Null(vm.NewerForgeVersion);
            var saved = JObject.Parse(File.ReadAllText(Path.Combine(newer, "modpack.json")));
            _out.WriteLine("stamp after the save: " + saved["forgeVersion"]);
            Assert.Equal(Shared.ForgeVersion.Current, (string?)saved["forgeVersion"]);
            // What the warning is about: the field this version does not know is gone.
            Assert.Null(saved["somethingFromTheFuture"]);
        });
    }
}
