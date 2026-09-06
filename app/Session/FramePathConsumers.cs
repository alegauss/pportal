using System.Text.RegularExpressions;
using ChiakiNg.Protocol;

namespace ChiakiNg.Session;

/// <summary>The three consumers PP638's linker run named, by kind.</summary>
public enum ConsumerKind
{
    /// <summary>session.c, which drives the stream connection.</summary>
    Session,

    /// <summary>The port's own shim, which wraps the frame path as PP286-PP291's oracles.</summary>
    Shim,

    /// <summary>The C suite, which links four of the files through four test files.</summary>
    Suite,
}

/// <summary>
/// PP758: which side of PP696's flip a consumer's own text is on.
///
/// PP670 asked this of the BUILT shim, because the flip leaves the shim's declarations in the header
/// and only the DLL can say whether they are exported. The other three consumers are the opposite
/// case: what the flip does to session.c and to the suite's list is delete text, so the text is the
/// only thing that can answer and there is nothing built to ask.
/// </summary>
public enum ConsumerShape
{
    /// <summary>Every call, or every file, is there - which is every tree before the flip.</summary>
    Asking,

    /// <summary>None of them is, which is the tree the flip leaves.</summary>
    Silent,

    /// <summary>
    /// Some but not all, which is neither shape and is what a half-finished deletion looks like.
    ///
    /// Named rather than folded into either: PP696 lands in one transaction, so a tree in this state
    /// is a commit that stopped halfway, and reporting it as Asking or Silent would hide exactly the
    /// failure the one-transaction rule exists to prevent.
    /// </summary>
    Partial,
}

/// <summary>Where a symbol's managed counterpart lives.</summary>
public enum CounterpartAssembly
{
    /// <summary>The application: <c>ChiakiNg.Protocol</c>.</summary>
    App,

    /// <summary>
    /// PP712: the application's other namespace, <c>ChiakiNg.Session</c>.
    ///
    /// The port's app assembly has two, and a counterpart may be in either - the baseline's
    /// statistics are in this one. Added rather than resolved by trying both, because a name that
    /// resolves in whichever namespace happens to have it is how a row ends up naming something
    /// plausible instead of the thing that answers.
    /// </summary>
    AppSession,

    /// <summary>The test project: <c>ChiakiNg.Tests</c>, where a C test file's counterpart is a test class.</summary>
    Tests,
}

/// <summary>
/// PP713: what a counterpart that names no member is, since three things look alike as silence.
/// </summary>
public enum CounterpartKind
{
    /// <summary>A member does what the call does, and the row names it.</summary>
    Member,

    /// <summary>
    /// The call is a constructor's, so there is no member to name.
    ///
    /// chiaki_frame_processor_init and chiaki_video_receiver_init are these: an init that takes the
    /// struct it fills is a constructor, and naming a method for one would be inventing a member.
    /// </summary>
    Constructor,

    /// <summary>
    /// The runtime removes the need, which is PP712's answer one census over.
    ///
    /// The two finis. A managed object is collected, so a row naming a method for a free would be
    /// describing C# rather than answering for a call.
    /// </summary>
    NotNeeded,

    /// <summary>
    /// The type ITSELF is the counterpart, which is what a C test file's is.
    ///
    /// The suite's four rows. A file is not a call, so there is no member it could name - and this
    /// is written down rather than left as the same silence, because leaving one legitimate reason
    /// to say nothing is how the other three got in.
    /// </summary>
    WholeType,
}

/// <summary>
/// The managed thing that stands where a C symbol stood.
/// </summary>
/// <param name="In">Which assembly resolves it.</param>
/// <param name="Type">The type's name inside that assembly's namespace, e.g. <c>ManagedStreamRun</c>.</param>
/// <param name="Member">
/// The member that is the counterpart. Null only where <paramref name="Kind"/> says which of the
/// two other things this is - PP713, because eleven rows here used to leave it null and mean three
/// different things by it.
/// </param>
/// <param name="Kind">Which of the three, defaulting to the one that names a member.</param>
public readonly record struct Counterpart(
    CounterpartAssembly In,
    string Type,
    string? Member = null,
    CounterpartKind Kind = CounterpartKind.Member)
{
    /// <summary>The namespace-qualified name, which is what reflection resolves.</summary>
    public string FullName => In switch
    {
        CounterpartAssembly.App => "ChiakiNg.Protocol." + Type,
        CounterpartAssembly.AppSession => "ChiakiNg.Session." + Type,
        _ => "ChiakiNg.Tests." + Type,
    };
}

