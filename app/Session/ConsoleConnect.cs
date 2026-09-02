using ChiakiNg.Native;
using ChiakiNg.Settings;

namespace ChiakiNg.Session;

/// <summary>Why a row cannot start a session, or that it can.</summary>
public enum ConnectRefusal
{
    /// <summary>It can.</summary>
    None,

    /// <summary>The console is not paired, so there is no key to open a session with.</summary>
    NotRegistered,

    /// <summary>A PSN row: no address, because it is reached through the relay.</summary>
    NoAddress,

    /// <summary>Paired according to the list, and the store holds no usable key for it.</summary>
    NoRegistration,
}

/// <summary>
/// What a session needs, assembled from one row and the store the Qt client registered into.
///
/// The same four values <see cref="ExchangeCapture"/> puts into a <see cref="ChiakiConnectInfo"/>,
/// named as a request so the assembling can be asserted without a console: the capture builds them
/// inline in the middle of a run that wakes hardware, which is the one place a rule cannot be read.
/// </summary>
public sealed record ConnectRequest
{
    /// <summary>Where the session is pointed.</summary>
    public required string Host { get; init; }

    /// <summary>Which protocol version the target speaks.</summary>
    public required bool Ps5 { get; init; }

    /// <summary>
    /// The console's nickname, or null.
    ///
    /// PP13's rule and not a convenience: <see cref="ConsoleActions.ConnectSendsTheNickname"/> says
    /// the nickname goes only with a DISCOVERED console, because it is what the wake-then-connect
    /// path waits to see come back on the network. A manual console has no name to wait for.
    /// </summary>
    public string? Nickname { get; init; }

    /// <summary>The registration key, as the store holds it.</summary>
    public required byte[] RegistKey { get; init; }

    /// <summary>The RP key - "morning" in libchiaki's spelling.</summary>
    public required byte[] Morning { get; init; }
}

/// <summary>What a row amounts to: a request, or a reason there is none.</summary>
public readonly record struct ConnectPlan(ConnectRefusal Refusal, ConnectRequest? Request);

/// <summary>
/// PP600: the front door's connect, which is the caller nothing had.
///
/// PP561 fitted eight pieces that fit and were never fitted; this is that one level up. The host
/// does start sessions - `--capture-exchange` builds a ChiakiConnectInfo and reaches a real console
/// - but that path exists to record PP297's oracle and is spelled as a developer flag. PP13's
/// console list draws rows and <see cref="ConsoleActions"/> models which actions each row offers,
/// and nothing performs one. So the front door decided and did not act.
///
/// WHAT THIS IS NOT is the choice between the native holepunch seam and the managed one. That needs
/// a console to settle, because the create's HTTP and websocket need PSN. This is the step before
/// it: somewhere for the choice to live. A PSN row is therefore REFUSED here, by name, rather than
/// silently doing the LAN thing with an empty address.
///
/// The store is asked for the registration by NICKNAME, which is the join
/// <see cref="ExchangeCapture"/> already uses and the only one a row can make: a
/// <see cref="ConsoleRow"/> carries no MAC, so a manual row - whose name is its address - cannot be
/// matched to a registration at all. It is refused as <see cref="ConnectRefusal.NoRegistration"/>,
/// which is true and is not the whole truth; PP13's row is what would have to carry more.
/// </summary>
public static class ConsoleConnect
{
    /// <summary>
    /// Whether the row offers the action at all, which is what the button's enabled state is.
    ///
    /// The screen's rule, and deliberately not the store's: a list can say a console is paired
    /// without this process being able to read the key for it, and the two failures want different
    /// words. This one is answerable from the row and is therefore answerable while drawing.
    /// </summary>
    public static bool CanConnect(ConsoleRow row)
        => row.Registered && !string.IsNullOrEmpty(row.Address);

