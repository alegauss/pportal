using System.Text.RegularExpressions;

namespace ChiakiNg.Session;

/// <summary>
/// PP9: the facts the renderer decision rests on, read rather than remembered.
///
/// PP9 offered three shapes for getting a libplacebo frame under a WPF compositor, and all three
/// take it as given that libplacebo means Vulkan: host a Vulkan child HWND and lose the ability to
/// draw over the video, share a Vulkan image into D3D11 and pay the interop, or throw libplacebo
/// away and with it the shader work that is the reason the picture looks the way it does.
///
/// The libplacebo this tree links reports PL_HAVE_D3D11. So there is a fourth shape, and it is
/// the one that gives up least: run libplacebo ON D3D11. The renderer is the same renderer - the
/// shaders live above pl_gpu and do not know which backend is under them - and what changes is
/// the dozen or so entry points that name a backend, each of which has a D3D11 counterpart with
/// the same shape. Nothing has to be shared out of Vulkan, because nothing is in Vulkan.
///
/// It also moves the zero-copy decoder. PP77 records vulkan being preferred because "it is the
/// one decoder whose frame the renderer can take without a copy"; on a D3D11 renderer that
/// sentence is about d3d11va, whose frames are already ID3D11Texture2D and which pl_d3d11_wrap
/// takes directly. d3d11va is PP51's non-NVIDIA floor, so the common machine gets the good path.
///
/// What this class does NOT establish, and the design says so in the same words: none of it has
/// been built or run. The D3D11-to-D3D9Ex hop that D3DImage requires is a real cost and a real
/// format restriction, and libplacebo's D3D11 backend is less exercised upstream than its Vulkan
/// one. These are the checkable claims, checked; the rest is a decision.
/// </summary>
public static partial class PlaceboBackends
{
    /// <summary>The Qt window whose backend calls are being counted.</summary>
    public const string WindowRelativePath = @"gui\src\qmlmainwindow.cpp";

    /// <summary>Where the build's libplacebo headers are, under the MSYS2 root compile.cmd uses.</summary>
    public static string HeaderDirectory
        => Path.Combine(
            Environment.GetEnvironmentVariable("MSYS2_ROOT") ?? @"C:\msys64",
            "mingw64", "include", "libplacebo");

    /// <summary>qmlmainwindow.cpp, or null outside a checkout.</summary>
    public static string? LocateWindow() => SanitizerSource.LocateRelative(WindowRelativePath);

    /// <summary>
    /// One of libplacebo's headers, or null where the toolchain is not installed here. Null and
    /// not an exception: the .NET host is buildable without MSYS2 (PP74), and a check that cannot
    /// see the C toolchain has to say so rather than fail.
    /// </summary>
    public static string? LocateHeader(string name)
    {
        string path = Path.Combine(HeaderDirectory, name);
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// Whether libplacebo was compiled with a backend, as its own config.h reports it.
    ///
    /// The header and not the documentation: libplacebo's D3D11 backend is optional at build
    /// time, so "libplacebo has D3D11" is a fact about this installation and not about the
    /// project. A tree whose libplacebo was built without it cannot take the decision below.
    /// </summary>
    public static bool Compiled(string configText, string backend)
    {
        ArgumentNullException.ThrowIfNull(configText);
        ArgumentNullException.ThrowIfNull(backend);
        return Regex.IsMatch(configText, @"#define\s+PL_HAVE_" + Regex.Escape(backend) + @"\s+1");
    }

    /// <summary>
    /// The distinct pl_&lt;backend&gt;_* entry points a source names, without the backend prefix.
    ///
    /// Stripped so the two backends can be compared as sets. That comparison is the whole
    /// argument for the fourth shape: if every Vulkan entry point the window uses has a D3D11
    /// counterpart, the port is a substitution, and if one does not, it is a rewrite.
    /// </summary>
    public static IReadOnlySet<string> BackendCalls(string text, string backend)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(backend);

        return Regex.Matches(text, @"\bpl_" + Regex.Escape(backend) + @"_([a-z0-9_]+)")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Whether the D3D11 backend can adopt a texture the caller already has, and specifically a
    /// video one - which is what makes a d3d11va decode frame free rather than copied.
    ///
    /// Both halves are asserted because only the second is the interesting claim. Wrapping an
    /// ID3D11Resource says nothing on its own; the header naming NV12 and P010 as formats it can
    /// wrap is what says a decoder's output goes in without a conversion pass first.
    /// </summary>
    public static bool WrapsAVideoTexture(string d3d11Header)
    {
        ArgumentNullException.ThrowIfNull(d3d11Header);
        return d3d11Header.Contains("pl_d3d11_wrap", StringComparison.Ordinal)
            && WrapTextureRegex().IsMatch(d3d11Header)
            && d3d11Header.Contains("DXGI_FORMAT_NV12", StringComparison.Ordinal)
            && d3d11Header.Contains("DXGI_FORMAT_P010", StringComparison.Ordinal);
    }

    /// <summary>
    /// How many backend-agnostic renderer calls the window makes - the work that survives a
    /// backend change, and the work option C would have discarded.
    /// </summary>
    public static int RendererCalls(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return AgnosticRegex().Matches(text).Count;
    }

    [GeneratedRegex(@"ID3D11Resource\s*\*\s*tex\s*;")]
    private static partial Regex WrapTextureRegex();

    [GeneratedRegex(@"\bpl_(render_image|renderer_[a-z_]+|frame_[a-z_]+|tex_[a-z_]+|dispatch_[a-z_]+)\b")]
    private static partial Regex AgnosticRegex();
}
