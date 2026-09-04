using System.Text.RegularExpressions;

namespace ChiakiNg.Session;

/// <summary>One COM method whose status the caller reads and whose return the CLR would rewrite.</summary>
/// <param name="Where">The file, repository-relative.</param>
/// <param name="Interface">The interface it is declared on.</param>
/// <param name="Method">The method's name.</param>
/// <param name="Returns">What it is declared to return, which is why the omission matters.</param>
public readonly record struct UnpreservedComMethod(string Where, string Interface, string Method, string Returns);

/// <summary>
/// PP693: a COM method declared without PreserveSig answers with an uninitialised int.
///
/// PP652's spike declared sixteen WASAPI methods as <c>int Foo(out X bar)</c> on a ComImport
/// interface and left the attribute off every one. Without it the CLR reads the declared int as an
/// <c>[out, retval]</c> and treats the real return as an HRESULT it turns into exceptions - so
/// every <c>hr == 0</c> compared against an uninitialised local, and every genuine failure arrived
/// as a thrown exception rather than the code the caller was testing for.
///
/// IT DID NOT CRASH. IT ANSWERED. Three of four capture devices reported unreadable were readable,
/// and the one reported as taking the console's format in shared mode does not - no device does.
/// The corrected run inverted the finding and the conclusion drawn from it, and the only reason it
/// was caught is that the numbers looked odd enough to re-read.
///
/// WHICH IS WHY THIS IS A CHECK AND NOT A NOTE. PP650's spike is clean and backs a shipped decision
/// about the video decoder, so the tree already carries a COM surface a decision rests on. The
/// failure is silent, the fix is one attribute, and nothing between the two was looking.
///
/// WHAT IS ALLOWED. A method returning void is not reading a status and needs nothing; the CLR's
/// rewrite is only a problem where the declaration claims the return. So the check reads the return
/// type and flags int and uint alone, which is the distinction that keeps it from being a rule
/// about attributes rather than about correctness.
///
/// PP705: this file RECORDS the phrases it judges, so every sweep here skips it.
/// </summary>
public static partial class ComSignatures
{
    /// <summary>The attribute whose absence is the defect.</summary>
    public const string Attribute = "PreserveSig";

    /// <summary>The two ways this tree declares a COM interface.</summary>
    public static IReadOnlyList<string> InterfaceMarkers { get; } = ["ComImport", "GeneratedComInterface"];

    /// <summary>The return types that claim a status the caller reads.</summary>
    public static IReadOnlyList<string> StatusReturns { get; } = ["int", "uint"];

    /// <summary>The directories a sweep reads, which is every part of the tree that holds C#.</summary>
    public static IReadOnlyList<string> SweptDirectories { get; } = ["app", "tests", "spike", "tools"];

    /// <summary>
    /// This check's own tests, excluded: they declare the defect on purpose.
    ///
    /// The same shape <see cref="MicrophoneSurface.CensusFileName"/> has, and for the same reason -
    /// a check whose subject is a text pattern will find the fixture that demonstrates it, and
    /// the first run of this one reported four of its own. Named rather than pattern-matched, so
    /// a second file wanting the exemption has to be added here on purpose.
    /// </summary>
    public const string FixtureFileName = "ComSignaturesTests.cs";

    /// <summary>The repository root, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.RepositoryRoot();

