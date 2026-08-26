using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP373: an encoder's failure log has to name the message the encoder is encoding.
///
/// It is the only thing distinguishing these paths. They return the same error to callers that mostly
/// pass it on, so a wrong name here is a session that ended somewhere nobody can find.
/// </summary>
public class EncoderLogIdentityTests
{
    /// <summary>
    /// THE RULE, over every encoder in the file, and derived rather than tabulated.
    ///
    /// Every word of the claimed name has to appear in the function's own name. A table of expected
    /// phrases would only restate the file, and would get updated alongside the copy-paste it was
    /// meant to catch.
    /// </summary>
    [Fact]
    public void EveryEncoderNamesItsOwnMessage()
    {
        string? path = EncoderLogIdentity.Locate();
        if (path is null)
            return;

        IReadOnlyList<EncoderLog> encoders =
            EncoderLogIdentity.EncodersIn(File.ReadAllText(path));

        // The sweep has to find them, or a rule over an empty set passes forever.
        Assert.True(encoders.Count >= 8, $"only {encoders.Count} encoders were found, so the rule covers almost nothing");

        IReadOnlyList<EncoderLog> wrong = EncoderLogIdentity.WearingAnothersName(encoders);

        Assert.True(
            wrong.Count == 0,
            "these encoders log a message that is not theirs:\n  " + string.Join("\n  ", wrong));
    }

    /// <summary>
    /// PP377: THE SAME RULE OVER senkusha.c, whose five encoders all pass it.
    ///
    /// That is the point of running it here. PP373's file had two copied lines in it, so the rule
    /// going green there only proves the fix; a second file where nothing was ever wrong is what
    /// shows the reader is not simply agreeing with whatever it finds.
    /// </summary>
    [Fact]
    public void EverySenkushaEncoderNamesItsOwnMessageToo()
    {
        string? path = EncoderLogIdentity.LocateSenkusha();
        if (path is null)
            return;

        IReadOnlyList<EncoderLog> encoders =
            EncoderLogIdentity.EncodersIn(File.ReadAllText(path));

        Assert.True(encoders.Count >= 5, $"only {encoders.Count} encoders were found in senkusha.c");

        IReadOnlyList<EncoderLog> wrong = EncoderLogIdentity.WearingAnothersName(encoders);

        Assert.True(
            wrong.Count == 0,
            "these encoders log a message that is not theirs:\n  " + string.Join("\n  ", wrong));
    }

    /// <summary>
    /// PP377: and the SHARED helper below them names no caller, which is where the defect was.
    ///
    /// The encoders each own their message, so a wrong name there is a copy-paste. The helper owns
    /// none of them: it is reached by the echo command and by the client MTU command, and every one
    /// of its three failure logs said "echo command". A client MTU command that went unacked
    /// reported the wrong send on the only line a reader gets.
    /// </summary>
    [Fact]
    public void TheSharedAckHelperNamesNoCaller()
    {
        string? path = EncoderLogIdentity.LocateSenkusha();
        if (path is null)
            return;

        Assert.True(
            EncoderLogIdentity.TheSharedHelperNamesNoCaller(
                File.ReadAllText(path), "senkusha_send_data_wait_for_ack"),
            "the shared ack helper spells a caller's message name, or its callers pass the same one");
    }

    /// <summary>And the reader rejects the helper as it was, so the check above means something.</summary>
    [Fact]
    public void TheReaderFindsAHelperThatNamesOneCaller()
    {
        const string asItWas = """
            static ChiakiErrorCode senkusha_send_data_wait_for_ack(ChiakiSenkusha *senkusha, uint8_t *buf, size_t buf_size)
            {
            	ChiakiErrorCode err = chiaki_takion_send_message_data(&senkusha->takion, 1, 8, buf, buf_size, NULL);
            	if(err != CHIAKI_ERR_SUCCESS)
            	{
            		CHIAKI_LOGE(senkusha->log, "Senkusha failed to send echo command");
            		return err;
            	}
            	return err;
            }
            """;

        Assert.False(
            EncoderLogIdentity.TheSharedHelperNamesNoCaller(asItWas, "senkusha_send_data_wait_for_ack"));
    }

