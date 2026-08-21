namespace ChiakiNg.Protocol;

/// <summary>Every way one response's turn through the intake can end.</summary>
public enum IntakeExit
{
    /// <summary>The receive itself failed.</summary>
    ReceiveFailed,

    /// <summary>The address could not be printed.</summary>
    AddressUnprintable,

    /// <summary>Its family is not one of the two handled.</summary>
    UnsupportedFamily,

    /// <summary>It is a new address and there is no room left for extras.</summary>
    ExtrasFull,

    /// <summary>It is a new address and was taken on as one.</summary>
    NewCandidate,

    /// <summary>
    /// It is a candidate already known - the ordinary case, and the one with no release.
    /// </summary>
    KnownCandidate,
}

/// <summary>
/// PP246: reading one response and working out whose it was.
///
/// AN ALLOCATION PER RESPONSE, RELEASED ON EVERY BRANCH BUT THE COMMON ONE. A sockaddr is allocated
/// at the top of each turn. Six exits free it. The seventh - the address matching a candidate the
/// console already offered - does not, because the one release covering that stretch sits INSIDE the
/// branch for a new address rather than after it. That branch is what every response from every
/// offered candidate takes, so the leak is not on an error path: it is on the path a working
/// connection is made of.
///
/// Nothing here allocates, so there is no leak to reproduce. What this carries is the accounting -
/// <see cref="Releases"/> says which exits free and which does not - and the assertion that the core
/// still puts the release where it does.
///
/// THE INDEX AFTER THE SEARCH IS PAST THE END, AND THE GUARD ABOVE IT IS WHAT BOUNDS IT. A search
/// that matches nothing leaves the counter at one past the last used entry, and the branch for a new
/// address indexes with exactly that value. It is in bounds only because the guard refusing a fourth
/// extra runs first - so a limit that reads as a policy about how many candidates to accept is also
/// the only thing keeping an array index legal. Looks wrong and is not; see
/// <see cref="IndexIsInBounds"/>.
/// </summary>
public static class ResponseIntake
{
    /// <summary>How many extra addresses beyond the console's list are taken on.</summary>
    public const int ExtrasAllowed = PunchProbe.ExtraAddresses;

    /// <summary>Whether this exit releases the address it allocated.</summary>
    public static bool Releases(IntakeExit exit) => exit != IntakeExit.KnownCandidate;

    /// <summary>
    /// The exit one response takes.
    /// </summary>
    /// <param name="received">Whether the receive worked.</param>
    /// <param name="printable">Whether the address could be rendered.</param>
    /// <param name="supported">Whether its family is handled.</param>
    /// <param name="known">Whether it matches a candidate already held.</param>
    /// <param name="extrasUsed">How many extras have been taken on already.</param>
    public static IntakeExit Exit(
        bool received, bool printable, bool supported, bool known, int extrasUsed)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(extrasUsed);

        if (!received)
            return IntakeExit.ReceiveFailed;

        // The family is read BEFORE the address is printed, so an unsupported one never reaches the
        // printing at all.
        if (!supported)
            return IntakeExit.UnsupportedFamily;

        if (!printable)
            return IntakeExit.AddressUnprintable;

        if (known)
            return IntakeExit.KnownCandidate;

        return extrasUsed >= ExtrasAllowed ? IntakeExit.ExtrasFull : IntakeExit.NewCandidate;
    }

    /// <summary>
    /// Where a search that matched nothing leaves the index - one past the last used entry.
    /// </summary>
    public static int IndexAfterAMissedSearch(int candidateCount, int extrasUsed)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(candidateCount);
        ArgumentOutOfRangeException.ThrowIfNegative(extrasUsed);

        return candidateCount + extrasUsed;
    }

    /// <summary>
    /// Whether that index is legal, given the arrays are sized for the count plus the extras
    /// allowance.
    ///
    /// It is - but only for an extras count the guard has already passed. Feed it a value the guard
    /// would have refused and this says so.
    /// </summary>
    public static bool IndexIsInBounds(int candidateCount, int extrasUsed)
        => IndexAfterAMissedSearch(candidateCount, extrasUsed)
            < PunchProbe.SlotsFor(candidateCount);

    /// <summary>
    /// What a new candidate's mapped address is set to, per family - each an exact fit for its
    /// string and terminator, with the copy length written as a literal beside it.
    /// </summary>
    public static (string Text, int Copied) MappedPlaceholderFor(bool ipv4)
        => ipv4 ? ("0.0.0.0", 8) : ("0:0:0:0:0:0:0:0", 16);

    /// <summary>
    /// How many bytes are copied into a new candidate's address - the whole buffer, not the string.
    ///
    /// So whatever the printing left untouched beyond the terminator travels with it, and PP242
    /// copies that same field whole into the session.
    /// </summary>
    public const int AddressCopied = PunchAccept.AddressLength;

    /// <summary>What a new candidate is typed as.</summary>
    public const CandidateType NewCandidateType = CandidateType.Derived;
}

