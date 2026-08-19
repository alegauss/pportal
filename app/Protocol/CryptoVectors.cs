using System.Globalization;
using System.Text.RegularExpressions;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP23: the oracle, read out of the suite that already holds it.
///
/// test/rpcrypt.c is 311 lines of nonces, morning keys and the exact bytes a real console produced
/// from them. That is the closest thing this protocol's key derivation has to a specification, and
/// the one thing a managed rewrite must not do is keep its own copy of it: two copies of an oracle
/// agree with each other long after either agrees with a console, which is exactly the failure
/// PP82 named about the preference table.
///
/// So the vectors are parsed out of the C file. Every array named in a test function is available
/// by name, so a managed assertion cites the same bytes the munit case does - and a vector edited
/// in one place is edited for both, because there is only one place.
///
/// Like every other source check in this port it runs in a checkout and says so when it cannot,
/// rather than passing quietly.
/// </summary>
public static partial class CryptoVectors
{
    /// <summary>Where the vectors live, relative to the repository root.</summary>
    public const string RelativePath = @"test\rpcrypt.c";

    /// <summary>The file, or null when this is not running out of a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>
    /// Every <c>static const uint8_t name[] = { … };</c> in one C function, by name.
    ///
    /// Scoped to a function rather than to the file because the names repeat: half a dozen cases
    /// each declare a `nonce` and a `morning`, and they are different bytes. A file-wide lookup
    /// would silently answer with whichever came last.
    /// </summary>
    public static IReadOnlyDictionary<string, byte[]> InFunction(string filePath, string function)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(function);

        string text = File.ReadAllText(filePath);
        Match fn = Regex.Match(text, @"\b" + Regex.Escape(function) + @"\s*\([^)]*\)\s*\{", RegexOptions.Singleline);
        if (!fn.Success)
            return new Dictionary<string, byte[]>(StringComparer.Ordinal);

        // To the end of the function, found by matching braces from the opening one. A byte array
        // holds no braces of its own, so counting is enough and a C parser is not.
        int start = fn.Index + fn.Length;
        int depth = 1;
        int i = start;
        while (i < text.Length && depth > 0)
        {
            if (text[i] == '{')
                depth++;
            else if (text[i] == '}')
                depth--;
            i++;
        }

        var found = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (Match m in ArrayRegex().Matches(text[start..i]))
        {
            found[m.Groups[1].Value] = m.Groups[2].Value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(ParseByte)
                .ToArray();
        }

        return found;
    }

    /// <summary>
    /// Every byte array in a file, at any scope. Some vectors are declared once at file scope and
    /// shared by every case in it - test/regist.c's ambassador is one - so a function-scoped
    /// lookup alone would miss exactly the value all the others are derived from.
    ///
    /// For UNIQUELY named arrays only. Names repeat across cases - every one of them declares its
    /// own `expected` - and this answers with whichever came last, silently. Use
    /// <see cref="InFunction"/> for anything a case owns.
    /// </summary>
    public static IReadOnlyDictionary<string, byte[]> InFile(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        var found = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (Match m in ArrayRegex().Matches(File.ReadAllText(filePath)))
        {
            found[m.Groups[1].Value] = m.Groups[2].Value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(ParseByte)
                .ToArray();
        }

        return found;
    }

    /// <summary>
    /// A scalar constant's literal text, so a pin or an id can be cited rather than copied. Null
    /// where the file does not declare one by that name.
    /// </summary>
    public static string? ScalarInFile(string filePath, string name)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(name);

        Match m = Regex.Match(
            File.ReadAllText(filePath),
            @"\b" + Regex.Escape(name) + @"\s*=\s*([^;]+);");

        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    private static byte ParseByte(string token)
        => token.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? byte.Parse(token[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            : byte.Parse(token, CultureInfo.InvariantCulture);

    // The bracket may carry a size. test/regist.c writes `ambassador[CHIAKI_RPCRYPT_KEY_SIZE]`
    // where test/rpcrypt.c writes `nonce[]`, and both are vectors this has to be able to read.
    [GeneratedRegex(@"uint8_t\s+(\w+)\s*\[[^\]]*\]\s*=\s*\{([^}]*)\}", RegexOptions.Singleline)]
    private static partial Regex ArrayRegex();
}
