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
/// PP591 MADE IT, AND THE ANSWER IS DELETED. It read its oauth token from /tmp/token.txt, a path no
/// Windows machine has, on a port whose first non-goal is Windows-only; it was in no ctest case and
/// needed a console and credentials, so every build produced a binary nothing could run. The probe
/// this port keeps is the managed one - PP479 drives the PSN sequence, PP508 reaches all seven seam
/// methods, over the nine wrappers PP481 put in the shim. So the members below now assert the
/// removal rather than the file, which is what stops it arriving back unnoticed.
///
/// PP563: AND THERE IS A THIRD, which this port wrote. The shim wraps nine holepunch exports, put
/// there by PP481 so the managed side could drive the C rather than replace it. So the deletion's
/// blast radius is session.c, this harness, and the port's own seam - and the third arrived from
/// inside the same block, from a task the roadmap lists among PP33's satisfied deps.
/// </summary>
public static class HolepunchConsumers
{
    /// <summary>
    /// PP591: the harness's path is NOT a constant here any more, and that is a rule rather than a
    /// tidy-up.
    ///
    /// PP278's corpus sweeps every public string constant in this assembly and asserts that each
    /// repository path it finds is on disk - so a constant naming a file this port deliberately
    /// deleted turns that sweep red on a tree that is correct. The path lives in the test that
    /// asserts its absence, which is where PP435 put the two binaries it removed for the same
    /// reason: a deleted file is held by the check that says it is gone, not by the corpus of files
    /// something reads.
    /// </summary>
    public const string LibCMakeRelativePath = @"lib\CMakeLists.txt";

    /// <summary>Its CMakeLists, or null outside a checkout.</summary>
    public static string? LocateLibCMake() => SanitizerSource.LocateRelative(LibCMakeRelativePath);

    /// <summary>
    /// The exports the harness called. Eight, and every one of them a function PP33's deletion had
    /// to keep working or remove a caller of.
    ///
    /// PP591: kept as the list of what left with the file. It is what a returning harness would have
    /// to be measured against, and it is the size of what the deletion stopped owing.
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
    ///
    /// PP591 settled it by decision AND by removal, so this reads false now. It is kept because the
    /// question it asks is what a harness coming back would trip: a declaration alone is not a
    /// consumer, and neither is a source file nothing builds.
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
    ///
    /// PP590: AND HAS LEFT IT AGAIN, which is the first consumer this deletion has actually removed.
    /// Its whole dependency was one ask for the control port, and session.c reads the same value out
    /// of the same handle a few hundred lines earlier - so the ask was a second reading of something
    /// already known, and ctrl.c now takes what session.c recorded.
    ///
    /// PP591: and the harness went with the decision PP544 parked. TWO ARE LEFT, and they are the
    /// two that are actually the work: session.c, which is PP340's seam, and the shim, which is this
    /// port's own. Neither leaves by being read again - the four this list once held were three
    /// findings and one file nobody could run.
    ///
    /// PP632: AND SESSION.C HAS LEFT IT, which is the removal all of this was for. Its nine asks
    /// went with the `holepunch_session` field, and the Qt client's build - the only thing that ever
    /// set that field - retired in the same commit, because gui/ calls eleven of these exports
    /// directly. ONE IS LEFT, and it is the port's own seam: the nine wrappers PP481 put in the shim
    /// so the managed side could drive the C rather than replace it.
    /// </summary>
    public static IReadOnlyList<string> All { get; } = [ShimRelativePath];

