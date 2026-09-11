using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using SMSModForge.View;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Typing in a dropdown narrows it to the names that contain what was typed.
/// <para/>
/// Driven through real ComboBoxes on the real window rather than through the
/// converter alone: the whole feature is an attached behaviour reaching into a
/// control template for a part by name, and a converter reading an attached
/// property off an ancestor. Every one of those is the kind of thing that
/// compiles, binds to nothing, and silently does nothing at all.
/// </summary>
public sealed class ComboBoxSearchTests
{
    private readonly ITestOutputHelper _out;
    public ComboBoxSearchTests(ITestOutputHelper o) => _out = o;

    /// <summary>An editable dropdown over a handful of the game's names, wired
    /// exactly as the implicit style wires every one in the editor.</summary>
    private static ComboBox Dropdown(params string[] items)
    {
        var box = new ComboBox { IsEditable = true, ItemsSource = items, Width = 200 };
        var window = new Window
        {
            Width = 300, Height = 200,
            Left = -10000, Top = -10000,        // off screen, like the harness's own
            ShowInTaskbar = false,
            Content = box,
        };
        window.Show();
        box.ApplyTemplate();
        WindowHarness.Pump();
        return box;
    }

    private static TextBox Typing(ComboBox box)
        => (TextBox)box.Template.FindName("PART_EditableTextBox", box);

    /// <summary>What the dropdown is actually showing.</summary>
    private static string[] Showing(ComboBox box)
    {
        WindowHarness.Pump();
        var shown = new System.Collections.Generic.List<string>();
        for (int i = 0; i < box.Items.Count; i++)
        {
            if (box.ItemContainerGenerator.ContainerFromIndex(i) is ComboBoxItem row
                && row.Visibility == Visibility.Visible)
                shown.Add((string)box.Items[i]);
        }
        return shown.ToArray();
    }

    /// <summary>Type, the way a person does: one character at a time into the
    /// box, with the caret after what they wrote.</summary>
    private static void Type(ComboBox box, string text)
    {
        var typing = Typing(box);
        box.IsDropDownOpen = true;
        WindowHarness.Pump();

        typing.Text = text;
        typing.SelectionStart = text.Length;
        typing.SelectionLength = 0;
        WindowHarness.Pump();
    }

    [Fact]
    public void ANameIsFoundByItsMiddleNotOnlyItsStart()
    {
        // The point of the whole thing. WPF's own completion finds Anna from
        // "An" and never from "na", and half the names in this editor are
        // found by their middle.
        WindowHarness.Run(_ =>
        {
            var box = Dropdown("Anna", "Adrian", "Kate", "Nadia");
            Type(box, "na");

            var shown = Showing(box);
            _out.WriteLine("showing: " + string.Join(", ", shown));

            Assert.Contains("Anna", shown);
            Assert.Contains("Nadia", shown);
            Assert.DoesNotContain("Kate", shown);
            Assert.DoesNotContain("Adrian", shown);
        });
    }

    [Fact]
    public void NothingTypedShowsEverything()
    {
        WindowHarness.Run(_ =>
        {
            var box = Dropdown("Anna", "Adrian", "Kate");
            box.IsDropDownOpen = true;
            WindowHarness.Pump();

            Assert.Equal(3, Showing(box).Length);
        });
    }

    [Fact]
    public void CaseDoesNotMatter()
    {
        WindowHarness.Run(_ =>
        {
            var box = Dropdown("Anna_GoldenBikini", "Kate");
            Type(box, "GOLDEN");
            Assert.Single(Showing(box));
        });
    }

    [Fact]
    public void WhatCompletionAddedIsNotTreatedAsTyped()
    {
        // The trap this behaviour exists to avoid. An editable ComboBox
        // completes as you type: enter "An" and WPF puts "Anna" in the box with
        // "na" selected. Filtering on the box's text would then be filtering on
        // a word nobody wrote, and the list would collapse to the single item
        // the completion happened to land on - so "An" would stop offering
        // "Adrian" the moment "Anna" was completed over it.
        WindowHarness.Run(_ =>
        {
            var box = Dropdown("Anna", "Adrian", "Kate");
            var typing = Typing(box);
            box.IsDropDownOpen = true;
            WindowHarness.Pump();

            // Exactly the state completion leaves behind: the whole word, with
            // everything after the caret selected.
            typing.Text = "Anna";
            typing.SelectionStart = 2;
            typing.SelectionLength = 2;
            WindowHarness.Pump();

            Assert.Equal("An", ComboBoxSearch.GetTyped(box));

            var shown = Showing(box);
            _out.WriteLine("showing: " + string.Join(", ", shown));
            Assert.Contains("Anna", shown);
            Assert.Contains("Adrian", shown);
            Assert.DoesNotContain("Kate", shown);
        });
    }

