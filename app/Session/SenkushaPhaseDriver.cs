namespace ChiakiNg.Session;

/// <summary>Who measures the link on a given tree.</summary>
public enum SenkushaDriver
{
    /// <summary>The C's own senkusha, because no session path installs a phase.</summary>
    TheC,

    /// <summary>A session path installs one, and the C's run has left session.c.</summary>
    ThePort,

    /// <summary>
    /// Both, which for senkusha is a state to ship rather than a transition to finish.
    ///
    /// This is where the two drivers differ. session.c reaches the C's stream run and never the
    /// callback, so "both" there means the port is wrong about which code runs. Here the callback is
    /// ASKED FOR FIRST and the C's run is the other arm, so both present means the port measures and
    /// the C is what a tree with nothing installed still gets - which is PP792's deliberate choice
    /// and PP763's lesson kept.
    /// </summary>
    Fallback,

    /// <summary>Neither, which is a session that measures nothing at all.</summary>
    Nobody,
}

/// <summary>
/// PP809, under PP784: the senkusha phase has a driver, asked of the two files that decide.
///
/// PP764 wrote this question for the stream after PP696 shipped green with nothing installed, and
/// the answer cost a revert. Senkusha reached the same shape: PP792 put the callback in session.c,
/// PP805 wired the arrivals, and until this line no file constructed a phase - two facts in two
/// files, each a legitimate state on its own and together a port that measures nothing.
///
/// READ AS CODE AND NOT AS TEXT, which <see cref="DeadAssertions.CodeOnly"/> is for: this tree
/// spells its own symbols in string literals and comments all the time, and PP735 named the trap.
///
/// THE PHASE'S TYPE AND NOT ITS INSTALL, which is PP806's finding arriving before it could cost
/// anything. Two seams now declare an InstallOn, so the call says nothing about which one is being
/// installed; the construction names the type, and no other seam can spell it.
/// </summary>
public static class SenkushaPhaseDriver
{
    /// <summary>The C that either runs its own senkusha or does not.</summary>
    public const string SessionRelativePath = @"lib\src\session.c";

    /// <summary>The call that means the C is still measuring.</summary>
    public const string TheCsRun = "chiaki_senkusha_run(";

    /// <summary>And the construction that means a path installs the port's.</summary>
    public const string ThePortsPhase = "new ManagedSenkushaPhase(";

    /// <summary>What makes a file one of the paths this question is about, which is PP767's rule.</summary>
    public const string BuildsASession = StreamPhaseDriver.BuildsASession;

    /// <summary>Whether session.c still measures the link itself.</summary>
    public static bool TheCRunsIt(string sessionSource)
    {
        ArgumentNullException.ThrowIfNull(sessionSource);

        return CCall.Code(sessionSource).Contains(TheCsRun, StringComparison.Ordinal);
    }

    /// <summary>The files that open a session AND put a managed senkusha phase on it.</summary>
    public static IReadOnlyList<string> DrivingSessionPathsIn(IEnumerable<(string Name, string Text)> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        var found = new List<string>();

        foreach ((string name, string text) in files)
        {
            string code = DeadAssertions.CodeOnly(text);

            if (code.Contains(BuildsASession, StringComparison.Ordinal)
                && code.Contains(ThePortsPhase, StringComparison.Ordinal))
            {
                found.Add(name);
            }
        }

        return found;
    }

    /// <summary>
    /// PP784: the session paths that must install a phase and do not.
    ///
    /// THIS QUESTION ONLY BECOMES ANSWERABLE WHEN THE C'S ARM GOES. While session.c ran its own
    /// senkusha for anything with nothing installed, a path that forgot one measured correctly and
    /// there was nothing to name. Now it measures nothing.
    ///
    /// AND THE FAILURE IS QUIET, which is why this exists rather than a log line. The stream phase's
    /// absence quits the session with UNINITIALIZED and PP801 found it in one run. Senkusha's error
    /// is not fatal: session.c takes 1454/1454/1000 and streams anyway, so the run looks fine and the
    /// launch spec carries numbers no console answered. PP764's shape - two facts in two files,
    /// each legitimate alone - with the symptom removed.
    ///
    /// The exclusion list is <see cref="StreamPhaseDriver.SessionPathsThatDoNotStream"/>'s, and
    /// deliberately the same one: a path that opens a session and does not mean to stream does not
    /// measure a link either, and two lists would be one rename away from disagreeing.
    /// </summary>
    public static IReadOnlyList<string> PathsThatMustMeasure(IEnumerable<(string Name, string Text)> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        IReadOnlyList<(string Name, string Text)> all = [.. files];
        var measuring = new HashSet<string>(DrivingSessionPathsIn(all), StringComparer.OrdinalIgnoreCase);

        return
        [
            .. all
                .Where(one => !measuring.Contains(one.Name))
                .Where(one => !StreamPhaseDriver.SessionPathsThatDoNotStream.Contains(
                    Path.GetFileName(one.Name), StringComparer.OrdinalIgnoreCase))
                .Where(one => DeadAssertions.CodeOnly(one.Text)
                    .Contains(BuildsASession, StringComparison.Ordinal))
                .Select(one => one.Name)
                .Order(StringComparer.Ordinal),
        ];
    }

    /// <summary>Which of the four states a tree is in.</summary>
    public static SenkushaDriver DriverOf(string sessionSource, IEnumerable<(string Name, string Text)> appFiles)
    {
        IReadOnlyList<(string Name, string Text)> files = [.. appFiles];

        bool c = TheCRunsIt(sessionSource);
        bool port = DrivingSessionPathsIn(files).Count > 0;

        return (c, port) switch
        {
            (true, false) => SenkushaDriver.TheC,
            (false, true) => SenkushaDriver.ThePort,
            (true, true) => SenkushaDriver.Fallback,
            _ => SenkushaDriver.Nobody,
        };
    }

    /// <summary>session.c, or null outside a checkout.</summary>
    public static string? LocateSession() => SanitizerSource.LocateRelative(SessionRelativePath);
}
