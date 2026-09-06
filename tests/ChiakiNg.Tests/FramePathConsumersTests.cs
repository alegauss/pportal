using System.Reflection;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP669, under PP295: the third criterion as a check - every consumer PP638 named has a counterpart.
///
/// The consumers are read out of their files and each symbol found is resolved here, by reflection,
/// to a type that exists and a member that exists. Both directions: a call with no row fails by
/// name, and a row with no call is stale and fails by name too. Nothing is counted - PP638's six,
/// twelve and four are the ledger's and this asserts the sets.
/// </summary>
public class FramePathConsumersTests(ITestOutputHelper output)
{
    private static readonly Assembly App = typeof(ManagedStreamRun).Assembly;
    private static readonly Assembly Tests = typeof(FramePathConsumersTests).Assembly;

    private static Type? Resolve(Counterpart counterpart)
        => (counterpart.In == CounterpartAssembly.App ? App : Tests).GetType(counterpart.FullName);

    private static string? Read(ConsumerKind kind)
        => FramePathConsumers.Locate(FramePathConsumers.RelativePathOf(kind)) is { } path
            ? File.ReadAllText(path)
            : null;

    /// <summary>
    /// Every counterpart the model names resolves, and the member it names exists on it.
    ///
    /// This is the half that runs outside a checkout as well: the mapping is a claim about this
    /// assembly, and a counterpart renamed away fails here before any file is read.
    ///
    /// PP713: AND EVERY ROW SAYS WHICH KIND IT IS. Eleven of the symbol rows named a type and
    /// nothing else, and meant three different things by it - a member nobody had looked up, a
    /// constructor, and a free the runtime makes needless. Silence cannot be judged, so it is no
    /// longer allowed: a row naming no member has to say which of the reasons that is.
    /// </summary>
    [Fact]
    public void EveryCounterpartResolvesAndSaysWhatKindItIs()
    {
        IEnumerable<Counterpart> all = FramePathConsumers.Session.Select(s => s.Answer)
            .Concat(FramePathConsumers.Shim.Select(s => s.Answer))
            .Concat(FramePathConsumers.Suite.Select(s => s.Answer));

        foreach (Counterpart counterpart in all.Distinct())
        {
            Type? type = Resolve(counterpart);
            Assert.True(type is not null, $"{counterpart.FullName} does not resolve");

            if (counterpart.Kind == CounterpartKind.Member)
            {
                string member = Assert.IsType<string>(counterpart.Member);

                Assert.True(
                    type.GetMember(member).Length > 0,
                    $"{counterpart.FullName} has no member {member}");

                continue;
            }

            // The three that name none say so, and none of them may name one anyway - a row that
            // did would be two answers about one call.
            Assert.True(
                counterpart.Member is null,
                $"{counterpart.FullName} is {counterpart.Kind} and names {counterpart.Member}");

            if (counterpart.Kind == CounterpartKind.Constructor)
                Assert.NotEmpty(type.GetConstructors());
        }
    }

    /// <summary>
    /// PP713: the eleven that were silent, and what each of them turned out to be.
    ///
    /// The answer this task produced, as a count rather than a re-reading. Seven had a member all
    /// along - FecCodec.Decode and ManagedVideoReceiver.FramesLostTotal among them, which are calls
    /// that DO something - two are constructors, and two are finis the runtime makes needless.
    ///
    /// Asserted as the split so that a row moving between the three is a decision somebody takes
    /// rather than a default nobody notices.
    /// </summary>
    [Fact]
    public void TheShimsRowsAreSevenMembersTwoConstructorsAndTwoNeedless()
    {
        Counterpart[] answers = [.. FramePathConsumers.Shim.Select(one => one.Answer)];

        int members = answers.Count(one => one.Kind == CounterpartKind.Member);
        int constructors = answers.Count(one => one.Kind == CounterpartKind.Constructor);
        int needless = answers.Count(one => one.Kind == CounterpartKind.NotNeeded);

        output.WriteLine($"{members} member(s), {constructors} ctor(s), {needless} needless");

        Assert.Equal(9, members);
        Assert.Equal(2, constructors);
        Assert.Equal(2, needless);

        // The two PP713 named as calls that do something, now naming the member that does it.
        Assert.Contains(answers, one => one.Type == "FecCodec" && one.Member == "Decode");
        Assert.Contains(
            answers, one => one.Member == nameof(ChiakiNg.Protocol.ManagedVideoReceiver.FramesLostTotal));
    }

