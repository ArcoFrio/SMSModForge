using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using SMSModForge.Localization;
using SMSModForge.Shared;

namespace SMSModForge.View;

/// <summary>
/// One of the editor's texts, in the author's own words for the language on
/// screen: opened from a pencil (<see cref="UiTextMarks"/>) with that element's
/// texts listed, or from Language ▸ Find a text to edit with none, to search.
/// Saved into their languages folder by <see cref="UiTextEdits"/> and shown at
/// once, everywhere.
/// </summary>
public partial class UiTextEditWindow : Window
{
    /// <summary>A text in the list.</summary>
    public sealed class TextRow
    {
        public string Key { get; init; } = "";
        public string Part { get; init; } = "text";
        public string Shown { get; init; } = "";
        public string Detail { get; init; } = "";
    }

    /// <summary>One form of the text being edited - the text itself, or one of
    /// the forms a text that says a number takes.</summary>
    public sealed class FormRow : INotifyPropertyChanged
    {
        public string Key { get; init; } = "";
        public string Label { get; init; } = "";
        public string English { get; init; } = "";
        public string LanguageLabel { get; init; } = "";
        public string Was { get; init; } = "";

        private string _text = "";
        public string Text
        {
            get => _text;
            set { _text = value ?? ""; Changed(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Changed([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    private readonly ObservableCollection<TextRow> _rows = new();
    private readonly ObservableCollection<FormRow> _forms = new();
    private readonly IReadOnlyList<UiTextMarks.Found> _fromPencil;
    private string _baseKey = "";

    private UiTextEditWindow(IReadOnlyList<UiTextMarks.Found> texts)
    {
        InitializeComponent();
        _fromPencil = texts;
        TextList.ItemsSource = _rows;
        FormList.ItemsSource = _forms;
        FillFromPencil();
        Loaded += (_, _) =>
        {
            if (_rows.Count > 0) TextList.SelectedIndex = 0;
            else SearchBox.Focus();
        };
    }

    /// <summary>
    /// Open it for <paramref name="texts"/> - an element's, from its pencil - or
    /// for none, to search. Not in the tests: a window raised there stops the
    /// suite on somebody's screen.
    /// </summary>
    public static void Open(Window owner, IReadOnlyList<UiTextMarks.Found>? texts)
    {
        if (Services.TestMode.Active) return;
        if (Loc.Current.Code == Loc.EnglishCode) return;
        var window = new UiTextEditWindow(texts ?? Array.Empty<UiTextMarks.Found>()) { Owner = owner };
        window.ShowDialog();
    }

    /// <summary>What the language on screen shows for a key now.</summary>
    private static string ShownNow(string key)
        => Loc.Current.Translation?.Translated(key) ?? UiTextEdits.EnglishOf(key);

    /// <summary>A text's key as the list shows it: its words now, and what it
    /// is - a tooltip, or corrected by the author already.</summary>
    private static TextRow RowFor(string key, string part)
    {
        var forms = UiTextEdits.FormsOf(key, Loc.Current.Code);
        string first = forms.Count > 0 ? forms[forms.Count - 1] : key;
        bool yours = forms.Any(f => UiTextEdits.IsEdited(Loc.Current.Code, f, Loc.UserFolder));
        var detail = new List<string>();
        if (part == "tooltip") detail.Add(Loc.T("uiEdit.part.tooltip"));
        if (yours) detail.Add(Loc.T("uiEdit.yoursAlready"));
        detail.Add(key);
        return new TextRow { Key = key, Part = part, Shown = ShownNow(first), Detail = string.Join(" · ", detail) };
    }

    private void FillFromPencil()
    {
        _rows.Clear();
        foreach (var f in _fromPencil) _rows.Add(RowFor(f.Key, f.Part));
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        string wanted = SearchBox.Text.Trim();
        if (wanted.Length == 0)
        {
            FillFromPencil();
            StatusText.Text = "";
            return;
        }
        if (wanted.Length < 2) return;

        // Every text once, by the key a window asks for: a text that says a
        // number is found by any of its forms, and listed by its own key.
        var keys = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in Loc.English.Entries)
        {
            string key = TextFile.FormOf(entry.Key) != null ? TextFile.BaseOf(entry.Key) : entry.Key;
            if (seen.Contains(key)) continue;
            bool match = entry.Text.Contains(wanted, StringComparison.CurrentCultureIgnoreCase)
                         || ShownNow(entry.Key).Contains(wanted, StringComparison.CurrentCultureIgnoreCase)
                         || entry.Key.Contains(wanted, StringComparison.OrdinalIgnoreCase);
            if (!match) continue;
            seen.Add(key);
            keys.Add(key);
            if (keys.Count >= 200) break;
        }
        _rows.Clear();
        foreach (string key in keys) _rows.Add(RowFor(key, "text"));
        StatusText.Text = keys.Count == 0 ? Loc.T("uiEdit.none") : "";
    }

    private void TextList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _forms.Clear();
        _baseKey = "";
        if (TextList.SelectedItem is not TextRow row)
        {
            NothingText.Visibility = Visibility.Visible;
            KeyText.Text = FormGuideText.Text = "";
            ShippedButton.IsEnabled = SaveButton.IsEnabled = false;
            return;
        }

        _baseKey = row.Key;
        string code = Loc.Current.Code;
        var forms = UiTextEdits.FormsOf(row.Key, code);
        string language = TranslationFiles.NativeName(code) ?? code;
        bool plural = forms.Count > 1 || (forms.Count == 1 && forms[0] != row.Key);
        foreach (string key in forms)
        {
            string shown = ShownNow(key);
            _forms.Add(new FormRow
            {
                Key = key,
                Label = plural ? Loc.F("uiEdit.form", "form", TextFile.FormOf(key) ?? key) : "",
                English = UiTextEdits.EnglishOf(key),
                LanguageLabel = Loc.F("uiEdit.yours", "language", language),
                Was = shown,
                Text = shown,
            });
        }
        NothingText.Visibility = Visibility.Collapsed;
        KeyText.Text = row.Key;
        FormGuideText.Text = plural ? TextFileWriter.FormGuide(code, PluralRules.FormsFor(code)) : "";
        SaveButton.IsEnabled = _forms.Count > 0;
        ShippedButton.IsEnabled = forms.Any(f => UiTextEdits.IsEdited(code, f, Loc.UserFolder));
        StatusText.Text = "";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_forms.Count == 0) return;
        string code = Loc.Current.Code;

        // A gap the English fills and the words lost is a sentence with a hole
        // in it, in every place the text is used: not saved.
        var problems = new List<string>();
        foreach (var form in _forms)
        {
            var (lost, unknown) = UiTextEdits.CheckGaps(form.English, form.Text);
            if (lost.Count > 0) problems.Add(Loc.F("uiEdit.lostGaps", "gaps", string.Join(" ", lost)));
            if (unknown.Count > 0) problems.Add(Loc.F("uiEdit.unknownGaps", "gaps", string.Join(" ", unknown)));
        }
        if (problems.Count > 0)
        {
            StatusText.Text = string.Join("\n", problems.Distinct());
            return;
        }

        string? path = null;
        foreach (var form in _forms.Where(f => f.Text != f.Was))
            path = UiTextEdits.Save(code, form.Key, form.Text, Loc.UserFolder);
        if (path == null)
        {
            StatusText.Text = Loc.T("uiEdit.unchanged");
            return;
        }
        Reload(code);
        StatusText.Text = Loc.F("uiEdit.saved", "path", path);
    }

    private void Shipped_Click(object sender, RoutedEventArgs e)
    {
        string code = Loc.Current.Code;
        foreach (var form in _forms) UiTextEdits.Forget(code, form.Key, Loc.UserFolder);
        Reload(code);
        StatusText.Text = Loc.T("uiEdit.backToShipped");
    }

    /// <summary>Read the language again, so the words just saved are on screen
    /// everywhere - and in this window's list.</summary>
    private void Reload(string code)
    {
        string keep = _baseKey;
        Loc.Use(code);
        var rows = _rows.ToList();
        _rows.Clear();
        foreach (var r in rows) _rows.Add(RowFor(r.Key, r.Part));
        TextList.SelectedItem = _rows.FirstOrDefault(r => r.Key == keep);
    }
}
