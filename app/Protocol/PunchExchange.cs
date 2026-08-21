namespace ChiakiNg.Protocol;

/// <summary>What the answering loop does with one thing that arrived, or failed to.</summary>
public enum PunchStep
{
    /// <summary>A request: answer it and go back to waiting.</summary>
    Answer,

    /// <summary>An extra response where a request was expected. Ordinary; wait past it.</summary>
    Ignore,

    /// <summary>Nothing arrived in time, and something had been answered already: done.</summary>
    Done,

    /// <summary>Nothing arrived in time and nothing ever had.</summary>
    TimedOut,

    /// <summary>The receive itself failed. Logged, and waited on again with the WHOLE timeout.</summary>
    WaitAgain,

    /// <summary>A datagram of the wrong size, or of a type this does not know.</summary>
    Fatal,
}

/// <summary>
/// PP238: the loop that answers a console's punch requests, which succeeds by FALLING QUIET.
///
/// There is no path where receiving something returns success. It answers every request that
/// arrives and goes back to waiting, and the only way out with success is a timeout AFTER at least
/// one was answered. A timeout with nothing answered is the timeout error, which is right - but the
/// caller is told "done" by an absence of traffic rather than by a result.
///
/// A RECEIVE THAT FAILS COSTS NOTHING. It logs and continues, and continuing re-enters a select
/// with the full timeout again - so the number bounds silence rather than the call, which is
/// exactly the shape PP212 measured in the notification wait. Two different loops in this file,
/// the same mistake about what a timeout is for.
///
/// And a bad datagram gets three treatments: the wrong SIZE is fatal, a response where a request
/// was expected is ignored and waited past because an extra one is ordinary, and any other type is
/// fatal and hexdumped.
/// </summary>
public static class PunchExchange
{
    /// <summary>The size a request has to be, which is the reply's size too.</summary>
    public const int RequestLength = PunchResponse.Length;

    /// <summary>
    /// What to do next.
    /// </summary>
    /// <param name="timedOut">Whether the wait ended without anything arriving.</param>
    /// <param name="answeredAny">Whether a request has been answered at some point.</param>
    /// <param name="received">Bytes the receive returned, negative where it failed.</param>
    /// <param name="messageType">The type word, meaningful only when a whole datagram arrived.</param>
    public static PunchStep Next(bool timedOut, bool answeredAny, int received, uint messageType)
    {
        // The only success, and it is an absence.
        if (timedOut)
            return answeredAny ? PunchStep.Done : PunchStep.TimedOut;

        // Costs nothing: the next wait gets the whole timeout over again.
        if (received < 0)
            return PunchStep.WaitAgain;

        if (received != RequestLength)
            return PunchStep.Fatal;

        if (messageType == PunchResponse.ResponseType)
            return PunchStep.Ignore;

        return messageType == PunchResponse.RequestType ? PunchStep.Answer : PunchStep.Fatal;
    }

    /// <summary>Whether a step leaves the loop.</summary>
    public static bool Leaves(PunchStep step)
        => step is PunchStep.Done or PunchStep.TimedOut or PunchStep.Fatal;

    /// <summary>Whether a step is one the caller is told succeeded.</summary>
    public static bool IsSuccess(PunchStep step) => step == PunchStep.Done;
}

/// <summary>
/// PP238: the loop where the core writes it.
/// </summary>
public static class PunchExchangeSource
{
    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => PushNotificationSource.Locate();

    /// <summary>Whether the only success is still a timeout with something already answered.</summary>
    public static bool SuccessIsStillATimeout(string core)
    {
        string body = Body(core);

        return body.Contains("if(err == CHIAKI_ERR_TIMEOUT && received)", StringComparison.Ordinal)
            && body.Contains("return CHIAKI_ERR_SUCCESS;", StringComparison.Ordinal)

            // And there is no other success return in it, which is what makes the first one the only one.
            && body.IndexOf("return CHIAKI_ERR_SUCCESS;", StringComparison.Ordinal)
                == body.LastIndexOf("return CHIAKI_ERR_SUCCESS;", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether a failed receive still continues rather than counting - which is what hands the next
    /// wait the whole timeout again.
    /// </summary>
    public static bool AFailedReceiveStillCostsNothing(string core)
    {
        string body = Body(core);

        int failed = body.IndexOf(
            "Receiving response from %s:%d failed", StringComparison.Ordinal);
        if (failed < 0)
            return false;

        int carriesOn = body.IndexOf("continue;", failed, StringComparison.Ordinal);
        int sized = body.IndexOf("if (len != sizeof(req))", StringComparison.Ordinal);

        return carriesOn > failed && sized > carriesOn;
    }

    /// <summary>Whether a bad datagram still gets those three different treatments.</summary>
    public static bool ThreeTreatmentsForABadDatagram(string core)
    {
        string body = Body(core);

        return body.Contains("Received request of unexpected size", StringComparison.Ordinal)
            && body.Contains("Received an extra response, ignoring", StringComparison.Ordinal)
            && body.Contains("Received response of unexpected type", StringComparison.Ordinal)
            && body.Contains("chiaki_log_hexdump(", StringComparison.Ordinal);
    }

    /// <summary>receive_request_send_response_ps's body, cut at the two lines that bound it.</summary>
    private static string Body(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        int start = core.LastIndexOf(
            "static ChiakiErrorCode receive_request_send_response_ps(Session *session",
            StringComparison.Ordinal);
        if (start < 0)
            return "";

        int end = core.IndexOf("static void log_session_state(", start, StringComparison.Ordinal);
        return end < 0 ? core[start..] : core[start..end];
    }
}
