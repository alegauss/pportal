using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ChiakiNg.Native;

/// <summary>Which renderer resolved, as chiaki_decoder_choice reads it.</summary>
public enum ChiakiRenderer { Vulkan = 0, OpenGL = 1 }

/// <summary>
/// PP4: the managed half of the seam.
///
/// Every call into the protocol goes through chiaki-shim.dll, and not through libchiaki
/// directly. libchiaki is a static archive whose CHIAKI_EXPORT expands to nothing, so there are
/// no symbols to import however the marshalling is written - and giving it some would mean
/// starting the port by editing the half that is not being ported, across 95 functions and 22
/// callback typedefs. The shim exports what the port needs, keeps every struct on the C side of
/// the line, and is where a Qt-free copy of streamsession.cpp's adaptation goes.
///
/// Loading it by hand
/// ------------------
/// The default resolver looks beside the assembly, and the shim is not there. It statically
/// links chiaki-lib, which reaches OpenSSL, so it imports libcrypto-3-x64.dll - and the one
/// directory that already holds that is the portable tree the Qt client is deployed into. So the
/// resolver below looks there first. NativeLibrary.Load of an absolute path uses the altered
/// search path, which means the shim's own imports resolve out of the directory it came from
/// rather than out of whatever happens to be on PATH.
///
/// A build directory is probed after it, so a developer who ran compile.cmd without the deploy
/// step still gets a loadable shim. Nothing probes the system directories: a chiaki-shim.dll
/// found somewhere else on the machine is not this build's, and silently calling it is the
/// failure the ABI check below exists to catch.
/// </summary>
public static class ChiakiNative
{
    /// <summary>
    /// The name every DllImport in this assembly carries, including the ones in other types -
    /// they all come through the one resolver below.
    /// </summary>
    internal const string Library = "chiaki-shim";

    /// <summary>
    /// PP8: SDL, which the port calls directly rather than through the shim.
    ///
    /// "SDL is not Qt and does not have to move" - so it does not. What it does need is the same
    /// resolver: SDL2.dll lives in the portable tree beside chiaki-shim.dll, and letting the
    /// runtime find it on PATH would pick up whichever SDL a machine happens to have.
    /// </summary>
    internal const string Sdl = "SDL2";

    /// <summary>Must equal CHIAKI_SHIM_ABI in shim/chiaki_shim.h.</summary>
    public const uint ExpectedAbi = 30;

    /// <summary>
    /// A module initializer and not a static constructor, because the resolver has to be in place
    /// before the first P/Invoke in the ASSEMBLY, not before the first one in this class. A static
    /// constructor runs when its own type is first touched, so a call into chiaki-shim.dll from
    /// <see cref="ChiakiLog"/> - which is where the callbacks live - would have gone out through
    /// the runtime's default search and loaded whichever chiaki-shim.dll the PATH offered.
    ///
    /// SetDllImportResolver throws if it is called twice for one assembly, so this is also the
    /// reason there is exactly one of these.
    /// </summary>
    [ModuleInitializer]
    internal static void InstallResolver()
        => NativeLibrary.SetDllImportResolver(typeof(ChiakiNative).Assembly, Resolve);

    /// <summary>The path the shim was loaded from, or null while it has never been loaded.</summary>
    public static string? LoadedFrom { get; private set; }

    /// <summary>
    /// Where a shim built by this repository can be: beside the executable, then in the native
    /// build tree of the checkout it came out of.
    ///
    /// PP22: AppContext.BaseDirectory and not Assembly.Location. The latter is the empty string in
    /// a single-file publish - the assembly is inside the .exe, so it has no path of its own - and
    /// what that produced was a published host that could not find its shim at all, while every
    /// build directly out of the tree worked. The compiler says so as IL3000; the publish that
    /// exposed it is the one this exists for.
    ///
    /// The checkout is found by walking up rather than by counting "..". A count is a fixed depth,
    /// and the depth is not fixed: a publish sits one deeper than a build, which is exactly the
    /// layout that would have been silently wrong here.
    /// </summary>
    private static IEnumerable<string> Candidates(string dll = "chiaki-shim.dll")
    {
        string here = AppContext.BaseDirectory;

        // Beside the executable first: that is where a published host carries it.
        yield return Path.Combine(here, dll);

        // ...then the native build tree of whatever checkout this came out of. Both spellings,
        // because compile.cmd's portable tree and a bare cmake build put it in different places.
        for (string? dir = here; dir is not null; dir = Path.GetDirectoryName(dir))
        {
            yield return Path.Combine(dir, "build", "chiaki-ng-Win", dll);
            yield return Path.Combine(dir, "build", "shim", dll);
        }
    }