    /// <summary>And a file's counterpart is the class itself, which is its own answer.</summary>
    [Fact]
    public void EverySuiteRowNamesAWholeType()
        => Assert.All(
            FramePathConsumers.Suite,
            one => Assert.Equal(CounterpartKind.WholeType, one.Answer.Kind));

    /// <summary>
    /// PP734: which rows are answered by a seam nothing in app fills, computed rather than claimed.
    ///
    /// The census is PP295's third criterion, and PP669's rule is that a mapping is not a call. An
    /// interface member with no implementation outside the tests is a mapping one level further
    /// out: a promise that something COULD answer, read as something that does.
    ///
    /// BOTH DIRECTIONS, as every list here is. A row arriving is a counterpart that was shipping
    /// code and has become a shape; a row leaving is the day somebody filled the seam - which for
    /// this one is PP707's first criterion, and is the commit where this list should shorten.
    /// </summary>
    [Fact]
    public void TheRowsAnsweredOnlyByASeamAreTheseAndNoOthers()
    {
        string[] found =
        [
            .. FramePathConsumers.Session
                .Concat(FramePathConsumers.Shim)
                .Where(one => IsSeamOnly(one.Answer))
                .Select(one => one.Symbol)
                .Order(StringComparer.Ordinal),
        ];

        output.WriteLine(found.Length == 0 ? "none" : string.Join(", ", found));

        Assert.Equal(FramePathConsumers.SeamOnly, found);

        // PP271: a reader that called nothing a seam would satisfy an empty list. The other
        // interface row is filled, and saying so is what proves the question is being asked.
        Assert.Contains(
            FramePathConsumers.Session.Concat(FramePathConsumers.Shim),
            one => one.Answer.Type == nameof(IVideoReceiverOutbound) && !IsSeamOnly(one.Answer));
    }

    /// <summary>
    /// Whether a counterpart is an interface no class in app implements.
    ///
    /// A class answers for itself. An interface is answered by whatever implements it, and a test
    /// double is not an answer - it is the thing PP669 wrote this census to tell apart from one.
    /// </summary>
    private static bool IsSeamOnly(Counterpart counterpart)
    {
        Type? type = Resolve(counterpart);

        return type is { IsInterface: true }
            && !App.GetTypes().Any(one => one.IsClass && type.IsAssignableFrom(one));
    }

    /// <summary>
    /// What session.c and the shim actually call is exactly what is modelled - each way.
    ///
    /// PP758: AND ON WHICHEVER SIDE OF THE FLIP THE TREE IS. session.c's five calls are text PP696
    /// deletes, so "every row has a call" is an assertion about one shape of the tree, and the commit
    /// that changes the shape is the one commit forbidden from editing a test. The stale-model
    /// direction is asked on both sides - a call with no row is wrong whichever shape it turns up in.
    /// </summary>
    [Theory]
    [InlineData(ConsumerKind.Session)]
    [InlineData(ConsumerKind.Shim)]
    public void TheCallsAndTheRowsAgree(ConsumerKind kind)
    {
        if (Read(kind) is not { } source)
            return;

        IReadOnlyList<string> found = FramePathConsumers.CallsIn(source);
        IReadOnlyList<string> modelled = [.. FramePathConsumers.Modelled(kind).Select(s => s.Symbol)];

        ConsumerShape shape = FramePathConsumers.ShapeOf(kind, source);
        output.WriteLine($"{kind} is {shape}: {string.Join(", ", found)}");

        // Either way round: a symbol called with no row is a model that fell behind its consumer.
        Assert.Empty(found.Except(modelled));

        if (shape == ConsumerShape.Asking)
        {
            Assert.Empty(modelled.Except(found));
            return;
        }

        // And the silent side, which is the tree PP696 leaves. Nothing is called - and the file was
        // read, which an empty string or a path resolving elsewhere would otherwise satisfy.
        Assert.Equal(ConsumerShape.Silent, shape);
        Assert.Empty(found);
        Assert.True(
            FramePathConsumers.WasActuallyRead(kind, source),
            $"{kind} calls none of them, and holds none of what survives the flip either");
    }