    /// <summary>
    /// The row's registration - by identity where the row has one, and by name where it does not.
    ///
    /// PP624 put the MAC on the row, so this is the join the Qt client makes. The nickname is kept
    /// as the fallback rather than deleted: a PSN row carries no identity at all, and a console
    /// whose reply this port could not read a six-byte host-id out of is still a console somebody
    /// registered under a name.
    ///
    /// The two are exclusive rather than tried in turn. A row that HAS an identity is answered by
    /// it either way, because the case PP624 is about is two consoles sharing a nickname: falling
    /// through to the name for the one that did not match hands it the other's credentials.
    /// </summary>
    public static RegisteredHost? RegistrationFor(
        ConsoleRow row, IReadOnlyList<RegisteredHost> hosts)
    {
        ArgumentNullException.ThrowIfNull(hosts);

        if (row.Mac is { Length: > 0 } mac)
        {
            // The identity is the answer, INCLUDING when it is no. Falling through to the name here
            // is what makes two consoles under one nickname interchangeable again - the second one
            // would take the first's key and open a session with the wrong console's credentials.
            return hosts.FirstOrDefault(one =>
                HostId.Key(one.ServerMac) is { } key
                && string.Equals(key, mac, StringComparison.Ordinal));
        }

        return hosts.FirstOrDefault(one =>
            string.Equals(one.ServerNickname, row.Name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// What starting a session from this row would take, or why it would not.
    ///
    /// Every refusal is named rather than folded into null. A console that is on the network and
    /// not paired, one that is paired and whose key this process cannot read, and one that is only
    /// reachable through PSN are three different things to tell somebody, and a single "cannot
    /// connect" is the sentence a port collects complaints about.
    /// </summary>
    public static ConnectPlan Prepare(ConsoleRow row, IReadOnlyList<RegisteredHost> hosts)
    {
        ArgumentNullException.ThrowIfNull(hosts);

        if (!row.Registered)
            return new(ConnectRefusal.NotRegistered, null);

        if (string.IsNullOrEmpty(row.Address))
            return new(ConnectRefusal.NoAddress, null);

        if (RegistrationFor(row, hosts) is not { } host
            || host.RpRegistKey is not { Length: > 0 } registKey
            || host.RpKey is not { Length: 16 } morning)
        {
            return new(ConnectRefusal.NoRegistration, null);
        }

        return new(
            ConnectRefusal.None,
            new ConnectRequest
            {
                Host = row.Address,
                // The store's own spelling of which console this is, not the row's: the row knows
                // it was discovered and the target enum is what says PS5, exactly as the capture
                // reads it.
                Ps5 = host.Target >= PsnTarget,
                Nickname = ConsoleActions.ConnectSendsTheNickname(
                    new ConsoleActionState(row.Discovered, row.Manual, row.Registered, null))
                    ? row.Name
                    : null,
                RegistKey = registKey,
                Morning = morning,
            });
    }

    /// <summary>
    /// The target value at which a registration is a PS5's.
    ///
    /// Read from the store rather than from the row for the reason the capture reads it there: the
    /// registration is what carries the protocol version, and discovery's host-type string is a
    /// different fact that can disagree with it.
    /// </summary>
    public const int PsnTarget = 1_000_000;

    /// <summary>What to put on the screen for a refusal, in the words the front door uses.</summary>
    public static string Explain(ConnectRefusal refusal) => refusal switch
    {
        ConnectRefusal.None => "",
        ConnectRefusal.NotRegistered => "This console is not registered.",
        ConnectRefusal.NoAddress => "This console is only reachable through PSN, which is not wired yet.",
        ConnectRefusal.NoRegistration => "No usable registration for this console - register it again.",
        _ => "This console cannot be connected to.",
    };
}

/// <summary>
/// Where a prepared request becomes a session.
///
/// A seam and not a static call, for the reason every other seam here is one: the real thing needs
/// a console on the network, and the rules above are what a test can hold. What crosses it is the
/// request, so a fake starter asserts the assembling and the real one asserts nothing.
/// </summary>
public interface IConsoleSessionStarter
{
    /// <summary>Starts a session, and answers with what libchiaki said.</summary>
    ChiakiError Start(ConnectRequest request);
}

/// <summary>
/// The real one: the four calls <see cref="ExchangeCapture"/> makes, without the recording.
///
/// The session is created, started and released here. THAT IS THE LIMIT OF THIS TASK and it is
/// deliberate: there is no screen to hand a running session to, and a session held open behind a
/// window that cannot show it is worse than one that reports what it managed and stops. PP600 is
/// the caller; the screen it hands to is Block C's.
/// </summary>
public sealed class NativeConsoleSessionStarter : IConsoleSessionStarter
{
    /// <inheritdoc />
    public ChiakiError Start(ConnectRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        ChiakiSession.LibInit();

        using var info = new ChiakiConnectInfo { Host = request.Host, Ps5 = request.Ps5 };
        info.SetRegistKey(request.RegistKey);
        info.SetMorning(request.Morning);
        info.SetVideoPreset(ChiakiVideoResolution.P720, ChiakiVideoFps.Fps60);
        info.SetFlags(autoDowngrade: true, keyboard: false, dualSense: false, idrOnFecFailure: false);

        using ChiakiSession? session = ChiakiSession.TryCreate(info, null, out ChiakiError created);
        if (session is null)
            return created;

        return session.Start();
    }
}