    private static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? path)
    {
        // The shim and SDL both come out of the portable tree, and for the same reason: whatever
        // the runtime's own search would find on PATH is not this build's.
        string dll = name switch
        {
            Library => "chiaki-shim.dll",
            Sdl => "SDL2.dll",
            _ => "",
        };

        if (dll.Length == 0)
            return IntPtr.Zero;

        foreach (string candidate in Candidates(dll))
        {
            if (!File.Exists(candidate))
                continue;

            AllowDependenciesFrom(Path.GetDirectoryName(candidate));
            if (!NativeLibrary.TryLoad(candidate, out IntPtr handle))
                continue;
            if (name == Library)
                LoadedFrom = candidate;
            else
                SdlLoadedFrom = candidate;
            return handle;
        }

        // IntPtr.Zero would let the runtime fall back to its own search, which could find a
        // chiaki-shim.dll that is not this build's. Refusing by name says which build is missing.
        throw new DllNotFoundException(
            $"{dll} was not found. Looked in:{Environment.NewLine}  "
            + string.Join(Environment.NewLine + "  ", Candidates(dll))
            + $"{Environment.NewLine}Run compile.cmd, which builds it and copies it into the portable tree.");
    }

    /// <summary>Where SDL2 was loaded from, or null while it has never been loaded.</summary>
    public static string? SdlLoadedFrom { get; private set; }

    private static readonly HashSet<string> allowedDirs = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// PP117: puts a directory on the PROCESS's dll search path, not just on this load's.
    ///
    /// NativeLibrary.TryLoad of an absolute path uses LOAD_WITH_ALTERED_SEARCH_PATH, which covers
    /// the static imports of the module being loaded and nothing else. SDL2 resolves a dependency
    /// from inside its own initialisation instead, and that lookup runs through the process search
    /// order - so the altered path does not reach it and the load fails with ERROR_DLL_INIT_FAILED.
    ///
    /// Which is not what it looked like. Windows reports that failure as a MODAL DIALOG, and in a
    /// process with no visible window there is nothing to dismiss and nothing to see: the load
    /// simply never returns. Two days of "SDL hangs" is one error box nobody can click. So the
    /// error mode is set first, and permanently - a future dependency that goes missing has to
    /// fail rather than wait.
    /// </summary>
    private static void AllowDependenciesFrom(string? directory)
    {
        if (directory is null || !allowedDirs.Add(directory))
            return;

        // SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX | SEM_NOOPENFILEERRORBOX.
        SetErrorMode(0x0001 | 0x0002 | 0x8000);

        // LOAD_LIBRARY_SEARCH_DEFAULT_DIRS, which is what makes AddDllDirectory count.
        SetDefaultDllDirectories(0x00001000);
        AddDllDirectory(directory);
    }

    [DllImport("kernel32", SetLastError = true)]
    private static extern uint SetErrorMode(uint mode);

    [DllImport("kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetDefaultDllDirectories(uint directoryFlags);

    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr AddDllDirectory(string newDirectory);

    /// <summary>
    /// Checked before anything else is called, because the failure it prevents has no symptom: a
    /// DLL left by an older build exports every name this assembly imports, and the arguments
    /// land in the wrong places quietly.
    /// </summary>
    public static void CheckAbi()
    {
        uint actual = AbiVersion();
        if (actual != ExpectedAbi)
            throw new InvalidOperationException(
                $"chiaki-shim.dll reports ABI {actual}, this host was built against {ExpectedAbi}. "
                + $"Loaded from {LoadedFrom ?? "<unknown>"}.");
    }

    [DllImport(Library, EntryPoint = "chiaki_shim_abi_version", CallingConvention = CallingConvention.Cdecl)]
    public static extern uint AbiVersion();

    [DllImport(Library, EntryPoint = "chiaki_shim_error_string", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr ErrorStringPtr(int errorCode);

    /// <summary>
    /// chiaki_error_string, copied out of the static string the shim returns.
    ///
    /// Marshalled by hand rather than declared as a string return, because the default would
    /// have the runtime free a pointer this side does not own.
    /// </summary>
    public static string? ErrorString(int errorCode)
        => Marshal.PtrToStringUTF8(ErrorStringPtr(errorCode));

    [DllImport(Library, EntryPoint = "chiaki_shim_decoder_choice", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr DecoderChoicePtr(
        [MarshalAs(UnmanagedType.I1)] bool vulkanListed,
        [MarshalAs(UnmanagedType.I1)] bool cudaListed,
        [MarshalAs(UnmanagedType.I1)] bool d3d11vaListed,
        [MarshalAs(UnmanagedType.I1)] bool nvidiaCard,
        int renderer,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? requested);

    [DllImport(Library, EntryPoint = "chiaki_shim_decoder_choice_needs_vulkan_context",
        CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool DecoderChoiceNeedsVulkanContext(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? choice);

    /// <summary>
    /// The decoder the Qt client would choose for the same machine, answered by the same
    /// function it asks. PP77 extracted it so the branch holding the non-NVIDIA floor could be
    /// asserted; calling it from here is what keeps the port from re-deriving that decision in
    /// C# and getting a second, unasserted answer.
    /// </summary>
    public static string? DecoderChoice(
        bool vulkanListed, bool cudaListed, bool d3d11vaListed,
        bool nvidiaCard, ChiakiRenderer renderer, string? requested)
        => Marshal.PtrToStringUTF8(DecoderChoicePtr(
            vulkanListed, cudaListed, d3d11vaListed, nvidiaCard, (int)renderer, requested));
}
