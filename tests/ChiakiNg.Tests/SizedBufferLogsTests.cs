using ChiakiNg.Protocol;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP405: two logs printed a sized receive buffer with the conversion that has no size.
///
/// The ctrl one printed the control messages queued behind a short payload; the websocket one
/// printed a frame buffer past the bytes that arrived. Both were one conversion from correct.
/// </summary>
public class SizedBufferLogsTests(ITestOutputHelper output)
{
    /// <summary>
    /// PP576: THE LIST AND THE PATTERN ARE ONE LIST, and were two.
    ///
    /// BufferNames said "payload", "buf", "data" and nothing read it. The regex that does the work
    /// said the same three inside its own alternation. Two copies agree until one is edited, and
    /// nothing compared them - so a fourth buffer name added to the list would have changed no
    /// behaviour, failed no test, and read as covered.
    ///
    /// The same defect PP551 found between HolepunchDirection's results and the state PP478 carried,
    /// one file over and pre-existing.
    /// </summary>
    [Fact]
    public void ThePatternLooksForEveryNameTheListCarries()
    {
        IReadOnlyList<string> missed = SizedBufferLogs.NamesThePatternMisses();

        Assert.True(missed.Count == 0, $"the pattern ignores: {string.Join(", ", missed)}");
        Assert.NotEmpty(SizedBufferLogs.BufferNames);
    }

    /// <summary>
    /// And the check bites: a name the pattern does not contain is reported. Asserted against the
    /// real pattern with a name that is not in it, because a check that only ever saw agreement
    /// would pass on an empty list too.
    /// </summary>
    [Fact]
    public void ANameThePatternIgnoresIsReported()
    {
        Assert.DoesNotContain(
            "frame", SizedBufferLogs.UnsizedLogPattern, StringComparison.Ordinal);

        foreach (string name in SizedBufferLogs.BufferNames)
            Assert.Contains(name, SizedBufferLogs.UnsizedLogPattern, StringComparison.Ordinal);
    }

    /// <summary>THE TASK. Nothing in lib/src prints a sized buffer with %s.</summary>
    [Fact]
    public void NoLogPrintsASizedBufferWithoutItsSize()
    {
        string? directory = SizedBufferLogs.Locate();
        if (directory is null)
            return;

        IReadOnlyDictionary<string, IReadOnlyList<string>> offenders = SizedBufferLogs.Offenders(directory);

        foreach ((string file, IReadOnlyList<string> logs) in offenders)
        {
            foreach (string log in logs)
                output.WriteLine($"{file}: {log}");
        }

        Assert.Empty(offenders);
    }

    /// <summary>
    /// And the two files that had one still log, so the rule above is not passing on an absence.
    ///
    /// PP271: a sweep that found nothing has not passed. If the regex stopped matching, the check
    /// above would go green on a tree that had regressed - this is what says the files are read.
    /// </summary>
    [Fact]
    public void TheTwoFilesThatHadOneStillLog()
    {
        string? directory = SizedBufferLogs.Locate();
        if (directory is null)
            return;

        foreach (string relative in (string[])["ctrl.c", @"remote\holepunch.c"])
        {
            string source = File.ReadAllText(Path.Combine(directory, relative));

            Assert.True(
                SizedBufferLogs.EveryLogSaysHowLong(source),
                $"{relative} either has no logs to read or prints a buffer without its size");
        }
    }

    /// <summary>PP272: the rule answers no to a file with nothing in it.</summary>
    [Fact]
    public void TheRuleAnswersNoToAnEmptyFile()
    {
        Assert.False(SizedBufferLogs.EveryLogSaysHowLong(""));
        Assert.Empty(SizedBufferLogs.InFile(""));
    }

    /// <summary>And it tells the two conversions apart, which is the whole of the fix.</summary>
    [Fact]
    public void ItTellsTheTwoConversionsApart()
    {
        Assert.Single(SizedBufferLogs.InFile("""CHIAKI_LOGE(log, "was \"%s\"", payload);"""));
        Assert.Empty(SizedBufferLogs.InFile("""CHIAKI_LOGE(log, "was \"%.*s\"", (int)size, payload);"""));

        // A string that happens to be called something else is not one of these.
        Assert.Empty(SizedBufferLogs.InFile("""CHIAKI_LOGE(log, "host %s", hostname);"""));

        // A note quoting the shape it replaced is not the shape - PP399, PP400, PP401, PP403.
        Assert.Empty(SizedBufferLogs.InFile("""// was CHIAKI_LOGE(log, "was %s", payload);"""));
    }
}
