using ChiakiNg.Protocol;

namespace ChiakiNg.Session;

/// <summary>Who runs the session thread's stream phase on a given tree.</summary>
public enum StreamDriver
{
    /// <summary>The C's own stream connection, which is what every tree ran on before the handover.</summary>
    TheC,

    /// <summary>Something in app installs a run on the session before it starts.</summary>
    ThePort,

    /// <summary>
    /// Neither, which is a session that reaches the stream phase and stops there.
    ///
    /// The state PP696 shipped in. It is the one this whole class exists to name.
    /// </summary>
    Nobody,

    /// <summary>
    /// Both, which is a tree mid-transition rather than a state to ship.
    ///
    /// Named rather than folded into TheC: the C's run wins, because session.c calls it and never
    /// reaches the callback - so a port that thought it had taken the stream over would be wrong
    /// about which code was running, with nothing to say so.
    /// </summary>
    Both,
}

/// <summary>
/// PP764: the stream phase has a driver, asked of the two files that between them decide.
///
/// PP696 was green and the client could not stream. PP762 says why no check saw it: what was
/// missing was a CALL at a composition root, and every census in this tree counts types and members.
/// SeamReach counts interfaces no class implements; the run-host census counts members with no
/// counterpart. A function pointer nobody installs is neither.
///
/// A GATE WITH NO CONSOLE CANNOT OPEN A SESSION, and that is not the hole. The hole is that two
/// facts sat in two files and nothing read them together: session.c had stopped calling
/// chiaki_stream_connection_run, and no file in app installed a callback in its place. Either alone
/// is a legitimate state, and together they are a session that stops.
///
/// READ AS CODE AND NOT AS TEXT. <see cref="DeadAssertions.CodeOnly"/> strips comments and blanks
/// string contents, which matters more here than usual: <see cref="StreamRunHandoff"/> spells the
/// install's own name in a string literal, because describing the contract is its job. A reader
/// keyed on flat text would find that and call the phase driven. PP735 named the trap - a symbol in
/// quotes is a model and not a caller - and this is the fourth check to want it.
///
/// WHAT THIS DOES NOT DO is prove a run works. It proves somebody is expected to make one, which is
/// the fact whose absence cost a revert.
/// </summary>
public static class StreamPhaseDriver
{
    /// <summary>The C that either calls its own run or does not.</summary>
    public const string SessionRelativePath = @"lib\src\session.c";

    /// <summary>The call that means the C is still driving it.</summary>
    public const string TheCsRun = "chiaki_stream_connection_run(";

    /// <summary>
    /// The call that means the port is, spelled with its receiver.
    ///
    /// The dot is doing work: <c>StreamHandover</c> DECLARES this method, and a declaration reads
    /// <c>public void InstallOn(</c> with nothing before it. Requiring the receiver tells the one
    /// site that defines it from the sites that use it, so the declaring file needs no exception -
    /// and it is excluded by name as well, because a check that rests on one of those is a check
    /// that a rename can quietly empty.
    /// </summary>
    public const string ThePortsInstall = ".InstallOn(";

    /// <summary>Where that method is declared, which is not a call of it.</summary>
    public const string DeclaringFile = "StreamHandover.cs";

    /// <summary>
    /// PP767: what makes a file one of the paths this question is about.
    ///
    /// The first version asked ANY file in app, and PP762 answered it from inside
    /// ManagedStreamPhase - the class that would do the driving, which nothing constructs. So the
    /// check passed on a tree where the C hands over, a composition root exists, and no session ever
    /// builds one: a client that cannot stream and a gate saying it can.
    ///
    /// A path that opens a session is what has to install, and this is how one is recognised.
    /// </summary>
    public const string BuildsASession = "ChiakiSession.TryCreate(";

    /// <summary>And the composition root, whose construction is the other way to install one.</summary>
    public const string ThePortsPhase = "new ManagedStreamPhase(";

    /// <summary>
    /// PP806: the seam's own type, which is what a path doing its own composition must name.
    ///
    /// <see cref="ThePortsInstall"/> was the whole of the second spelling until PP792 put a second
    /// handover in the tree with a method of the same name. A dot tells a call from a declaration
    /// and says nothing about WHICH seam is being installed, so the moment a session path installs a
    /// senkusha phase the stream's question is answered by an install that has nothing to do with
    /// the stream - and what that would hide is the state PP762 shipped: the C hands the phase over,
    /// a root exists, and no session installs one.
    ///
    /// A TYPE AND NOT A LONGER EXCLUSION LIST. The receiver at a call site is a variable, so its
    /// type is not there to be read; where it IS spelled is the construction, and no other seam can
    /// spell this one. Excluding files by name instead would be a list a rename empties in silence.
    /// </summary>
    public const string ThePortsHandover = "new StreamHandover(";

