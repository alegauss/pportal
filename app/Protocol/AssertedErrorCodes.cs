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
/// whose only reader is compiled out. One was a failed <c>chiaki_mutex_lock</c> in the websocket
/// thread, which went on to enqueue a notification under a lock it did not hold, signal a condition
/// variable, and unlock a mutex it never took. It is checked now, which is why the ceiling below is
/// 53 and not 54.
///
/// PP406 CORRECTED WHAT THAT WAS WORTH. This class called that site load-bearing, and on Windows it
/// is not: <c>chiaki_mutex_lock</c> is EnterCriticalSection and a single success return, so the
/// branch cannot be taken. The check is still right and costs nothing, and it silenced a real
/// warning - but the claim was about the C in general and this port builds one platform. See
/// <see cref="CanFailCeiling"/>, which is the number that survives that question.
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
    /// PP406: the ones whose callee can fail at all - the number the ceiling above hides.
    ///
    /// Thirty-three of the fifty-three assert a primitive with a single success return, and a
    /// correction to one of those adds a branch no execution reaches. What is left is the waits,
    /// the joins and the stop pipe - calls with a timeout, a WSA_INVALID_EVENT or a join that did
    /// not - and five that assert a function the file defines itself. See
    /// <see cref="ThreadPrimitives"/>, which reads the primitives out of the C.
    ///
    /// Several of the twenty are <c>assert(err == SUCCESS || err == TIMEOUT)</c>, which is an assert
    /// that a wait returned one of two expected answers rather than that it could not fail. They are
    /// counted because the third answer is still unhandled in the shipped build.
    /// </summary>
    public const int CanFailCeiling = 20;

    /// <summary>Each assert in a file, paired with the call it inspects.</summary>
    /// <returns>The callee name and the assert text, in the order they appear.</returns>
    public static IReadOnlyList<(string Callee, string Assert)> WithCallees(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        string code = CCall.Code(source);
        var found = new List<(string, string)>();

        foreach (Match assertion in ErrorCodeAssert().Matches(code))
            found.Add((CalleeBefore(code, assertion.Index), assertion.Value.Trim()));

        return found;
    }

    /// <summary>
    /// The chiaki_ call in the statement before <paramref name="at"/>, or the empty string.
    ///
    /// The assert follows the call it inspects, on the next line, so the span to read is from the
    /// statement boundary before it. Read backwards rather than by line number, because two of
    /// these are written across a line break and a line-indexed reader would miss both.
    /// </summary>
    public static string CalleeBefore(string code, int at)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentOutOfRangeException.ThrowIfNegative(at);

        int start = code.LastIndexOfAny([';', '{', '}'], Math.Max(0, Math.Min(at, code.Length) - 1));
        if (start < 0)
            start = 0;

        int previous = code.LastIndexOfAny([';', '{', '}'], Math.Max(0, start - 1));
        if (previous < 0)
            previous = 0;

        Match call = ChiakiCall().Match(code[previous..start]);
        return call.Success ? call.Groups[1].Value : string.Empty;
    }

    /// <summary>
    /// Every assert whose callee has a failure path, by file.
    ///
    /// An assert whose callee cannot be identified counts here. An unreadable call should widen
    /// what gets looked at rather than narrow it.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> CanFail(
        string directory, IReadOnlyDictionary<string, string> primitives)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(primitives);

        var found = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        foreach (string path in Directory.EnumerateFiles(directory, "*.c", SearchOption.AllDirectories))
        {
            List<string> risky =
            [
                .. WithCallees(File.ReadAllText(path))
                    .Where(pair => ThreadPrimitives.CanFail(
                        pair.Callee.Length == 0 ? "chiaki_unreadable_callee" : pair.Callee, primitives))
                    .Select(pair => $"{pair.Callee}: {pair.Assert}"),
            ];

            if (risky.Count > 0)
                found[Path.GetFileName(path)] = risky;
        }

        return found;
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

    // The last call in a span, which is the one whose result the assert holds. Not just chiaki_:
    // five of these assert a function the file defines itself, and a reader that only knew the
    // library's prefix reported those as unattributed and hid what they were.
    [GeneratedRegex(@"\b(?!if|while|for|switch|return|sizeof|assert)([A-Za-z_][A-Za-z0-9_]*)\s*\(", RegexOptions.RightToLeft)]
    private static partial Regex ChiakiCall();
}
