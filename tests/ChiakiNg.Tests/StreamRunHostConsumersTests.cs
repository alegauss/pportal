using System.Reflection;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP707's second criterion: every member of the run's host answered, owed, or needless.
///
/// PP295 wrote the run and PP640 asserted its six orderings, and the host it walks has
/// implementations only in this project. So the sequence is right and nothing runs it - which is
/// why PP696, the commit that stops session.c asking, would leave the application with no stream.
///
/// THE CENSUS COMES BEFORE THE HOST, which is PP669's lesson one interface over: the frame path's
/// consumers were mapped by reflection before anything was deleted, so the ones with no counterpart
/// were a decision rather than a surprise. A host written without this finds its gaps one compile
/// error at a time, and each gap is a piece of work rather than a stub.
///
/// BOTH DIRECTIONS. Every member of the interface has a row and every row names a member - so a
/// member added to the host fails here until somebody says what answers for it, and a row left
/// behind by a member that went fails too.
///
/// PP712: AND AN ANSWERED ROW NAMES A MEMBER. The first version let a counterpart be a type alone
/// and three rows took the option, so the census reported four members owed where seven are.
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
    /// Every counterpart resolves AND names a member that exists on it.
    ///
    /// PP712 is why the second half is not optional. The first version of this let a row name a
    /// type alone, and three took the option - SendBig pointed at a builder with no BIG, and the
    /// check passed because the TYPE resolved. A counterpart with no member is a claim about a
    /// namespace, and PP669's rule is that a mapping is not a call.
    /// </summary>
    [Fact]
    public void EveryCounterpartResolvesAndNamesAMemberThatExists()
    {
        foreach (HostMember member in StreamRunHostConsumers.Members)
        {
            if (member.How != HostAnswer.Answered)
            {
                Assert.Null(member.Answer);
                continue;
            }

            Counterpart counterpart = Assert.NotNull(member.Answer);

            Type? type = App.GetType(counterpart.FullName);
            Assert.True(type is not null, $"{member.Member}: {counterpart.FullName} does not resolve");

            string named = Assert.IsType<string>(counterpart.Member);

            Assert.True(
                type.GetMember(named).Length > 0,
                $"{member.Member}: {counterpart.FullName} has no member {named}");
        }
    }

    /// <summary>
    /// ONE IS OWED, AND IT IS NAMED.
    ///
    /// The answer this criterion exists to produce, and it is a small number for a reason worth
    /// stating: most of what a real host needs already existed, so the work between here and a
    /// managed run was four pieces rather than twenty-six, and three of them have landed.
    ///
    /// Asserted as the SET rather than as a count. One more arriving is a decision somebody takes,
    /// and one of these being answered should shorten this list in the same commit.
    /// </summary>
    [Fact]
    public void TheOwedMembersAreTheseAndNoOthers()
    {
        output.WriteLine(string.Join(", ", StreamRunHostConsumers.Owed));

        Assert.Equal(["SendBig"], StreamRunHostConsumers.Owed);
    }

    /// <summary>
    /// One member, one subsystem - and the count that matters is objects rather than members.
    ///
    /// PP712 moved this from two to four, because SendBig and SendConnected were reported as
    /// answered by types with no member doing either. PP714 wrote congestion control and took two
    /// members with it, a start and a stop being one thread. PP719 took a single member and was the
    /// largest of the three, since nothing managed raised a session event at all. PP723 took three
    /// at once: a sender's init, its fini and the counter lifted out of it are one object.
    /// </summary>
    [Fact]
    public void TheOwedMemberIsOneSubsystem()
    {
        output.WriteLine(string.Join(", ", StreamRunHostConsumers.OwedSubsystems));

        Assert.Single(StreamRunHostConsumers.OwedSubsystems);
        Assert.Single(StreamRunHostConsumers.Owed);
        Assert.Contains("SendBig", StreamRunHostConsumers.Owed);

        // PP714, PP719 and PP723: each of the three is gone from both lists, which is what shipping
        // one of these looks like from the census's side.
        Assert.DoesNotContain(
            StreamRunHostConsumers.Owed,
            one => one.Contains("Congestion", StringComparison.Ordinal)
                || one.Contains("FeedbackSender", StringComparison.Ordinal)
                || one is "SendConnected" or "LiftInputToWire");
    }

    /// <summary>
    /// And what the runtime makes needless is said so, rather than answered by a plausible type.
    ///
    /// PP712's other half. Three frees and two lock calls have no counterpart because a managed
    /// object is collected and a managed lock is the language's - and a row naming a type for
    /// either would be describing C# while looking like a mapping.
    /// </summary>
    [Fact]
    public void WhatTheRuntimeMakesNeedlessSaysSo()
    {
        string[] needless =
        [
            .. StreamRunHostConsumers.Members
                .Where(one => one.How == HostAnswer.NotNeeded)
                .Select(one => one.Member)
                .Order(StringComparer.Ordinal),
        ];

        output.WriteLine(string.Join(", ", needless));

        Assert.Equal(
            ["FreeAudioReceiver", "FreeHapticsReceiver", "FreeVideoReceiver", "Lock", "Unlock"],
            needless);
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