/// <summary>One symbol a consumer calls, and what answers for it on the managed side.</summary>
/// <param name="Symbol">The C symbol as the consumer spells it - prefixed or, for two of them, not.</param>
/// <param name="Answer">Its counterpart.</param>
public readonly record struct ConsumedSymbol(string Symbol, Counterpart Answer);

/// <summary>One C test file in the suite's list, and the managed test class that holds its ground.</summary>
/// <param name="File">The file, as test/CMakeLists.txt spells it.</param>
/// <param name="Answer">The class in the test project.</param>
public readonly record struct ConsumedTestFile(string File, Counterpart Answer);

/// <summary>
/// PP669, under PP295's third criterion: every consumer PP638's linker run named has a counterpart.
///
/// PP638 asked the build what deleting streamconnection.c, videoreceiver.c, frameprocessor.c and
/// fec.c would leave undefined, and it answered with three consumers: session.c, which drove the
/// stream connection; this port's shim, which wrapped the frame path so PP286 through PP291 could
/// be held against the C; and the C suite, which linked four of the files through four test files.
/// The criterion said each of those must have a counterpart before the four left - because a port
/// that answered the library's own callers alone leaves the gate red at link time.
///
/// PP697: ALL THREE HAVE STOPPED, in PP696. session.c calls none of the five, the shim's fourteen
/// are behind an #ifdef that is off, and the suite's four files are out of its list. The rows stay
/// and so does every reader: what this census answers now is which side of that a tree is on, and
/// the counterparts it names are the ground those consumers used to hold.
///
/// THIS IS THE CRITERION AS A CHECK, in the shape the tree gives every count it has learned not to
/// type. <see cref="FecConsumers"/> is the lesson: "one caller" stayed in the prose for two ports
/// after there were three, and the one always missed was the shim. So the consumers are READ, out
/// of the files they are - the calls in session.c and in the shim, the list in test/CMakeLists.txt -
/// and each symbol found is looked up here. A symbol with no row fails by name; a row with no call
/// is a stale row and fails too, in the other direction. What this class holds is the mapping alone,
/// and the mapping is verified by reflection: a counterpart is a type that resolves and a member
/// that exists, never a sentence.
///
/// WHAT A COUNTERPART IS, per kind. For session.c it is the managed run and its host - the C's init,
/// run and stop are the run's own lifecycle, and its one unprefixed call is the IDR request that
/// PP291's outbound seam carries. For the shim it is the managed implementation each wrapper was
/// the oracle FOR: the wrappers exist so a differential could run, and the thing on the other side
/// of the differential is what stays. For the suite it is the managed test class that asserts the
/// same ground. None of this deletes anything - that is the fourth criterion, and PP639 made it a
/// rule rather than a step - but it is what the deletion is measured against.
///
/// THE SHIM IS NOT TWO-SHAPE, AND DELIBERATELY. The flip that removes the four files will put the
/// shim's wrappers inside an #ifdef, and PP662 found that text-keyed readers keep seeing what an
/// #ifdef hides. That is the right answer for this one: the census reads calls to say what a
/// counterpart is owed for, and a call still in the text is still owed one. The readers that must
/// NOT keep seeing the wrappers are the differentials that CALL them, and PP670 taught those.
///
/// PP758: THE OTHER TWO ARE, because the flip deletes their text rather than guarding it. session.c
/// stops calling and the suite's list stops naming four files, so a check written for one shape goes
/// red in the commit that does it - and PP623's whole discipline is that the commit editing the C
/// may not edit a test file. <see cref="ShapeOf"/> is that question, and <see cref="WasActuallyRead"/>
/// is what keeps the silent answer from being one an unreadable file could give.
/// </summary>
public static class FramePathConsumers
{
    /// <summary>
    /// PP734: the symbols whose counterpart is a seam nothing in app fills yet.
    ///
    /// PP713 made every row say what KIND of counterpart it names. This is the question that
    /// exposed: whether the thing named is reached. Two rows name an interface member, and only one
    /// of them is filled - StreamOutbound implements IVideoReceiverOutbound in app, and the only
    /// implementations of IStreamRunHost are doubles in the test project.
    ///
    /// THE CHECK COULD NOT SEE THE DIFFERENCE, because both resolve and both name a member that
    /// exists. So the census reported the same confidence about a call answered by shipping code
    /// and a call answered by a shape - and PP669's own rule is that a mapping is not a call.
    ///
    /// WHICH MATTERS BECAUSE THIS CENSUS IS A CRITERION. PP295's third is what the deletion of four
    /// C files is measured against, so a promise that something COULD answer is being read as
    /// something that DOES.
    ///
    /// NOT A FAILURE, A VERDICT. A seam with no implementation is honest while somebody's open work
    /// is to fill it - which for this one was PP707's first criterion. What must not happen is the
    /// census staying quiet about which rows they are, so this list is asserted in both directions:
    /// a row arriving here is news, and a row leaving it is the day the seam got filled.
    ///
    /// PP745 WAS THAT DAY. ManagedStreamRunHost implements IStreamRunHost in app, so the row this
    /// list was written for left it. Kept rather than deleted with its last entry, for the reason
    /// the paragraph above gives: what would report a counterpart going back to being a shape is
    /// this list, and its absence would report nothing at all.
    /// </summary>
    public static IReadOnlyList<string> SeamOnly { get; } = [];

