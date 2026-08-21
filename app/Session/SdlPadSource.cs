namespace ChiakiNg.Session;

/// <summary>
/// PP218: the ownership rule for the two calls that look alike, read out of SDL's own header.
///
/// <see cref="Gamepads.NameForIndex"/> and <see cref="Gamepads.MappingForDeviceIndex"/> sit next to
/// each other in SDL_gamecontroller.h, take the same argument, and return the same thing as far as
/// managed code can see - an IntPtr, marshalled with PtrToStringUTF8. One of them SDL keeps and one
/// of them the caller must free, and getting it wrong leaks a string per pad per enumeration
/// without failing, without logging and without ever growing enough to become a symptom.
///
/// So it is held against the header rather than remembered. Unlike every other source check in this
/// port, the file is NOT in the checkout: it belongs to the toolchain compile.cmd builds against,
/// found the way compile.cmd finds it - MSYS2_ROOT, defaulting where that script defaults. A
/// machine without those headers gets null and the assertion is skipped, the same as running
/// outside a checkout.
/// </summary>
public static class SdlPadSource
{
    /// <summary>Where compile.cmd looks when the environment does not say.</summary>
    public const string DefaultMsys2Root = @"C:\msys64";

    /// <summary>The header, under whichever MSYS2 the build is using.</summary>
    public const string HeaderRelativePath = @"mingw64\include\SDL2\SDL_gamecontroller.h";

    /// <summary>The header's path, or null where this toolchain is not installed.</summary>
    public static string? LocateHeader()
    {
        string root = Environment.GetEnvironmentVariable("MSYS2_ROOT") is { Length: > 0 } set
            ? set
            : DefaultMsys2Root;

        string path = Path.Combine(root, HeaderRelativePath);
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// Whether the name is still SDL's to keep - a <c>const char *</c>, which is the half that must
    /// NOT be freed.
    /// </summary>
    public static bool TheNameIsStillOwnedBySdl(string header)
    {
        ArgumentNullException.ThrowIfNull(header);
        return header.Contains(
            "extern DECLSPEC const char *SDLCALL SDL_GameControllerNameForIndex(int joystick_index);",
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the mapping string is still the caller's - a bare <c>char *</c> whose own
    /// documentation names the call that releases it.
    /// </summary>
    public static bool TheMappingStringIsStillTheCallersToFree(string header)
    {
        ArgumentNullException.ThrowIfNull(header);

        int declared = header.IndexOf(
            "extern DECLSPEC char *SDLCALL SDL_GameControllerMappingForDeviceIndex(int joystick_index);",
            StringComparison.Ordinal);
        if (declared < 0)
            return false;

        // The sentence is in the comment ABOVE the declaration, so the slice looks backwards.
        int comment = header.LastIndexOf("/**", declared, StringComparison.Ordinal);
        if (comment < 0)
            return false;

        return header[comment..declared]
            .Contains("Must be freed with SDL_free()", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the two are still adjacent, which is the whole reason this check exists: a pair a
    /// reader meets together, with one difference that is invisible from the managed side.
    /// </summary>
    public static bool TheyAreStillNeighbours(string header)
    {
        ArgumentNullException.ThrowIfNull(header);

        int name = header.IndexOf("SDL_GameControllerNameForIndex(int", StringComparison.Ordinal);
        int mapping = header.IndexOf(
            "SDL_GameControllerMappingForDeviceIndex(int", StringComparison.Ordinal);

        return name >= 0 && mapping >= 0;
    }
}
