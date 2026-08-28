using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP505, under PP340: what the session request names, and the outcome where it names nothing.
///
/// Four sources, and the interesting one is the fallback: a request that is well formed, reaches
/// the right console over an already-connected socket, and carries a Host line naming nothing.
/// </summary>
public class SessionRequestAddressTests
{
    /// <summary>The local arm names the address that answered, not the first one tried.</summary>
    [Fact]
    public void TheLocalArmNamesTheAddressThatAnswered()
    {
        SessionRequestTarget target = SessionRequestAddress.Local(
            ["192.0.2.1", "192.0.2.2", "192.0.2.3"], i => i == 2);

        Assert.Equal(HostnameSource.Resolved, target.Source);
        Assert.Equal("192.0.2.3", target.Hostname);
        Assert.Equal(3, target.Attempts);
        Assert.Equal(SessionRequestAddress.SessionPort, target.Port);
        Assert.False(target.OverRudp);
    }

    /// <summary>
    /// THE FALLBACK: a failed lookup leaves the literal in place and the request still goes out.
    ///
    /// The connect on that same candidate succeeds, so this is not an error path - it is a request
    /// to the right machine whose Host line says "unknown".
    /// </summary>
    [Fact]
    public void AFailedLookupStillSendsARequestNamingNothing()
    {
        SessionRequestTarget target = SessionRequestAddress.Local([null], _ => true);

        Assert.Equal(HostnameSource.Fallback, target.Source);
        Assert.Equal(SessionRequestAddress.FallbackHostname, target.Hostname);

        Assert.True(SessionRequestAddress.BuildsARequest(target));
        Assert.False(SessionRequestAddress.NamesTheConsole(target));
    }

    /// <summary>
    /// An empty candidate list and a list where nothing connects end identically.
    ///
    /// Which is why PP339's failure was unreadable: a PSN session that fell into this arm had an
    /// empty list, and the report was the same one a genuinely unreachable console gets.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void NoAddressAnsweringEndsWithoutARequest(int candidates)
    {
        string?[] addresses = [.. Enumerable.Range(0, candidates).Select(i => $"192.0.2.{i}")];

        SessionRequestTarget target = SessionRequestAddress.Local(addresses, _ => false);

        Assert.Equal(HostnameSource.None, target.Source);
        Assert.Null(target.Hostname);
        Assert.Equal(SessionRequestAddress.NoAddressReason, target.QuitReason);
        Assert.False(SessionRequestAddress.BuildsARequest(target));
    }

    /// <summary>
    /// The PSN arm names the punched address and the punched port, over the rudp channel.
    ///
    /// The port is the visible difference: the local arm always says 9295, this one says whatever
    /// the punch settled on.
    /// </summary>
    [Fact]
    public void ThePsnArmNamesThePunchedAddressAndPort()
    {
        SessionRequestTarget target = SessionRequestAddress.Psn("203.0.113.9", ctrlPort: 41234);

        Assert.Equal(HostnameSource.Punched, target.Source);
        Assert.Equal("203.0.113.9", target.Hostname);
        Assert.Equal(41234, target.Port);
        Assert.NotEqual(SessionRequestAddress.SessionPort, target.Port);
        Assert.True(target.OverRudp);
        Assert.True(SessionRequestAddress.NamesTheConsole(target));
    }

    /// <summary>
    /// Three of the four sources build a request and only two of those name the console.
    ///
    /// The one-line statement of the whole file: "a request was sent" and "the console was named"
    /// are different questions with different answers.
    /// </summary>
    [Fact]
    public void BuildingARequestAndNamingTheConsoleAreDifferentQuestions()
    {
        SessionRequestTarget[] outcomes =
        [
            SessionRequestAddress.Local(["192.0.2.1"], _ => true),
            SessionRequestAddress.Local([null], _ => true),
            SessionRequestAddress.Psn("203.0.113.9", 41234),
            SessionRequestAddress.Local([], _ => true),
        ];

        Assert.Equal(3, outcomes.Count(SessionRequestAddress.BuildsARequest));
        Assert.Equal(2, outcomes.Count(SessionRequestAddress.NamesTheConsole));
    }

    /// <summary>
    /// THE DRIFT CHECK: the local arm still names before it connects, the fallback still carries
    /// on, and the PSN arm still takes both from the punch.
    /// </summary>
    [Fact]
    public void TheCStillAddressesItThisWay()
    {
        if (SessionRequestAddressSource.Locate() is not { } path)
            return;

        string source = File.ReadAllText(path);

        Assert.True(SessionRequestAddressSource.TheLocalArmNamesBeforeItConnects(source));
        Assert.True(SessionRequestAddressSource.TheFallbackCarriesOn(source));
        Assert.True(SessionRequestAddressSource.ThePsnArmTakesAddressAndPortFromThePunch(source));
        Assert.True(SessionRequestAddressSource.TheHostLineCarriesBoth(source));
    }
}
