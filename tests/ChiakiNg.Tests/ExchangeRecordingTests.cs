using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP297: the recording format, and the redaction that has to happen before anything is stored.
/// </summary>
public class ExchangeRecordingTests
{
    /// <summary>The ordinary case: entries keep their order and their offsets.</summary>
    [Fact]
    public void EntriesKeepTheirOrderAndOffsets()
    {
        var recording = new ExchangeRecording();
        recording.Add(1_000_000, ExchangeDirection.Sent, "session", "POST /sce/rp/session");
        recording.Add(1_000_250, ExchangeDirection.Received, "session", "HTTP/1.1 200 OK");
        recording.Add(1_040_000, ExchangeDirection.Sent, "ctrl", "hello");

        Assert.Equal(3, recording.Entries.Count);

        // The first entry is the origin, so it is at zero and the rest are relative to it. That is
        // what lets two recordings of the same exchange be compared without a wall clock.
        Assert.Equal(0, recording.Entries[0].AtMicroseconds);
        Assert.Equal(250, recording.Entries[1].AtMicroseconds);
        Assert.Equal(40_000, recording.Entries[2].AtMicroseconds);
    }

    /// <summary>
    /// A payload is redacted when it is ADDED, not when it is written.
    ///
    /// The distinction is the whole point: an unredacted account id never exists in the recording,
    /// so no later mistake can write one out.
    /// </summary>
    [Fact]
    public void SecretsAreRedactedOnTheWayIn()
    {
        var recording = new ExchangeRecording();
        recording.Add(0, ExchangeDirection.Sent, "session", "RP-RegistKey: 3e91107c00000000");

        string stored = recording.Entries[0].Payload;
        Assert.DoesNotContain("3e91107c00000000", stored, StringComparison.Ordinal);
        Assert.Contains("redacted", stored, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>An address in a payload is redacted too, by the same rules the session log uses.</summary>
    [Fact]
    public void AddressesAreRedactedByTheSameRules()
    {
        var recording = new ExchangeRecording();
        recording.Add(0, ExchangeDirection.Received, "ctrl", "connected to 192.168.1.42:9295");

        Assert.DoesNotContain("192.168.1.42", recording.Entries[0].Payload, StringComparison.Ordinal);
    }

    /// <summary>A recording survives being written and read back.</summary>
    [Fact]
    public void ItRoundTrips()
    {
        var recording = new ExchangeRecording();
        recording.Add(0, ExchangeDirection.Sent, "session", "one");
        recording.Add(500, ExchangeDirection.Received, "session", "two");

        ExchangeRecording? read = ExchangeRecording.Read(recording.Write());

        Assert.NotNull(read);
        Assert.Equal(recording.Entries, read.Entries);
    }

    /// <summary>
    /// Including payloads with tabs and newlines, which an HTTP exchange is full of.
    ///
    /// The payload is the last field so a tab inside it cannot shift the others, and newlines are
    /// escaped rather than quoted - a quoted format would need a quoting rule for the quote.
    /// </summary>
    [Fact]
    public void TabsAndNewlinesSurvive()
    {
        var recording = new ExchangeRecording();
        recording.Add(0, ExchangeDirection.Received, "session",
            "HTTP/1.1 200 OK\r\nRP-Version:\t10.0\r\n\r\nbody\\with\\backslashes");

        ExchangeRecording? read = ExchangeRecording.Read(recording.Write());

        Assert.NotNull(read);
        Assert.Equal(recording.Entries[0].Payload, read.Entries[0].Payload);

        // ...and the written form really is one line per entry, which is what makes a diff legible.
        Assert.Equal(2, recording.Write().Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    /// <summary>Text that is not a recording is refused rather than half-read.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("not a recording")]
    [InlineData("chiaki-exchange-99\n0\t->\tsession\thello\n")]
    public void TextThatIsNotARecordingIsRefused(string text)
        => Assert.Null(ExchangeRecording.Read(text));

    /// <summary>And so is a line that is malformed, rather than being skipped.</summary>
    [Theory]
    [InlineData("0\t->\tsession")]                 // too few fields
    [InlineData("later\t->\tsession\thello")]      // not a number
    [InlineData("0\t??\tsession\thello")]          // not a direction
    public void AMalformedLineRefusesTheWholeRecording(string line)
        => Assert.Null(ExchangeRecording.Read($"{ExchangeRecording.FormatVersion}\n{line}\n"));

    /// <summary>
    /// A read does not re-redact, which would redact the redaction.
    ///
    /// Running the patterns over a payload that already says &lt;redacted&gt; is how a round trip
    /// stops being one, and this asserts the round trip above is genuine rather than idempotent by
    /// luck.
    /// </summary>
    [Fact]
    public void ReadingDoesNotRedactAgain()
    {
        var recording = new ExchangeRecording();
        recording.Add(0, ExchangeDirection.Sent, "session", "addr=10.0.0.7 key=3e91107c00000000");

        string once = recording.Entries[0].Payload;
        ExchangeRecording? read = ExchangeRecording.Read(recording.Write());

        Assert.NotNull(read);
        Assert.Equal(once, read.Entries[0].Payload);
    }

    /// <summary>An empty recording is still a recording.</summary>
    [Fact]
    public void AnEmptyRecordingRoundTrips()
    {
        ExchangeRecording? read = ExchangeRecording.Read(new ExchangeRecording().Write());

        Assert.NotNull(read);
        Assert.Empty(read.Entries);
    }
}
