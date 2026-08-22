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
}
