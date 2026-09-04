using System.Globalization;

namespace ChiakiNg.Protocol;

/// <summary>The three bounds one asked-for sample length settles.</summary>
/// <param name="WindowMicroseconds">How long the capture keeps taking datagrams.</param>
/// <param name="Limit">How many it takes, whichever bound is reached first.</param>
/// <param name="Hold">How long the session is held open, so the window has a session to sample.</param>
public readonly record struct SampleBounds(long WindowMicroseconds, int Limit, TimeSpan Hold);

/// <summary>
/// PP526, under PP27: one asked-for length, and the three bounds it settles.
///
/// PP525 found no loss and no reordering in 1610 video packets and named the honest next step - a
/// worse link, or a longer look. Neither could be asked for: PP510's bounds were constants and
/// PP514's command built the writer with the defaults, so every capture was the same file.
///
/// THE THREE NUMBERS HAVE TO MOVE TOGETHER, WHICH IS WHY THIS IS ONE TYPE. The window bounds what
/// reaches the file; the count bounds how big the file gets; the hold bounds how long the session
/// stays open. A window past the hold samples nothing, a hold past the window keeps a console
/// streaming into a capture that already closed, and a count that binds first reports a window it
/// never reached. Exposing any one of them alone is a flag that appears to work.
///
/// SO A CALLER ASKS FOR A LENGTH AND THE REST IS DERIVED - from two numbers a real session
/// measured rather than two somebody picked.
/// </summary>
public static class SampleWindow
{
    /// <summary>The length a capture takes when nobody asks for one.</summary>
    public const int DefaultSeconds = 5;

    /// <summary>
    /// The longest sample this will settle, which is a guard against a typo and not a judgement.
    ///
    /// Two minutes is about five megabytes at the rate below. What is on the other side of this
    /// bound is a console held open for an hour by a mistyped argument, with nobody in the room.
    /// </summary>
    public const int MaximumSeconds = 120;

    /// <summary>
    /// What a console sent, measured: 2000 datagrams over 2486 milliseconds of a real session.
    ///
    /// The count is derived at this rate so it stays a bound rather than becoming a target - on a
    /// link like the measured one it is still what closes the capture, exactly as PP510 had it.
    /// A faster link closes on the count and says so; a slower one closes on the window.
    /// </summary>
    public const int DatagramsPerSecond = 805;

    /// <summary>
    /// What the opening costs before the first datagram arrives - PP521's 122 milliseconds.
    ///
    /// Added to the hold rather than to the window, because the window's origin is the FIRST
    /// datagram and the hold's is the session's start. Without it every sample is short by an
    /// opening.
    /// </summary>
    public static TimeSpan Opening { get; } = TimeSpan.FromMilliseconds(122);

    /// <summary>
    /// The floor under every hold: what PP297's exchange needs, which is not about datagrams.
    ///
    /// A datagram capture wants the window plus the opening; the exchange capture wants long enough
    /// for the control conversation to finish, and twelve seconds is what PP297 settled. The hold
    /// is the larger of the two, so asking for a longer sample never shortens the exchange run.
    /// </summary>
    public static TimeSpan ExchangeHold { get; } = TimeSpan.FromSeconds(12);

    /// <summary>The bounds for a sample nobody asked a length for.</summary>
    public static SampleBounds Default => For(DefaultSeconds);

    /// <summary>
    /// The three bounds for a sample of <paramref name="seconds"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Outside one second to <see cref="MaximumSeconds"/>. A caller taking a length from a command
    /// line should use <see cref="TryParse"/>, which refuses rather than throws.
    /// </exception>
    public static SampleBounds For(int seconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(seconds);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(seconds, MaximumSeconds);

        long window = seconds * 1_000_000L;
        var hold = TimeSpan.FromSeconds(seconds) + Opening;

        return new SampleBounds(
            window,
            seconds * DatagramsPerSecond,
            hold > ExchangeHold ? hold : ExchangeHold);
    }

    /// <summary>
    /// Reads a sample length off a command line, or null where it is not one.
    /// </summary>
    /// <param name="text">
    /// The argument's text, or null where the flag was absent. Null yields
    /// <see cref="Default"/> - an absent flag is the default length and not a refusal.
    /// </param>
    /// <remarks>
    /// NULL FOR A BAD LENGTH RATHER THAN A FALLBACK TO THE DEFAULT. A run asked for sixty seconds
    /// and silently given five is a measurement about the wrong thing, and the file it leaves says
    /// nothing about which length it holds.
    /// </remarks>
    public static SampleBounds? TryParse(string? text)
    {
        if (text is null)
            return Default;

        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int seconds)
            && seconds is > 0 and <= MaximumSeconds
                ? For(seconds)
                : null;
    }

    /// <summary>The flag a length is asked for with.</summary>
    public const string Flag = "--capture-seconds";

    /// <summary>What a command line said about the length.</summary>
    public enum Asked
    {
        /// <summary>No flag, so <see cref="Default"/> and no complaint.</summary>
        Absent,

        /// <summary>A length was asked for and is the one to use.</summary>
        Parsed,

        /// <summary>A length was asked for and cannot be read, which is a refusal.</summary>
        Malformed,
    }

    /// <summary>
    /// The length a command line asks for, read WITHOUT reference to which run flag it accompanies.
    ///
    /// THAT INDEPENDENCE IS THE POINT. The parse used to be gated on the two capture flags that
    /// existed when it was written, and each run flag added since - --measure-decoder, then
    /// --show-stream - read the resulting bounds without joining the condition. Both then took the
    /// default in silence: a session asked to hold for 120 seconds held for 8.8 and wrote a row
    /// saying 8801ms, which is the exact failure the parse's own remarks warn about. Nothing here
    /// knows what flag it is for, so there is no list for a fifth consumer to be missing from.
    /// </summary>
    public static Asked From(IReadOnlyList<string> args, out SampleBounds bounds)
    {
        ArgumentNullException.ThrowIfNull(args);

        bounds = Default;

        if (!Session.HostCommandLine.Has(args, Flag))
            return Asked.Absent;

        if (TryParse(Session.HostCommandLine.ValueAfter(args, Flag)) is not { } asked)
            return Asked.Malformed;

        bounds = asked;
        return Asked.Parsed;
    }
}
