using System.Diagnostics;
using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP549: the adapter behind PP546's interface, over the pieces that run.
///
/// WHAT IS NOT ASSERTED, as in PP548: the wakeup and the start call need PSN, so nothing below
/// performs one. What can be checked offline is the identity check - which is the reason this
/// adapter exists rather than an incidental part of it - and the wait over the queue PushChannel
/// fills.
/// </summary>
public class LiveHolepunchStartStepsTests
{
    private const string Console =
        "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static LiveHolepunchStartSteps Steps(NotificationQueue? queue = null, string? expected = null)
        => new("Authorization: Bearer t", "sid", queue ?? new NotificationQueue(), expected ?? Console);

    private static string Member(string uid)
        => "{\"body\":{\"data\":{\"members\":[{\"deviceUniqueId\":\"" + uid + "\"}]}}}";

    /// <summary>
    /// THE CONSOLE THAT JOINED IS THE ONE ASKED FOR - and a different one is named as such.
    ///
    /// PP257 found the C returns success for both, because the branch that makes this check writes
    /// a shadowed variable. PP546 declared it would report the failure instead; this is the piece
    /// that decides what the failure IS, so without it that departure reports nothing.
    /// </summary>
    [Fact]
    public void TheWrongConsoleIsNamedAndTheRightOnePasses()
    {
        Assert.Equal(StartFailure.None, Steps().CheckIdentity(Member(Console)));

        Assert.Equal(
            StartFailure.WrongConsole,
            Steps().CheckIdentity(Member("fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210")));
    }

    /// <summary>Case is not identity - the C converts both to the same bytes before comparing.</summary>
    [Fact]
    public void TheComparisonIgnoresCase()
        => Assert.Equal(StartFailure.None, Steps().CheckIdentity(Member(Console.ToUpperInvariant())));

    /// <summary>
    /// The three ways the id itself is wrong, each answered by its own name and in the C's order:
    /// the field, then its length, then whether it converts.
    /// </summary>
    [Theory]
    [InlineData("""{"body":{"data":{"members":[{}]}}}""", StartFailure.MemberFieldMissing)]
    [InlineData("""{"body":{"data":{"members":[]}}}""", StartFailure.MemberFieldMissing)]
    [InlineData("""{"body":{"data":{}}}""", StartFailure.MemberFieldMissing)]
    [InlineData("""{"body":{"data":{"members":[{"deviceUniqueId":42}]}}}""", StartFailure.MemberFieldMissing)]
    [InlineData("not json at all", StartFailure.MemberFieldMissing)]
    [InlineData("""{"body":{"data":{"members":[{"deviceUniqueId":"abcd"}]}}}""", StartFailure.MemberIdWrongLength)]
    public void EachWayTheIdIsWrongHasItsOwnName(string payload, StartFailure expected)
        => Assert.Equal(expected, Steps().CheckIdentity(payload));

    /// <summary>
    /// Sixty-four characters that are not hex are NotHex and not WrongConsole, which is the
    /// distinction the C makes and then loses. Both write its shadowed variable, so a test that
    /// only checked "some failure" would not tell PP257's two apart.
    /// </summary>
    [Fact]
    public void SixtyFourNonHexCharactersAreNotHex()
    {
        string uid = new('z', SessionStart.DeviceIdTextLength);

        Assert.Equal(SessionStart.DeviceIdTextLength, uid.Length);
        Assert.Equal(StartFailure.MemberIdNotHex, Steps().CheckIdentity(Member(uid)));
    }

    /// <summary>The pointer is the C's, step for step.</summary>
    [Fact]
    public void ThePointerIsTheCs()
        => Assert.Equal(
            ["body", "data", "members", "0", "deviceUniqueId"],
            LiveHolepunchStartSteps.MemberPointer);

