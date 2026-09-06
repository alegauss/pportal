using System.Globalization;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP38: the count that makes the non-goal a gate.
///
/// "No line ships without an assertion that fails without it" was a sentence in a file. This is the
/// number, and it may fall and may not rise - see <see cref="AssertionRatchet"/> for the join it
/// rests on and for how coarse that join deliberately is.
///
/// It is here rather than in the host's selftest because it is about the suites, and the suite is
/// what a runner runs (PP36). A ratchet that only reported where somebody remembered to look is the
/// discipline it exists to replace.
/// </summary>
public class AssertionRatchetTests(ITestOutputHelper output)
{
    /// <summary>
    /// A ledger in the shapes the real one carries, with ids that will never exist.
    ///
    /// FOUR DIGITS, deliberately, and it cost a red run to learn. The join is "this id is named
    /// somewhere in an assertion file", and a fixture IS an assertion file - so the first version
    /// of this sample, written with two real ids in it, marked both as covered and the debt fell by
    /// two for nothing. That is the failure the previous task warned about, arriving from the one
    /// direction nobody watches: not somebody gaming the number, but test data that happened to
    /// spell a real task. Nothing above 9000 will ever be one.
    /// </summary>
    private const string Ledger = """
        ## Block E — Windows-only build

        - ✅ **PP9001 (the first half)** **symptom here** — outcome here.
        - 🗑 **PP9002** **abandoned** — deleted before this line was filed.
        - ✅ **PP9003** **symptom here** — outcome here.
        """;

    /// <summary>Only the shipped marker, and a partial is its id.</summary>
    [Fact]
    public void TheLedgerNamesWhatShipped()
    {
        IReadOnlySet<string> shipped = AssertionRatchet.Shipped(Ledger);

        Assert.Contains("PP9001", shipped);
        Assert.Contains("PP9003", shipped);

        // Retired is work nobody built. Demanding a test for it would make the debt grow by not
        // doing things, which is the one way a ratchet can be actively harmful.
        Assert.DoesNotContain("PP9002", shipped);
    }

    /// <summary>
    /// PP305: the ledger's own sentence for each id, which is what makes the debt payable.
    ///
    /// A partial's qualifier lives inside the id's own asterisks - "**PP9001 (the first half)**" -
    /// so the symptom is the SECOND bold run and not the first. Read greedily, the id and the
    /// sentence come back as one string and every line of the list says the same useless thing.
    /// </summary>
    [Fact]
    public void EachShippedTaskCarriesTheSentenceTheLedgerGivesIt()
    {
        IReadOnlyDictionary<string, string> symptoms = AssertionRatchet.ShippedWithSymptom(Ledger);

        Assert.Equal("symptom here", symptoms["PP9001"]);
        Assert.Equal("symptom here", symptoms["PP9003"]);
        Assert.False(symptoms.ContainsKey("PP9002"));
    }

    /// <summary>
    /// PP310: an exemption is a record, so the reason is what makes it one.
    ///
    /// A bare id would be something appended in a hurry; a sentence is something a reviewer reads.
    /// So a line naming an id and nothing else is not an exemption and does not count as one - the
    /// ratchet stays red, which is the right direction for a rule about not loosening rules.
    /// </summary>
    [Fact]
    public void AnExemptionWithNoReasonIsNotOne()
    {
        IReadOnlyDictionary<string, string> exempt = AssertionRatchet.ExemptionsIn("""
            # exempt PP9001 - a pass over a list; its whole output is prose.
            exempt PP9003 - a measurement; its result is the number, not a behaviour.
            # exempt PP9004
            # exempt PP9005 -
            96
            """);

        Assert.Equal(2, exempt.Count);
        Assert.Equal("a pass over a list; its whole output is prose.", exempt["PP9001"]);
        Assert.Contains("PP9003", exempt);

        // Named and not excused.
        Assert.DoesNotContain("PP9004", exempt);
        Assert.DoesNotContain("PP9005", exempt);
    }

