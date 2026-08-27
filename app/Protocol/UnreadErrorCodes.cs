using System.Text.RegularExpressions;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>One error code whose only reader is an assert.</summary>
/// <param name="File">Repository-relative, with forward slashes.</param>
/// <param name="Variable">The identifier the declaration gives it.</param>
/// <param name="Callee">The call whose result it holds.</param>
public readonly record struct UnreadErrorCode(string File, string Variable, string Callee);

/// <summary>
/// PP428: the error codes the shipped build ignores entirely, found rather than transcribed.
///
/// PP357 argued from the source that an assert is nothing here, because Release is built with
/// -DNDEBUG. PP404 censused the asserts that inspect an error code and PP406 split them by whether
/// the callee can fail. PP426 then counted the build's warnings and found seventeen of them saying
/// the same thing - and none of the three tasks had cited the compiler.
///
/// THE TWO READINGS ANSWER DIFFERENT QUESTIONS, which is why joining them is worth something.
/// <see cref="AssertedErrorCodes"/> is authoritative about what is ASSERTED. This is authoritative
/// about what the shipped build IGNORES: a declaration whose identifier appears nowhere but inside
/// an assert, so removing the assert removes the last reader. That is the subset and the sharper
/// number, because a result read elsewhere still has a reader once the assert goes.
///
/// DERIVED, NOT COPIED. PP425 established why: a list transcribed out of the compiler's output would
/// confirm the transcription. This finds the shape, and the count it produces is held against what
/// a clean rebuild prints - seventeen, which is an independent oracle rather than the same reading
/// twice.
///
/// EVERY ONE MUST BE IN PP404'S CENSUS. The compiler names them all; a census that missed one would
/// still read as complete, and that is the hole this closes.
/// </summary>
public static partial class UnreadErrorCodes
{
    /// <summary>The tree this reads over.</summary>
    public const string RelativePath = @"lib\src";

    /// <summary>
    /// What a clean rebuild of lib prints as unused variable 'err' or 'mutex_err', plus the two it
    /// calls set-but-not-used.
    ///
    /// The independent oracle. If this reader's count and this number part company, one of the two
    /// is wrong and neither can be trusted until that is settled.
    /// </summary>
    public const int WhatTheCompilerCounts = 17;

    /// <summary>lib/src, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateDirectory(RelativePath);

    /// <summary>Every error code in the tree whose only reader is an assert.</summary>
    public static IReadOnlyList<UnreadErrorCode> All(string root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var found = new List<UnreadErrorCode>();

        string lib = Path.Combine(root, RelativePath);
        if (!Directory.Exists(lib))
            return found;

        foreach (string path in Directory.EnumerateFiles(lib, "*.c", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            found.AddRange(InFile(File.ReadAllText(path)).Select(
                one => one with { File = relative }));
        }

        return found;
    }

    /// <summary>
    /// The same, for one file's text. The File of each is left empty for the caller to fill.
    /// </summary>
    public static IReadOnlyList<UnreadErrorCode> InFile(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        string code = CCall.Code(source);
        var found = new List<UnreadErrorCode>();

        foreach (Match declaration in DeclarationRegex().Matches(code))
        {
            string name = declaration.Groups["name"].Value;
            string callee = declaration.Groups["callee"].Value;

            if (OnlyReaderIsAnAssert(code, name, declaration.Index))
                found.Add(new UnreadErrorCode("", name, callee));
        }

        return found;
    }

    /// <summary>
    /// Whether every read of this declaration's identifier, inside the block it was declared in, is
    /// an assert.
    ///
    /// SCOPED TO THE BLOCK, WHICH IS THE WHOLE DIFFICULTY. The first version of this asked the file,
    /// and found three of the seventeen: `err` is declared in nearly every function in ctrl.c, and a
    /// file-wide search sees the other functions' reads and concludes the assert is not the only one.
    /// The compiler is talking about ONE declaration in ONE block, and so is this.
    /// </summary>
    /// <param name="code">The translation unit, comments already stripped.</param>
    /// <param name="name">The identifier the declaration gives it.</param>
    /// <param name="declaredAt">Where the declaration starts.</param>
    public static bool OnlyReaderIsAnAssert(string code, string name, int declaredAt)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentException.ThrowIfNullOrEmpty(name);

        if (declaredAt < 0 || declaredAt >= code.Length)
            return false;

        (int from, int to) = EnclosingBlock(code, declaredAt);
        if (from < 0)
            return false;

        var reads = 0;
        var asserted = 0;

        foreach (Match use in Regex.Matches(
                     code[from..to], $@"(?<![A-Za-z0-9_]){Regex.Escape(name)}(?![A-Za-z0-9_])"))
        {
            int at = from + use.Index;

            // The declaration itself is not a read.
            if (at >= declaredAt && at <= declaredAt + DeclarationSpan)
                continue;

            // NOR IS A REASSIGNMENT. `err = chiaki_thread_join(..)` writes the variable; counting
            // it as a read is what left three of the seventeen out, and the compiler names those
            // "set but not used" precisely because BOTH assignments are only ever asserted.
            if (IsWrittenAt(code, at + name.Length))
                continue;

            reads++;

            if (InsideAnAssert(code, at))
                asserted++;
        }

        return reads > 0 && reads == asserted;
    }

