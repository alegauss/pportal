using System.Text.RegularExpressions;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>One log message that opens with a function name, and the function it is actually in.</summary>
/// <param name="In">The enclosing function.</param>
/// <param name="Claims">The name its message opens with.</param>
/// <param name="Message">The message, for a failure that has to be recognisable.</param>
public readonly record struct PrefixedLog(string In, string Claims, string Message)
{
    /// <summary>Whether the prefix is the function the log is in.</summary>
    public bool NamesItsOwnFunction => string.Equals(In, Claims, StringComparison.Ordinal);

    /// <summary>Said the way a failure should read.</summary>
    public override string ToString() => $"{In}() logs \"{Claims}: {Message}\"";
}

/// <summary>
/// PP390: the logs in holepunch.c whose prefix names a function, and the decisions about them.
///
/// The file prefixes its messages with a function name - 284 of them where the name is one this
/// translation unit defines. Seven name a different function, and ALL SEVEN ARE ALREADY DECIDED:
///
///   deleteSession's two say http_send_session_message. Genuinely misnamed, and reproduced on
///   purpose by PP235 - correcting them would make this port's logs disagree with every report ever
///   written against the Qt client's. They are <see cref="MisnamedLogs.All"/>.
///
///   Five say check_candidates from its own helpers. PP237 called those misnamed and PP238
///   corrected it: the prefix names the OPERATION a reader is following, and across a call tree
///   that is defensible - a log changing name three times inside one exchange would be worse. They
///   are <see cref="MisnamedLogs.NamesTheOperationNotTheFunction"/>.
///
/// PP389 WAS THE FOURTH REDISCOVERY, after PP237 and PP256. It swept the C, found the seven, fixed
/// them, and was retired - and the only thing that caught it was an unrelated check going red,
/// which is luck rather than a gate. So this class does not assert that no log wears another name.
/// It asserts that the set which does is EXACTLY what those two lists declare, which is the
/// question a fifth sweep should be answered with.
///
/// THE RULE ONLY JUDGES A PREFIX THAT IS A FUNCTION NAME IN THIS FILE. Plenty of messages open with
/// something else - a subsystem, a field, a word - and those claim nothing about where they are.
/// </summary>
public static partial class LogPrefixIdentity
{
    /// <summary>Where the convention lives.</summary>
    public const string RelativePath = @"lib\src\remote\holepunch.c";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    [GeneratedRegex(@"(?m)^(?:static |CHIAKI_EXPORT )[\w \*]+? (?<fn>\w+)\(")]
    private static partial Regex Declaration();

    [GeneratedRegex(@"CHIAKI_LOG[EWIV]\([^,]+,\s*""(?<claims>\w+): (?<message>[^""]*)""")]
    private static partial Regex PrefixedMessage();