    /// <summary>The four files the criterion is about, which no consumer below may be one of.</summary>
    public static IReadOnlyList<string> Leaving { get; } =
    [
        @"lib\src\streamconnection.c",
        @"lib\src\videoreceiver.c",
        @"lib\src\frameprocessor.c",
        @"lib\src\fec.c",
    ];

    /// <summary>session.c, which drives the stream connection.</summary>
    public const string SessionRelativePath = @"lib\src\session.c";

    /// <summary>The shim's body, where the wrappers call what they wrap.</summary>
    public const string ShimRelativePath = @"shim\chiaki_shim.c";

    /// <summary>The suite's list, which is where a C test file is a consumer or is not.</summary>
    public const string SuiteListRelativePath = @"test\CMakeLists.txt";

    /// <summary>
    /// What session.c calls, and what answers.
    ///
    /// Five calls today, one of them unprefixed - stream_connection_send_idr_request is exported
    /// without the chiaki_ that every other entry point carries, and PP638's linker run is what
    /// noticed. The stop is a flag on the managed side because that is what the C's stop is: it
    /// sets should_stop and signals, and the run reads it at the next wait.
    /// </summary>
    public static IReadOnlyList<ConsumedSymbol> Session { get; } =
    [
        new("chiaki_stream_connection_init", new(CounterpartAssembly.App, "ManagedStreamRun", "Run")),
        new("chiaki_stream_connection_fini", new(CounterpartAssembly.App, "StreamTeardown", "From")),
        new("chiaki_stream_connection_stop", new(CounterpartAssembly.App, "IStreamRunHost", "ShouldStop")),
        new("stream_connection_send_idr_request", new(CounterpartAssembly.App, "IVideoReceiverOutbound", "SendIdrRequest")),
        new("chiaki_stream_connection_run", new(CounterpartAssembly.App, "ManagedStreamRun", "Run")),
    ];

    /// <summary>
    /// What the shim calls, and what answers.
    ///
    /// Each wrapper was written as an oracle for one managed port, so its counterpart is that port.
    /// create_matrix is jerasure's and not chiaki's - the one symbol in the seventeen with no
    /// chiaki_ prefix at all - and the shim forward-declares it itself, which is why the reader
    /// below has to tell a declaration from a call.
    /// </summary>
    public static IReadOnlyList<ConsumedSymbol> Shim { get; } =
    [
        // PP713: eleven of these named a type and nothing else, and meant three different things by
        // it. Each now says which - the member that answers, a constructor, or a need the runtime
        // removes - because PP712 asked the same question of the run-host census and three of the
        // four rows that had taken the option were wrong.
        new("chiaki_fec_decode", new(CounterpartAssembly.App, "FecCodec", "Decode")),
        new("create_matrix", new(CounterpartAssembly.App, "FecMatrix", "Cauchy")),
        new(
            "chiaki_frame_processor_init",
            new(CounterpartAssembly.App, Assembler, null, CounterpartKind.Constructor)),
        new(
            "chiaki_frame_processor_fini",
            new(CounterpartAssembly.App, Assembler, null, CounterpartKind.NotNeeded)),
        new("chiaki_frame_processor_alloc_frame", new(CounterpartAssembly.App, Assembler, "AllocFrame")),
        new("chiaki_frame_processor_put_unit", new(CounterpartAssembly.App, Assembler, "PutUnit")),
        new("chiaki_frame_processor_flush_possible", new(CounterpartAssembly.App, Assembler, "FlushPossible")),
        new("chiaki_frame_processor_flush", new(CounterpartAssembly.App, Assembler, "Flush")),
        new(
            "chiaki_video_receiver_init",
            new(CounterpartAssembly.App, Receiver, null, CounterpartKind.Constructor)),
        new(
            "chiaki_video_receiver_fini",
            new(CounterpartAssembly.App, Receiver, null, CounterpartKind.NotNeeded)),
        new("chiaki_video_receiver_stream_info", new(CounterpartAssembly.App, Receiver, "StreamInfo")),
        new("chiaki_video_receiver_av_packet", new(CounterpartAssembly.App, Receiver, "AvPacket")),
        new(
            "chiaki_video_receiver_get_frames_lost_total",
            new(CounterpartAssembly.App, Receiver, "FramesLostTotal")),
    ];

