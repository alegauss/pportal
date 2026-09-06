using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP780, under PP27: the linker's answer about takion's deletion, held against the tree.
///
/// PP565's rule is that a deletion is measured rather than reasoned about, and PP638 is what it
/// costs to skip: §PP295 named the video receiver's callers and missed streamconnection's own. This
/// is the same question one module down, and its answer has a surprise of the same size.
///
/// THE SHIM HOLDS EIGHTEEN OF THE TWENTY-FOUR. Those exports are the port's oracle - a managed
/// reorder queue, send buffer, MAC gate and AV parser compared against the C being replaced - so
/// the deletion takes away what proves the replacement right. PP563 found that shape one module
/// over, and PP33's answer is the one to copy: record what the C answers before removing it.
/// </summary>
public class TakionConsumersTests(ITestOutputHelper output)
{
    /// <summary>
    /// EVERY ROW STILL CALLS WHAT IT CLAIMS, read out of each consumer's own source.
    ///
    /// One direction only, and the census says why: a file may call an inline takion function the
    /// link never sees, so a call the rows lack is not a gap. A row whose symbol is gone IS one -
    /// it means a consumer stopped being one and the plan is about a tree that no longer exists.
    /// </summary>
    [Fact]
    public void EveryRowStillCallsTheSymbolsItNames()
    {
        var read = 0;

        foreach (TakionConsumer consumer in TakionConsumers.Consumers)
        {
            if (TakionConsumers.Locate(consumer.File) is not { } path)
                continue;

            read++;
            IReadOnlyList<string> stale = TakionConsumers.StaleIn(consumer, File.ReadAllText(path));

            Assert.True(
                stale.Count == 0,
                $"{consumer.File} no longer calls {string.Join(", ", stale)}");
        }

        output.WriteLine($"{read} of {TakionConsumers.Consumers.Count} consumers read");

        // Outside a checkout none resolve, and that is the skip rather than a pass.
        if (read > 0)
            Assert.Equal(TakionConsumers.Consumers.Count, read);
    }

    /// <summary>
    /// AND THE ORACLE IS THE BIGGEST OF THEM, which is what this measurement is for.
    ///
    /// Eighteen against nine distinct across lib's four files. A census that reported only a total
    /// would have said twenty-four and hidden the one fact that changes the plan.
    /// </summary>
    [Fact]
    public void TheShimHoldsMoreThanTheLibraryDoes()
    {
        int library = TakionConsumers.Consumers
            .Where(one => one.Kind == TakionConsumerKind.Library)
            .SelectMany(one => one.Symbols)
            .Distinct(StringComparer.Ordinal)
            .Count();

        output.WriteLine(
            $"{TakionConsumers.Symbols.Count} symbols: shim {TakionConsumers.ShimSymbolCount}, "
                + $"lib {library}");

        Assert.Equal(24, TakionConsumers.Symbols.Count);
        Assert.Equal(18, TakionConsumers.ShimSymbolCount);
        Assert.True(
            TakionConsumers.ShimSymbolCount > library,
            "the shim is no longer the largest consumer, so PP780's finding has moved");

        // Three groups, and each has at least one row - the split is the deliverable, because the
        // three want three different answers.
        Assert.All(
            Enum.GetValues<TakionConsumerKind>(),
            kind => Assert.Contains(TakionConsumers.Consumers, one => one.Kind == kind));
    }

    /// <summary>
    /// A READING OVERCOUNTS, and audioreceiver.c is the proof.
    ///
    /// It calls three takion functions and appears in no link error, because all three are static
    /// inline in takion.h. A census built by grepping would have listed a consumer the deletion
    /// does not have to answer for - which is the argument for asking the linker.
    /// </summary>
    [Fact]
    public void TheInlineThreeAreCalledAndNeverLinked()
    {
        if (SanitizerSource.LocateRelative(@"lib\src\audioreceiver.c") is not { } source
            || SanitizerSource.LocateRelative(@"lib\include\chiaki\takion.h") is not { } header)
        {
            return;
        }

        IReadOnlyList<string> called = TakionConsumers.CallsIn(File.ReadAllText(source));
        string takionHeader = File.ReadAllText(header);

        // The three audioreceiver.c itself calls, which is the case that makes the point.
        string[] audio =
        [
            .. TakionConsumers.InlineInTheHeader.Where(
                one => one.Contains("av_packet_audio", StringComparison.Ordinal)),
        ];

        Assert.Equal(3, audio.Length);

        foreach (string one in audio)
        {
            Assert.Contains(one, called);
            Assert.Contains($"static inline uint8_t {one}", takionHeader, StringComparison.Ordinal);
        }

        // And eight in all, which is what a reading would have put on rows the link never asks for.
        Assert.Equal(8, TakionConsumers.InlineInTheHeader.Count);

        // And it is not a consumer, which is the whole point of the distinction.
        Assert.DoesNotContain(
            TakionConsumers.Consumers, one => one.File.EndsWith("audioreceiver.c", StringComparison.Ordinal));
    }
}
