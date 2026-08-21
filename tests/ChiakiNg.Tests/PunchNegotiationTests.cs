using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP241: the two answers nobody reads, and the ids that look reused and are not.
///
/// <see cref="AFailedSendIsIndistinguishableFromASentOne"/> is the shape: downstream of the send
/// there is no value that differs, so the failure surfaces half a minute later wearing the name of
/// something else entirely.
/// </summary>
public class PunchNegotiationTests
{
    /// <summary>
    /// A send that failed and a send that worked are the same to everything after them - the
    /// difference is a timeout on the next wait, which names the acknowledgement rather than the
    /// send.
    /// </summary>
    [Fact]
    public void AFailedSendIsIndistinguishableFromASentOne()
    {
        Assert.Equal(NegotiationOutcome.Ok, PunchNegotiation.OfferOutcome(sent: true));
        Assert.Equal(NegotiationOutcome.FailedUnheard, PunchNegotiation.OfferOutcome(sent: false));

        // And what a reader is shown instead.
        Assert.Contains(
            "ACK of our connection offer",
            PunchNegotiation.WhatAnUnsentOfferLooksLike,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "send", PunchNegotiation.WhatAnUnsentOfferLooksLike, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The check is unheard twice over: its answer is discarded here, and PP233 measured that the
    /// answer is unreliable - a tokener it cannot allocate reports success.
    /// </summary>
    [Fact]
    public void TheCheckIsUnheardTwiceOver()
    {
        // A real failure, discarded at the call site.
        Assert.Equal(
            NegotiationOutcome.FailedUnheard,
            PunchNegotiation.CheckOutcome(SessionCheckOutcome.HttpNotOk));

        // And a failure the check itself calls success, so it never even reaches here as one.
        Assert.Equal(
            NegotiationOutcome.Ok,
            PunchNegotiation.CheckOutcome(SessionCheckOutcome.NoTokener));

        Assert.False(SessionCheck.IsFailure(SessionCheckOutcome.NoTokener));
    }

    /// <summary>
    /// The ids look reused and are not: taken, incremented, the accept sent with what that made,
    /// and only then incremented again. One, two, three.
    /// </summary>
    [Fact]
    public void TheAcceptDoesNotReuseTheOffersId()
    {
        (int offer, int accept, int next) = PunchNegotiation.RequestIds(PunchNegotiation.FirstRequestId);

        Assert.Equal(1, offer);
        Assert.Equal(2, accept);
        Assert.Equal(3, next);

        Assert.NotEqual(offer, accept);
    }

    /// <summary>And a second round carries on from where the first left it.</summary>
    [Fact]
    public void TheSecondRoundCarriesOn()
    {
        (_, _, int next) = PunchNegotiation.RequestIds(PunchNegotiation.FirstRequestId);
        (int offer, int accept, _) = PunchNegotiation.RequestIds(next);

        Assert.Equal(3, offer);
        Assert.Equal(4, accept);
    }

    /// <summary>Every rule above, still written the same way in the core it was read from.</summary>
    [Fact]
    public void TheNegotiationIsStillTheCores()
    {
        string? file = PunchNegotiationSource.Locate();
        if (file is null)
            return;

        string core = File.ReadAllText(file);

        Assert.True(
            PunchNegotiationSource.TheOfferIsStillSentUnchecked(core),
            "the offer is still sent with its answer discarded");
        Assert.True(
            PunchNegotiationSource.TheCheckIsStillCalledUnchecked(core),
            "and the check too");
        Assert.True(
            PunchNegotiationSource.AnUnsentOfferStillLooksLikeATimeout(core),
            "with the timeout message after both");
        Assert.True(
            PunchNegotiationSource.TheIdsAreStillTakenThenIncremented(core),
            "and the ids still taken then incremented");
    }
}