    /// <summary>PP290's frame assembler, which the six frame-processor wrappers were the oracle for.</summary>
    private const string Assembler = "FrameAssembler";

    /// <summary>PP291's receiver, which the five video-receiver wrappers were the oracle for.</summary>
    private const string Receiver = "ManagedVideoReceiver";

    /// <summary>
    /// The four C test files that link the four library files, and the managed class for each.
    ///
    /// allocbudget.c is the one that is not obviously a frame-path test: it wraps malloc to count
    /// the frame processor's allocations per packet (PP44), and links the four files for that.
    /// </summary>
    public static IReadOnlyList<ConsumedTestFile> Suite { get; } =
    [
        // PP713: a file's counterpart is a CLASS and not a member, and each of these says so rather
        // than leaving the same null the symbol rows above used to leave for three other reasons.
        new("fec.c", new(CounterpartAssembly.Tests, "FecCodecTests", null, CounterpartKind.WholeType)),
        new(
            "frameprocessor.c",
            new(CounterpartAssembly.Tests, "FrameAssemblerTests", null, CounterpartKind.WholeType)),
        new(
            "videoreceiver.c",
            new(CounterpartAssembly.Tests, "ManagedVideoReceiverTests", null, CounterpartKind.WholeType)),
        new(
            "allocbudget.c",
            new(CounterpartAssembly.Tests, "AllocBudgetTests", null, CounterpartKind.WholeType)),
    ];

    /// <summary>A file, or null outside a checkout.</summary>
    public static string? Locate(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        return SanitizerSource.LocateRelative(relativePath);
    }

    /// <summary>
    /// The symbols of the four files that a C source calls, distinct and in first-call order.
    ///
    /// The shape of a symbol is narrow on purpose: the four files' own prefixes, the two unprefixed
    /// stream_connection_send_ exports, and jerasure's create_matrix. Comments are stripped first,
    /// and a line that declares rather than calls - the shim's <c>extern int *create_matrix(...)</c>
    /// - is not a call, which is the same distinction <see cref="FecConsumers.Declares"/> draws.
    /// </summary>
    public static IReadOnlyList<string> CallsIn(string cSource)
    {
        ArgumentNullException.ThrowIfNull(cSource);

        var found = new List<string>();
        foreach (string line in CCall.Code(cSource).Split('\n'))
        {
            if (line.Contains("extern ", StringComparison.Ordinal))
                continue;

            foreach (Match match in SymbolCall.Matches(line))
            {
                string symbol = match.Groups["symbol"].Value;
                if (!found.Contains(symbol))
                    found.Add(symbol);
            }
        }

        return found;
    }

    private static readonly Regex SymbolCall = new(
        @"\b(?<symbol>chiaki_(?:stream_connection|video_receiver|frame_processor|fec)_\w+|stream_connection_send_\w+|create_matrix)\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// The files test/CMakeLists.txt lists for chiaki-unit, as it spells them.
    ///
    /// The <c>set(CHIAKI_UNIT_SOURCES ...)</c> block alone - a file appended under an option later
    /// in the same list is conditional, and a conditional consumer is a different fact.
    /// </summary>
    public static IReadOnlyList<string> SuiteFilesIn(string cmake)
    {
        ArgumentNullException.ThrowIfNull(cmake);

        Match block = SuiteBlock.Match(cmake);
        if (!block.Success)
            return [];

        return [.. block.Groups["files"].Value
            .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)];
    }

