using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP312: the requirement names in roadkeep.toml against the ones the roadmap spells.
///
/// Two files, no reader in common, and both directions of disagreement are a real problem. A line
/// waiting on an undeclared name is a typo that reads exactly like a blocker. A declared name
/// nothing uses is a blocker that was lifted and never removed - which is the worse one, because it
/// says the project is still waiting for something it already has.
/// </summary>
public class BacklogRequirementsTests(ITestOutputHelper output)
{
    /// <summary>
    /// A config in the shape this project's carries, comments included - and one of those comments
    /// QUOTES a sentence, which is the shape that broke the first version of the reader.
    ///
    /// Each entry in the real table has a paragraph above it saying why the thing is absent, and
    /// one of them quotes PP284. An unpaired quote inside a comment desynchronises every pair after
    /// it: the first run against the real file found one name that was a fragment of prose and none
    /// of the four declared.
    /// </summary>
    private const string Config = """
        [requirements]
        declared = [
            # a PS5 on the LAN, reachable
            "console",
            # somebody looking, because the window says so - "a
            # person answers in one glance" - and a capture does not
            "a-person-looking",
            "runner",
        ]
        """;

    /// <summary>The array is the declaration; the prose explaining it is not, quotes and all.</summary>
    [Fact]
    public void OnlyTheArrayDeclares()
    {
        IReadOnlySet<string> declared = BacklogRequirements.Declared(Config);

        Assert.Equal(3, declared.Count);
        Assert.Contains("console", declared);
        Assert.Contains("a-person-looking", declared);
        Assert.Contains("runner", declared);

        // And nothing from inside the comment, which is where the fragment came from.
        Assert.DoesNotContain(declared, name => name.Contains("glance", StringComparison.Ordinal));

        // Every name is also written in the comment above it in the real file, and a comment is
        // not a declaration - so a config with only prose declares nothing.
        Assert.Empty(BacklogRequirements.Declared("# console and runner are what we wait on\n"));
    }

    /// <summary>A line's annotation is read, one name or several.</summary>
    [Fact]
    public void WhatALineWaitsOnIsRead()
    {
        IReadOnlySet<string> used = BacklogRequirements.Used("""
            - ⏳ **PP297** (deps: —) (requires: console) **symptom** — why. → §PP297
            - 📋 **PP302** (deps: —) (requires: signing-certificate, runner) **symptom** — why.
            - 📋 **PP311** (deps: —) **symptom** — why.
            """);

        Assert.Equal(3, used.Count);
        Assert.Contains("console", used);
        Assert.Contains("signing-certificate", used);
        Assert.Contains("runner", used);
    }

    /// <summary>
    /// THE DRIFT CHECK. Every name a line waits on is declared, and every declared name is waited
    /// on by something.
    /// </summary>
    [Fact]
    public void TheDeclaredNamesAndTheUsedOnesAreTheSame()
    {
        string? configPath = BacklogRequirements.LocateConfig();
        string? roadmapPath = BacklogRequirements.LocateRoadmap();
        Assert.True(configPath is not null && roadmapPath is not null, "not running out of a checkout");

        IReadOnlySet<string> declared = BacklogRequirements.Declared(File.ReadAllText(configPath));
        IReadOnlySet<string> used = BacklogRequirements.Used(File.ReadAllText(roadmapPath));

        output.WriteLine("declared: " + string.Join(", ", declared));
        output.WriteLine("used:     " + string.Join(", ", used));

        Assert.NotEmpty(declared);

        string[] undeclared = [.. used.Where(name => !declared.Contains(name))];
        Assert.True(undeclared.Length == 0,
            "these lines wait on a name roadkeep.toml does not declare, so a typo reads as a real "
                + "blocker: " + string.Join(", ", undeclared));

        string[] unused = [.. declared.Where(name => !used.Contains(name))];
        Assert.True(unused.Length == 0,
            "these are declared and nothing waits on them, so the backlog says it is still waiting "
                + "for something it has: " + string.Join(", ", unused));
    }

    /// <summary>
    /// PP486, THE THIRD CHECK: no open line says in its own prose that it needs something its
    /// requires group leaves out.
    ///
    /// The two checks above hold each set against the other, and both were green while PP481 said
    /// "no test can exercise one without a live console" and declared nothing at all. `pick` reads
    /// the group and not the sentence, so it offered that line as the next ready thing to do from the
    /// moment it was filed - and the session that filed it then reported the block as gated on a
    /// decision rather than on hardware.
    /// </summary>
    [Fact]
    public void NoOpenLineNeedsSomethingItDoesNotDeclare()
    {
        if (BacklogRequirements.LocateRoadmap() is not { } roadmapPath)
            return;

        IReadOnlyList<BacklogRequirements.RequirementGap> gaps =
            BacklogRequirements.Gaps(File.ReadAllText(roadmapPath));

        foreach (BacklogRequirements.RequirementGap gap in gaps)
            output.WriteLine($"{gap.Id}: says \"{gap.Phrase}\", does not require {gap.Requirement}");

        Assert.True(
            gaps.Count == 0,
            "these lines say in words that they need something and do not declare it, so pick offers "
                + "them as ready: "
                + string.Join(", ", gaps.Select(g => $"{g.Id} ({g.Requirement})")));
    }