    /// <summary>
    /// PP573: what PP33's own line has to say about the count, now that four tasks have moved it.
    ///
    /// Its `why` said "session.c is its only caller" while PP544, PP563 and PP564 each added one -
    /// and that sentence is the first thing a session picking the ready line reads, so the scope of
    /// the deletion was wrong at the point where somebody decides what it costs.
    ///
    /// Held as the count rather than the sentence: the line has 135 characters for its reason and
    /// will be reworded, but a line claiming ONE caller when this list holds three is the defect.
    ///
    /// PP591: the number it has to agree with is now TWO - ctrl.c stopped asking and the harness is
    /// gone. The check still refuses "only caller" by name: the shape the claim had for four shipped
    /// tasks is worth keeping out of the line even now that the count is heading back toward one.
    ///
    /// Written from <see cref="All"/> rather than as a literal, because the two move together and a
    /// hand-typed word is what let the line say "only caller" through three findings.
    /// </summary>
    public static bool TheRoadmapLineAgreesOnTheCount(string roadmapLine)
    {
        ArgumentNullException.ThrowIfNull(roadmapLine);

        // The claim that was there, in the shape it was there.
        if (roadmapLine.Contains("only caller", StringComparison.OrdinalIgnoreCase))
            return false;

        return roadmapLine.Contains(SentenceFor(All.Count), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// PP622: the clause the line has to carry, which is not a count word and a fixed plural.
    ///
    /// It was `CountWord(count) + " files call it"`, and four, three and two all read correctly.
    /// ONE DOES NOT. "one files call it" is a sentence this check demanded and no writer would
    /// write, and one is the count PP33 is heading for - the shim is the caller that survives
    /// session.c. So the gate would have turned from holding the line honest to refusing every
    /// correct spelling of it, in the commit that first made the line true.
    ///
    /// Zero has a sentence of its own rather than falling through to the plural. A line whose
    /// consumers are all gone is not making a claim about how many files call the C; it is a line
    /// about an archive, and "no file calls it" is what that says.
    ///
    /// The failure this avoids is the quiet one: the check and the line are edited in the same
    /// commit either way, and a check edited to accommodate a sentence is not the same thing as a
    /// check that knew the sentence in advance.
    /// </summary>
    public static string SentenceFor(int count) => count switch
    {
        0 => "no file calls it",
        1 => "one file calls it",
        _ => $"{CountWord(count)} files call it",
    };

    /// <summary>The count as the line spells it. Words, because that is how a sentence carries a number.</summary>
    public static string CountWord(int count) => count switch
    {
        1 => "one",
        2 => "two",
        3 => "three",
        4 => "four",
        _ => count.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    /// <summary>PP564: the fourth consumer, which only the linker found; PP33 removed it.</summary>
    public const string CtrlRelativePath = @"lib\src\ctrl.c";

    /// <summary>ctrl.c, or null outside a checkout.</summary>
    public static string? LocateCtrl() => SanitizerSource.LocateRelative(CtrlRelativePath);

    /// <summary>
    /// The one export ctrl.c used to call, and the whole of its dependency: the control port,
    /// guarded by the same handle-is-null test session.c uses, falling back to SESSION_CTRL_PORT.
    ///
    /// That guard is why this was the cheapest of the four to remove and the easiest to miss: the
    /// file already had an answer for not having a holepunch session.
    /// </summary>
    public const string CtrlCall = "chiaki_get_ps_ctrl_port";

    /// <summary>The field session.c records the answer in, which ctrl.c reads instead.</summary>
    public const string RecordedPortField = "ctrl_port";

    /// <summary>
    /// PP590: whether ctrl.c takes the recorded port and asks nobody.
    ///
    /// Both halves, and the second is the one that makes this a deletion rather than a rename. A
    /// ctrl.c that read the field AND still carried the call would satisfy a check that only looked
    /// for the field, and would still be linked against holepunch.c - which is the whole of what
    /// being a consumer means here.
    ///
    /// The fallback is asserted too. session-&gt;ctrl_port is zero on every path with no holepunch
    /// session, and a file that lost SESSION_CTRL_PORT would connect to port 0 on the direct path -
    /// which is the LAN case, and the one this port is used on most.
    /// </summary>
    public static bool CtrlReadsTheRecordedPort(string ctrlSource)
    {
        ArgumentNullException.ThrowIfNull(ctrlSource);

        return !ctrlSource.Contains(CtrlCall, StringComparison.Ordinal)
            && ctrlSource.Contains("session->" + RecordedPortField, StringComparison.Ordinal)
            && ctrlSource.Contains("SESSION_CTRL_PORT", StringComparison.Ordinal);
    }

    /// <summary>
    /// PP590: and whether session.c is what records it, from the ask it was already making.
    ///
    /// The join the change rests on. session.c reads the port in session_thread_request_session,
    /// which session_thread_func runs before chiaki_ctrl_start - so ctrl_connect reads a value
    /// written at the same instant it used to ask for one. A session.c that stopped recording would
    /// leave ctrl.c falling back to 9295 on the PSN path, silently, on hardware no test here has.
    /// </summary>
    public static bool SessionRecordsTheCtrlPort(string sessionSource)
    {
        ArgumentNullException.ThrowIfNull(sessionSource);

        return sessionSource.Contains(CtrlCall + "(session->holepunch_session)", StringComparison.Ordinal)
            && sessionSource.Contains("session->" + RecordedPortField + " =", StringComparison.Ordinal);
    }

    /// <summary>
    /// PP632: and nothing records it now, which is the other side of the same removal.
    ///
    /// The ask session.c recorded FROM was one of the nine, so PP590's arrangement has one half
    /// left: ctrl.c reads <c>session-&gt;ctrl_port</c> and falls back to SESSION_CTRL_PORT, and the
    /// field is zero on every path. That is not a regression - PP590's fallback was written for
    /// exactly the case where nobody told us, and every case is now that case.
    ///
    /// Asserted rather than assumed, because a ctrl.c that lost the fallback would connect to port
    /// zero on the LAN path, which is the path this port is used on.
    /// </summary>
    public static bool NothingRecordsTheCtrlPortAndCtrlStillFallsBack(
        string sessionSource, string ctrlSource)
    {
        ArgumentNullException.ThrowIfNull(sessionSource);
        ArgumentNullException.ThrowIfNull(ctrlSource);

        return !sessionSource.Contains(CtrlCall, StringComparison.Ordinal)
            && !sessionSource.Contains("session->" + RecordedPortField + " =", StringComparison.Ordinal)
            && CtrlReadsTheRecordedPort(ctrlSource);
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
