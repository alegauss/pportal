using System.Text.RegularExpressions;

namespace ChiakiNg.Session;

/// <summary>
/// PP316: what the build does with a warning, held against the projects the gate builds.
///
/// A warning is a message with no recipient. compile.cmd prints hundreds of lines, test.cmd prints
/// hundreds more, and nothing between them stops on one - so xUnit2000 named an assertion that
/// compared two literals and could not fail, in every run, from the commit that wrote it until a
/// session read the log for an unrelated reason. Three others were keeping it company: xUnit2029 on
/// an assertion that could not name what broke it, CS0108 on a property hiding the one it inherited,
/// CS8123 on a tuple element name the compiler dropped.
///
/// The policy is therefore <c>TreatWarningsAsErrors</c> and not a list of codes. PP22 named IL3000
/// through IL3002 for a reason that was specific and correct, and the list still could not have
/// caught any of the four - which is PP279's finding about the root-file list arriving one file out.
///
/// Two projects, because two are what the gate builds: the host and the test assembly that
/// references it. spike\, gate\ and tools\ carry csproj files of their own and no gate compiles
/// them, so asserting a policy there would be asserting something nothing enforces.
/// </summary>
public static partial class WarningPolicy
{
    /// <summary>The test assembly. The host is <see cref="BuildWorkflow.HostProjectRelativePath"/>.</summary>
    public const string TestProjectRelativePath = @"tests\ChiakiNg.Tests\ChiakiNg.Tests.csproj";

    /// <summary>
    /// The projects a gate in this tree compiles, and therefore the ones this policy binds.
    /// </summary>
    public static IReadOnlyList<string> GatedProjects { get; } =
        [BuildWorkflow.HostProjectRelativePath, TestProjectRelativePath];

    /// <summary>
    /// The only warning code this port suppresses outright, and the project it is suppressed in.
    ///
    /// Named here so that <see cref="SuppressedIn"/> has something to be held against. NoWarn is
    /// the door this whole policy can be walked back out of one code at a time, and a suppression
    /// nobody has to justify is the state before PP316 with an extra step.
    /// </summary>
    public static IReadOnlySet<string> AllowedSuppressions { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "WPF0001" };

    /// <summary>The gated projects, resolved against this checkout; empty outside one.</summary>
    public static IReadOnlyList<string> LocateGatedProjects() =>
        [.. GatedProjects.Select(SanitizerSource.LocateRelative).OfType<string>()];

    /// <summary>
    /// Whether a project text turns every compiler warning into an error.
    ///
    /// The value is read rather than assumed present: <c>&lt;TreatWarningsAsErrors&gt;false&lt;/&gt;</c>
    /// is the shape a future disabling would take, and a check testing only for the element's
    /// presence would call that a pass.
    /// </summary>
    public static bool RefusesEveryWarning(string projectText)
    {
        ArgumentNullException.ThrowIfNull(projectText);

        Match match = TreatWarningsAsErrorsRegex().Match(projectText);
        return match.Success
            && string.Equals(match.Groups[1].Value.Trim(), "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Every warning code a project text silences, in declaration order and without duplicates.
    ///
    /// <c>$(NoWarn)</c> and any other MSBuild reference is dropped: it names whatever the SDK
    /// already put there, which is not this port's decision and not a code.
    /// </summary>
    public static IReadOnlyList<string> SuppressedIn(string projectText)
    {
        ArgumentNullException.ThrowIfNull(projectText);

        var codes = new List<string>();

        foreach (Match element in NoWarnRegex().Matches(projectText))
        {
            foreach (string token in element.Groups[1].Value.Split(
                [';', ',', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                if (token.Contains('$', StringComparison.Ordinal))
                    continue;

                if (!codes.Contains(token, StringComparer.OrdinalIgnoreCase))
                    codes.Add(token);
            }
        }

        return codes;
    }

    [GeneratedRegex(@"<TreatWarningsAsErrors>([^<]*)</TreatWarningsAsErrors>", RegexOptions.IgnoreCase)]
    private static partial Regex TreatWarningsAsErrorsRegex();

    [GeneratedRegex(@"<NoWarn>([^<]*)</NoWarn>", RegexOptions.IgnoreCase)]
    private static partial Regex NoWarnRegex();
}