/// <summary>
/// PP246: the intake where the core writes it.
/// </summary>
public static class ResponseIntakeSource
{
    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => PushNotificationSource.Locate();

    /// <summary>Whether the address is still allocated once per response.</summary>
    public static bool TheAddressIsStillAllocatedPerResponse(string core)
        => Body(core).Contains(
            "recv_address = malloc(sizeof(struct sockaddr_in6));", StringComparison.Ordinal);

    /// <summary>
    /// THE LEAK. Whether the last release is still inside the new-address branch rather than after
    /// it - which is what leaves the known-address path with none.
    /// </summary>
    public static bool TheReleaseIsStillInsideTheNewAddressBranch(string core)
    {
        string body = Body(core);

        int branch = body.IndexOf("        if(!existing_candidate)\n", StringComparison.Ordinal);
        if (branch < 0)
            return false;

        // Indented to twelve, so inside the block that opens at eight - and the very next line
        // closes that block, so there is nothing after it covering the other path.
        int inside = body.IndexOf("\n            free(recv_address);\n        }\n", branch, StringComparison.Ordinal);
        if (inside < 0)
            return false;

        // And nothing frees it after the branch closes, before the loop turns over.
        int after = inside + "\n            free(recv_address);\n        }\n".Length;
        int turnsOver = body.IndexOf("        uint32_t msg_type", after, StringComparison.Ordinal);

        return turnsOver > after
            && !body[after..turnsOver].Contains("free(recv_address)", StringComparison.Ordinal);
    }

    /// <summary>And how many releases there are in total, which is one per exit but the common one.</summary>
    public static int ReleaseCount(string core)
        => Body(core).Split("free(recv_address);", StringSplitOptions.None).Length - 1;

    /// <summary>Whether the guard refusing a fourth extra still runs before the index is used.</summary>
    public static bool TheGuardStillRunsBeforeTheIndex(string core)
    {
        string body = Body(core);

        int guard = body.IndexOf(
            "if(extra_addresses_used >= EXTRA_CANDIDATE_ADDRESSES)", StringComparison.Ordinal);
        int indexed = body.IndexOf(
            "candidate = &candidates[i];\n                responses_received[i] = 0;", StringComparison.Ordinal);

        return guard >= 0 && indexed > guard;
    }

    /// <summary>And whether the search still leaves the index at one past the used entries.</summary>
    public static bool TheSearchStillRunsToTheEnd(string core)
        => Body(core).Contains(
            "for (; i < num_candidates + extra_addresses_used; i++)", StringComparison.Ordinal);

    /// <summary>Whether the address copy is still sized from the buffer rather than the string.</summary>
    public static bool TheAddressCopyIsStillWholeBuffer(string core)
        => Body(core).Contains(
            "memcpy(candidate->addr, recv_address_string, sizeof(recv_address_string));",
            StringComparison.Ordinal);

    /// <summary>And whether the two placeholders are still copied at their exact lengths.</summary>
    public static bool ThePlaceholdersAreStillExact(string core)
    {
        string body = Body(core);

        (string v4, int v4Len) = ResponseIntake.MappedPlaceholderFor(ipv4: true);
        (string v6, int v6Len) = ResponseIntake.MappedPlaceholderFor(ipv4: false);

        return body.Contains($"memcpy(candidate->addr_mapped, \"{v4}\", {v4Len});", StringComparison.Ordinal)
            && body.Contains($"memcpy(candidate->addr_mapped, \"{v6}\", {v6Len});", StringComparison.Ordinal)
            && v4.Length + 1 == v4Len
            && v6.Length + 1 == v6Len;
    }

    /// <summary>The intake, from the allocation to where the message type is read.</summary>
    private static string Body(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        string text = core.Replace("\r\n", "\n", StringComparison.Ordinal);

        // LAST, for the reason PP213, PP233, PP234, PP236, PP243, PP244 and PP245 each wrote down.
        int function = text.LastIndexOf(
            "static ChiakiErrorCode check_candidates(", StringComparison.Ordinal);
        if (function < 0)
            return "";

        int start = text.IndexOf("        struct sockaddr* recv_address;", function, StringComparison.Ordinal);
        if (start < 0)
            return "";

        int end = text.IndexOf("        if (msg_type == MSG_TYPE_REQ)", start, StringComparison.Ordinal);
        return end < 0 ? text[start..] : text[start..end];
    }
}
