using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP618: the single-threaded runner exists once, from a package, and nowhere else.
///
/// Twenty-six view test files each carried their own copy of it — twenty-five bounded at thirty
/// seconds and one at sixty. That was not boilerplate that happens to repeat: a control cannot be
/// constructed off that apartment at all, and a thread that never finishes reports NOTHING — no
/// pass, no failure, no name — so the wrapper is the only thing between a hung UI primitive and a
/// suite that says nothing whatever. Twenty-six copies of a load-bearing thing is twenty-five
/// chances for the one that matters to be subtly different, and nothing here would have noticed.
///
/// This is what fails if they come back. The deletion is the whole of PP618, and a deletion is the
/// one kind of change that leaves nothing behind to assert on unless somebody writes this down.
/// </summary>
public class ApartmentRunnerTests
{
    /// <summary>What a copy of the runner looks like, whatever it is called.</summary>
    private const string Starting = "SetApartmentState";

    /// <summary>
    /// The files that still start one inline, and nothing else may join them.
    ///
    /// PP618 was about the twenty-six view test files that each declared a REUSABLE runner — the
    /// eight lines pasted into each of them. These two write the scaffolding out inside the probe
    /// methods themselves, around bodies that build a DirectComposition device and hand a value
    /// back, so they were never in that task's count and are not in its deletion. PP620 empties
    /// this list; until it does, the list is what keeps a twenty-seventh copy from arriving quietly.
    /// </summary>
    private static readonly string[] StillInline = ["RenderProbeTests.cs", "SteamShortcutTests.cs"];

    [Fact]
    public void NoTestFileStartsAnApartmentOfItsOwn()
    {
        string? root = SanitizerSource.RepositoryRoot();
        Assert.True(root is not null, "not running out of a checkout");

        string here = Path.Combine(root!, "tests", "ChiakiNg.Tests");
        string[] carrying =
        [
            .. Directory.EnumerateFiles(here, "*.cs", SearchOption.AllDirectories)
                .Where(one => !one.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .Where(one => !one.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .Where(one => !one.EndsWith(nameof(ApartmentRunnerTests) + ".cs", StringComparison.Ordinal))
                .Where(one => File.ReadAllText(one).Contains(Starting, StringComparison.Ordinal))
                .Select(one => Path.GetFileName(one)),
        ];

        string[] arrived = [.. carrying.Except(StillInline, StringComparer.Ordinal)];

        Assert.True(
            arrived.Length == 0,
            $"{arrived.Length} file(s) start an apartment of their own again: {string.Join(", ", arrived)}. "
                + "Winwright.InApp carries Apartment.Run, bounded the same way and rethrowing what the "
                + "work threw with its stack intact");

        // Both directions, so the list cannot outlive what it excuses: a file that stopped starting
        // one and stayed on this list would keep the door open for a new copy under its name.
        string[] gone = [.. StillInline.Except(carrying, StringComparer.Ordinal)];

        Assert.True(
            gone.Length == 0,
            $"{string.Join(", ", gone)} no longer start an apartment inline, so take them off "
                + $"{nameof(StillInline)} — PP620 is what empties it");
    }

    [Fact]
    public void TheRunnerTheSuiteUsesIsBoundedAndSurfacesWhatItCaught()
    {
        // The other half, so the deletion is not measured by absence alone: what replaced the copies
        // has to do what they did. A thread that never finishes becomes a named timeout rather than
        // a suite that stops, and what the work threw comes back as itself.
        var slow = Assert.Throws<Winwright.InApp.ApartmentTimeoutException>(
            () => Winwright.InApp.Apartment.Run(
                () => Thread.Sleep(TimeSpan.FromSeconds(5)),
                within: TimeSpan.FromMilliseconds(50),
                named: "work that does not finish"));

        Assert.Contains("work that does not finish", slow.Message, StringComparison.Ordinal);

        // Its own type and its own message, not a string inside a wrapper: the copies rethrew as
        // XunitException(failure.ToString()), which flattened the exception into text — so a refusal
        // a test wanted to assert on by type arrived as a paragraph.
        var threw = Assert.Throws<InvalidOperationException>(
            () => Winwright.InApp.Apartment.Run(() => throw new InvalidOperationException("the work's own")));

        Assert.Equal("the work's own", threw.Message);
    }
}
