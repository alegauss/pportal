using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>What arriving datagram did to the race.</summary>
public enum RaceOutcome
{
    /// <summary>A request from the other end: answer it, and keep waiting.</summary>
    Answered,

    /// <summary>A valid response that did not finish the count. Only reachable above one round.</summary>
    Counted,

    /// <summary>A valid response that finished the count - this candidate is the connection.</summary>
    Selected,

    /// <summary>A response carrying the wrong request id. Ignored, and NOT counted.</summary>
    WrongRequestId,

    /// <summary>A message that is neither request nor response, from a candidate that was offered.</summary>
    Fatal,

    /// <summary>The same, from a DERIVED candidate - skipped instead, because those are guesses.</summary>
    Skipped,

    /// <summary>An address nobody offered, taken on as a new derived candidate.</summary>
    NewCandidate,

    /// <summary>And one too many of those, dropped.</summary>
    ExtraLimitReached,
}

/// <summary>
/// PP33: the candidate race - the point where the hole punching stops being shapes and decides
/// something.
///
/// THE WINNER IS THE FIRST TO ANSWER, NOT THE BEST. There is no preference for local over derived,
/// no scoring, no waiting to see whether a better one turns up: the count that selects is
/// CHECK_CANDIDATES_REQUEST_NUMBER round trips, and that constant is ONE. So a single valid
/// response decides the connection, the offered order does not matter, and the race is settled by
/// latency alone. A port that sorted the candidates by type before racing them would produce a
/// different console on a multi-homed network - and would look more sensible while doing it.
///
/// The multi-round machinery is all there and currently degenerate. The request ids are an array of
/// five-byte values, one per round, and a response is matched against <c>request_id[responses]</c> -
/// the id for the round it would be answering. At a REQUEST_NUMBER of one that is always the first
/// and only id, but the indexing is faithful to what raising the constant would mean, so this ports
/// the machine rather than the special case it collapses to.
///
/// Three asymmetries in what an unwanted datagram does:
///
///   A WRONG REQUEST ID IS IGNORED AND NOT COUNTED. It is a late reply to a round that has already
///   passed, so it neither advances the candidate nor kills the race - the loop simply carries on.
///
///   AN UNEXPECTED MESSAGE TYPE IS FATAL, unless the candidate is DERIVED. A derived address is
///   something this client guessed at rather than something the console offered, so rubbish from
///   one is expected and rubbish from an offered candidate means the exchange has gone wrong.
///
///   AN ADDRESS NOBODY OFFERED BECOMES A CANDIDATE. Up to EXTRA_CANDIDATE_ADDRESSES of them, typed
///   DERIVED, with a mapped port of zero and a mapped address of all zeros - the NAT answering from
///   somewhere other than where it was written to. The fourth is dropped with a log and the race
///   continues.
///
/// The sockets are not here. This is the decision the sockets feed, ported so it can be tested at
/// all: a race whose only real input is which datagram arrives first cannot be pinned down by a
/// test that has to open twenty-three UDP sockets to run.
/// </summary>
public sealed class CandidateRace
{
    /// <summary>Round trips before a candidate is selected. ONE - see the class note.</summary>
    public const int RequestNumber = 1;

    /// <summary>How long one attempt waits, in seconds.</summary>
    public const float SelectTimeoutSeconds = 0.5F;

    /// <summary>And how many attempts there are.</summary>
    public const int SelectTries = 20;

    /// <summary>How long the selected candidate then has to connect, in seconds.</summary>
    public const int SelectConnectionSeconds = 5;

    /// <summary>How many unoffered addresses may be taken on.</summary>
    public const int ExtraCandidateAddresses = 3;

    /// <summary>The message type this end sends.</summary>
    /// <remarks>
    /// PP454: derived, not read again. <see cref="PunchResponse"/> is the one place the probe packet's
    /// geometry is written down, and this class was the second of three to have read it out of the
    /// same C independently.
    /// </remarks>
    public const uint RequestType = PunchResponse.RequestType;

    /// <summary>And the one it is waiting for.</summary>
    public const uint ResponseType = PunchResponse.ResponseType;

