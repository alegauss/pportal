using System.Reflection;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP271: every drift locator in the assembly, asked whether it can find its file.
///
/// Each drift check opens the same way - find the file, and RETURN if it is not there. That early
/// return is right for a published host, which has no checkout beside it, and it is also why a
/// locator answering null turns its whole test green without reading a line. PP270 was one of those,
/// found by backdating a file and watching for a red that did not come.
///
/// <see cref="DriftCorpusTests"/> guards the same shape for the paths under the Qt tree. It does not
/// reach the locators pointing at the hole-punching source, which is most of them and which share
/// two resolvers between them. This asks all of them, by reflection rather than by a list, for the
/// reason the corpus gives: a list covers what exists the day it is written.
/// </summary>
public class LocatorCorpusTests(ITestOutputHelper output)
{
    /// <summary>
    /// Every public static parameterless method named Locate that answers a path.
    /// </summary>
    public static IReadOnlyList<MethodInfo> Locators()
    {
        var found = new List<MethodInfo>();

        foreach (Type type in typeof(SanitizerSource).Assembly.GetTypes())
        {
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (!string.Equals(method.Name, "Locate", StringComparison.Ordinal))
                    continue;

                // The ones taking an argument are asked by their own callers, which know what to
                // pass; there is nothing this can hand them that would mean anything.
                if (method.GetParameters().Length != 0 || method.ReturnType != typeof(string))
                    continue;

                found.Add(method);
            }
        }

        return found;
    }

    /// <summary>
    /// THE GUARD. In a checkout every locator must find its file, and any that does not is named.
    /// </summary>
    [Fact]
    public void EveryLocatorFindsItsFile()
    {
        // The one the whole corpus rests on. Without a checkout there is nothing to ask.
        if (SanitizerSource.Locate() is null)
            return;

        IReadOnlyList<MethodInfo> locators = Locators();

        // A reflection sweep that finds nothing passes for the wrong reason, which is the shape
        // this file exists to catch.
        Assert.True(locators.Count > 50, $"only {locators.Count} locators were found by reflection");

        var missing = new List<string>();

        foreach (MethodInfo locator in locators)
        {
            string? path;
            try
            {
                path = (string?)locator.Invoke(null, null);
            }
            catch (TargetInvocationException ex)
            {
                missing.Add($"{locator.DeclaringType!.Name}.Locate threw {ex.InnerException?.GetType().Name}");
                continue;
            }

            if (path is null)
                missing.Add($"{locator.DeclaringType!.Name}.Locate answered null");
            else if (!File.Exists(path) && !Directory.Exists(path))
                missing.Add($"{locator.DeclaringType!.Name}.Locate answered {path}, which is not there");
        }

        output.WriteLine($"{locators.Count} locators, {missing.Count} unable to find their file");

        Assert.True(
            missing.Count == 0,
            "these drift checks would return early and pass without reading anything:\n  "
            + string.Join("\n  ", missing));
    }

    /// <summary>
    /// And the two most of them delegate to, named so a rename shows up here rather than as a
    /// silence spread across thirty-seven classes.
    /// </summary>
    [Fact]
    public void TheTwoSharedResolversAnswer()
    {
        if (SanitizerSource.Locate() is null)
            return;

        foreach (string? path in new[]
        {
            ChiakiNg.Protocol.PushNotificationSource.Locate(),
            ChiakiNg.Protocol.PortGuessingSource.Locate(),
        })
        {
            Assert.NotNull(path);
            Assert.True(File.Exists(path), $"{path} is not there");
        }
    }

    /// <summary>
    /// A locator that cannot find its file makes its test pass, which is what this measures rather
    /// than describes: the early return is in every one of them.
    /// </summary>
    [Fact]
    public void TheEarlyReturnIsWhatMakesThisNecessary()
    {
        // Asked with nothing to find, the shared resolver answers null rather than throwing - so a
        // test built on it runs no assertion at all and reports success.
        Assert.Null(SanitizerSource.LocateRelative(
            Path.Combine("no-such-directory", "no-such-file.c")));
    }

    /// <summary>
    /// PP276: the other half of PP271. That one made a locator finding NOTHING a failure; this one
    /// is about a locator finding the WRONG thing, which is worse because it is indistinguishable
    /// from success unless you already know which file was meant.
    ///
    /// Built rather than found. The ambiguity that exposed this is real - .gitignore exists at the
    /// root and again in app\, and app\ sits between the host's output directory and the root - but
    /// it cannot be reproduced from HERE, because this assembly runs out of tests\ and the walk
    /// from there never passes through app\. A test that only asserts from wherever the runner sits
    /// is a test that would have been green on the day the bug was written.
    /// </summary>
    [Fact]
    public void TheRootCopyWinsOverTheNearerOne()
    {
        string tree = Path.Combine(Path.GetTempPath(), "pp276-" + Guid.NewGuid().ToString("N"));
        string nested = Path.Combine(tree, "app", "bin", "Debug");
        try
        {
            Directory.CreateDirectory(nested);
            File.WriteAllText(Path.Combine(tree, SanitizerSource.RootAnchor), "");
            File.WriteAllText(Path.Combine(tree, ".gitignore"), "the root's");
            File.WriteAllText(Path.Combine(tree, "app", ".gitignore"), "the decoy");

            Assert.Equal(tree, SanitizerSource.RepositoryRootFrom(nested));

            // The whole assertion. Walking up from nested, app\.gitignore is the first match and
            // the root's is the right one.
            Assert.Equal(
                Path.Combine(tree, ".gitignore"),
                SanitizerSource.LocateRelative(".gitignore", nested));

            // ...and a name the root does not carry is still absent, rather than being satisfied by
            // a copy lying nearer. Anchoring must not turn into "find it anywhere".
            File.WriteAllText(Path.Combine(tree, "app", "decoy-only.txt"), "");
            Assert.Null(SanitizerSource.LocateRelative("decoy-only.txt", nested));
        }
        finally
        {
            if (Directory.Exists(tree))
                Directory.Delete(tree, recursive: true);
        }
    }

    /// <summary>
    /// And outside a checkout there is no root, so every locator answers null and every drift check
    /// says it could not run - which is what a published host depends on.
    /// </summary>
    [Fact]
    public void NoAnchorMeansNoRootAndNoAnswers()
    {
        string tree = Path.Combine(Path.GetTempPath(), "pp276-bare-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tree);
            File.WriteAllText(Path.Combine(tree, ".gitignore"), "beside it, and still not found");

            Assert.Null(SanitizerSource.RepositoryRootFrom(tree));
            Assert.Null(SanitizerSource.LocateRelative(".gitignore", tree));
        }
        finally
        {
            if (Directory.Exists(tree))
                Directory.Delete(tree, recursive: true);
        }
    }
}