    /// <summary>And an exempt task is out of the count rather than counted and forgiven.</summary>
    [Fact]
    public void WhatIsExemptIsNotInTheDebt()
    {
        string? root = SanitizerSource.RepositoryRoot();
        Assert.True(root is not null, "not running out of a checkout");

        IReadOnlyDictionary<string, string> exempt = AssertionRatchet.Exemptions(root);
        Assert.NotEmpty(exempt);

        IReadOnlyList<string> uncovered = AssertionRatchet.Uncovered(root);
        Assert.All(exempt.Keys, id => Assert.DoesNotContain(id, uncovered));

        // An exemption for something that never shipped is a line about nothing, and the likeliest
        // way to get one is a typo in an id.
        string? ledgerPath = AssertionRatchet.LocateLedger();
        Assert.True(ledgerPath is not null, "no ledger to check the exemptions against");

        IReadOnlySet<string> shipped = AssertionRatchet.Shipped(File.ReadAllText(ledgerPath));
        string[] unknown = [.. exempt.Keys.Where(id => !shipped.Contains(id))];

        Assert.True(unknown.Length == 0,
            "these are excused and never shipped, so they excuse nothing: " + string.Join(", ", unknown));
    }

    /// <summary>
    /// PP311: where an id is named, which is the question a surprising count asks.
    ///
    /// The join cannot be repaired by parsing - this tree writes some coverage claims as data, so
    /// an id inside a string literal is a claim in one file and a fixture in the next. What is left
    /// is audit, and this is it: a payment nobody made shows up as a line that reads like test data.
    /// </summary>
    [Fact]
    public void WhereAnIdIsNamedIsAnswerable()
    {
        string? root = SanitizerSource.RepositoryRoot();
        Assert.True(root is not null, "not running out of a checkout");

        // This file names it, in the summary above.
        IReadOnlyList<string> here = AssertionRatchet.WhereNamed(root, "PP311");
        Assert.NotEmpty(here);
        Assert.Contains(here, line => line.Contains("AssertionRatchetTests.cs", StringComparison.Ordinal));

        // Every line carries the file, the line number and the text, because the text is the half
        // that says whether the claim is real.
        Assert.All(here, line => Assert.Contains(":", line, StringComparison.Ordinal));

        // And something no assertion mentions answers nothing rather than throwing.
        //
        // PP739: DERIVED rather than chosen, which is what stopped this being stepped in. An id
        // spelled here is an id named in an assertion file, so the obvious form of this line -
        // asking where a made-up id is named - finds itself and fails. It had happened four times
        // when the comment above this said so, and a fifth time to the file that read the comment
        // and put the literal somewhere else. AnAbsentId asks the suites what they spell, so no
        // literal in any file can make this red again.
        string absent = AssertionRatchet.AnAbsentId(root);
        Assert.Empty(AssertionRatchet.WhereNamed(root, absent));
    }

    /// <summary>
    /// PP739: the absent id is absent because it was checked, not because it looked unused.
    ///
    /// The property that matters is not which number comes back - it is that whatever the suites
    /// spell, this is not one of them. So the test asserts against the union the sweep itself
    /// reads, which is the same set the derivation walked.
    /// </summary>
    [Fact]
    public void TheAbsentIdIsNamedByNoAssertionFile()
    {
        string? root = SanitizerSource.RepositoryRoot();
        Assert.True(root is not null, "not running out of a checkout");

        string absent = AssertionRatchet.AnAbsentId(root);

        output.WriteLine($"derived: {absent}");

        Assert.StartsWith(AssertionRatchet.Prefix, absent, StringComparison.Ordinal);
        Assert.True(
            int.Parse(absent[AssertionRatchet.Prefix.Length..], CultureInfo.InvariantCulture)
                >= AssertionRatchet.AbsentFloor,
            $"{absent} is below the floor the walk starts at");

        var spelled = new HashSet<string>(StringComparer.Ordinal);
        foreach (string file in AssertionRatchet.AssertionFiles(root))
        {
            foreach (string one in AssertionRatchet.Named(File.ReadAllText(file)))
                spelled.Add(one);
        }

        output.WriteLine($"{spelled.Count} distinct id(s) spelled across the suites");

        Assert.DoesNotContain(absent, spelled);

        // PP271: a derivation that returned something no file could ever name would also pass the
        // line above, so the sweep has to be shown finding an id that IS spelled. Built, not
        // written, or naming it here would be the very defect this closes.
        string fixture = AssertionRatchet.Prefix + 9901.ToString(CultureInfo.InvariantCulture);
        Assert.Contains(fixture, spelled);
    }

