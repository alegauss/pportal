using ChiakiNg.Native;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP717: what a controller change becomes, and the C's own text as the thing it answers to.
///
/// Under PP707, whose host owes the sender this feeds. The recorder is static in feedbacksender.c
/// so there is no oracle; the decisions are read out of the source and the bytes go out through
/// PP676's formatters, which do have one.
/// </summary>
public class FeedbackHistoryRecorderTests(ITestOutputHelper output)
{
    private static ChiakiControllerTouch Finger(int id, ushort x, ushort y) => new(x, y, id);

    /// <summary>Nothing moved, so nothing is sent. The common case, sixty times a second.</summary>
    [Fact]
    public void AnUnchangedPadProducesNothing()
    {
        Assert.Empty(FeedbackHistoryRecorder.Record(PadSnapshot.Idle, PadSnapshot.Idle));
        Assert.False(FeedbackHistoryRecorder.Dirties(PadSnapshot.Idle, PadSnapshot.Idle));
    }

    /// <summary>A digital button is 0xff down and 0 up, and nothing between.</summary>
    [Fact]
    public void ADigitalButtonIsAllOnesOrNothing()
    {
        PadSnapshot down = PadSnapshot.Idle with { Buttons = ChiakiControllerButton.Cross };

        Assert.Equal(
            [HistoryEvent.Press(ChiakiControllerButton.Cross, 0xff)],
            FeedbackHistoryRecorder.Record(PadSnapshot.Idle, down));

        Assert.Equal(
            [HistoryEvent.Press(ChiakiControllerButton.Cross, 0)],
            FeedbackHistoryRecorder.Record(down, PadSnapshot.Idle));
    }

    /// <summary>
    /// The sixteen buttons come out lowest bit first, which the ring then reverses.
    ///
    /// Asserted as the whole sequence rather than as a set: the order the diff produces them in is
    /// the order the console reads them in backwards, so a walk from the top would put a chord's
    /// events on the wire in the wrong sequence.
    /// </summary>
    [Fact]
    public void ButtonsComeOutLowestBitFirst()
    {
        PadSnapshot chord = PadSnapshot.Idle with
        {
            Buttons = ChiakiControllerButton.Ps | ChiakiControllerButton.Cross | ChiakiControllerButton.L1,
        };

        IReadOnlyList<HistoryEvent> events = FeedbackHistoryRecorder.Record(PadSnapshot.Idle, chord);
        output.WriteLine(string.Join(", ", events.Select(one => one.Button)));

        Assert.Equal(
            [ChiakiControllerButton.Cross, ChiakiControllerButton.L1, ChiakiControllerButton.Ps],
            events.Select(one => one.Button));
    }

    /// <summary>
    /// A trigger carries its LEVEL, so half-held and fully-held are different events.
    ///
    /// The assertion that fails for a port treating L2 as a bit: both of these would be 0xff, and
    /// the console would be told a squeeze it never got.
    /// </summary>
    [Fact]
    public void ATriggerCarriesItsLevelRatherThanAPressedByte()
    {
        PadSnapshot half = PadSnapshot.Idle with { L2 = 60 };
        PadSnapshot full = PadSnapshot.Idle with { L2 = 200 };

        Assert.Equal(
            [HistoryEvent.Press(ChiakiControllerButton.L2, 60)],
            FeedbackHistoryRecorder.Record(PadSnapshot.Idle, half));

        // And a change BETWEEN two held levels is an event, which a bit comparison would miss.
        Assert.Equal(
            [HistoryEvent.Press(ChiakiControllerButton.L2, 200)],
            FeedbackHistoryRecorder.Record(half, full));
    }

    /// <summary>Both triggers, and R2 comes last in the walk.</summary>
    [Fact]
    public void TheTriggersAreLastAndInThatOrder()
    {
        PadSnapshot both = PadSnapshot.Idle with { L2 = 10, R2 = 20, Buttons = ChiakiControllerButton.Ps };

        IReadOnlyList<HistoryEvent> events = FeedbackHistoryRecorder.Record(PadSnapshot.Idle, both);

        Assert.Equal(
            [ChiakiControllerButton.Ps, ChiakiControllerButton.L2, ChiakiControllerButton.R2],
            events.Select(one => one.Button));
    }

