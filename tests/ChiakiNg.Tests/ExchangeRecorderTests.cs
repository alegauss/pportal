using System.Text;
using ChiakiNg.Native;
using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP326: the tap joined to the recording, and the two decisions the join forced.
///
/// Every message here goes through <see cref="ChiakiMessageTap.Emit"/>, so it crosses into C and
/// comes back the way a real one from ctrl.c would - the same argument PP323's own tests make. A
/// recorder that only worked when a test called its private method would not be a thing this file
/// could produce.
///
/// Collection-serialised, because the tap underneath is GLOBAL: lib/src holds one function pointer.
/// </summary>
[Collection(nameof(ExchangeRecorderTests))]
[CollectionDefinition(nameof(ExchangeRecorderTests), DisableParallelization = true)]
public class ExchangeRecorderTests
{
    /// <summary>A session head arrives as itself, because it is text and reads as text.</summary>
    [Fact]
    public void TheSessionChannelIsRecordedAsTheTextItIs()
    {
        using ExchangeRecorder recorder = ExchangeRecorder.Start();

        const string head = "GET /sie/ps5/rp/sess/init HTTP/1.1\r\nRp-Version: 10.0\r\n\r\n";
        ChiakiMessageTap.Emit(
            ExchangeTapDirection.Sent, ChiakiMessageTap.SessionChannel, 0, Encoding.ASCII.GetBytes(head));

        ExchangeEntry only = Assert.Single(recorder.Recording.Entries);
        Assert.Equal("session", only.Channel);
        Assert.Equal(ExchangeDirection.Sent, only.Direction);
        Assert.Contains("/sie/ps5/rp/sess/init", only.Payload, StringComparison.Ordinal);
        Assert.Contains("Rp-Version: 10.0", only.Payload, StringComparison.Ordinal);
    }

    /// <summary>
    /// And PP325 still takes the two secret headers out of it on the way through here.
    ///
    /// Asserted at THIS level and not only at the sanitiser's, because the recorder is what a
    /// session actually calls: a wiring mistake between the two would leave every unit test on
    /// SessionHeaderSanitizer green and the file on disk carrying the nonce.
    /// </summary>
    [Fact]
    public void TheSessionChannelStillLosesItsSecretsOnTheWayIn()
    {
        using ExchangeRecorder recorder = ExchangeRecorder.Start();

        const string head = "HTTP/1.1 200 OK\r\nRP-Nonce: hK9+Lm/2Qw8vZa1sTb4xYg==\r\n\r\n";
        ChiakiMessageTap.Emit(
            ExchangeTapDirection.Received, ChiakiMessageTap.SessionChannel, 0, Encoding.ASCII.GetBytes(head));

        Assert.DoesNotContain("hK9+Lm/2Qw8vZa1sTb4xYg==", recorder.Write(), StringComparison.Ordinal);
        Assert.Contains("RP-Nonce", recorder.Write(), StringComparison.Ordinal);
    }

    /// <summary>
    /// A control message keeps its type and its bytes, rendered as dash-separated pairs.
    ///
    /// The type is in the payload because ExchangeEntry has no field for one, and for this channel
    /// the type is part of what was said.
    /// </summary>
    [Fact]
    public void AControlMessageKeepsItsTypeAndItsBytes()
    {
        using ExchangeRecorder recorder = ExchangeRecorder.Start();

        // GO_HOME, which carries nothing secret.
        ChiakiMessageTap.Emit(
            ExchangeTapDirection.Sent, ChiakiMessageTap.CtrlChannel, 0x14, [0xde, 0xad, 0xbe, 0xef]);

        ExchangeEntry only = Assert.Single(recorder.Recording.Entries);
        Assert.Equal("0014 de-ad-be-ef", only.Payload);
    }

