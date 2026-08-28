using ChiakiNg.Native;
using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP479, PP340: the managed side driving the PSN flow, rather than describing it.
///
/// PP429 wrote the nine call sites down, PP460 their order and guards, PP478 the state and its
/// lifetimes. This runs them. The tests worth having are the ones a hand-written sequence would get
/// wrong: that the order comes from PP460's model rather than a second list, that each failure does
/// what PP460 said, and that the registration info is not kept.
/// </summary>
public class HolepunchConnectTests
{
    /// <summary>A session that answers, and records what was asked.</summary>
    private sealed class Stub : IHolepunchSession
    {
        public List<string> Asked { get; } = [];

        public ChiakiError OfferResult { get; set; } = ChiakiError.Success;

        public ChiakiError PunchResult { get; set; } = ChiakiError.Success;

        public int Finis { get; private set; }

        public object GetSocket(HolepunchPortType type)
        {
            Asked.Add($"socket:{type}");
            return $"sock-{type}";
        }

        public object GetRegistInfo()
        {
            Asked.Add("registinfo");
            return new object();
        }

        public ChiakiError CreateOffer()
        {
            Asked.Add("offer");
            return OfferResult;
        }

        public ChiakiError PunchHole(HolepunchPortType type)
        {
            Asked.Add($"punch:{type}");
            return PunchResult;
        }

        public string GetSelectedAddress()
        {
            Asked.Add("address");
            return "203.0.113.7";
        }

        public ushort GetCtrlPort()
        {
            Asked.Add("ctrlport");
            return 9296;
        }

        public void Fini()
        {
            Asked.Add("fini");
            Finis++;
        }
    }

    private static HolepunchConnect With(Stub stub, bool rudpInits = true)
        => new(stub, _ => rudpInits ? new object() : null);

    /// <summary>
    /// The happy path asks all seven, in PP460's order, and holds what PP478 says it should.
    /// </summary>
    [Fact]
    public void TheHappyPathAsksAllSevenInOrder()
    {
        var stub = new Stub();

        HolepunchConnectOutcome outcome = With(stub).Run();

        Assert.Null(outcome.FailedAt);
        Assert.Equal(ChiakiError.Success, outcome.Error);

        Assert.Equal(
            new[]
            {
                "socket:Ctrl", "registinfo", "offer", "punch:Data", "socket:Data", "address", "ctrlport",
            },
            stub.Asked.ToArray());

        Assert.NotNull(outcome.Rudp);
        Assert.Equal("sock-Data", outcome.DataSocket);
        Assert.Equal("203.0.113.7", outcome.Hostname);
        Assert.Equal((ushort)9296, outcome.CtrlPort);

        // And the fini is teardown, not a step - nothing released a session that succeeded.
        Assert.Equal(0, outcome.FinisCalled);
    }

    /// <summary>
    /// THE ORDER IS PP460'S, not a second list - so it has one place to be wrong.
    ///
    /// PP454 and PP458 each cost a task to undo a duplicated model; this asserts the sequence driven is
    /// the one asserted against the C.
    /// </summary>
    [Fact]
    public void TheOrderComesFromTheModelAndNotFromHere()
    {
        var stub = new Stub();
        With(stub).Run();

        // Seven steps asked, seven in the model, and the fini is in neither.
        Assert.Equal(HolepunchFlow.ExecutionOrder.Count, stub.Asked.Count);
        Assert.DoesNotContain(HolepunchStep.Fini, HolepunchFlow.ExecutionOrder);
        Assert.DoesNotContain("fini", stub.Asked);
    }

    /// <summary>
    /// The ctrl socket's failure surfaces through the rudp init it feeds - PP460's CaughtByWhatItFeeds,
    /// and the one place a failure is reported under another name.
    /// </summary>
    [Fact]
    public void AFailedRudpInitStopsAtTheCtrlSocket()
    {
        var stub = new Stub();

        HolepunchConnectOutcome outcome = With(stub, rudpInits: false).Run();

        Assert.Equal(HolepunchStep.CtrlSocket, outcome.FailedAt);
        Assert.Null(outcome.Rudp);

        // Asked for the socket, then released the session. Nothing after it ran.
        Assert.Equal(new[] { "socket:Ctrl", "fini" }, stub.Asked.ToArray());
        Assert.Equal(1, outcome.FinisCalled);
    }

