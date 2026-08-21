using System.Text;

namespace ChiakiNg.Protocol;

/// <summary>Which of the two local candidates fills in a winner's mapped address.</summary>
public enum MappedSource
{
    /// <summary>The first entry - this side's address as this side knows it.</summary>
    Directly,

    /// <summary>
    /// The second - this side's address as the outside world sees it. Called "remote" in the core,
    /// and read out of the LOCAL array.
    /// </summary>
    ViaStun,
}

/// <summary>
/// PP248: deciding whether the winning address is on this network.
///
/// EVERY PREFIX TEST OVERSHOOTS, BY TWO DIFFERENT MISTAKES. The six written as literals compare
/// over the literal's length PLUS ONE - the extra byte is its terminator, so the comparison only
/// succeeds if the address ENDS at the prefix, which no address printed by inet_ntop ever does.
/// "10.0.0.1" against "10." over four bytes differs at the fourth: a digit against a nul.
///
/// The sixteen generated for 172.16 through 172.31 overshoot by TWO, and for a different reason:
/// they compare over the BUFFER's size rather than the string's. The buffer is nine bytes, the
/// string it holds is seven plus a terminator, so the comparison reaches a byte past even the
/// terminator. Same effect, different error - and worth separating, because a port that shortened
/// every count by one would fix six of them and leave sixteen broken.
///
/// This was measured rather than reasoned about: the comparisons were compiled and run with this
/// project's own compiler, and every one returned non-zero.
///
/// <see cref="Strncmp"/> is a faithful strncmp so the port's tests exercise the real semantics
/// rather than a description of them, and <see cref="IsLocalAsWritten"/> uses it with the lengths
/// the core passes.
///
/// The consequence is narrow, and stating it exactly matters more than stating it loudly: the flag
/// is only computed for a candidate DISCOVERED from traffic, and one the console typed as local
/// takes the local branch regardless. What is lost is a discovered address that happens to be
/// private - it gets its mapped address from the STUN-derived candidate instead of the directly
/// known one.
///
/// AND BOTH CANDIDATES ARE LOCAL. The one the core calls remote_candidate is the second entry of
/// the LOCAL array. The two names say how each was discovered, not whose address each is.
/// </summary>
public static class PrivateAddress
{
    /// <summary>
    /// The buffer the 172 prefixes are built in, and the count they are compared over - the same
    /// number, which is the mistake.
    /// </summary>
    public const int CompareBuffer = 9;

    /// <summary>The IPv4 prefixes, with the length the core compares each over.</summary>
    public static IReadOnlyList<(string Prefix, int Length)> Ipv4Tests { get; } = BuildIpv4Tests();

    /// <summary>And the IPv6 ones, four spellings of the same two prefixes.</summary>
    public static IReadOnlyList<(string Prefix, int Length)> Ipv6Tests { get; } =
    [
        ("FC", 3), ("fc", 3), ("FD", 3), ("fd", 3),
    ];

    private static List<(string, int)> BuildIpv4Tests()
    {
        // The two literals, each overshooting by one - the prefix plus its terminator.
        List<(string, int)> tests = [("10.", 4), ("192.168.", 9)];

        // And sixteen through thirty-one, which IS the right range. These overshoot by two: the
        // count is the buffer's size, not the seven-character string sprintf puts in it.
        for (int block = 16; block < 32; block++)
            tests.Add(($"172.{block}.", CompareBuffer));

        return tests;
    }

    /// <summary>
    /// A faithful strncmp: compares at most <paramref name="count"/> bytes and stops at the first
    /// difference OR at a terminator in the left operand.
    ///
    /// Written out rather than approximated with StartsWith, because the whole finding lives in
    /// what happens when the count reaches past one operand's end.
    /// </summary>
    public static int Strncmp(string left, string right, int count)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        byte[] a = Terminated(left);
        byte[] b = Terminated(right);

        byte c1 = 0;
        byte c2 = 0;

        for (int at = 0; at < count; at++)
        {
            c1 = at < a.Length ? a[at] : (byte)0;
            c2 = at < b.Length ? b[at] : (byte)0;

            if (c1 == 0 || c1 != c2)
                return c1 - c2;
        }

