using System.Linq;
using SMSModForge.Model;
using SMSModForge.Rendering;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Labels that name a variable.
/// <para/>
/// At runtime a <c>[PV:name]</c> label follows the variable. In the editor
/// there is no running game, so the preview draws the declared default - what
/// the label will say the first time a player sees the screen. Drawing the raw
/// token instead would make every such label look like a mistake AND make the
/// layout round it wrong, since the token is far longer than the value.
/// </summary>
public sealed class UiTextTokenTests
{
    private readonly ITestOutputHelper _out;
    public UiTextTokenTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void A_token_is_drawn_as_the_variables_default()
    {
        var was = UiTextTokens.Lookup;
        try
        {
            UiTextTokens.Lookup = name => name == "PlayerName" ? "Anna" : null;
            Assert.Equal("Hello Anna!", UiTextTokens.Resolve("Hello [PV:PlayerName]!"));
        }
        finally { UiTextTokens.Lookup = was; }
    }

    [Fact]
    public void A_token_naming_nothing_stays_visible()
    {
        // The failure this protects: a typo resolving to an empty string is a
        // label that silently says nothing, which is far harder to spot than
        // one that still reads "[PV:Coims]".
        var was = UiTextTokens.Lookup;
        try
        {
            UiTextTokens.Lookup = _ => null;
            Assert.Equal("You have [PV:Coims]", UiTextTokens.Resolve("You have [PV:Coims]"));
        }
        finally { UiTextTokens.Lookup = was; }
    }

    [Fact]
    public void Plain_text_is_left_exactly_alone()
    {
        var was = UiTextTokens.Lookup;
        try
        {
            UiTextTokens.Lookup = _ => "SHOULD NOT APPEAR";
            Assert.Equal("Are you sure?", UiTextTokens.Resolve("Are you sure?"));
            Assert.False(UiTextTokens.HasAny("Are you sure?"));
        }
        finally { UiTextTokens.Lookup = was; }
    }

    [Fact]
    public void Opening_a_pack_points_the_preview_at_its_own_variables()
    {
        // Captured once, this would leave a second pack's labels showing the
        // first pack's values.
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.Pack.Variables.Add(new PackVariableDef
            {
                Name = "Coins",
                Type = PackVariableType.Int,
                DefaultValue = "250",
            });

            // RebindUis runs on load; do what it does.
            vm.AddOwnUiCommand.Execute(UiTemplate.Find("panel"));
            WindowHarness.Pump();

            _out.WriteLine(UiTextTokens.Resolve("[PV:Coins] coins"));
            Assert.Equal("250 coins", UiTextTokens.Resolve("[PV:Coins] coins"));
        });
    }

    [Fact]
    public void The_label_is_drawn_at_the_length_of_the_value_not_the_token()
    {
        // The reason this matters for more than looks: a token is far longer
        // than what it stands for, so a preview drawing the token wraps and
        // overflows where the game would not.
        if (!VanillaUiLibrary.IsAvailable) { _out.WriteLine("no extraction - skipping"); return; }

        var was = UiTextTokens.Lookup;
        try
        {
            UiTextTokens.Lookup = _ => "7";

            var root = UiTemplate.Find("label")!.Build();
            root.Text!.Value = "[PV:SomeVariableWithALongName]";
            int tokenPixels = Painted(root);

            root.Text.Value = "7";
            int valuePixels = Painted(root);

            _out.WriteLine($"token drew {tokenPixels}px, value drew {valuePixels}px");
            Assert.Equal(valuePixels, tokenPixels);
        }
        finally { UiTextTokens.Lookup = was; }
    }

    private static int Painted(UiNodeDef root)
    {
        var pixels = UiAuthoredRenderer.Render(root, 1920, 1080, VanillaUiLibrary.Assets);
        int n = 0;
        for (int i = 3; i < pixels.Length; i += 4)
            if (pixels[i] != 0) n++;
        return n;
    }
}
