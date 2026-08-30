using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP533: the direction PP33 takes, and the fact it rests on - read out of session.c.
/// </summary>
public class HolepunchDirectionTests
{
    private static string Source()
    {
        string? path = HolepunchDirection.Locate();
        Assert.NotNull(path);
        return File.ReadAllText(path);
    }

    /// <summary>
    /// THE ONE THAT DECIDES IT. Every mention of the handle is the assignment, a null guard, or one
    /// of PP340's nine - so the handle carries nothing between the call sites and can be replaced by
    /// what they produce.
    ///
    /// Counted against the whole file rather than checked site by site: a use nobody knew about is
    /// exactly what would sink the direction, and it would show up here as a total that disagrees.
    /// </summary>
    [Fact]
    public void TheHandleIsOnlyEverTakenGuardedOrPassed()
    {
        string source = Source();

        Assert.True(HolepunchDirection.TheHandleIsOnlyTakenGuardedOrPassed(source));

        Assert.Equal(
            HolepunchDirection.Assignments + HolepunchDirection.Guards + HolepunchSeam.Count,
            HolepunchDirection.MentionsIn(source));
    }

    /// <summary>A kind for each of the nine, and nine kinds - so a tenth cannot arrive unclassified.</summary>
    [Fact]
    public void EveryAskHasAKind()
    {
        Assert.Equal(HolepunchSeam.Asks.Count, HolepunchDirection.Kinds.Count);
        Assert.Equal(HolepunchSeam.Count, HolepunchDirection.Kinds.Count);
    }

    /// <summary>Five results, two verbs, two releases - and the five are what the replacement is.</summary>
    [Fact]
    public void TheNineAreFiveResultsTwoVerbsAndTwoReleases()
    {
        Assert.Equal(5, HolepunchDirection.Kinds.Count(k => k == HolepunchAskKind.Result));
        Assert.Equal(2, HolepunchDirection.Kinds.Count(k => k == HolepunchAskKind.Verb));
        Assert.Equal(2, HolepunchDirection.Kinds.Count(k => k == HolepunchAskKind.Release));

        Assert.Equal(
            HolepunchDirection.Kinds.Count(k => k == HolepunchAskKind.Result),
            HolepunchDirection.Results.Count);
    }

    /// <summary>
    /// FIVE, NOT FOUR. §PP533 describes the replacement as "the sockets, the address and the port"
    /// and leaves out the registration info the session request carries - the one item that is
    /// neither a socket nor an endpoint.
    /// </summary>
    [Fact]
    public void TheRegistrationInfoIsOneOfTheResults()
        => Assert.Contains(
            HolepunchDirection.Results,
            result => result.What.Contains("registration info", StringComparison.Ordinal));

    /// <summary>The releases are the two finis, which give the session back and produce nothing.</summary>
    [Fact]
    public void TheReleasesAreTheTwoFinis()
    {
        IEnumerable<string> released = HolepunchSeam.Asks
            .Where((_, at) => HolepunchDirection.Kinds[at] == HolepunchAskKind.Release)
            .Select(ask => ask.Callee);

        Assert.All(released, callee => Assert.Equal("chiaki_holepunch_session_fini", callee));
    }

    /// <summary>Each result still lands where the replacement would have to put it.</summary>
    [Fact]
    public void EveryResultIsStillKept() => Assert.True(HolepunchDirection.EveryResultIsStillKept(Source()));

    /// <summary>
    /// And the two verbs still keep only an error code. If either started returning something
    /// session.c held, the replacement would need a sixth value.
    /// </summary>
    [Fact]
    public void TheVerbsStillKeepOnlyAnError() => Assert.True(HolepunchDirection.TheVerbsKeepOnlyAnError(Source()));

    /// <summary>The handle is handed in, not built - which is why the caller can hand results instead.</summary>
    [Fact]
    public void TheHandleComesFromTheConnectInfo()
        => Assert.True(HolepunchDirection.TakenFromTheConnectInfo(Source()));

    /// <summary>The five guards are what become "were there results".</summary>
    [Fact]
    public void TheGuardsAreTheFive()
        => Assert.Equal(HolepunchDirection.Guards, HolepunchDirection.GuardsIn(Source()));

    /// <summary>
    /// A file that read a field of the handle fails the count, which is the check working. Written
    /// against a doctored copy because the real one cannot be made to do it.
    /// </summary>
    [Fact]
    public void AnExtraUseOfTheHandleIsCaught()
    {
        string doctored = Source() + "\nvoid extra(ChiakiSession *session) { (void)session->holepunch_session; }\n";

        Assert.False(HolepunchDirection.TheHandleIsOnlyTakenGuardedOrPassed(doctored));
    }
}
