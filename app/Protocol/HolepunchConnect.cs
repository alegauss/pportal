using ChiakiNg.Native;

namespace ChiakiNg.Protocol;

/// <summary>Which socket the flow is asking for - the only thing telling the two calls apart.</summary>
public enum HolepunchPortType
{
    /// <summary>CHIAKI_HOLEPUNCH_PORT_TYPE_CTRL.</summary>
    Ctrl,

    /// <summary>CHIAKI_HOLEPUNCH_PORT_TYPE_DATA.</summary>
    Data,
}

/// <summary>
/// The nine things session.c asks a holepunch session for, as one interface.
///
/// PP429 wrote them down as a list; this is that list with types on it. The shapes are the C's, not
/// tidied: the socket getters hand back a handle that cannot be null (PP461), the registration info is
/// a value, and the address is a string the caller is expected to keep.
/// </summary>
public interface IHolepunchSession
{
    /// <summary>The socket for a channel. Never null - see PP461.</summary>
    object GetSocket(HolepunchPortType type);

    /// <summary>The registration info the session request carries.</summary>
    object GetRegistInfo();

    /// <summary>An offer for the data connection.</summary>
    ChiakiError CreateOffer();

    /// <summary>A hole punched for a channel.</summary>
    ChiakiError PunchHole(HolepunchPortType type);

    /// <summary>The address the console was reached at.</summary>
    string GetSelectedAddress();

    /// <summary>The port the control channel connects to.</summary>
    ushort GetCtrlPort();

    /// <summary>The session released. Reached from two teardown paths.</summary>
    void Fini();
}

/// <summary>
/// What the flow holds once it has run, and where it stopped.
/// </summary>
/// <param name="FailedAt">The step that failed, or null where none did.</param>
/// <param name="Error">Why, where one failed.</param>
/// <param name="Rudp">The rudp built from the ctrl socket, or null where that failed or was skipped.</param>
/// <param name="DataSocket">
/// The stream's socket, or null - which means a LOCAL session, not an unset field. PP461 and PP478.
/// </param>
/// <param name="Hostname">The address the console was reached at, where the flow got that far.</param>
/// <param name="CtrlPort">The control port, or the session default where the flow did not get there.</param>
/// <param name="FinisCalled">How many times the session was released.</param>
public readonly record struct HolepunchConnectOutcome(
    HolepunchStep? FailedAt,
    ChiakiError Error,
    object? Rudp,
    object? DataSocket,
    string? Hostname,
    ushort CtrlPort,
    int FinisCalled);

/// <summary>
/// PP479, under PP340: the managed side owning the PSN flow - the sequence itself, not a reading of it.
///
/// This is where PP340 stops being modelling. PP429 wrote down the nine call sites, PP460 their
/// execution order and each one's guard, PP478 the five pieces of state and their three lifetimes. All
/// of that described what session.c does. THIS DOES IT.
///
/// IT DRIVES <see cref="HolepunchFlow.ExecutionOrder"/> RATHER THAN A LIST OF ITS OWN. The order is
/// PP460's, read from the model that is asserted against the C, so a step added or moved there moves
/// here. A second hand-written sequence is the duplication PP454 and PP458 both cost a task to undo.
///
/// EACH STEP'S FAILURE IS PP460'S GUARD, NOT A CHOICE MADE HERE. The two error-returning steps quit;
/// the ctrl socket is caught by the rudp init it feeds, which is the one place a failure surfaces under
/// another name; the data socket and the three value-returning steps have nothing to report. Where
/// <see cref="HolepunchFlow.GuardFor"/> says NoFailureToReport this asks and carries on, because that
/// is what the C does and not because nothing can go wrong.
///
/// THE REGISTRATION INFO IS SCOPED, WHICH IS THE ONE THING A MANAGED FLOW HAS TO WORK AT. PP478 found
/// it is a pointer to a stack local, sound only because four calls finish inside its block. Here it is
/// a local of <see cref="Run"/> and never stored on the outcome, so the lifetime the C gets from a
/// closing brace this gets from not keeping it. A field would compile and would be the bug.
///
/// AND THE FINI IS NOT IN THE SEQUENCE. It is teardown, reached from two paths, and PP460's order
/// excludes it for that reason - so this calls it on the way out rather than as a step, and counts the
/// calls so a test can see it happened once.
/// </summary>
public sealed class HolepunchConnect
{
    private readonly IHolepunchSession session;
    private readonly Func<object, object?> initRudp;

