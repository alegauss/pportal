using System.Globalization;
using System.Text.RegularExpressions;

namespace ChiakiNg.Session;

/// <summary>
/// PP439: the size of the C oracle, and the gate that reads it.
///
/// ctest reports the whole munit suite as ONE test, so "100% tests passed out of 1" is the same
/// sentence whether 145 cases ran or seven of them stopped being compiled in. munit prints its own
/// "N of N tests successful" and ctest ran with --output-on-failure, which throws that away on a
/// green run - so nothing in this tree recorded how big the oracle is.
///
/// THE MECHANISM THAT SHRINKS IT IS REAL AND NAMED. CHIAKI_ENABLE_FFMPEG_DECODER is a tri_option
/// defaulting to AUTO, and a configure that cannot find ffmpeg sets it OFF rather than failing.
/// ffmpegdecoder.c is then not compiled, its seven cases do not exist, main.c's suites[] entry is
/// behind the same macro, and the gate still prints a pass.
///
/// WHAT THIS CLASS HOLDS is the other half: that the gate still asks for the number. The enforcement
/// is in scripts/test-windows.sh, because that is where ctest is run, and a check in a managed suite
/// cannot make a shell script read a file. So this asserts the script's shape - it asks ctest for
/// -V, it names the floor file, and its failure path prints everything it captured.
///
/// THAT LAST ONE IS NOT DECORATION. -V has to be captured to a file for the count to be readable,
/// and a captured run prints nothing while it hangs. PP68 and PP70 each cost a session to diagnose
/// for exactly that reason, so the failure and timeout paths emptying the file to the screen is a
/// property worth asserting rather than remembering.
/// </summary>
public static partial class CSuiteFloor
{
    /// <summary>The recorded size of the oracle.</summary>
    public const string FloorRelativePath = @"tests\c-suite-floor.txt";

    /// <summary>And the gate that runs ctest and compares against it.</summary>
    public const string GateRelativePath = @"scripts\test-windows.sh";

    /// <summary>
    /// The smallest count that could plausibly be the whole suite.
    ///
    /// PP271's rule as a constant: a floor file that had been emptied to "1" would let the suite
    /// shrink to nothing and still pass its own check, so the number has to be big enough to be
    /// the thing it claims to be.
    /// </summary>
    public const int PlausibleMinimum = 100;

    /// <summary>The floor file, or null outside a checkout.</summary>
    public static string? LocateFloor() => SanitizerSource.LocateRelative(FloorRelativePath);

    /// <summary>The gate, or null outside a checkout.</summary>
    public static string? LocateGate() => SanitizerSource.LocateRelative(GateRelativePath);

    /// <summary>
    /// The number the floor file records, or null where it holds none.
    ///
    /// The LAST number on the last non-comment line, which is the shape tests/assertion-ratchet.txt
    /// already uses: a long prose header explaining the ratchet, and the value on its own at the
    /// end. Comments are dropped first, so a "145" inside the explanation is not the value.
    /// </summary>
    public static int? Read(string floorText)
    {
        ArgumentNullException.ThrowIfNull(floorText);

        int? last = null;

        foreach (string line in floorText.Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                continue;

            Match number = NumberRegex().Match(trimmed);
            if (number.Success)
                last = int.Parse(number.Value, CultureInfo.InvariantCulture);
        }

        return last;
    }

    /// <summary>
    /// Whether the gate asks ctest for the verbose output the count lives in.
    ///
    /// --output-on-failure was the previous flag and it is the one this replaces: it prints munit's
    /// summary only on a red, which is the run where the number is not the question.
    /// </summary>
    public static bool AsksCtestForTheCount(string gateScript)
    {
        ArgumentNullException.ThrowIfNull(gateScript);

        string code = WithoutComments(gateScript);

        return code.Contains("-V", StringComparison.Ordinal)
            && !code.Contains("--output-on-failure", StringComparison.Ordinal);
    }

    /// <summary>Whether the gate reads the floor file at all.</summary>
    public static bool ReadsTheFloor(string gateScript)
    {
        ArgumentNullException.ThrowIfNull(gateScript);

        return WithoutComments(gateScript)
            .Contains("c-suite-floor.txt", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether a captured run is emptied to the screen when it fails or hangs.
    ///
    /// Counted rather than tested for presence: one `cat` would satisfy a check for the word and
    /// leave the timeout path silent, and the timeout path is the one PP68 and PP70 were about.
    /// </summary>
    public static int PrintsTheCaptureCount(string gateScript)
    {
        ArgumentNullException.ThrowIfNull(gateScript);

        return CatCaptureRegex().Matches(WithoutComments(gateScript)).Count;
    }

    // Shell comments, so a comment naming a flag does not count as passing it. PP400's rule, and
    // this file's own prose is why it matters - the block above the change discusses
    // --output-on-failure at length.
    private static string WithoutComments(string script)
        => ShellCommentRegex().Replace(script, "");

    [GeneratedRegex(@"[0-9]+")]
    private static partial Regex NumberRegex();

    [GeneratedRegex(@"(?m)^[ \t]*#.*$")]
    private static partial Regex ShellCommentRegex();

    // cat "$ctest_out" - the capture going to the screen.
    [GeneratedRegex(@"cat\s+""\$ctest_out""")]
    private static partial Regex CatCaptureRegex();
}
