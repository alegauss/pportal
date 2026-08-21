using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP211: the mapping grid's own navigation, which no device is needed to state.
///
/// PP18 says this screen cannot be proved without a pad in the room, and that is true of the half
/// that lights up a press. It is not true of where the focus goes: that is three columns, a
/// checkbox in the first cell, and arithmetic. Every number below is read off
/// ControllerMappingDialog.qml, and the last test holds it against that file.
/// </summary>
public class MappingGridFocusTests
{
    /// <summary>The mapping the Qt client actually draws is twenty-odd rows, not three.</summary>
    private const int RealRows = 20;

    private static MappingGridFocus Grid(int rows = RealRows) => new(rows);

    /// <summary>
    /// The checkbox takes the first cell, so a mapping row is one column further right than its
    /// index - which is the whole of why the guards are written with an index+1 in them.
    /// </summary>
    [Fact]
    public void TheCheckBoxTakesTheGridsFirstCell()
    {
        Assert.Equal(1, MappingGridFocus.ColumnOf(0));
        Assert.Equal(2, MappingGridFocus.ColumnOf(1));
        Assert.Equal(0, MappingGridFocus.ColumnOf(2));
        Assert.Equal(1, MappingGridFocus.ColumnOf(3));
    }

    /// <summary>Right off the checkbox is the first row's first slot, its neighbour in the grid.</summary>
    [Fact]
    public void RightFromTheCheckBoxIsTheFirstSlot()
        => Assert.Equal(
            new FocusMove(MappingGridFocus.StopOf(0, 0), Handled: true),
            Grid().Right(MappingGridFocus.CheckBox));

    /// <summary>And left off the first row is the checkbox again, going back the way it came.</summary>
    [Fact]
    public void LeftFromTheFirstRowIsTheCheckBox()
        => Assert.Equal(
            new FocusMove(MappingGridFocus.CheckBox, Handled: true),
            Grid().Left(MappingGridFocus.StopOf(0, 0)));

    /// <summary>
    /// The first slot of a row in the leftmost column has nothing to its left, and says so by not
    /// consuming the key rather than by swallowing it.
    /// </summary>
    [Fact]
    public void LeftIsRefusedAtTheLeftEdge()
    {
        MappingGridFocus grid = Grid();

        foreach (int row in new[] { 2, 5, 8 })
        {
            Assert.Equal(0, MappingGridFocus.ColumnOf(row));

            int stop = MappingGridFocus.StopOf(row, 0);
            Assert.Equal(new FocusMove(stop, Handled: false), grid.Left(stop));
        }
    }

    /// <summary>The second slot always has the first one beside it, left edge or not.</summary>
    [Fact]
    public void LeftFromTheSecondSlotIsAlwaysTheFirst()
        => Assert.Equal(
            new FocusMove(MappingGridFocus.StopOf(2, 0), Handled: true),
            Grid().Left(MappingGridFocus.StopOf(2, 1)));

    /// <summary>The rightmost column is where a row runs out, and only its second slot asks.</summary>
    [Fact]
    public void RightIsRefusedAtTheRightEdge()
    {
        MappingGridFocus grid = Grid();

        foreach (int row in new[] { 1, 4, 7 })
        {
            Assert.Equal(2, MappingGridFocus.ColumnOf(row));

            int stop = MappingGridFocus.StopOf(row, 1);
            Assert.Equal(new FocusMove(stop, Handled: false), grid.Right(stop));
        }
    }

    /// <summary>Anywhere else, right off the second slot is the next row's first.</summary>
    [Fact]
    public void RightFromTheSecondSlotCrossesToTheNextRow()
        => Assert.Equal(
            new FocusMove(MappingGridFocus.StopOf(3, 0), Handled: true),
            Grid().Right(MappingGridFocus.StopOf(2, 1)));

    /// <summary>
    /// The last row is refused too, and that is a SECOND guard rather than the same one: with
    /// twenty-one rows the last sits in the leftmost column, where the edge test would let it
    /// step into a slot the grid does not draw.
    /// </summary>
    [Fact]
    public void RightIsRefusedOnTheLastRowWhereverItSits()
    {
        const int rows = 21;
        int last = rows - 1;

        Assert.NotEqual(2, MappingGridFocus.ColumnOf(last));

        int stop = MappingGridFocus.StopOf(last, 1);
        Assert.Equal(new FocusMove(stop, Handled: false), new MappingGridFocus(rows).Right(stop));
    }

    /// <summary>The top grid row has nothing above it, and neither does the checkbox.</summary>
    [Fact]
    public void UpIsRefusedOnTheTopGridRow()
    {
        MappingGridFocus grid = Grid();

        Assert.Equal(
            new FocusMove(MappingGridFocus.CheckBox, Handled: false),
            grid.Up(MappingGridFocus.CheckBox));

        foreach (int row in new[] { 0, 1 })
        {
            foreach (int slot in new[] { 0, 1 })
            {
                int stop = MappingGridFocus.StopOf(row, slot);
                Assert.Equal(new FocusMove(stop, Handled: false), grid.Up(stop));
            }
        }
    }

