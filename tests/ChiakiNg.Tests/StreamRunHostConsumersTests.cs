using System.Reflection;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP707's second criterion: every member of the run's host answered, or on record as owed.
///
/// PP295 wrote the run and PP640 asserted its six orderings, and the host it walks has
/// implementations only in this project. So the sequence is right and nothing runs it - which is
/// why PP696, the commit that stops session.c asking, would leave the application with no stream.
///
/// THE CENSUS COMES BEFORE THE HOST, which is PP669's lesson one interface over: the frame path's
/// consumers were mapped by reflection before anything was deleted, so the two with no counterpart
/// were a decision rather than a surprise. A host written without this finds its gaps one compile
/// error at a time, and each gap is a piece of work rather than a stub.
///
/// BOTH DIRECTIONS. Every member of the interface has a row and every row names a member - so a
/// member added to the host fails here until somebody says what answers for it, and a row left
/// behind by a member that went fails too.
/// </summary>
public class StreamRunHostConsumersTests(ITestOutputHelper output)
{
    private static readonly Assembly App = typeof(IStreamRunHost).Assembly;

    /// <summary>The interface's members by name, properties counted once.</summary>
    private static IReadOnlyList<string> Declared()
        =>
        [
            .. typeof(IStreamRunHost)
                .GetMembers(BindingFlags.Public | BindingFlags.Instance)
                .Where(one => one is not MethodInfo { IsSpecialName: true })
                .Select(one => one.Name)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];

    /// <summary>
    /// THE CENSUS: the interface's members and the rows are the same set.
    ///
    /// Read from the type rather than counted, so a member added to the host is news here rather
    /// than a compile error in whatever tries to implement it later.
    /// </summary>
    [Fact]
    public void EveryMemberHasARowAndEveryRowAMember()
    {
        IReadOnlyList<string> declared = Declared();
        string[] rows = [.. StreamRunHostConsumers.Members.Select(one => one.Member).Order(StringComparer.Ordinal)];

        output.WriteLine($"{declared.Count} member(s), {rows.Length} row(s)");

        Assert.Empty(declared.Except(rows, StringComparer.Ordinal));
        Assert.Empty(rows.Except(declared, StringComparer.Ordinal));

        // PP271: a reader that found no members would satisfy both of those.
        Assert.NotEmpty(declared);
    }

    /// <summary>Each member is judged once, so a row cannot be two answers about one thing.</summary>
    [Fact]
    public void EachMemberIsJudgedOnce()
        => Assert.Equal(
            StreamRunHostConsumers.Members.Count,
            StreamRunHostConsumers.Members.Select(one => one.Member).Distinct(StringComparer.Ordinal).Count());

    /// <summary>
    /// Every counterpart resolves, and the member it names exists on it.
    ///
    /// The half that runs anywhere: the mapping is a claim about this assembly, so a counterpart
    /// renamed away fails before any file is read. A null is not checked here - that is the next
    /// test's subject and is a decision rather than an absence.
    /// </summary>
    [Fact]
    public void EveryCounterpartResolves()
    {
        foreach (HostMember member in StreamRunHostConsumers.Members)
        {
            if (member.Answer is not { } counterpart)
                continue;

            Type? type = App.GetType(counterpart.FullName);
            Assert.True(type is not null, $"{member.Member}: {counterpart.FullName} does not resolve");

            if (counterpart.Member is { } named)
            {
                Assert.True(
                    type.GetMember(named).Length > 0,
                    $"{member.Member}: {counterpart.FullName} has no member {named}");
            }
        }
    }

    /// <summary>
    /// TWO ARE OWED, AND THEY ARE NAMED. Congestion control and the feedback sender.
    ///
    /// The answer this criterion exists to produce, and it is a small number for a reason worth
    /// stating: almost everything a real host needs already exists, so the work between here and a
    /// managed run is two pieces rather than twenty-six.
    ///
    /// Asserted as the SET rather than as a count. A third arriving is a decision somebody takes,
    /// and one of these two being answered should lower this list in the same commit.
    /// </summary>
    [Fact]
    public void TheOwedMembersAreTheseAndNoOthers()
    {
        output.WriteLine(string.Join(", ", StreamRunHostConsumers.Owed));

        Assert.Equal(
            ["FiniFeedbackSender", "StartCongestionControl", "StartFeedbackSender", "StopCongestionControl"],
            StreamRunHostConsumers.Owed);
    }

    /// <summary>
    /// And the two owed things are two subsystems, not four members.
    ///
    /// Congestion control's start and stop are one thread, and the feedback sender's init and fini
    /// are one object. Counting the members would say four pieces of work where there are two, which
    /// is the sort of number a plan gets made from.
    /// </summary>
    [Fact]
    public void TheFourOwedMembersAreTwoSubsystems()
    {
        string[] congestion = [.. StreamRunHostConsumers.Owed.Where(one => one.Contains("Congestion", StringComparison.Ordinal))];
        string[] feedback = [.. StreamRunHostConsumers.Owed.Where(one => one.Contains("FeedbackSender", StringComparison.Ordinal))];

        Assert.Equal(2, congestion.Length);
        Assert.Equal(2, feedback.Length);
        Assert.Equal(StreamRunHostConsumers.Owed.Count, congestion.Length + feedback.Length);
    }

    /// <summary>Every row says why, because a mapping with no reason is a table.</summary>
    [Fact]
    public void EveryRowGivesAReason()
        => Assert.All(
            StreamRunHostConsumers.Members,
            one => Assert.False(string.IsNullOrWhiteSpace(one.Why)));

    /// <summary>
    /// The host still has no implementation outside this project, which is what PP707 is about.
    ///
    /// The finding as a check. If somebody writes one in app/, this goes red - and that is the
    /// commit where PP707's first criterion starts being answerable rather than a plan.
    /// </summary>
    [Fact]
    public void TheHostIsStillImplementedOnlyByTests()
    {
        Type[] inApp =
        [
            .. App.GetTypes().Where(one => typeof(IStreamRunHost).IsAssignableFrom(one) && one.IsClass),
        ];

        output.WriteLine(inApp.Length == 0 ? "none in app/" : string.Join(", ", inApp.Select(one => one.Name)));

        Assert.Empty(inApp);

        // And this project has at least one, or the run above is asserted by nobody.
        Assert.Contains(
            typeof(StreamRunHostConsumersTests).Assembly.GetTypes(),
            one => typeof(IStreamRunHost).IsAssignableFrom(one) && one.IsClass);
    }
}
