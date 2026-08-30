using System.Diagnostics;
using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP548: the adapter behind PP545's interface, over the pieces that run.
///
/// WHAT IS NOT ASSERTED HERE, said plainly: the two HTTP calls and the websocket connect need PSN
/// and a network, so nothing below performs one. What can be checked offline is everything either
/// side of them - the order the steps refuse in, and the wait over the queue PushChannel fills,
/// which is the piece the create actually finishes on.
/// </summary>
public class LiveHolepunchCreateStepsTests
{
    private static LiveHolepunchCreateSteps Steps() => new("Authorization: Bearer t", "ctx");

    /// <summary>
    /// The open refuses before a lookup has produced a host. That ordering is the C's - the thread
    /// is created after the fqdn is known - and a step that opened against a null host would fail
    /// later and less clearly.
    /// </summary>
    [Fact]
    public async Task TheOpenRefusesBeforeTheLookupHasRun()
    {
        using var steps = Steps();

        Assert.Null(steps.Fqdn);
        Assert.False(await steps.OpenWebSocketAsync(CancellationToken.None));
    }

    /// <summary>And the wait refuses when no connect was started, rather than waiting out its bound.</summary>
    [Fact]
    public async Task TheWaitRefusesWhenNothingWasStarted()
    {
        using var steps = Steps();

        var clock = Stopwatch.StartNew();
        bool opened = await steps.WaitForOpenAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
        clock.Stop();

        Assert.False(opened);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(1), "it waited instead of refusing");
    }

    /// <summary>Either of the two notifications finishes the create's wait, and nothing else does.</summary>
    [Fact]
    public void OnlyTheTwoCreationNotificationsFinishTheWait()
    {
        Assert.True(LiveHolepunchCreateSteps.Finishes(new(PushNotificationType.SessionCreated, "{}")));
        Assert.True(LiveHolepunchCreateSteps.Finishes(new(PushNotificationType.MemberCreated, "{}")));

        foreach (PushNotificationType other in new[]
        {
            PushNotificationType.Unknown,
            PushNotificationType.MemberDeleted,
            PushNotificationType.CustomData1Updated,
            PushNotificationType.SessionMessageCreated,
            PushNotificationType.SessionDeleted,
        })
        {
            Assert.False(LiveHolepunchCreateSteps.Finishes(new(other, "{}")));
        }
    }

    /// <summary>
    /// A notification already on the queue ends the wait at once - the queue PushChannel fills is
    /// the one the wait reads, which is the whole join this adapter exists to make.
    /// </summary>
    [Fact]
    public async Task ANotificationOnTheQueueEndsTheWait()
    {
        using var steps = Steps();
        steps.Queue.Enqueue(new QueuedNotification(PushNotificationType.SessionCreated, "{}"));

        Assert.True(await steps.WaitForCreatedAsync(TimeSpan.FromSeconds(5), CancellationToken.None));
    }

    /// <summary>
    /// PP558: AND IT TAKES WHAT IT HANDLED OFF THE QUEUE, which is what the C does.
    ///
    /// This test used to assert the opposite, on the reasoning that the C's wait is a cursor walk
    /// so a notification stays for whatever reads next. Half right: the wait does rescan from the
    /// front, but every one of the C's loops calls clear_notification at the end of each pass. What
    /// the create handled is not the start's to find.
    /// </summary>
    [Fact]
    public async Task TheWaitTakesWhatItHandled()
    {
        using var steps = Steps();
        steps.Queue.Enqueue(new QueuedNotification(PushNotificationType.MemberCreated, "{}"));

        Assert.True(await steps.WaitForCreatedAsync(TimeSpan.FromSeconds(5), CancellationToken.None));

        Assert.Equal(0, steps.Queue.Count);
    }

    /// <summary>And what it did not handle is left where it was.</summary>
    [Fact]
    public async Task TheWaitLeavesWhatItDidNotHandle()
    {
        using var steps = Steps();
        steps.Queue.Enqueue(new QueuedNotification(PushNotificationType.CustomData1Updated, "{}"));
        steps.Queue.Enqueue(new QueuedNotification(PushNotificationType.SessionCreated, "{}"));

        Assert.True(await steps.WaitForCreatedAsync(TimeSpan.FromSeconds(5), CancellationToken.None));

        // The custom data is the start's, and the start is about to want it.
        Assert.Equal(1, steps.Queue.Count);
        Assert.Equal(PushNotificationType.CustomData1Updated, steps.Queue.Front!.Type);
    }

    /// <summary>
    /// An empty queue runs out the deadline and answers false rather than waiting for ever, which
    /// is the create's own bound and not the websocket one.
    /// </summary>
    [Fact]
    public async Task AnEmptyQueueTimesOut()
    {
        using var steps = Steps();

        var clock = Stopwatch.StartNew();
        bool created = await steps.WaitForCreatedAsync(TimeSpan.FromMilliseconds(120), CancellationToken.None);
        clock.Stop();

        Assert.False(created);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), "the deadline was not honoured");
    }
}
