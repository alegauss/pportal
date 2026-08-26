using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP331: a control message the library has no name for, reported as such.
///
/// PP297's capture is the first time anything in this tree watched a real control channel, and it
/// found a type the enum does not name: 0x41, payload 00-00-00-00-02-01-00-00, sent by the console
/// shortly after DISPLAYB on a session that connected and stayed up. Not an error path and not a
/// corner case - it arrived on the first capture ever taken.
///
/// WHAT THE C DID WITH IT IS THE DEFECT. The default arm of the dispatch switch hexdumped the
/// payload at WARNING, and the line above it - the one naming which type had arrived - was commented
/// out and had been since the fork. So every session logged eight anonymous bytes at a level meaning
/// something is broken, with nothing saying what was received or that anything was unhandled.
///
/// NAMING THE TYPE IS RESEARCH; REPORTING IT IS NOT. This does not claim to know what 0x41 is. It
/// holds the weaker and checkable half: a message the library cannot name says so, with its number,
/// at a level meaning unhandled.
///
/// THE PORT IS WHY IT MATTERS BEYOND TIDINESS. PP294 rewrites ctrl.c against this recording, and a
/// type the C silently drops is one the rewrite silently drops differently - the replay agrees, both
/// are wrong, and the disagreement surfaces as a stream behaving oddly much later. A named unknown
/// is something a replay can assert about; an anonymous hexdump is not.
/// </summary>
public static class UnhandledCtrlMessage
{
    /// <summary>Where the dispatch switch lives, relative to the repository root.</summary>
    public const string RelativePath = @"lib\src\ctrl.c";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>
    /// The one the capture holds. A constant rather than a name, because naming it would be a claim
    /// about what it means and nothing in this tree can make one.
    /// </summary>
    public const ushort Observed = 0x41;

    /// <summary>
    /// The prefix that identifies the dispatch function, distinct from the ten handlers whose names
    /// begin with it and from the prototype the file declares for each of them.
    /// </summary>
    public const string Dispatch = "ctrl_message_received(ChiakiCtrl *ctrl, uint16_t msg_type";

    /// <summary>Every type <see cref="CtrlMessage"/> names, which is every type the port can route.</summary>
    public static IReadOnlySet<ushort> Named { get; } =
        Enum.GetValues<CtrlMessage>().Select(m => (ushort)m).ToHashSet();

    /// <summary>Whether the port has a name for this type.</summary>
    public static bool IsNamed(ushort type) => Named.Contains(type);

    /// <summary>The ones it does not, in the order they arrived and without repeats.</summary>
    public static IReadOnlyList<ushort> UnnamedIn(IEnumerable<ushort> types)
    {
        ArgumentNullException.ThrowIfNull(types);

        return [.. types.Where(t => !IsNamed(t)).Distinct()];
    }

    /// <summary>
    /// The default arm of the dispatch switch, from its <c>default:</c> to the end of the function,
    /// or null where the function cannot be found.
    ///
    /// The LAST <c>default:</c> in the body, because that is the one belonging to this switch: an
    /// arm added above it would otherwise be read as the whole tail.
    /// </summary>
    public static string? DefaultArm(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        string? body = CFunction.Body(source, Dispatch);
        if (body is null)
            return null;

        int at = body.LastIndexOf("default:", StringComparison.Ordinal);
        return at < 0 ? null : body[at..];
    }

    /// <summary>
    /// Whether the arm says which type arrived, by interpolating the value rather than spelling a
    /// sentence about it.
    ///
    /// The number is the whole point: "unknown type" without one tells a reader exactly as little
    /// as the hexdump did.
    /// </summary>
    public static bool ItNamesTheType(string arm)
    {
        ArgumentNullException.ThrowIfNull(arm);

        // Joined rather than read line by line: a log call whose arguments wrap is the ordinary
        // shape here, and the value is on the continuation line in exactly the case that matters.
        string active = string.Join(' ', Active(arm));

        for (int at = active.IndexOf("CHIAKI_LOG", StringComparison.Ordinal);
             at >= 0;
             at = active.IndexOf("CHIAKI_LOG", at + 1, StringComparison.Ordinal))
        {
            int end = active.IndexOf(");", at, StringComparison.Ordinal);
            string call = end < 0 ? active[at..] : active[at..end];

            if (call.Contains("msg_type", StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Whether the arm reports at a level meaning unhandled rather than broken.
    ///
    /// Both halves: the naming line and the hexdump beneath it. A hexdump left at
    /// <c>CHIAKI_LOG_WARNING</c> under an informational sentence still colours the session red for
    /// something that is not wrong.
    /// </summary>
    public static bool ItReportsAsUnhandled(string arm)
    {
        ArgumentNullException.ThrowIfNull(arm);

        foreach (string line in Active(arm))
        {
            if (line.Contains("CHIAKI_LOGW", StringComparison.Ordinal)
                || line.Contains("CHIAKI_LOGE", StringComparison.Ordinal)
                || line.Contains("CHIAKI_LOG_WARNING", StringComparison.Ordinal)
                || line.Contains("CHIAKI_LOG_ERROR", StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Whether the arm still carries a commented-out log, which is how this defect was written in
    /// the first place and the shape it would return in.
    ///
    /// A comment that mentions logging is fine - the prose above the branch does. A comment that IS
    /// a log call is the thing: a line somebody disabled instead of deleting, left where the next
    /// reader takes it for the behaviour.
    /// </summary>
    public static bool ItCarriesACommentedOutLog(string arm)
    {
        ArgumentNullException.ThrowIfNull(arm);

        foreach (string raw in arm.Split('\n'))
        {
            string line = raw.Trim();
            if (!line.StartsWith("//", StringComparison.Ordinal))
                continue;

            // A call, not a mention: the name followed by its opening parenthesis.
            if (line.Contains("CHIAKI_LOG", StringComparison.Ordinal)
                && line.Contains("(ctrl->session->log", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The lines of the arm that are not comments.</summary>
    private static IEnumerable<string> Active(string arm)
    {
        foreach (string raw in arm.Split('\n'))
        {
            string line = raw.Trim();
            if (!line.StartsWith("//", StringComparison.Ordinal))
                yield return line;
        }
    }
}