    /// <summary>Where the request id sits in the eighty-eight byte message.</summary>
    public const int RequestIdOffset = PunchResponse.EchoAt;

    /// <summary>How long it is.</summary>
    public const int RequestIdLength = PunchResponse.EchoLength;

    /// <summary>And how long the message around it is.</summary>
    public const int MessageLength = PunchResponse.Length;

    /// <summary>The mapped address a taken-on candidate is given over IPv4.</summary>
    public const string DerivedMappedAddressV4 = "0.0.0.0";

    /// <summary>And over IPv6, which this build does not enable - see <see cref="EnableIpv6"/>.</summary>
    public const string DerivedMappedAddressV6 = "0:0:0:0:0:0:0:0";

    /// <summary>Whether IPv6 candidates are raced at all. They are not.</summary>
    public const bool EnableIpv6 = false;

    private readonly List<Candidate> candidates;
    private readonly List<int> responses;
    private readonly IReadOnlyList<byte[]> requestIds;
    private int extraUsed;

    /// <summary>A race over the candidates offered, answering the ids this end sent.</summary>
    public CandidateRace(IEnumerable<Candidate> offered, IReadOnlyList<byte[]> requestIds)
    {
        ArgumentNullException.ThrowIfNull(offered);
        ArgumentNullException.ThrowIfNull(requestIds);

        candidates = [.. offered];
        responses = [.. candidates.Select(_ => 0)];
        this.requestIds = requestIds;
        Offered = candidates.Count;
    }

    /// <summary>How many candidates were in the offer, before any were taken on.</summary>
    public int Offered { get; }

    /// <summary>Every candidate in the race, offered and taken on.</summary>
    public IReadOnlyList<Candidate> Candidates => candidates;

    /// <summary>How many unoffered addresses have been taken on.</summary>
    public int ExtraUsed => extraUsed;

    /// <summary>The one that won, or null while the race is still running.</summary>
    public Candidate? Selected { get; private set; }

    /// <summary>How many valid responses a candidate has answered with.</summary>
    public int ResponsesFrom(int index) => responses[index];

    /// <summary>
    /// One datagram, from wherever it arrived from.
    ///
    /// An address that was not offered is taken on as a derived candidate before anything else is
    /// decided about it - so the first datagram from a NAT's other port is both what creates the
    /// candidate and what it answers for.
    /// </summary>
    public RaceOutcome Receive(string address, ushort port, uint messageType, byte[]? requestId)
    {
        ArgumentNullException.ThrowIfNull(address);

        int index = IndexOf(address, port);
        if (index < 0)
        {
            if (extraUsed >= ExtraCandidateAddresses)
                return RaceOutcome.ExtraLimitReached;

            index = TakeOn(address, port);
            if (messageType != ResponseType && messageType != RequestType)
                return RaceOutcome.NewCandidate;
        }

        if (messageType == RequestType)
            return RaceOutcome.Answered;

        if (messageType != ResponseType)
        {
            // Rubbish from a guess is expected; rubbish from an offered candidate is not.
            return candidates[index].Type == CandidateType.Derived
                ? RaceOutcome.Skipped
                : RaceOutcome.Fatal;
        }

        // The id for the round this response would be answering - which is how many have already
        // been counted, not how many were sent.
        byte[] expected = requestIds[responses[index]];
        if (requestId is null || !expected.AsSpan().SequenceEqual(requestId))
            return RaceOutcome.WrongRequestId;

        responses[index]++;
        if (responses[index] > RequestNumber - 1)
        {
            Selected = candidates[index];
            return RaceOutcome.Selected;
        }

        return RaceOutcome.Counted;
    }

    private int IndexOf(string address, ushort port)
    {
        for (int i = 0; i < candidates.Count; i++)
        {
            if (string.Equals(candidates[i].Address, address, StringComparison.Ordinal)
                && candidates[i].Port == port)
            {
                return i;
            }
        }

        return -1;
    }

    private int TakeOn(string address, ushort port)
    {
        candidates.Add(new Candidate(CandidateType.Derived, address, DerivedMappedAddressV4, port, 0));
        responses.Add(0);
        extraUsed++;
        return candidates.Count - 1;
    }
}

