using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP239: the two requests, and the name that means both of them.
///
/// <see cref="TheWakeupIsBuiltFromTheAnswerNotTheQuestion"/> is the one that matters. In the core
/// both URLs are called user_profile_url, and only one of them makes a wakeup that reaches a
/// console - so the port gives them different names and this asserts the difference is real.
/// </summary>
public class Ps4WakeupTests
{
    /// <summary>What the discovery service answered with, in one run's shape.</summary>
    private const string Discovered = "https://asm.np.community.playstation.net/asm/v1";

    /// <summary>The bootstrap is fixed and is the only compiled-in address in the exchange.</summary>
    [Fact]
    public void TheBootstrapIsTheOnlyAddressCompiledIn()
    {
        Assert.StartsWith("https://", Ps4Wakeup.DiscoveryUrl, StringComparison.Ordinal);
        Assert.Contains("baseUrls/userProfile", Ps4Wakeup.DiscoveryUrl, StringComparison.Ordinal);
    }

    /// <summary>
    /// THE SHADOW. The wakeup is built from the ANSWER, and building it from the question gives a
    /// perfectly well-formed URL aimed at the discovery service - which is what a port resolving
    /// the name the other way produces, and nothing in the result would say so.
    /// </summary>
    [Fact]
    public void TheWakeupIsBuiltFromTheAnswerNotTheQuestion()
    {
        string right = Ps4Wakeup.UrlFor(Discovered, "someone");
        string wrong = Ps4Wakeup.UrlFor(Ps4Wakeup.DiscoveryUrl, "someone");

        Assert.NotEqual(right, wrong);

        // Both are well formed and both end the same way, which is why the mistake is invisible.
        Assert.EndsWith("/v1/users/someone/remoteConsole/wakeUp?platform=PS4", right, StringComparison.Ordinal);
        Assert.EndsWith("/v1/users/someone/remoteConsole/wakeUp?platform=PS4", wrong, StringComparison.Ordinal);

        Assert.Contains("baseUrls/userProfile", wrong, StringComparison.Ordinal);
        Assert.DoesNotContain("baseUrls/userProfile", right, StringComparison.Ordinal);
    }

    /// <summary>The wakeup names the account and the platform.</summary>
    [Fact]
    public void TheWakeupNamesTheAccountAndThePlatform()
    {
        string url = Ps4Wakeup.UrlFor(Discovered, "player-one");

        Assert.Contains("player-one", url, StringComparison.Ordinal);
        Assert.Contains("platform=PS4", url, StringComparison.Ordinal);
    }

    /// <summary>
    /// The buffer the answer is copied into, carried as a bound the core does not check. The
    /// string comes from the network, which is what separates this from every other unbounded copy
    /// in that file.
    /// </summary>
    [Fact]
    public void TheAnswerIsMeasuredAgainstABufferTheCoreDoesNotCheck()
    {
        Assert.Equal(128, Ps4Wakeup.HostBuffer);
        Assert.True(Ps4Wakeup.Fits(Discovered));

        Assert.False(Ps4Wakeup.Fits(new string('a', 128)));
        Assert.True(Ps4Wakeup.Fits(new string('a', 127)));
    }

    /// <summary>The host is what is left after the scheme is removed and the path cut off.</summary>
    [Fact]
    public void TheHostIsWhatSurvivesTheStripping()
    {
        Assert.Equal("asm.np.community.playstation.net", Ps4Wakeup.HostOf(Discovered));
        Assert.Equal("example.net", Ps4Wakeup.HostOf("http://example.net/a/b"));
        Assert.Equal("example.net", Ps4Wakeup.HostOf("example.net"));
    }

    /// <summary>
    /// And the stripping is a text removal rather than a parse, so a url carrying the scheme text
    /// somewhere else loses that occurrence too. The core keeps the stripped string as well as the
    /// host it cuts from it, which is why this is worth asserting where it happens.
    /// </summary>
    [Fact]
    public void TheSchemeIsRemovedFromWhereverItAppears()
    {
        // Two occurrences: the first removal takes the front one and the second takes the other,
        // so a query naming a scheme is silently edited.
        Assert.Equal(
            "example.net/redirect?to=example.org",
            Ps4Wakeup.StripScheme("https://example.net/redirect?to=http://example.org"));

        // The HOST usually survives it, because the cut happens before the second occurrence -
        // which is exactly why the damage is easy to miss.
        Assert.Equal(
            "example.net",
            Ps4Wakeup.HostOf("https://example.net/redirect?to=http://example.org"));
    }

    /// <summary>Every rule above, still written the same way in the core it was read from.</summary>
    [Fact]
    public void TheWakeupIsStillTheCores()
    {
        string? file = Ps4WakeupSource.Locate();
        if (file is null)
            return;

        string core = File.ReadAllText(file);

        Assert.True(Ps4WakeupSource.TheBootstrapIsStillCompiledIn(core), "the bootstrap");
        Assert.True(Ps4WakeupSource.TheNameIsStillShadowed(core), "and the name is still shadowed");
        Assert.True(
            Ps4WakeupSource.TheAnswerStillGoesIntoAFixedBuffer(core),
            "the answer into 128 bytes, unbounded");
        Assert.True(
            Ps4WakeupSource.TheSchemeIsStillRemovedNotParsed(core),
            "and the scheme removed rather than parsed");
    }
}
