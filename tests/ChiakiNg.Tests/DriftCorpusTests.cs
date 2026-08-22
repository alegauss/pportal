using System.Reflection;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP21: the source the drift checks read, and the guard that makes its absence LOUD.
///
/// This port asserts that it still matches what it was ported from by reading the Qt client:
/// the QML a screen came from, the switch a capture reads, the format strings a mapping is written
/// with. Every one of those checks is written the same way - locate the file, and RETURN EARLY when
/// it is not there, because a published binary has no gui\ beside it and a check that cannot run
/// should say so rather than fail.
///
/// That is correct and it has a cost, and PP21 is the moment the cost arrives. Qt is no longer a
/// build dependency, so nothing in the toolchain would notice gui\ being deleted - and the day it
/// went, every one of those checks would start passing while reading nothing at all. The suite
/// would be greener than ever and would be measuring the empty set.
///
/// So the corpus itself is asserted, once, here. Not the contents - the other tests do that - just
/// that the files they open are on disk. If gui\ is deleted this goes red and names what stopped
/// being checked, which is the difference between a decision and an accident.
/// </summary>
public class DriftCorpusTests(ITestOutputHelper output)
{
    /// <summary>
    /// Every repository path the app assembly declares, from the corpus itself.
    ///
    /// PP278: this used to reflect here and keep only values starting <c>gui\</c>. Both halves have
    /// moved into <see cref="DriftCorpus"/> - the sweep because SelfTest needs the same answer and
    /// was carrying a hand-written copy of it, and the prefix filter because it was never the rule,
    /// only the tree PP21 was worried about.
    /// </summary>
    public static IReadOnlyList<string> Declared() => DriftCorpus.Declared();

    /// <summary>
    /// There is a corpus at all. A reflection sweep that found nothing would pass the test below
    /// vacuously, which is the failure mode this file is about wearing a different hat.
    /// </summary>
    [Fact]
    public void TheCorpusIsNotEmpty()
    {
        IReadOnlyList<string> declared = Declared();

        output.WriteLine($"{declared.Count} repository path(s) are read by drift checks");

        // A ratchet, and it catches one thing: a predicate that stopped matching. The corpus is 58
        // today and the floor is 50, which is deliberately loose - it does NOT catch losing a single
        // -segment path like roadkeep.toml or package.cmd, which DriftCorpus recognises by existing.
        // Raise it when the corpus grows; never lower it.
        Assert.True(declared.Count >= 50, $"only {declared.Count} paths found - the sweep is not working");
    }

    /// <summary>
    /// And every one of them is on disk.
    ///
    /// This is the assertion PP21 added. Qt is no longer built, so nothing else would notice gui\
    /// going away - and the checks that read it are written to skip quietly when it has.
    /// </summary>
    [Fact]
    public void EveryFileTheDriftChecksReadIsStillThere()
    {
        // DriftCorpus.Missing() and not LocateRelative here: a declared path may be a directory,
        // and the resolver tests File.Exists. Written the other way this reported lib\src gone on
        // every run - PP271's finding about that same constant, arriving from the other side.
        IReadOnlyList<string> missing = DriftCorpus.Missing();

        Assert.True(
            missing.Count == 0,
            "the source these checks read is gone, so they are passing without reading anything. "
                + "Qt is no longer a build dependency (PP21), so nothing else will notice: "
                + string.Join(", ", missing));
    }

    /// <summary>
    /// The one path everything else starts from. Named separately because a failure here means the
    /// sweep above is reporting on a tree it is not running in, rather than on a deletion.
    /// </summary>
    [Fact]
    public void TheCheckoutIsWhereThisIsRunning()
        => Assert.NotNull(SanitizerSource.Locate());

    /// <summary>
    /// PP278: and no drift check may hand the resolver a path the sweep cannot see.
    ///
    /// This is the half that makes the rest hold. A reflection sweep finds CONSTANTS, so a path
    /// written as a literal at the call site is invisible to it however good the predicate is -
    /// and eight of them were, four under lib\ and test\ that no version of the old gui\ filter
    /// would ever have reached.
    ///
    /// Read off the source rather than the assembly, because that is where the distinction lives:
    /// by the time it is IL, a literal and a constant reference are the same string. The cost is
    /// that this test knows what the call looks like, which is why it asserts it found the calls at
    /// all before asserting anything about them.
    /// </summary>
    [Fact]
    public void NoDriftCheckPassesTheResolverALiteral()
    {
        string? root = SanitizerSource.RepositoryRoot();
        Assert.NotNull(root);

        var literals = new List<string>();
        int calls = 0;

        foreach (string file in Directory.EnumerateFiles(
            Path.Combine(root, "app"), "*.cs", SearchOption.AllDirectories))
        {
            // The SDK's build output sits under app\, and it carries generated sources.
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                if (!lines[i].Contains("LocateRelative(", StringComparison.Ordinal))
                    continue;

                calls++;
                if (lines[i].Contains("LocateRelative(@\"", StringComparison.Ordinal)
                    || lines[i].Contains("LocateRelative(\"", StringComparison.Ordinal))
                {
                    literals.Add($"{Path.GetFileName(file)}:{i + 1}  {lines[i].Trim()}");
                }
            }
        }

        output.WriteLine($"{calls} call(s) to the resolver, {literals.Count} of them with a literal");

        // The sweep finding no calls would pass the assertion below over nothing, which is this
        // file's own subject matter wearing a third hat.
        Assert.True(calls >= 10, $"only {calls} resolver calls found - this scan is not working");

        Assert.True(
            literals.Count == 0,
            "these paths are literals at the call site, so no reflection sweep can see them and "
                + "nothing guards them. Declare each as a public const on the type that reads it:\n  "
                + string.Join("\n  ", literals));
    }
}