    /// <summary>
    /// PP801: the session paths that open one and do not mean to stream, named so the rest is closed.
    ///
    /// The host's selftest builds sessions to assert what a REFUSAL looks like - a bad address, an
    /// empty one, a create that must fail - and a phase on those would be a stream nobody asked for
    /// on a run that has no console. Every other path that opens a session since PP783 has to
    /// install one, because the C no longer runs the stream and a session with no phase quits.
    ///
    /// A LIST OF ONE, and it is the reason for having a list at all: <see cref="PathsThatMustDrive"/>
    /// is otherwise a name check, and a name check goes quietly empty when a file is renamed.
    /// </summary>
    public static IReadOnlyList<string> SessionPathsThatDoNotStream { get; } = ["SelfTest.cs"];

    /// <summary>
    /// PP801: the files that open a session, must install a phase, and do not.
    ///
    /// PP783 flipped the run and wired one path. The other two - the captures and the front door -
    /// reached a callback that was not there and quit with UNINITIALIZED, each losing something
    /// different and none of them saying so. This is the question asked of all of them at once.
    /// </summary>
    public static IReadOnlyList<string> PathsThatMustDrive(IEnumerable<(string Name, string Text)> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        IReadOnlyList<(string Name, string Text)> all = [.. files];
        var driving = new HashSet<string>(DrivingSessionPathsIn(all), StringComparer.OrdinalIgnoreCase);

        return
        [
            .. all
                .Where(one => !driving.Contains(one.Name))
                .Where(one => !SessionPathsThatDoNotStream.Contains(
                    Path.GetFileName(one.Name), StringComparer.OrdinalIgnoreCase))
                .Where(one => DeadAssertions.CodeOnly(one.Text)
                    .Contains(BuildsASession, StringComparison.Ordinal))
                .Select(one => one.Name)
                .Order(StringComparer.Ordinal),
        ];
    }

