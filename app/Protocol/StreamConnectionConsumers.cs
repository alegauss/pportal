using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP638: who PP295's deletion has to answer for, asked of the linker.
///
/// §PP295 says "lib has one caller and this port's own seam is the other". That is true of the VIDEO
/// RECEIVER and says nothing about who calls streamconnection.c itself - and the deletion needs
/// both, because the file leaving is what lets videoreceiver.c, frameprocessor.c and fec.c leave.
///
/// TAKEN THE WAY PP565 TOOK PP33'S: the four files out of the source list, and the build asked.
/// Seventeen symbols across three kinds of consumer, and one of them was not written down anywhere.
///
/// SESSION.C WAS THAT ONE. Six references - init, run, stop, fini twice, and the IDR request - so
/// streamconnection.c could not leave until session.c stopped driving the stream, and session.c is
/// PP28's subject. The same shape PP564 found for ctrl.c: a consumer three readings missed and a
/// linker named in thirty seconds.
///
/// PP697: IT HAS STOPPED. PP696 replaced the run with an installed callback and dropped the other
/// four references, and the four files left lib's source list in the same commit. The census is
/// kept and not deleted with the work it measured: what it reads is which side of that the tree is
/// on, and a file coming back is exactly what it would be needed to notice.
/// </summary>
public static class StreamConnectionConsumers
{
    /// <summary>The four files the measurement took out together.</summary>
    public static IReadOnlyList<string> Measured { get; } =
    [
        @"lib\src\streamconnection.c",
        @"lib\src\videoreceiver.c",
        @"lib\src\frameprocessor.c",
        @"lib\src\fec.c",
    ];

    /// <summary>The name lib's list spells each of them with, which is a relative path with slashes.</summary>
    public static IReadOnlyList<string> MeasuredAsTheListSpellsThem { get; } =
        [.. Measured.Select(one => one.Replace(@"lib\", string.Empty, StringComparison.Ordinal).Replace('\\', '/'))];

    /// <summary>
    /// PP761: which side of PP696's flip lib's own source list is on.
    ///
    /// PP565's rule is that a recorded result is about a TREE, so a file that left on its own would
    /// leave seventeen symbols measured against something else. PP696 is the four leaving TOGETHER,
    /// which is that rule satisfied rather than broken - and one leaving alone still is not.
    ///
    /// The three answers are the same three <see cref="ConsumerShape"/> gives everywhere else, and
    /// Partial is the one that matters here: it is exactly "a file left on its own".
    /// </summary>
    public static ConsumerShape ShapeOfTheList(string cmake)
    {
        ArgumentNullException.ThrowIfNull(cmake);

        int found = MeasuredAsTheListSpellsThem.Count(
            one => cmake.Contains(one, StringComparison.Ordinal));

        if (found == 0)
            return ConsumerShape.Silent;

        return found == Measured.Count ? ConsumerShape.Asking : ConsumerShape.Partial;
    }

    /// <summary>
    /// What lib's list still holds once the four have gone, so silence can be told from a file
    /// nobody read.
    ///
    /// takion.c and session.c: the first is what streamconnection.c sat beside and the second is the
    /// caller PP638 found. Neither leaves with this deletion, and a list naming neither is not lib's.
    /// </summary>
    public static IReadOnlyList<string> SurvivesTheFlip { get; } = ["src/takion.c", "src/session.c"];

    /// <summary>Whether the text really is lib's list, by what survives the flip in it.</summary>
    public static bool TheListWasActuallyRead(string cmake)
    {
        ArgumentNullException.ThrowIfNull(cmake);

        return SurvivesTheFlip.All(one => cmake.Contains(one, StringComparison.Ordinal));
    }

    /// <summary>lib's own caller of streamconnection, which §PP295 does not name.</summary>
    public const string SessionRelativePath = @"lib\src\session.c";

    /// <summary>session.c, or null outside a checkout.</summary>
    public static string? LocateSession() => SanitizerSource.LocateRelative(SessionRelativePath);

    /// <summary>
    /// What session.c reaches for, as the linker named them.
    ///
    /// `fini` appears twice in the object and once here: this is the surface, not the call count -
    /// what a deletion has to answer for is which entry points, and the second teardown site is
    /// session.c's error path.
    /// </summary>
    public static IReadOnlyList<string> SessionCalls { get; } =
    [
        "chiaki_stream_connection_init",
        "chiaki_stream_connection_run",
        "chiaki_stream_connection_stop",
        "chiaki_stream_connection_fini",
        UnprefixedExport,
    ];

    /// <summary>
    /// PP564's trap, a second time in the same block.
    ///
    /// `stream_connection_send_idr_request` carries no `chiaki_` in front of it and is exported all
    /// the same, so a sweep keyed on that prefix - which is how a reader finds these - walks past
    /// it. The first instance was `holepunch_session_create_offer`, and finding it cost PP564 a
    /// linker run; this one would have cost the same.
    /// </summary>
    public const string UnprefixedExport = "stream_connection_send_idr_request";

    /// <summary>
    /// The C suite's own files that link what the library would stop building.
    ///
    /// Not a defect and worth naming: PP591's harness was a target nobody could run, and these are
    /// four that run on every gate. A deletion that forgot them turns the suite red at link time,
    /// which is loud - but it is loud in a commit that thought it was finished.
    /// </summary>
    public static IReadOnlyList<string> SuiteFiles { get; } =
    [
        @"test\fec.c",
        @"test\frameprocessor.c",
        @"test\videoreceiver.c",
        @"test\allocbudget.c",
    ];

    /// <summary>The shim, which PP584 says a deletion line must name and this one now does.</summary>
    public const string ShimRelativePath = @"shim\chiaki_shim.c";

    /// <summary>The shim, or null outside a checkout.</summary>
    public static string? LocateShim() => SanitizerSource.LocateRelative(ShimRelativePath);

    /// <summary>
    /// How many symbols the shim reaches across the four files, including one in jerasure directly.
    ///
    /// `create_matrix` is not libchiaki's at all - it is the Cauchy matrix builder the FEC decode
    /// sits on - so the shim reaches past the file being deleted into what that file links.
    /// </summary>
    public const int ShimSymbols = 12;

    /// <summary>Whatever session.c still names of the surface.</summary>
    public static IReadOnlyList<string> StillCalledBySession(string sessionSource)
    {
        ArgumentNullException.ThrowIfNull(sessionSource);

        return
        [
            .. SessionCalls.Where(call =>
                sessionSource.Contains(call + "(", StringComparison.Ordinal))
        ];
    }
}
