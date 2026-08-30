using ChiakiNg.Native;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP553, under PP33: <see cref="IHolepunchSession"/> answered from the three sequences, with no C.
///
/// PP481 implemented this interface over the real holepunch.c and said so plainly: it drives the C
/// rather than replacing it, which is the opposite of a deletion. PP533 settled which direction does
/// remove something. This is the seam pointing that way - the same seven asks, answered by
/// PP548's create, PP549's start and PP550's punch instead of by nine P/Invokes.
///
/// THE DIVISION IS THE C'S. create, start and the CTRL punch happen before session.c is entered;
/// session.c asks for the socket that punch produced, makes its registration request, then asks for
/// the DATA hole. So <see cref="PrepareAsync"/> is everything the C does before the session thread,
/// and the interface is what the session thread sees.
///
/// ONE PLACE BLOCKS, AND IT IS THE SEAM ITSELF. <see cref="PunchHole"/> returns a ChiakiError
/// because session.c is synchronous C, and the sequence behind it is async. That call waits. It is
/// the only one, it is on the session thread rather than a UI one, and it is where a synchronous
/// caller meets an asynchronous implementation - which is a fact about the boundary, not a
/// shortcut. When session.c goes, so does the wait.
///
/// WHAT IT CANNOT ANSWER YET IS THE REGISTRATION INFO. PP551 says why that one is different: its
/// lifetime is a block, and PP479 keeps it out of its outcome for the same reason. So this returns
/// what it was given and does not manufacture one - see <see cref="RegistInfo"/>.
/// </summary>
public sealed class SequencedHolepunchSession : IHolepunchSession, IDisposable
{
    private readonly Func<HolepunchPortType, Task<HolepunchPunchResult>> punch;
    private readonly Dictionary<HolepunchPortType, object> sockets = [];

    /// <param name="punch">
    /// Runs PP550's punch for one port and gives back the result. A function rather than the
    /// sequence itself, because the ctrl punch has already run by the time this is constructed and
    /// the data one has not.
    /// </param>
    public SequencedHolepunchSession(Func<HolepunchPortType, Task<HolepunchPunchResult>> punch)
    {
        ArgumentNullException.ThrowIfNull(punch);
        this.punch = punch;
    }

    /// <summary>The address the console was reached at, from the candidate the race chose.</summary>
    public string SelectedAddress { get; set; } = "";

    /// <summary>The control port, from the same candidate.</summary>
    public ushort CtrlPort { get; set; }

    /// <summary>
    /// The registration info, which is whatever the caller was holding when it built this.
    ///
    /// NOT PRODUCED HERE, deliberately. PP551: this is the one result whose lifetime is a block, so
    /// a class that manufactured and stored one would be writing the field PP479 warns about. The
    /// caller owns it for as long as the registration takes and no longer.
    /// </summary>
    public object? RegistInfo { get; set; }

    /// <summary>How many times the session was released, which is what a teardown test reads.</summary>
    public int FinisCalled { get; private set; }

    /// <summary>Whether the offer was made, which the C does once before punching the data hole.</summary>
    public bool OfferMade { get; private set; }

    /// <summary>The socket a punch produced for this port, once one has.</summary>
    public void Record(HolepunchPortType type, object socket)
    {
        ArgumentNullException.ThrowIfNull(socket);
        sockets[type] = socket;
    }

    /// <summary>
    /// Everything the C does before session.c is entered: create, start, and the CTRL punch.
    ///
    /// Given as delegates rather than the sequences, so this composes what already runs instead of
    /// constructing it - the three have their own adapters and their own tests.
    ///
    /// PP556: AND IT RECORDS THE SOCKET, which is what makes "prepared" mean anything. The first
    /// version returned true on a punched hole and was static, so it could not; the caller had to
    /// know to record separately, and the first thing session.c asks for is that socket. Forgetting
    /// left a session that had done everything right and threw on the first ask.
    ///
    /// A punch that punched without producing a socket is therefore not prepared either. The socket
    /// comes back with the result because <see cref="HolepunchPunchResult"/> does not carry one -
    /// it is the race's, and the sequence does not own it.
    /// </summary>
    public async Task<bool> PrepareAsync(
        Func<Task<bool>> create,
        Func<Task<bool>> start,
        Func<Task<(HolepunchPunchResult Result, object? Socket)>> punchCtrl)
    {
        ArgumentNullException.ThrowIfNull(create);
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(punchCtrl);

        if (!await create().ConfigureAwait(false) || !await start().ConfigureAwait(false))
            return false;

        (HolepunchPunchResult result, object? socket) = await punchCtrl().ConfigureAwait(false);

        if (result.Outcome != HolepunchPunchOutcome.Punched || socket is null)
            return false;

        Record(HolepunchPortType.Ctrl, socket);
        return true;
    }

    /// <summary>The socket the punch for this port produced.</summary>
    public object GetSocket(HolepunchPortType type)
        => sockets.TryGetValue(type, out object? socket)
            ? socket
            : throw new InvalidOperationException($"no hole has been punched for {type}");

