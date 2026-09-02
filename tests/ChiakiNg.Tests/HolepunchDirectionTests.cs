using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP533: the direction PP33 takes, and the fact it rests on - read out of session.c.
/// </summary>
public class HolepunchDirectionTests
{
    /// <summary>
    /// PP631: session.c while it still asks - null outside a checkout, and null once PP33's flip
    /// lands.
    ///
    /// This one had `Assert.NotNull` on the path, which asserted that the runner was in a checkout.
    /// After the flip that becomes an assertion about the wrong thing: the file is there and says
    /// nothing about the handle. The checkout question is <see cref="SessionHolepunchShape.Locate"/>
    /// and is asked once, by PP630's own assertions; these decline instead.
    /// </summary>
    private static string? Source() => SessionHolepunchShape.AskingSource();

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
        if (Source() is not { } source)
            return;

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
        => Assert.Contains(HolepunchDirection.Results, result => result.Name == "hinfo");

    /// <summary>
    /// PP551: the five are PP478's five, not a second list saying the same thing.
    ///
    /// The first version restated them here in its own words. Two lists that agree today drift the
    /// first time one is edited, and this one would have drifted silently - nothing compares them.
    /// </summary>
    [Fact]
    public void TheResultsAreTheStatePP478Carried()
        => Assert.Same(HolepunchState.Carried, HolepunchDirection.Results);

    /// <summary>
    /// PP551: FOUR DURABLE AND ONE SCOPED, which is what "five results" left out.
    ///
    /// The registration info's address is taken and handed to four calls that finish inside its
    /// block. A replacement holding it in a field would compile, and PP479 says in as many words
    /// that it would be the bug.
    /// </summary>
    [Fact]
    public void FourResultsOutliveTheirCallAndOneDoesNot()
    {
        Assert.Equal(4, HolepunchDirection.Durable.Count);
        Assert.Equal("hinfo", Assert.Single(HolepunchDirection.Scoped).Name);
        Assert.Equal(StateLifetime.Block, HolepunchDirection.Scoped[0].Lifetime);

        Assert.Equal(
            HolepunchDirection.Results.Count,
            HolepunchDirection.Durable.Count + HolepunchDirection.Scoped.Count);
    }

    /// <summary>
    /// PP551: THE JOIN NOTHING HAD. PP479's outcome carries all four durable results and not the
    /// scoped one - asserted against the record's own members, so the change PP479 warns about
    /// fails here rather than compiling.
    /// </summary>
    [Fact]
    public void TheOutcomeCarriesTheDurableResultsAndNotTheScopedOne()
    {
        Assert.True(HolepunchDirection.TheOutcomeCarriesTheDurableResultsOnly());

        // The ctrl socket is answered by the rudp built from it, which is the one indirection.
        Assert.True(HolepunchDirection.Answers("Rudp", HolepunchStep.CtrlSocket));
        Assert.False(HolepunchDirection.Answers("DataSocket", HolepunchStep.CtrlSocket));

        // And a field for the scoped one would be recognised, which is what makes the check bite.
        Assert.True(HolepunchDirection.Answers("RegistInfo", HolepunchStep.RegistInfo));
    }

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
    public void EveryResultIsStillKept()
    {
        if (Source() is not { } source)
            return;

        Assert.True(HolepunchDirection.EveryResultIsStillKept(source));
    }

    /// <summary>
    /// And the two verbs still keep only an error code. If either started returning something
    /// session.c held, the replacement would need a sixth value.
    /// </summary>
    [Fact]
    public void TheVerbsStillKeepOnlyAnError()
    {
        if (Source() is not { } source)
            return;

        Assert.True(HolepunchDirection.TheVerbsKeepOnlyAnError(source));
    }

    /// <summary>The handle is handed in, not built - which is why the caller can hand results instead.</summary>
    [Fact]
    public void TheHandleComesFromTheConnectInfo()
    {
        if (Source() is not { } source)
            return;

        Assert.True(HolepunchDirection.TakenFromTheConnectInfo(source));
    }

    /// <summary>The five guards are what become "were there results".</summary>
    [Fact]
    public void TheGuardsAreTheFive()
    {
        if (Source() is not { } source)
            return;

        Assert.Equal(HolepunchDirection.Guards, HolepunchDirection.GuardsIn(source));
    }

    /// <summary>
    /// A file that read a field of the handle fails the count, which is the check working.
    ///
    /// PP631: the doctored copy is built from a LITERAL rather than from session.c, so this one goes
    /// on running after the flip. It is the only assertion here that is about the rule instead of
    /// about the file, and a rule that stopped being exercised the moment the C changed is a rule
    /// nobody would notice breaking.
    /// </summary>
    [Fact]
    public void AnExtraUseOfTheHandleIsCaught()
    {
        string doctored =
            $"void extra(ChiakiSession *session) {{ (void){HolepunchDirection.Handle}; }}\n";

        Assert.False(HolepunchDirection.TheHandleIsOnlyTakenGuardedOrPassed(doctored));
    }
}