    /// <summary>
    /// Whether what follows an identifier is a plain assignment to it.
    ///
    /// <c>=</c> and not <c>==</c>, <c>!=</c>, <c>&gt;=</c> or <c>&lt;=</c>: a comparison reads the
    /// variable and an assignment replaces it.
    /// </summary>
    public static bool IsWrittenAt(string code, int after)
    {
        ArgumentNullException.ThrowIfNull(code);

        int at = after;
        while (at < code.Length && (code[at] == ' ' || code[at] == '\t'))
            at++;

        if (at >= code.Length || code[at] != '=')
            return false;

        // "==" is a comparison; a single "=" is a write.
        return at + 1 >= code.Length || code[at + 1] != '=';
    }

    /// <summary>
    /// How far past a declaration's start its own identifier can sit.
    ///
    /// "ChiakiErrorCode " is sixteen characters; a generous bound covers whitespace without reaching
    /// the assert on the next line.
    /// </summary>
    private const int DeclarationSpan = 24;

    /// <summary>
    /// The block a position sits in, as the half-open range of its braces.
    ///
    /// Walked backwards counting braces until one opens that has not closed, then forwards to its
    /// match. Returns (-1, -1) where the position is not inside a block at all.
    /// </summary>
    public static (int From, int To) EnclosingBlock(string code, int at)
    {
        ArgumentNullException.ThrowIfNull(code);

        if (at < 0 || at >= code.Length)
            return (-1, -1);

        var depth = 0;
        var from = -1;

        for (int back = at; back >= 0; back--)
        {
            if (code[back] == '}')
            {
                depth++;
            }
            else if (code[back] == '{')
            {
                if (depth == 0)
                {
                    from = back;
                    break;
                }

                depth--;
            }
        }

        if (from < 0)
            return (-1, -1);

        depth = 0;
        for (int forward = from; forward < code.Length; forward++)
        {
            if (code[forward] == '{')
            {
                depth++;
            }
            else if (code[forward] == '}' && --depth == 0)
            {
                return (from, forward);
            }
        }

        return (from, code.Length);
    }

    /// <summary>
    /// Whether an index sits between an <c>assert(</c> and the semicolon that ends it.
    ///
    /// The nearest assert behind it, and the nearest semicolon behind it: if the assert is closer,
    /// the index is inside one.
    /// </summary>
    public static bool InsideAnAssert(string code, int at)
    {
        ArgumentNullException.ThrowIfNull(code);

        int assert = code.LastIndexOf("assert(", at, StringComparison.Ordinal);
        if (assert < 0)
            return false;

        int statement = code.LastIndexOf(';', at);

        return assert > statement;
    }

    /// <summary>
    /// How many the tree still holds, which is the count the compiler prints.
    ///
    /// Not a ratchet at zero: sixteen of the seventeen assert a mutex lock or a condition signal,
    /// which PP406 established cannot fail on this platform, and rewriting those into checks would
    /// add a failure path for something that has none. The number is held so a NEW one is visible.
    /// </summary>
    public const int Ceiling = WhatTheCompilerCounts;

    // ChiakiErrorCode <name> = <callee>(...);
    [GeneratedRegex(@"ChiakiErrorCode\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<callee>[A-Za-z_][A-Za-z0-9_]*)\s*\(")]
    private static partial Regex DeclarationRegex();
}