    /// <summary>Whether session.c still runs the stream itself.</summary>
    public static bool TheCRunsIt(string sessionSource)
    {
        ArgumentNullException.ThrowIfNull(sessionSource);

        return CCall.Code(sessionSource).Contains(TheCsRun, StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether one of app's own files installs a run, given each file's name and text.
    ///
    /// Taken as a sequence rather than a directory so the reader can be asked about text this
    /// tree's own tests write, which is the only way its negative side is ever exercised: a tree
    /// that has the call cannot demonstrate what happens to one that does not.
    /// </summary>
    public static IReadOnlyList<string> InstallersIn(IEnumerable<(string Name, string Text)> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        var found = new List<string>();

        foreach ((string name, string text) in files)
        {
            if (Path.GetFileName(name).Equals(DeclaringFile, StringComparison.OrdinalIgnoreCase))
                continue;

            if (DeadAssertions.CodeOnly(text).Contains(ThePortsInstall, StringComparison.Ordinal))
                found.Add(name);
        }

        return found;
    }

    /// <summary>
    /// PP767: the files that open a session AND put a managed phase on it.
    ///
    /// This is the question <see cref="InstallersIn"/> should have been asking. An install anywhere
    /// says a composition root exists; an install on a path that BUILDS A SESSION says one is
    /// actually reached. PP762 made the difference visible by adding the first without the second.
    ///
    /// Either spelling counts: constructing the phase, or calling the install directly. The phase is
    /// what a caller normally reaches for and the install is what the phase itself does, so a path
    /// that did its own composition is not a path this should refuse.
    ///
    /// PP806: AND THE SECOND SPELLING NAMES THE SEAM AS WELL AS THE CALL. A bare install used to be
    /// enough, which was exact while <see cref="StreamHandover"/> was the only class in the tree
    /// declaring that method - PP792's senkusha handover ended that. A path composing its own
    /// constructs the thing it installs, so <see cref="ThePortsHandover"/> is the evidence and the
    /// install is what it then does with it; a path installing some OTHER seam spells neither.
    /// </summary>
    public static IReadOnlyList<string> DrivingSessionPathsIn(IEnumerable<(string Name, string Text)> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        var found = new List<string>();

        foreach ((string name, string text) in files)
        {
            if (Path.GetFileName(name).Equals(DeclaringFile, StringComparison.OrdinalIgnoreCase))
                continue;

            string code = DeadAssertions.CodeOnly(text);

            if (!code.Contains(BuildsASession, StringComparison.Ordinal))
                continue;

            bool composesItsOwn = code.Contains(ThePortsHandover, StringComparison.Ordinal)
                && code.Contains(ThePortsInstall, StringComparison.Ordinal);

            if (code.Contains(ThePortsPhase, StringComparison.Ordinal) || composesItsOwn)
                found.Add(name);
        }

        return found;
    }

    /// <summary>
    /// Which of the four states a tree is in.
    ///
    /// PP767: the port's half is now a session path that installs, not an install anywhere. The
    /// weaker reading passed on a tree whose only install was inside the class that would do the
    /// driving, which nothing constructed.
    /// </summary>
    public static StreamDriver DriverOf(string sessionSource, IEnumerable<(string Name, string Text)> appFiles)
    {
        IReadOnlyList<(string Name, string Text)> files = [.. appFiles];

        bool c = TheCRunsIt(sessionSource);
        bool port = DrivingSessionPathsIn(files).Count > 0;

        return (c, port) switch
        {
            (true, false) => StreamDriver.TheC,
            (false, true) => StreamDriver.ThePort,
            (true, true) => StreamDriver.Both,
            _ => StreamDriver.Nobody,
        };
    }

    /// <summary>How a session path says where it is pointing the session.</summary>
    public const string TheSessionsTarget = "Host = ";

    /// <summary>And how a phase is told, which is the argument that has to agree.</summary>
    public const string APhasesTarget = "IPAddress.Parse(";

    /// <summary>The two phases a session path composes, both taking an address of their own.</summary>
    public static IReadOnlyList<string> ThePhases { get; } =
        ["new ManagedStreamPhase(", "new ManagedSenkushaPhase("];

    /// <summary>
    /// PP830: the session paths that point a phase somewhere other than the session.
    ///
    /// A PHASE OPENS ITS OWN SOCKET, and that is the reason this can go wrong rather than the reason
    /// it cannot. The session's connect info is pointed at a relay and the phase's takion is not, so
    /// ctrl and senkusha reach the console through the forwarder while the stream dials the console
    /// directly - which the console appears to refuse, and which a recording of the relay cannot see
    /// at all. ExchangeCapture carried a comment arguing the opposite for two tasks.
    ///
    /// READ AS TWO IDENTIFIERS AND NOT AS ADDRESSES. Neither value exists until a console answers,
    /// so what a gate can compare is the NAME each is spelled with: whatever a path assigns to Host
    /// is what it must hand its phases. That is exactly the mistake - <c>Host = target</c> beside
    /// <c>IPAddress.Parse(address)</c> - and it is invisible to every check that counts calls.
    ///
    /// A path with no Host and a path with no phase are both silent here. This asks one question of
    /// the paths that do both, which <see cref="DrivingSessionPathsIn"/> already names.
    /// </summary>
    /// <returns>Each disagreeing file, with the two identifiers, ordered by name.</returns>
    public static IReadOnlyList<(string Name, string Session, string Phase)> PhasesPointedElsewhere(
        IEnumerable<(string Name, string Text)> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        IReadOnlyList<(string Name, string Text)> all = [.. files];
        var driving = new HashSet<string>(DrivingSessionPathsIn(all), StringComparer.OrdinalIgnoreCase);

        var found = new List<(string, string, string)>();

        foreach ((string name, string text) in all.Where(one => driving.Contains(one.Name)))
        {
            string code = DeadAssertions.CodeOnly(text);

            if (Identifier(code, TheSessionsTarget) is not { } pointed)
                continue;

            foreach (string phase in ThePhases)
            {
                if (Arguments(code, phase) is not { } arguments)
                    continue;

                if (Identifier(arguments, APhasesTarget) is not { } aimed)
                    continue;

                if (!string.Equals(aimed, pointed, StringComparison.Ordinal))
                    found.Add((name, pointed, aimed));
            }
        }

        return [.. found.OrderBy(one => one.Item1, StringComparer.Ordinal)];
    }

    /// <summary>The identifier a marker is followed by, or null where the marker is not there.</summary>
    private static string? Identifier(string code, string marker)
    {
        int at = code.IndexOf(marker, StringComparison.Ordinal);
        if (at < 0)
            return null;

        at += marker.Length;

        int end = at;
        while (end < code.Length && (char.IsLetterOrDigit(code[end]) || code[end] == '_'))
            end++;

        return end > at ? code[at..end] : null;
    }

    /// <summary>One construction's argument list, parens balanced, or null where it is not there.</summary>
    private static string? Arguments(string code, string construction)
    {
        int at = code.IndexOf(construction, StringComparison.Ordinal);
        if (at < 0)
            return null;

        int open = at + construction.Length;
        var depth = 1;

        for (int scan = open; scan < code.Length; scan++)
        {
            if (code[scan] == '(')
                depth++;
            else if (code[scan] == ')' && --depth == 0)
                return code[open..scan];
        }

        return null;
    }

    /// <summary>session.c, or null outside a checkout.</summary>
    public static string? LocateSession() => SanitizerSource.LocateRelative(SessionRelativePath);

    /// <summary>Every C# file under app, as a name and its text, or empty outside a checkout.</summary>
    public static IReadOnlyList<(string Name, string Text)> AppFiles()
    {
        if (SanitizerSource.RepositoryRoot() is not { } root)
            return [];

        string app = Path.Combine(root, "app");
        if (!Directory.Exists(app))
            return [];

        return
        [
            .. Directory.EnumerateFiles(app, "*.cs", SearchOption.AllDirectories)
                .Where(one => !one.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    && !one.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .Select(one => (one, File.ReadAllText(one))),
        ];
    }
}
