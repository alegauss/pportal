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

    /// <summary>The label of the ending a default run reaches, since the deploy runs by default.</summary>
    public const string DeployLabel = ":ok_deploy";

    /// <summary>The file, or null when this is not running out of a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>
    /// PP586: whether the deploy ending picks its recommendation from the FLAG and not from a file
    /// being on disk.
    ///
    /// The claim sweep above cannot see this one. The line it recommends is
    /// `echo [compile]   %~dp0%DEPLOY_DISP%\chiaki.exe` - the portable tree, not `\gui\chiaki.exe`
    /// and not the words "the Qt client" - so it names neither thing <see cref="Claims"/> looks
    /// for, and the branch above it decided which ending was reached rather than what a line said.
    ///
    /// PRESENCE IS NOT PROVENANCE. `if not exist` was the test, and it is a fair guard for a line
    /// saying the client ALSO exists (that line asks exactly the question it answers). It is not a
    /// guard for "run this one": a stale binary from an earlier `compile.cmd gui` satisfies it, so
    /// a run that had just printed that the Qt deploy was skipped recommended the Qt client anyway,
    /// and never named the .NET host - which is where --recount and --ratchet are.
    /// </summary>
    public static bool TheDeployEndingAsksTheFlag(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        string[] lines = source.ReplaceLineEndings("\n").Split('\n');
        var inBlock = false;

        foreach (string raw in lines)
        {
            string text = raw.Trim();

            if (text.StartsWith(':') && text.Length > 1 && char.IsLetter(text[1]))
            {
                // Exactly this label. `:ok_deploy_managed` is the ending this one falls through
                // TO, and matching it by prefix would read that block's first `if` instead.
                inBlock = text.Equals(DeployLabel, StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inBlock || text.StartsWith("rem ", StringComparison.OrdinalIgnoreCase))
                continue;

            // The FIRST decision in the block is the one that chooses the ending. A later `if exist`
            // is free to add a note; what this holds is which question was asked first.
            if (text.StartsWith("if ", StringComparison.OrdinalIgnoreCase))
                return text.Contains(GuiFlag, StringComparison.Ordinal);
        }

        return false;
    }

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
                //
                // PP586: and so does its own test. `if exist <path> echo <path>` is cmd's one-line
                // form of the two-line block below, and asks the same question of the same path -
                // so a rule that only looked ABOVE would report the tighter of the two spellings
                // as the unguarded one.
                claims.Add(new Claim(i + 1, text, guardedAbove || namesFlag || testsExistence));
            }

            if (namesFlag || testsExistence)
                guardedAbove = true;
        }

        return claims;
    }
}
