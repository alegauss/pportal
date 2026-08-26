using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP386: the reader that asks whether a call happens, without asking how it is punctuated.
///
/// Every case here is a real edit from this port's history. The four that broke drift checks in one
/// block of work are the first four, and each is a shape the old spelling could not survive.
/// </summary>
public class CCallTests
{
    /// <summary>
    /// THE EDITS THAT BROKE CHECKS. Each row is the same call before and after a change that moved
    /// no behaviour, and the old checks quoted the left-hand form.
    ///
    /// An ASSIGNMENT is deliberately not among them, and that is worth saying because it looks like
    /// it belongs: <c>err = f(x);</c> still contains <c>f(x);</c> whole, so reading a result never
    /// broke anything. What breaks a quoted statement is losing its terminator - being wrapped in a
    /// guard, or becoming an argument - and only those shapes are here.
    /// </summary>
    [Theory]
    // PP383: two sends wrapped in a guard that reads their result.
    [InlineData(
        "ctrl_message_toggle_microphone(ctrl, false);",
        "\tCTRL_FEATURE_SEND(ctrl_message_toggle_microphone(ctrl, false), \"the first toggle\");")]
    // PP385: four calls wrapped in a guard of their own.
    [InlineData(
        "ctrl_message_set_fallback_session_id(ctrl);",
        "\t\tCTRL_FALLBACK_SESSION_ID(ctrl_message_set_fallback_session_id(ctrl));")]
    // PP385: a call passed as an argument, across two lines.
    [InlineData(
        "ctrl_message_send(ctrl, CTRL_MESSAGE_TYPE_HEARTBEAT_REP, NULL, 0);",
        "\tCTRL_SEND_OR_FAIL(\n\t\t\tctrl_message_send(ctrl, CTRL_MESSAGE_TYPE_HEARTBEAT_REP, NULL, 0),\n\t\t\t\"x\");")]
    // PP385: the login PIN, wrapped and wrapped across lines at once.
    [InlineData(
        "ctrl_message_send(ctrl, CTRL_MESSAGE_TYPE_LOGIN_PIN_REP, login_pin, login_pin_size);",
        "\t\t\t\tCTRL_SEND_OR_FAIL(\n\t\t\t\t\t\tctrl_message_send(ctrl, CTRL_MESSAGE_TYPE_LOGIN_PIN_REP, login_pin, login_pin_size),\n\t\t\t\t\t\t\"the login PIN\");")]
    public void TheCallSurvivesTheEditThatBrokeTheOldCheck(string call, string after)
    {
        // The old spelling: the statement quoted whole. This is what went red.
        Assert.DoesNotContain(call, after, StringComparison.Ordinal);

        // The claim that was actually being made.
        Assert.True(CCall.Happens(after, call));
    }

    /// <summary>Layout does not change what a call is.</summary>
    [Theory]
    [InlineData("f(a, b);", "f(a,b);")]
    [InlineData("f(a, b);", "\t\t\tf(a, b);")]
    [InlineData("f(a, b);", "f(a,\n\t\tb);")]
    [InlineData("f(a, b)", "f(a, b);")]
    [InlineData("f(a, b);", "err = f(a, b);")]
    public void TheSameCallIsFoundHoweverItIsWritten(string call, string source)
    {
        Assert.True(CCall.Happens(source, call));
    }

    /// <summary>
    /// And a DIFFERENT call is not, which is what stops the loosening from giving the check away.
    ///
    /// The closing parenthesis is the guard: <c>free(notif)</c> is not <c>free(notif->json_buf)</c>,
    /// and both are in the same function.
    /// </summary>
    [Theory]
    [InlineData("free(notif);", "free(notif->json_buf);")]
    [InlineData("f(a, b);", "f(a, c);")]
    [InlineData("f(a);", "f();")]
    [InlineData("chiaki_ctrl_stop(&session->ctrl);", "chiaki_ctrl_join(&session->ctrl);")]
    public void ADifferentCallIsNotFound(string call, string source)
    {
        Assert.False(CCall.Happens(source, call));
    }

