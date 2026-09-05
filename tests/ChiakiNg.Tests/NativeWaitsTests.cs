using ChiakiNg.Native;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP585: the timing constants across the seam, and which are meant to agree.
///
/// The family PP577 to PP581 held enums, a struct size, a symbol table and six callback signatures
/// against the C. This is the same question for waits, and the failure it catches is quieter: a macro
/// moved upstream leaves the port waiting a different length, which no crash reports.
/// </summary>
public class NativeWaitsTests
{
    private static string? Read(string relativePath)
    {
        string? path = NativeWaits.Locate(relativePath);
        return path is null ? null : File.ReadAllText(path);
    }

    /// <summary>
    /// THE RULE: every macro a managed constant follows still says what the row says, and the managed
    /// constant is still that number.
    /// </summary>
    [Fact]
    public void EveryMirroredWaitStillMatchesTheMacro()
    {
        foreach (NativeWait wait in NativeWaits.Mirrored)
        {
            if (Read(wait.SourceRelativePath) is not { } source)
                return;

            string? body = NativeWaits.BodyOf(source, wait.Name);
            Assert.True(body is not null, $"{wait.SourceRelativePath} no longer defines {wait.Name}");
            Assert.Equal(wait.CText, body);

            // Where the body is a plain number the managed side has to BE it. Where it is an
            // expression - the two halvings - the C's own arithmetic is the claim, and the managed
            // constant is derived the same way rather than typed.
            if (NativeWaits.NumberOf(body!) is { } number)
                Assert.Equal(number, wait.Managed);
            else
                Assert.NotNull(wait.Managed);
        }
    }

    /// <summary>
    /// The two halvings resolve to half of the row above them, on both sides.
    ///
    /// Asserted as arithmetic rather than as 200 and 100, so changing the base moves the derived value
    /// and this stays true - which is the property the C's expression has and a typed number does not.
    /// </summary>
    [Fact]
    public void TheTwoWakeupWaitsAreHalfTheirResendClock()
    {
        Assert.Equal(
            ChiakiNg.Protocol.RudpSendBuffer.ResendTimeoutMs / 2,
            ChiakiNg.Protocol.RudpSendBuffer.ResendWakeupTimeoutMs);

        Assert.Equal(
            ChiakiNg.Protocol.TakionResendLoop.ResendTimeoutMs / 2,
            ChiakiNg.Protocol.TakionResendLoop.WakeupTimeoutMs);
    }

    /// <summary>
    /// The four waits the C never named are still written where the row says, and still that number.
    ///
    /// THIS IS THE HALF A COUNT CANNOT DO. Each of the four sits in a file that defines a macro with
    /// the same value for a different wait, so a join by number would bind the port to the wrong one
    /// and agree until upstream moved either.
    /// </summary>
    [Fact]
    public void TheFourUnnamedWaitsAreStillAtTheirCallSites()
    {
        foreach (NativeWait wait in NativeWaits.Literals)
        {
            if (Read(wait.SourceRelativePath) is not { } source)
                return;

            Assert.True(
                source.Contains(wait.Name, StringComparison.Ordinal),
                $"{wait.SourceRelativePath} no longer contains {wait.Name}");

            Assert.NotNull(wait.Managed);
        }
    }

    /// <summary>
    /// And they are genuinely a trap: each one's file defines a macro holding the SAME number for
    /// something else. Stated as an assertion so that if upstream ever separates them, this stops
    /// claiming a hazard that has gone.
    ///
    /// PP632: session.c's row went with the registration wait it named, so this is one case now.
    /// SESSION_EXPECT_CTRL_START_MS is still 10000 and still waits on the ctrl start - what left is
    /// the OTHER wait that shared the number, so there is nothing left for a reader to confuse it
    /// with in that file.
    /// </summary>
    [Theory]
    [InlineData(@"lib\src\ctrl.c", "CTRL_EXPECT_TIMEOUT", 5000.0)]
    public void AMacroInTheSameFileHoldsTheSameNumberForSomethingElse(
        string relativePath, string macro, double shared)
    {
        if (Read(relativePath) is not { } source)
            return;

        string? body = NativeWaits.BodyOf(source, macro);
        Assert.NotNull(body);
        Assert.Equal(shared, NativeWaits.NumberOf(body!));

        NativeWait literal = NativeWaits.Literals.Single(w => w.SourceRelativePath == relativePath);
        Assert.Equal(shared, literal.Managed);
    }

    /// <summary>
    /// COMPLETENESS: every timing macro in the thirteen files is accounted for by a row.
    ///
    /// This is what makes the list a rule rather than a snapshot. A macro added upstream that no row
    /// names turns this red in the commit that vendors it, which is the whole reason PP585 was a line
    /// and not a comment.
    /// </summary>
    [Fact]
    public void EveryTimingMacroInTheCIsAccountedFor()
    {
        var unaccounted = new List<string>();
        var seen = 0;

        foreach (string relativePath in NativeWaits.Sources)
        {
            if (Read(relativePath) is not { } source)
                return;

            foreach (string macro in NativeWaits.MacrosIn(source))
            {
                seen++;
                bool named = NativeWaits.All.Any(
                    w => w.SourceRelativePath == relativePath && w.Name == macro);

                if (!named)
                    unaccounted.Add($"{relativePath}: {macro}");
            }
        }

        Assert.Empty(unaccounted);

        // The count, so a file dropping out of Sources cannot make the sweep pass by finding nothing.
        Assert.Equal(32, seen);
    }