    /// <summary>
    /// PP743: THE FIXTURE FLOOR, as a check rather than as the comment it had been twice.
    ///
    /// An id named in an assertion file counts as covered, so a fixture spelling a number the
    /// backlog has not reached pays that task's debt before the task exists. The rule was written
    /// in this file's own fixture summary and again in StackedSummariesTests, and seven files broke
    /// it anyway - eleven ids between 900 and 999 against a ledger whose highest was 742.
    ///
    /// Empty is the rule holding. A row names the file and the id, because the fix is to move that
    /// id and the useful failure says which one.
    /// </summary>
    [Fact]
    public void NoFixtureIdSitsWhereTheBacklogWillReachIt()
    {
        string? root = SanitizerSource.RepositoryRoot();
        Assert.True(root is not null, "not running out of a checkout");

        IReadOnlyList<string> squatting = AssertionRatchet.FixturesBelowTheFloor(root);

        output.WriteLine(squatting.Count == 0 ? "none" : string.Join("\n", squatting));

        Assert.True(
            squatting.Count == 0,
            "these are fixture ids the backlog will reach, and each will be born covered:\n"
                + string.Join("\n", squatting));

        // PP271: empty would also be what a check that sees nothing reports. The suites DO carry
        // fixture ids, and what keeps them out of the list above is the floor - so both halves are
        // stated, and a sweep that stopped reading would fail here rather than pass quietly.
        string fixture = AssertionRatchet.Prefix + 9901.ToString(CultureInfo.InvariantCulture);

        Assert.DoesNotContain(fixture, AssertionRatchet.TaskIds(root));
        Assert.True(AssertionRatchet.NumberOf(fixture) >= AssertionRatchet.FixtureFloor);
        Assert.NotEmpty(AssertionRatchet.WhereNamed(root, fixture));
    }

    /// <summary>
    /// And the check reads TASK LINES rather than prose, which is what keeps it honest.
    ///
    /// Filing this task wrote its own example id into a roadmap sentence. Read as prose, that
    /// sentence would have excused the very fixture the task is about.
    /// </summary>
    [Fact]
    public void OnlyALineMakesAnIdATask()
    {
        string? root = SanitizerSource.RepositoryRoot();
        Assert.True(root is not null, "not running out of a checkout");

        IReadOnlySet<string> tasks = AssertionRatchet.TaskIds(root);

        output.WriteLine($"{tasks.Count} task id(s) across the governed files");

        // The ledger's own entries are lines, so a task this suite names is in there.
        Assert.Contains("PP38", tasks);

        // And every id the ratchet calls shipped is one of them, which is the join both rest on.
        string? ledger = AssertionRatchet.LocateLedger();
        Assert.True(ledger is not null, "no ledger");

        Assert.All(AssertionRatchet.Shipped(File.ReadAllText(ledger)), id => Assert.Contains(id, tasks));
    }

    /// <summary>And it is stable: two calls over the same tree agree.</summary>
    [Fact]
    public void TheAbsentIdIsTheSameOnEveryCall()
    {
        string? root = SanitizerSource.RepositoryRoot();
        Assert.True(root is not null, "not running out of a checkout");

        Assert.Equal(AssertionRatchet.AnAbsentId(root), AssertionRatchet.AnAbsentId(root));
    }

