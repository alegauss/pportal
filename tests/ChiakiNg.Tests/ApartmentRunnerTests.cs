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
///
/// PP620 finished it. Two files wrote the scaffolding out INSIDE their probe methods rather than
/// declaring a reusable helper, so PP618's count never reached them and this test carried them as a
/// named exception. It carries none now.
/// </summary>
public class ApartmentRunnerTests
{
    /// <summary>What a copy of the runner looks like, whatever it is called.</summary>
    private const string Starting = "SetApartmentState";

    /// <summary>
    /// PP620: nothing is excused any more, and the exception list is gone with the five sites.
    ///
    /// PP618 was about the twenty-six view test files that each declared a REUSABLE runner — the
    /// eight lines pasted into each of them. RenderProbeTests and SteamShortcutTests wrote the
    /// scaffolding out inside the probe methods themselves, four times in one file and once in the
    /// other, around bodies that build a DirectComposition device and hand a value back. They were
    /// never in that task's count, so they were not in its deletion, and this test named them as an
    /// exception rather than letting them read as a twenty-seventh copy arriving quietly.
    ///
    /// The four that produce values are <c>Apartment.Run(Func)</c> now, which is what the overload
    /// is for: the answer comes back rather than being assigned into a captured local from another
    /// thread. The list is DELETED rather than emptied — an empty array left in place is the shape
    /// of an excuse, and the next file that wants one would find it already written.
    /// </summary>
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

        Assert.True(
            carrying.Length == 0,
            $"{carrying.Length} file(s) start an apartment of their own again: {string.Join(", ", carrying)}. "
                + "Winwright.InApp carries Apartment.Run, bounded the same way and rethrowing what the "
                + "work threw with its stack intact; the Func overload is for a body that hands a "
                + "value back, which is what PP620's five sites did");
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