    /// <summary>
    /// A name that merely ENDS with the sought one is not it - which removing the whitespace makes
    /// reachable, because the two become adjacent.
    /// </summary>
    [Fact]
    public void ANameThatEndsWithItIsNotIt()
    {
        Assert.False(CCall.Happens("xfree(notif);", "free(notif);"));
        Assert.False(CCall.Happens("chiaki_free(notif);", "free(notif);"));

        // But a real call preceded by punctuation is.
        Assert.True(CCall.Happens("if(!free(notif))", "free(notif)"));
        Assert.True(CCall.Happens("\tfree(notif);", "free(notif);"));
    }

    /// <summary>Counting, for the checks that assert a call happens exactly so many times.</summary>
    [Fact]
    public void ItCountsRatherThanOnlyFinding()
    {
        const string Twice = """
            	ctrl_message_toggle_microphone(ctrl, false);
            	ctrl_message_toggle_microphone(ctrl, false);
            """;

        Assert.Equal(2, CCall.Count(Twice, "ctrl_message_toggle_microphone(ctrl, false);"));
        Assert.Equal(0, CCall.Count(Twice, "ctrl_message_toggle_microphone(ctrl, true);"));
    }

    /// <summary>Ordering, which is the other claim these checks make.</summary>
    [Fact]
    public void ItAnswersOrdering()
    {
        const string Teardown = """
            	chiaki_ctrl_stop(&session->ctrl);
            	chiaki_ctrl_join(&session->ctrl);
            	chiaki_session_send_event(session, &quit_event);
            """;

        Assert.True(CCall.InOrder(
            Teardown,
            "chiaki_ctrl_stop(&session->ctrl)",
            "chiaki_ctrl_join(&session->ctrl)",
            "chiaki_session_send_event(session, &quit_event)"));

        Assert.False(CCall.InOrder(
            Teardown,
            "chiaki_ctrl_join(&session->ctrl)",
            "chiaki_ctrl_stop(&session->ctrl)"));

        // A missing call is not an order.
        Assert.False(CCall.InOrder(Teardown, "chiaki_ctrl_stop(&session->ctrl)", "nothing(here)"));
    }

    /// <summary>
    /// PP272: it answers no about a file with nothing in it, and about a call that is nothing.
    /// </summary>
    [Fact]
    public void ItReadsWhatItIsGiven()
    {
        Assert.False(CCall.Happens("", "free(notif);"));
        Assert.Equal(0, CCall.Count("", "free(notif);"));
        Assert.Equal(-1, CCall.At("", "free(notif);"));
        Assert.False(CCall.InOrder("", "free(notif)"));

        // A call that is only a terminator claims nothing, and says so.
        Assert.Equal(0, CCall.Count("free(notif);", ";"));
        Assert.False(CCall.InOrder("free(notif);"));
    }

    /// <summary>Compacting is what makes the rest of it work, so it is stated on its own.</summary>
    [Fact]
    public void CompactingRemovesLayoutAndNothingElse()
    {
        Assert.Equal("f(a,b);", CCall.Compact("\tf(a,\n\t\tb); "));
        Assert.Equal("f(md,md+0x10,0x10);", CCall.Compact("f(md, md + 0x10, 0x10);"));
        Assert.Equal("", CCall.Compact("  \t\r\n "));
        Assert.Equal("", CCall.Compact(""));

        // TOKENS STAY SEPARATE. Deleting layout must not create an adjacency the C never had.
        Assert.Equal("ChiakiErrorCode err=f(x);", CCall.Compact("ChiakiErrorCode err = f(x);"));
    }

    /// <summary>
    /// THE TRAP THE FIRST VERSION FELL INTO. A preprocessor line above the call.
    ///
    /// Removing every space welded <c>#endif</c> to <c>xor_bytes</c>, so the call began
    /// mid-identifier and the boundary test refused it - a false negative introduced by the reader
    /// written to remove false positives. gkcrypt.c really is written this way.
    /// </summary>
    [Fact]
    public void ACallUnderAPreprocessorLineIsStillACall()
    {
        const string Real = """
            	SHA256(data, 0x20, md);
            #endif
            	xor_bytes(md, md + 0x10, 0x10);
            """;

        Assert.True(CCall.Happens(Real, "xor_bytes(md, md + 0x10, 0x10);"));
        Assert.Equal(1, CCall.Count(Real, "xor_bytes(md, md + 0x10, 0x10);"));

        // And the boundary test still does its job on a name that really does end with it.
        Assert.False(CCall.Happens("#endif\n\tmy_xor_bytes(md, md + 0x10, 0x10);", "xor_bytes(md, md + 0x10, 0x10);"));
    }
}
