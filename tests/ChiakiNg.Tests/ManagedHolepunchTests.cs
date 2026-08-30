using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP561: the three sequences and the session wired to each other.
///
/// WHAT IS NOT ASSERTED: the create's HTTP calls and websocket need PSN, so every run below stops
/// in the create. What can be checked offline is the wiring itself - one queue, one stop, and which
/// stage is named when one fails.
/// </summary>
public class ManagedHolepunchTests
{
    private static ManagedHolepunch Holepunch(HolepunchStop? stop = null)
        => new("Authorization: Bearer t", "ctx", stop);

    /// <summary>
    /// ONE QUEUE, which is the join this class exists to make. The create owns the websocket, so
    /// the queue it fills is the one the start and the punch read.
    /// </summary>
    [Fact]
    public void TheQueueIsTheCreatesOwn()
    {
        using var holepunch = Holepunch();

        holepunch.Queue.Enqueue(new QueuedNotification(PushNotificationType.SessionCreated, "{}"));

        Assert.Equal(1, holepunch.Queue.Count);
    }

    /// <summary>
    /// AND ONE STOP. PP538's one-shot is consumed at fourteen checkpoints across the three
    /// sequences, so a cancel is one cancel wherever it lands rather than three separate ones.
    /// </summary>
    [Fact]
    public void TheStopIsShared()
    {
        var stop = new HolepunchStop();
        using var holepunch = Holepunch(stop);

        Assert.Same(stop, holepunch.Stop);

        stop.Cancel(stopWebsocketThread: false);
        Assert.True(holepunch.Stop.CheckAndConsume());
        Assert.False(holepunch.Stop.CheckAndConsume());
    }

    /// <summary>
    /// A create that cannot reach PSN names the create, not "something went wrong" - which is what
    /// a caller needs to tell a network problem from a console one.
    /// </summary>
    [Fact]
    public async Task AFailedCreateIsNamedAsTheCreate()
    {
        using var holepunch = Holepunch();
        holepunch.Stop.Cancel(stopWebsocketThread: false);

        ManagedHolepunchResult result = await holepunch.RunAsync(CancellationToken.None);

        Assert.Equal(ManagedHolepunchStage.Create, result.FailedAt);
        Assert.Null(result.Session);
    }

    /// <summary>
    /// No session comes back unless every stage ran, so a caller cannot get one that would throw on
    /// the first ask - which is PP556's guarantee, carried through the wiring.
    /// </summary>
    [Fact]
    public async Task NoSessionComesBackFromAFailedRun()
    {
        using var holepunch = Holepunch();
        holepunch.Stop.Cancel(stopWebsocketThread: false);

        Assert.Null((await holepunch.RunAsync(CancellationToken.None)).Session);
    }

    /// <summary>
    /// The four stages are the C's own division of what happens before session.c is entered - the
    /// create, the start, the ctrl hole, and recording what it produced.
    /// </summary>
    [Fact]
    public void TheStagesAreTheFourBeforeTheSessionThread()
        => Assert.Equal(
            [
                ManagedHolepunchStage.Create,
                ManagedHolepunchStage.Start,
                ManagedHolepunchStage.PunchCtrl,
                ManagedHolepunchStage.Record,
            ],
            Enum.GetValues<ManagedHolepunchStage>());

    /// <summary>
    /// PP561: the session punches the DATA hole on demand, which is what session.c asks for - so
    /// the punch given here is used twice, once now and once from inside the session.
    /// </summary>
    [Fact]
    public async Task TheCtrlHoleIsTakenAndTheDataOneIsNot()
    {
        var asked = new List<HolepunchPortType>();
        var session = new SequencedHolepunchSession(
            type => Task.FromResult(new HolepunchPunchResult(HolepunchPunchOutcome.Punched, null, [])));

        object socket = new();

        Assert.True(await session.TakeCtrlHoleAsync(() =>
        {
            asked.Add(HolepunchPortType.Ctrl);
            return Task.FromResult(
                (new HolepunchPunchResult(HolepunchPunchOutcome.Punched, null, []), (object?)socket));
        }));

        Assert.Equal([HolepunchPortType.Ctrl], asked);
        Assert.Same(socket, session.GetSocket(HolepunchPortType.Ctrl));
        Assert.Throws<InvalidOperationException>(() => session.GetSocket(HolepunchPortType.Data));

        session.Dispose();
    }
}
