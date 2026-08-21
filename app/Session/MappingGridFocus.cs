namespace ChiakiNg.Session;

/// <summary>
/// PP211: how focus moves inside the controller mapping grid, which is not how it moves anywhere
/// else in this port.
///
/// <see cref="FocusChain"/> settled the shared vocabulary: the six controls in
/// gui/src/qml/controls handle Up and Down, both walk the tab chain, and Left and Right are left
/// alone so a slider still changes its value. ControllerMappingDialog.qml is a SCREEN and not one
/// of those controls, and it answers all four - which means reusing the shared chain here would
/// navigate the one screen that differs in exactly the way it differs.
///
/// The whole file is one piece of arithmetic spelled several ways. The stops sit in a GridLayout
/// of three columns whose FIRST CELL IS THE ANALOG CHECKBOX, so mapping row i occupies cell i+1
/// and its grid column is (i+1)%3. A row is two tab stops - its two binding slots - and the
/// checkbox is one, which is the whole reason the vertical jumps are not all the same size.
///
/// Deliberately without a visual tree, for the reason <see cref="FocusChainBehavior.Decide"/>
/// gives: moving focus needs a window, and deciding where it goes needs none. This screen is the
/// one PP18 says cannot be proved without a pad in the room; the arithmetic below is the half
/// that can.
/// </summary>
public sealed class MappingGridFocus
{
    /// <summary>The grid's width, which every guard in the QML is a restatement of.</summary>
    public const int Columns = 3;

    /// <summary>Tab stops crossed by one vertical step: three mapping rows of two slots each.</summary>
    private const int RowStride = 2 * Columns;

    /// <summary>
    /// The same step taken from the row directly under the checkbox, which is one stop and not
    /// two. The QML writes this as <c>index == 2 ? 5 : 6</c>, and read as a magic number it looks
    /// arbitrary - it is the only value that lands on the checkbox rather than past it.
    /// </summary>
    private const int StrideOverTheCheckBox = RowStride - 1;

    private readonly int rows;

    /// <param name="rows">How many chiaki buttons the mapping draws, one row each.</param>
    public MappingGridFocus(int rows)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rows);
        this.rows = rows;
    }

    /// <summary>The analog opt-in, which is the first stop and shares the grid's first cell.</summary>
    public const int CheckBox = 0;

    /// <summary>Every stop: the checkbox, then two slots per row.</summary>
    public int StopCount => 1 + (2 * rows);

    /// <summary>The tab stop a row's slot occupies.</summary>
    public static int StopOf(int row, int slot) => 1 + (2 * row) + slot;

    /// <summary>Which mapping row a stop belongs to. Undefined for the checkbox.</summary>
    public static int RowOf(int stop) => (stop - 1) / 2;

    /// <summary>Which of the row's two binding slots a stop is.</summary>
    public static int SlotOf(int stop) => (stop - 1) % 2;

    /// <summary>Which of the three grid columns a mapping row sits in, the checkbox taking one.</summary>
    public static int ColumnOf(int row) => (row + 1) % Columns;

    /// <summary>Whether a stop is one this grid actually draws.</summary>
    public bool IsOnTheGrid(int stop) => stop >= 0 && stop < StopCount;

    /// <summary>
    /// Left. The checkbox does not answer it at all - it sits in the leftmost column with nothing
    /// beside it - and the first slot of a row refuses it at the left edge, which is where the
    /// column arithmetic is stated as <c>(index + 1) % 3 != 0</c>.
    /// </summary>
    public FocusMove Left(int stop)
    {
        Validate(stop);

        if (stop == CheckBox)
            return Stay(stop);

        // The second slot always has the first one beside it, so only the first slot asks.
        if (SlotOf(stop) == 0 && ColumnOf(RowOf(stop)) == 0)
            return Stay(stop);

        return new FocusMove(stop - 1, Handled: true);
    }

    /// <summary>
    /// Right. The checkbox and the first slot always have somewhere to go; the second slot is the
    /// one at the right edge, and is also the one that can be last in the chain.
    /// </summary>
    public FocusMove Right(int stop)
    {
        Validate(stop);

        if (stop == CheckBox)
            return new FocusMove(StopOf(0, 0), Handled: true);

        if (SlotOf(stop) == 0)
            return new FocusMove(stop + 1, Handled: true);

        int row = RowOf(stop);

        // Two guards where one would do: the QML asks lastInFocusChain as well as the column, and
        // the column alone would let the very last row step into a stop that is not drawn.
        if (row == rows - 1 || ColumnOf(row) == Columns - 1)
            return Stay(stop);

        return new FocusMove(stop + 1, Handled: true);
    }

    /// <summary>
    /// Up. A whole grid row at a time, refused from the top row - and from the row under the
    /// checkbox the step is one stop shorter, because the checkbox is one stop where a mapping
    /// row is two.
    /// </summary>
    public FocusMove Up(int stop)
    {
        Validate(stop);

        // The checkbox has no Up case in the QML at all: the key falls through to whatever
        // contains the screen, which is the same boundary rule FocusChain states.
        if (stop == CheckBox)
            return Stay(stop);

        int row = RowOf(stop);
        if (row <= 1)
            return Stay(stop);

        int stride = SlotOf(stop) == 0 && row == Columns - 1 ? StrideOverTheCheckBox : RowStride;
        return new FocusMove(stop - stride, Handled: true);
    }

    /// <summary>
    /// Down, and the one move on this screen that no guard covers.
    ///
    /// From a slot it is refused within three rows of the end, so it never leaves the grid. From
    /// the CHECKBOX it counts five stops with nothing asking how many rows exist - and
    /// nextItemInFocusChain wraps rather than stopping, so on a grid of fewer than three rows the
    /// Qt client walks out of the screen entirely. The real mapping has twenty rows and it never
    /// bites; this returns the stop the QML lands on either way, and
    /// <see cref="IsOnTheGrid"/> is what says whether that stop exists. Reproduced, not fixed.
    /// </summary>
    public FocusMove Down(int stop)
    {
        Validate(stop);

        if (stop == CheckBox)
            return new FocusMove(StrideOverTheCheckBox, Handled: true);

        int row = RowOf(stop);
        if (row >= rows - Columns)
            return Stay(stop);

        return new FocusMove(stop + RowStride, Handled: true);
    }

    /// <summary>Refusing to move is also refusing the key, so it reaches whatever contains us.</summary>
    private static FocusMove Stay(int stop) => new(stop, Handled: false);

    private void Validate(int stop)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(stop);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(stop, StopCount);
    }
}

