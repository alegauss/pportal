using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP560: PP547's eleven steps, read out of holepunch.c instead of believed.
/// </summary>
public class HolepunchPunchOrderTests
{
    private static string Source()
    {
        string? path = HolepunchPunchOrder.Locate();
        Assert.NotNull(path);
        return File.ReadAllText(path);
    }

    /// <summary>
    /// THE ORDER IS THE C'S, which nothing checked. PP547 declared it, PP550 put running pieces
    /// behind it, and every task since rested on a list that had been believed.
    /// </summary>
    [Fact]
    public void TheCStillMakesThoseCallsInThatOrder()
        => Assert.True(HolepunchPunchOrder.TheOrderIsStillTheCs(Source()));

    /// <summary>And the sequence's own list is the same one, compared rather than repeated.</summary>
    [Fact]
    public void TheSequenceDeclaresTheSameSteps()
        => Assert.True(HolepunchPunchOrder.TheSequenceRunsTheSameSteps());

    /// <summary>
    /// Ten of the eleven are calls; the one that is not is named.
    ///
    /// Preconditions is a guard on session state. A step with no anchor is a claim about the C
    /// that this cannot check, so it says which one rather than quietly checking ten of eleven.
    /// </summary>
    [Fact]
    public void OneStepIsNotACallAndSaysSo()
    {
        Assert.Equal(10, HolepunchPunchOrder.Calls.Count);
        Assert.Equal(HolepunchPunch.ExecutionOrder.Count, HolepunchPunchOrder.Anchors.Count);

        IEnumerable<HolepunchPunchStep> uncheckable = HolepunchPunchOrder.Anchors
            .Where(one => one.Anchor is null)
            .Select(one => one.Step);

        Assert.Equal([HolepunchPunchStep.Preconditions], uncheckable);
    }

    /// <summary>
    /// The search is bounded to the punch's own body, which matters: wait_for_session_message is
    /// called from more than one function, so a file-wide search would find an order no single
    /// function runs.
    /// </summary>
    [Fact]
    public void TheBodyIsThePunchsOwn()
    {
        string body = HolepunchPunchOrder.BodyIn(Source());

        Assert.StartsWith(HolepunchPunchOrder.Definition, body, StringComparison.Ordinal);
        Assert.DoesNotContain("\nCHIAKI_EXPORT", body, StringComparison.Ordinal);

        // And it is shorter than the file it came from, which a bound that failed would not be.
        Assert.True(body.Length < Source().Length);
    }

    /// <summary>
    /// Two calls swapped is caught, which is the check working rather than passing. Written against
    /// a doctored copy because the real one cannot be made to do it.
    /// </summary>
    [Fact]
    public void AStepMissingIsCaught()
    {
        string source = Source();

        // The candidate race, renamed - so the order this reads is nine of the ten.
        string doctored = source.Replace(
            HolepunchPunchOrder.Calls[4], "check_candidates_GONE(", StringComparison.Ordinal);

        Assert.NotEqual(source, doctored);
        Assert.False(HolepunchPunchOrder.TheOrderIsStillTheCs(doctored));
    }

    /// <summary>A file with no punch in it answers false rather than passing vacuously.</summary>
    [Fact]
    public void AFileWithoutThePunchIsNotAnOrder()
    {
        Assert.Equal("", HolepunchPunchOrder.BodyIn("int main(void) { return 0; }"));
        Assert.False(HolepunchPunchOrder.TheOrderIsStillTheCs("int main(void) { return 0; }"));
    }
}
