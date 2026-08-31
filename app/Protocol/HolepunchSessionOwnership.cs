using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP592, under PP33: who destroys the holepunch session itself, which is a different question from
/// PP502's about the two sockets it hands out.
///
/// PP502 answered the sockets: the creator closes neither, each is closed by what it was handed to,
/// and the copy of the handle is what makes that safe. This is the handle above them, and the answer
/// is the opposite shape - the creator does not destroy it either, and the CALLEE does.
///
/// WHY IT MATTERS TO PP33. Two of the nine asks <see cref="HolepunchSeam"/> records are
/// chiaki_holepunch_session_fini, on session.c's two teardown paths. Read as a list of calls they
/// look like the cheapest of the nine to remove - hand the destruction back to whoever created the
/// session and two sites go. That reading is wrong, and this is what says so: streamsession.cpp
/// calls chiaki_holepunch_session_init and never finis, so session.c's two sites are the ONLY
/// destructor the Qt client has. Moving them to the caller leaks the PSN session, the websocket and
/// UPnP threads, the port mappings and the curl share.
///
/// AND THERE IS A SECOND OWNER. The shim wraps fini as chiaki_shim_holepunch_session_fini for the
/// managed driver, which creates its own handle through chiaki_shim_holepunch_session_init. Today
/// those two owners can never meet: chiaki_shim_session_create hands chiaki_session_init a connect
/// info whose holepunch_session the shim never writes, so the C session the shim builds always has
/// a null handle and its fini never runs on a managed one.
///
/// THAT DISJOINTNESS IS HELD BY NOTHING ELSE. Wiring the handle into the shim's connect info is the
/// natural next move for anything porting the PSN path - it is what the Qt client does at
/// streamsession.cpp's chiaki_connect_info.holepunch_session - and the day it happens both owners
/// fini one handle. So the check below is on the shim's silence, not on the managed side's care.
/// </summary>
public static class HolepunchSessionOwnership
{
    /// <summary>Where session.c's nine asks live.</summary>
    public const string SessionRelativePath = @"lib\src\session.c";

    /// <summary>session.c, or null outside a checkout.</summary>
    public static string? LocateSession() => SanitizerSource.LocateRelative(SessionRelativePath);

    /// <summary>Where the Qt client creates the session it never destroys.</summary>
    public const string QtClientRelativePath = @"gui\src\streamsession.cpp";

    /// <summary>The shim, which is the second owner.</summary>
    public const string ShimRelativePath = @"shim\chiaki_shim.c";

    /// <summary>The destructor both sides reach for.</summary>
    public const string Fini = "chiaki_holepunch_session_fini";

    /// <summary>The constructor the Qt client calls.</summary>
    public const string Init = "chiaki_holepunch_session_init";

    /// <summary>The connect-info field that would put the two owners on one handle.</summary>
    public const string ConnectInfoField = "holepunch_session";

    /// <summary>
    /// PP596: the field the four asks that are NOT guarded on the handle are guarded on instead.
    ///
    /// session.c's nine sites split. Five test <c>session-&gt;holepunch_session</c> directly; the
    /// regist info, the offer, the punch and the data socket test <c>session-&gt;rudp</c>. That reads
    /// as a second condition and is the same one: rudp is assigned in exactly one place, inside the
    /// handle's own guard, so a non-null rudp implies a non-null handle.
    ///
    /// IT IS A JOIN AND NOT A COINCIDENCE, which is why it is checked. An assignment to rudp
    /// anywhere else makes four asks reachable with a null holepunch session -
    /// chiaki_get_regist_info dereferences it immediately - so this is a crash the tree is one edit
    /// away from, and nothing else in the suite is looking at it.
    /// </summary>
    public const string RudpField = "rudp";

    /// <summary>The function whose one caller is the whole of the reachability question.</summary>
    public const string SessionInit = "chiaki_session_init";

    /// <summary>The Qt client, or null outside a checkout - and Qt is not built, so it may be gone.</summary>
    public static string? LocateQtClient() => SanitizerSource.LocateRelative(QtClientRelativePath);

    /// <summary>The shim, or null outside a checkout.</summary>
    public static string? LocateShim() => SanitizerSource.LocateRelative(ShimRelativePath);