    /// <summary>
    /// And it rejects two callers that pass the SAME name, which interpolation alone would let past.
    /// </summary>
    [Fact]
    public void TheReaderFindsTwoCallersPassingOneName()
    {
        const string bothTheSame = """
            static ChiakiErrorCode senkusha_send_data_wait_for_ack(ChiakiSenkusha *senkusha, uint8_t *buf, size_t buf_size, const char *what)
            {
            	CHIAKI_LOGE(senkusha->log, "Senkusha failed to send %s", what);
            	return CHIAKI_ERR_SUCCESS;
            }

            static ChiakiErrorCode senkusha_send_echo_command(ChiakiSenkusha *senkusha, bool enable)
            {
            	return senkusha_send_data_wait_for_ack(senkusha, buf, n, "echo command");
            }

            static ChiakiErrorCode senkusha_send_client_mtu_command(ChiakiSenkusha *senkusha)
            {
            	return senkusha_send_data_wait_for_ack(senkusha, buf, n, "echo command");
            }
            """;

        Assert.False(
            EncoderLogIdentity.TheSharedHelperNamesNoCaller(bothTheSame, "senkusha_send_data_wait_for_ack"));
    }

    /// <summary>
    /// And no two of them log the same sentence, which is the sharper half of the same defect.
    ///
    /// "controller connection protobuf encoding failed" appeared in two functions, four lines apart.
    /// Two identical sentences from two different places cannot narrow anything down at all.
    /// </summary>
    [Fact]
    public void NoTwoEncodersLogTheSameSentence()
    {
        string? path = EncoderLogIdentity.Locate();
        if (path is null)
            return;

        IReadOnlyList<string> shared = EncoderLogIdentity.ClaimsUsedTwice(
            EncoderLogIdentity.EncodersIn(File.ReadAllText(path)));

        Assert.True(
            shared.Count == 0,
            "these claims are made by more than one encoder:\n  " + string.Join("\n  ", shared));
    }

    /// <summary>And the reader finds both of the originals, so the checks mean something.</summary>
    [Fact]
    public void TheReaderFindsBothCopiedLines()
    {
        const string asItWas = """
            static ChiakiErrorCode stream_connection_enable_microphone(ChiakiStreamConnection *stream_connection)
            {
            	bool pbr = pb_encode(&stream, tkproto_TakionMessage_fields, &msg);
            	if(!pbr)
            	{
            		CHIAKI_LOGE(stream_connection->log, "StreamConnection controller connection protobuf encoding failed");
            		return CHIAKI_ERR_UNKNOWN;
            	}
            	return CHIAKI_ERR_SUCCESS;
            }

            CHIAKI_EXPORT ChiakiErrorCode stream_connection_send_corrupt_frame(ChiakiStreamConnection *stream_connection)
            {
            	bool pbr = pb_encode(&stream, tkproto_TakionMessage_fields, &msg);
            	if(!pbr)
            	{
            		CHIAKI_LOGE(stream_connection->log, "StreamConnection heartbeat protobuf encoding failed");
            		return CHIAKI_ERR_UNKNOWN;
            	}
            	return CHIAKI_ERR_SUCCESS;
            }
            """;

        IReadOnlyList<EncoderLog> encoders = EncoderLogIdentity.EncodersIn(asItWas);

        Assert.Equal(2, encoders.Count);
        Assert.Equal(2, EncoderLogIdentity.WearingAnothersName(encoders).Count);
    }

