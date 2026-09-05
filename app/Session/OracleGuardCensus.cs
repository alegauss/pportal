namespace ChiakiNg.Session;

/// <summary>PP704: what a file's guard is protecting, which decides whether it is a cost.</summary>
public enum GuardKind
{
    /// <summary>
    /// A comparison against the C, which does not run when the oracle is absent.
    ///
    /// The cost this census exists to print. An early return in xUnit is a pass, so these are the
    /// assertions a green run did not make.
    /// </summary>
    Comparison,

    /// <summary>
    /// A test OF the guard, which declines for a different reason.
    ///
    /// Named and not counted. What it asserts is that the guard reports the build it got, and a
    /// build without the oracle is one of the two cases it is FOR - so it not running is the check
    /// working rather than an assertion skipped. Counting these would inflate the floor with tests
    /// whose absence costs nothing.
    /// </summary>
    GuardsOwnTest,

    /// <summary>
    /// Code that asks the same question and asserts nothing.
    ///
    /// Two shapes, and neither is a cost. Production code branches on the oracle because it has to -
    /// the recorder will not re-derive a recording from a library that is not there, and the seam's
    /// allowance follows the same answer - and a test may read it to PRINT it. Nothing declines
    /// either way, so these are named to close the sweep and left out of the floor.
    /// </summary>
    Reads,
}

