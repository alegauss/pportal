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
    /// NOTHING IS OWED, WHICH IS PP707'S SECOND CRITERION MET.
    ///
    /// The answer this criterion exists to produce. Most of what a real host needs already existed,
    /// so the work between the census and here was four subsystems rather than twenty-six members,
    /// and PP714, PP719, PP723 and PP727 wrote them one commit at a time.
    ///
    /// Asserted as the SET rather than as a count, and it stays: a member added to the host with no
    /// counterpart lands back in this list, and the assertion is that nobody left one there.
    /// </summary>
    [Fact]
    public void NothingIsOwed()
    {
        output.WriteLine(
            StreamRunHostConsumers.Owed.Count == 0 ? "none" : string.Join(", ", StreamRunHostConsumers.Owed));

        Assert.Empty(StreamRunHostConsumers.Owed);
        Assert.Empty(StreamRunHostConsumers.OwedSubsystems);
    }

    /// <summary>
    /// And the four that were owed are each answered by a member, not by the list having been emptied.
    ///
    /// The direction that would otherwise be missing. Deleting a row, or answering it with a type
    /// alone, empties the list above just as well as writing the subsystem does - which is the
    /// failure PP712 caught the first time, when SendBig was reported as answered by a builder with
    /// no BIG in it.
    /// </summary>
    [Fact]
    public void EachSubsystemThatWasOwedIsAnsweredByAMember()
    {
        string[] answered =
        [
            .. StreamRunHostConsumers.Members
                .Where(one => one.How == HostAnswer.Answered && one.Answer is { Member: not null })
                .Select(one => $"{one.Answer!.Value.Type}.{one.Answer!.Value.Member}"),
        ];

        output.WriteLine(string.Join(", ", answered.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)));

        Assert.Contains($"{nameof(ManagedCongestionControl)}.{nameof(ManagedCongestionControl.Start)}", answered);
        Assert.Contains($"{nameof(ManagedSessionEvents)}.{nameof(ManagedSessionEvents.SendConnected)}", answered);
        Assert.Contains($"{nameof(ManagedFeedbackSender)}.{nameof(ManagedFeedbackSender.Start)}", answered);
        Assert.Contains($"{nameof(BigMessage)}.{nameof(BigMessage.Encode)}", answered);
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
