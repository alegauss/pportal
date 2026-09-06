using System.Reflection;
using ChiakiNg.Native;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP722: the eight events outside the frame path, counted against the C that raises them.
///
/// PP719 answered nine and named the rest. This holds the census of all seventeen to lib/src: which
/// file assigns each type, which the port answers, and that exactly one member is raised by nothing.
/// PP712's rule is the reason it is a census first - the count that matters is subsystems, and the
/// eight are four pieces of work rather than eight.
/// </summary>
public class SessionEventRaisersTests(ITestOutputHelper output)
{
    private static readonly Assembly App = typeof(ManagedSessionEvents).Assembly;

    private static string? Read(string relativePath)
    {
        string? path = SessionEventRaisers.Locate(relativePath);

        return path is null ? null : File.ReadAllText(path);
    }

    /// <summary>
    /// EVERY MEMBER OF THE ENUM HAS A ROW, which is what makes the count a count.
    ///
    /// Both directions: a member added to the mirror with no row here, or a row naming a member the
    /// mirror has dropped, is the census going quietly stale.
    /// </summary>
    [Fact]
    public void EveryEventTypeHasExactlyOneRow()
    {
        ChiakiEventType[] members = [.. Enum.GetValues<ChiakiEventType>().Order()];
        ChiakiEventType[] rows = [.. SessionEventRaisers.All.Select(one => one.Event).Order()];

        output.WriteLine($"{rows.Length} row(s) for {members.Length} member(s)");

        Assert.Equal(members, rows);
        Assert.Equal(rows.Length, rows.Distinct().Count());
    }