    /// <summary>
    /// THE RENDERING SURVIVES THE SANITISER, which is the whole reason it is dashes.
    ///
    /// This is the assertion the design exists for. Sixteen bytes is where the two obvious
    /// renderings die: as continuous hex it is 32 characters and LongHexPattern takes any run of 16
    /// or more, and as space-separated pairs it is exactly what HexdumpRowPattern was written for.
    /// Either way the payload would come back as a marker and the recording would hold nothing.
    /// </summary>
    [Fact]
    public void SixteenBytesOfControlPayloadSurviveTheSanitiser()
    {
        using ExchangeRecorder recorder = ExchangeRecorder.Start();

        byte[] payload = [.. Enumerable.Range(0, 16).Select(i => (byte)(0xa0 + i))];
        ChiakiMessageTap.Emit(ExchangeTapDirection.Received, ChiakiMessageTap.CtrlChannel, 0x16, payload);

        string stored = Assert.Single(recorder.Recording.Entries).Payload;

        Assert.DoesNotContain("redacted", stored, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("0016 a0-a1-a2-a3-a4-a5-a6-a7-a8-a9-aa-ab-ac-ad-ae-af", stored);
    }

    /// <summary>
    /// The six secret types lose their payload and keep their type.
    ///
    /// Keeping the type is what makes the recording still describe the exchange - a replay can see
    /// that a login happened here - and losing the payload is what stops it carrying the PIN.
    /// </summary>
    [Theory]
    [InlineData((ushort)0x33)]   // SESSION_ID: the payload IS the session id
    [InlineData((ushort)0x5)]    // LOGIN
    [InlineData((ushort)0x4)]    // LOGIN_PIN_REQ
    [InlineData((ushort)0x23)]   // KEYBOARD_TEXT_CHANGE_REQ: text the person typed
    [InlineData((ushort)0x24)]   // KEYBOARD_TEXT_CHANGE_RES
    public void ASecretControlMessageKeepsItsTypeAndLosesItsPayload(ushort type)
    {
        using ExchangeRecorder recorder = ExchangeRecorder.Start();

        ChiakiMessageTap.Emit(
            ExchangeTapDirection.Sent, ChiakiMessageTap.CtrlChannel, type, Encoding.ASCII.GetBytes("hunter2"));

        string stored = Assert.Single(recorder.Recording.Entries).Payload;

        Assert.DoesNotContain("hunter2", stored, StringComparison.Ordinal);
        Assert.DoesNotContain("68-75-6e", stored, StringComparison.Ordinal);
        Assert.Equal($"{type:x4} {CtrlMessageSecrets.Marker}", stored);
    }

    /// <summary>
    /// The PIN reply too, which does not fit in the theory above because its type does not fit in a
    /// byte - 0x8004, and a cast that lost the high bits would record the PIN.
    /// </summary>
    [Fact]
    public void ThePinReplyIsRedactedDespiteItsTypeNotFittingInAByte()
    {
        using ExchangeRecorder recorder = ExchangeRecorder.Start();

        ChiakiMessageTap.Emit(
            ExchangeTapDirection.Sent, ChiakiMessageTap.CtrlChannel, 0x8004, Encoding.ASCII.GetBytes("8461"));

        string stored = Assert.Single(recorder.Recording.Entries).Payload;

        Assert.Equal($"8004 {CtrlMessageSecrets.Marker}", stored);
        Assert.DoesNotContain("8461", stored, StringComparison.Ordinal);
        Assert.DoesNotContain("38-34", stored, StringComparison.Ordinal);
    }

    /// <summary>A heartbeat has no body, and renders as its type and nothing else.</summary>
    [Fact]
    public void AMessageWithNoPayloadIsItsTypeAlone()
    {
        using ExchangeRecorder recorder = ExchangeRecorder.Start();

        ChiakiMessageTap.Emit(ExchangeTapDirection.Received, ChiakiMessageTap.CtrlChannel, 0xfe, []);

        Assert.Equal("00fe ", Assert.Single(recorder.Recording.Entries).Payload);
    }

    /// <summary>
    /// Entries keep the order they arrived in, and the first is the origin.
    ///
    /// The clock starts at Start() rather than at the first message, so the first offset is the gap
    /// between turning recording on and anything happening - but ExchangeRecording rebases on its
    /// own first entry, so what comes out still starts at zero.
    /// </summary>
    [Fact]
    public void EntriesKeepTheirOrderAndStartAtZero()
    {
        using ExchangeRecorder recorder = ExchangeRecorder.Start();

        ChiakiMessageTap.Emit(ExchangeTapDirection.Sent, ChiakiMessageTap.CtrlChannel, 0x14, [1]);
        ChiakiMessageTap.Emit(ExchangeTapDirection.Received, ChiakiMessageTap.CtrlChannel, 0x16, [2]);
        ChiakiMessageTap.Emit(ExchangeTapDirection.Sent, ChiakiMessageTap.CtrlChannel, 0x50, [3]);

        IReadOnlyList<ExchangeEntry> entries = recorder.Recording.Entries;

        Assert.Equal(3, entries.Count);
        Assert.Equal(0, entries[0].AtMicroseconds);
        Assert.True(entries[1].AtMicroseconds >= 0);
        Assert.True(entries[2].AtMicroseconds >= entries[1].AtMicroseconds);
        Assert.Equal(["0014 01", "0016 02", "0050 03"], entries.Select(e => e.Payload));
    }

    /// <summary>Disposing stops it, and the tap underneath goes with it.</summary>
    [Fact]
    public void DisposingStopsTheRecording()
    {
        ExchangeRecorder recorder = ExchangeRecorder.Start();
        Assert.True(ChiakiMessageTap.Active);

        recorder.Dispose();
        Assert.False(ChiakiMessageTap.Active);

        ChiakiMessageTap.Emit(ExchangeTapDirection.Sent, ChiakiMessageTap.CtrlChannel, 0x14, [1]);
        Assert.Empty(recorder.Recording.Entries);

        // Twice, because a session ending can race an explicit stop.
        recorder.Dispose();
    }

    /// <summary>
    /// PP297: the file `--record` leaves behind, into a directory that did not exist.
    ///
    /// The log directory is there on a machine that has run the Qt client and not on one that has
    /// not, and a recording lost because nobody had streamed yet would be found only by the person
    /// who most needed it.
    /// </summary>
    [Fact]
    public void TheRecordingIsWrittenAndMakesItsDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "pportal-record-" + Guid.NewGuid().ToString("n"));
        string path = Path.Combine(directory, "nested", "exchange.txt");

