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
    /// PP388: THE MIXING THAT MADE THE OTHER TWENTY IMPOSSIBLE.
    ///
    /// A position from At is comparable to one from Mark and to nothing else. This is the whole
    /// reason Mark exists: twenty predicates measured a call against an anchor found by a raw
    /// IndexOf, and converting only the call would have compared a compacted position with a raw
    /// one - a check that compiles, returns a bool, and means nothing.
    /// </summary>
    [Fact]
    public void AnAnchorAndACallAreMeasuredInTheSameSpace()
    {
        const string Body = """
            	ctrl_request_retry = true;

            	ctrl_disconnect_tcp(ctrl);
            	ctrl_connect_tcp(ctrl);
            """;

        int retry = CCall.Mark(Body, "ctrl_request_retry = true;");
        int disconnect = CCall.At(Body, "ctrl_disconnect_tcp(ctrl)", retry);
        int reconnect = CCall.At(Body, "ctrl_connect_tcp(ctrl)", retry);

        Assert.True(retry >= 0);
        Assert.True(disconnect > retry);
        Assert.True(reconnect > disconnect);

        // The raw index is a DIFFERENT number, which is what makes mixing them silent rather than
        // loud - both are plausible offsets into something.
        Assert.NotEqual(Body.IndexOf("ctrl_request_retry = true;", StringComparison.Ordinal), retry);
    }

    /// <summary>
    /// And a slice taken from compacted text agrees with marks into it, which is what the converted
    /// predicates rely on.
    /// </summary>
    [Fact]
    public void ASliceOfCompactedTextAgreesWithItsMarks()
    {
        const string Body = """
            	notif->json = NULL;
            	notif->json_buf = NULL;
            	free(notif);
            """;

        string compact = CCall.Compact(Body);

        int node = CCall.At(compact, "free(notif)");
        Assert.True(node >= 0);

        string before = compact[..node];

        Assert.True(CCall.Mark(before, "notif->json = NULL;") >= 0);
        Assert.True(CCall.Mark(before, "notif->json_buf = NULL;") >= 0);

        // Compacting is idempotent, so marking into an already-compacted string is the same answer.
        Assert.Equal(CCall.Compact(compact), compact);
    }

    /// <summary>An anchor is not a call, so it gets no identifier-boundary test.</summary>
    [Fact]
    public void AnAnchorNeedNotBeAName()
    {
        Assert.True(CCall.Mark("if(retry)\n\tf(x);", "if(retry)") >= 0);
        Assert.True(CCall.Mark("\tsession->gw_status = GATEWAY_STATUS_FOUND;", "gw_status = GATEWAY_STATUS_FOUND") >= 0);
        Assert.True(CCall.Mark("quit:\n\treturn err;", "quit:") >= 0);

        Assert.Equal(-1, CCall.Mark("f(x);", "nothing here"));
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
        Assert.Equal(-1, CCall.Mark("", "free(notif);"));
        Assert.False(CCall.InOrder("", "free(notif)"));

        // And a start past the end is an answer rather than a throw, which a converted predicate
        // reaches whenever its first mark lands near the tail.
        Assert.Equal(-1, CCall.Mark("f(x);", "f(x)", 99));

        // A call that is only a terminator claims nothing, and says so.
        Assert.Equal(0, CCall.Count("free(notif);", ";"));
        Assert.False(CCall.InOrder("free(notif);"));
    }

    /// <summary>
    /// PP400: a comment quoting the old code is not the old code.
    ///
    /// Three absence checks went red on this in one session, each because the comment explaining a
    /// fix quoted what it had replaced. The code was right every time and the reader was reading
    /// prose.
    /// </summary>
    [Fact]
    public void ACommentQuotingTheOldCodeIsNotTheOldCode()
    {
        const string Fixed = """
            	// PP399: the clamp was inverted. The test said `len > max_len * 2`, which permits four
            	// times what fits.
            	if (len > (max_len - 1) / 2) {
            		len = (max_len - 1) / 2;
            	}
            """;

        // The comment is why a check on the raw text finds what is not there.
        Assert.True(CCall.Mark(Fixed, "max_len * 2") >= 0);

        // And is not there once the prose is gone.
        Assert.Equal(-1, CCall.Mark(CCall.Code(Fixed), "max_len * 2"));

        // The code itself survives.
        Assert.True(CCall.Mark(CCall.Code(Fixed), "len = (max_len - 1) / 2;") >= 0);
    }

    /// <summary>Block comments too, and a string that looks like one is left alone.</summary>
    [Fact]
    public void BlockCommentsGoAndStringsStay()
    {
        Assert.Equal(-1, CCall.Mark(CCall.Code("a; /* srand(x) */ b;"), "srand(x)"));

        // A message somebody logs is code, whatever it looks like.
        const string Logged = """CHIAKI_LOGE(log, "// srand(x) was here");""";

        Assert.True(CCall.Mark(CCall.Code(Logged), "srand(x)") >= 0);
    }

    /// <summary>And it reads what it is given (PP272).</summary>
    [Fact]
    public void CodeReadsWhatItIsGiven()
    {
        Assert.Equal("", CCall.Code(""));
        Assert.Equal("f(x);", CCall.Code("f(x);"));
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
