using System.Text.RegularExpressions;

namespace ChiakiNg.Session;

/// <summary>
/// PP309: the three things about OnStartup that a window has to exist for, asserted without one.
///
/// PP225 was the one code change in this port that shipped with nothing asserting it - PP307's pass
/// over the ratchet's ninety-six found seventy-five already tested under a neighbouring id and
/// eleven that a test could not have covered, and this was the twelfth. It is also the worst kind
/// to leave unasserted, because both halves of it fail SILENTLY.
///
/// Clearing StartupUri throws. The property refuses null, the exception is raised inside OnStartup
/// before anything is drawn, and the process dies with no window - so the first version showed
/// nothing at all and said nothing about why.
///
/// Calling the mapping screen from OnStartup finds no window. StartupUri creates MainWindow after
/// OnStartup RETURNS, so work that runs inline is work that runs too early; queued at
/// ApplicationIdle it runs behind the window that PP224 fills. The version that called it directly
/// did not throw and did not draw - it simply did nothing, which is the failure a test is for.
///
/// Read out of the source, because the alternative is a WPF application object per assertion and a
/// message loop to unwind. What is being held here is a decision about ordering, and the decision
/// is visible where it was made.
/// </summary>
public static partial class StartupSequence
{
    /// <summary>Where the startup ordering is decided, relative to the repository root.</summary>
    public const string RelativePath = @"app\App.xaml.cs";

    /// <summary>The source, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>
    /// Whether anything assigns null to StartupUri.
    ///
    /// The property refuses it and throws where it is set, which is inside OnStartup - so this is
    /// not a style rule. Any spelling of the assignment is the same crash, and the two that read
    /// naturally are matched: the bare name and the qualified one.
    /// </summary>
    public static bool ClearsStartupUri(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return ClearsStartupUriRegex().IsMatch(source);
    }

    /// <summary>
    /// Whether the mapping screen is QUEUED rather than called, and at an idle priority.
    ///
    /// Both halves. Queued at a priority above Loaded would run before the window is up, which is
    /// the same nothing-happens as calling it inline, so the priority is part of the decision
    /// rather than a detail of it.
    /// </summary>
    public static bool QueuesTheMappingScreen(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return source.Contains(
            "Dispatcher.BeginInvoke(StartMappingScreen, DispatcherPriority.ApplicationIdle)",
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the queueing happens after base.OnStartup, which is what creates the window.
    ///
    /// Queued before it, the work is scheduled against a dispatcher whose window does not exist
    /// yet - and every flag above IS handled before it, deliberately, because those exit instead of
    /// drawing. So the position of this one line is the whole distinction.
    /// </summary>
    public static bool QueuesAfterTheWindowIsCreated(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        int created = source.IndexOf("base.OnStartup(e);", StringComparison.Ordinal);
        int queued = source.IndexOf("Dispatcher.BeginInvoke(StartMappingScreen", StringComparison.Ordinal);

        return created >= 0 && queued > created;
    }

    // StartupUri = null, however it is qualified and however it is spaced.
    [GeneratedRegex(@"(?:^|[^A-Za-z0-9_.])(?:[A-Za-z0-9_.]+\.)?StartupUri\s*=\s*null")]
    private static partial Regex ClearsStartupUriRegex();
}