    /// <summary>The two error-returning steps quit, and nothing after them is asked.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnErrorReturningStepQuits(bool failTheOffer)
    {
        var stub = new Stub();
        if (failTheOffer)
            stub.OfferResult = ChiakiError.Network;
        else
            stub.PunchResult = ChiakiError.Network;

        HolepunchConnectOutcome outcome = With(stub).Run();

        Assert.Equal(
            failTheOffer ? HolepunchStep.CreateOffer : HolepunchStep.PunchHole,
            outcome.FailedAt);
        Assert.Equal(ChiakiError.Network, outcome.Error);

        // The guard PP460 named, and the session released once.
        Assert.Equal(
            HolepunchGuard.QuitsToCtrlTeardown, HolepunchFlow.GuardFor(outcome.FailedAt!.Value));
        Assert.Equal(1, outcome.FinisCalled);

        // Nothing past the failure was asked, and the data socket was never taken.
        Assert.DoesNotContain("socket:Data", stub.Asked);
        Assert.Null(outcome.DataSocket);
    }

    /// <summary>
    /// A failed punch never reaches the data socket, which is what makes PP461's "cannot be invalid
    /// here" true of this flow too.
    /// </summary>
    [Fact]
    public void TheDataSocketIsOnlyTakenAfterASuccessfulPunch()
    {
        var stub = new Stub { PunchResult = ChiakiError.Network };
        With(stub).Run();

        Assert.Contains("punch:Data", stub.Asked);
        Assert.DoesNotContain("socket:Data", stub.Asked);
    }

    /// <summary>
    /// A LOCAL SESSION ASKS NOTHING and leaves the data socket null, which is the value senkusha reads
    /// as "use the ordinary socket" - PP478's point, and where a managed flow would refuse local play.
    /// </summary>
    [Fact]
    public void ALocalSessionAsksNothingAndLeavesTheSocketNull()
    {
        var stub = new Stub();

        HolepunchConnectOutcome outcome = With(stub).Run(isPsn: false);

        Assert.Empty(stub.Asked);
        Assert.Null(outcome.DataSocket);
        Assert.Null(outcome.Rudp);
        Assert.Null(outcome.FailedAt);
        Assert.Equal(HolepunchConnect.DefaultPort, outcome.CtrlPort);
    }

    /// <summary>
    /// THE REGISTRATION INFO IS NOT KEPT, which is the one thing PP478 says a managed flow has to work
    /// at: the C gets that lifetime from a closing brace and this gets it from not storing it.
    ///
    /// Asserted through the outcome's shape - there is nowhere to put it - and through the info being
    /// asked for exactly once.
    /// </summary>
    [Fact]
    public void TheRegistInfoIsAskedForAndNotKept()
    {
        var stub = new Stub();
        HolepunchConnectOutcome outcome = With(stub).Run();

        Assert.Single(stub.Asked, a => a == "registinfo");

        // The outcome carries the four pieces PP478 says outlive the block, and not the fifth: there is
        // nowhere on it to put the registration info.
        Assert.NotNull(outcome.Rudp);
        Assert.NotNull(outcome.DataSocket);
        Assert.NotNull(outcome.Hostname);
        Assert.NotEqual(HolepunchConnect.DefaultPort, outcome.CtrlPort);

        Assert.DoesNotContain(
            "hinfo",
            typeof(HolepunchConnectOutcome).GetProperties().Select(p => p.Name.ToLowerInvariant()));

        Assert.Equal(StateLifetime.Block, HolepunchState.TheShortestLived.Lifetime);
        Assert.Equal(HolepunchStep.RegistInfo, HolepunchState.TheShortestLived.FromStep);
    }
}