    /// <summary>
    /// PP314: a claim needs a file, the way an exemption needs a reason.
    ///
    /// A bare list of ids is something appended to in a hurry; an id with a path beside it is a
    /// statement that can be checked against the commit that made it.
    /// </summary>
    [Fact]
    public void AClaimWithNoFileIsNotOne()
    {
        IReadOnlyDictionary<string, IReadOnlyList<string>> claims = AssertionRatchet.IndexIn("""
            # PP9001 tests/Commented.cs
            PP9002 tests/ChiakiNg.Tests/RealTests.cs
            PP9003
            PP9005 tests/ChiakiNg.Tests/ModelTests.cs tests/ChiakiNg.Tests/ViewTests.cs
            """);

        Assert.Equal(2, claims.Count);
        Assert.Equal(["tests/ChiakiNg.Tests/RealTests.cs"], claims["PP9002"]);

        // PP315: several files is the ordinary case, not the exception - a screen ships its model
        // and its view together, and the commit that shipped it touched both.
        Assert.Equal(
            ["tests/ChiakiNg.Tests/ModelTests.cs", "tests/ChiakiNg.Tests/ViewTests.cs"],
            claims["PP9005"]);

        // A comment is not a claim, and a bare id is not one either.
        Assert.DoesNotContain("PP9001", claims);
        Assert.DoesNotContain("PP9003", claims);
    }

    /// <summary>
    /// And the real index: every file it names exists, and every id it claims actually shipped.
    ///
    /// Both directions matter. A path that is gone means the claim points at nothing - the file
    /// was renamed and the coverage went with it. An id that never shipped is a line about nothing,
    /// and the likeliest way to get one is a typo.
    /// </summary>
    [Fact]
    public void EveryClaimNamesAFileThatExistsAndATaskThatShipped()
    {
        string? root = SanitizerSource.RepositoryRoot();
        Assert.True(root is not null, "not running out of a checkout");

        IReadOnlyDictionary<string, IReadOnlyList<string>> claims = AssertionRatchet.Index(root);
        Assert.NotEmpty(claims);

        string[] missing =
        [
            .. claims
                .SelectMany(claim => claim.Value.Select(file => (claim.Key, file)))
                .Where(claim => !File.Exists(Path.Combine(root, claim.file.Replace('/', Path.DirectorySeparatorChar))))
                .Select(claim => $"{claim.Key} -> {claim.file}"),
        ];

        Assert.True(missing.Length == 0,
            "these claims name a file this tree does not have, so the coverage they record points "
                + "at nothing: " + string.Join(", ", missing));

        string? ledgerPath = AssertionRatchet.LocateLedger();
        Assert.True(ledgerPath is not null, "no ledger to check the index against");

        IReadOnlySet<string> shipped = AssertionRatchet.Shipped(File.ReadAllText(ledgerPath));
        string[] unknown = [.. claims.Keys.Where(id => !shipped.Contains(id))];

        Assert.True(unknown.Length == 0,
            "these are claimed and never shipped, so they cover nothing: " + string.Join(", ", unknown));
    }

    /// <summary>
    /// PP308: which of a commit's changed paths could hold an assertion.
    ///
    /// The same three places the walk covers, asked of a path instead - which is what lets the file
    /// list of a COMMIT be filtered the same way as a checkout. That filter is the whole of what
    /// the git diagnostic needs from this side; running git is the caller's.
    /// </summary>
    [Theory]
    [InlineData("tests/ChiakiNg.Tests/HolepunchIdentifiersTests.cs", true)]
    [InlineData("test/takion.c", true)]
    [InlineData("app/SelfTest.cs", true)]
    [InlineData(@"tests\ChiakiNg.Tests\Foo.cs", true)]
    [InlineData("app/Protocol/Candidate.cs", false)]
    [InlineData("lib/src/session.c", false)]
    [InlineData("tests/assertion-ratchet.txt", false)]
    [InlineData("docs/ROADMAP.md", false)]
    [InlineData("", false)]
    public void WhatCouldHoldAnAssertionIsRecognisedByPath(string path, bool expected)
        => Assert.Equal(expected, AssertionRatchet.IsAssertionPath(path));

