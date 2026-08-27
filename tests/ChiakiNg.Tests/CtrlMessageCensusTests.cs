using System.Reflection;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP440, under PP294: ctrl.c's 22 message types, each with the managed type that answers it.
///
/// The census exists because three readers gave three answers. Hex matching found 0x1 inside 0x13;
/// C-constant matching reported nine types as unported and all nine were answered. What the right
/// reader found is that both enums carry all 22 with the same values and nothing checked that.
/// </summary>
public class CtrlMessageCensusTests(ITestOutputHelper output)
{
    /// <summary>
    /// THE RULE. The census is exactly what ctrl.c declares, by value.
    /// </summary>
    [Fact]
    public void TheCensusIsExactlyWhatTheCDeclares()
    {
        if (CtrlMessageCensus.LocateCtrl() is not { } path)
            return;

        string source = File.ReadAllText(path);

        IReadOnlyList<(string CName, ushort Value)> declared = CtrlMessageCensus.Declared(source);
        output.WriteLine($"{declared.Count} declared in ctrl.c, {CtrlMessageCensus.Rows.Count} rows here");

        // PP271: a reader that found no members would agree with any census at all.
        Assert.True(declared.Count >= 20, $"only {declared.Count} enum members read from ctrl.c");

        IReadOnlyList<string> apart = CtrlMessageCensus.Disagreements(source);

        Assert.True(
            apart.Count == 0,
            "the census and ctrl.c's enum disagree, so what is left of PP294 is being measured "
                + "against the wrong list:\n  " + string.Join("\n  ", apart));
    }

    /// <summary>
    /// AND THE MANAGED ENUM CARRIES THE SAME NUMBERS. Two declarations of one wire contract, which
    /// is what nothing checked.
    ///
    /// By value and not by name: DISPLAYA maps to DisplayA and GOTO_BED to GotoBed, so no
    /// snake-to-Pascal transform gets both - and the number is what a console actually sends.
    /// </summary>
    [Fact]
    public void TheManagedEnumCarriesTheSameValues()
    {
        if (CtrlMessageCensus.LocateCtrl() is not { } path)
            return;

        ushort[] fromC = [.. CtrlMessageCensus.Declared(File.ReadAllText(path)).Select(t => t.Value)];
        ushort[] managed = [.. Enum.GetValues<CtrlMessage>().Select(v => (ushort)v)];

        Assert.True(fromC.Length >= 20, "the C reader is not working");

        ushort[] missing = [.. fromC.Except(managed).Order()];
        ushort[] extra = [.. managed.Except(fromC).Order()];

        Assert.True(
            missing.Length == 0,
            "ctrl.c declares message types CtrlMessage does not, so a rewrite would drop them "
                + "silently: " + string.Join(", ", missing.Select(v => $"0x{v:x}")));

        Assert.True(
            extra.Length == 0,
            "CtrlMessage names values ctrl.c does not declare: "
                + string.Join(", ", extra.Select(v => $"0x{v:x}")));
    }

    /// <summary>
    /// Every row names a type that exists, so the census cannot outlive the classes it points at.
    ///
    /// Reflection rather than a string compared with a string: a class renamed in a refactor takes
    /// its row with it or turns this red, and prose would have done neither.
    /// </summary>
    [Fact]
    public void EveryRowNamesATypeThatExists()
    {
        Assembly host = typeof(CtrlMessageCensus).Assembly;

        foreach (CtrlMessageRow row in CtrlMessageCensus.Rows)
        {
            Type? answered = host.GetTypes()
                .FirstOrDefault(t => string.Equals(t.Name, row.AnsweredBy, StringComparison.Ordinal));

            Assert.True(
                answered is not null,
                $"{row.CName} is answered by {row.AnsweredBy}, which is not a type in this assembly");
        }
    }

    /// <summary>
    /// And every row says what that type answers, because a class name alone does not tell a reader
    /// choosing a --part whether the work is done or merely adjacent.
    /// </summary>
    [Fact]
    public void EveryRowSaysWhatIsAnswered()
    {
        foreach (CtrlMessageRow row in CtrlMessageCensus.Rows)
        {
            Assert.True(
                row.Because.Length >= 20,
                $"{row.CName}'s row carries no phrase a reader could act on");
        }
    }

    /// <summary>
    /// The nine the C-constant reader called unported are all in the census, named so that the false
    /// negative is recorded rather than only remembered.
    /// </summary>
    [Theory]
    [InlineData("GOTO_BED")]
    [InlineData("KEYBOARD_OPEN")]
    [InlineData("KEYBOARD_CLOSE_REMOTE")]
    [InlineData("GO_HOME")]
    [InlineData("DISPLAYA")]
    [InlineData("DISPLAYB")]
    [InlineData("MIC_CONNECT")]
    [InlineData("MIC_TOGGLE")]
    [InlineData("SWITCH_TO_STREAM_CONNECTION")]
    public void TheNineTheConstantReaderMissedAreAnswered(string cName)
    {
        CtrlMessageRow row = Assert.Single(CtrlMessageCensus.Rows, r => r.CName == cName);
        Assert.NotEqual("", row.AnsweredBy);
    }

    /// <summary>
    /// A disagreement is reported either way, on synthetic text - the real enum has to agree and so
    /// cannot be the fixture for the case that matters.
    /// </summary>
    [Fact]
    public void ADisagreementIsReportedEitherWay()
    {
        // A twenty-third type, which is the one §PP294 warns about.
        IReadOnlyList<string> added = CtrlMessageCensus.Disagreements(
            "CTRL_MESSAGE_TYPE_SOMETHING_NEW = 0x77,\n");

        Assert.Contains(added, line => line.Contains("SOMETHING_NEW", StringComparison.Ordinal)
            && line.Contains("no row", StringComparison.Ordinal));

        // And a value that moved under a name the census already has.
        IReadOnlyList<string> moved = CtrlMessageCensus.Disagreements(
            "CTRL_MESSAGE_TYPE_SESSION_ID = 0x99,\n");

        Assert.Contains(moved, line => line.Contains("SESSION_ID", StringComparison.Ordinal)
            && line.Contains("0x99", StringComparison.Ordinal));
    }

    /// <summary>PP400: a constant named in a comment is not one the file declares.</summary>
    [Fact]
    public void ACommentedConstantIsNotDeclared()
    {
        Assert.Empty(CtrlMessageCensus.Declared(
            "// CTRL_MESSAGE_TYPE_REMOVED_LAST_YEAR = 0x99,\n"));

        Assert.Empty(CtrlMessageCensus.Declared(
            "/* PP413: CTRL_MESSAGE_TYPE_SESSION_ID = 0x33 is what the arm above reads. */\n"));
    }

    /// <summary>PP272: and an empty file declares nothing.</summary>
    [Fact]
    public void AnEmptyFileDeclaresNothing()
    {
        Assert.Empty(CtrlMessageCensus.Declared(""));

        // Every row then reads as missing from the C, which is the honest answer and not silence.
        Assert.Equal(CtrlMessageCensus.Rows.Count, CtrlMessageCensus.Disagreements("").Count);
    }
}