/// <summary>
/// PP33: the race's rules where the Qt core states them.
/// </summary>
public static class CandidateRaceSource
{
    /// <summary>Where the race is run.</summary>
    public const string RelativePath = @"lib\src\remote\holepunch.c";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>The constants this port copied, and what the core spells them.</summary>
    public static IReadOnlyList<(string Name, string Value)> Constants { get; } =
    [
        ("CHECK_CANDIDATES_REQUEST_NUMBER", "1"),
        ("SELECT_CANDIDATE_TIMEOUT_SEC", "0.5F"),
        ("SELECT_CANDIDATE_TRIES", "20"),
        ("SELECT_CANDIDATE_CONNECTION_SEC", "5"),
        ("EXTRA_CANDIDATE_ADDRESSES", "3"),
        ("MSG_TYPE_REQ", "0x06000000"),
        ("MSG_TYPE_RESP", "0x07000000"),
        ("ENABLE_IPV6", "false"),
    ];

    /// <summary>Whether every one of them still holds the value this port was built against.</summary>
    public static bool TheConstantsAreStillTheseValues(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        foreach ((string name, string value) in Constants)
        {
            if (!core.Contains($"#define {name} {value}", StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Whether the selection is still "more responses than one less than the count" - the
    /// comparison that makes ONE round trip enough to decide the connection.
    /// </summary>
    public static bool TheFirstToAnswerStillWins(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains(
            "if(responses > (CHECK_CANDIDATES_REQUEST_NUMBER - 1))", StringComparison.Ordinal);
    }

    /// <summary>Whether a response is still matched against the id for the round it answers.</summary>
    public static bool TheIdIsStillIndexedByTheCount(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        // The offset is written in hex there and in decimal here, so it is spelled both ways at
        // once rather than compared as a string that happens to match.
        string offset = "0x" + Convert.ToString(CandidateRace.RequestIdOffset, 16);

        return core.Contains("int responses = responses_received[i];", StringComparison.Ordinal)
            && core.Contains($"memcmp(response_buf + {offset}", StringComparison.Ordinal)
            && core.Contains("request_id[responses], sizeof(request_id[responses]))", StringComparison.Ordinal);
    }

    /// <summary>Whether a wrong id is still ignored rather than counted or fatal.</summary>
    public static bool AWrongIdIsStillIgnored(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        int at = core.IndexOf(
            "check_candidates: Received response with unexpected request ID", StringComparison.Ordinal);
        if (at < 0)
            return false;

        // The block ends by carrying on, not by counting and not by jumping out.
        int end = core.IndexOf("received_response = true;", at, StringComparison.Ordinal);
        return end > at && core[at..end].Contains("continue;", StringComparison.Ordinal);
    }

    /// <summary>Whether an unexpected type is still fatal except for a derived candidate.</summary>
    public static bool AnUnexpectedTypeIsStillFatalExceptForDerived(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        int at = core.IndexOf(
            "check_candidates: Received response of unexpected type", StringComparison.Ordinal);
        if (at < 0)
            return false;

        int end = core.IndexOf("responses_received[i];", at, StringComparison.Ordinal);
        if (end < at)
            return false;

        string block = core[at..end];
        return block.Contains("if(candidate->type == CANDIDATE_TYPE_DERIVED)", StringComparison.Ordinal)
            && block.Contains("continue;", StringComparison.Ordinal)
            && block.Contains("goto cleanup_sockets;", StringComparison.Ordinal);
    }

    /// <summary>Whether an unoffered address still becomes a derived candidate, up to the limit.</summary>
    public static bool AnUnofferedAddressIsStillTakenOn(string core)
    {
        ArgumentNullException.ThrowIfNull(core);
        return core.Contains("candidate->type = CANDIDATE_TYPE_DERIVED;", StringComparison.Ordinal)
            && core.Contains("candidate->port_mapped = 0;", StringComparison.Ordinal)
            && core.Contains($"memcpy(candidate->addr_mapped, \"{CandidateRace.DerivedMappedAddressV4}\"", StringComparison.Ordinal)
            && core.Contains("if(extra_addresses_used >= EXTRA_CANDIDATE_ADDRESSES)", StringComparison.Ordinal);
    }
}
