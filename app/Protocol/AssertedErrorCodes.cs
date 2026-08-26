using System.Text.RegularExpressions;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP404: the census PP357 did not take.
///
/// PP357 settled the argument - this project configures Release with -DNDEBUG, read out of the build
/// cache rather than assumed, so no <c>assert</c> in lib/src reaches the binary that ships. What it
/// did not settle is how many invariants stand on one. Its check reads ctrl.c and looks for a single
/// shape: an assert between a size and a memcpy, which is what the two keyboard handlers had.
///
/// THIS COUNTS A DIFFERENT SUBJECT - the error code. Fifty-four calls in lib/src return a
/// <c>ChiakiErrorCode</c> that nothing but an assert inspects, across eleven files. Two had been
/// looked at: <see cref="SessionCreate"/> names the one beside the notification wait, where a wait
/// that failed re-tests its condition and waits again. The rest were neither counted nor read.
///
/// THE COMPILER WAS ALREADY POINTING AT TWO OF THEM, as -Wunused-but-set-variable on an error code
/// whose only reader is compiled out. One of those was load-bearing rather than a candidate: a
/// failed <c>chiaki_mutex_lock</c> in the websocket thread went on to enqueue a notification under a
/// lock it did not hold, signal a condition variable, and unlock a mutex it never took. That one is
/// checked now, which is why the ceiling below is 53 and not 54.
///
/// A CEILING AND NOT A LIST. The remaining sites are not each wrong - most assert an initialisation
/// that does not fail on this platform. What the number is for is direction: it may fall, and a
/// commit that lowers it lowers this constant with it, but a fifty-fourth invariant written to stand
/// on something the shipped build deletes turns the suite red where it is written.
/// </summary>
public static partial class AssertedErrorCodes
{
    /// <summary>The tree this counts over.</summary>
    public const string RelativePath = @"lib\src";

    /// <summary>That directory, or null outside a checkout.</summary>
    /// <remarks>
    /// PP382: through <see cref="SanitizerSource.LocateDirectory"/>, because the file-shaped
    /// locator answers null for a directory and a rule over a tree then never runs anywhere.
    /// </remarks>
    public static string? Locate() => SanitizerSource.LocateDirectory(RelativePath);

    /// <summary>
    /// The most error codes that may be inspected by nothing but an assert. It may fall.
    /// </summary>
    public const int Ceiling = 53;

    /// <summary>
    /// And the fewest, so a regex that quietly stops matching is not read as an improvement.
    ///
    /// PP271: a sweep that finds nothing has not passed. Well below the ceiling, because the gap
    /// between them is the room the port has to keep correcting these without editing two numbers.
    /// </summary>
    public const int Floor = 30;

    /// <summary>Every assert whose subject is an error code, by file.</summary>
    /// <returns>File name to the assert text of each, so a failure names what it found.</returns>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Census(string directory)
    {
        ArgumentNullException.ThrowIfNull(directory);

        var found = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        foreach (string path in Directory.EnumerateFiles(directory, "*.c", SearchOption.AllDirectories))
        {
            IReadOnlyList<string> asserts = InFile(File.ReadAllText(path));
            if (asserts.Count > 0)
                found[Path.GetFileName(path)] = asserts;
        }

        return found;
    }

    /// <summary>The same, for one file's text.</summary>
    public static IReadOnlyList<string> InFile(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        // Through Code, because the note left above a corrected site quotes the assert it replaced -
        // the mistake PP399, PP400 and PP401 each made once, and PP403 was written to avoid.
        return [.. ErrorCodeAssert().Matches(CCall.Code(source)).Select(m => m.Value.Trim())];
    }

    /// <summary>How many there are altogether.</summary>
    public static int Total(IReadOnlyDictionary<string, IReadOnlyList<string>> census)
    {
        ArgumentNullException.ThrowIfNull(census);
        return census.Values.Sum(a => a.Count);
    }

    /// <summary>
    /// The site the websocket thread had, which is checked rather than asserted now.
    ///
    /// Named rather than counted: the count says how many are left, and this says the one that was
    /// a defect is not among them.
    /// </summary>
    public static bool TheNotificationLockIsChecked(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        string code = CCall.Code(core);

        return CCall.Happens(code, "chiaki_mutex_lock(&session->notif_mutex);")
            && CCall.Mark(code, "if (mutex_err != CHIAKI_ERR_SUCCESS)") >= 0
            && CCall.Mark(code, "assert(mutex_err == CHIAKI_ERR_SUCCESS)") < 0;
    }

    // An assert whose subject is an error code. Newlines are crossed and semicolons are not, so a
    // multi-line assert is one match and the statement after it is never part of one.
    [GeneratedRegex(@"assert\s*\([^;]*CHIAKI_ERR[^;]*\);")]
    private static partial Regex ErrorCodeAssert();
}
