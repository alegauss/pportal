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
    /// </summary>
    [Fact]
    public void EveryCounterpartResolves()
    {
        IEnumerable<Counterpart> all = FramePathConsumers.Session.Select(s => s.Answer)
            .Concat(FramePathConsumers.Shim.Select(s => s.Answer))
            .Concat(FramePathConsumers.Suite.Select(s => s.Answer));

        foreach (Counterpart counterpart in all.Distinct())
        {
            Type? type = Resolve(counterpart);
            Assert.True(type is not null, $"{counterpart.FullName} does not resolve");

            if (counterpart.Member is { } member)
                Assert.True(
                    type.GetMember(member).Length > 0,
                    $"{counterpart.FullName} has no member {member}");
        }
    }

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
