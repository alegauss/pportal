using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>One file that quotes session.c's holepunch handle, and which half of the port it is.</summary>
/// <param name="RelativePath">Where it is, from the repository root.</param>
/// <param name="IsTest">Whether it asserts against a model, rather than being one.</param>
public readonly record struct OracleReader(string RelativePath, bool IsTest);

/// <summary>
/// PP621: what PP33's deletion costs beyond the C, counted rather than argued.
///
/// PP584 settled the invariant for a deletion line: it must name this port's own shim among what
/// calls the C, because the shim wraps 130 entry points and is therefore a caller of everything any
/// deletion removes. <see cref="HolepunchConsumers.All"/> holds the result - session.c and the shim,
/// two files - and PP573 holds PP33's line to that number.
///
/// BOTH ARE TRUE AND NEITHER IS THE COST. What a caller list counts is translation units the linker
/// would break. What it cannot see is that <c>session-&gt;holepunch_session</c> is also the SUBJECT
/// of this port's oracle: model classes under app/ quote session.c's holepunch text literally - the
/// assignment, the two fini sites, the ctrl and data socket reads, the offer, the punch, the regist
/// info, the selected address and the ctrl port - and files under tests/ assert against them. The
/// text they quote is a specification, which is how the managed flow was held to the C it was ported
/// from.
///
/// SO THE COMMIT PP598 DESCRIBES IS BIGGER THAN THE LINE THAT DESCRIBES IT. "The retirement rides in
/// PP33's own commit" deletes nine calls from one .c file and invalidates every one of these
/// assertions in the same transaction. A session picking PP33 reads two consumers and sizes the work
/// at a file and a seam; what it meets is an oracle rewrite.
///
/// IT IS TWO MEASURES AND NOT ONE. <see cref="Census"/> finds what quotes session.c's text;
/// <see cref="Dependents"/> finds the tests that assert against those models without quoting
/// anything, which is most of them. Counting only the first says the tests are nearly free, and the
/// tests are where the rewrite is.
///
/// FOUND, NOT LISTED. The readers are located by the handle they quote, so a model converted away
/// from the C leaves this census by being converted and one added arrives in it without anybody
/// remembering to say so. That is the difference between this and <see cref="HolepunchConsumers"/>,
/// whose list is named on purpose because a deletion needs which and not how many: here the question
/// is how many, and a hand-typed figure would be true on the day it was typed.
/// </summary>
public static class HolepunchOracleReaders
{
    /// <summary>The half of the port that models the C.</summary>
    public const string ModelsDirectory = "app";

    /// <summary>The half that asserts against those models.</summary>
    public const string TestsDirectory = "tests";

    /// <summary>
    /// The text a reader is found by, taken from <see cref="HolepunchDirection"/> rather than spelled
    /// again.
    ///
    /// Spelling it here would put this file in its own census, which is the one answer that is
    /// certainly wrong: a census is not a reader of session.c.
    /// </summary>
    public static string Handle => HolepunchDirection.Handle;

    /// <summary>The models directory, or null outside a checkout.</summary>
    public static string? LocateModels() => SanitizerSource.LocateDirectory(ModelsDirectory);

    /// <summary>The tests directory, or null outside a checkout.</summary>
    public static string? LocateTests() => SanitizerSource.LocateDirectory(TestsDirectory);

    /// <summary>
    /// Whether a path is somewhere a build wrote.
    ///
    /// app\bin and app\obj carry a compiled copy of the models under every configuration and target
    /// framework the host has ever been built for, and each of those directories answers the search
    /// with sources it was handed. Counting them would make the census a function of how many times
    /// somebody has built, which is the one input it must not have.
    /// </summary>
    public static bool IsGenerated(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);

        return relativePath.Split(['\\', '/'], StringSplitOptions.None)
            .Any(segment =>
                segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
                || segment.Equals("obj", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Whether one file's text quotes the handle at all.</summary>
    public static bool Quotes(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return source.Contains(Handle, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every reader under both directories, ordered by path.
    ///
    /// Reads only C#, which is the whole of both halves. The C itself is deliberately outside: what
    /// session.c and the shim cost is <see cref="HolepunchConsumers.All"/>'s question, and answering
    /// it twice in two shapes is how the two numbers would come to disagree.
    /// </summary>
    public static IReadOnlyList<OracleReader> Census(string repositoryRoot)
    {
        ArgumentNullException.ThrowIfNull(repositoryRoot);

        return
        [
            .. Under(repositoryRoot, ModelsDirectory, isTest: false),
            .. Under(repositoryRoot, TestsDirectory, isTest: true),
        ];
    }

    /// <summary>
    /// The test files that assert against a model in the census, which the census itself misses.
    ///
    /// THE READING THAT MADE THIS NECESSARY. Thirteen files quote the handle and only three of them
    /// are tests, which reads as though the tests are nearly free. They are not: a model states what
    /// session.c does and its tests assert that statement, so they carry the fact WITHOUT carrying
    /// the text - which is exactly the shape a search for the text cannot see.
    ///
    /// Found by the model's type name, because that is how the join is made here: a test lives in
    /// tests/ChiakiNg.Tests, opens `using ChiakiNg.Protocol`, and names the class. It is a heuristic
    /// and the direction of its error is the safe one - a test that names a model it does not really
    /// depend on costs a reading, and one that depends without naming does not exist in C#.
    /// </summary>
    public static IReadOnlyList<string> Dependents(
        string repositoryRoot, IReadOnlyList<OracleReader> census)
    {
        ArgumentNullException.ThrowIfNull(repositoryRoot);
        ArgumentNullException.ThrowIfNull(census);

        string[] models =
        [
            .. census.Where(one => !one.IsTest)
                .Select(one => Path.GetFileNameWithoutExtension(one.RelativePath))
        ];

        string full = Path.Combine(repositoryRoot, TestsDirectory);
        if (models.Length == 0 || !Directory.Exists(full))
            return [];

        HashSet<string> already =
            [.. census.Select(one => one.RelativePath), .. new[] { CensusRelativePath }];

        return
        [
            .. Directory.EnumerateFiles(full, "*.cs", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(repositoryRoot, path))
                .Where(relative => !IsGenerated(relative) && !already.Contains(relative))
                .Where(relative => Array.Exists(
                    models,
                    model => File.ReadAllText(Path.Combine(repositoryRoot, relative))
                        .Contains(model, StringComparison.Ordinal)))
                .Order(StringComparer.OrdinalIgnoreCase)
        ];
    }

    /// <summary>
    /// This census, excluded from its own dependent list.
    ///
    /// Its tests name the class, which is what <see cref="Dependents"/> looks for - so without this
    /// the count would include the file that exists to make the count.
    /// </summary>
    public const string CensusRelativePath = @"tests\ChiakiNg.Tests\HolepunchOracleReadersTests.cs";

    /// <summary>One directory's readers, relative to the root and ordered.</summary>
    private static IEnumerable<OracleReader> Under(string root, string directory, bool isTest)
    {
        string full = Path.Combine(root, directory);
        if (!Directory.Exists(full))
            return [];

        return Directory.EnumerateFiles(full, "*.cs", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path))
            .Where(relative => !IsGenerated(relative))
            .Where(relative => Quotes(File.ReadAllText(Path.Combine(root, relative))))
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(relative => new OracleReader(relative, isTest));
    }
}
