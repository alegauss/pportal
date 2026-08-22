using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP262: what the queue owns.
///
/// <see cref="TheEnqueueDoesNotMakeANodeTheLast"/> carries the task: the property the queue depends
/// on is established somewhere else entirely.
/// </summary>
public class NotificationOwnershipTests
{
    /// <summary>
    /// THE FINDING. The forward link is cleared by the constructor, and the enqueue relies on that
    /// without stating it.
    /// </summary>
    [Fact]
    public void TheEnqueueDoesNotMakeANodeTheLast()
    {
        Assert.Equal(LinkClearedBy.Constructor, NotificationOwnership.ClearsTheLink);
        Assert.False(NotificationOwnership.EnqueueMakesItLast);
    }

    /// <summary>Three things released, in that order, and the node last.</summary>
    [Fact]
    public void ThreeThingsAreReleasedAndTheNodeIsLast()
    {
        Assert.Equal(
            [Released.Document, Released.TextBuffer, Released.Node],
            NotificationOwnership.ReleasedInOrder);

        Assert.Equal(Released.Node, NotificationOwnership.ReleasedInOrder[^1]);
    }

    /// <summary>And the two that are not the node are written to before it goes.</summary>
    [Fact]
    public void TheOtherTwoAreWrittenBeforeTheFree()
    {
        Assert.True(NotificationOwnership.WrittenBeforeTheFree(Released.Document));
        Assert.True(NotificationOwnership.WrittenBeforeTheFree(Released.TextBuffer));
        Assert.False(NotificationOwnership.WrittenBeforeTheFree(Released.Node));
    }

    /// <summary>A dequeue tells the caller nothing, which the port's own does not copy.</summary>
    [Fact]
    public void ADequeueTellsTheCallerNothing()
    {
        Assert.False(NotificationOwnership.DequeueReports);

        // PP212's answers, which is the deliberate difference.
        var queue = new NotificationQueue();
        Assert.False(queue.Dequeue());

        queue.Enqueue(new QueuedNotification(PushNotificationType.SessionMessageCreated, "{}"));
        Assert.True(queue.Dequeue());
    }

    /// <summary>The remover takes the first match and no others.</summary>
    [Fact]
    public void TheRemoverTakesTheFirstMatchOnly()
    {
        // "axaa" without its one x is "aaa" - three letters, not two.
        Assert.Equal("aaa", NotificationOwnership.RemoveFirst("axaa", "x"));

        // And with two matches, only the first goes: "axa|axa" loses the leading "xa".
        Assert.Equal("a|axa", NotificationOwnership.RemoveFirst("axa|axa", "xa"));

        // No match, nothing changes.
        Assert.Equal("abc", NotificationOwnership.RemoveFirst("abc", "z"));
    }

    /// <summary>
    /// Two calls, one per scheme - which is what PP239 measured as a scheme being removed from
    /// wherever it appears.
    /// </summary>
    [Fact]
    public void TwoCallsAreWhatStripsBothSchemes()
    {
        Assert.Equal(
            "example.net/redirect?to=example.org",
            NotificationOwnership.StripSchemes("https://example.net/redirect?to=http://example.org"));

        // The same answer PP239's own stripping gives.
        Assert.Equal(
            Ps4Wakeup.StripScheme("https://example.net/redirect?to=http://example.org"),
            NotificationOwnership.StripSchemes("https://example.net/redirect?to=http://example.org"));

        // And one call alone would leave the other behind.
        Assert.Equal(
            "example.net/redirect?to=http://example.org",
            NotificationOwnership.RemoveFirst(
                "https://example.net/redirect?to=http://example.org", "https://"));
    }

    /// <summary>Every rule above, still written the same way in the core it was read from.</summary>
    [Fact]
    public void TheOwnershipIsStillTheCores()
    {
        string? file = NotificationOwnershipSource.Locate();
        if (file is null)
            return;

        string core = File.ReadAllText(file);

        Assert.True(
            NotificationOwnershipSource.TheEnqueueStillNeverClearsTheLink(core),
            "the enqueue still leaves the forward link alone on both branches");
        Assert.True(
            NotificationOwnershipSource.TheConstructorStillClearsIt(core),
            "and the constructor still does it instead");

        Assert.True(
            NotificationOwnershipSource.TheDequeueStillReleasesThree(core),
            "the dequeue still releases three, in that order");
        Assert.True(
            NotificationOwnershipSource.TheDequeueStillWritesBeforeFreeing(core),
            "and still writes to the node before freeing it");
        Assert.True(
            NotificationOwnershipSource.AnEmptyDequeueStillSaysNothing(core),
            "an empty dequeue still says nothing");

        Assert.True(
            NotificationOwnershipSource.TheRemoverIsStillCalledOncePerScheme(core),
            "and the remover still takes one match, called once per scheme");
    }
}