        return c1 - c2;
    }

    private static byte[] Terminated(string text)
    {
        byte[] bytes = new byte[Encoding.ASCII.GetByteCount(text) + 1];
        Encoding.ASCII.GetBytes(text, bytes);
        return bytes;
    }

    /// <summary>
    /// Whether an address is judged private, using the lengths the core passes.
    ///
    /// Always false. That is the finding, and it is produced rather than asserted: this really does
    /// run the comparisons.
    /// </summary>
    public static bool IsLocalAsWritten(string address)
    {
        ArgumentNullException.ThrowIfNull(address);

        // The family is chosen by looking for a dot - PP236's test, and PP236's blind spot.
        IReadOnlyList<(string Prefix, int Length)> tests =
            PunchResponse.FamilyOf(address) == System.Net.Sockets.AddressFamily.InterNetwork
                ? Ipv4Tests
                : Ipv6Tests;

        return tests.Any(t => Strncmp(address, t.Prefix, t.Length) == 0);
    }

    /// <summary>
    /// And whether it WOULD be judged private compared over each prefix's own length - which is
    /// plainly what was meant, and is not "one less" for all of them.
    /// </summary>
    public static bool IsLocalAsIntended(string address)
    {
        ArgumentNullException.ThrowIfNull(address);

        IReadOnlyList<(string Prefix, int Length)> tests =
            PunchResponse.FamilyOf(address) == System.Net.Sockets.AddressFamily.InterNetwork
                ? Ipv4Tests
                : Ipv6Tests;

        return tests.Any(t => Strncmp(address, t.Prefix, t.Prefix.Length) == 0);
    }

    /// <summary>
    /// By how much each test overshoots its prefix - one for the literals, two for the generated.
    /// </summary>
    public static int Overshoot((string Prefix, int Length) test) => test.Length - test.Prefix.Length;

    /// <summary>
    /// Which local candidate fills in the winner's mapped address.
    /// </summary>
    /// <param name="type">How the winning candidate was arrived at.</param>
    /// <param name="address">Its address.</param>
    public static MappedSource FillFrom(CandidateType type, string address)
    {
        ArgumentNullException.ThrowIfNull(address);

        // The private test is only consulted for a discovered candidate.
        bool local = type == CandidateType.Derived && IsLocalAsWritten(address);

        return type == CandidateType.Local || local ? MappedSource.Directly : MappedSource.ViaStun;
    }

    /// <summary>And which it would be if the tests worked.</summary>
    public static MappedSource FillFromIfTheTestsWorked(CandidateType type, string address)
    {
        ArgumentNullException.ThrowIfNull(address);

        bool local = type == CandidateType.Derived && IsLocalAsIntended(address);

        return type == CandidateType.Local || local ? MappedSource.Directly : MappedSource.ViaStun;
    }
}

/// <summary>
/// PP248: the classification where the core writes it.
/// </summary>
public static class PrivateAddressSource
{
    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => PushNotificationSource.Locate();

    /// <summary>Whether every prefix test is still compared over one byte more than it needs.</summary>
    public static bool EveryTestIsStillOneByteTooLong(string core)
    {
        string body = Body(core);

        foreach ((string prefix, int length) in PrivateAddress.Ipv4Tests.Take(2)
            .Concat(PrivateAddress.Ipv6Tests))
        {
            if (!body.Contains(
                $"strncmp(selected_candidate->addr, \"{prefix}\", {length})", StringComparison.Ordinal))
            {
                return false;
            }

            // The literal plus its terminator - which is what makes it unmatchable.
            if (prefix.Length + 1 != length)
                return false;
        }

        return true;
    }

    /// <summary>
    /// And the generated ones too: a nine-byte buffer, filled for sixteen through thirty-one, and
    /// compared over nine.
    /// </summary>
    public static bool TheGeneratedTestsAreStillTheSame(string core)
    {
        string body = Body(core);

        return body.Contains("for (int j = 16; j < 32; j++)", StringComparison.Ordinal)
            && body.Contains("char compare_addr[9] = {0};", StringComparison.Ordinal)
            && body.Contains("sprintf(compare_addr, \"172.%d.\", j);", StringComparison.Ordinal)
            && body.Contains(
                "strncmp(selected_candidate->addr, compare_addr, 9)", StringComparison.Ordinal);
    }

    /// <summary>Whether the flag is still only computed for a discovered candidate.</summary>
    public static bool TheFlagIsStillOnlyForDiscoveredCandidates(string core)
        => Body(core).Contains(
            "if(selected_candidate->type == CANDIDATE_TYPE_DERIVED)", StringComparison.Ordinal);

    /// <summary>And whether an explicitly local candidate still takes the local branch regardless.</summary>
    public static bool AnExplicitlyLocalCandidateStillTakesIt(string core)
        => Body(core).Contains(
            "if(selected_candidate->type == CANDIDATE_TYPE_LOCAL || local)", StringComparison.Ordinal);

    /// <summary>
    /// Whether both branches still read the LOCAL candidate array - including the one whose
    /// variable is named for the remote end.
    /// </summary>
    public static bool BothBranchesStillReadTheLocalArray(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        string text = core.Replace("\r\n", "\n", StringComparison.Ordinal);

        return text.Contains("Candidate *local_candidate = &local_candidates[0];", StringComparison.Ordinal)
            && text.Contains("Candidate *remote_candidate = &local_candidates[1];", StringComparison.Ordinal)
            && Body(core).Contains(
                "memcpy(selected_candidate->addr_mapped, remote_candidate->addr,", StringComparison.Ordinal);
    }

    /// <summary>The classification, from the wipe to the copy out.</summary>
    private static string Body(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        string text = core.Replace("\r\n", "\n", StringComparison.Ordinal);

        int start = text.IndexOf(
            "    memset(selected_candidate->addr_mapped, 0,", StringComparison.Ordinal);
        if (start < 0)
            return "";

        int end = text.IndexOf(
            "    memcpy(out_candidate, selected_candidate,", start, StringComparison.Ordinal);
        return end < 0 ? text[start..] : text[start..end];
    }
}
