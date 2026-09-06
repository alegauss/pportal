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

    /// <summary>Which of the four states a tree is in.</summary>
    public static StreamDriver DriverOf(string sessionSource, IEnumerable<(string Name, string Text)> appFiles)
    {
        bool c = TheCRunsIt(sessionSource);
        bool port = InstallersIn(appFiles).Count > 0;

        return (c, port) switch
        {
            (true, false) => StreamDriver.TheC,
            (false, true) => StreamDriver.ThePort,
            (true, true) => StreamDriver.Both,
            _ => StreamDriver.Nobody,
        };
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
