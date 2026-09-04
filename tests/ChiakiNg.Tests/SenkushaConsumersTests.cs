using System.Reflection;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP702, under PP27: the takion symbols senkusha.c calls, counted once and kept.
///
/// PP27's fourth criterion is an end state - takion.c, takionsendbuffer.c and reorderqueue.c leave
/// the build - and senkusha.c is not one of the three. It calls five of takion's exports, so the
/// criterion cannot be met while it stands, and PP638's linker run never asked: that one was about
/// the FRAME path, which is PP295's subject and a different set of files.
///
/// BOTH DIRECTIONS, which is the whole discipline. The calls are read out of senkusha.c and each is
/// looked up in the model; a call with no row fails by name and a row with no call fails too. The
/// reader matches anything in takion's namespace rather than the five, so a SIXTH call arriving is
/// what this catches - a pattern listing the five could only ever confirm what it was given.
/// </summary>
public class SenkushaConsumersTests(ITestOutputHelper output)
{
    private static readonly Assembly App = typeof(ManagedTakion).Assembly;

    private static Type? Resolve(Counterpart counterpart) => App.GetType(counterpart.FullName);

    private static string? Source()
        => SenkushaConsumers.Locate() is { } path ? File.ReadAllText(path) : null;

    /// <summary>
    /// THE CENSUS: what senkusha.c calls is exactly what is modelled, each way.
    ///
    /// The count is not asserted. Five is what the reading found and a sixth arriving is the news
    /// this exists for, so what is held is the SETS - which is a stronger claim than a number and
    /// the one that names the symbol when it breaks.
    /// </summary>
    [Fact]
    public void TheCallsAndTheRowsAgree()
    {
        if (Source() is not { } source)
            return;

        IReadOnlyList<string> found = SenkushaConsumers.CallsIn(source);
        output.WriteLine($"senkusha.c calls: {string.Join(", ", found)}");

        IReadOnlyList<string> modelled = [.. SenkushaConsumers.Symbols.Select(one => one.Symbol)];

        Assert.Empty(found.Except(modelled, StringComparer.Ordinal));
        Assert.Empty(modelled.Except(found, StringComparer.Ordinal));

        // PP271: a reader that matched nothing would satisfy both of those.
        Assert.NotEmpty(found);
    }

    /// <summary>
    /// Every counterpart resolves, and the member it names exists on it.
    ///
    /// The half that runs outside a checkout too: the mapping is a claim about this assembly, so a
    /// counterpart renamed away fails here before any file is read.
    /// </summary>
    [Fact]
    public void EveryCounterpartResolves()
    {
        foreach (Counterpart counterpart in SenkushaConsumers.Symbols.Select(one => one.Answer).Distinct())
        {
            Type? type = Resolve(counterpart);
            Assert.True(type is not null, $"{counterpart.FullName} does not resolve");

            if (counterpart.Member is { } member)
            {
                Assert.True(
                    type.GetMember(member).Length > 0,
                    $"{counterpart.FullName} has no member {member}");
            }
        }
    }

    /// <summary>
    /// senkusha.c is NOT one of the three files the criterion names, which is why this line exists.
    ///
    /// The whole finding in one assertion. If senkusha were among them its calls would leave with
    /// it and there would be nothing to count; it is not, so every symbol above is a link-time
    /// dependency the deletion has to answer for.
    /// </summary>
    [Fact]
    public void SenkushaIsNotOneOfTheFilesThatLeave()
    {
        Assert.DoesNotContain(SenkushaConsumers.RelativePath, SenkushaConsumers.Leaving);

        // And the three are the criterion's, read from the roadmap rather than typed twice.
        Assert.Equal(
            ["lib\\src\\reorderqueue.c", "lib\\src\\takion.c", "lib\\src\\takionsendbuffer.c"],
            SenkushaConsumers.Leaving.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// The formatter is one of the five, which is what PP679's decision was for.
    ///
    /// It is defined in takion.c and called only here, so who owned it had to be settled before this
    /// census could say anything about it. PP679 settled it - the parse and the formatter went
    /// managed and the C's copy stands until senkusha is ported - and this is that decision showing
    /// up as a row.
    /// </summary>
    [Fact]
    public void TheV7FormatterIsOneOfThem()
    {
        ConsumedSymbol formatter = Assert.Single(
            SenkushaConsumers.Symbols,
            one => one.Symbol == AvPacketV7Source.FormatterName);

        Assert.Equal(nameof(AvPacketV7), formatter.Answer.Type);
        Assert.Equal(nameof(AvPacketV7.FormatHeader), formatter.Answer.Member);
    }

    /// <summary>
    /// The reader tells a call from a comment and from a declaration.
    ///
    /// The two ways a census over C text overcounts, and both have cost this tree a wrong number
    /// before: PP400's comment-as-code and the shim's own forward declaration of create_matrix.
    /// </summary>
    [Fact]
    public void TheReaderCountsCallsAndNotProse()
    {
        Assert.Equal(
            ["chiaki_takion_send_raw"],
            SenkushaConsumers.CallsIn("err = chiaki_takion_send_raw(&s->takion, data, size);"));

        Assert.Empty(SenkushaConsumers.CallsIn("// chiaki_takion_send_raw(x) is what this does"));
        Assert.Empty(SenkushaConsumers.CallsIn("extern int chiaki_takion_send_raw(void);"));
        Assert.Empty(SenkushaConsumers.CallsIn("chiaki_takion_send_raw is named and not called"));
    }

    /// <summary>
    /// And a sixth call would be found, which is what makes the sets above worth asserting.
    ///
    /// Over invented text rather than the file: the claim is that the READER is not restricted to
    /// the five, and asserting it against senkusha.c would need a sixth call to exist there.
    /// </summary>
    [Fact]
    public void ASixthCallWouldBeFound()
        => Assert.Equal(
            ["chiaki_takion_format_congestion"],
            SenkushaConsumers.CallsIn("chiaki_takion_format_congestion(buf, &packet, key_pos);"));
}
