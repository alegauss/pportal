namespace ChiakiNg.Session;

/// <summary>
/// PP492: the step that fills the portable tree, and the branch of it a GUI-off build takes.
///
/// The .NET host loads chiaki-shim.dll and chiaki-render.dll out of <c>build\chiaki-ng-Win</c> -
/// <see cref="ChiakiNg.Native.ChiakiNative"/> looks there before it looks at the build directory,
/// because whatever is on PATH is not this build's. What puts them there is the deploy, and the
/// deploy has two paths: the Qt one, which walks the client's whole dependency closure with ldd,
/// and the native-only one PP269 added for builds with CHIAKI_ENABLE_GUI off.
///
/// PP269's path copied the two libraries and nothing else. That is invisible on an incremental
/// build - the closure is already in the tree from whenever the client was last built with the GUI
/// on - and produces an unloadable tree after a clean, where the two land beside nothing. The
/// resolver then finds the file, TryLoad fails on a missing import, and the error says the DLL "was
/// not found" while listing the path it is sitting at.
///
/// SO THE WALK IS A SCRIPT NOW, AND BOTH PATHS CALL IT. What is asserted here is that call, from
/// both sides, because the tree it produces cannot be checked at all from a machine with no MSYS2:
/// there is no ldd, no /mingw64, and the closure is a different set of files on a runner than it is
/// here. A predicate over the scripts is what a checkout can answer.
/// </summary>
public static class NativeDeploy
{
    /// <summary>The build driver, which owns the choice between the two deploy paths.</summary>
    public const string BuildRelativePath = @"scripts\build-windows.sh";

    /// <summary>The Qt deploy, which packages the client and its closure.</summary>
    public const string QtDeployRelativePath = @"scripts\deploy-windows-msys2.sh";

    /// <summary>The closure walk itself, called by both.</summary>
    public const string ClosureRelativePath = @"scripts\deploy-native-deps.sh";

    /// <summary>The two this repository builds, copied out of the build directory.</summary>
    public static IReadOnlyList<string> BuiltHere { get; } =
        ["chiaki-shim.dll", "chiaki-render.dll"];

    /// <summary>
    /// The one that is staged from MSYS2 instead, because no walk can find it.
    ///
    /// The host opens SDL2 by name at runtime, so nothing in the tree imports it and ldd never
    /// reports it. A Qt build gets it as a dependency of chiaki.exe; a GUI-off build has to be told.
    /// The Qt deploy stages SDL3 explicitly for exactly this reason, one version along.
    /// </summary>
    public static IReadOnlyList<string> StagedFromMsys2 { get; } = ["SDL2.dll"];

    /// <summary>Everything the portable tree owes the host, from both sources.</summary>
    public static IReadOnlyList<string> HostLibraries { get; } = [.. BuiltHere, .. StagedFromMsys2];

    /// <summary>The build driver, or null outside a checkout.</summary>
    public static string? LocateBuild() => SanitizerSource.LocateRelative(BuildRelativePath);

    /// <summary>The Qt deploy, or null outside a checkout.</summary>
    public static string? LocateQtDeploy() => SanitizerSource.LocateRelative(QtDeployRelativePath);

    /// <summary>The closure walk, or null outside a checkout.</summary>
    public static string? LocateClosure() => SanitizerSource.LocateRelative(ClosureRelativePath);

    /// <summary>The script's own name, as a caller spells it.</summary>
    private const string ScriptName = "deploy-native-deps.sh";

    /// <summary>
    /// Whether the native-only branch collects the closure as well as the two libraries.
    ///
    /// The branch is <c>deploy_native_only</c>; the assertion is that it both stages the pair and
    /// hands them to the walk. Staging alone is what PP269 shipped and what this repairs, so a
    /// check for the copy loop would pass on the broken version.
    /// </summary>
    public static bool TheNativeOnlyPathCollectsTheClosure(string buildScript)
    {
        ArgumentNullException.ThrowIfNull(buildScript);

        string text = buildScript.Replace("\r\n", "\n", StringComparison.Ordinal);

        int branch = text.IndexOf("if [[ ${deploy_native_only:-0} -eq 1 ]]; then", StringComparison.Ordinal);
        if (branch < 0)
            return false;

        // To the end of the file is enough: this branch is the last thing before the Qt one, and a
        // call that landed after both would not be inside either.
        string after = text[branch..];
        int qtBranch = after.IndexOf("if [[ $do_deploy -eq 1 ]]; then", StringComparison.Ordinal);
        string body = qtBranch > 0 ? after[..qtBranch] : after;

        return body.Contains(ScriptName, StringComparison.Ordinal)
            && HostLibraries.All(dll => body.Contains(dll, StringComparison.Ordinal));
    }

    /// <summary>
    /// Whether the Qt path delegates its walk rather than keeping a second copy of it.
    ///
    /// Both halves: the call is there, and the loop it replaced is not. A script that calls the
    /// walk AND still carries its own would drift silently, since the two would agree until one
    /// was edited.
    /// </summary>
    public static bool TheQtPathDelegatesItsWalk(string qtDeployScript)
    {
        ArgumentNullException.ThrowIfNull(qtDeployScript);

        return qtDeployScript.Contains(ScriptName, StringComparison.Ordinal)
            && !qtDeployScript.Contains("extract_dependencies()", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the walk is transitive - it queues what it copies and scans that too.
    ///
    /// One level would be enough for the shim and would miss libplacebo's, which is the kind of
    /// difference that shows up as one machine working and another not.
    /// </summary>
    public static bool TheWalkIsTransitive(string closureScript)
    {
        ArgumentNullException.ThrowIfNull(closureScript);

        return closureScript.Contains("queue+=(\"$dependency\")", StringComparison.Ordinal)
            && closureScript.Contains("while [[ ${#queue[@]} -gt 0 ]]", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the walk still refuses to bundle a Windows system DLL.
    ///
    /// Copying one is not a missing file, so nothing downstream reports it - the application ships
    /// a kernel DLL from this machine and loads it in preference to the one the user's has.
    /// </summary>
    public static bool SystemLibrariesAreNotBundled(string closureScript)
    {
        ArgumentNullException.ThrowIfNull(closureScript);

        return closureScript.Contains("grep -iv \"system32\"", StringComparison.Ordinal)
            && closureScript.Contains("grep -iv \"windows\"", StringComparison.Ordinal);
    }
}
