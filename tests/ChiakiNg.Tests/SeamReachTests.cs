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

    /// <summary>A seam filled with nothing: every member a constant, which is what PP776 is about.</summary>
    private sealed class Refusing : IBangKeying
    {
        public bool DeriveSecret(ReadOnlySpan<byte> remotePubKey, ReadOnlySpan<byte> remoteSig) => false;

        public bool InitCrypt() => false;
    }

    /// <summary>And one that does work, so the check has a negative side it can be believed on.</summary>
    private sealed class Working : IBangKeying
    {
        public int Derived { get; private set; }

        public bool DeriveSecret(ReadOnlySpan<byte> remotePubKey, ReadOnlySpan<byte> remoteSig)
        {
            Derived++;
            return remotePubKey.Length > 0;
        }

        public bool InitCrypt() => Derived > 0;
    }

    /// <summary>
    /// PP776: A REFUSAL IS NOT AN IMPLEMENTATION, and the sweep above cannot tell them apart.
    ///
    /// PP773 filled IBangKeying with a class whose derive returns false - the bang handler needs an
    /// instance and the port had no ECDH of its own - and <see cref="SeamReach.Expected"/> went
    /// empty on a stub. A refusing class and a real one are the same shape, which is exactly why the
    /// stub compiles, so the type graph can never answer this: the bodies have to.
    /// </summary>
    [Fact]
    public void AConstantBodiedFillerIsAStandIn()
    {
        Assert.True(SeamReach.IsStandIn(typeof(Refusing), typeof(IBangKeying)));
        Assert.False(SeamReach.IsStandIn(typeof(Working), typeof(IBangKeying)));

        // The real one, which is what took IBangKeying off the list honestly.
        Assert.False(SeamReach.IsStandIn(typeof(SessionBangKeying), typeof(IBangKeying)));

        // And a class that does not fill the seam at all is not a stand-in for it, which is a
        // different answer from "fills it with nothing".
        Assert.False(SeamReach.IsStandIn(typeof(Working), typeof(IAudioSink)));
    }

    /// <summary>
    /// AND THE SWEEP AGREES WITH ITS OWN LIST, both ways.
    ///
    /// The same contract the unreached list is held to. A row arriving means a seam went back to
    /// being a shape with nothing behind it; a row leaving is the commit that wrote one.
    /// </summary>
    [Fact]
    public void TheStandInSeamsAreTheseAndNoOthers()
    {
        string[] found = [.. SeamReach.FilledOnlyByStandInsIn(App)];
        string[] declared =
            [.. SeamReach.ExpectedStandIns.Select(one => one.Interface).Order(StringComparer.Ordinal)];

        output.WriteLine($"{found.Length} seam(s) filled only by stand-ins");
        output.WriteLine(string.Join("\n", found));

        Assert.Equal(declared, found);

        // IBangKeying is the one this axis was filed about, and it is filled by something that
        // works - so it appears on neither list, which is the state PP773 ended in.
        Assert.DoesNotContain(nameof(IBangKeying), found);
        Assert.DoesNotContain(nameof(IBangKeying), SeamReach.UnreachedIn(App));
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
