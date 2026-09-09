using System.Linq;
using SMSModForge.Model;
using SMSModForge.Rendering;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Centring a grid's last row when it never filled up.
/// <para/>
/// Reported from the gifting screen: nineteen gifts in rows of seven leave a
/// last row of five, and Unity packs it against the start corner - so a grid
/// that is symmetrical everywhere else ends with everything shoved left.
/// <para/>
/// Unity has no option for this, which is why it is arithmetic of ours rather
/// than a flag passed through. The numbers below are the reported case.
/// </summary>
public sealed class UiGridCenterTests
{
    private readonly ITestOutputHelper _out;
    public UiGridCenterTests(ITestOutputHelper o) => _out = o;

    private const double Cell = 100, Gap = 10, Columns = 7;

    private static UiLayoutDef Grid(bool centre, string startCorner = "UpperLeft",
                                    string startAxis = "Horizontal") => new()
    {
        Kind = UiLayoutKinds.Grid,
        CellSize = new[] { (float)Cell, (float)Cell },
        Spacing = new[] { (float)Gap, (float)Gap },
        Constraint = "FixedColumnCount",
        ConstraintCount = (int)Columns,
        Alignment = "UpperLeft",
        StartCorner = startCorner,
        StartAxis = startAxis,
        CenterLastLine = centre,
    };

    /// <summary>n children of one cell each, laid out in a wide box.</summary>
    private static UiRect[] Lay(UiLayoutDef layout, int n)
    {
        var inner = new UiRect(0, 0, Columns * Cell + (Columns - 1) * Gap, 1000);
        var items = Enumerable.Range(0, n)
            .Select(_ => new UiLayoutGroups.Item(Cell, Cell))
            .ToList();
        return UiLayoutGroups.Arrange(layout, inner, items);
    }

    [Fact]
    public void The_reported_case_nineteen_in_rows_of_seven()
    {
        var packed = Lay(Grid(centre: false), 19);
        var centred = Lay(Grid(centre: true), 19);

        // The last row holds five of a possible seven, so two cells of slack -
        // one cell's worth on each side.
        double expected = (7 - 5) * 0.5 * (Cell + Gap);

        _out.WriteLine($"last row starts at {packed[14].X} packed, {centred[14].X} centred " +
                       $"(expected shift {expected})");

        Assert.Equal(expected, centred[14].X - packed[14].X, 3);
        Assert.Equal(expected, centred[18].X - packed[18].X, 3);
    }

    [Fact]
    public void The_rows_that_did_fill_up_do_not_move()
    {
        var packed = Lay(Grid(centre: false), 19);
        var centred = Lay(Grid(centre: true), 19);

        for (int i = 0; i < 14; i++)             // the first two rows of seven
            Assert.Equal(packed[i].X, centred[i].X, 3);

        // And nothing moves vertically: this is a horizontal question.
        for (int i = 0; i < 19; i++)
            Assert.Equal(packed[i].Y, centred[i].Y, 3);
    }

    [Fact]
    public void A_grid_that_comes_out_even_is_untouched()
    {
        // Fourteen fills two rows exactly. There is no slack, so there is
        // nothing to centre and the option must do nothing at all.
        var packed = Lay(Grid(centre: false), 14);
        var centred = Lay(Grid(centre: true), 14);

        for (int i = 0; i < 14; i++)
            Assert.Equal(packed[i].X, centred[i].X, 3);
    }

    [Fact]
    public void A_grid_of_one_short_row_is_the_alignments_business_not_this()
    {
        // Three items never reach a second row, and Unity makes the grid three
        // columns WIDE rather than seven with four empty - so there is no full
        // row above for the last one to line up with. Nothing to centre
        // against; where the block sits is what Alignment is for.
        var packed = Lay(Grid(centre: false), 3);
        var centred = Lay(Grid(centre: true), 3);

        for (int i = 0; i < 3; i++)
            Assert.Equal(packed[i].X, centred[i].X, 3);

        // And the alignment still does its own job on the block as a whole.
        var middled = Grid(centre: false);
        middled.Alignment = "UpperCenter";
        var block = Lay(middled, 3);

        _out.WriteLine($"left {packed[0].X}, centred block {block[0].X}");
        Assert.True(block[0].X > packed[0].X);
    }

    [Fact]
    public void Starting_from_the_right_slides_the_other_way()
    {
        // The slack is on the left when the grid fills right-to-left, so
        // centring has to move the row LEFT. Adding the same offset either way
        // would push it further into the corner it is already in.
        var packed = Lay(Grid(centre: false, startCorner: "UpperRight"), 19);
        var centred = Lay(Grid(centre: true, startCorner: "UpperRight"), 19);

        double shift = centred[14].X - packed[14].X;
        _out.WriteLine($"shift {shift}");
        Assert.Equal(-(7 - 5) * 0.5 * (Cell + Gap), shift, 3);
    }

    [Fact]
    public void A_grid_that_fills_downwards_centres_the_last_column_instead()
    {
        // Filling downwards, the incomplete line is a COLUMN, and centring it
        // is a vertical question. Treating it as horizontal would move the
        // wrong things in the wrong direction.
        var layout = Grid(centre: true, startAxis: "Vertical");
        layout.Constraint = "FixedRowCount";
        layout.ConstraintCount = 7;

        var plain = Grid(centre: false, startAxis: "Vertical");
        plain.Constraint = "FixedRowCount";
        plain.ConstraintCount = 7;

        var packed = Lay(plain, 19);
        var centred = Lay(layout, 19);

        // Last column holds five of seven, so it slides DOWN by one cell.
        double expected = (7 - 5) * 0.5 * (Cell + Gap);
        _out.WriteLine($"vertical shift {packed[14].Y - centred[14].Y}");

        Assert.Equal(expected, packed[14].Y - centred[14].Y, 3);
        Assert.Equal(packed[14].X, centred[14].X, 3);     // and not sideways
    }
}
