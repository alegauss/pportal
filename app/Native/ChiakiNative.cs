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

    /// <summary>Must equal CHIAKI_SHIM_ABI in shim/chiaki_shim.h.</summary>
    public const uint ExpectedAbi = 4;

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
    /// Where a shim built by this repository can be. Relative to the assembly, because the .NET
    /// host builds into app\bin\&lt;config&gt;\&lt;tfm&gt;\&lt;rid&gt; and the native build into build\ beside it.
    /// </summary>
    private static IEnumerable<string> Candidates()
    {
        string? here = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        if (here is null)
            yield break;

        // Beside the assembly first: that is where a published host will carry it.
        yield return Path.Combine(here, "chiaki-shim.dll");

        // ...then the repository, five levels up from app\bin\Debug\net10.0-windows\win-x64.
        string repo = Path.GetFullPath(Path.Combine(here, "..", "..", "..", "..", ".."));
        yield return Path.Combine(repo, "build", "chiaki-ng-Win", "chiaki-shim.dll");
        yield return Path.Combine(repo, "build", "shim", "chiaki-shim.dll");
    }

    private static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? path)
    {
        if (name != Library)
            return IntPtr.Zero;

        foreach (string candidate in Candidates())
        {
            if (!File.Exists(candidate))
                continue;
            if (!NativeLibrary.TryLoad(candidate, out IntPtr handle))
                continue;
            LoadedFrom = candidate;
            return handle;
        }

        // IntPtr.Zero would let the runtime fall back to its own search, which could find a
        // chiaki-shim.dll that is not this build's. Refusing by name says which build is missing.
        throw new DllNotFoundException(
            $"chiaki-shim.dll was not found. Looked in:{Environment.NewLine}  "
            + string.Join(Environment.NewLine + "  ", Candidates())
            + $"{Environment.NewLine}Run compile.cmd, which builds it and copies it into the portable tree.");
    }

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
