using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP33: send-and-wait, where what you asked for is recognised by one byte.
/// </summary>
public class RudpExchangeTests
{
    private static RudpMessage Message(RudpPacketType type, int dataSize = 4, RudpMessage? sub = null)
        => new(
            RudpFrame.SizeFor(dataSize),
            (ushort)(RudpFrame.HeaderSize + dataSize),
            type,
            (byte)((ushort)type >> 8),
            [.. Enumerable.Repeat((byte)1, dataSize)],
            0,
            sub);

    private static RudpMessage? Matched(RudpMessage received, RudpPacketType expected, int min = 0)
    {
        RudpMatch match = RudpExchange.Match(received, expected, min, out RudpMessage? matched);
        return match == RudpMatch.Found ? matched : null;
    }

    /// <summary>The wanted message, arriving on its own.</summary>
    [Theory]
    [InlineData(RudpPacketType.InitResponse)]
    [InlineData(RudpPacketType.CookieResponse)]
    [InlineData(RudpPacketType.Finish)]
    [InlineData(RudpPacketType.CtrlMessage)]
    public void TheWantedMessageIsFound(RudpPacketType expected)
        => Assert.NotNull(Matched(Message(expected), expected));

    /// <summary>
    /// THE MATCH IS A PREFIX. It compares the SUBTYPE - the type's high byte - so waiting for
    /// INIT_RESPONSE (0xD000) accepts any type whose high half is 0xD0, including ones nobody has
    /// declared.
    ///
    /// A port comparing the sixteen-bit type would reject messages the Qt client accepts, with no
    /// way of knowing which ones it was losing.
    /// </summary>
    [Fact]
    public void ATypeNobodyDeclaredIsAcceptedOnItsHighByte()
    {
        RudpMessage undeclared = Message((RudpPacketType)0xD0FF);

        Assert.NotNull(Matched(undeclared, RudpPacketType.InitResponse));
        Assert.NotEqual(RudpPacketType.InitResponse, undeclared.Type);
    }

    /// <summary>And a type whose high byte is not the wanted one is not it.</summary>
    [Fact]
    public void AnotherTypeEntirelyIsNotAccepted()
        => Assert.Null(Matched(Message(RudpPacketType.CookieResponse), RudpPacketType.InitResponse));

    /// <summary>Waiting for a control message accepts PP201's four types under one name.</summary>
    [Theory]
    [InlineData(RudpPacketType.CtrlMessage)]
    [InlineData(RudpPacketType.Offset8)]
    [InlineData(RudpPacketType.Offset10)]
    [InlineData(RudpPacketType.Unknown)]
    public void FourTypesSatisfyOneControlMessageExpectation(RudpPacketType arriving)
        => Assert.NotNull(Matched(Message(arriving), RudpPacketType.CtrlMessage));

    /// <summary>
    /// A WRONG MESSAGE IS UNWRAPPED, NOT DISCARDED. The sub-message is promoted over the outer one
    /// and the same check runs again, so the wanted message is found behind two unwanted ones.
    /// </summary>
    [Fact]
    public void TheWantedMessageIsFoundBehindTwoUnwantedOnes()
    {
        RudpMessage nested = Message(
            RudpPacketType.Ack,
            sub: Message(
                RudpPacketType.SessionMessage,
                sub: Message(RudpPacketType.InitResponse)));

        RudpMessage? matched = Matched(nested, RudpPacketType.InitResponse);

        Assert.NotNull(matched);
        Assert.Equal(RudpPacketType.InitResponse, matched.Type);
    }

    /// <summary>A chain with nothing wanted in it retries the whole send.</summary>
    [Fact]
    public void AChainWithNothingWantedRetries()
    {
        RudpMessage nested = Message(RudpPacketType.Ack, sub: Message(RudpPacketType.SessionMessage));

        Assert.Equal(
            RudpMatch.Retry,
            RudpExchange.Match(nested, RudpPacketType.InitResponse, 0, out RudpMessage? matched));

        Assert.Null(matched);
    }

    /// <summary>
    /// The unwrapping stops at the FIRST match, so an outer message that satisfies the expectation
    /// is the one returned even when a better-looking one is nested behind it.
    /// </summary>
    [Fact]
    public void TheOutermostMatchIsTheOneTaken()
    {
        RudpMessage nested = Message(
            RudpPacketType.CtrlMessage, dataSize: 4, sub: Message(RudpPacketType.Offset10, dataSize: 8));

        RudpMessage? matched = Matched(nested, RudpPacketType.CtrlMessage);

        Assert.NotNull(matched);
        Assert.Equal(RudpPacketType.CtrlMessage, matched.Type);
        Assert.Equal(4, matched.Data.Length);
    }