        try
        {
            using ExchangeRecorder recorder = ExchangeRecorder.Start();
            ChiakiMessageTap.Emit(ExchangeTapDirection.Sent, ChiakiMessageTap.CtrlChannel, 0x14, [1, 2]);

            Assert.True(recorder.TryWriteTo(path, out string message), message);

            Assert.Contains("1 entries", message, StringComparison.Ordinal);
            Assert.Contains("0014 01-02", File.ReadAllText(path), StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Writing STOPS it, so nothing lands in the file while it is being serialised.
    ///
    /// The recorder is disposed on the way into the write for this reason, and it is asserted
    /// because the alternative reads identically and fails only under a session that is still busy.
    /// </summary>
    [Fact]
    public void WritingStopsTheRecording()
    {
        string path = Path.Combine(Path.GetTempPath(), "pportal-record-" + Guid.NewGuid().ToString("n") + ".txt");

        try
        {
            using ExchangeRecorder recorder = ExchangeRecorder.Start();
            Assert.True(recorder.TryWriteTo(path, out _));

            Assert.False(ChiakiMessageTap.Active);

            ChiakiMessageTap.Emit(ExchangeTapDirection.Sent, ChiakiMessageTap.CtrlChannel, 0x14, [9]);
            Assert.Empty(recorder.Recording.Entries);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// A path that cannot be written answers false and a sentence, and does not throw.
    ///
    /// This runs on the way out of an application the user has already closed. A throw would replace
    /// whatever they did last with a crash dialog, over a diagnostic failing to save.
    /// </summary>
    [Fact]
    public void AnUnwritablePathIsAnAnswerAndNotAThrow()
    {
        using ExchangeRecorder recorder = ExchangeRecorder.Start();

        // A directory where a file has to be, which no permission can make writable.
        bool written = recorder.TryWriteTo(Path.GetTempPath(), out string message);

        Assert.False(written);
        Assert.Contains("could not write", message, StringComparison.Ordinal);
    }

    /// <summary>
    /// EVERY NUMBER IN THE SECRET LIST IS STILL THE ONE ctrl.c DECLARES.
    ///
    /// The list is a copy, and a type renumbered upstream would leave it redacting a message that no
    /// longer carries the secret while recording the one that now does. That is a leak whose test
    /// stays green, so it is joined by NAME - a check on numbers alone could say the set was
    /// unchanged while every one of them had moved.
    /// </summary>
    [Fact]
    public void EverySecretTypeStillMatchesTheOneCtrlDeclares()
    {
        string? path = CtrlMessageSecrets.Locate();
        if (path is null)
            return;

        IReadOnlyDictionary<string, ushort> declared =
            CtrlMessageSecrets.DeclaredIn(File.ReadAllText(path));

        Assert.NotEmpty(declared);

        foreach ((string name, ushort value) in CtrlMessageSecrets.Secret)
        {
            Assert.True(declared.ContainsKey(name), $"ctrl.c no longer declares {name}");
            Assert.Equal(value, declared[name]);
        }
    }

    /// <summary>
    /// And the channel names are still the ones messagetap.h spells.
    ///
    /// They cross the seam as C string literals and are compared here as managed constants, so the
    /// two spellings have to agree or every ctrl message would be rendered as a session one - which
    /// would put the control channel's bytes in as Latin-1 text and skip the type list entirely.
    /// </summary>
    [Fact]
    public void TheChannelNamesStillMatchTheHeader()
    {
        string? path = MessageTapSource.Locate(MessageTapSource.TapHeader);
        if (path is null)
            return;

        string header = File.ReadAllText(path);

        Assert.Contains(
            $"#define CHIAKI_MESSAGE_TAP_CHANNEL_CTRL \"{ChiakiMessageTap.CtrlChannel}\"",
            header, StringComparison.Ordinal);
        Assert.Contains(
            $"#define CHIAKI_MESSAGE_TAP_CHANNEL_SESSION \"{ChiakiMessageTap.SessionChannel}\"",
            header, StringComparison.Ordinal);
    }
}
