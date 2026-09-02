using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP597: what PP33's deletion costs gui/, held as a check that fires when the deletion lands.
///
/// PP596 settled that nothing a default build compiles reaches session.c's nine holepunch asks, so
/// removing them changes no shipped behaviour. What it changes is whether the Qt client can be
/// compiled at all: streamsession.cpp assigns chiaki_connect_info.holepunch_session, and the field
/// would be gone with the asks that read it.
///
/// THE DRIFT CHECKS SURVIVE THAT AND GuiFreshness DOES NOT. Around twenty readers under app/ locate
/// a gui/ source and read its text - the mapping tokens, the touchpad rules, the focus chain, the
/// QML controls - and none of them compiles anything. GuiFreshness is the one that knows about a
/// built client, and it reports Stale as a failure on purpose (PP270: a warning among four thousand
/// passing tests is one nobody reads). `compile.cmd gui` is the only thing that refreshes that
/// binary, so a tree where it cannot run leaves every later gui/ edit permanently stale for anybody
/// who has built the client once.
///
/// So this is not a guard on the tree as it is. It is a guard on the change, and its job is to be
/// red in the commit that deletes the field, naming the decision that commit owes.
/// </summary>
public class GuiBuildabilityTests
{
    /// <summary>
    /// The Qt client compiles against the field PP33's deletion removes.
    ///
    /// Both sides, because either can move first. A gui/ that stopped using the field is a client
    /// the deletion no longer costs anything; a header that lost it is the deletion having landed.
    /// </summary>
    [Fact]
    public void TheQtClientNeedsTheFieldTheDeletionWouldRemove()
    {
        if (HolepunchSessionOwnership.LocateQtClient() is not { } client)
            return;

        if (HolepunchSessionOwnership.LocateSessionHeader() is not { } header)
            return;

        Assert.True(
            HolepunchSessionOwnership.TheQtClientCompilesAgainstTheField(
                File.ReadAllText(client), File.ReadAllText(header)),
            "the join PP597 records has moved: either gui/ no longer needs "
                + $"{HolepunchSessionOwnership.ConnectInfoField}, or session.h no longer declares it. "
                + "If this is PP33's deletion landing, decide gui/ in the same commit - retire the "
                + "client's build, or give GuiFreshness a state for a client that cannot be rebuilt, "
                + "because Stale is a failure and compile.cmd gui is the only thing that clears it");
    }

    /// <summary>
    /// And the reader needs both halves, so a header that kept the field cannot cover a client that
    /// stopped using it.
    /// </summary>
    [Fact]
    public void EitherHalfMissingIsTheJoinGone()
    {
        Assert.True(HolepunchSessionOwnership.TheQtClientCompilesAgainstTheField(
            "chiaki_connect_info.holepunch_session = holepunch_session;",
            "\tChiakiHolepunchSession holepunch_session;"));

        Assert.False(HolepunchSessionOwnership.TheQtClientCompilesAgainstTheField(
            "chiaki_connect_info.host = host;",
            "\tChiakiHolepunchSession holepunch_session;"));

        Assert.False(HolepunchSessionOwnership.TheQtClientCompilesAgainstTheField(
            "chiaki_connect_info.holepunch_session = holepunch_session;",
            "\tChiakiRudp rudp;"));
    }

    /// <summary>
    /// Stale is a failure, which is the whole reason the deletion owes gui/ a decision.
    ///
    /// If it were a note, a client nobody can rebuild would be an untidy line rather than a red
    /// suite, and PP597 would not be worth a task. PP529 chose the failure deliberately and this
    /// records that the choice is what makes the cost real.
    /// </summary>
    [Fact]
    public void AStaleClientIsAFailureAndNotANote()
    {
        // A checkout with a client older than a source, built here so the state is the real reader's
        // answer rather than an enum value quoted back.
        string root = Path.Combine(Path.GetTempPath(), "pp597-" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "build", "gui"));
            Directory.CreateDirectory(Path.Combine(root, "gui", "src"));

            string client = Path.Combine(root, GuiFreshness.ClientRelativePath);
            File.WriteAllText(client, "not really a client");
            File.SetLastWriteTimeUtc(client, DateTime.UtcNow.AddHours(-1));

            string source = Path.Combine(root, "gui", "src", "streamsession.cpp");
            File.WriteAllText(source, "// edited after the client was built");
            File.SetLastWriteTimeUtc(source, DateTime.UtcNow);

            // PP632: Retired, not Stale. This arrangement - a client older than a source beside it
            // - is exactly the one PP597 said would be permanently red once nothing could rebuild
            // it, and the state PP597 asked for by name is what answers instead.
            GuiBuild answer = GuiFreshness.CheckIn(root);

            Assert.Equal(GuiBuildState.Retired, answer.State);
            Assert.Contains("retired", GuiFreshness.Explain(answer), StringComparison.OrdinalIgnoreCase);

            // The comparison it used to make is kept and still right, which is what says the state
            // changed rather than the rule being lost.
            Assert.Equal(GuiBuildState.Stale, GuiFreshness.WouldHaveCheckedIn(root).State);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// And a checkout that never built one is a state rather than a failure, which is what makes
    /// gui/ a READ-ONLY oracle today and the reason the drift checks are not in danger.
    /// </summary>
    [Fact]
    public void ANeverBuiltClientIsNotAFailure()
    {
        GuiBuild answer = GuiFreshness.Check();

        Assert.True(
            answer.State is GuiBuildState.NeverBuilt or GuiBuildState.NoCheckout
                or GuiBuildState.Retired,
            $"GuiFreshness answered {answer.State}, and PP632 left it three answers");

        // The property that matters: never having built one is answerable, so a fresh clone reads
        // gui/ without owning a compiler for it.
        Assert.Contains(GuiBuildState.NeverBuilt, Enum.GetValues<GuiBuildState>());
    }
}