    /// <summary>
    /// AND THE RAISER COLUMN IS THE C's, read rather than claimed.
    ///
    /// The join PP719 built, widened by PP722: its prefix started at "event.type" and session.c
    /// names two of its four locals otherwise, so the sweep found two raisers in a file with four.
    /// </summary>
    [Theory]
    [InlineData(EventRaiser.StreamConnection, @"lib\src\streamconnection.c")]
    [InlineData(EventRaiser.VideoReceiver, @"lib\src\videoreceiver.c")]
    [InlineData(EventRaiser.Ctrl, @"lib\src\ctrl.c")]
    [InlineData(EventRaiser.Session, @"lib\src\session.c")]
    public void EachFileRaisesExactlyWhatItsRowsSay(EventRaiser raiser, string relativePath)
    {
        if (Read(relativePath) is not { } source)
            return;

        string[] raised = [.. SessionEventRaisers.RaisedIn(source).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

        string[] claimed =
        [
            .. SessionEventRaisers.All
                .Where(one => one.Raiser == raiser)
                .Select(one => SessionEventRaisers.CNameOf(one.Event))
                .Order(StringComparer.Ordinal),
        ];

        output.WriteLine($"{relativePath}: {string.Join(", ", raised)}");

        Assert.Equal(claimed, raised);
    }

    /// <summary>
    /// EXACTLY ONE MEMBER IS RAISED BY NOTHING, and it is the one the Qt client still handles.
    ///
    /// PP33 removed the file that raised it. The member stays because deleting it renumbers every
    /// value after it, so what would otherwise be dead weight is a hole the enum has to keep.
    /// </summary>
    [Fact]
    public void OnlyTheHolepunchEventHasNoRaiser()
    {
        RaisedEvent unraised = Assert.Single(
            SessionEventRaisers.All, one => one.Raiser == EventRaiser.Nobody);

        Assert.Equal(ChiakiEventType.Holepunch, unraised.Event);

        // And no file in lib/src assigns it, which is the claim the row makes.
        foreach (string relative in SessionEventRaisers.RaiserRelativePaths)
        {
            if (Read(relative) is { } source)
                Assert.DoesNotContain(SessionEventRaisers.CNameOf(ChiakiEventType.Holepunch), SessionEventRaisers.RaisedIn(source));
        }
    }

    /// <summary>
    /// THE EIGHT OUTSIDE THE FRAME PATH ARE FOUR SUBSYSTEMS, which is the answer PP722 was filed for.
    ///
    /// PP712's shape: seven owed members read as one job until somebody grouped them. Here the
    /// three keyboard events are one screen, the pin and the quit are one pair already consumed,
    /// the regist pair is one callback, and the holepunch member is its own thing.
    /// </summary>
    [Fact]
    public void TheEventsOutsideTheFramePathAreFourPiecesOfWork()
    {
        IReadOnlyList<RaisedEvent> outside = SessionEventRaisers.Outside;

        output.WriteLine(string.Join("\n", outside.Select(one => $"{one.Event,-20} {one.Subsystem}")));

        Assert.Equal(8, outside.Count);
        Assert.Equal(4, SessionEventRaisers.OutsideSubsystems.Count);

        // And the frame path's nine are the ones PP719 already answers, by its own list.
        Assert.Equal(
            [.. ManagedSessionEvents.RaisedByTheFramePath.Order()],
            [.. SessionEventRaisers.All.Where(one => one.Subsystem == SessionEventRaisers.FramePath)
                .Select(one => one.Event).Order()]);
    }

    /// <summary>
    /// TWO ARE OWED AND THEY ARE THE PAIR ONLY ONE OF WHICH CAN FIRE.
    ///
    /// Both live in the regist callback under FINISHED_SUCCESS, one behind auto_regist and the
    /// other behind its negation and not-a-PS5. So a handler waiting for a nickname after a
    /// successful registration waits forever on the console this port is for - which is a thing to
    /// know before writing the handler rather than after.
    /// </summary>
    [Fact]
    public void TheOwedTwoAreTheMutuallyExclusiveRegistPair()
    {
        Assert.Equal(
            [ChiakiEventType.Regist, ChiakiEventType.NicknameReceived],
            SessionEventRaisers.Owed);

        Assert.All(
            SessionEventRaisers.All.Where(one => SessionEventRaisers.Owed.Contains(one.Event)),
            one => Assert.Equal("the auto-regist callback", one.Subsystem));

        if (Read(@"lib\src\session.c") is not { } source)
            return;

        // The two guards, still opposite. If either moves, the pair stops being exclusive and this
        // census's claim about it is the thing that would otherwise stay true-looking.
        Assert.Contains("if(session->auto_regist)", source, StringComparison.Ordinal);
        Assert.Contains("if(!session->connect_info.ps5 && !session->auto_regist)", source, StringComparison.Ordinal);
    }

    /// <summary>Every counterpart a row names resolves, member included - PP712's rule.</summary>
    [Fact]
    public void EveryCounterpartResolvesToAMember()
    {
        foreach (RaisedEvent row in SessionEventRaisers.All)
        {
            if (row.Managed is not { } counterpart)
                continue;

            // One assembly, two namespaces: CounterpartAssembly picks which name to build, and
            // FullName has already done that, so there is nothing to choose between here.
            Type? type = App.GetType(counterpart.FullName);

            Assert.True(type is not null, $"{row.Event}: {counterpart.FullName} does not resolve");

            if (counterpart.Member is not { } member)
                continue;

            Assert.True(
                type.GetMember(member, BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance).Length > 0,
                $"{row.Event}: {counterpart.FullName} has no member {member}");
        }
    }

    /// <summary>Every row says something, because a mapping with no reason is a table.</summary>
    [Fact]
    public void EveryRowGivesAReason()
        => Assert.All(SessionEventRaisers.All, row => Assert.False(string.IsNullOrWhiteSpace(row.Why)));

    /// <summary>The managed name maps to the C's, which is the whole join.</summary>
    [Theory]
    [InlineData(ChiakiEventType.Connected, "CHIAKI_EVENT_CONNECTED")]
    [InlineData(ChiakiEventType.LoginPinRequest, "CHIAKI_EVENT_LOGIN_PIN_REQUEST")]
    [InlineData(ChiakiEventType.KeyboardRemoteClose, "CHIAKI_EVENT_KEYBOARD_REMOTE_CLOSE")]
    [InlineData(ChiakiEventType.VideoFecFailure, "CHIAKI_EVENT_VIDEO_FEC_FAILURE")]
    public void TheManagedNameSpellsTheCs(ChiakiEventType member, string expected)
        => Assert.Equal(expected, SessionEventRaisers.CNameOf(member));
}
