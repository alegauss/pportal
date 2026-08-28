using ChiakiNg.Native;

namespace ChiakiNg.Protocol;

/// <summary>
/// A session that answers the nine asks and records which of them the flow used.
///
/// Public because the evidence PP508 wants is a RUN, and a recording session is the instrument -
/// keeping it in the test project would put the instrument on the other side of the seam from the
/// thing it measures.
/// </summary>
public sealed class RecordingHolepunchSession : IHolepunchSession
{
    private readonly List<string> invoked = [];
    private readonly List<HolepunchPortType> ports = [];

    /// <summary>Where the run should stop, or null to reach the end.</summary>
    public HolepunchStep? FailAt { get; init; }

    /// <summary>Every method called, in order, with repeats kept.</summary>
    public IReadOnlyList<string> Invoked => invoked;

    /// <summary>Which port types the socket getter was asked for.</summary>
    public IReadOnlyList<HolepunchPortType> SocketPorts => ports;

    /// <summary></summary>
    public object GetSocket(HolepunchPortType type)
    {
        invoked.Add(nameof(GetSocket));
        ports.Add(type);
        return new object();
    }

    /// <summary></summary>
    public object GetRegistInfo()
    {
        invoked.Add(nameof(GetRegistInfo));
        return new object();
    }

    /// <summary></summary>
    public ChiakiError CreateOffer()
    {
        invoked.Add(nameof(CreateOffer));
        return FailAt == HolepunchStep.CreateOffer ? ChiakiError.Network : ChiakiError.Success;
    }

    /// <summary></summary>
    public ChiakiError PunchHole(HolepunchPortType type)
    {
        invoked.Add(nameof(PunchHole));
        return FailAt == HolepunchStep.PunchHole ? ChiakiError.Network : ChiakiError.Success;
    }

    /// <summary></summary>
    public string GetSelectedAddress()
    {
        invoked.Add(nameof(GetSelectedAddress));
        return "203.0.113.7";
    }

    /// <summary></summary>
    public ushort GetCtrlPort()
    {
        invoked.Add(nameof(GetCtrlPort));
        return 41234;
    }

    /// <summary></summary>
    public void Fini() => invoked.Add(nameof(Fini));
}

/// <summary>
/// PP508, under PP340: which of the seam's seven methods the managed flow actually calls.
///
/// PP429 wrote the nine call sites down, PP479 gave them an interface, PP480 joined the two. None
/// of the three says the flow ever CALLS the seven.
///
/// THE GAP IS INVISIBLE FROM EITHER SIDE. PP480's join is between a list and a type - the interface
/// read by reflection, the sites from PP429's census - so a method can be declared, joined to a
/// site, and reached by nothing. The flow would skip a step of the PSN connect in silence while
/// every check around it stayed green: complete census, complete interface, complete join.
///
/// SO THE EVIDENCE IS A RUN, NOT A LIST. Drive <see cref="HolepunchConnect"/> with a recording
/// session, once to the end and once into each failure, and take the union of what it invoked. It
/// must EQUAL the seam's seven rather than contain them, so a method the flow calls that the seam
/// does not know about fails here too.
///
/// TWO DETAILS MAKE IT MORE THAN A COUNT. The socket getter has to be seen with BOTH port types -
/// PP429's point about it being that a managed side returning one socket for both would compile.
/// And Fini has to be seen, which needs a failing run: the success path never calls it, so a check
/// over one good run reports six of seven without saying which one is missing.
/// </summary>
public static class HolepunchFlowCoverage
{
    /// <summary>The runs the union is taken over: the whole flow, and each failure.</summary>
    public static IReadOnlyList<HolepunchStep?> Runs { get; } =
        [null, HolepunchStep.CreateOffer, HolepunchStep.PunchHole];

    /// <summary>
    /// Every method the flow invokes across <see cref="Runs"/>, and every port it asks for.
    /// </summary>
    public static (IReadOnlySet<string> Methods, IReadOnlySet<HolepunchPortType> Ports) Exercise()
    {
        var methods = new SortedSet<string>(StringComparer.Ordinal);
        var ports = new HashSet<HolepunchPortType>();

        foreach (HolepunchStep? failAt in Runs)
        {
            var session = new RecordingHolepunchSession { FailAt = failAt };

            // A rudp that always initialises: the ctrl socket's own failure is PP479's subject and
            // taking it here would hide the steps after it from every run.
            _ = new HolepunchConnect(session, _ => new object()).Run();

            methods.UnionWith(session.Invoked);
            ports.UnionWith(session.SocketPorts);
        }

        return (methods, ports);
    }

    /// <summary>The seven methods the seam says the nine sites reach.</summary>
    public static IReadOnlySet<string> SeamMethods { get; } =
        new SortedSet<string>(
            HolepunchSeamJoin.Joins.Select(j => j.Method), StringComparer.Ordinal);
}
