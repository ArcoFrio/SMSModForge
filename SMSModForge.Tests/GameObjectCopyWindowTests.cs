using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using SMSModForge.Model;
using SMSModForge.ViewModel;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// A place's object rows as drawn (1.8.3): a copy shows what a copy takes -
/// beside the original, the overrides, the particle colour - and an object of
/// the pack's own shows none of it, only the "Copy of" box that makes it one.
/// </summary>
[Trait("Speed", "Slow")]   // builds a window on the harness thread
public sealed class GameObjectCopyWindowTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "SMSModForge-copyrows-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void A_copys_row_shows_its_settings_and_an_objects_own_row_does_not()
    {
        var pack = PackRepository.CreateEmpty("copyrows");
        pack.Places.Add(new PlaceDef
        {
            Key = "room", DisplayName = "Room", InternalName = "Room",
            GameObjects =
            {
                new GameObjectDef { Name = "Own" },
                new GameObjectDef { Name = "Copied", CopyOf = "6_Effects/Fx" },
            },
        });
        PackRepository.Save(pack, _dir);

        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(_dir);
            vm.SelectedTabIndex = MainTab.Places;
            vm.SelectedPlace = vm.Places.Single();
            WindowHarness.Pump();
            window.UpdateLayout();

            bool Shown<T>(string name, string path, DependencyProperty property) where T : FrameworkElement
            {
                var found = Descendants(window).OfType<T>()
                    .Where(e => e.DataContext is GameObjectViewModel g && g.Name == name
                                && BindingOperations.GetBinding(e, property)?.Path.Path == path).ToList();
                Assert.True(found.Count == 1, $"{found.Count} controls bound to {path} on '{name}'s row");
                return found[0].IsVisible;
            }

            foreach (var (name, copy) in new[] { ("Copied", true), ("Own", false) })
            {
                Assert.True(Shown<TextBox>(name, "CopyOf", TextBox.TextProperty), $"'{name}' has no Copy of box");
                Assert.Equal(copy, Shown<CheckBox>(name, "CopyBeside", ToggleButton.IsCheckedProperty));
                Assert.Equal(copy, Shown<CheckBox>(name, "OverrideParallax", ToggleButton.IsCheckedProperty));
                Assert.Equal(copy, Shown<TextBox>(name, "ParticleColor", TextBox.TextProperty));
            }
        });
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var d in Descendants(child)) yield return d;
        }
    }
}