    /// <summary>The registration info, which the caller owns.</summary>
    public object GetRegistInfo()
        => RegistInfo ?? throw new InvalidOperationException("no registration info was given");

    /// <summary>
    /// The offer, which the C makes once before punching the data hole.
    ///
    /// Recorded rather than sent: PP550's sequence sends the offer as one of its eleven steps, so a
    /// send here would be the second one. What session.c's call means at this seam is "the punch
    /// about to run should offer", and the punch does.
    ///
    /// PP555: SO THIS CANNOT FAIL, AND IN THE C IT CAN. PP460 gives this step the guard
    /// QuitsToCtrlTeardown - a real path session.c takes when the offer is refused. Against this
    /// session that guard is unreachable, because the offer has not been sent yet when the call is
    /// made. The failure is not lost, it MOVES: the punch's own SendOffer step carries it, and
    /// arrives one step later. <see cref="OfferFailed"/> keeps the cause legible there.
    /// </summary>
    public ChiakiError CreateOffer()
    {
        OfferMade = true;
        return ChiakiError.Success;
    }

    /// <summary>Where the offer's failure arrives instead, a step later than the C's.</summary>
    public const HolepunchStep TheOfferFailsAt = HolepunchStep.PunchHole;

    /// <summary>And which of the punch's own steps carries it.</summary>
    public const HolepunchPunchStep TheOfferIsSentAt = HolepunchPunchStep.SendOffer;

    /// <summary>
    /// Whether the punch failed on the offer it sends - the C's create_offer failure, arriving
    /// under the punch's name.
    ///
    /// Without this the departure is a punch failure like any other and the cause is gone. The
    /// session quits to the same teardown either way, so the difference is what can be said about
    /// it rather than what happens.
    /// </summary>
    public bool OfferFailed { get; private set; }

    /// <summary>
    /// The punch for this port, waited on - the one blocking call, and see the note on the type.
    ///
    /// PP554: AND WHERE EXCEPTIONS STOP. This returns a ChiakiError to a caller written in C, which
    /// has no way to catch anything. The sequence behind it is ordinary managed code and can throw:
    /// a cancelled token makes the poll's Task.Delay throw, and the punch does not catch it - so
    /// before this, cancelling by token unwound through a C stack frame instead of answering
    /// Canceled, which is the answer the punch has a whole one-shot for.
    /// </summary>
    public ChiakiError PunchHole(HolepunchPortType type)
    {
        try
        {
            return Reported(punch(type).GetAwaiter().GetResult());
        }
        catch (OperationCanceledException)
        {
            // The same answer PP538's one-shot gives. A token and a stop are two ways to say it.
            return ChiakiError.Canceled;
        }
        catch (Exception thrown) when (thrown is not (OutOfMemoryException or StackOverflowException))
        {
            Thrown = thrown;
            return ChiakiError.Unknown;
        }
    }

    /// <summary>
    /// What was thrown, where something was.
    ///
    /// Kept rather than swallowed: the C gets an error code because that is all it can take, and
    /// this is how a managed caller or a test finds out what actually happened.
    /// </summary>
    public Exception? Thrown { get; private set; }

    /// <summary>
    /// The outcome as an error, noting on the way whether the offer is what failed.
    ///
    /// PP555: this is the only place that can tell. By the time the flow sees an error the step it
    /// stopped at is PunchHole, whichever of the punch's eleven actually failed.
    /// </summary>
    private ChiakiError Reported(HolepunchPunchResult result)
    {
        if (result.StoppedAt == TheOfferIsSentAt)
            OfferFailed = true;

        return Reported(result.Outcome);
    }

    /// <summary>
    /// What each punch outcome is to a C caller.
    ///
    /// Cancelled is Canceled and a timeout is HostDown, which is the mapping PP546 read out of the
    /// start: a console that does not answer inside the deadline is down, not slow.
    /// </summary>
    public static ChiakiError Reported(HolepunchPunchOutcome outcome) => outcome switch
    {
        HolepunchPunchOutcome.Punched => ChiakiError.Success,
        HolepunchPunchOutcome.Cancelled => ChiakiError.Canceled,
        HolepunchPunchOutcome.TimedOut => ChiakiError.HostDown,
        HolepunchPunchOutcome.Uninitialised => ChiakiError.InvalidData,
        _ => ChiakiError.Unknown,
    };

    /// <summary>The address the race chose.</summary>
    public string GetSelectedAddress() => SelectedAddress;

    /// <summary>The port the race chose.</summary>
    public ushort GetCtrlPort() => CtrlPort;

    /// <summary>
    /// The session released. Counted, because the C reaches it from two teardown paths and PP479's
    /// outcome carries the count so a test can see it happened once.
    /// </summary>
    public void Fini() => FinisCalled++;

    /// <summary>Releasing twice is what the C does; disposing is not a third.</summary>
    public void Dispose() => sockets.Clear();
}
