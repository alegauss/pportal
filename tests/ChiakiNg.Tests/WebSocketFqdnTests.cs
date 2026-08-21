using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP254: the second tokener branch, and what it leaves null.
///
/// <see cref="SuccessIsReportedWithNoAddressWritten"/> carries the task, and
/// <see cref="TheSameBranchIsInBothFunctions"/> ties it to PP233 - one shape, two sites, and only
/// here does it leave a caller holding nothing.
/// </summary>
public class WebSocketFqdnTests
{
    /// <summary>
    /// THE FINDING. The one outcome that is neither a failure nor an address.
    /// </summary>
    [Fact]
    public void SuccessIsReportedWithNoAddressWritten()
    {
        Assert.False(WebSocketFqdn.IsFailure(FqdnLookupOutcome.NoTokener));
        Assert.False(WebSocketFqdn.WritesAnAddress(FqdnLookupOutcome.NoTokener));

        Assert.True(WebSocketFqdn.SucceedsWithoutAnAddress(FqdnLookupOutcome.NoTokener));

        // And what the caller ends up holding is what the session was created with.
        Assert.Null(WebSocketFqdn.AddressAfter(FqdnLookupOutcome.NoTokener, "ps.example.net"));
    }

    /// <summary>It is the only outcome with that gap in it.</summary>
    [Fact]
    public void ItIsTheOnlyOutcomeWithThatGap()
    {
        FqdnLookupOutcome[] gaps =
        [
            .. Enum.GetValues<FqdnLookupOutcome>().Where(WebSocketFqdn.SucceedsWithoutAnAddress)
        ];

        Assert.Equal([FqdnLookupOutcome.NoTokener], gaps);
    }

    /// <summary>Every real failure is reported as one, and writes nothing either.</summary>
    [Theory]
    [InlineData(FqdnLookupOutcome.HttpNotOk)]
    [InlineData(FqdnLookupOutcome.Network)]
    [InlineData(FqdnLookupOutcome.Unreadable)]
    [InlineData(FqdnLookupOutcome.FieldAbsent)]
    [InlineData(FqdnLookupOutcome.FieldNotAString)]
    public void ARealFailureIsReportedAsOne(FqdnLookupOutcome outcome)
    {
        Assert.True(WebSocketFqdn.IsFailure(outcome));
        Assert.False(WebSocketFqdn.WritesAnAddress(outcome));
        Assert.Null(WebSocketFqdn.AddressAfter(outcome, "ps.example.net"));
    }

    /// <summary>And a lookup that worked hands the address back.</summary>
    [Fact]
    public void ALookupThatWorkedHandsItBack()
        => Assert.Equal(
            "ps.example.net", WebSocketFqdn.AddressAfter(FqdnLookupOutcome.Ok, "ps.example.net"));

    /// <summary>
    /// One shape, two functions - and PP233's site discards its answer, so only this one leaves a
    /// caller acting on a success it cannot use.
    /// </summary>
    [Fact]
    public void TheSameBranchIsInBothFunctions()
    {
        Assert.False(SessionCheck.IsFailure(SessionCheckOutcome.NoTokener));
        Assert.False(WebSocketFqdn.IsFailure(FqdnLookupOutcome.NoTokener));

        // PP241 measured that the session check's answer is discarded at the call site. This one's
        // is read, and read as success.
        Assert.Equal(
            NegotiationOutcome.Ok, PunchNegotiation.CheckOutcome(SessionCheckOutcome.NoTokener));
    }

    /// <summary>The document is asked two questions, not one.</summary>
    [Fact]
    public void TheFieldIsCheckedTwiceForTwoThings()
    {
        Assert.Equal(FqdnLookupOutcome.FieldAbsent, WebSocketFqdn.Read(hasField: false, isString: true));
        Assert.Equal(
            FqdnLookupOutcome.FieldNotAString, WebSocketFqdn.Read(hasField: true, isString: false));
        Assert.Equal(FqdnLookupOutcome.Ok, WebSocketFqdn.Read(hasField: true, isString: true));
    }

    /// <summary>And the address it produces is what the socket's URL is built from.</summary>
    [Fact]
    public void TheAddressFeedsTheSocketsUrl()
    {
        string? found = WebSocketFqdn.AddressAfter(FqdnLookupOutcome.Ok, "ps.example.net");

        Assert.NotNull(found);
        Assert.Equal("wss", PushSocket.UrlFor(found).Scheme);
        Assert.Equal("ps.example.net", PushSocket.UrlFor(found).Host);
    }

    /// <summary>Every rule above, still written the same way in the core it was read from.</summary>
    [Fact]
    public void TheLookupIsStillTheCores()
    {
        string? file = WebSocketFqdnSource.Locate();
        if (file is null)
            return;

        string core = File.ReadAllText(file);

        Assert.True(
            WebSocketFqdnSource.TheTokenerBranchStillSetsNoCode(core),
            "the tokener branch still leaves without setting a code");
        Assert.True(
            WebSocketFqdnSource.TheParseFailureBelowStillSetsOne(core),
            "while the parse failure below it still sets one");

        Assert.True(
            WebSocketFqdnSource.TheAddressIsStillWrittenOnlyAtTheEnd(core),
            "the address is still written in one place, at the end");
        Assert.True(
            WebSocketFqdnSource.TheCallerStillReadsOnlyTheCode(core),
            "and the caller still reads only the code, against a field it set to null");

        Assert.True(
            WebSocketFqdnSource.TheFieldIsStillCheckedTwice(core), "the field is still checked twice");
        Assert.True(
            WebSocketFqdnSource.TheCopyStillPrecedesTheRelease(core),
            "the copy still precedes the release");
        Assert.True(
            WebSocketFqdnSource.TheUnnamedMessagesAreStillTheAllocations(core),
            "the two messages that do not name the function are still the two allocation failures");
        Assert.True(
            WebSocketFqdnSource.TheOtherAllocationFailureStillReportsIt(core),
            "and the other one still returns a code, which is what makes the tokener's silence an omission");
    }
}
