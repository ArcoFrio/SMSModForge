using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using SMSModForge.Localization;
using SMSModForge.Model;

namespace SMSModForge.View;

/// <summary>
/// The pack's translations in one list - how much of each is done, how much
/// has gone out of date - and two things to do with one: copy it over another
/// language, or delete it (the author, 2026-09-28). Opened from the Edit
/// button beside Editing in.
/// <para/>
/// It works on the translation files themselves, like every other tool that
/// does (translating by machine, checking): the caller opens it inside
/// <c>MainViewModel.WithTranslationFilesCurrent</c>, so anything typed in a
/// language and not saved yet is in its file first, and the language being
/// edited is read back from its file afterwards.
/// </summary>
public partial class PackTranslationsWindow : Window
{
    private readonly ModPack _pack;
    private readonly string _root;

    /// <summary>One translation, as a row.</summary>
    public sealed class Row
    {
        public PackTranslations.Summary Summary { get; init; } = null!;
        public string Code => Summary.Code;
        public string Language => NameOf(Summary.Code);
        public string Translated => Loc.F("packTranslations.translatedValue", "percent", Summary.Percent.ToString(),
                                          "done", Summary.Translated.ToString("N0"), "total", Summary.Total.ToString("N0"));
        public string OutOfDate => Summary.OutOfDate.ToString("N0");
        public string File => System.IO.Path.GetFileName(Summary.Path);
    }

    public sealed record Choice(string Code, string Label);

    /// <summary>
    /// Asked before a transfer or a delete, with what is about to happen and a
    /// title: yes goes ahead. A person by default - never under the test
    /// harness, where nobody is there to answer - and a test's answer when a
    /// test sets it.
    /// </summary>
    public Func<string, string, bool> Confirm { get; set; }

    /// <summary>The languages deleted while it was open.</summary>
    public List<string> Deleted { get; } = new();

    /// <summary>Whether any file was changed while it was open.</summary>
    public bool Changed { get; private set; }

    private readonly Services.Translation.TranslationSummaryCache? _summaries;
    private List<Row> _rows = new();

    /// <param name="summaries">Where each translation's standing is kept
    /// between openings (1.6.3); worked out afresh without one.</param>
    public PackTranslationsWindow(ModPack pack, string packRoot,
                                  Services.Translation.TranslationSummaryCache? summaries = null)
    {
        _pack = pack;
        _root = packRoot;
        _summaries = summaries;
        Confirm = AskAPerson;
        InitializeComponent();
        Refresh();
    }

    private static string NameOf(string code)
        => (TranslationFiles.NativeName(code) ?? code) + "  (" + code + ")";

    private bool AskAPerson(string message, string title)
    {
        if (Services.TestMode.Active) return false;
        return MessageBox.Show(this, message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning,
                               MessageBoxResult.No) == MessageBoxResult.Yes;
    }

    /// <summary>The list again, from the files, keeping the row picked.</summary>
    public void Refresh()
    {
        string? picked = (List.SelectedItem as Row)?.Code;
        var summaries = _summaries?.Get(_pack, _root) ?? PackTranslations.Summaries(_pack, _root);
        var rows = _rows = summaries.Select(s => new Row { Summary = s }).ToList();
        List.ItemsSource = rows;
        List.SelectedItem = rows.FirstOrDefault(r => r.Code == picked) ?? rows.FirstOrDefault();
        EmptyText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RefreshTargets();
    }

    /// <summary>Where the picked translation can be copied: every other
    /// language the pack has a translation into or the editor knows - not the
    /// pack's own, which is its default text rather than a translation.</summary>
    private void RefreshTargets()
    {
        string? from = (List.SelectedItem as Row)?.Code;
        string? wanted = TargetPicker.SelectedValue as string;
        var codes = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in _rows) codes.Add(r.Code);
        foreach (var c in Loc.AvailableCodes()) codes.Add(c);
        codes.Remove(_pack.OwnLanguage);
        if (from != null) codes.Remove(from);
        var choices = codes.Select(c => new Choice(c, NameOf(c))).ToList();
        TargetPicker.ItemsSource = choices;
        TargetPicker.SelectedValue = choices.Any(c => c.Code == wanted) ? wanted : null;
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        bool picked = List.SelectedItem is Row;
        DeleteButton.IsEnabled = picked;
        TransferButton.IsEnabled = picked && TargetPicker.SelectedValue is string;
    }

    private void Selection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ReferenceEquals(sender, List)) RefreshTargets();
        else UpdateButtons();
    }

    /// <summary>Copy the picked translation over <paramref name="to"/>, once
    /// the author says so. False when nothing was done.</summary>
    public bool TransferSelected(string to)
    {
        if (List.SelectedItem is not Row row || string.IsNullOrEmpty(to)) return false;
        string fromName = NameOf(row.Code), toName = NameOf(to);
        if (!Confirm(Loc.F("packTranslations.transfer.confirm", "from", fromName, "to", toName),
                     Loc.T("packTranslations.transfer.title")))
            return false;

        int copied = PackTranslations.Transfer(_pack, _root, row.Code, to);
        Changed |= copied > 0;
        StatusText.Text = Loc.P("packTranslations.transferred", copied, "from", fromName, "to", toName);
        Refresh();
        return copied > 0;
    }

    /// <summary>Delete the picked translation, once the author says so.</summary>
    public bool DeleteSelected()
    {
        if (List.SelectedItem is not Row row) return false;
        string name = NameOf(row.Code);
        if (!Confirm(Loc.F("packTranslations.delete.confirm", "language", name), Loc.T("packTranslations.delete.title")))
            return false;

        if (!PackTranslations.Delete(_root, row.Code)) return false;
        Changed = true;
        Deleted.Add(row.Code);
        StatusText.Text = Loc.F("packTranslations.deleted", "language", name);
        Refresh();
        return true;
    }

    private void Transfer_Click(object sender, RoutedEventArgs e)
    {
        if (TargetPicker.SelectedValue is string to) TransferSelected(to);
    }

    private void Delete_Click(object sender, RoutedEventArgs e) => DeleteSelected();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