/// <summary>One file that declines when an oracle is absent, and how many guards it carries.</summary>
/// <param name="Where">The file, relative to the repository root.</param>
/// <param name="Guard">
/// The call whose false answer makes an assertion decline, spelled WITH its class where it has one.
///
/// PP704: qualified, and that is what makes the sweep sound. A bare name matches the guard's own
/// definition as well as its callers - GuardsIn says so, which is why the total was always a floor -
/// and a sweep looking for unnamed callers would then report the file that DEFINES a guard as one
/// that forgot to declare itself.
/// </param>
/// <param name="Kind">Whether it is a comparison that costs, or a test of the guard itself.</param>
public readonly record struct GuardedFile(string Where, string Guard, GuardKind Kind = GuardKind.Comparison);

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
/// Counted from the files themselves rather than declared, for the reason every count in this port
/// is: the nine wrappers were nine in the prose for two commits after they were ten.
///
/// PP683: AND NOT ONLY THE TEST PROJECT. The host's own selftest guards too - it asks whether the
/// device id's format oracle is there before comparing the managed id against the C's - and for two
/// blocks that comparison was counted by nothing, so the number the gate printed understated what a
/// bare build had skipped. That is not a tidiness point: the one guarded comparison outside the
/// census was exactly the one whose guard was wrong, and PP681 found it by reading rather than from
/// a row that looked surprising.
///
/// So the list is files that GUARD, wherever they live. What still keeps a file out is defining a
/// guard rather than asking one, which is why the classes the guards belong to are not here.
/// </summary>
public static class OracleGuardCensus
{
    /// <summary>
    /// The files whose assertions decline without an oracle, and the guard each one asks.
    ///
    /// Three oracles: the seam's shape for holepunch, the json one, and - PP670 - the frame path's,
    /// which six differentials ask before they call the fourteen. More guard CALLS than that,
    /// because one oracle is asked through whichever predicate the asking file already had.
    /// Named per file rather than derived, because being guarded is a decision somebody took and a
    /// file that stopped guarding is a finding rather than a smaller number.
    /// </summary>
    public static IReadOnlyList<GuardedFile> Files { get; } =
    [
        // PP33: JsonCTests and FrameParsingTests used to be here and are not, which is a smaller
        // number for the right reason. Their comparisons did not go: they read json-c's answers out
        // of JsonOracleRecording, taken from the library once, so they run on every build. What is
        // left of the json oracle is the ONE guard that re-derives the recording from the library -
        // and that one declining costs nothing, because what it protects is asserted either way.
        new(@"tests\ChiakiNg.Tests\JsonDifferentialTests.cs", JsonGuard),
        new(@"tests\ChiakiNg.Tests\NativeHolepunchSessionTests.cs", "SeamWraps"),
        new(@"tests\ChiakiNg.Tests\HolepunchSessionOwnershipTests.cs", HolepunchGuard),
        new(@"tests\ChiakiNg.Tests\FecCodecTests.cs", FramePathGuard),
        new(@"tests\ChiakiNg.Tests\FecMatrixTests.cs", FramePathGuard),
        new(@"tests\ChiakiNg.Tests\FecVectorTests.cs", FramePathGuard),
        new(@"tests\ChiakiNg.Tests\FrameAssemblerTests.cs", FramePathGuard),
        new(@"tests\ChiakiNg.Tests\ManagedVideoReceiverTests.cs", FramePathGuard),
        new(@"tests\ChiakiNg.Tests\AllocBudgetTests.cs", FramePathGuard),

        // PP694: libopus, which is a fourth oracle and arrived after PP665 wrote this list. Its
        // guard asks the build the way PP681's correction requires, and the five comparisons behind
        // it are the encoder differential - so a build with CHIAKI_LIB_ENABLE_OPUS off declines
        // them and this is what says how many.
        new(@"tests\ChiakiNg.Tests\ManagedOpusEncoderTests.cs", OpusGuard),

        // PP683: the host, which is not a test file and guards all the same. Its one comparison is
        // the device id's shape against holepunch.c's, and it asks OfTheBuild through the predicate
        // PP681 corrected rather than through the two names above - so the row names the call as it
        // is written, the way every other row does.
        new(SelfTestPath, SelfTestGuard),

        // PP704: the sweep PP683 left, and it was not empty. PP676's wrappers arrived after PP665
        // wrote this list, so nothing was ever removed - the list simply stopped being complete on
        // the day they landed, and eight comparisons went uncounted.
        new(@"tests\ChiakiNg.Tests\FeedbackPayloadTests.cs", FeedbackGuard),

        // PP52: the cleaning stage's end-to-end runs the encoder, so it declines with libopus. Found
        // by the sweep above rather than remembered, which is what PP704 built it for.
        new(@"tests\ChiakiNg.Tests\CleanedMicrophoneTests.cs", OpusGuard),

        // And the files that test the GUARDS rather than what the guards protect. Named so the
        // sweep is closed, and NOT counted: a guard test declining on a build without the oracle is
        // the check doing its job, not an assertion somebody lost.
        new(@"tests\ChiakiNg.Tests\ShimHolepunchShapeTests.cs", HolepunchGuard, GuardKind.GuardsOwnTest),
        new(@"tests\ChiakiNg.Tests\ShimHolepunchShapeTests.cs", SelfTestGuard, GuardKind.GuardsOwnTest),
        new(@"tests\ChiakiNg.Tests\ShimFramePathShapeTests.cs", FramePathGuard, GuardKind.GuardsOwnTest),
        new(@"tests\ChiakiNg.Tests\DeletedLibraryOraclesTests.cs", JsonGuard, GuardKind.GuardsOwnTest),
        new(@"tests\ChiakiNg.Tests\NativeSeamTests.cs", HolepunchGuard, GuardKind.GuardsOwnTest),
        new(@"tests\ChiakiNg.Tests\NativeSeamTests.cs", JsonGuard, GuardKind.GuardsOwnTest),

        // And what the sweep found that is neither: code asking the same question and asserting
        // nothing. Two of these are the HOST's - the recorder refuses to re-derive a recording from
        // a library that has gone, and the seam's json allowance follows the same answer - which is
        // a use of a guard nobody had thought to look for, because the census had only ever read
        // test files. The other two read it to define a helper and to print it.
        new(@"app\Protocol\JsonOracleRecorder.cs", JsonGuard, GuardKind.Reads),
        new(@"app\Session\NativeSeam.cs", JsonGuard, GuardKind.Reads),
        new(@"tests\ChiakiNg.Tests\NativeHolepunchSessionTests.cs", HolepunchGuard, GuardKind.Reads),
        new(@"tests\ChiakiNg.Tests\OracleGuardCensusTests.cs", JsonGuard, GuardKind.Reads),
    ];