    /// <param name="session">The nine asks.</param>
    /// <param name="initRudp">
    /// What the ctrl socket feeds: returns null on failure, which is the only way that step's failure
    /// is visible. The C's chiaki_rudp_init, injected because it is the seam PP460 called
    /// CaughtByWhatItFeeds.
    /// </param>
    public HolepunchConnect(IHolepunchSession session, Func<object, object?> initRudp)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(initRudp);

        this.session = session;
        this.initRudp = initRudp;
    }

    /// <summary>The port a session uses when the flow never reaches the ctrl port. SESSION_PORT.</summary>
    public const ushort DefaultPort = 9295;

    /// <summary>
    /// Runs the seven in-line steps in PP460's order, stopping where the C would.
    /// </summary>
    /// <param name="isPsn">
    /// Whether this is a PSN session at all. False runs nothing and leaves the data socket null, which
    /// is the local path - the C's `if(session->rudp)` around the whole block.
    /// </param>
    public HolepunchConnectOutcome Run(bool isPsn = true)
    {
        object? rudp = null;
        object? dataSocket = null;
        string? hostname = null;
        ushort ctrlPort = DefaultPort;
        var finis = 0;

        if (!isPsn)
        {
            // Nothing asked, nothing released: the local path never made a holepunch session.
            return new HolepunchConnectOutcome(null, ChiakiError.Success, null, null, null, ctrlPort, finis);
        }

        foreach (HolepunchStep step in HolepunchFlow.ExecutionOrder)
        {
            switch (step)
            {
                case HolepunchStep.CtrlSocket:
                    rudp = initRudp(session.GetSocket(HolepunchPortType.Ctrl));
                    if (rudp is null)
                    {
                        // PP460: CaughtByWhatItFeeds. PP339 made this a quit after it had carried on
                        // with a null rudp and reported the failure as "no address answered".
                        session.Fini();
                        finis++;
                        return new HolepunchConnectOutcome(
                            step, ChiakiError.Unknown, null, null, null, ctrlPort, finis);
                    }

                    break;

                case HolepunchStep.RegistInfo:
                    // A local, deliberately. PP478: keeping it would outlive what the C's block gives
                    // it, and a field here would compile.
                    _ = session.GetRegistInfo();
                    break;

                case HolepunchStep.CreateOffer:
                {
                    ChiakiError err = session.CreateOffer();
                    if (err != ChiakiError.Success)
                        return Quit(step, err, rudp, ref finis);

                    break;
                }

                case HolepunchStep.PunchHole:
                {
                    ChiakiError err = session.PunchHole(HolepunchPortType.Data);
                    if (err != ChiakiError.Success)
                        return Quit(step, err, rudp, ref finis);

                    break;
                }

                case HolepunchStep.DataSocket:
                    // PP461: nothing to check. The getter returns the address of a field and the punch
                    // above already quit on failure, so this cannot be invalid here.
                    dataSocket = session.GetSocket(HolepunchPortType.Data);
                    break;

                case HolepunchStep.SelectedAddress:
                    hostname = session.GetSelectedAddress();
                    break;

                default:
                    ctrlPort = session.GetCtrlPort();
                    break;
            }
        }

        return new HolepunchConnectOutcome(
            null, ChiakiError.Success, rudp, dataSocket, hostname, ctrlPort, finis);
    }

    private HolepunchConnectOutcome Quit(
        HolepunchStep step, ChiakiError error, object? rudp, ref int finis)
    {
        session.Fini();
        finis++;

        return new HolepunchConnectOutcome(step, error, rudp, null, null, DefaultPort, finis);
    }
}
