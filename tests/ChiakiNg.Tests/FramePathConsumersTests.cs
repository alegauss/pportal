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
    /// What session.c and the shim actually call is exactly what is modelled - each way.
    /// </summary>
    [Theory]
    [InlineData(ConsumerKind.Session)]
    [InlineData(ConsumerKind.Shim)]
    public void TheCallsAndTheRowsAgree(ConsumerKind kind)
    {
        if (Read(kind) is not { } source)
            return;

        IReadOnlyList<string> found = FramePathConsumers.CallsIn(source);
        output.WriteLine($"{kind}: {string.Join(", ", found)}");

        IReadOnlyList<string> modelled = [.. FramePathConsumers.Modelled(kind).Select(s => s.Symbol)];

        Assert.Empty(found.Except(modelled));
        Assert.Empty(modelled.Except(found));
    }

    /// <summary>
    /// The unprefixed two are among what is found: PP638's finding was that a sweep for chiaki_
    /// misses them, and a reader that missed them would agree with a model that omitted them.
    /// </summary>
    [Fact]
    public void TheUnprefixedSymbolsAreSeen()
    {
        if (Read(ConsumerKind.Session) is not { } session || Read(ConsumerKind.Shim) is not { } shim)
            return;

        Assert.Contains("stream_connection_send_idr_request", FramePathConsumers.CallsIn(session));
        Assert.Contains("create_matrix", FramePathConsumers.CallsIn(shim));
    }

    /// <summary>
    /// The four C test files are still in the suite's list - still consumers - and each has its
    /// managed class in this assembly.
    /// </summary>
    [Fact]
    public void EveryLinkedTestFileHasAManagedClass()
    {
        if (Read(ConsumerKind.Suite) is not { } cmake)
            return;

        IReadOnlyList<string> listed = FramePathConsumers.SuiteFilesIn(cmake);
        Assert.NotEmpty(listed);

        foreach (ConsumedTestFile file in FramePathConsumers.Suite)
        {
            Assert.Contains(file.File, listed);
            Assert.True(Resolve(file.Answer) is not null, $"{file.Answer.FullName} does not resolve");
        }
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