    /// <summary>
    /// Notifications already on the queue are read at once, and PP558: taken off it.
    ///
    /// This asserted that nothing was removed, which was wrong - the C's start loop clears each
    /// notification at the end of every pass. PP557 changed what it takes to finish: the member
    /// alone used to end the wait, and both halves are needed.
    /// </summary>
    [Fact]
    public async Task WhatIsOnTheQueueEndsTheWaitAndIsTaken()
    {
        var queue = new NotificationQueue();
        queue.Enqueue(new QueuedNotification(PushNotificationType.MemberCreated, Member(Console)));
        queue.Enqueue(new QueuedNotification(
            PushNotificationType.CustomData1Updated, CustomData(GoodCustomData)));

        StartFailure? failure = await Steps(queue)
            .WaitForMemberAsync(TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.Equal(StartFailure.None, failure);
        Assert.Equal(0, queue.Count);
    }

    /// <summary>
    /// NOBODY JOINED IS NULL, not a failure - and that is what makes PP546's HostDown reachable.
    ///
    /// The outcome was declared when the sequence shipped and nothing could produce it: every
    /// answer the wait could give was one of PP257's names for a console that DID join. A console
    /// that never joins failed no check.
    /// </summary>
    [Fact]
    public async Task NobodyJoiningIsNullAndReachesHostDown()
    {
        var clock = Stopwatch.StartNew();
        StartFailure? failure = await Steps()
            .WaitForMemberAsync(TimeSpan.FromMilliseconds(120), CancellationToken.None);
        clock.Stop();

        Assert.Null(failure);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), "the deadline was not honoured");
    }

    /// <summary>
    /// PP552: A DEAD SOCKET ENDS THE WAIT, rather than thirty seconds of polling a queue nothing
    /// can fill.
    ///
    /// PP548's create wait has always checked this and PP549's did not. Asserted by the clock as
    /// well as the answer, because the answer alone is the same either way - the bug was never a
    /// wrong result, only a slow one.
    /// </summary>
    [Fact]
    public async Task ADeadChannelEndsTheWaitEarly()
    {
        var steps = new LiveHolepunchStartSteps(
            "Authorization: Bearer t", "sid", new NotificationQueue(), Console)
        {
            ChannelEnded = () => true,
        };

        var clock = Stopwatch.StartNew();
        StartFailure? failure = await steps.WaitForMemberAsync(
            TimeSpan.FromSeconds(30), CancellationToken.None);
        clock.Stop();

        Assert.Null(failure);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5),
            $"it served out the deadline: {clock.Elapsed}");
    }

    private static string CustomData(string text)
        => "{\"body\":{\"data\":{\"customData1\":\"" + text + "\"}}}";

    private const string GoodCustomData = "0123456789abcdef0123456789abcdef";

    /// <summary>
    /// PP557: BOTH HALVES, which is what the C's loop runs until.
    ///
    /// The C handles two notification types and this handled one, so a member joining ended the
    /// wait and reported success - while SessionStart.Finished, which is what "started" means, also
    /// wants the custom data. The member alone is not a started session.
    /// </summary>
    [Fact]
    public async Task TheMemberAloneIsNotAStartedSession()
    {
        var queue = new NotificationQueue();
        queue.Enqueue(new QueuedNotification(PushNotificationType.MemberCreated, Member(Console)));

        LiveHolepunchStartSteps steps = Steps(queue);

        Assert.Null(await steps.WaitForMemberAsync(
            TimeSpan.FromMilliseconds(150), CancellationToken.None));

        // The half that did arrive is remembered, so the other one finishes it.
        Assert.True(steps.Seen.HasFlag(SessionStateFlags.ConsoleJoined));
        Assert.False(SessionStart.Finished(steps.Seen));

        queue.Enqueue(new QueuedNotification(PushNotificationType.CustomData1Updated, CustomData(GoodCustomData)));

        Assert.Equal(
            StartFailure.None,
            await steps.WaitForMemberAsync(TimeSpan.FromSeconds(5), CancellationToken.None));
        Assert.True(SessionStart.Finished(steps.Seen));
    }

    /// <summary>Both arriving together finishes it in one wait, in either order.</summary>
    [Fact]
    public async Task BothHalvesInOneWaitFinishIt()
    {
        var queue = new NotificationQueue();
        queue.Enqueue(new QueuedNotification(PushNotificationType.CustomData1Updated, CustomData(GoodCustomData)));
        queue.Enqueue(new QueuedNotification(PushNotificationType.MemberCreated, Member(Console)));

        LiveHolepunchStartSteps steps = Steps(queue);

        Assert.Equal(
            StartFailure.None,
            await steps.WaitForMemberAsync(TimeSpan.FromSeconds(5), CancellationToken.None));
    }

    /// <summary>
    /// PP557: the three CustomData failures, each producible for the first time.
    ///
    /// PP257 declared them and nothing looked at this notification at all, so like PP549's HostDown
    /// they were named and unreachable.
    /// </summary>
    [Theory]
    [InlineData("{\"body\":{\"data\":{}}}", StartFailure.CustomDataFieldMissing)]
    [InlineData("{\"body\":{\"data\":{\"customData1\":7}}}", StartFailure.CustomDataFieldMissing)]
    [InlineData("{\"body\":{\"data\":{\"customData1\":\"abcd\"}}}", StartFailure.CustomDataWrongLength)]
    [InlineData("{\"body\":{\"data\":{\"customData1\":\"zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz\"}}}",
        StartFailure.CustomDataUndecodable)]
    public void EachCustomDataFailureCanNowHappen(string payload, StartFailure expected)
        => Assert.Equal(expected, Steps().CheckCustomData(payload));

    /// <summary>A custom data failure stops the wait, which is the C's break out of its loop.</summary>
    [Fact]
    public async Task ACustomDataFailureStopsTheWait()
    {
        var queue = new NotificationQueue();
        queue.Enqueue(new QueuedNotification(PushNotificationType.MemberCreated, Member(Console)));
        queue.Enqueue(new QueuedNotification(
            PushNotificationType.CustomData1Updated, CustomData("abcd")));

        Assert.Equal(
            StartFailure.CustomDataWrongLength,
            await Steps(queue).WaitForMemberAsync(TimeSpan.FromSeconds(5), CancellationToken.None));
    }

    /// <summary>
    /// A half already accounted for is not re-checked, which matters because the queue never
    /// drains - the C's own queue is dequeued only at teardown.
    /// </summary>
    [Fact]
    public void AHalfAlreadySeenIsNotCheckedAgain()
    {
        LiveHolepunchStartSteps steps = Steps();

        Assert.Equal(StartFailure.None, steps.Consider(
            new QueuedNotification(PushNotificationType.MemberCreated, Member(Console))));
        Assert.True(steps.Seen.HasFlag(SessionStateFlags.ConsoleJoined));

        // A second member notification naming someone else is skipped, not called a wrong console.
        Assert.Equal(StartFailure.None, steps.Consider(new QueuedNotification(
            PushNotificationType.MemberCreated,
            Member("fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210"))));
    }

    /// <summary>Only a member notification is read - the create's own are left for what wants them.</summary>
    [Fact]
    public async Task OtherNotificationsDoNotEndTheWait()
    {
        var queue = new NotificationQueue();
        queue.Enqueue(new QueuedNotification(PushNotificationType.SessionCreated, Member(Console)));

        Assert.Null(await Steps(queue).WaitForMemberAsync(
            TimeSpan.FromMilliseconds(120), CancellationToken.None));
    }

    /// <summary>The C's two guards: created, and not already finished.</summary>
    [Fact]
    public void TheGuardsAreCreatedAndNotAlreadyStarted()
    {
        var steps = Steps();

        Assert.False(steps.PreconditionsHold(out bool created));
        Assert.False(created);

        steps.State = SessionStateFlags.Created;
        Assert.True(steps.PreconditionsHold(out created));
        Assert.True(created);

        steps.State |= SessionStateFlags.ConsoleJoined | SessionStateFlags.CustomData1Received;
        Assert.False(steps.PreconditionsHold(out created));
        Assert.True(created);
    }

    /// <summary>
    /// Neither call is attempted without what it needs, rather than reaching PSN to be refused.
    /// </summary>
    [Fact]
    public async Task NeitherCallRunsWithoutItsBody()
    {
        var steps = Steps();

        Assert.False(await steps.StartSessionAsync(CancellationToken.None));
        Assert.False(await steps.WakeUpPs4Async(CancellationToken.None));
    }
}
