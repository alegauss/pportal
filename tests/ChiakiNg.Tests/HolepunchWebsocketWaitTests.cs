using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP541: the four facts that together make a failed websocket hang the session.
///
/// Each is harmless alone and they are only a defect in combination, which is why they are held
/// together rather than one per test: an untimed wait is fine when something always ends it, one
/// setter is fine when the failure paths report some other way, and a cancel that signals is fine
/// when the loop it wakes can leave.
///
/// These assert the C AS IT IS, not as it should be. A repair upstream turns them red, and that is
/// the point - PP107 records the same choice about its own two, for the same reason: the port's
/// drift checks assert the managed side matches lib/, so a local patch would leave them agreeing
/// with a libchiaki nobody else runs.
/// </summary>
public class HolepunchWebsocketWaitTests
{
    private static string? Source()
    {
        string? path = HolepunchWebsocketWait.Locate();
        return path is null ? null : File.ReadAllText(path);
    }

    /// <summary>One: the wait has no timeout.</summary>
    [Fact]
    public void TheWaitForTheWebsocketIsUntimed()
    {
        if (Source() is not { } source)
            return;

        Assert.True(HolepunchWebsocketWait.WaitIsUntimed(source),
            "session_create's wait for SESSION_STATE_WS_OPEN is no longer an untimed cond_wait - "
            + "if it grew a timeout, PP541 is repaired and this test should say so rather than fail");
    }

    /// <summary>Two: exactly one line can end it, and it is on the success path.</summary>
    [Fact]
    public void OnlyOneSiteSetsTheBitTheWaitTests()
    {
        if (Source() is not { } source)
            return;

        Assert.Equal(1, HolepunchWebsocketWait.SitesSettingTheBit(source));
    }

    /// <summary>
    /// Three: the thread's failure path sets nothing and signals nothing. This is the line that
    /// turns an untimed wait into a hang.
    /// </summary>
    [Fact]
    public void TheThreadsCleanupLeavesTheWaiterWithNothing()
    {
        if (Source() is not { } source)
            return;

        Assert.True(HolepunchWebsocketWait.CleanupLeavesTheWaiterStuck(source),
            "websocket_thread_func's cleanup now touches the bit or the condition - PP541 repaired");
    }

    /// <summary>
    /// Four: the cancel signals both conds, so the wait wakes and cannot leave. Asserted because
    /// it is the fact that makes the hang counter-intuitive: cancelling looks like it should work.
    /// </summary>
    [Fact]
    public void TheCancelSignalsTheConditionItCannotRelease()
    {
        if (Source() is not { } source)
            return;

        Assert.True(HolepunchWebsocketWait.CancelSignalsBothConds(source));
    }

    /// <summary>
    /// And the readers can tell a repair from the defect. Each is run against the shape a fix
    /// would have, because a check that cannot distinguish them holds nothing.
    /// </summary>
    [Fact]
    public void ARepairedShapeIsReadAsRepaired()
    {
        const string timedInstead = """
            while (!(session->state & SESSION_STATE_WS_OPEN))
            {
                err = chiaki_cond_timedwait(&session->state_cond, &session->state_mutex, 10000);
            }
            """;

        Assert.False(HolepunchWebsocketWait.WaitIsUntimed(timedInstead));

        const string cleanupThatSignals = """
            static void* websocket_thread_func(void *user) {
                goto cleanup;
            cleanup:
                curl_easy_cleanup(curl);
                session->ws_open = false;
                chiaki_cond_signal(&session->state_cond);
                return NULL;
            }
            """;

        Assert.False(HolepunchWebsocketWait.CleanupLeavesTheWaiterStuck(cleanupThatSignals));

        // And the shape as it stands reads as stuck, so the check is not simply always false.
        const string cleanupAsItIs = """
            static void* websocket_thread_func(void *user) {
                goto cleanup;
            cleanup:
                curl_easy_cleanup(curl);
                session->ws_open = false;

                return NULL;
            }
            """;

        Assert.True(HolepunchWebsocketWait.CleanupLeavesTheWaiterStuck(cleanupAsItIs));

        const string cancelSignallingOne = """
            chiaki_holepunch_main_thread_cancel(Session *session, bool stop_thread)
            {
                session->main_should_stop = true;
                chiaki_cond_signal(&session->notif_cond);
            }
            """;

        Assert.False(HolepunchWebsocketWait.CancelSignalsBothConds(cancelSignallingOne));
    }
}
