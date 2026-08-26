using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP339: no failure in the session thread ends with a cancellation poll.
///
/// QUIT ends the session. CHECK_STOP ends it only where somebody asked to stop, so on an ordinary
/// failure it returns and execution carries on past it. The two read alike at a call site, which
/// is why this is a check and not a review note.
/// </summary>
public class SessionErrorExitsTests
{
    /// <summary>
    /// THE DEFECT, as a shape rather than as a line.
    ///
    /// A block that logs at ERROR level and then reaches its closing brace with only a CHECK_STOP
    /// in it has no exit: the error is reported and the thread continues. The rudp init was written
    /// that way, and the consequence was a session that failed reporting that no address answered -
    /// on a holepunch session, which has no address to answer.
    /// </summary>
    [Fact]
    public void NoLoggedFailureEndsWithACancellationPoll()
    {
        string? path = SessionErrorExits.Locate();
        if (path is null)
            return;

        IReadOnlyList<string> polling =
            SessionErrorExits.ErrorsThatOnlyPollForCancellation(File.ReadAllText(path));

        Assert.True(
            polling.Count == 0,
            "these failures poll for cancellation instead of exiting:\n  " + string.Join("\n  ", polling));
    }

    /// <summary>
    /// And the reader finds one where there is one, so the check above means something.
    ///
    /// Written against a copy of the block as it was, because a check that cannot fail is a check
    /// that says nothing - and this one passes on the tree either way unless it can see the shape.
    /// </summary>
    [Fact]
    public void TheReaderFindsAFailureThatOnlyPolls()
    {
        const string asItWas = """
            	if(!session->rudp)
            	{
            		CHIAKI_LOGE(session->log, "Initializing rudp failed");
            		CHECK_STOP(quit);
            	}
            """;

        string found = Assert.Single(SessionErrorExits.ErrorsThatOnlyPollForCancellation(asItWas));

        Assert.Contains("Initializing rudp failed", found, StringComparison.Ordinal);
    }

    /// <summary>And does not report one that exits properly.</summary>
    [Fact]
    public void TheReaderIgnoresAFailureThatExits()
    {
        const string proper = """
            	if(err != CHIAKI_ERR_SUCCESS)
            	{
            		CHIAKI_LOGE(session->log, "Failed to send switch to stream connection message");
            		QUIT(quit_ctrl);
            	}
            """;

        Assert.Empty(SessionErrorExits.ErrorsThatOnlyPollForCancellation(proper));
    }
}