    /// <summary>
    /// A MATCHED MESSAGE THAT ARRIVED TOO SHORT RE-ENTERS THE SEND. The size check runs AFTER the
    /// type check, and its failure is a retry of the whole request - not a look at what else the
    /// datagram was carrying.
    ///
    /// So a short message hides a good one behind it: the chain is abandoned at the first match.
    /// </summary>
    [Fact]
    public void AShortMatchRetriesRatherThanLookingDeeper()
    {
        RudpMessage nested = Message(
            RudpPacketType.InitResponse,
            dataSize: 2,
            sub: Message(RudpPacketType.InitResponse, dataSize: 16));

        Assert.Equal(
            RudpMatch.Retry,
            RudpExchange.Match(nested, RudpPacketType.InitResponse, 8, out RudpMessage? matched));

        Assert.Null(matched);
    }

    /// <summary>And one that is long enough is taken.</summary>
    [Fact]
    public void AMatchOfTheRightSizeIsTaken()
        => Assert.NotNull(Matched(Message(RudpPacketType.InitResponse, dataSize: 16), RudpPacketType.InitResponse, 8));

    /// <summary>
    /// THE FOUR YOU MAY SEND ARE NOT THE FOUR YOU MAY EXPECT. The two switches admit disjoint sets,
    /// and no type appears in both.
    /// </summary>
    [Fact]
    public void TheSendableAndExpectableSetsAreDisjoint()
    {
        Assert.Equal(4, RudpExchange.Sendable.Count);
        Assert.Equal(4, RudpExchange.Expectable.Count);
        Assert.Empty(RudpExchange.Sendable.Intersect(RudpExchange.Expectable.Keys));
    }

    /// <summary>Asking to wait for something unsupported is refused, not retried.</summary>
    [Theory]
    [InlineData(RudpPacketType.Ack)]
    [InlineData(RudpPacketType.SessionMessage)]
    [InlineData(RudpPacketType.StreamConnectionSwitchAck)]
    public void AnUnsupportedExpectationIsRefusedOutright(RudpPacketType expected)
    {
        Assert.Equal(
            RudpMatch.Unsupported,
            RudpExchange.Match(Message(expected), expected, 0, out RudpMessage? matched));

        Assert.Null(matched);
    }

    /// <summary>And the same types cannot be sent by this exchange either way round.</summary>
    [Fact]
    public void OnlyTheFourSendableTypesCanBeSent()
    {
        Assert.True(RudpExchange.CanSend(RudpPacketType.InitRequest));
        Assert.False(RudpExchange.CanSend(RudpPacketType.InitResponse));
        Assert.False(RudpExchange.CanSend(RudpPacketType.Finish));
    }

    /// <summary>Every rule above, still stated the same way in the core.</summary>
    [Fact]
    public void TheExchangesRulesAreStillTheQtCores()
    {
        string? path = RudpExchangeSource.Locate();
        if (path is null)
            return;

        string core = File.ReadAllText(path);

        Assert.True(RudpExchangeSource.TheMatchIsStillOnTheSubtype(core), "one byte, not two");
        Assert.True(RudpExchangeSource.AWrongMessageIsStillUnwrapped(core), "promoted and re-checked");
        Assert.True(
            RudpExchangeSource.PromotionStillThrowsThePayloadAway(core), "the outer payload freed");
        Assert.True(RudpExchangeSource.ATooShortPayloadStillRetries(core), "short re-enters the send");
        Assert.True(RudpExchangeSource.TheTwoSetsAreStillDisjoint(core), "send and expect, disjoint");
        Assert.True(
            RudpExchangeSource.AnUnsupportedTypeIsStillRefused(core), "refused rather than retried");
    }

    /// <summary>
    /// And the subtype check earns its green: a core that had been tightened to compare the whole
    /// type must turn it red, because the width of that comparison is what this file is about.
    /// </summary>
    [Fact]
    public void TheSubtypeCheckFailsOnATightenedCore()
    {
        string? path = RudpExchangeSource.Locate();
        if (path is null)
            return;

        string core = File.ReadAllText(path);

        string tightened = core.Replace(
            "if(message->subtype != 0xD0)", "if(message->type != INIT_RESPONSE)", StringComparison.Ordinal);

        Assert.NotEqual(core, tightened);
        Assert.False(RudpExchangeSource.TheMatchIsStillOnTheSubtype(tightened));
    }
}
