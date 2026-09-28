using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SMSModForge.Localization;
using SMSModForge.Model;
using SMSModForge.Shared;
using SMSModForge.ViewModel;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// Something just added is called what it is called in the language it is
/// written in, not the one the editor is shown in. An author reading the
/// editor in Portuguese got "Novo SFX" in a pack written in English
/// (2026-09-27).
/// <para/>
/// A name the pack translates - a character's, a quest's title, the words on
/// a UI - is in the language being edited; one it does not is the same in
/// every language, so it is in the pack's own words.
/// </summary>
public sealed class NewRecordNamesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-newnames-" + Guid.NewGuid().ToString("N"));
    private readonly bool _wasTestMode = Services.TestMode.Active;

    public NewRecordNamesTests()
    {
        Directory.CreateDirectory(_dir);
        Services.TestMode.Active = true;
    }

    public void Dispose()
    {
        Loc.Use(Loc.EnglishCode);
        Services.TestMode.Active = _wasTestMode;
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private static string In(string code, string key) => Loc.Preview(code).T(key);

    /// <summary>An English pack with a Spanish translation, open, with the
    /// editor shown in Portuguese.</summary>
    private MainViewModel OpenInPortuguese(string packLanguage = "en")
    {
        var pack = PackRepository.CreateEmpty("newnames.pack");
        pack.Language = packLanguage;
        var d = new DialogueDef { Key = "beach" };
        d.Nodes.Add(new DialogueNodeDef { Id = 1, Text = "Hello there!" });
        pack.Dialogues.Add(d);
        PackRepository.Save(pack, _dir);
        var source = PackTranslations.Source(pack);
        var es = new TextFile();
        foreach (var e in source.Entries) es.Add(new TextFile.Entry { Key = e.Key, Text = "ES " + e.Text, English = e.Text });
        PackTranslations.Write(pack, _dir, "es", source, es);

        Loc.Use("pt-BR");
        var vm = new MainViewModel();
        vm.OpenPackFromPath(_dir);

        // The point of the test: the editor's own words for these are not
        // English. Were they, English names would prove nothing.
        Assert.Equal("pt-BR", Loc.Current.Code);
        Assert.NotEqual(In("en", "sfx.newName"), Loc.T("sfx.newName"));
        Assert.NotEqual(In("en", "characters.newName"), Loc.T("characters.newName"));
        return vm;
    }

    private static IEnumerable<string> TextsOf(UiNodeDef node)
    {
        if (node.Text?.Value is { Length: > 0 } value) yield return value;
        foreach (var child in node.Children)
            foreach (var text in TextsOf(child)) yield return text;
    }

    [Fact]
    public void InThePacksOwnWords_EverythingNewIsInThePacksLanguage_NotTheEditors()
    {
        var vm = OpenInPortuguese();

        vm.AddSfxCommand.Execute(null);
        Assert.Equal(In("en", "sfx.newName"), vm.Sfx.Last().Model.DisplayName);

        vm.AddCharacterCommand.Execute(null);
        Assert.Equal(In("en", "characters.newName"), vm.Characters.Last().DisplayName);

        vm.AddQuestCommand.Execute(null);
        Assert.Equal(In("en", "quests.newName"), vm.Quests.Last().Title);

        vm.AddDialogueCommand.Execute(null);
        Assert.Equal(In("en", "dialogues.newName"), vm.Pack.Dialogues.Last().DisplayName);

        vm.SfxTree.AddFolder();
        Assert.Contains(vm.SfxTree.Tree.OfType<UnitFolderNode>(), f => f.Name == In("en", "common.newFolder"));

        vm.AddOwnUiCommand.Execute(UiTemplate.Find("dialog"));
        var ui = vm.Pack.Uis.Last();
        Assert.Equal(In("en", "ui.template.dialog.name"), ui.Name);
        var texts = ui.Nodes.SelectMany(TextsOf).ToList();
        Assert.Contains(In("en", "ui.template.text.yes"), texts);
        Assert.Contains(In("en", "ui.template.text.no"), texts);
        Assert.DoesNotContain(Loc.T("ui.template.text.yes"), texts);
    }

    [Fact]
    public void APackWrittenInPortuguese_GetsPortugueseNames_WhateverTheEditorIsIn()
    {
        var vm = OpenInPortuguese(packLanguage: "pt-BR");
        Loc.Use(Loc.EnglishCode);

        vm.AddSfxCommand.Execute(null);
        Assert.Equal(In("pt-BR", "sfx.newName"), vm.Sfx.Last().Model.DisplayName);
        Assert.NotEqual(In("en", "sfx.newName"), vm.Sfx.Last().Model.DisplayName);
    }

    [Fact]
    public void EditingATranslation_ANameItTranslatesIsInThatLanguage_AndOneItDoesNotIsInThePacksOwn()
    {
        var vm = OpenInPortuguese();
        vm.EditingLanguage = "es";
        Assert.True(vm.IsEditingTranslation);
        Assert.NotEqual(In("en", "characters.newName"), In("es", "characters.newName"));

        // A character's name is typed per language: the new one's is Spanish.
        vm.AddCharacterCommand.Execute(null);
        Assert.Equal(In("es", "characters.newName"), vm.Characters.Last().DisplayName);

        vm.AddQuestCommand.Execute(null);
        Assert.Equal(In("es", "quests.newName"), vm.Quests.Last().Title);

        // An SFX's name is one name for every language, and that is the
        // pack's: Spanish there would be in front of every English reader.
        vm.AddSfxCommand.Execute(null);
        Assert.Equal(In("en", "sfx.newName"), vm.Sfx.Last().Model.DisplayName);
    }
}
