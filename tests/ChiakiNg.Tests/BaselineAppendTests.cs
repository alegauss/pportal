using ChiakiNg.Native;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP23: the ledger is APPENDED to, and a failure to append says so.
///
/// The file is one row per session and it accumulates: PP5 compares two builds by holding their
/// rows against each other, which needs the rows of every session before this one to still be
/// there. A writer that truncated would leave a file that looks correct - one valid row - and has
/// silently thrown away the history it exists to hold.
///
/// And a write that cannot happen has to be reported. The ledger lives under a directory the
/// application creates; a session that ran before that directory existed must not believe it
/// recorded itself.
/// </summary>
public class BaselineAppendTests : IDisposable
{
    private readonly string directory =
        Path.Combine(Path.GetTempPath(), "chiaki-baseline-" + Guid.NewGuid().ToString("N"));

    public BaselineAppendTests() => Directory.CreateDirectory(directory);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a suite over.
        }

        GC.SuppressFinalize(this);
    }

    private static SessionBaseline Reference(ulong durationMs)
    {
        var baseline = new SessionBaseline();
        baseline.SetStarted(DateTimeOffset.FromUnixTimeSeconds(1754944267));
        baseline.SetDuration(TimeSpan.FromMilliseconds(durationMs));
        baseline.SetAppVersion("1.10.0");
        baseline.SetVideo("h264", 1920, 1080, 60, 30000);
        baseline.SetConfig("cuda", "opengl", 0.05, idrOnFecFailure: true);
        return baseline;
    }

    /// <summary>
    /// Two sessions, two rows, and both still there. The second value is asserted alongside the
    /// first rather than instead of it, because a truncating writer passes any check that only
    /// looks for the row it just wrote.
    /// </summary>
    [Fact]
    public void ASecondSessionAddsARowRatherThanReplacingOne()
    {
        string path = Path.Combine(directory, "baseline.jsonl");

        using (SessionBaseline first = Reference(754321))
            Assert.Equal(ChiakiError.Success, first.AppendTo(path));

        using (SessionBaseline second = Reference(42))
            Assert.Equal(ChiakiError.Success, second.AppendTo(path));

        string contents = File.ReadAllText(path);

        Assert.Equal(2, contents.Count(c => c == '\n'));
        Assert.Contains("\"duration_ms\":754321", contents, StringComparison.Ordinal);
        Assert.Contains("\"duration_ms\":42", contents, StringComparison.Ordinal);
    }

    /// <summary>The file is created by the first append rather than having to exist.</summary>
    [Fact]
    public void TheFirstAppendCreatesTheFile()
    {
        string path = Path.Combine(directory, "fresh.jsonl");
        Assert.False(File.Exists(path));

        using SessionBaseline baseline = Reference(1000);

        Assert.Equal(ChiakiError.Success, baseline.AppendTo(path));
        Assert.True(File.Exists(path));
    }

    /// <summary>
    /// A path whose directory does not exist fails rather than succeeding quietly. The ledger sits
    /// under a directory the application creates, and a session that ran before it existed must
    /// not believe it recorded itself.
    /// </summary>
    [Fact]
    public void AWriteThatCannotHappenIsReported()
    {
        string path = Path.Combine(directory, "no_such_directory", "out.jsonl");

        using SessionBaseline baseline = Reference(1000);

        Assert.NotEqual(ChiakiError.Success, baseline.AppendTo(path));
        Assert.False(File.Exists(path));
    }

    /// <summary>
    /// Every row in the file is a whole row. The C refuses a buffer that is one byte short and
    /// leaves it UNTOUCHED rather than writing a prefix - a partial line in a ledger is not a
    /// short row, it is a corrupt one and the row after it as well.
    ///
    /// The port cannot pass a short buffer through <see cref="SessionBaseline.Format"/>, which
    /// sizes from the library's own maximum. What it can assert is the other side of that: the
    /// maximum really is enough for a filled session, so the sizing this relies on is not a guess.
    /// </summary>
    [Fact]
    public void TheLibrarysMaximumIsEnoughForAFilledSession()
    {
        using SessionBaseline baseline = Reference(754321);

        baseline.PushHandoff(900);
        baseline.PushInputToWire(400);
        foreach (FrameStageTimer stage in Enum.GetValues<FrameStageTimer>())
            baseline.PushStage(stage, 4200);

        string line = baseline.Format();

        Assert.EndsWith("\n", line, StringComparison.Ordinal);
        Assert.StartsWith("{", line, StringComparison.Ordinal);

        // A whole row: it parses, which a prefix would not.
        using System.Text.Json.JsonDocument document =
            System.Text.Json.JsonDocument.Parse(line);
        Assert.Equal(System.Text.Json.JsonValueKind.Object, document.RootElement.ValueKind);
    }
}