    /// <summary>
    /// The unprefixed two are among what is found: PP638's finding was that a sweep for chiaki_
    /// misses them, and a reader that missed them would agree with a model that omitted them.
    ///
    /// PP758: the session half is asked only while session.c is still asking. The shim's stays
    /// unconditional - create_matrix goes inside an #ifdef and stays in the text, which is the whole
    /// reason this census is not two-shape for that consumer.
    /// </summary>
    [Fact]
    public void TheUnprefixedSymbolsAreSeen()
    {
        if (Read(ConsumerKind.Session) is not { } session || Read(ConsumerKind.Shim) is not { } shim)
            return;

        Assert.Contains("create_matrix", FramePathConsumers.CallsIn(shim));

        if (FramePathConsumers.ShapeOf(ConsumerKind.Session, session) == ConsumerShape.Asking)
            Assert.Contains("stream_connection_send_idr_request", FramePathConsumers.CallsIn(session));
        else
            Assert.DoesNotContain("stream_connection_send_idr_request", FramePathConsumers.CallsIn(session));
    }

    /// <summary>
    /// The four C test files are in the suite's list - still consumers - and each has its managed
    /// class in this assembly.
    ///
    /// PP758: the CLASS half is asserted on both shapes and the LISTING half only on one. That split
    /// is the point of the census: the managed counterpart is what has to exist after the C file
    /// goes, so a flip that took the four files out and let their classes rot would pass a check
    /// that stopped asking anything once the list was empty.
    /// </summary>
    [Fact]
    public void EveryLinkedTestFileHasAManagedClass()
    {
        if (Read(ConsumerKind.Suite) is not { } cmake)
            return;

        IReadOnlyList<string> listed = FramePathConsumers.SuiteFilesIn(cmake);
        Assert.NotEmpty(listed);

        ConsumerShape shape = FramePathConsumers.ShapeOf(ConsumerKind.Suite, cmake);
        output.WriteLine($"suite is {shape}: {string.Join(", ", listed)}");

        foreach (ConsumedTestFile file in FramePathConsumers.Suite)
        {
            if (shape == ConsumerShape.Asking)
                Assert.Contains(file.File, listed);
            else
                Assert.DoesNotContain(file.File, listed);

            Assert.True(Resolve(file.Answer) is not null, $"{file.Answer.FullName} does not resolve");
        }

        Assert.NotEqual(ConsumerShape.Partial, shape);
        Assert.True(
            FramePathConsumers.WasActuallyRead(ConsumerKind.Suite, cmake),
            "the list names none of the files that stay, so it is not the suite's list");
    }

