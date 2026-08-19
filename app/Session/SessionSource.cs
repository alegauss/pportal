using System.Text.RegularExpressions;

namespace ChiakiNg.Session;

/// <summary>
/// PP91: the check that keeps the transposed clamp from coming back.
///
/// The defect was <c>std::clamp(0.0, value, bound)</c> - the value first and the bounds after, so
/// what got clamped was the literal 0.0 and the upper bound never applied. It read correctly at a
/// glance, survived review four times in one file, and produced a touch coordinate off the end of
/// the console's own touchpad.
///
/// There is nothing textual to compare against the way <see cref="SanitizerSource"/> compares
/// patterns, because this half is logic rather than a string. What can be said instead is a
/// property of the source: no <c>std::clamp</c> in the file may take a literal as its first
/// argument. That is narrow enough to never fire on correct code and exactly wide enough to catch
/// the mistake that was made - which is what a check earns its place with.
///
/// Like the sanitiser check, it runs in a checkout and reports that it could not run anywhere
/// else, rather than passing quietly.
/// </summary>
public static partial class SessionSource
{
    /// <summary>Where the touch paths live, relative to the repository root.</summary>
    public const string RelativePath = @"gui\src\streamsession.cpp";

    /// <summary>The file, or null when this is not running out of a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>
    /// Every <c>std::clamp</c> call in the file, as its raw argument text. One entry per call, so
    /// a caller can say something about each of them.
    /// </summary>
    public static IReadOnlyList<string> ClampCalls(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        // Comments come out first, and not as tidiness: the comment above the fixed calls quotes
        // the broken form to say what it was, and a check that read that would report the defect
        // it exists to prevent. Found by writing this and watching it go red on its own prose.
        string code = LineCommentRegex().Replace(File.ReadAllText(filePath), "");

        return ClampRegex()
            .Matches(code)
            .Select(m => m.Groups[1].Value)
            .ToList();
    }

    /// <summary>
    /// Whether a call's first argument is a numeric literal, which is the shape of the defect: the
    /// value being clamped is never a constant, so a literal in that position means the bounds and
    /// the value were swapped.
    /// </summary>
    public static bool FirstArgumentIsLiteral(string arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        string first = arguments.Split(',')[0].Trim();
        return first.Length > 0 && (char.IsAsciiDigit(first[0]) || first[0] is '-' or '+' or '.');
    }

    /// <summary>
    /// PP98: the property that keeps the haptics fold saturating.
    ///
    /// Every branch of the intensity switch narrows a uint32_t mean into a uint16_t, and three of
    /// them used to do it bare. What can be said about the source instead of about the arithmetic
    /// is that no assignment in PushHapticsFrame takes a temp straight across - they all go
    /// through rumble_saturate. That is the shape of the mistake, and nothing else looks like it.
    /// </summary>
    public static IReadOnlyList<string> BareRumbleNarrowings(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        string code = LineCommentRegex().Replace(File.ReadAllText(filePath), "");
        return BareNarrowingRegex()
            .Matches(code)
            .Select(m => m.Value.Trim())
            .ToList();
    }

    // `left = temp_left;` and its three siblings: an assignment straight from a temp with nothing
    // between them. A saturating branch reads `left = rumble_saturate(temp_left / 5);` and does
    // not match, which is the whole distinction.
    [GeneratedRegex(@"\b(?:left|right)\s*=\s*temp_(?:left|right)\s*[;/*]")]
    private static partial Regex BareNarrowingRegex();

    // Argument text up to the closing parenthesis. The calls in this file take no nested call in
    // the first argument, so a non-greedy run to the first ")" is enough; one that appeared would
    // truncate the text and, at worst, make this check say a call looks wrong - which is the
    // direction a check should fail in.
    [GeneratedRegex(@"std::clamp\(([^;]*?)\)\s*;")]
    private static partial Regex ClampRegex();

    // Line comments only. streamsession.cpp has no block comment wrapping a clamp, and a stripper
    // that tried to handle every C++ comment form would be a second parser to get wrong.
    [GeneratedRegex(@"//[^\n]*")]
    private static partial Regex LineCommentRegex();
}