    [Fact]
    public void OneDropdownNarrowingDoesNotNarrowAnother()
    {
        // Why this hides containers rather than filtering the collection.
        // Several dropdowns in this editor share one option list - some of them
        // a row apiece inside a list - and they share its default view with it.
        // A filter set on that view narrows every one of them at once.
        WindowHarness.Run(_ =>
        {
            var shared = new System.Collections.ObjectModel.ObservableCollection<string>
                { "Anna", "Adrian", "Kate" };

            var first = new ComboBox { IsEditable = true, ItemsSource = shared, Width = 200 };
            var second = new ComboBox { IsEditable = true, ItemsSource = shared, Width = 200 };
            var panel = new StackPanel();
            panel.Children.Add(first);
            panel.Children.Add(second);
            var window = new Window
            {
                Width = 300, Height = 200, Left = -10000, Top = -10000,
                ShowInTaskbar = false, Content = panel,
            };
            window.Show();
            first.ApplyTemplate();
            second.ApplyTemplate();
            WindowHarness.Pump();

            Type(first, "na");
            var narrowed = Showing(first);
            _out.WriteLine("first: " + string.Join(", ", narrowed));
            Assert.DoesNotContain("Kate", narrowed);

            // The list itself never moved - which is the whole difference. A
            // filter set on the view would have taken Kate out of it, and every
            // other dropdown over the same list with her.
            Assert.Equal(3, shared.Count);
            Assert.Equal(3, first.Items.Count);
            Assert.Equal(3, second.Items.Count);

            // Only one dropdown can be open at a time, so opening the second
            // closes the first and clears its search - by design. What matters
            // is that the second was never narrowed by the first.
            second.IsDropDownOpen = true;
            WindowHarness.Pump();
            var other = Showing(second);
            _out.WriteLine("second: " + string.Join(", ", other));
            Assert.Contains("Kate", other);
            Assert.Equal(3, other.Length);
        });
    }

    [Fact]
    public void ClosingTheListLeavesItWholeForNextTime()
    {
        WindowHarness.Run(_ =>
        {
            var box = Dropdown("Anna", "Adrian", "Kate");
            Type(box, "na");
            Assert.DoesNotContain("Kate", Showing(box));

            box.IsDropDownOpen = false;
            WindowHarness.Pump();
            box.IsDropDownOpen = true;

            Assert.Equal(3, Showing(box).Length);
        });
    }

    [Fact]
    public void NarrowingLeavesTheListItselfAlone()
    {
        // The reason this hides containers instead of filtering the view, and
        // the reason it is safe to do while a value is bound: everything is
        // still IN the list, so nothing downstream of it can notice.
        //
        // A view filter would take the hidden item out of Items, and this
        // editor has been bitten before by an option list that stopped offering
        // a bound value - the combo writes empty back over it. Typing does
        // clear the selection here, but that is the editable ComboBox's own
        // doing and happens with or without any of this.
        WindowHarness.Run(_ =>
        {
            var box = Dropdown("Anna", "Adrian", "Kate");
            Type(box, "na");

            Assert.DoesNotContain("Kate", Showing(box));
            Assert.Equal(3, box.Items.Count);
            Assert.Contains("Kate", box.Items.Cast<string>());
        });
    }

    [Fact]
    public void APlainDropdownIsLeftExactlyAsItWas()
    {
        // The control. Most dropdowns in the editor are short fixed lists with
        // nowhere to type - enums, comparisons, easings - and they keep WPF's
        // own jump-to-first-letter untouched.
        WindowHarness.Run(_ =>
        {
            var box = new ComboBox { ItemsSource = new[] { "Male", "Female", "Custom" }, Width = 200 };
            var window = new Window
            {
                Width = 300, Height = 200, Left = -10000, Top = -10000,
                ShowInTaskbar = false, Content = box,
            };
            window.Show();
            box.ApplyTemplate();
            box.IsDropDownOpen = true;
            WindowHarness.Pump();

            Assert.Equal("", ComboBoxSearch.GetTyped(box));
            Assert.Equal(3, Showing(box).Length);
        });
    }
}
