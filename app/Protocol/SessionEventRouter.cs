using ChiakiNg.Native;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP747, under PP707: where the frame path's nine events land, so none of them is counted unheard.
///
/// PP719 built the seam and its Send drops an event where nothing is listening, keeping the count.
/// In a run that count WAS the record: the rumble bytes, the trigger sides, the LED colour, the
/// player index and the two intensities all arrived and went nowhere, because
/// <see cref="ISessionEventSink"/> had no implementation outside the test project.
///
/// A ROUTER AND NOT A SECOND SEAM. PP741 measured the cost of the other shape - PP740 closed one
/// seam by opening another and the census reported success - so the state stops here, where a pad
/// driver or a screen reads it, instead of behind an interface invented to carry it one hop further.
///
/// CONNECTED IS THE ONE WITH WORDS. The front door already translates the C's CONNECTED into a
/// state a screen prints, and this raises the managed one the same way. The other eight are held
/// rather than translated, for the reason ConsoleSession gives about the C's: a screen saying
/// "Rumble" would be reading the enum aloud.
///
/// LAST WINS, WHICH IS WHAT THESE EVENTS MEAN. Every one of them carries a value the console has
/// just set, not an occurrence to queue - PP689's finding is that the pad info handler writes the
/// new value and then reads it back into the event, so the latest is the state and an older one is
/// only history.
/// </summary>
public sealed class SessionEventRouter : ISessionEventSink
{
    private readonly Action? connected;

    /// <summary>Takes an optional hook for the one event the front door has a state for.</summary>
    public SessionEventRouter(Action? onConnected = null) => connected = onConnected;

    /// <summary>How many events arrived, of any kind.</summary>
    public int Received { get; private set; }

    /// <summary>How many of them were CONNECTED, which the run raises exactly once.</summary>
    public int Connected { get; private set; }

    /// <summary>The last rumble the console asked for, or null before it asks.</summary>
    public RumbleState? Rumble { get; private set; }

    /// <summary>The last trigger effects, whose two sides are ten bytes each.</summary>
    public TriggerEffectsState? TriggerEffects { get; private set; }

    /// <summary>The last LED colour a pad info decided.</summary>
    public PadLed? Led { get; private set; }

    /// <summary>The last player index, which is what lights up on the pad.</summary>
    public byte? PlayerIndex { get; private set; }

    /// <summary>The last haptic intensity, one of the pad info five.</summary>
    public DualSenseEffectIntensity? HapticIntensity { get; private set; }

    /// <summary>And the adaptive triggers' own.</summary>
    public DualSenseEffectIntensity? TriggerIntensity { get; private set; }

    /// <summary>
    /// Whether motion control has been asked to take its origin as it is now.
    ///
    /// A flag rather than a value, because that is what the event is: MOTION_RESET carries nothing
    /// and means the console wants the origin re-taken. Cleared by <see cref="TakeMotionReset"/>,
    /// so whoever acts on it says that it did.
    /// </summary>
    public bool MotionResetWanted { get; private set; }

    /// <summary>The last FEC failure, as frame index and whether a keyframe had been asked for.</summary>
    public (int FrameIndex, bool IdrRequestSent)? FecFailure { get; private set; }

    /// <summary>Takes the motion reset, if one is outstanding, and clears it.</summary>
    public bool TakeMotionReset()
    {
        bool wanted = MotionResetWanted;
        MotionResetWanted = false;

        return wanted;
    }

    /// <inheritdoc/>
    public void Send(in SessionEvent raised)
    {
        Received++;

        switch (raised.Type)
        {
            case ChiakiEventType.Connected:
                Connected++;
                connected?.Invoke();
                break;

            case ChiakiEventType.Rumble:
                Rumble = raised.Rumble;
                break;

            case ChiakiEventType.TriggerEffects:
                TriggerEffects = raised.TriggerEffects;
                break;

            case ChiakiEventType.MotionReset:
                MotionResetWanted = true;
                break;

            case ChiakiEventType.LedColor:
                Led = raised.Led;
                break;

            case ChiakiEventType.PlayerIndex:
                PlayerIndex = raised.PlayerIndex;
                break;

            case ChiakiEventType.HapticIntensity:
                HapticIntensity = raised.Intensity;
                break;

            case ChiakiEventType.TriggerIntensity:
                TriggerIntensity = raised.Intensity;
                break;

            case ChiakiEventType.VideoFecFailure:
                FecFailure = (raised.FecFrameIndex, raised.FecIdrRequestSent);
                break;

            default:
                // The other eight of ChiakiEventType are raised outside the frame path - PP722's
                // census says by whom - and reaching one here would mean this seam had gained a
                // caller nothing in that census names.
                break;
        }
    }

    /// <summary>The event kinds this router has somewhere to put, which is PP719's nine.</summary>
    public static IReadOnlyList<ChiakiEventType> Routed => ManagedSessionEvents.RaisedByTheFramePath;
}
