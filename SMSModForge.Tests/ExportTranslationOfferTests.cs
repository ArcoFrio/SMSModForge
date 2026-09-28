using System;
using System.IO;
using System.Linq;
using SMSModForge.Localization;
using SMSModForge.Model;
using SMSModForge.Services.Translation;
using SMSModForge.ViewModel;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// The offer, made before an export or a publish, to translate what players
/// read into every language ModForge has.
/// <para/>
/// What it asks is a person's decision - it sends the pack's text to somebody
/// else's service - so with nobody at the keyboard it must be declined, and
/// the export must still go ahead. A test that answered yes would be a test
/// sending an author's words to Google.
/// </summary>
public sealed class ExportTranslationOfferTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "smsmodforge-offer-" + Guid.NewGuid().ToString("N"));

    public ExportTranslationOfferTests() => Directory.CreateDirectory(_root);

    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }

    [Fact]
    public void Unattended_TheOfferIsDeclined_TheExportGoesAhead_AndNothingIsSent()
    {
        bool was = Services.TestMode.Active;
        Services.TestMode.Active = true;
        try
        {
            var vm = new MainViewModel();
            var dialogue = new DialogueDef { Key = "chat" };
            dialogue.Nodes.Add(new DialogueNodeDef { Id = 1, Text = "A line nobody has translated yet." });
            vm.Pack.Dialogues.Add(dialogue);
            PackRepository.Save(vm.Pack, _root);
            vm.PackRoot = _root;

            // The control: there IS something to offer, so the answer below is
            // the harness declining, not the offer finding nothing to ask about.
            Assert.Contains(PackTranslationJob.StillToTranslate(vm.Pack, _root), w => w.Missing > 0);

            Assert.True(vm.OfferTranslationBeforeExport(), "the export was stopped");
            Assert.False(Directory.Exists(PackTranslations.FolderOf(_root)),
                "something was translated or written with nobody there to say yes");
        }
        finally { Services.TestMode.Active = was; }
    }
}
