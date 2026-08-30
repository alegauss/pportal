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
}
