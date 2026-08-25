using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP327: `--record`, which is the whole of what PP297 was reduced to - "a flag rather than a
/// project" - and the half of it that can be checked with no console in the room.
///
/// What cannot be checked here is a real capture. What can is every decision the flag makes before
/// one starts: whether it was asked for, where it writes, and what it does with an argument that is
/// not a path.
/// </summary>
public class RecordingFlagTests
{
    private static readonly DateTimeOffset Noon =
        new(2026, 8, 25, 12, 34, 56, TimeSpan.Zero);

    private const string Logs = @"C:\logs";

    /// <summary>No flag, no recording - and null rather than a path nobody asked to be written.</summary>
    [Fact]
    public void WithoutTheFlagThereIsNoRecording()
    {
        Assert.Null(HostCommandLine.RecordingPath([], Logs, Noon));
        Assert.Null(HostCommandLine.RecordingPath(["--selftest"], Logs, Noon));
    }

    /// <summary>A path after the flag is the path.</summary>
    [Fact]
    public void APathAfterTheFlagIsUsed()
    {
        Assert.Equal(
            @"D:\captures\one.txt",
            HostCommandLine.RecordingPath(["--record", @"D:\captures\one.txt"], Logs, Noon));
    }

    /// <summary>
    /// A bare flag lands in the log directory under a stamped name.
    ///
    /// Stamped and not fixed, because a recording exists to be compared with another one. A default
    /// that reused a name would silently replace the file it was about to be diffed against, which
    /// is the worst failure this feature has available to it.
    /// </summary>
    [Fact]
    public void ABareFlagIsStampedIntoTheLogDirectory()
    {
        string? path = HostCommandLine.RecordingPath(["--record"], Logs, Noon);

        Assert.Equal(Path.Combine(Logs, "exchange-20260825-123456.txt"), path);
    }

    /// <summary>And two runs a second apart do not write the same file.</summary>
    [Fact]
    public void TwoRunsDoNotCollide()
    {
        Assert.NotEqual(
            HostCommandLine.RecordingPath(["--record"], Logs, Noon),
            HostCommandLine.RecordingPath(["--record"], Logs, Noon.AddSeconds(1)));
    }

    /// <summary>
    /// A FLAG AFTER THE FLAG IS NOT A PATH, which is the case --capture-mapping gets wrong.
    ///
    /// It takes whatever follows it, so `--capture-mapping --analog` writes a PNG named "--analog".
    /// Copying that here would mean `--record --selftest` writing the recording to a file called
    /// "--selftest" and never running the selftest, so an argument starting with a dash means the
    /// path was omitted.
    /// </summary>
    [Theory]
    [InlineData("--selftest")]
    [InlineData("--analog")]
    [InlineData("-h")]
    public void AFlagAfterTheFlagIsNotAPath(string next)
    {
        string? path = HostCommandLine.RecordingPath(["--record", next], Logs, Noon);

        Assert.Equal(Path.Combine(Logs, "exchange-20260825-123456.txt"), path);
    }

    /// <summary>The flag is answered wherever it sits, not only first.</summary>
    [Fact]
    public void TheFlagIsFoundAnywhereInTheArguments()
    {
        Assert.Equal(
            @"D:\one.txt",
            HostCommandLine.RecordingPath(["--map-controller", "--record", @"D:\one.txt"], Logs, Noon));
    }

    /// <summary>
    /// It is on the list, so `--help` names it and `Unrecognised` does not refuse it.
    ///
    /// PP306's own check holds the list against the dispatch in App.xaml.cs; this is the other
    /// direction - a flag that works and is undocumented is one nobody finds.
    /// </summary>
    [Fact]
    public void TheFlagIsDocumentedAndAccepted()
    {
        Assert.Contains(HostCommandLine.Flags, f => f.Name == "--record");
        Assert.Empty(HostCommandLine.Unrecognised(["--record"]));
        Assert.Contains("--record", HostCommandLine.Usage(), StringComparison.Ordinal);
    }
}
