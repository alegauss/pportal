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
    /// PP599: the file that drives the C session's PSN path, which is the client this retires.
    ///
    /// The two facts that were separate until decision A joined them. PP596 established that the Qt
    /// client is the ONLY thing that puts a holepunch handle into a ChiakiSession, so it is the only
    /// thing that can enter session.c's PSN path at all. This record says that same client stops
    /// being built. Together they say nobody will ask session.c for PSN once the retirement lands.
    ///
    /// WHY IT MATTERS TO PP33'S PLAN. §PP533 settled the direction as a CONVERSION - session.c stops
    /// taking a holepunch handle and starts taking the five results it currently derives, four of
    /// them durable and the registration info scoped to its own block (PP551). That design assumed
    /// session.c must keep doing PSN for somebody. After A it does not: the managed flow owns the
    /// PSN sequence (PP340), the shim never passes a handle (PP592), and the Qt client is going. So
    /// the nine asks are REMOVED, and the five-result plumbing is work that no longer has a caller.
    ///
    /// This is not a contradiction of PP533 - it is its premise expiring, which is a thing a settled
    /// design is allowed to do and a thing nobody notices unless it is written down.
    /// </summary>
    public static string PsnDriverRelativePath => Protocol.HolepunchSessionOwnership.QtClientRelativePath;

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
