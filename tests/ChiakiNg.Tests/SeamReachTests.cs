using System.Reflection;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP741: the reached question, asked of the assembly instead of one census's rows.
///
/// PP734 and PP738 each added it to a census and each asked it only of that census. PP740 then
/// closed the run host's two rows and introduced IAudioFrameSink in the same commit, so the axis
/// read empty while the same shape sat one layer out. This is the sweep that would have said so.
/// </summary>
public class SeamReachTests(ITestOutputHelper output)
{
    private static readonly Assembly App = typeof(IStreamRunHost).Assembly;

    /// <summary>
    /// THE LIST IS THE ASSEMBLY'S, both ways.
    ///
    /// A row arriving means a counterpart stopped being shipping code; a row leaving is the commit
    /// that gave one an implementation. Neither is allowed to happen quietly, which is the whole
    /// reason this is a declared list and not a printed number.
    /// </summary>
    [Fact]
    public void TheUnreachedSeamsAreTheseAndNoOthers()
    {
        string[] found = [.. SeamReach.UnreachedIn(App)];
        string[] declared = [.. SeamReach.Expected.Select(one => one.Interface).Order(StringComparer.Ordinal)];

        output.WriteLine($"{found.Length} of {SeamReach.DeclaredIn(App).Count} public interfaces are unreached");
        output.WriteLine(string.Join("\n", found));

        Assert.Equal(declared, found);
    }

    /// <summary>
    /// PP271: a sweep that found everything unreached would also match a list saying so.
    ///
    /// So the other side is stated too - most of what app declares IS filled, and one interface
    /// this suite can name is filled by a class PP740 wrote.
    /// </summary>
    [Fact]
    public void TheSweepFindsTheSeamsThatAreFilled()
    {
        IReadOnlyList<string> declared = SeamReach.DeclaredIn(App);
        IReadOnlyList<string> unreached = SeamReach.UnreachedIn(App);

        Assert.True(
            unreached.Count < declared.Count,
            "every public interface reads as unreached, so the sweep is finding no classes at all");

        // IAudioSink is the one PP740 filled, and ManagedAudioReceiverPair is what fills it.
        Assert.Contains(nameof(IAudioSink), declared);
        Assert.DoesNotContain(nameof(IAudioSink), unreached);
        Assert.True(typeof(IAudioSink).IsAssignableFrom(typeof(ManagedAudioReceiverPair)));
    }

    /// <summary>Every row says what it is waiting for, because a name with no reason is a list.</summary>
    [Fact]
    public void EveryRowGivesAReason()
        => Assert.All(SeamReach.Expected, row => Assert.False(string.IsNullOrWhiteSpace(row.Why)));

    /// <summary>
    /// And no row names an interface the assembly has stopped declaring.
    ///
    /// A renamed or deleted interface would otherwise leave a row that reads as a known gap and
    /// answers for nothing - the failure PP712 filed one census over.
    /// </summary>
    [Fact]
    public void NoRowNamesSomethingTheAssemblyNoLongerHas()
        => Assert.Empty(SeamReach.NamedButNotDeclared(App));

    /// <summary>
    /// THE TWO CENSUSES AGREE WITH THIS ONE, which is what makes it a generalisation.
    ///
    /// PP734's and PP738's lists answer the same question over their own rows. Anything they call
    /// seam-only has to be unreached here, or two checks are disagreeing about one assembly.
    /// </summary>
    [Fact]
    public void TheCensusesThatAskedItFirstStillAgree()
    {
        IReadOnlySet<string> unreached = new HashSet<string>(SeamReach.UnreachedIn(App), StringComparer.Ordinal);

        // The frame path names its seam-only rows by SYMBOL, and the symbols live in two of its
        // three groups; the third is keyed by file and carries none of them.
        ConsumedSymbol[] symbols = [.. FramePathConsumers.Session, .. FramePathConsumers.Shim];

        foreach (string symbol in FramePathConsumers.SeamOnly)
        {
            ConsumedSymbol row = Assert.Single(symbols, one => one.Symbol == symbol);

            Assert.Contains(row.Answer.Type, unreached);
        }

        foreach (string member in StreamRunHostConsumers.SeamOnly)
        {
            HostMember row = StreamRunHostConsumers.Members.Single(one => one.Member == member);

            Assert.NotNull(row.Answer);
            Assert.Contains(row.Answer.Value.Type, unreached);
        }
    }
}