    /// <summary>
    /// The groups add up, and twenty-two of the thirty-two macros have a managed constant behind them.
    ///
    /// Written as the split rather than as a total, because "31 and 33" was the reading this task
    /// started from and the two numbers never joined to each other.
    ///
    /// PP632: three literals, not four. session.c's registration wait was reached only through the
    /// holepunch handle, so it went with the nine - and a row for a wait that is not in the file
    /// could not be joined to anything, which is what this list is for.
    ///
    /// PP718: twenty and twelve, not nineteen and thirteen. PP714 ported congestion control and
    /// this split stayed valid across the move, which is exactly why it was not enough on its own -
    /// see TheUnportedGroupDoesNotClaimAPortedFile.
    ///
    /// PP723: twenty-two and ten. The feedback sender's two ends of one window moved together,
    /// because the thread that waits the outer one is what the task wrote.
    /// </summary>
    [Fact]
    public void TheGroupsAreTwentyTwoThreeOneAndTen()
    {
        Assert.Equal(22, NativeWaits.Mirrored.Count);
        Assert.Equal(3, NativeWaits.Literals.Count);
        Assert.Single(NativeWaits.Departures);
        Assert.Equal(10, NativeWaits.Unported.Count);

        // Macros are the two groups that name one; the literals and the departure name none.
        Assert.Equal(
            32,
            NativeWaits.All.Count(w => w.Kind is WaitKind.MirrorsAMacro or WaitKind.NoCounterpartYet));

        // Nothing unported claims a managed value, and nothing mirrored omits one.
        Assert.All(NativeWaits.Unported, w => Assert.Null(w.Managed));
        Assert.All(NativeWaits.Mirrored, w => Assert.NotNull(w.Managed));
    }

    /// <summary>
    /// PP718: no unported row calls a file unported that a mirrored row already follows a macro out of.
    ///
    /// The direction the census was missing, and the one a ship falsifies. PP714 wrote congestion
    /// control, moved nothing here, and the whole gate stayed green: every macro was still accounted
    /// for and the four counts still added to 32, because a row moving between two groups keeps both
    /// of those true. What was false was the row's own sentence.
    ///
    /// A half-ported file passes this. feedbacksender.c has PP717's recorder and still owes the
    /// thread that waits its window, and its two rows say which without claiming the file is
    /// untouched - which is the difference between a row that is behind and a row that is wrong.
    /// </summary>
    [Fact]
    public void TheUnportedGroupDoesNotClaimAPortedFile()
    {
        Assert.True(
            NativeWaits.Unclaimed.Count == 0,
            "these rows call a file unported that a mirrored row follows a macro out of: "
                + string.Join(", ", NativeWaits.Unclaimed.Select(w => $"{w.Name}: {w.Note}")));

        // And the check can still fire: the phrase it looks for is one the rows really use.
        Assert.Contains(
            NativeWaits.Unported,
            w => w.Note.Contains(NativeWaits.UnportedClaim, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The name filter admits what the core writes and refuses a size or a count, which is the line
    /// between a wait and every other macro in these files.
    /// </summary>
    [Theory]
    [InlineData("CTRL_EXPECT_TIMEOUT", true)]
    [InlineData("HEARTBEAT_INTERVAL_MS", true)]
    [InlineData("SEARCH_REQUEST_SLEEP_MS", true)]
    [InlineData("SECOND_US", true)]
    [InlineData("SELECT_CANDIDATE_TRIES", false)]
    [InlineData("WEBSOCKET_MAX_FRAME_SIZE", false)]
    [InlineData("STUN_MSG_TYPE_BINDING_REQUEST", false)]
    [InlineData("SENKUSHA_PING_COUNT_DEFAULT", false)]
    public void TheNameFilterSeparatesAWaitFromASize(string name, bool expected)
        => Assert.Equal(expected, NativeWaits.IsATimingName(name));

    /// <summary>
    /// A body reads without its trailing comment, and an expression reads as no number rather than as
    /// a wrong one. TAKION_AV_REORDER_TIMEOUT_US carries "// ~1 frame at 60fps" on its own line.
    /// </summary>
    [Fact]
    public void ABodyDropsItsCommentAndAnExpressionIsNotANumber()
    {
        const string source = """
            #define TAKION_AV_REORDER_TIMEOUT_US 16000 // ~1 frame at 60fps
            #define TAKION_DATA_RESEND_TIMEOUT_MS 200
            #define TAKION_DATA_RESEND_WAKEUP_TIMEOUT_MS (TAKION_DATA_RESEND_TIMEOUT_MS/2)
            """;

        Assert.Equal("16000", NativeWaits.BodyOf(source, "TAKION_AV_REORDER_TIMEOUT_US"));
        Assert.Equal(16000.0, NativeWaits.NumberOf("16000"));

        string? derived = NativeWaits.BodyOf(source, "TAKION_DATA_RESEND_WAKEUP_TIMEOUT_MS");
        Assert.Equal("(TAKION_DATA_RESEND_TIMEOUT_MS/2)", derived);
        Assert.Null(NativeWaits.NumberOf(derived!));

        // And a prefix is not a match: the resend macro must not answer for the wakeup one.
        Assert.Equal("200", NativeWaits.BodyOf(source, "TAKION_DATA_RESEND_TIMEOUT_MS"));
    }
}
