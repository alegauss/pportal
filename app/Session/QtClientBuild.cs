namespace ChiakiNg.Session;

/// <summary>
/// PP598: the Qt client's build is three pieces, and they retire together or not at all.
///
/// THE DECISION, taken 2026-08-31 and recorded here because the commit that acts on it is not this
/// one. PP597 established that PP33's deletion and a buildable Qt client are mutually exclusive -
/// gui/ calls eleven holepunch exports directly, across streamsession.cpp and qmlbackend.cpp, so
/// the file PP33 removes is one gui/ links against. The choice was between letting the client go
/// and holding PP33 back; it is the first. gui/ stays as source and stops being a build target.
///
/// IT DOES NOT HAPPEN YET, AND THAT IS THE POINT. The client builds today - configure resolves,
/// WebEngine is optional in gui/CMakeLists.txt, and `compile.cmd gui` links gui\chiaki.exe in
/// thirteen steps. Taking the affordance away before the deletion that breaks it would remove
/// something that works and buy nothing, so the retirement rides in PP33's own commit. This holds
/// the shape of it until then.
///
/// THREE PIECES, AND THE HALF-DONE RETIREMENT IS THE EXPENSIVE ONE. `compile.cmd`'s `gui` argument
/// sets CHIAKI_ENABLE_GUI=ON; <see cref="GuiFreshness"/> compares the built client against gui/ and
/// fails on Stale; <see cref="GuiFreshness.ClientRelativePath"/> is the binary both mean. Remove the
/// argument and leave the check, and every checkout that ever built a client is permanently red
/// with nothing able to clear it - which is what PP597 was filed to prevent, reached from the other
/// side.
///
/// TWO OF THE THREE ARE HELD BY THE COMPILER, not by a test, and deliberately: this type names
/// GuiFreshness below, so deleting that class without deleting this one does not build. Only the
/// argument lives in a .cmd file no compiler reads, which is why it is the one with an assertion.
/// </summary>
public static class QtClientBuild
{
    /// <summary>The argument that turns the client on, as compile.cmd spells it.</summary>
    public const string CompileArgument = "gui";

    /// <summary>What that argument sets, which is the line that makes the affordance real.</summary>
    public const string EnableFlag = "CHIAKI_ENABLE_GUI=ON";

    /// <summary>The gate that offers it.</summary>
    public const string CompileRelativePath = "compile.cmd";

    /// <summary>
    /// The binary the argument produces and the check reads, named through GuiFreshness rather than
    /// copied.
    ///
    /// This reference is the retirement's own guard: it is what makes deleting GuiFreshness without
    /// deleting this file a build error rather than a check that quietly stopped existing.
    /// </summary>
    public static string ClientRelativePath => GuiFreshness.ClientRelativePath;

    /// <summary>compile.cmd, or null outside a checkout.</summary>
    public static string? LocateCompile() => SanitizerSource.LocateRelative(CompileRelativePath);

    /// <summary>
    /// Whether compile.cmd still offers the argument that builds the client.
    ///
    /// Read as the assignment rather than as the word. `gui` appears in that file's comments and in
    /// its usage line, and PP587's finding one file over was that a banner naming a command is not
    /// the command - so what is looked for is the line that sets the flag, with rem lines skipped
    /// because compile.cmd's own comments quote it while explaining PP529.
    /// </summary>
    public static bool CompileStillBuildsTheClient(string compileCmd)
    {
        ArgumentNullException.ThrowIfNull(compileCmd);

        foreach (string line in compileCmd.ReplaceLineEndings("\n").Split('\n'))
        {
            string trimmed = line.TrimStart();

            if (trimmed.StartsWith("rem ", StringComparison.OrdinalIgnoreCase))
                continue;

            if (trimmed.Contains(EnableFlag, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