    /// <summary>PP676's wrappers, which are a fourth oracle and one PP665 could not have known.</summary>
    public const string FeedbackGuard = "NativeFeedback.IsAvailable";

    /// <summary>PP694's, which is a fifth.</summary>
    public const string OpusGuard = "NativeOpusEncoder.IsAvailable";

    /// <summary>The json oracle, qualified so its own definition is not read as a caller.</summary>
    public const string JsonGuard = "DeletedLibraryOracles.JsonOracleIsAvailable";

    /// <summary>The holepunch seam's shape, likewise.</summary>
    public const string HolepunchGuard = "ShimHolepunchShape.WrappingHeader";

    /// <summary>The host's selftest, which is the one guarded comparison outside the test project.</summary>
    public const string SelfTestPath = @"app\SelfTest.cs";

    /// <summary>
    /// The guard it asks, spelled with its class as it is written at the call.
    ///
    /// PP74's shape means the selftest has branches that are about a MACHINE rather than about a
    /// build - a missing .NET SDK, an absent console - and those are not oracle guards. Naming the
    /// call is what keeps this row about the one that is.
    /// </summary>
    public const string SelfTestGuard = "ShimHolepunchShape.TheFormatOracleIsAvailable";

    /// <summary>
    /// PP670: the one call every frame-path differential asks, spelled with its class so a file
    /// guarding the holepunch seam through the same method name is not counted twice.
    /// </summary>
    public const string FramePathGuard = "ShimFramePathShape.WrappingHeader";

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
    ///
    /// PP704: COMPARISONS ONLY. A test of the guard itself declines on the same build and costs
    /// nothing, because a build without the oracle is one of the two cases it exists to check.
    /// Counting those would make the floor larger and less true at the same time.
    /// </summary>
    public static int WouldDecline()
        => Counted().Where(one => one.File.Kind == GuardKind.Comparison).Sum(one => one.Guards);

    /// <summary>
    /// The directories a sweep for an unnamed caller reads: the host, and the suite.
    /// </summary>
    public static IReadOnlyList<string> SweptDirectories { get; } = ["app", @"tests\ChiakiNg.Tests"];

    /// <summary>The distinct guards this census knows about, which is what the sweep looks for.</summary>
    public static IReadOnlyList<string> KnownGuards { get; } =
        [.. Files.Select(one => one.Guard).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    /// <summary>
    /// Every file under the swept directories that calls a known guard and has no row.
    ///
    /// PP704: THE LIST WENT STALE BY ADDITION, which is why this exists rather than a rule about a
    /// directory. Nothing was ever removed from <see cref="Files"/>; two oracles arrived after it
    /// was written and one of them brought eight guards with it, so what was needed was not a better
    /// list but a check that the list is closed. A file calling a guard this census knows and not
    /// naming itself here fails, and the answer is a row rather than a smaller number.
    ///
    /// Bounded by the guards it already knows, which is the honest limit: a SIXTH oracle arriving
    /// with a name nothing here has heard of is invisible to this, exactly as the fourth and fifth
    /// were. What it stops is the same oracle spreading to files nobody counted.
    /// </summary>
    /// <returns>Pairs of file and guard, ordered, empty where the list is closed.</returns>
    public static IReadOnlyList<(string Where, string Guard)> Unnamed()
    {
        var named = Files.Select(one => (one.Where, one.Guard)).ToHashSet();
        var found = new List<(string, string)>();

        foreach (string directory in SweptDirectories)
        {
            if (SanitizerSource.LocateDirectory(directory) is not { } root)
                continue;

            string? repository = SanitizerSource.RepositoryRoot();
            if (repository is null)
                continue;

            foreach (string path in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                if (path.Contains(@"\obj\", StringComparison.Ordinal)
                    || path.Contains(@"\bin\", StringComparison.Ordinal))
                {
                    continue;
                }

                string source = File.ReadAllText(path);
                string relative = Path.GetRelativePath(repository, path);

                foreach (string guard in KnownGuards)
                {
                    if (GuardsIn(source, guard) > 0 && !named.Contains((relative, guard)))
                        found.Add((relative, guard));
                }
            }
        }

        return [.. found.Order()];
    }
}