    /// <summary>
    /// Every offending method in one source, with the interface each is on.
    ///
    /// A hand reader rather than a parser, for <see cref="FecConsumers"/>'s reason: what is being
    /// looked for is narrow and regular - an interface declaration carrying a marker, then the
    /// method lines inside its braces until they balance. Comments are stripped first, so a
    /// docstring naming the attribute does not satisfy the check for the method under it.
    /// </summary>
    public static IReadOnlyList<UnpreservedComMethod> UnpreservedIn(string where, string source)
    {
        ArgumentNullException.ThrowIfNull(where);
        ArgumentNullException.ThrowIfNull(source);

        string[] lines = CCall.Code(source).ReplaceLineEndings("\n").Split('\n');

        var found = new List<UnpreservedComMethod>();
        bool marked = false;
        string? inside = null;
        int depth = 0;
        bool preserved = false;

        foreach (string raw in lines)
        {
            string line = raw.Trim();

            if (inside is null)
            {
                if (InterfaceMarkers.Any(marker => line.Contains('[' + marker, StringComparison.Ordinal)))
                {
                    marked = true;
                    continue;
                }

                if (marked && Declaration().Match(line) is { Success: true } declaration)
                {
                    inside = declaration.Groups["name"].Value;
                    depth = line.Count(c => c == '{');
                    marked = false;
                    preserved = false;
                    continue;
                }

                // A marker reaches the NEXT declaration and no further. ComImport on a class is
                // legal and common, and leaving the flag armed past it would arm the check for an
                // ordinary interface further down the file - which is how it first found four of
                // its own fixtures.
                if (marked && OtherDeclaration().IsMatch(line))
                    marked = false;

                continue;
            }

            depth += line.Count(c => c == '{');
            depth -= line.Count(c => c == '}');

            if (line.Contains('[' + Attribute, StringComparison.Ordinal))
                preserved = true;

            if (Method().Match(line) is { Success: true } method)
            {
                string returns = method.Groups["returns"].Value;

                if (!preserved && StatusReturns.Contains(returns, StringComparer.Ordinal))
                    found.Add(new UnpreservedComMethod(where, inside, method.Groups["name"].Value, returns));

                preserved = false;
            }

            if (depth <= 0)
                inside = null;
        }

        return found;
    }

    /// <summary>
    /// Every offending method in the tree.
    ///
    /// Swept rather than listed, because the whole failure is one somebody would not think to add
    /// to a list: the next COM interface anybody writes is the one this exists for.
    /// </summary>
    public static IReadOnlyList<UnpreservedComMethod> UnpreservedInTheTree()
    {
        if (Locate() is not { } root)
            return [];

        var found = new List<UnpreservedComMethod>();

        foreach (string directory in SweptDirectories)
        {
            string full = Path.Combine(root, directory);
            if (!Directory.Exists(full))
                continue;

            foreach (string file in PhraseCensus
                .Sweepable(Directory.EnumerateFiles(full, "*.cs", SearchOption.AllDirectories))
                .OrderBy(one => one, StringComparer.OrdinalIgnoreCase))
            {
                found.AddRange(UnpreservedIn(Path.GetRelativePath(root, file), File.ReadAllText(file)));
            }
        }

        return found;
    }

    /// <summary>Every file in the tree that declares a COM interface at all, so the sweep is not empty.</summary>
    public static IReadOnlyList<string> FilesDeclaringComInterfaces()
    {
        if (Locate() is not { } root)
            return [];

        var found = new List<string>();

        foreach (string directory in SweptDirectories)
        {
            string full = Path.Combine(root, directory);
            if (!Directory.Exists(full))
                continue;

            foreach (string file in PhraseCensus
                .Sweepable(Directory.EnumerateFiles(full, "*.cs", SearchOption.AllDirectories))
                .OrderBy(one => one, StringComparer.OrdinalIgnoreCase))
            {
                string code = CCall.Code(File.ReadAllText(file));

                if (InterfaceMarkers.Any(marker => code.Contains('[' + marker, StringComparison.Ordinal)))
                    found.Add(Path.GetRelativePath(root, file));
            }
        }

        return found;
    }

    // An interface declaration, with or without a modifier before it.
    [GeneratedRegex(@"\binterface\s+(?<name>I\w+)")]
    private static partial Regex Declaration();

    // Anything else a marker can sit on, which ends its reach.
    [GeneratedRegex(@"\b(?:class|struct|record|enum|delegate)\b")]
    private static partial Regex OtherDeclaration();

    // One method inside it: a return type, a name, an open paren. A property or field has no paren,
    // and an attribute line is not a method because it does not reach one.
    [GeneratedRegex(@"^(?:\[[^\]]*\]\s*)*(?<returns>[A-Za-z_][\w<>?.\[\]]*)\s+(?<name>\w+)\s*\(")]
    private static partial Regex Method();
}