    /// <summary>
    /// Whether the Qt client creates a holepunch session and leaves the destruction to session.c.
    ///
    /// Both halves. "Creates one" alone would be true of a file that also destroyed it, and "never
    /// finis" alone is true of every file in the tree that has nothing to do with the holepunch.
    /// </summary>
    public static bool TheQtClientCreatesAndDoesNotDestroy(string qtClientSource)
    {
        ArgumentNullException.ThrowIfNull(qtClientSource);

        return qtClientSource.Contains(Init + "(", StringComparison.Ordinal)
            && !qtClientSource.Contains(Fini, StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the shim keeps its holepunch handle out of the connect info it hands
    /// chiaki_session_init.
    ///
    /// This is the whole guard, and it is a check on an absence, which is the only kind that can
    /// see this coming. The shim's nine wrappers take the handle as a bare argument from managed
    /// code; the moment one of them - or a new setter - writes it into the connect info instead,
    /// chiaki_session_fini finis a session the managed owner also finis.
    ///
    /// Read on the shim's own assignments rather than on the field's name appearing at all: the
    /// wrappers are named for it, and a rule that banned the word would fail on
    /// chiaki_shim_holepunch_session_init.
    /// </summary>
    public static bool TheShimNeverWiresTheHandleIn(string shimSource)
    {
        ArgumentNullException.ThrowIfNull(shimSource);

        foreach (string line in shimSource.ReplaceLineEndings("\n").Split('\n'))
        {
            string trimmed = line.TrimStart();

            // A comment discussing the field is not the shim doing it, and this file's comments
            // discuss it at length - which is the same trap CompileMessages and BuildWorkflow each
            // walked into on rem and # lines.
            if (trimmed.StartsWith("//", StringComparison.Ordinal)
                || trimmed.StartsWith('*')
                || trimmed.StartsWith("/*", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (string reach in (string[])[".", "->"])
            {
                int at = trimmed.IndexOf(reach + ConnectInfoField, StringComparison.Ordinal);
                if (at < 0)
                    continue;

                // An assignment to it, rather than a read of it.
                string after = trimmed[(at + reach.Length + ConnectInfoField.Length)..].TrimStart();
                if (after.StartsWith('=') && !after.StartsWith("==", StringComparison.Ordinal))
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// PP596: whether session.c assigns rudp only inside the holepunch handle's own guard.
    ///
    /// The join four of the nine asks stand on. Read as the assignment's position relative to the
    /// guard rather than as a count: what matters is that every one of them is under a line testing
    /// the handle, and a second assignment elsewhere is the edit this exists to catch.
    ///
    /// The initialiser in chiaki_session_init is not an assignment for this purpose - it sets the
    /// field to NULL, which is the state the guard protects rather than a way past it.
    /// </summary>
    public static IReadOnlyList<int> RudpAssignmentsOutsideTheGuard(string sessionSource)
    {
        ArgumentNullException.ThrowIfNull(sessionSource);

        string[] lines = sessionSource.ReplaceLineEndings("\n").Split('\n');
        var loose = new List<int>();
        bool guarded = false;

        for (int i = 0; i < lines.Length; i++)
        {
            string trimmed = lines[i].TrimStart();

            if (trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith('*'))
                continue;

            // A line testing the handle opens the region the assignment is allowed in. The region
            // is left at the next line whose indentation returns to the guard's own, which is what
            // a closing brace at that depth is.
            if (trimmed.Contains("if(session->" + ConnectInfoField, StringComparison.Ordinal)
                || trimmed.Contains("if (session->" + ConnectInfoField, StringComparison.Ordinal))
            {
                guarded = true;
                continue;
            }

            if (guarded && trimmed.StartsWith('}') && Indent(lines[i]) <= 1)
                guarded = false;

            if (!trimmed.Contains("session->" + RudpField + " =", StringComparison.Ordinal))
                continue;

            // NULL is the guarded state, not an escape from it.
            if (trimmed.Contains("= NULL", StringComparison.Ordinal))
                continue;

            if (!guarded)
                loose.Add(i + 1);
        }

        return loose;
    }

    /// <summary>How many leading tabs or four-space steps a line carries.</summary>
    private static int Indent(string line)
    {
        int depth = 0;

        foreach (char c in line)
        {
            if (c == '\t')
                depth++;
            else if (c != ' ')
                break;
        }

        return depth;
    }

    /// <summary>
    /// PP596: the files that call chiaki_session_init, which is where a holepunch session can enter
    /// one at all.
    ///
    /// Two, and the difference between them is the whole finding. The Qt client passes a handle and
    /// is not compiled by a default build - PP21 turned Qt off and PP529 records that only
    /// `compile.cmd gui` builds it. The shim passes none, which
    /// <see cref="TheShimNeverWiresTheHandleIn"/> holds. So nothing this port BUILDS reaches the
    /// nine asks: they are compiled, and dead.
    /// </summary>
    public static IReadOnlyList<string> SessionInitCallers { get; } =
        [QtClientRelativePath, ShimRelativePath];

    /// <summary>Where the field the Qt client needs is declared.</summary>
    public const string SessionHeaderRelativePath = @"lib\include\chiaki\session.h";

    /// <summary>session.h, or null outside a checkout.</summary>
    public static string? LocateSessionHeader()
        => SanitizerSource.LocateRelative(SessionHeaderRelativePath);

    /// <summary>
    /// PP597: whether the Qt client still compiles against the field PP33's deletion would remove.
    ///
    /// This is a guard on a FUTURE change rather than on the tree as it is, which is why it is worth
    /// writing down. PP596 established that nothing a default build compiles reaches the nine asks,
    /// so deleting them changes no shipped behaviour - what it changes is whether gui/ can be
    /// compiled at all, because streamsession.cpp assigns
    /// <c>chiaki_connect_info.holepunch_session</c> and the field would be gone.
    ///
    /// AND THAT IS NOT A DEAD END, IT IS A PERMANENT RED. The drift checks only READ gui/, so they
    /// survive; GuiFreshness does not. It compares the newest gui/ source against the client
    /// somebody last built and reports Stale as a FAILURE, deliberately - PP270's argument that a
    /// warning among four thousand passing tests is a warning nobody reads. `compile.cmd gui` is the
    /// only thing that refreshes that binary. Take the field away and the command cannot run, so
    /// every later edit to gui/ - which the drift checks require - leaves a client that can never be
    /// made fresh again, for anyone who has ever built one.
    ///
    /// So PP33's session.c half owes a decision about gui/ in the same commit: retire the client's
    /// build, or give GuiFreshness a state for "cannot be rebuilt". Whichever it is, this goes red
    /// and names it.
    /// </summary>
    public static bool TheQtClientCompilesAgainstTheField(string qtClientSource, string sessionHeader)
    {
        ArgumentNullException.ThrowIfNull(qtClientSource);
        ArgumentNullException.ThrowIfNull(sessionHeader);

        return qtClientSource.Contains("." + ConnectInfoField, StringComparison.Ordinal)
            && sessionHeader.Contains(ConnectInfoField, StringComparison.Ordinal);
    }
}
