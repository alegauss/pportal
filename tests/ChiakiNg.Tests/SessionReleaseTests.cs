using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP507, under PP340: the order chiaki_session_fini releases a session in.
///
/// Every step is a release, so nothing here fails and a wrong order is a use-after-free. The two
/// facts worth pinning are the mutex outliving its users and the null the last line is handed.
/// </summary>
public class SessionReleaseTests
{
    /// <summary>A local session skips the two PSN steps and runs the other seven.</summary>
    [Fact]
    public void ALocalSessionSkipsTheTwoPsnSteps()
    {
        IReadOnlyList<SessionReleaseStep> steps = SessionRelease.RunFor(isPsn: false);

        Assert.DoesNotContain(SessionReleaseStep.Rudp, steps);
        Assert.DoesNotContain(SessionReleaseStep.Holepunch, steps);
        Assert.Equal(SessionRelease.Order.Count - 2, steps.Count);
    }

    /// <summary>And a PSN session runs all nine, with the rudp before the holepunch.</summary>
    [Fact]
    public void APsnSessionRunsAllNineWithTheRudpFirst()
    {
        List<SessionReleaseStep> steps = [.. SessionRelease.RunFor(isPsn: true)];

        Assert.Equal(SessionRelease.Order, steps);
        Assert.True(
            steps.IndexOf(SessionReleaseStep.Rudp) < steps.IndexOf(SessionReleaseStep.Holepunch),
            "PP502's order");
    }

    /// <summary>
    /// The mutex is destroyed after everything that could take it, and only freeaddrinfo follows.
    ///
    /// A list with the mutex moved up answers no, which is what makes this a check rather than a
    /// restatement of the enum's declaration order.
    /// </summary>
    [Fact]
    public void TheMutexIsDestroyedAfterEveryStepThatCouldTakeIt()
    {
        Assert.True(SessionRelease.TheMutexOutlivesEveryUser(SessionRelease.Order));

        List<SessionReleaseStep> moved = [.. SessionRelease.Order];
        moved.Remove(SessionReleaseStep.StateMutex);
        moved.Insert(1, SessionReleaseStep.StateMutex);

        Assert.False(SessionRelease.TheMutexOutlivesEveryUser(moved));
    }

    /// <summary>Exactly one step holds the mutex, and it is the one at the top.</summary>
    [Fact]
    public void OneStepHoldsTheMutex()
    {
        Assert.Equal(SessionReleaseStep.FreeStringsUnderLock, SessionRelease.HoldsTheMutex);
        Assert.Equal(SessionReleaseStep.FreeStringsUnderLock, SessionRelease.Order[0]);

        // It is not a call the model spells, because it is two frees rather than a fini.
        Assert.False(SessionRelease.Calls.ContainsKey(SessionRelease.HoldsTheMutex));
    }

    /// <summary>
    /// The address list is null for a PSN session and not for a local one, and the call is the same
    /// either way.
    /// </summary>
    [Fact]
    public void TheAddressListIsNullForPsnAndTheCallIsUnchanged()
    {
        Assert.True(SessionRelease.AddrInfoIsNull(isPsn: true));
        Assert.False(SessionRelease.AddrInfoIsNull(isPsn: false));

        Assert.Contains(SessionReleaseStep.FreeAddrInfo, SessionRelease.RunFor(isPsn: true));
        Assert.Contains(SessionReleaseStep.FreeAddrInfo, SessionRelease.RunFor(isPsn: false));
    }

    /// <summary>
    /// THE DRIFT CHECK: the C still runs the steps in this order, holds the mutex for the two frees
    /// only, and calls freeaddrinfo unguarded.
    /// </summary>
    [Fact]
    public void TheCStillReleasesInThisOrder()
    {
        if (SessionHolepunchShape.AskingSource() is not { } source)
            return;

        string fini = Assert.IsType<string>(SessionReleaseSource.FiniBody(source));

        Assert.True(SessionReleaseSource.TheStepsRunInThisOrder(fini));
        Assert.True(SessionReleaseSource.OnlyTheTwoFreesAreUnderTheMutex(fini));
        Assert.True(SessionReleaseSource.TheAddrInfoCallIsUnguarded(fini));
        Assert.True(SessionReleaseSource.OnlyTheTwoPsnStepsAreGuarded(fini));
    }

    /// <summary>
    /// This is not PP336's subject, and the two are named apart so a reader does not merge them.
    ///
    /// SessionTeardown is how the session THREAD exits and which quit reason survives; this is what
    /// chiaki_session_fini does afterwards. Two files, two questions, one word in English.
    /// </summary>
    [Fact]
    public void ItIsADifferentQuestionFromTheThreadsExit()
    {
        Assert.NotEqual(typeof(SessionTeardown), typeof(SessionRelease));

        // The thread's exits are two; the release's steps are nine.
        Assert.Equal(2, Enum.GetValues<SessionExit>().Length);
        Assert.Equal(9, SessionRelease.Order.Count);
    }
}
