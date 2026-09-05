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
/// The managed thing that stands where a C symbol stood.
/// </summary>
/// <param name="In">Which assembly resolves it.</param>
/// <param name="Type">The type's name inside that assembly's namespace, e.g. <c>ManagedStreamRun</c>.</param>
/// <param name="Member">A member that is the counterpart more precisely, or null where the type is.</param>
public readonly record struct Counterpart(CounterpartAssembly In, string Type, string? Member = null)
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
/// fec.c would leave undefined, and it answered with three consumers: session.c, which drives the
/// stream connection; this port's shim, which wraps the frame path so PP286 through PP291 could be
/// held against the C; and the C suite, which links four of the files through four test files. The
/// criterion says each of those must have a counterpart before the four leave - because a port that
/// answered the library's own callers alone leaves the gate red at link time.
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
/// NOT TWO-SHAPE, AND DELIBERATELY. The flip that removes the four files will put the shim's
/// wrappers inside an #ifdef, and PP662 found that text-keyed readers keep seeing what an #ifdef
/// hides. That is the right answer here: this census reads calls to say what a counterpart is owed
/// for, and a call still in the text is still owed one. The readers that must NOT keep seeing the
/// wrappers are the differentials that CALL them, and those are the next slice's, not this one's.
/// </summary>
public static class FramePathConsumers
{
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
        new("chiaki_fec_decode", new(CounterpartAssembly.App, "FecCodec")),
        new("create_matrix", new(CounterpartAssembly.App, "FecMatrix")),
        new("chiaki_frame_processor_init", new(CounterpartAssembly.App, Assembler)),
        new("chiaki_frame_processor_fini", new(CounterpartAssembly.App, Assembler)),
        new("chiaki_frame_processor_alloc_frame", new(CounterpartAssembly.App, Assembler)),
        new("chiaki_frame_processor_put_unit", new(CounterpartAssembly.App, Assembler)),
        new("chiaki_frame_processor_flush_possible", new(CounterpartAssembly.App, Assembler)),
        new("chiaki_frame_processor_flush", new(CounterpartAssembly.App, Assembler)),
        new("chiaki_video_receiver_init", new(CounterpartAssembly.App, Receiver)),
        new("chiaki_video_receiver_fini", new(CounterpartAssembly.App, Receiver)),
        new("chiaki_video_receiver_stream_info", new(CounterpartAssembly.App, Receiver, "StreamInfo")),
        new("chiaki_video_receiver_av_packet", new(CounterpartAssembly.App, Receiver, "AvPacket")),
        new("chiaki_video_receiver_get_frames_lost_total", new(CounterpartAssembly.App, Receiver)),
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
        new("fec.c", new(CounterpartAssembly.Tests, "FecCodecTests")),
        new("frameprocessor.c", new(CounterpartAssembly.Tests, "FrameAssemblerTests")),
        new("videoreceiver.c", new(CounterpartAssembly.Tests, "ManagedVideoReceiverTests")),
        new("allocbudget.c", new(CounterpartAssembly.Tests, "AllocBudgetTests")),
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
}