    /// <summary>A vertical step is a whole grid row - three mapping rows - and keeps its slot.</summary>
    [Fact]
    public void UpCrossesAWholeGridRow()
    {
        MappingGridFocus grid = Grid();

        Assert.Equal(
            new FocusMove(MappingGridFocus.StopOf(2, 0), Handled: true),
            grid.Up(MappingGridFocus.StopOf(5, 0)));

        Assert.Equal(
            new FocusMove(MappingGridFocus.StopOf(1, 1), Handled: true),
            grid.Up(MappingGridFocus.StopOf(4, 1)));
    }

    /// <summary>
    /// Except from the row directly under the checkbox, where the step is one stop shorter -
    /// the checkbox is one stop where a mapping row is two, and this is the only length that
    /// lands on it rather than past it.
    /// </summary>
    [Fact]
    public void UpFromTheRowUnderTheCheckBoxLandsOnIt()
    {
        MappingGridFocus grid = Grid();

        Assert.Equal(
            new FocusMove(MappingGridFocus.CheckBox, Handled: true),
            grid.Up(MappingGridFocus.StopOf(2, 0)));

        Assert.Equal(
            new FocusMove(MappingGridFocus.CheckBox, Handled: true),
            grid.Up(MappingGridFocus.StopOf(2, 1)));
    }

    /// <summary>Down is the same step the other way.</summary>
    [Fact]
    public void DownCrossesAWholeGridRow()
        => Assert.Equal(
            new FocusMove(MappingGridFocus.StopOf(3, 0), Handled: true),
            Grid().Down(MappingGridFocus.StopOf(0, 0)));

    /// <summary>And is refused within three rows of the end, so it never leaves the grid.</summary>
    [Fact]
    public void DownIsRefusedWithinThreeRowsOfTheEnd()
    {
        MappingGridFocus grid = Grid();

        foreach (int row in new[] { 17, 18, 19 })
        {
            int stop = MappingGridFocus.StopOf(row, 0);
            Assert.Equal(new FocusMove(stop, Handled: false), grid.Down(stop));
        }

        Assert.Equal(
            new FocusMove(MappingGridFocus.StopOf(19, 0), Handled: true),
            grid.Down(MappingGridFocus.StopOf(16, 0)));
    }

    /// <summary>
    /// The defect, reproduced. Down off the CHECKBOX counts the same five stops with nothing
    /// asking how many rows exist, so a grid too small to have a second row of cells lands on a
    /// stop that is not drawn. In the Qt client the tab chain wraps rather than stopping, which
    /// means focus leaves the screen. The real mapping has twenty rows and it never bites.
    /// </summary>
    [Fact]
    public void DownFromTheCheckBoxIsGuardedByNothing()
    {
        var small = new MappingGridFocus(2);
        FocusMove off = small.Down(MappingGridFocus.CheckBox);

        Assert.True(off.Handled);
        Assert.False(small.IsOnTheGrid(off.Next));

        MappingGridFocus real = Grid();
        FocusMove onto = real.Down(MappingGridFocus.CheckBox);

        Assert.Equal(new FocusMove(MappingGridFocus.StopOf(2, 0), Handled: true), onto);
        Assert.True(real.IsOnTheGrid(onto.Next));
    }

    /// <summary>
    /// A stop knows its row and slot, which is what lets the grid be addressed by either.
    /// </summary>
    [Fact]
    public void AStopKnowsWhichRowAndSlotItIs()
    {
        for (int row = 0; row < RealRows; row++)
        {
            for (int slot = 0; slot < 2; slot++)
            {
                int stop = MappingGridFocus.StopOf(row, slot);
                Assert.Equal(row, MappingGridFocus.RowOf(stop));
                Assert.Equal(slot, MappingGridFocus.SlotOf(stop));
            }
        }

        Assert.Equal(1 + (2 * RealRows), Grid().StopCount);
    }

    /// <summary>Every number above, still written the same way in the screen it was read from.</summary>
    [Fact]
    public void TheGridsArithmeticIsStillTheQtClients()
    {
        string? file = ControllerMappingScreenSource.Locate();
        if (file is null)
            return;

        string qml = File.ReadAllText(file);

        Assert.True(MappingGridFocusSource.TheGridIsStillThreeColumns(qml), "three columns");
        Assert.True(MappingGridFocusSource.TheCheckBoxIsStillInTheGrid(qml), "the checkbox is a cell");
        Assert.True(MappingGridFocusSource.LeftIsStillRefusedAtTheLeftEdge(qml), "the left edge");
        Assert.True(MappingGridFocusSource.RightIsStillRefusedAtTheRightEdge(qml), "the right edge");
        Assert.True(MappingGridFocusSource.AVerticalStepIsStillAWholeRow(qml), "six stops up or down");
        Assert.True(MappingGridFocusSource.TheRowUnderTheCheckBoxStillCountsFive(qml), "and five over the box");
        Assert.True(MappingGridFocusSource.TheCheckBoxDownIsStillUnguarded(qml), "the unguarded one");
    }
}