    /// <summary>
    /// PP758: THE SHAPE READER ITSELF, on texts rather than on whichever tree this runs against.
    ///
    /// The two-shape checks above can only ever exercise one side, and the side they exercise is the
    /// one that already worked. So the reader is asked both questions here directly - which is what
    /// makes the silent branch something that was tested rather than something that was written.
    /// </summary>
    [Fact]
    public void TheShapeReaderTellsTheThreeStatesApart()
    {
        const string Asking = """
            	err = chiaki_stream_connection_init(&session->stream_connection, session, max);
            	chiaki_stream_connection_fini(&session->stream_connection);
            	chiaki_stream_connection_stop(&session->stream_connection);
            	return stream_connection_send_idr_request(&session->stream_connection);
            	err = chiaki_stream_connection_run(&session->stream_connection, data_sock);
            	chiaki_session_start(); chiaki_session_stop(); chiaki_session_join();
            """;

        Assert.Equal(ConsumerShape.Asking, FramePathConsumers.ShapeOf(ConsumerKind.Session, Asking));

        // One call left behind is a transaction that stopped halfway, and says so rather than
        // rounding to either side.
        const string Halfway = "\tchiaki_stream_connection_stop(&session->stream_connection);";
        Assert.Equal(ConsumerShape.Partial, FramePathConsumers.ShapeOf(ConsumerKind.Session, Halfway));

        const string Silent = """
            	chiaki_session_start(); chiaki_session_stop(); chiaki_session_join();
            """;

        Assert.Equal(ConsumerShape.Silent, FramePathConsumers.ShapeOf(ConsumerKind.Session, Silent));
        Assert.True(FramePathConsumers.WasActuallyRead(ConsumerKind.Session, Silent));

        // And silence alone is not the silent shape: an empty file calls nothing either.
        Assert.Equal(ConsumerShape.Silent, FramePathConsumers.ShapeOf(ConsumerKind.Session, ""));
        Assert.False(FramePathConsumers.WasActuallyRead(ConsumerKind.Session, ""));
    }

    /// <summary>And the same three, for the suite's list, which is read as files rather than calls.</summary>
    [Fact]
    public void TheShapeReaderTellsThemApartForTheSuitesList()
    {
        const string Stays = "main.c\n\thttp.c\n\ttakion.c\n\tseqnum.c";

        string asking = $"set(CHIAKI_UNIT_SOURCES\n\t{Stays}\n\tfec.c\n\tframeprocessor.c"
            + "\n\tallocbudget.c\n\tvideoreceiver.c)";
        Assert.Equal(ConsumerShape.Asking, FramePathConsumers.ShapeOf(ConsumerKind.Suite, asking));

        string halfway = $"set(CHIAKI_UNIT_SOURCES\n\t{Stays}\n\tfec.c)";
        Assert.Equal(ConsumerShape.Partial, FramePathConsumers.ShapeOf(ConsumerKind.Suite, halfway));

        string silent = $"set(CHIAKI_UNIT_SOURCES\n\t{Stays})";
        Assert.Equal(ConsumerShape.Silent, FramePathConsumers.ShapeOf(ConsumerKind.Suite, silent));
        Assert.True(FramePathConsumers.WasActuallyRead(ConsumerKind.Suite, silent));

        // A list that lost everything is not the flip: it is a file that could not be parsed.
        Assert.Equal(ConsumerShape.Silent, FramePathConsumers.ShapeOf(ConsumerKind.Suite, ""));
        Assert.False(FramePathConsumers.WasActuallyRead(ConsumerKind.Suite, ""));
    }

    /// <summary>No consumer is one of the four files themselves, which would make the census circular.</summary>
    [Fact]
    public void NoConsumerIsAFileThatLeaves()
    {
        foreach (ConsumerKind kind in Enum.GetValues<ConsumerKind>())
            Assert.DoesNotContain(FramePathConsumers.RelativePathOf(kind), FramePathConsumers.Leaving);
    }

    /// <summary>The readers see what they are for: a call, not a declaration, and not a comment.</summary>
    [Fact]
    public void TheReadersTellACallFromADeclaration()
    {
        Assert.Empty(FramePathConsumers.CallsIn(""));
        Assert.Empty(FramePathConsumers.CallsIn("extern int *create_matrix(unsigned int k, unsigned int m);"));
        Assert.Empty(FramePathConsumers.CallsIn("/* chiaki_fec_decode(frame, 1) */"));

        Assert.Equal(["create_matrix"], FramePathConsumers.CallsIn("\tmatrix = create_matrix(k, m);"));
        Assert.Equal(
            ["chiaki_fec_decode"],
            FramePathConsumers.CallsIn("\treturn chiaki_fec_decode(a, b);\n\tchiaki_fec_decode(c, d);"));

        Assert.Empty(FramePathConsumers.SuiteFilesIn(""));
        Assert.Equal(["main.c", "fec.c"], FramePathConsumers.SuiteFilesIn("set(CHIAKI_UNIT_SOURCES\n\tmain.c\n\tfec.c)"));
    }
}