    /// <summary>A finger arriving is a down event at its position.</summary>
    [Fact]
    public void AFingerArrivingIsADownEvent()
    {
        PadSnapshot touched = PadSnapshot.Idle with { Slot0 = Finger(3, 500, 400) };

        Assert.Equal(
            [HistoryEvent.Touch(true, 3, 500, 400)],
            FeedbackHistoryRecorder.Record(PadSnapshot.Idle, touched));
    }

    /// <summary>A finger that only moved is an event too, which is what makes a drag reach the console.</summary>
    [Fact]
    public void AFingerMovingIsADownEventAtTheNewPosition()
    {
        PadSnapshot was = PadSnapshot.Idle with { Slot0 = Finger(3, 500, 400) };
        PadSnapshot now = PadSnapshot.Idle with { Slot0 = Finger(3, 512, 400) };

        Assert.Equal(
            [HistoryEvent.Touch(true, 3, 512, 400)],
            FeedbackHistoryRecorder.Record(was, now));
    }

    /// <summary>A finger leaving reports the OLD id at the OLD position, not the empty slot's zeros.</summary>
    [Fact]
    public void AFingerLeavingReportsWhereItWas()
    {
        PadSnapshot was = PadSnapshot.Idle with { Slot0 = Finger(3, 500, 400) };

        Assert.Equal(
            [HistoryEvent.Touch(false, 3, 500, 400)],
            FeedbackHistoryRecorder.Record(was, PadSnapshot.Idle));
    }

    /// <summary>
    /// ONE FINGER REPLACING ANOTHER IN A SLOT IS ONE EVENT, not two.
    ///
    /// The else that PP717 exists to pin. The old finger's release goes out and the new finger's
    /// arrival does not - it is reported on the next change instead, because the branch that would
    /// have emitted it is the alternative to the one that ran.
    ///
    /// A port written from a description emits both, and its console sees a press the client never
    /// sent in that packet.
    /// </summary>
    [Fact]
    public void AFingerReplacedInItsSlotEmitsOnlyTheRelease()
    {
        PadSnapshot was = PadSnapshot.Idle with { Slot0 = Finger(3, 500, 400) };
        PadSnapshot now = PadSnapshot.Idle with { Slot0 = Finger(4, 100, 100) };

        IReadOnlyList<HistoryEvent> events = FeedbackHistoryRecorder.Record(was, now);
        output.WriteLine(string.Join(", ", events.Select(one => $"{(one.Down ? "down" : "up")} {one.PointerId}")));

        Assert.Equal([HistoryEvent.Touch(false, 3, 500, 400)], events);

        // And the arrival is reported on the NEXT change, from a state that now knows about it.
        Assert.Equal(
            [HistoryEvent.Touch(true, 4, 110, 100)],
            FeedbackHistoryRecorder.Record(now, now with { Slot0 = Finger(4, 110, 100) }));
    }

    /// <summary>Both slots are walked, slot 0 before slot 1.</summary>
    [Fact]
    public void BothSlotsAreWalkedInOrder()
    {
        PadSnapshot now = PadSnapshot.Idle with { Slot0 = Finger(1, 10, 10), Slot1 = Finger(2, 20, 20) };

        Assert.Equal(
            [HistoryEvent.Touch(true, 1, 10, 10), HistoryEvent.Touch(true, 2, 20, 20)],
            FeedbackHistoryRecorder.Record(PadSnapshot.Idle, now));
    }

    /// <summary>Touches come before buttons, which is the whole walk in one case.</summary>
    [Fact]
    public void TouchesComeBeforeButtonsWhichComeBeforeTheTriggers()
    {
        PadSnapshot now = new(ChiakiControllerButton.Cross, 90, 0, Finger(1, 10, 10), PadSnapshot.NoTouch);

        IReadOnlyList<HistoryEvent> events = FeedbackHistoryRecorder.Record(PadSnapshot.Idle, now);

        Assert.Equal(
            [HistoryEventKind.Touchpad, HistoryEventKind.Button, HistoryEventKind.Button],
            events.Select(one => one.Kind));

        Assert.Equal(ChiakiControllerButton.Cross, events[1].Button);
        Assert.Equal(ChiakiControllerButton.L2, events[2].Button);
    }

