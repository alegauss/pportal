namespace ChiakiNg.Session;

/// <summary>
/// PP431: the CMake modules this build may not use, because CMake has removed them.
///
/// Configuring printed one warning, CMP0148: "The FindPythonInterp and FindPythonLibs modules are
/// removed." Removed, not deprecated. <c>find_package(PythonInterp 3 REQUIRED)</c> worked only while
/// cmake_minimum_required names 3.10 and so selects that policy's OLD behaviour - on a CMake where
/// it is NEW, configure fails rather than warns.
///
/// AND THE COMMENT ON IT NAMED A PURPOSE IT DID NOT SERVE: stopping nanopb from finding Python 2.7.
/// FindPythonInterp set <c>PYTHON_EXECUTABLE</c>; nanopb reads <c>Python_EXECUTABLE</c> and does its
/// own <c>find_package(Python REQUIRED COMPONENTS Interpreter)</c>. Nothing that call set reached
/// nanopb, and the modern module prefers Python 3 on its own.
///
/// BOTH HALVES ARE HELD, because reverting one is the shape a hurried edit takes. The module must
/// not come back, and the variable it set must not be consumed - lib/protobuf runs the nanopb
/// generator as the interpreter and was the only reader in the tree.
///
/// A VERSION FLOOR IS NOT A FIX, which is the general lesson and why this is a rule rather than a
/// one-line change. Raising cmake_minimum_required would turn the warning into an error; lowering it
/// would hide the next one. Naming the removed modules is what survives either.
/// </summary>
public static class RemovedCMakeModules
{
    /// <summary>The build files this rule reads.</summary>
    public static IReadOnlyList<string> Files { get; } =
    [
        "CMakeLists.txt",
        @"lib\CMakeLists.txt",
        @"lib\protobuf\CMakeLists.txt",
        @"gui\CMakeLists.txt",
        @"test\CMakeLists.txt",
    ];

    /// <summary>
    /// The modules CMP0148 removed, as a find_package would name them.
    /// </summary>
    public static IReadOnlySet<string> Removed { get; } =
        new HashSet<string>(StringComparer.Ordinal) { "PythonInterp", "PythonLibs" };

    /// <summary>
    /// The variables those modules set, which nothing may read once the modules are gone.
    ///
    /// Held separately from the modules: a build that stopped calling FindPythonInterp but kept
    /// reading PYTHON_EXECUTABLE would configure and then run the generator as an empty string.
    /// </summary>
    public static IReadOnlySet<string> AbandonedVariables { get; } =
        new HashSet<string>(StringComparer.Ordinal) { "PYTHON_EXECUTABLE", "PYTHON_VERSION_STRING" };

    /// <summary>What the generator is run as now.</summary>
    public const string InterpreterVariable = "Python3_EXECUTABLE";

    /// <summary>
    /// Every use this tree still makes of a removed module or its variables.
    ///
    /// Comments stripped: this fix left comments naming both the module and the variable it
    /// replaced, and a reader that counted those would report the thing it removed. PP400's rule.
    /// </summary>
    public static IReadOnlyList<string> Uses(string root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var uses = new List<string>();

        foreach (string relative in Files)
        {
            string path = Path.Combine(root, relative.Replace('\\', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
                continue;

            string code = CCall.Code(File.ReadAllText(path));

            foreach (string module in Removed)
            {
                if (code.Contains($"find_package({module}", StringComparison.Ordinal))
                    uses.Add($"{relative}: find_package({module}) - CMake removed this module");
            }

            foreach (string variable in AbandonedVariables)
            {
                if (code.Contains($"${{{variable}}}", StringComparison.Ordinal))
                    uses.Add($"{relative}: reads ${{{variable}}}, which nothing sets any more");
            }
        }

        return uses;
    }

    /// <summary>
    /// Whether the nanopb generator is still run as the interpreter FindPython3 provides.
    ///
    /// The other half. A build that dropped the removed module and left the generator reading
    /// nothing would satisfy <see cref="Uses"/> and fail at generate time.
    /// </summary>
    public static bool TheGeneratorStillNamesTheInterpreter(string root)
    {
        ArgumentNullException.ThrowIfNull(root);

        string path = Path.Combine(root, "lib", "protobuf", "CMakeLists.txt");
        if (!File.Exists(path))
            return false;

        string code = CCall.Code(File.ReadAllText(path));

        return code.Contains($"\"${{{InterpreterVariable}}}\"", StringComparison.Ordinal)
            && code.Contains("NANOPB_GENERATOR_PY", StringComparison.Ordinal);
    }

    /// <summary>And whether the top-level build asks for that interpreter at all.</summary>
    public static bool TheInterpreterIsStillFound(string root)
    {
        ArgumentNullException.ThrowIfNull(root);

        string path = Path.Combine(root, "CMakeLists.txt");
        if (!File.Exists(path))
            return false;

        string code = CCall.Code(File.ReadAllText(path));

        return code.Contains("find_package(Python3", StringComparison.Ordinal)
            && code.Contains("COMPONENTS Interpreter", StringComparison.Ordinal);
    }
}