    /// <summary>And a commit's file list becomes the assertion files in it, once each.</summary>
    [Fact]
    public void ACommitsFileListBecomesItsAssertionFiles()
    {
        IReadOnlyList<string> files = AssertionRatchet.AssertionFilesIn(
        [
            "app/Protocol/Candidate.cs",
            "tests/ChiakiNg.Tests/CandidateTests.cs",
            "docs/ROADMAP.md",
            "tests/ChiakiNg.Tests/CandidateTests.cs",
            "  test/fec.c  ",
        ]);

        Assert.Equal(["tests/ChiakiNg.Tests/CandidateTests.cs", "test/fec.c"], files);
    }

    /// <summary>
    /// An id is a whole id: the prefix of one is not found inside it.
    ///
    /// Every id here is above 900 for the reason the fixture above gives, and the NEGATIVE case is
    /// the one that catches people out. Written the obvious way - a two-digit id as the expected
    /// value of a DoesNotContain - it named a real shipped task in an assertion file, and paid that
    /// task's debt off by asserting that it had not been paid. The prose explaining this fell into
    /// it too, one paragraph after fixing it, which is how firmly the join and the file are the
    /// same thing: anything written here is a claim about coverage, comments included.
    /// </summary>
    [Fact]
    public void AnIdIsNotAPrefixOfAnother()
    {
        IReadOnlySet<string> named = AssertionRatchet.Named("/// PP9001: the wrap, and PP9000 beside it.");

        // PP743: EXACTLY these two, which is the negative case without spelling one. Written as a
        // DoesNotContain over a truncation of PP9000, the expected value is itself an id named in
        // an assertion file - and the shorter it is the likelier it is a real task. Asserting the
        // whole set says the same thing and leaves no such literal behind.
        string[] found = [.. named.Order(StringComparer.Ordinal)];
        Assert.Equal(["PP9000", "PP9001"], found);
    }

    /// <summary>The ceiling file explains itself, so the number is read past the comments.</summary>
    [Fact]
    public void TheCeilingIsReadPastWhatExplainsIt()
    {
        Assert.Equal(97, AssertionRatchet.CeilingIn("# why this exists\n#\n97\n"));

        // A file that carries no number is not a ceiling of zero, which would fail every run and
        // be fixed by deleting the check.
        Assert.Equal(-1, AssertionRatchet.CeilingIn("# nothing but prose\n"));
        Assert.Equal(-1, AssertionRatchet.CeilingIn(""));
    }

    /// <summary>
    /// THE RATCHET. It may fall and it may not rise, and both directions are a failure here.
    ///
    /// Above the ceiling means a task shipped with nothing naming it, and the commit that did it is
    /// the cheapest place that will ever be fixed. Below means the debt was paid and the ceiling was
    /// not lowered with it - which is not harmless: the ratchet has just quietly loosened by exactly
    /// what was gained, and the next task can ship uncovered for free.
    /// </summary>
    [Fact]
    public void TheDebtDoesNotGrow()
    {
        string? root = SanitizerSource.RepositoryRoot();
        Assert.True(root is not null, "not running out of a checkout");

        int ceiling = AssertionRatchet.Ceiling(root);
        Assert.True(ceiling >= 0, $"no readable ceiling in {AssertionRatchet.CeilingRelativePath}");

        IReadOnlyList<string> uncovered = AssertionRatchet.Uncovered(root);

        // A scan that stopped working reports zero debt and passes, which is this test's own
        // subject wearing the other hat.
        Assert.True(uncovered.Count > 0 || ceiling == 0,
            "no uncovered tasks found at all and the ceiling is not zero - the scan is not working");

        output.WriteLine($"{uncovered.Count} shipped task(s) named by no assertion, ceiling {ceiling}");

        string newest = string.Join(", ", uncovered.Take(12));

        Assert.False(
            uncovered.Count > ceiling,
            $"{uncovered.Count} shipped tasks are named by no assertion and the ceiling is {ceiling}. "
                + "The non-goal says no line ships without an assertion that fails without it, so "
                + "the task that just shipped needs one - newest uncovered: " + newest);

        Assert.False(
            uncovered.Count < ceiling,
            $"the debt fell to {uncovered.Count} and {AssertionRatchet.CeilingRelativePath} still "
                + $"says {ceiling}. Lower it in this commit: a ratchet that is not tightened when it "
                + "could be has given the gain away.");
    }
}
