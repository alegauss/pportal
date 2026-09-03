namespace ChiakiNg.Session;

/// <summary>One test file that declines when an oracle is absent, and how many guards it carries.</summary>
/// <param name="Where">The file, relative to the repository root.</param>
/// <param name="Guard">The call whose false answer makes an assertion decline.</param>
public readonly record struct GuardedFile(string Where, string Guard);

/// <summary>
/// PP663 made twenty-one assertions opt-in, and the gate reports the same number either way.
///
/// The flip was right and this is its cost, measured rather than assumed. Every assertion that
/// compares a managed implementation against the C it replaces needs the C present, so each one now
/// asks whether the shim carries the oracle and returns early when it does not. An early return in
/// xUnit is a PASS. So the suite prints 5272 passed on a build with both oracles and 5272 passed on
/// a build with neither, and nothing in its output distinguishes them.
///
/// THAT IS PP56's STALE GREEN, arriving through a door PP56 did not have. There the binary was old
/// and the suite reported on code that had changed; here the binary is right and the suite reports
/// on assertions that did not run. Both are a green that means less than a reader takes it to mean.
///
/// So the count is made visible. This is not a check that the guards are correct - they are, and
/// their counterparts assert the other side - it is a check that the gate SAYS which configuration
/// it ran under, in a number a person reads rather than in twenty-one silent returns.
///
/// Counted from the test files themselves rather than declared, for the reason every count in this
/// port is: the nine wrappers were nine in the prose for two commits after they were ten.
/// </summary>
public static class OracleGuardCensus
{
    /// <summary>
    /// The files whose assertions decline without an oracle, and the guard each one asks.
    ///
    /// Two guards because there are two oracles: the seam's shape for holepunch, and the json one.
    /// Named per file rather than derived, because being guarded is a decision somebody took and a
    /// file that stopped guarding is a finding rather than a smaller number.
    /// </summary>
    public static IReadOnlyList<GuardedFile> Files { get; } =
    [
        new(@"tests\ChiakiNg.Tests\JsonCTests.cs", "JsonOracleIsAvailable"),
        new(@"tests\ChiakiNg.Tests\JsonDifferentialTests.cs", "JsonOracleIsAvailable"),
        new(@"tests\ChiakiNg.Tests\FrameParsingTests.cs", "JsonOracleIsAvailable"),
        new(@"tests\ChiakiNg.Tests\NativeHolepunchSessionTests.cs", "SeamWraps"),
        new(@"tests\ChiakiNg.Tests\HolepunchSessionOwnershipTests.cs", "WrappingHeader"),
    ];

    /// <summary>A file, or null outside a checkout.</summary>
    public static string? Locate(string relative) => SanitizerSource.LocateRelative(relative);

    /// <summary>
    /// How many guards one file carries.
    ///
    /// The call, followed by an opening parenthesis, in the file's own text. A declaration of the
    /// guard is not a use of it - which is why <see cref="Files"/> names test files only, and the
    /// classes that define the guards are not in the list.
    /// </summary>
    public static int GuardsIn(string source, string guard)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(guard);

        int found = 0;
        int at = source.IndexOf(guard + "(", StringComparison.Ordinal);
        while (at >= 0)
        {
            found++;
            at = source.IndexOf(guard + "(", at + 1, StringComparison.Ordinal);
        }

        return found;
    }

    /// <summary>Every file's guard count, in the order this class declares them.</summary>
    public static IReadOnlyList<(GuardedFile File, int Guards)> Counted()
    {
        var found = new List<(GuardedFile, int)>();

        foreach (GuardedFile file in Files)
        {
            if (Locate(file.Where) is not { } path)
                continue;

            found.Add((file, GuardsIn(File.ReadAllText(path), file.Guard)));
        }

        return found;
    }

    /// <summary>
    /// How many assertions decline on a build with neither oracle.
    ///
    /// The number this whole class exists to put in front of a reader. It is a floor rather than an
    /// exact count - a guard at the top of a helper protects every test that calls it - and a floor
    /// is what the claim needs: the gate is quieter than it looks by at least this much.
    /// </summary>
    public static int WouldDecline() => Counted().Sum(one => one.Guards);
}
