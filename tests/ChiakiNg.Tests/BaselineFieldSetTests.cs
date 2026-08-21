using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP23: the ledger's field set, which is closed at both ends.
///
/// test/sessionbaseline.c fills a reference session and asserts two things about the line it
/// produces. Both matter more to this port than to the client that wrote them, because PP5 makes
/// the ledger the file the two builds are COMPARED through:
///
///   every declared field is CARRIED. A field the managed side stopped writing makes a row that
///   parses and cannot be held against the other client's;
///
///   and nothing identifying is carried AT ALL. No host, no address, no nickname, no device id, no
///   account, no token. The session log has a sanitizer to strip those after the fact; the ledger
///   is built so there is nothing to strip - which only stays true while nobody adds a field.
///
/// The second is the one worth a test on this side. The ledger is a file a user is asked to attach
/// to a report, and a managed row that carried a hostname would leak it into every comparison
/// anybody ever ran.
/// </summary>
public class BaselineFieldSetTests
{
    /// <summary>
    /// The reference session, filled with the C fixture's own numbers so the two lines can be put
    /// side by side. The stage statistics are the one part the port cannot fill - see
    /// <see cref="TheStagesAreCarriedEvenThoughThePortCannotFillThem"/>.
    /// </summary>
    private static string ReferenceLine()
    {
        using var baseline = new SessionBaseline();

        baseline.SetStarted(DateTimeOffset.FromUnixTimeSeconds(1754944267));
        baseline.SetDuration(TimeSpan.FromMilliseconds(754321));
        baseline.SetAppVersion("1.10.0");
        baseline.SetVideo("h264", 1920, 1080, 60, 30000);

        // opengl with cuda on purpose: PP72's pair, from an NVIDIA card whose window fell back.
        baseline.SetConfig("cuda", "opengl", 0.05, idrOnFecFailure: true);

        baseline.SetMeasured(27.5, 0.0125, 45210, 12, 7, 36000);

        baseline.PushHandoff(900);
        baseline.PushHandoff(1500);
        baseline.PushHandoff(1200);
        baseline.PushInputToWire(400);
        baseline.PushInputToWire(800);

        return baseline.Format();
    }

    /// <summary>Every key the C's fixture declares, in the shape it looks for them.</summary>
    private static readonly string[] Carried =
    [
        "\"schema\":", "\"started_utc\":", "\"duration_ms\":", "\"app_version\":",
        "\"video\":", "\"width\":", "\"height\":", "\"fps\":", "\"codec\":",
        "\"settings\":", "\"hw_decoder\":", "\"renderer\":", "\"bitrate_kbps\":",
        "\"packet_loss_max\":", "\"idr_on_fec_failure\":",
        "\"measured_bitrate_mbps\":", "\"average_packet_loss\":",
        "\"frames\":", "\"presented\":", "\"lost\":", "\"dropped\":",
        "\"handoff_us\":", "\"stages_us\":", "\"receive\":", "\"reorder\":",
        "\"reassemble\":", "\"correct\":", "\"decode\":",
        "\"latency\":", "\"estimate_us\":", "\"input_to_wire_us\":", "\"network_rtt_us\":",
        "\"min\":", "\"max\":", "\"avg\":", "\"p50\":", "\"p99\":", "\"samples\":",
    ];

    /// <summary>
    /// And the labels that must never appear - checked against the WHOLE line, values included,
    /// which is blunt on purpose: a value carrying a hostname is the same leak as a field named
    /// for one.
    /// </summary>
    private static readonly string[] Never =
    [
        "host", "address", "ipv4", "ipv6", "nickname", "duid", "account",
        "psn", "session_id", "regist", "morning", "token", "url", "http",
    ];

    [Fact]
    public void EveryDeclaredFieldIsCarried()
    {
        string line = ReferenceLine();

        string[] missing = [.. Carried.Where(k => !line.Contains(k, StringComparison.Ordinal))];

        Assert.True(missing.Length == 0, "the line is missing " + string.Join(", ", missing));
    }

    /// <summary>
    /// Nothing that identifies a console, a network or an account. The session log carries a
    /// sanitizer to remove exactly these labels after the fact; here they are absent instead, and
    /// that only stays true while nobody adds a field.
    /// </summary>
    [Fact]
    public void NothingIdentifyingIsCarried()
    {
        string line = ReferenceLine();

        string[] leaked = [.. Never.Where(k => line.Contains(k, StringComparison.OrdinalIgnoreCase))];

        Assert.True(leaked.Length == 0, "the line carries " + string.Join(", ", leaked));
    }

    /// <summary>
    /// The check has to be able to fail, or it passes by looking at the wrong string. A hostname
    /// in a field the port DOES control - the renderer name - is caught, which is the shape of the
    /// leak this guards against.
    /// </summary>
    [Fact]
    public void TheCheckCatchesALeakItShouldCatch()
    {
        using var baseline = new SessionBaseline();
        baseline.SetConfig("cuda", "ps5-livingroom.host", 0.05, idrOnFecFailure: false);

        string line = baseline.Format();

        Assert.Contains(
            Never,
            k => line.Contains(k, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The five per-stage statistics are CARRIED but cannot be FILLED from this port: the seam
    /// exposes handoff and input-to-wire and nothing else, so receive, reorder, reassemble,
    /// correct and decode are written as zeros whatever the session did.
    ///
    /// Asserted rather than left as a silence. The C's fixture pushes one distinguishable value
    /// per stage precisely because "a stage filed under another stage's name" is the defect it
    /// exists to catch - and a port that writes five zeros cannot be caught by it, which is worse
    /// than being caught.
    /// </summary>
    [Fact]
    public void TheStagesAreCarriedEvenThoughThePortCannotFillThem()
    {
        string line = ReferenceLine();

        foreach (string stage in new[] { "receive", "reorder", "reassemble", "correct", "decode" })
            Assert.Contains($"\"{stage}\":", line, StringComparison.Ordinal);

        // The handoff the port CAN fill is not zero, so the zeros above are the gap and not a
        // formatter that writes nothing for everything.
        Assert.DoesNotContain("\"handoff_us\":{\"min\":0,\"max\":0", line, StringComparison.Ordinal);
    }
}
