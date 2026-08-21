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
    /// Every path in the app assembly that names a file under gui\, found by reflection.
    ///
    /// Reflected rather than listed, so the guard grows with the checks. A list written here would
    /// cover the thirty-odd that exist today and would silently stop covering the next one, which
    /// is the same shape of rot this whole file exists to prevent.
    /// </summary>
    public static IReadOnlyList<string> Declared()
    {
        var paths = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Type type in typeof(SanitizerSource).Assembly.GetTypes())
        {
            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (!field.IsLiteral || field.FieldType != typeof(string))
                    continue;

                if (field.GetRawConstantValue() is not string value)
                    continue;

                if (value.StartsWith(@"gui\", StringComparison.OrdinalIgnoreCase))
                    paths.Add(value);
            }
        }

        return [.. paths];
    }

    /// <summary>
    /// There is a corpus at all. A reflection sweep that found nothing would pass the test below
    /// vacuously, which is the failure mode this file is about wearing a different hat.
    /// </summary>
    [Fact]
    public void TheCorpusIsNotEmpty()
    {
        IReadOnlyList<string> declared = Declared();

        output.WriteLine($"{declared.Count} file(s) under gui\\ are read by drift checks");
        Assert.True(declared.Count >= 10, $"only {declared.Count} paths found - the sweep is not working");
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
        List<string> missing = [];

        foreach (string relative in Declared())
        {
            if (SanitizerSource.LocateRelative(relative) is null)
                missing.Add(relative);
        }

        Assert.True(
            missing.Count == 0,
            "the Qt source these checks read is gone, so they are passing without reading anything. "
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
}
