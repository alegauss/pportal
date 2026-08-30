namespace ChiakiNg.Session;

/// <summary>
/// PP528: the region between the decoder pull and the presenter, where a loss count can go
/// missing.
///
/// <c>chiaki_ffmpeg_decoder_pull_frame</c> hands the caller the decoder's accumulated loss count
/// and zeroes it in the same call, so whoever receives that number is the only one who will ever
/// see it. In the Qt client the frame-available handler receives it and <c>presentFrame</c> is the
/// only thing that folds it into the session record - so every <c>return</c> in between is a count
/// that is simply gone.
///
/// Two of them were, and neither is decoder-neutral. One is an empty pull, which is what a codec
/// whose internal buffer is backing up gives; the other is <c>prepareFrameForPresentation</c>
/// failing, which is the hardware-to-software readback PP48 measured at 793us on cuda against
/// 2253us on d3d11va. So the counter went missing hardest on the slower copy path, which is
/// exactly the difference PP76 exists to measure.
///
/// This models the region rather than the fix, and that is deliberate. The repair is four lines
/// and the thing worth holding is the shape: a fourth return added later would reintroduce the
/// defect silently, and no test that only knew about today's two would notice.
/// </summary>
public static class FrameLossAccounting
{
    /// <summary>Where the frame-available handler lives, relative to the repository root.</summary>
    public const string RelativePath = @"gui\src\qmlbackend.cpp";

    /// <summary>The call that hands the count over and zeroes it. The region starts here.</summary>
    public const string PullCall = "chiaki_ffmpeg_decoder_pull_frame(";

    /// <summary>The call that folds the count into the record. The region ends here.</summary>
    public const string PresentCall = "->presentFrame(";

    /// <summary>
    /// The accumulator a return has to touch before it may leave. Named as a constant because the
    /// assertion is about this identifier appearing beside every return, and a check that spelled
    /// it inline in a regex would pass the day it is renamed.
    /// </summary>
    public const string Carrier = "carried_frames_lost";

    /// <summary>
    /// Adding to the accumulator, which is narrower than <see cref="Carrier"/> on purpose.
    ///
    /// The handler also READS the accumulator once, at the top, to merge what an earlier pull left
    /// behind. A rule that accepted the bare identifier would count that single line as covering
    /// every return below it, and would go green on a return that carries nothing.
    /// </summary>
    public const string CarrySite = "carried_frames_lost.fetchAndAddRelaxed(";

    /// <summary>The file, or null when this is not running out of a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>One <c>return;</c> in the region, and whether the count survives it.</summary>
    public sealed record EarlyReturn(int Line, bool CarriesTheCount);

    /// <summary>
    /// The lines from the pull to the present, exclusive of neither.
    ///
    /// Empty when either end is missing, which is a real answer rather than a defensive one: a
    /// handler that no longer pulls or no longer presents is not this region with a hole in it,
    /// it is a different handler, and the caller below reports that as a failure.
    /// </summary>
    public static IReadOnlyList<string> RegionLines(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        string[] lines = source.ReplaceLineEndings("\n").Split('\n');
        int start = Array.FindIndex(lines, l => l.Contains(PullCall, StringComparison.Ordinal));
        if (start < 0)
            return [];

        int end = Array.FindIndex(lines, start + 1, l => l.Contains(PresentCall, StringComparison.Ordinal));
        return end < 0 ? [] : lines[start..(end + 1)];
    }

    /// <summary>
    /// Every bare <c>return;</c> in the region, with whether the path that reaches it adds the
    /// count to the accumulator first.
    ///
    /// The search is bounded to the return's OWN block, walking up and counting braces until the
    /// one that opened it. Anything looser reads the merge at the top of the handler as covering
    /// every return underneath, which is the one wrong answer this rule must not give: it is
    /// precisely the pre-repair code, and a check that calls that compliant holds nothing.
    ///
    /// A cleanup call between the carry and the return does not disqualify it, which is why this
    /// is a search rather than a look at the line above: freeing the frame is the last thing the
    /// transfer-failure path does, and the carry belongs before the free rather than after it.
    /// </summary>
    public static IReadOnlyList<EarlyReturn> EarlyReturns(string source)
    {
        var region = RegionLines(source);
        var found = new List<EarlyReturn>();

        for (int i = 0; i < region.Count; i++)
        {
            if (region[i].Trim() == "return;")
                found.Add(new EarlyReturn(i, CarriedBefore(region, i)));
        }

        return found;
    }

    /// <summary>The upward walk described above, for one return.</summary>
    private static bool CarriedBefore(IReadOnlyList<string> region, int returnIndex)
    {
        int depth = 0;
        for (int j = returnIndex - 1; j >= 0; j--)
        {
            string above = region[j].Trim();
            if (above.Length == 0 || above.StartsWith("//", StringComparison.Ordinal))
                continue;

            if (above == "}")
            {
                depth++;
            }
            else if (above.EndsWith('{'))
            {
                // The brace that opened the block this return sits in: the path stops here.
                if (depth == 0)
                    return false;
                depth--;
            }
            else if (depth == 0 && above.Contains(CarrySite, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
