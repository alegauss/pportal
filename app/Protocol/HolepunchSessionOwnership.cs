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
}
