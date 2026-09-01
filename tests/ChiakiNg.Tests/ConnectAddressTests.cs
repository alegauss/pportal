using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP614, under PP27: where a capture's session is pointed, which is not always where the console
/// is.
///
/// PP613's relay sees whole datagrams because it forwards them, and it needs a session aimed at it.
/// Everything else about a capture stays the console's: the registration selects it, its keys are
/// the session's, and discovery still finds it - because a relay has to be told where to forward.
/// Only the connect address moves, and that is small enough to be one function.
///
/// It matters that this is the CAPTURE path and not the host's UI. PP600 is the line about nothing
/// a user can click reaching a session, and PP613 was filed as waiting on it. It was not: what it
/// needed was a caller that takes an address, and one already starts sessions.
/// </summary>
public class ConnectAddressTests
{
    /// <summary>With nothing to go through, the session goes to the console.</summary>
    [Fact]
    public void WithoutAViaTheConsoleIsTheTarget()
    {
        Assert.Equal("192.168.1.40", ExchangeCapture.ConnectAddress("192.168.1.40", null));
        Assert.Equal("192.168.1.40", ExchangeCapture.ConnectAddress("192.168.1.40", ""));
        Assert.Equal("192.168.1.40", ExchangeCapture.ConnectAddress("192.168.1.40", "   "));
    }

    /// <summary>
    /// And a blank is the same as absent, deliberately.
    ///
    /// A flag given with no value is a caller who meant the console. Connecting to "" would fail
    /// inside the session request, which is a long way from the mistake and reads as the console
    /// refusing.
    /// </summary>
    [Fact]
    public void AViaGivenIsWhereItGoes()
    {
        Assert.Equal("127.0.0.1", ExchangeCapture.ConnectAddress("192.168.1.40", "127.0.0.1"));

        // Trimmed, because a value off a command line carries whatever the shell left on it.
        Assert.Equal("127.0.0.1", ExchangeCapture.ConnectAddress("192.168.1.40", " 127.0.0.1 "));
    }

    /// <summary>
    /// The console's address is still required, which is what keeps the relay pointable.
    ///
    /// A run that skipped discovery because it was going somewhere else would have no far side to
    /// give the relay, and PP613's whole shape is a forward to an address somebody knows.
    /// </summary>
    [Fact]
    public void TheConsolesAddressIsStillRequired()
    {
        Assert.Throws<ArgumentException>(() => ExchangeCapture.ConnectAddress("", "127.0.0.1"));
        Assert.Throws<ArgumentException>(() => ExchangeCapture.ConnectAddress("  ", null));
        Assert.Throws<ArgumentNullException>(() => ExchangeCapture.ConnectAddress(null!, null));
    }

    /// <summary>
    /// The host reads the flag, so the function above is reachable from a command line.
    ///
    /// Read out of App.xaml.cs rather than by running the host: starting a capture needs a console,
    /// and what this holds is the wiring - a function nothing calls is the shape PP569 and PP570
    /// each found once.
    /// </summary>
    [Fact]
    public void TheHostPassesTheFlagThrough()
    {
        if (SanitizerSource.LocateRelative(@"app\App.xaml.cs") is not { } path)
            return;

        string text = File.ReadAllText(path);

        Assert.Contains("\"--via\"", text, StringComparison.Ordinal);

        // And it reaches the capture rather than being read and dropped.
        int flag = text.IndexOf("\"--via\"", StringComparison.Ordinal);
        int run = text.LastIndexOf("ExchangeCapture.Run(", flag, StringComparison.Ordinal);

        Assert.True(
            run >= 0 && flag - run < 500,
            "--via is read somewhere that is not the capture call, so the flag is parsed and lost");
    }
}
