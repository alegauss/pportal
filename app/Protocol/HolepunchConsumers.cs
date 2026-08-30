using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP544: who consumes libchiaki's holepunch, which is not only session.c.
///
/// PP33's design said "session.c is its only caller", and recorded holepunch-test.c as a file an
/// earlier version had counted "that is not in the tree". It is in the tree:
/// lib/src/remote/holepunch-test.c, a configured executable under CHIAKI_ENABLE_TESTS that links
/// chiaki-lib and is built by the same command that builds the suite.
///
/// It calls eight of the exports directly, so PP33's deletion has a second consumer. Removing curl
/// and json-c from the library breaks this target too, and the break would arrive as a compile
/// error in a file the backlog said was absent - which is the worst place for a surprise, because
/// the first instinct would be to doubt the build rather than the record.
///
/// WHAT IT IS is a standalone manual harness: it takes credentials on the command line and drives a
/// real PSN session. Whether it is ported, deleted with the C, or kept as the hardware probe that
/// lines like PP322 and PP481 keep wanting is a decision - and the point of holding it here is that
/// the decision was not available to make while the file was recorded as gone.
///
/// PP563: AND THERE IS A THIRD, which this port wrote. The shim wraps nine holepunch exports, put
/// there by PP481 so the managed side could drive the C rather than replace it. So the deletion's
/// blast radius is session.c, this harness, and the port's own seam - and the third arrived from
/// inside the same block, from a task the roadmap lists among PP33's satisfied deps.
/// </summary>
public static class HolepunchConsumers
{
    /// <summary>The harness, relative to the repository root.</summary>
    public const string TestHarnessRelativePath = @"lib\src\remote\holepunch-test.c";

    /// <summary>Where its target is declared.</summary>
    public const string LibCMakeRelativePath = @"lib\CMakeLists.txt";

    /// <summary>The file, or null when this is not running out of a checkout.</summary>
    public static string? LocateHarness() => SanitizerSource.LocateRelative(TestHarnessRelativePath);

    /// <summary>Its CMakeLists, or null outside a checkout.</summary>
    public static string? LocateLibCMake() => SanitizerSource.LocateRelative(LibCMakeRelativePath);

    /// <summary>
    /// The exports the harness calls. Eight, and every one of them a function PP33's deletion has
    /// to keep working or remove a caller of.
    /// </summary>
    public static IReadOnlyList<string> HarnessCalls { get; } =
    [
        "chiaki_holepunch_free_device_list",
        "chiaki_holepunch_list_devices",
        "chiaki_holepunch_session_create",
        "chiaki_holepunch_session_fini",
        "chiaki_holepunch_session_get_stun_allocation",
        "chiaki_holepunch_session_init",
        "chiaki_holepunch_session_punch_hole",
        "chiaki_holepunch_session_start",
    ];

    /// <summary>Whether the harness still calls every one of them.</summary>
    public static IReadOnlyList<string> MissingFromHarness(string harnessSource)
    {
        ArgumentNullException.ThrowIfNull(harnessSource);

        return [.. HarnessCalls.Where(call =>
            !harnessSource.Contains(call + "(", StringComparison.Ordinal))];
    }

    /// <summary>
    /// Whether the target is still declared and still links the library. Both halves matter: a
    /// target that stopped linking chiaki-lib would no longer be a consumer, and one that was
    /// deleted outright would settle PP544 by removal rather than by decision.
    /// </summary>
    public static bool TargetStillLinksTheLibrary(string libCMake)
    {
        ArgumentNullException.ThrowIfNull(libCMake);

        return libCMake.Contains("add_executable(holepunch-test", StringComparison.Ordinal)
            && libCMake.Contains("target_link_libraries(holepunch-test chiaki-lib)", StringComparison.Ordinal);
    }

    /// <summary>PP563: the third consumer, which this port wrote itself.</summary>
    public const string ShimRelativePath = @"shim\chiaki_shim.c";

    /// <summary>The shim, or null outside a checkout.</summary>
    public static string? LocateShim() => SanitizerSource.LocateRelative(ShimRelativePath);

    /// <summary>
    /// The exports the shim wraps. Nine, and PP481 is what put them there.
    ///
    /// PP481 implemented the nine asks over the real C rather than replacing it, which PP533 later
    /// named as the direction that removes nothing. That was a deliberate decision and this does
    /// not reopen it. What it records is the consequence nobody wrote down: the port's own shim is
    /// now a caller of the file PP33 exists to delete, so the deletion has THREE consumers - and
    /// the third was created inside the same block, by a task listed as one of PP33's own deps.
    /// </summary>
    public static IReadOnlyList<string> ShimCalls { get; } =
    [
        "chiaki_get_holepunch_sock",
        "chiaki_get_ps_ctrl_port",
        "chiaki_get_ps_selected_addr",
        "chiaki_get_regist_info",
        "chiaki_holepunch_generate_client_device_uid",
        "chiaki_holepunch_session_fini",
        "chiaki_holepunch_session_init",
        "chiaki_holepunch_session_punch_hole",
        "chiaki_holepunch_session_set_recorded",
    ];

