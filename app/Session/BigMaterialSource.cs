namespace ChiakiNg.Session;

/// <summary>One thing the BIG needs, and where a composition root is allowed to get it.</summary>
/// <param name="What">The field, as the C's send_big names it.</param>
/// <param name="Reader">The call that answers it out of the session.</param>
/// <param name="Invented">
/// What the root spelled instead, before PP777 - a literal or a stand-in constructor. Null where
/// there was nothing to spell, which is none of these: every one of them had a plausible constant.
/// </param>
public readonly record struct BigMaterial(string What, string Reader, string Invented);

/// <summary>
/// PP777: the BIG's material comes off the session, asked of the composition root's own text.
///
/// A LIVE CONSOLE ACKED THE MESSAGE AND ANSWERED NOTHING, which is what a BIG it cannot read looks
/// like from this side: there is no refusal to log, because the console has no way to say the
/// launch spec was hidden under a key stream it does not share. Two DataAcks and no bang.
///
/// PP726 formats the spec against the C's own template and PP727 hides it against the C's own
/// obfuscation, both asserted byte for byte. Neither was wrong. What was wrong is what the root
/// handed them: a crypt built from thirty-two zero bytes, an MTU measured in the other direction, a
/// resolution spelled as 1280 by 720, and a target spelled as the only one this tree has met.
///
/// EACH OF THOSE IS A PLAUSIBLE CONSTANT, which is the whole reason this check exists rather than a
/// comment. A zeroed crypt is a valid crypt. 1280 by 720 is what the connect info asks for today.
/// Ps5_1 is the only PS5 target there is. mtu_out is a number senkusha really measured. Every one
/// of them reads as right, produces a well-formed message, and costs a console trial to find.
///
/// SO THE CHECK IS THAT THE READER IS NAMED. Not that the constant is absent - a spec may legitimately
/// mention 720 somewhere - but that the call which asks the session is present in the file that
/// builds the message. A root that goes back to inventing one of these loses its reader first.
/// </summary>
public static class BigMaterialSource
{
    /// <summary>Where the BIG is built.</summary>
    public const string RelativePath = @"app\Session\ManagedStreamPhase.cs";

    /// <summary>Every field whose value belongs to the session, and the reader that asks for it.</summary>
    public static IReadOnlyList<BigMaterial> Required { get; } =
    [
        new(
            "session->rpcrypt",
            "SessionBigMaterial.AuthOf(",
            "new RpCrypt(ChiakiTarget.Ps5_1, new byte[16], new byte[16])"),
        new(
            "connect_info.video_profile",
            "SessionBigMaterial.ProfileOf(",
            "Width: 1280, Height: 720"),
        new(
            "session->mtu_in",
            "transport.MtuIn",
            "transport.MtuOut"),
        new(
            "session->target",
            "auth.Target",
            "Target: ChiakiTarget.Ps5_1"),
    ];

    /// <summary>The root's file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>
    /// The readers a given source is missing, by the field each answers.
    ///
    /// Read as CODE, which PP735's trap makes necessary here more than usual: this file's own
    /// docstrings spell every one of the invented forms, and a check over flat text would find them
    /// in the prose that explains why they are gone.
    /// </summary>
    public static IReadOnlyList<string> MissingIn(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        string code = DeadAssertions.CodeOnly(source);

        return
        [
            .. Required
                .Where(one => !code.Contains(one.Reader, StringComparison.Ordinal))
                .Select(one => one.What),
        ];
    }

    /// <summary>
    /// And the invented forms that are back, which is the other direction of the same question.
    ///
    /// A root could name every reader and still pass one of them somewhere that does not reach the
    /// spec, so the absence is asserted too - over code, for the reason above.
    /// </summary>
    public static IReadOnlyList<string> InventedIn(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        string code = DeadAssertions.CodeOnly(source);

        return
        [
            .. Required
                .Where(one => code.Contains(one.Invented, StringComparison.Ordinal))
                .Select(one => one.What),
        ];
    }
}