    /// <summary>Every event this produces serialises through PP676's formatters.</summary>
    [Fact]
    public void EveryEventProducedIsOneTheFormattersAccept()
    {
        PadSnapshot everything = new(
            (ChiakiControllerButton)0xffff, 44, 55, Finger(0, 1, 1), Finger(1, 1920, 942));

        IReadOnlyList<HistoryEvent> events = FeedbackHistoryRecorder.Record(PadSnapshot.Idle, everything);

        // Sixteen buttons, two triggers, two touches.
        Assert.Equal(20, events.Count);

        foreach (HistoryEvent one in events)
        {
            byte[] bytes = one.Serialise();
            Assert.InRange(bytes.Length, 2, FeedbackPayload.HistoryEventSizeMax);
        }
    }

    /// <summary>A snapshot read off a live state is the one the diff compares.</summary>
    [Fact]
    public void ASnapshotComesOffALiveState()
    {
        using var state = new ChiakiControllerState();
        state.Buttons = ChiakiControllerButton.Options;
        sbyte id = state.StartTouch(700, 300);

        PadSnapshot snapshot = PadSnapshot.From(state);
        output.WriteLine($"buttons {snapshot.Buttons}, slot0 {snapshot.Slot0}");

        Assert.Equal(ChiakiControllerButton.Options, snapshot.Buttons);
        Assert.Equal(id, snapshot.Slot0.Id);
        Assert.Equal(700, snapshot.Slot0.X);

        Assert.Contains(
            FeedbackHistoryRecorder.Record(PadSnapshot.Idle, snapshot),
            one => one.Kind == HistoryEventKind.Touchpad && one.Down);
    }

    /// <summary>Dirties agrees with Record, which is what the sender's flush turns on.</summary>
    [Fact]
    public void DirtiesAgreesWithWhetherAnythingCameOut()
    {
        PadSnapshot moved = PadSnapshot.Idle with { R2 = 1 };

        Assert.True(FeedbackHistoryRecorder.Dirties(PadSnapshot.Idle, moved));
        Assert.False(FeedbackHistoryRecorder.Dirties(moved, moved));
    }

    /// <summary>The C's function is there to be read, or this whole file is asserting nothing.</summary>
    [Fact]
    public void TheRecorderIsFoundInTheC()
    {
        string body = Assert.IsType<string>(FeedbackRecorderSource.Body());
        Assert.NotEmpty(body);
    }

    /// <summary>
    /// Every decision named is in the C, and the walk is in the order claimed.
    ///
    /// Both halves matter. The first fails when the C changes under the port; the second is the
    /// wire fact, because the ring formats newest first and reverses whatever order this produces.
    /// </summary>
    [Fact]
    public void EveryDecisionIsInTheCAndTheWalkIsInOrder()
    {
        string? body = FeedbackRecorderSource.Body();
        Assert.NotNull(body);

        // Every row, with nothing excused. A check that skipped one would be a row nobody re-reads.
        foreach (RecorderDecision decision in FeedbackRecorderSource.Decisions)
        {
            Assert.True(
                CCall.Happens(body, decision.InTheC),
                $"not in {FeedbackRecorderSource.Function}: {decision.InTheC}");
        }

        Assert.True(
            CCall.InOrder(body, [.. FeedbackRecorderSource.Walk]),
            "the walk is not touches, buttons, L2, R2");
    }

    /// <summary>Every decision says what would be wrong without it, and each is named once.</summary>
    [Fact]
    public void EveryDecisionIsNamedOnceAndGivesAReason()
    {
        Assert.All(
            FeedbackRecorderSource.Decisions,
            one =>
            {
                Assert.False(string.IsNullOrWhiteSpace(one.Why));
                Assert.False(string.IsNullOrWhiteSpace(one.Answers));
            });

        Assert.Equal(
            FeedbackRecorderSource.Decisions.Count,
            FeedbackRecorderSource.Decisions.Select(one => one.InTheC).Distinct(StringComparer.Ordinal).Count());
    }
}
