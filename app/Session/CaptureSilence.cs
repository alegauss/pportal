namespace ChiakiNg.Session;

/// <summary>What a running capture is actually doing, which is not the same as whether it opened.</summary>
public enum CaptureHealth
{
    /// <summary>Not started, so there is nothing to judge.</summary>
    Stopped,

    /// <summary>Open and inside the grace period, with no unit yet. Every capture begins here.</summary>
    Starting,

    /// <summary>Units are arriving.</summary>
    Streaming,

    /// <summary>Open past the grace period and has delivered nothing. The one PP695 is about.</summary>
    Silent,
}

/// <summary>
/// PP695: a capture that opens and never speaks.
///
/// PP652's capture opens the default communications endpoint, which is what a host should do. On a
/// machine whose default is a Bluetooth headset that endpoint carries two profiles - a music one
/// with no microphone, and a hands-free one - and opening the capture is meant to make Windows
/// switch. Sometimes it does: twenty units in 222 milliseconds, then a steady hundred a second.
/// Sometimes thirty seconds pass with nothing, on the same device with the same code, and Start
/// reports Running with an HRESULT of zero the whole time.
///
/// THE ENGINE IS OPEN AND THE RADIO HAS NOT SWITCHED, and nothing anywhere says so. A person whose
/// headset is the default starts a session, speaks, and is heard by nobody - which is worse than a
/// failure to open, because a failure to open can be shown.
///
/// SO THE JUDGEMENT IS SEPARATED FROM THE DEVICE. A capture knows how long it has been running and
/// how many units it has delivered, and those two numbers are all this needs. Pulling it out means
/// a test drives every state by holding the clock, which is the only way the SILENT case gets an
/// assertion at all: reproducing it needs a Bluetooth headset in the wrong profile.
///
/// THE GRACE PERIOD IS MEASURED, NOT PICKED. A wired microphone here delivered its first unit at 44
/// milliseconds and a Bluetooth one at 222 when it worked at all; a silent endpoint gave nothing in
/// thirty seconds. Two seconds is an order of magnitude above the slow success and an order below
/// the observed failure, which is the widest gap the readings leave.
/// </summary>
public static class CaptureSilence
{
    /// <summary>
    /// How long a capture may deliver nothing before that is a state rather than a start.
    ///
    /// Nine times the slowest observed success and a fifteenth of the observed silence.
    /// </summary>
    public static TimeSpan Grace { get; } = TimeSpan.FromSeconds(2);

    /// <summary>The slowest first unit seen from an endpoint that did work, which sets the floor.</summary>
    public static TimeSpan SlowestSuccess { get; } = TimeSpan.FromMilliseconds(222);

    /// <summary>
    /// What a capture is doing, from how long it has run and what it has delivered.
    ///
    /// A capture that has delivered anything is Streaming even if it has since stopped: this is
    /// about an endpoint that never spoke, not about one that went quiet. A dropout mid-session is a
    /// different fact with a different answer, and conflating them would make a person muting their
    /// microphone look like a broken device.
    /// </summary>
    public static CaptureHealth Judge(bool running, TimeSpan runningFor, long units)
        => Judge(running, runningFor, units, Grace);

    /// <summary>The same judgement against a given grace, which is what makes it testable.</summary>
    public static CaptureHealth Judge(bool running, TimeSpan runningFor, long units, TimeSpan grace)
    {
        if (!running)
            return CaptureHealth.Stopped;

        if (units > 0)
            return CaptureHealth.Streaming;

        return runningFor >= grace ? CaptureHealth.Silent : CaptureHealth.Starting;
    }

    /// <summary>
    /// What a host should say about a state, or null where there is nothing to say.
    ///
    /// Only the silent case has a message, and that is the point: a capture that is starting or
    /// streaming needs no words, and a host that narrated every state would train a person to
    /// ignore it before the one that matters arrived.
    /// </summary>
    public static string? Advice(CaptureHealth health, string deviceName) => health switch
    {
        CaptureHealth.Silent =>
            $"'{deviceName}' is open but has sent no audio. A Bluetooth headset can stay in its "
                + "music profile, which has no microphone. Try another input device.",
        _ => null,
    };

    /// <summary>Whether a state is one a person should be told about.</summary>
    public static bool WorthReporting(CaptureHealth health) => health == CaptureHealth.Silent;
}