    /// <summary>Whether the shim still wraps every one of them.</summary>
    public static IReadOnlyList<string> MissingFromShim(string shimSource)
    {
        ArgumentNullException.ThrowIfNull(shimSource);

        return [.. ShimCalls.Where(call =>
            !shimSource.Contains(call + "(", StringComparison.Ordinal))];
    }

    /// <summary>
    /// Everything the deletion has to answer for, named rather than counted.
    ///
    /// session.c is PP340's seam and has its own model; the others are here. Listing them is
    /// the point - "four" is a number, and what a deletion needs is which.
    ///
    /// PP564: ctrl.c JOINED THIS LIST FROM A LINKER, not from a reading. PP563 said three, having
    /// read the tree; building the library without holepunch.c named a fourth in thirty seconds.
    /// </summary>
    public static IReadOnlyList<string> All { get; } =
        [@"lib\src\session.c", CtrlRelativePath, TestHarnessRelativePath, ShimRelativePath];

    /// <summary>
    /// PP573: what PP33's own line has to say about the count, now that four tasks have moved it.
    ///
    /// Its `why` said "session.c is its only caller" while PP544, PP563 and PP564 each added one -
    /// and that sentence is the first thing a session picking the ready line reads, so the scope of
    /// the deletion was wrong at the point where somebody decides what it costs.
    ///
    /// Held as the count rather than the sentence: the line has 135 characters for its reason and
    /// will be reworded, but a line claiming ONE caller when this list holds four is the defect.
    /// </summary>
    public static bool TheRoadmapLineAgreesOnTheCount(string roadmapLine)
    {
        ArgumentNullException.ThrowIfNull(roadmapLine);

        // The claim that was there, in the shape it was there.
        if (roadmapLine.Contains("only caller", StringComparison.OrdinalIgnoreCase))
            return false;

        return roadmapLine.Contains("four files call it", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>PP564: the fourth consumer, which only the linker found.</summary>
    public const string CtrlRelativePath = @"lib\src\ctrl.c";

    /// <summary>ctrl.c, or null outside a checkout.</summary>
    public static string? LocateCtrl() => SanitizerSource.LocateRelative(CtrlRelativePath);

    /// <summary>
    /// The one export ctrl.c calls, and the whole of its dependency: the control port, guarded by
    /// the same handle-is-null test session.c uses, falling back to SESSION_CTRL_PORT.
    ///
    /// That guard is why this is the cheapest of the four to remove and the easiest to miss: the
    /// file already has an answer for not having a holepunch session.
    /// </summary>
    public const string CtrlCall = "chiaki_get_ps_ctrl_port";

    /// <summary>Whether ctrl.c still asks, and still has its fallback.</summary>
    public static bool CtrlStillAsksWithAFallback(string ctrlSource)
    {
        ArgumentNullException.ThrowIfNull(ctrlSource);

        return ctrlSource.Contains(CtrlCall + "(session->holepunch_session)", StringComparison.Ordinal)
            && ctrlSource.Contains("SESSION_CTRL_PORT", StringComparison.Ordinal);
    }

    /// <summary>
    /// PP564: the one symbol session.c needs that is not in the nine, and is not even prefixed.
    ///
    /// holepunch_session_create_offer carries no chiaki_ prefix, so a sweep keyed on that prefix -
    /// which is how a reader finds these - does not see it. It is exported all the same.
    /// </summary>
    public const string UnprefixedExport = "holepunch_session_create_offer";

    /// <summary>
    /// PP565: the one file in lib/ that curl and json-c are for, measured rather than counted.
    ///
    /// PP33's `remaining` query counts 420 curl and json-c sites and reports them all in
    /// holepunch.c. That is a grep, and a grep cannot see an include that a macro hides or a header
    /// that pulls another in. The compiler can: with holepunch.c out of the source list and BOTH
    /// libraries unlinked - so their headers are not on the include path at all - every remaining
    /// source in lib/ compiles, and libchiaki.a is produced.
    ///
    /// SO THE DEPENDENCY IS EXACTLY ONE FILE DEEP. Nothing else in the library reaches for either
    /// library, which is the DoD line "libchiaki builds with neither curl nor json-c" being true
    /// today for the archive, waiting only on this file leaving.
    ///
    /// The two executables still fail, on the holepunch symbols PP564 measured - which is a
    /// different problem, and the one the four consumers are.
    /// </summary>
    public const string OnlyFileNeedingCurlAndJsonC = @"lib\src\remote\holepunch.c";

    /// <summary>
    /// Whether the CMakeLists still links both, and still builds the one file they are for.
    ///
    /// The measurement is not repeatable in a test - it needs a build with three lines commented
    /// out. What a test CAN hold is that the tree it was measured on is the tree in front of it:
    /// change any of these three and the recorded result is about something else.
    /// </summary>
    public static bool TheMeasuredTreeIsStillThis(string libCMake)
    {
        ArgumentNullException.ThrowIfNull(libCMake);

        return libCMake.Contains("src/remote/holepunch.c", StringComparison.Ordinal)
            && libCMake.Contains("target_link_libraries(chiaki-lib CURL::libcurl)", StringComparison.Ordinal)
            && libCMake.Contains("pkg_search_module(json-c REQUIRED json-c IMPORTED_TARGET)", StringComparison.Ordinal);
    }
}
