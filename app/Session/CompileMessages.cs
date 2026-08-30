namespace ChiakiNg.Session;

/// <summary>
/// PP532: the lines compile.cmd prints about the Qt client, and whether each one is entitled to.
///
/// PP21 turned the client off and passes -DCHIAKI_ENABLE_GUI=OFF on every configure. Two messages
/// were left behind: the nodeploy success line named build\gui\chiaki.exe as what the run
/// produced, and the failure line credited "the Qt client built". Neither was a wrong build. Both
/// were a run describing itself wrongly, and the first was worse on a machine that HAS a client
/// from an earlier run, because the path resolves and points a reader at a real binary that is
/// not the one they just made.
///
/// The rule is not "do not mention the client". It is that a line claiming it must sit behind
/// something that knows whether there is one: a test of the flag, or a test that the file exists.
/// Those are the two honest ways to say it, and the two messages that broke had neither.
///
/// Held from here rather than by a shell test because the tree asserts shell scripts nowhere and
/// a batch harness is a larger thing than the defect. This reads the file the way BuildWorkflow
/// reads build.yml.
/// </summary>
public static class CompileMessages
{
    /// <summary>The gate, at the repository root.</summary>
    public const string RelativePath = "compile.cmd";

    /// <summary>The Qt client's path, as every line that names it spells the tail.</summary>
    public const string ClientPath = @"\gui\chiaki.exe";

    /// <summary>The client by name, for a line that claims it without naming the file.</summary>
    public const string ClientName = "the Qt client";

    /// <summary>The flag that decides whether there is one.</summary>
    public const string GuiFlag = "CHIAKI_ENABLE_GUI";

    /// <summary>The file, or null when this is not running out of a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>One line that claims the client, and whether anything checked first.</summary>
    /// <param name="Line">Its 1-based line number.</param>
    /// <param name="Text">The line, trimmed.</param>
    /// <param name="Guarded">Whether a flag test or an existence test stands before it.</param>
    public sealed record Claim(int Line, string Text, bool Guarded);

    /// <summary>
    /// Every echoed line that claims the client, with its verdict.
    ///
    /// Blocks are batch labels, and the guard has to be in the SAME one. A `goto` to a label whose
    /// body then prints is exactly how this defect would come back: the test would sit in the
    /// block above, and a rule that swept the whole file would call the printing block guarded by
    /// something it never runs.
    /// </summary>
    public static IReadOnlyList<Claim> Claims(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        string[] lines = source.ReplaceLineEndings("\n").Split('\n');
        var claims = new List<Claim>();
        var guardedAbove = false;

        for (int i = 0; i < lines.Length; i++)
        {
            string text = lines[i].Trim();

            // A label starts a new block, so whatever was checked above is no longer in scope.
            if (text.StartsWith(':') && text.Length > 1 && char.IsLetter(text[1]))
            {
                guardedAbove = false;
                continue;
            }

            if (text.StartsWith("rem ", StringComparison.OrdinalIgnoreCase) || text == "rem")
                continue;

            bool namesClient = text.Contains(ClientPath, StringComparison.OrdinalIgnoreCase)
                || text.Contains(ClientName, StringComparison.OrdinalIgnoreCase);
            bool namesFlag = text.Contains(GuiFlag, StringComparison.Ordinal);
            bool testsExistence = text.Contains("exist", StringComparison.OrdinalIgnoreCase)
                && text.Contains(ClientPath, StringComparison.OrdinalIgnoreCase);

            if (text.Contains("echo", StringComparison.OrdinalIgnoreCase) && namesClient)
            {
                // A line that names the flag is a line ABOUT the flag, and says so to the reader
                // as plainly as a test above it would.
                claims.Add(new Claim(i + 1, text, guardedAbove || namesFlag));
            }

            if (namesFlag || testsExistence)
                guardedAbove = true;
        }

        return claims;
    }
}