    /// <summary>
    /// And leaves a correct one alone, including the ones whose words are reordered or capitalised.
    ///
    /// "microphone enable" against enable_microphone, and "IDR request" against send_idr_request - the
    /// derivation is over words, not over the phrase, because neither name is a substring of the other.
    /// </summary>
    [Theory]
    [InlineData("stream_connection_enable_microphone", "microphone enable")]
    [InlineData("stream_connection_send_idr_request", "IDR request")]
    [InlineData("stream_connection_send_corrupt_frame", "corrupt frame")]
    [InlineData("stream_connection_send_streaminfo_ack", "streaminfo ack")]
    [InlineData("stream_connection_send_big", "big")]
    public void ACorrectClaimIsLeftAlone(string function, string claimed)
    {
        Assert.True(new EncoderLog(function, claimed).NamesItsOwnMessage);
    }

    /// <summary>And a claim borrowed from a neighbour is not.</summary>
    [Theory]
    [InlineData("stream_connection_enable_microphone", "controller connection")]
    [InlineData("stream_connection_send_corrupt_frame", "heartbeat")]
    [InlineData("stream_connection_send_disconnect", "streaminfo ack")]
    public void ABorrowedClaimIsNot(string function, string claimed)
    {
        Assert.False(new EncoderLog(function, claimed).NamesItsOwnMessage);
    }

    /// <summary>
    /// And a log in the function BELOW the one it belongs to is not credited to it.
    ///
    /// This is what reading each body separately buys. Pairing the definitions and the logs by
    /// position across the file would line them up off by one and agree with the file about it.
    /// </summary>
    [Fact]
    public void ALogIsReadFromItsOwnFunction()
    {
        const string two = """
            static ChiakiErrorCode stream_connection_send_heartbeat(ChiakiStreamConnection *sc)
            {
            	CHIAKI_LOGE(sc->log, "StreamConnection heartbeat protobuf encoding failed");
            }

            static ChiakiErrorCode stream_connection_send_disconnect(ChiakiStreamConnection *sc)
            {
            	CHIAKI_LOGE(sc->log, "StreamConnection disconnect protobuf encoding failed");
            }
            """;

        IReadOnlyList<EncoderLog> encoders = EncoderLogIdentity.EncodersIn(two);

        Assert.Equal(2, encoders.Count);
        Assert.Equal("heartbeat", encoders[0].Claimed);
        Assert.Equal("disconnect", encoders[1].Claimed);
        Assert.Empty(EncoderLogIdentity.WearingAnothersName(encoders));
    }

    /// <summary>And a forward declaration is not a definition, which is PP343's trap.</summary>
    [Fact]
    public void AForwardDeclarationCarriesNoLog()
    {
        const string prototype =
            "static ChiakiErrorCode stream_connection_send_heartbeat(ChiakiStreamConnection *sc);";

        Assert.Empty(EncoderLogIdentity.EncodersIn(prototype));
    }

    /// <summary>
    /// And a function that is BOTH declared and defined is counted once.
    ///
    /// The trap's other face, and the one this reader walked into. CFunction correctly returns the
    /// definition's body for the prototype match too - not a wrong answer, the same answer twice - so
    /// every claim in the file looked like a duplicate of itself and the check went red for a defect
    /// that was not there.
    /// </summary>
    [Fact]
    public void AFunctionDeclaredAndDefinedIsCountedOnce()
    {
        const string both = """
            static ChiakiErrorCode stream_connection_send_heartbeat(ChiakiStreamConnection *sc);

            static ChiakiErrorCode stream_connection_send_heartbeat(ChiakiStreamConnection *sc)
            {
            	CHIAKI_LOGE(sc->log, "StreamConnection heartbeat protobuf encoding failed");
            }
            """;

        EncoderLog only = Assert.Single(EncoderLogIdentity.EncodersIn(both));

        Assert.Equal("heartbeat", only.Claimed);
        Assert.Empty(EncoderLogIdentity.ClaimsUsedTwice(EncoderLogIdentity.EncodersIn(both)));
    }
}
