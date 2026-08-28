using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP509: the deferred store, read for work it is hiding from a caller who could do it.
///
/// PP486's check reads open roadmap lines for a need they do not declare. This reads set-aside
/// reasons for a need the project already declares - the same mistake, pointing the other way.
/// </summary>
public class DeferredReasonsTests(ITestOutputHelper output)
{
    private static IReadOnlySet<string> Declared()
    {
        if (BacklogRequirements.LocateConfig() is not { } path)
            return new HashSet<string>();

        return BacklogRequirements.Declared(File.ReadAllText(path));
    }

    /// <summary>The reason is the parenthesised clause, and nothing else on the line.</summary>
    [Fact]
    public void OnlyTheReasonIsRead()
    {
        const string line =
            "- ⏸ **PP76** (deps: —) **the decoder preference is measured on synthetic frames** "
            + "— set aside (needs a console): drops follow the network. → §PP76";

        Assert.Equal("needs a console", DeferredReasons.ReasonIn(line));
    }

    /// <summary>A line with no set-aside clause has no reason rather than an empty one.</summary>
    [Fact]
    public void ALineWithNoClauseHasNoReason()
    {
        Assert.Null(DeferredReasons.ReasonIn("## Block I — NVIDIA path"));
        Assert.Null(DeferredReasons.ReasonIn("- 📋 **PP1** (deps: —) **s** — w. → §PP1"));
    }

    /// <summary>
    /// A reason naming a declared requirement is the finding, and the symptom is not read.
    ///
    /// The second half matters: a deferred line keeps its own symptom and why, and those are about
    /// the port's subject. A check that read the whole line would flag every line mentioning a
    /// console, which in this backlog is most of them.
    /// </summary>
    [Fact]
    public void AReasonNamingADeclaredRequirementIsTheFinding()
    {
        IReadOnlySet<string> declared = new HashSet<string> { "console" };

        const string hidden =
            "- ⏸ **PP76** (deps: —) **s** — set aside (needs a console): why. → §PP76";
        const string subject =
            "- ⏸ **PP93** (deps: —) **the console sends SDR on most titles** "
            + "— set aside (a PS4 and a PS5 in the room): why. → §PP93";

        Assert.Single(DeferredReasons.Hidden(hidden, declared));
        Assert.Empty(DeferredReasons.Hidden(subject, declared));
    }

    /// <summary>
    /// A reason naming something the project does NOT declare is not a finding.
    ///
    /// PP55 is set aside for "no Reflex-capable display", which is a real absence and not one this
    /// project has given a name to. Un-deferring it would put a line on the roadmap that no caller
    /// can ask for.
    /// </summary>
    [Fact]
    public void AnUndeclaredAbsenceIsNotAFinding()
    {
        const string line =
            "- ⏸ **PP55** (deps: —) **s** — set aside (no Reflex-capable display on this machine): w. → §PP55";

        Assert.Empty(DeferredReasons.Hidden(line, new HashSet<string> { "console" }));
    }

    /// <summary>
    /// THE RULE: no set-aside line's reason names a requirement this project declares.
    ///
    /// Three did - PP50, PP72 and PP76, all waiting on a console the project has - and none of them
    /// was reachable by `pick --have console`, because pick reads the roadmap and the store is
    /// another file. They are roadmap lines now, each declaring it.
    /// </summary>
    [Fact]
    public void NoSetAsideReasonNamesSomethingTheProjectDeclares()
    {
        if (DeferredReasons.Locate() is not { } path)
            return;

        IReadOnlyList<HiddenByReason> hidden =
            DeferredReasons.Hidden(File.ReadAllText(path), Declared());

        foreach (HiddenByReason line in hidden)
            output.WriteLine($"{line.Id}: reason says \"{line.Phrase}\", and {line.Requirement} is declared");

        Assert.True(
            hidden.Count == 0,
            "these are set aside for something the project declares, so a caller that has it cannot "
                + "be offered them: " + string.Join(", ", hidden.Select(h => $"{h.Id} ({h.Requirement})")));
    }

    /// <summary>
    /// And the three that moved are on the roadmap declaring it.
    ///
    /// Both halves, because resuming without declaring would put them where `pick` reads and still
    /// offer them to a caller with no console.
    /// </summary>
    [Theory]
    [InlineData("PP50")]
    [InlineData("PP72")]
    [InlineData("PP76")]
    public void TheThreeThatMovedDeclareTheConsole(string id)
    {
        if (BacklogDeps.LocateRoadmap() is not { } path)
            return;

        string roadmap = File.ReadAllText(path);

        // Shipped means the line is gone and the requirement was met.
        if (BacklogDeps.LineFor(roadmap, id) is not { } line)
            return;

        Assert.Contains("console", BacklogRequirements.Used(line));
    }
}
