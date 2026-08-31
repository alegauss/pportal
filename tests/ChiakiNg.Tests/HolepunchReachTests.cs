using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP596: nothing this port builds reaches session.c's nine holepunch asks.
///
/// PP33 is sized from those nine as though they run. They do not, in any artifact a default build
/// produces: a holepunch session enters a ChiakiSession through exactly one assignment, in the Qt
/// client, and PP21 turned Qt off while PP529 recorded that only `compile.cmd gui` builds it. The
/// shim is the other caller of chiaki_session_init and passes no handle, which PP592 holds.
///
/// SO THE DEPENDENCY THAT IS LEFT IN session.c IS A COMPILE-TIME ONE. Deleting the nine changes the
/// behaviour of nothing this repository ships - it changes what gui/ can be compiled against, and
/// gui/ is kept as the oracle the drift checks read. That is a different blocker from the one PP33's
/// line implies, and a smaller one than a console.
///
/// The four sites guarded on rudp rather than on the handle are held here too, and that one is not
/// bookkeeping: an assignment to rudp outside the guard makes them reachable with a null handle, and
/// chiaki_get_regist_info dereferences it on the next line.
/// </summary>
public class HolepunchReachTests(ITestOutputHelper output)
{
    /// <summary>
    /// THE JOIN: rudp is assigned only inside the holepunch handle's own guard.
    ///
    /// Four of the nine test <c>session-&gt;rudp</c> and not the handle, which reads as a second
    /// condition and is the same one. It is one line away from being false, and the failure is a
    /// null dereference rather than a wrong answer.
    /// </summary>
    [Fact]
    public void RudpIsSetOnlyWhereTheHandleIsKnownToBeThere()
    {
        if (HolepunchSessionOwnership.LocateSession() is not { } path)
            return;

        IReadOnlyList<int> loose =
            HolepunchSessionOwnership.RudpAssignmentsOutsideTheGuard(File.ReadAllText(path));

        Assert.True(
            loose.Count == 0,
            "session.c assigns rudp outside `if(session->holepunch_session)`, so the regist info, "
                + "the offer, the punch and the data socket can run with a null handle - "
                + "chiaki_get_regist_info dereferences it. Lines: " + string.Join(", ", loose));
    }

    /// <summary>
    /// And the reader sees an assignment that escaped the guard, so the check above is not green on
    /// a pattern that stopped matching.
    /// </summary>
    [Fact]
    public void AnAssignmentOutsideTheGuardIsFound()
    {
        const string escaped = """
            	session->rudp = NULL;

            	if(session->holepunch_session)
            	{
            		session->rudp = chiaki_rudp_init(sock, log);
            	}

            	session->rudp = chiaki_rudp_init(other, log);
            """;

        int line = Assert.Single(
            HolepunchSessionOwnership.RudpAssignmentsOutsideTheGuard(escaped));

        Assert.Equal(8, line);

        // And the two legitimate ones are not reported: the NULL initialiser, and the guarded call.
        Assert.Empty(HolepunchSessionOwnership.RudpAssignmentsOutsideTheGuard(
            "\tsession->rudp = NULL;\n\tif(session->holepunch_session)\n\t{\n"
                + "\t\tsession->rudp = chiaki_rudp_init(sock, log);\n\t}\n"));
    }

    /// <summary>
    /// Two files call chiaki_session_init, and only one of them passes a handle.
    ///
    /// That is the whole of the reachability argument, so it is named rather than counted: a third
    /// caller is a third answer to "can the nine run", and it would arrive silently.
    /// </summary>
    [Fact]
    public void OnlyTwoThingsBuildAChiakiSession()
    {
        Assert.Equal(
            [@"gui\src\streamsession.cpp", @"shim\chiaki_shim.c"],
            HolepunchSessionOwnership.SessionInitCallers);

        if (SanitizerSource.RepositoryRoot() is not { } root)
            return;

        var callers = new List<string>();

        foreach (string file in Directory.EnumerateFiles(root, "*.c*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(root, file);

            if (relative.Contains(@"\build\", StringComparison.OrdinalIgnoreCase)
                || relative.StartsWith("build", StringComparison.OrdinalIgnoreCase)
                || relative.Contains(@"\third-party\", StringComparison.OrdinalIgnoreCase)
                || relative.StartsWith("third-party", StringComparison.OrdinalIgnoreCase)
                || relative.Contains(@"\.roadkeep\", StringComparison.OrdinalIgnoreCase)
                || relative.StartsWith(".roadkeep", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (Path.GetExtension(file) is not (".c" or ".cpp"))
                continue;

            string text = File.ReadAllText(file);

            // The definition in session.c is not a call.
            if (text.Contains(HolepunchSessionOwnership.SessionInit + "(", StringComparison.Ordinal)
                && !relative.Equals(
                    HolepunchSessionOwnership.SessionRelativePath, StringComparison.OrdinalIgnoreCase))
            {
                callers.Add(relative);
            }
        }

        callers.Sort(StringComparer.OrdinalIgnoreCase);
        foreach (string caller in callers)
            output.WriteLine(caller);

        Assert.Equal(HolepunchSessionOwnership.SessionInitCallers.Order(StringComparer.OrdinalIgnoreCase), callers);
    }

    /// <summary>
    /// And the one that passes a handle is the Qt client, which is what makes the nine dead in a
    /// default build rather than merely rare.
    ///
    /// The shim's half is PP592's and is not repeated; this is the other side of the same sentence.
    /// </summary>
    [Fact]
    public void TheQtClientIsTheOnlyThingThatPassesAHandle()
    {
        if (HolepunchSessionOwnership.LocateQtClient() is not { } path)
            return;

        string text = File.ReadAllText(path);

        Assert.Contains(
            "holepunch_session = holepunch_session", text, StringComparison.Ordinal);

        Assert.Contains(
            HolepunchSessionOwnership.SessionInit + "(", text, StringComparison.Ordinal);
    }
}
