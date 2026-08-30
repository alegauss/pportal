using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>What session.c does with each of the nine asks.</summary>
public enum HolepunchAskKind
{
    /// <summary>It keeps a value: a socket, a struct, an address, a port.</summary>
    Result,

    /// <summary>It makes something happen and keeps only an error code.</summary>
    Verb,

    /// <summary>It gives the session back.</summary>
    Release,
}

/// <summary>
/// PP533: which of the two directions PP33 can take, decided from what session.c actually keeps.
///
/// §PP33 named this and did not file it; §PP533 filed it and posed it as a choice. session.c stops
/// taking a holepunch handle and starts taking the RESULTS, or it keeps the handle and the managed
/// side fills it. Only the first deletes holepunch.c.
///
/// PP340's seam recorded the nine call sites and PP481 implemented them over the real C; neither
/// asked what session.c does with each. That is the question the choice turns on, because a handle
/// can only be replaced by results if results are all it is ever used for.
///
/// THEY ARE. Every mention of <c>session-&gt;holepunch_session</c> in session.c is one of three
/// things: the assignment that takes it out of the connect info, a null guard asking whether this
/// is a PSN session at all, or an argument to one of the nine. Nothing reads a field of it, stores
/// it elsewhere, or passes it on. So the handle carries nothing across the call sites, and the
/// direction is settled: hand session.c the results and the guards become "were there results".
///
/// AND THE RESULTS ARE FIVE, NOT FOUR. §PP533 says a managed holepunch would hand session.c "the
/// sockets, the address and the port" - which is the two sockets, the address and the port, and
/// leaves out the registration info the session request carries. That is the one item on this list
/// which is neither a socket nor an endpoint, and a port written to the four would have found it
/// missing at the point where the request is built.
///
/// PP551: BUT FIVE FIELDS WOULD STILL BE WRONG, and saying "five results" without saying that was
/// the half-truth this corrects. Four of them outlive the call that produced them; the registration
/// info does not - its address is taken and handed to four calls that all finish inside its block,
/// which PP478 read out of the C and PP479 works at. So the replacement holds
/// <see cref="Durable"/> and produces <see cref="Scoped"/> where it is used.
/// </summary>
public static class HolepunchDirection
{
    /// <summary>Where the asks are made.</summary>
    public const string RelativePath = HolepunchSeam.RelativePath;

    /// <summary>session.c, or null outside a checkout.</summary>
    public static string? Locate() => HolepunchSeam.Locate();

    /// <summary>
    /// What each of PP340's nine is, in the same file order <see cref="HolepunchSeam.Asks"/> uses.
    ///
    /// Parallel to that list by position, so a tenth call site arriving there without a kind here
    /// is a length disagreement rather than a silent gap.
    /// </summary>
    public static IReadOnlyList<HolepunchAskKind> Kinds { get; } =
    [
        HolepunchAskKind.Release,   // fini, on the init failure path
        HolepunchAskKind.Release,   // fini, on the teardown path
        HolepunchAskKind.Result,    // the ctrl socket
        HolepunchAskKind.Result,    // the registration info
        HolepunchAskKind.Verb,      // create the offer
        HolepunchAskKind.Verb,      // punch the hole
        HolepunchAskKind.Result,    // the data socket
        HolepunchAskKind.Result,    // the address it was reached at
        HolepunchAskKind.Result,    // the ctrl port
    ];

    /// <summary>
    /// The five values session.c would take instead of the handle, and how long each one lives.
    ///
    /// PP551: DERIVED FROM PP478, not restated. The first version of this listed the five in its
    /// own words, which was the same five <see cref="HolepunchState.Carried"/> already held with
    /// their lifetimes - two lists that agree today and drift apart the first time one is edited.
    /// Reading them from there means a sixth arriving, or a lifetime changing, arrives here too.
    /// </summary>
    public static IReadOnlyList<FlowState> Results => HolepunchState.Carried;

    /// <summary>
    /// The four that outlive the call that produced them, which is what a replacement can hold.
    /// </summary>
    public static IReadOnlyList<FlowState> Durable { get; } =
        [.. HolepunchState.Carried.Where(state => state.Lifetime != StateLifetime.Block)];

    /// <summary>
    /// And the one that does not: the registration info, whose address is taken and handed to four
    /// calls that all finish inside its block.
    ///
    /// PP551 IS WHY THIS DISTINCTION IS DRAWN AT ALL. PP533 settled that session.c takes five
    /// results, and left it sounding as though five fields would do. They would not: PP479 says of
    /// this one that keeping it on an outcome "would compile and would be the bug". So the
    /// replacement carries four and produces the fifth inside the block that uses it.
    /// </summary>
    public static IReadOnlyList<FlowState> Scoped { get; } =
        [.. HolepunchState.Carried.Where(state => state.Lifetime == StateLifetime.Block)];

    /// <summary>
    /// The join nothing had: PP479's outcome carries every durable result and not the scoped one.
    ///
    /// Read off the record's own members, so adding a registration info to it - the change PP479
    /// warns about, which compiles - fails here instead of shipping.
    /// </summary>
    public static bool TheOutcomeCarriesTheDurableResultsOnly()
    {
        IReadOnlyList<string> members =
            [.. typeof(HolepunchConnectOutcome).GetProperties().Select(one => one.Name)];

        bool durableAreThere = Durable.All(
            state => members.Any(name => Answers(name, state.FromStep)));

        bool scopedIsNot = Scoped.All(
            state => !members.Any(name => Answers(name, state.FromStep)));

        return durableAreThere && scopedIsNot;
    }

