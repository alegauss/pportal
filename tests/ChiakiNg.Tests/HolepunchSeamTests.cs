using ChiakiNg.Protocol;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP429, under PP340: the nine things session.c asks a holepunch session for.
///
/// PP340 found them by reading the callers and left the reading as prose. PP33's blocked-ness rests
/// on it, and a tenth call site would change that job without either line moving.
/// </summary>
public class HolepunchSeamTests(ITestOutputHelper output)
{
    /// <summary>
    /// THE NINE, AND IN THE ORDER THEY APPEAR IN THE FILE.
    ///
    /// FILE ORDER, WHICH IS NOT THE CONNECT SEQUENCE - and that difference caught this check out
    /// first. The two finis are defined in init and teardown, above the session thread, so they come
    /// first in the text and last in the flow. Claiming connect order and comparing positions is
    /// asserting one thing and measuring another.
    /// </summary>
    [Fact]
    public void TheNineAreStillTheOnesSessionMakes()
    {
        if (HolepunchSeam.Locate() is not { } path)
            return;

        string source = File.ReadAllText(path);
        IReadOnlyList<string> calls = HolepunchSeam.CallsIn(source);

        foreach (string call in calls)
            output.WriteLine(call);

        Assert.Equal(HolepunchSeam.Count, calls.Count);

        Assert.True(
            HolepunchSeam.TheCallsAreStillTheseInOrder(source),
            "session.c's holepunch calls are not the nine this names, or not in that order - so "
                + "PP340's premise and PP33's blocked-ness have moved and nothing said so");
    }

    /// <summary>
    /// AND THE TWO SOCKETS STILL ASK FOR DIFFERENT PORTS, which is all that tells them apart.
    ///
    /// A file that asked for the same port twice would still make nine calls in the right order,
    /// and a managed side returning one socket for both would compile.
    /// </summary>
    [Fact]
    public void TheTwoSocketsAskForDifferentPorts()
    {
        if (HolepunchSeam.Locate() is not { } path)
            return;

        Assert.True(
            HolepunchSeam.TheTwoSocketsStillAskForDifferentPorts(File.ReadAllText(path)),
            "the ctrl and data sockets no longer ask for different port types");
    }

    /// <summary>
    /// Every ask carries what it wants back, because the phrase is the interface.
    ///
    /// A callee with no sentence behind it is a name a port cannot be planned from, and this list
    /// exists to be planned from.
    /// </summary>
    [Fact]
    public void EveryAskSaysWhatItWants()
    {
        Assert.Equal(HolepunchSeam.Count, HolepunchSeam.Asks.Count);

        Assert.All(
            HolepunchSeam.Asks,
            ask =>
            {
                Assert.False(string.IsNullOrWhiteSpace(ask.Callee));
                Assert.True(
                    ask.Asks.Length > 20,
                    $"{ask.Callee} has a phrase too short to plan from");
            });

        // The two sockets are the only asks that name a port, and they name two.
        IReadOnlyList<HolepunchAsk> sockets =
            [.. HolepunchSeam.Asks.Where(ask => ask.PortType is not null
                && ask.Callee == "chiaki_get_holepunch_sock")];

        Assert.Equal(2, sockets.Count);
        Assert.NotEqual(sockets[0].PortType, sockets[1].PortType);
    }

    /// <summary>
    /// The reader refuses a tenth call, and a file that lost one.
    ///
    /// Both directions against synthetic text: growing the seam and shrinking it are the two ways
    /// PP340's premise can stop being true, and only one of them looks like progress.
    /// </summary>
    [Fact]
    public void TheReaderRefusesATenthAndAMissingOne()
    {
        string real = HolepunchSeam.Locate() is { } path ? File.ReadAllText(path) : "";
        if (real.Length == 0)
            return;

        Assert.True(HolepunchSeam.TheCallsAreStillTheseInOrder(real));

        // A tenth, appended: the count no longer matches.
        Assert.False(HolepunchSeam.TheCallsAreStillTheseInOrder(
            real + "\nvoid extra(void) { chiaki_get_ps_ctrl_port(session->holepunch_session); }\n"));

        // And nothing at all is not "still these".
        Assert.False(HolepunchSeam.TheCallsAreStillTheseInOrder(""));
        Assert.Empty(HolepunchSeam.CallsIn(""));
    }

    /// <summary>
    /// A comment naming a call does not count as one - PP400's rule.
    /// </summary>
    [Fact]
    public void ACommentDoesNotCount()
    {
        Assert.Empty(HolepunchSeam.CallsIn(
            "// chiaki_get_ps_ctrl_port(session->holepunch_session);"));

        // The offer call, whose name has no chiaki_ prefix - a display filter hid it once.
        Assert.Equal(
            ["holepunch_session_create_offer"],
            HolepunchSeam.CallsIn(
                "\t\tChiakiErrorCode err = holepunch_session_create_offer(session->holepunch_session);"));

        Assert.False(HolepunchSeam.TheTwoSocketsStillAskForDifferentPorts(
            "// chiaki_get_holepunch_sock(session->holepunch_session, CHIAKI_HOLEPUNCH_PORT_TYPE_CTRL);"));
    }
}