    private static readonly Regex SuiteBlock = new(
        @"set\(CHIAKI_UNIT_SOURCES(?<files>[^)]*)\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// The one file a consumer of a kind is read from, or the suite's list.
    /// </summary>
    public static string RelativePathOf(ConsumerKind kind) => kind switch
    {
        ConsumerKind.Session => SessionRelativePath,
        ConsumerKind.Shim => ShimRelativePath,
        ConsumerKind.Suite => SuiteListRelativePath,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>The symbols a kind's consumer is modelled as calling.</summary>
    public static IReadOnlyList<ConsumedSymbol> Modelled(ConsumerKind kind) => kind switch
    {
        ConsumerKind.Session => Session,
        ConsumerKind.Shim => Shim,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), "the suite is modelled by file, not by symbol"),
    };

    /// <summary>
    /// PP758: how many of a consumer's modelled rows its text still answers for.
    ///
    /// Counted against the MODEL rather than against everything found, because the two directions
    /// are different questions: this one is how far through the flip the file is, and a call with no
    /// row at all is the stale-model direction that is asked separately and on either shape.
    /// </summary>
    public static int StillAskedIn(ConsumerKind kind, string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (kind == ConsumerKind.Suite)
        {
            IReadOnlyList<string> listed = SuiteFilesIn(text);
            return Suite.Count(one => listed.Contains(one.File, StringComparer.Ordinal));
        }

        IReadOnlyList<string> called = CallsIn(text);
        return Modelled(kind).Count(row => called.Contains(row.Symbol, StringComparer.Ordinal));
    }

    /// <summary>How many rows a kind is modelled with, which is what a full count is measured against.</summary>
    public static int ModelledCount(ConsumerKind kind)
        => kind == ConsumerKind.Suite ? Suite.Count : Modelled(kind).Count;

    /// <summary>Which shape a consumer's text is in.</summary>
    public static ConsumerShape ShapeOf(ConsumerKind kind, string text)
    {
        int found = StillAskedIn(kind, text);

        if (found == 0)
            return ConsumerShape.Silent;

        return found == ModelledCount(kind) ? ConsumerShape.Asking : ConsumerShape.Partial;
    }

    /// <summary>
    /// PP758: what a consumer's file still holds after the flip, so silence can be told from a file
    /// nobody managed to read.
    ///
    /// A check that only says the calls are gone is satisfied by an empty string, a path that
    /// resolved to the wrong file, and a checkout that is not there - which is how PP749's first
    /// drift check passed while asserting nothing. The Silent side has to name something that STAYS.
    ///
    /// Not the four files' own symbols, obviously, and not something incidental either: these are
    /// exports and files whose removal would be a different task with its own line.
    /// </summary>
    public static IReadOnlyList<string> SurvivesTheFlip(ConsumerKind kind) => kind switch
    {
        ConsumerKind.Session => ["chiaki_session_start", "chiaki_session_stop", "chiaki_session_join"],
        ConsumerKind.Shim => ["chiaki_shim_has_framepath"],
        ConsumerKind.Suite => ["main.c", "http.c", "takion.c"],
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>
    /// PP758: the shape session.c is in on this tree, or Asking outside a checkout.
    ///
    /// Asked once and in app, rather than five times in the five test classes that need it. Five
    /// copies of "read the file and decide" is five chances for one of them to answer differently on
    /// the day it matters, and the day it matters is the single commit that changes the answer.
    ///
    /// Asking is the answer with no file, because that is the shape every published tree is in until
    /// PP696 lands - and a reader outside a checkout that guessed Silent would skip the half of each
    /// check that is doing the work today.
    /// </summary>
    public static ConsumerShape SessionShape()
        => Locate(SessionRelativePath) is { } path
            ? ShapeOf(ConsumerKind.Session, File.ReadAllText(path))
            : ConsumerShape.Asking;

    /// <summary>And the suite's list, the same way.</summary>
    public static ConsumerShape SuiteShape()
        => Locate(SuiteListRelativePath) is { } path
            ? ShapeOf(ConsumerKind.Suite, File.ReadAllText(path))
            : ConsumerShape.Asking;

    /// <summary>Whether the text really is the consumer's, by what survives the flip in it.</summary>
    public static bool WasActuallyRead(ConsumerKind kind, string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (kind != ConsumerKind.Suite)
            return SurvivesTheFlip(kind).All(one => text.Contains(one, StringComparison.Ordinal));

        IReadOnlyList<string> listed = SuiteFilesIn(text);
        return SurvivesTheFlip(kind).All(one => listed.Contains(one, StringComparer.Ordinal));
    }
}