    /// <summary>
    /// Whether an outcome member is the one a step produces.
    ///
    /// The ctrl socket is the exception and is named as one: the outcome holds the rudp built from
    /// it rather than the socket, because that is the only place its failure surfaces.
    /// </summary>
    public static bool Answers(string member, HolepunchStep? step)
    {
        ArgumentNullException.ThrowIfNull(member);

        return step switch
        {
            HolepunchStep.CtrlSocket => member == "Rudp",
            HolepunchStep.DataSocket => member == "DataSocket",
            HolepunchStep.SelectedAddress => member == "Hostname",
            HolepunchStep.CtrlPort => member == "CtrlPort",
            HolepunchStep.RegistInfo => member is "RegistInfo" or "Hinfo" or "HolepunchInfo",
            _ => false,
        };
    }

    /// <summary>
    /// Every mention of the handle, in file order: how each one uses it.
    ///
    /// One assignment, five guards, nine calls. Fifteen, and the count is not the point - what it
    /// is for is that a sixteenth doing something else would not fit any of the three.
    /// </summary>
    public const int Assignments = 1;

    /// <summary>The null guards asking whether this is a PSN session at all.</summary>
    public const int Guards = 5;

    /// <summary>The handle, as session.c spells it.</summary>
    public const string Handle = "session->holepunch_session";

    /// <summary>How many times the handle is named. Fifteen: the assignment, the guards, the nine.</summary>
    public static int MentionsIn(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        string code = CCall.Compact(CCall.Code(source));
        string handle = CCall.Compact(Handle);

        int count = 0;
        for (int at = code.IndexOf(handle, StringComparison.Ordinal);
             at >= 0;
             at = code.IndexOf(handle, at + handle.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    /// <summary>
    /// THE FACT THE DIRECTION RESTS ON: the handle is only ever taken, guarded, or passed.
    ///
    /// Counted rather than inspected site by site, because the claim is about the whole file: if
    /// the assignment, the guards and the nine account for every mention, then nothing else in
    /// session.c can be reading it. A use this does not know about makes the total disagree.
    /// </summary>
    public static bool TheHandleIsOnlyTakenGuardedOrPassed(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return MentionsIn(source) == Assignments + Guards + HolepunchSeam.Count
            && GuardsIn(source) == Guards
            && TakenFromTheConnectInfo(source);
    }

    /// <summary>The null guards, which become "were there results" once the handle is gone.</summary>
    public static int GuardsIn(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        string code = CCall.Compact(CCall.Code(source));
        string guard = CCall.Compact($"if({Handle})");

        int count = 0;
        for (int at = code.IndexOf(guard, StringComparison.Ordinal);
             at >= 0;
             at = code.IndexOf(guard, at + guard.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    /// <summary>The one assignment: session.c does not build the handle, it is handed one.</summary>
    public static bool TakenFromTheConnectInfo(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return CCall.Compact(CCall.Code(source)).Contains(
            CCall.Compact($"{Handle} = connect_info->holepunch_session;"), StringComparison.Ordinal);
    }

    /// <summary>
    /// Each result still goes where <see cref="Results"/> says, which is what makes them results.
    ///
    /// The address is the odd one and is checked as itself: it is delivered through an out
    /// parameter rather than returned, so nothing is assigned at that call site. A check written
    /// for the shape of the other four would have called it a verb.
    /// </summary>
    public static bool EveryResultIsStillKept(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        string code = CCall.Compact(CCall.Code(source));

        return code.Contains(
                CCall.Compact($"*rudp_sock = chiaki_get_holepunch_sock({Handle}, CHIAKI_HOLEPUNCH_PORT_TYPE_CTRL)"),
                StringComparison.Ordinal)
            && code.Contains(
                CCall.Compact($"hinfo = chiaki_get_regist_info({Handle})"), StringComparison.Ordinal)
            && code.Contains(
                CCall.Compact($"data_sock = chiaki_get_holepunch_sock({Handle}, CHIAKI_HOLEPUNCH_PORT_TYPE_DATA)"),
                StringComparison.Ordinal)
            && code.Contains(
                CCall.Compact($"chiaki_get_ps_selected_addr({Handle}, session->connect_info.hostname)"),
                StringComparison.Ordinal)
            && code.Contains(
                CCall.Compact($"port = chiaki_get_ps_ctrl_port({Handle})"), StringComparison.Ordinal);
    }

    /// <summary>
    /// And the two verbs keep only an error code, which is why neither is a result.
    ///
    /// If either returned something session.c held on to, the replacement would need a sixth value
    /// and the list above would be wrong.
    /// </summary>
    public static bool TheVerbsKeepOnlyAnError(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        string code = CCall.Compact(CCall.Code(source));

        return code.Contains(
                CCall.Compact($"err = holepunch_session_create_offer({Handle})"), StringComparison.Ordinal)
            && code.Contains(
                CCall.Compact($"err = chiaki_holepunch_session_punch_hole({Handle}, CHIAKI_HOLEPUNCH_PORT_TYPE_DATA)"),
                StringComparison.Ordinal);
    }

}