/// <summary>
/// PP211: the grid's arithmetic where the Qt client writes it down.
///
/// Every constant above is a number read out of one file. If that file changes shape, these say
/// so - which is the only thing standing between this port and a screen that navigates from
/// memory.
/// </summary>
public static class MappingGridFocusSource
{
    /// <summary>Whether the grid is still three columns wide.</summary>
    public static bool TheGridIsStillThreeColumns(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains("columns: 3", StringComparison.Ordinal);
    }

    /// <summary>Whether the analog opt-in still shares the grid with the rows, taking its first cell.</summary>
    public static bool TheCheckBoxIsStillInTheGrid(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);

        int grid = qml.IndexOf("columns: 3", StringComparison.Ordinal);
        int box = qml.IndexOf("id: analogStickMapping", StringComparison.Ordinal);
        int repeater = qml.IndexOf("id: chiakiButtons", StringComparison.Ordinal);

        return grid >= 0 && box > grid && repeater > box;
    }

    /// <summary>Whether Left is still refused by the column and not by a boundary flag.</summary>
    public static bool LeftIsStillRefusedAtTheLeftEdge(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains("(((index + 1)% 3) != 0)", StringComparison.Ordinal);
    }

    /// <summary>And whether Right is still refused by the column at the other end.</summary>
    public static bool RightIsStillRefusedAtTheRightEdge(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains("((index - 1) % 3) != 0", StringComparison.Ordinal);
    }

    /// <summary>Whether a vertical step is still a whole grid row of six tab stops.</summary>
    public static bool AVerticalStepIsStillAWholeRow(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains("for(var i = 0; i < 6; i++)", StringComparison.Ordinal)
            && qml.Contains("index < (chiakiButtons.count - 3)", StringComparison.Ordinal);
    }

    /// <summary>Whether the row under the checkbox still takes the shorter step.</summary>
    public static bool TheRowUnderTheCheckBoxStillCountsFive(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains("let count = index == 2 ? 5 : 6;", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the checkbox's own Down is still counted out with nothing asking how many rows
    /// there are. True means the defect is still present, which is what this asserts.
    /// </summary>
    public static bool TheCheckBoxDownIsStillUnguarded(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);

        int box = qml.IndexOf("id: analogStickMapping", StringComparison.Ordinal);
        int repeater = qml.IndexOf("id: chiakiButtons", StringComparison.Ordinal);
        if (box < 0 || repeater <= box)
            return false;

        string checkBox = qml[box..repeater];
        return checkBox.Contains("for(var i = 0; i < 5; i++)", StringComparison.Ordinal)
            && !checkBox.Contains("chiakiButtons.count", StringComparison.Ordinal);
    }
}