    /// <summary>
    /// PP501: PP27 declares the console its remaining half needs.
    ///
    /// The line PP486's check was built for, arriving a second time and from further away. PP481
    /// said "a live console" in its own prose and declared nothing; PP27 said "timed against the C
    /// on one capture", which names no hardware at all and is the same fact - a capture of takion's
    /// datagrams needs a session reaching the stream. So `pick` answered PP27 for twelve consecutive
    /// iterations, rightly for eleven of them, and would have gone on answering it.
    ///
    /// Named on this line and not left to the sweep above, because the sweep passes the moment the
    /// prose is reworded and this does not: the claim is that PP27 in particular is not startable
    /// here, and it stops being true when someone plugs a console in and passes --have.
    /// </summary>
    [Fact]
    public void PP27DeclaresTheConsoleItsTimingRunNeeds()
    {
        if (BacklogRequirements.LocateRoadmap() is not { } roadmapPath)
            return;

        string? line = File.ReadAllLines(roadmapPath)
            .FirstOrDefault(l => l.Contains("**PP27**", StringComparison.Ordinal));

        // Gone from the roadmap means shipped, and a shipped line's requirement was met.
        if (line is null)
            return;

        Assert.Contains("console", BacklogRequirements.Used(line));
        Assert.Empty(BacklogRequirements.Gaps(line));
    }

    /// <summary>A line whose prose names hardware and whose group is empty is the gap.</summary>
    [Fact]
    public void AGapIsWhereTheProseSaysItAndTheGroupDoesNot()
    {
        IReadOnlyList<BacklogRequirements.RequirementGap> gaps = BacklogRequirements.Gaps(
            "- 📋 **PP9999** (deps: —) **no test can exercise one without a live console** — why. → §PP9999");

        BacklogRequirements.RequirementGap gap = Assert.Single(gaps);
        Assert.Equal("PP9999", gap.Id);
        Assert.Equal("console", gap.Requirement);
    }

    /// <summary>And a line that declares it is not, which is what PP322 has always done.</summary>
    [Fact]
    public void ALineThatDeclaresWhatItNeedsIsNotAGap()
    {
        Assert.Empty(BacklogRequirements.Gaps(
            "- ⏳ **PP9998** (deps: —) (requires: console) **nothing without a live console** — why."));

        Assert.Empty(BacklogRequirements.Gaps(
            "- ⏳ **PP9997** (deps: —) (requires: a-person-looking) **needs a person looking** — why."));
    }

    /// <summary>
    /// The phrases are necessity and not mention, because this backlog talks about consoles
    /// constantly and a guard that flagged every one of those would be switched off in a week.
    /// </summary>
    [Theory]
    [InlineData("- 📋 **PP9996** (deps: —) **the console sends SDR on most titles** — why.")]
    [InlineData("- 📋 **PP9995** (deps: —) **a console answers with its own version** — why.")]
    public void MentioningAConsoleIsNotNeedingOne(string line)
        => Assert.Empty(BacklogRequirements.Gaps(line));

    /// <summary>
    /// A phrase in a clause about another task is a report, not a need of this line's own.
    ///
    /// The line that added this check flagged itself on the first run for exactly this reason: its
    /// symptom quotes PP481's words in order to say PP481 is missing a declaration. A guard that
    /// cannot tell those apart flags every line written about requirements.
    /// </summary>
    [Fact]
    public void APhraseAboutAnotherLineIsAReportAndNotANeed()
        => Assert.Empty(BacklogRequirements.Gaps(
            "- 📋 **PP9994** (deps: —) **PP481 says no test runs without a live console and declares "
                + "nothing** — why."));

    /// <summary>
    /// But only before the phrase. A line making its own claim and citing a neighbour afterwards is
    /// still making its own claim.
    /// </summary>
    [Fact]
    public void CitingAnotherLineAfterTheClaimStillLeavesTheClaim()
    {
        IReadOnlyList<BacklogRequirements.RequirementGap> gaps = BacklogRequirements.Gaps(
            "- 📋 **PP9993** (deps: —) **no test can run without a live console** — as PP481 found.");

        Assert.Equal("console", Assert.Single(gaps).Requirement);
    }

    /// <summary>Only task lines are read - a rationale paragraph is prose about the work.</summary>
    [Fact]
    public void OnlyTaskLinesAreRead()
        => Assert.Empty(BacklogRequirements.Gaps(
            "Some paragraph explaining that this needs a live console to finish at all.\n"));
}