    /// <summary>Every function this translation unit names, declaration or definition.</summary>
    public static IReadOnlySet<string> FunctionsIn(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return Declaration().Matches(source).Select(m => m.Groups["fn"].Value).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Every log whose prefix is a function name this file defines, with the function it sits in.
    ///
    /// A prefix that names nothing in the file is skipped rather than judged - see the class note.
    ///
    /// NOT THROUGH CFunction, AND THAT IS A FINDING RATHER THAN A PREFERENCE. That reader counts
    /// braces, and holepunch.c builds JSON: `{` and `}` inside string literals are not scope, so a
    /// body taken by counting runs past the function and swallows the several after it. Two versions
    /// of this reader reported the same six logs as misattributed when each was sitting in exactly
    /// the function it named.
    ///
    /// So a function's span here runs from its own declaration line to the NEXT one, which needs no
    /// braces at all. Prototypes match too and are harmless: they sit together at the top of the
    /// file, so the spans between them hold no logs.
    /// </summary>
    public static IReadOnlyList<PrefixedLog> AttributionsIn(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        IReadOnlySet<string> functions = FunctionsIn(source);
        var found = new List<PrefixedLog>();

        Match[] declarations = [.. Declaration().Matches(source).Cast<Match>()];

        for (var i = 0; i < declarations.Length; i++)
        {
            int from = declarations[i].Index;
            int to = i + 1 < declarations.Length ? declarations[i + 1].Index : source.Length;

            foreach (Match log in PrefixedMessage().Matches(source[from..to]))
            {
                string claims = log.Groups["claims"].Value;
                if (functions.Contains(claims))
                    found.Add(new PrefixedLog(declarations[i].Groups["fn"].Value, claims, log.Groups["message"].Value));
            }
        }

        return found;
    }

    /// <summary>The ones whose prefix is another function's name.</summary>
    public static IReadOnlyList<PrefixedLog> WearingAnothersName(IEnumerable<PrefixedLog> logs)
    {
        ArgumentNullException.ThrowIfNull(logs);

        return logs.Where(l => !l.NamesItsOwnFunction).ToList();
    }

    /// <summary>
    /// PP390: the ones wearing another name that neither decision accounts for.
    ///
    /// This is the whole of the rule. An empty answer means the file is exactly what PP235 and
    /// PP238 say it is; a non-empty one is either a NEW misattribution, or a decision that has been
    /// made and not written down - and either way it is a question rather than a fix, which is what
    /// PP389 got wrong.
    /// </summary>
    public static IReadOnlyList<PrefixedLog> UnaccountedFor(IEnumerable<PrefixedLog> logs)
    {
        ArgumentNullException.ThrowIfNull(logs);

        return WearingAnothersName(logs)
            .Where(l => !MisnamedLogs.NamesTheOperationNotTheFunction.Contains(l.In, StringComparer.Ordinal))
            .Where(l => !MisnamedLogs.All.Any(m => string.Equals(m.Function, l.In, StringComparison.Ordinal)))
            .ToList();
    }

    /// <summary>
    /// And the reverse: a function the lists claim wears another name, which no longer does.
    ///
    /// A list that outlives what it describes is the failure PP256's comment exists against - it
    /// records a decision about lines that may since have moved. Asked so the two lists are held to
    /// the file rather than only the file to them.
    /// </summary>
    public static IReadOnlyList<string> DeclaredButNoLongerWearingAnothersName(IEnumerable<PrefixedLog> logs)
    {
        ArgumentNullException.ThrowIfNull(logs);

        IReadOnlyList<PrefixedLog> wearing = WearingAnothersName(logs);

        return MisnamedLogs.NamesTheOperationNotTheFunction
            .Where(f => !wearing.Any(l => string.Equals(l.In, f, StringComparison.Ordinal)))
            .ToList();
    }

    /// <summary>
    /// The messages that reach a log under two different names, which is what makes one ungreppable.
    ///
    /// NOT a rule over the file, and the difference matters. Twelve CURL setopt failures share a
    /// sentence across a dozen functions, and that is the convention WORKING: the prefix is what
    /// tells them apart, so the same words under two correct names are two distinguishable logs.
    /// The first version of this asserted no message appears twice and reported all twelve, which
    /// would have been a rule against the thing being checked.
    ///
    /// It is worth something only where one NAME plus one sentence is reachable from two different
    /// functions, which is what a copy left standing beside its original produces: the grep finds
    /// both and cannot choose. Three of PP389's seven were exactly that.
    ///
    /// The axis matters and the first version had it wrong. It grouped by message and asked whether
    /// the claimed NAME varied - but a copy keeps the original's prefix, so both say the same name
    /// and the variation is in the function they sit in. Asking the wrong axis reported the twelve
    /// CURL sentences and missed all three real ones.
    /// </summary>
    public static IReadOnlyList<string> MessagesUnderTwoNames(IEnumerable<PrefixedLog> logs)
    {
        ArgumentNullException.ThrowIfNull(logs);

        return logs
            .GroupBy(l => (l.Claims, l.Message))
            .Where(g => g.Select(l => l.In).Distinct(StringComparer.Ordinal).Count() > 1)
            .Select(g => $"{g.Key.Claims}: {g.Key.Message}")
            .ToList();
    }
}
