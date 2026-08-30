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

    /// <summary>Where SDL declares the event union this port reads a queue of.</summary>
    public const string EventsHeaderRelativePath = @"mingw64\include\SDL2\SDL_events.h";

    /// <summary>
    /// PP579: the size SdlEventRaw promises, named so SDL's header can be asked about it.
    ///
    /// It was a literal 56 inside a StructLayout attribute, and Gamepads.cs says plainly what
    /// getting it wrong costs: "a queue read off by whole events rather than a compiler error".
    /// SDL asserts the size against its own union at compile time; nothing on this side did.
    ///
    /// ChiakiSession.cs refuses StructLayout on libchiaki's structs for exactly this reason - "a
    /// standing promise about MinGW's padding on every future libchiaki, kept by nothing and broken
    /// silently". The promise here is unavoidable, because the queue is read by value; what was
    /// avoidable was nobody keeping it.
    /// </summary>
    public const int EventSize = 56;

    /// <summary>SDL_events.h, or null where this toolchain is not installed.</summary>
    public static string? LocateEventsHeader() => Under(EventsHeaderRelativePath);

    /// <summary>
    /// PP579: whether SDL still says an event is <see cref="EventSize"/> bytes on a 64-bit pointer.
    ///
    /// The header does not write the number plainly - it is the first arm of a ternary on pointer
    /// size: <c>padding[sizeof(void *) &lt;= 8 ? 56 : ...]</c>. This port is x64 only, so that arm is
    /// the one it takes, and it is the arm this reads. A build for a wider pointer would need the
    /// second, which is a different question and one no non-goal here allows to arise.
    /// </summary>
    public static bool TheEventSizeIsStill(string eventsHeader, int size)
    {
        ArgumentNullException.ThrowIfNull(eventsHeader);

        // The padding declaration, and the arm taken when a pointer is eight bytes.
        return eventsHeader.Contains(
                   $"padding[sizeof(void *) <= 8 ? {size} :", StringComparison.Ordinal)
            && eventsHeader.Contains("SDL_COMPILE_TIME_ASSERT(SDL_Event,", StringComparison.Ordinal);
    }

    /// <summary>Either header, under whichever MSYS2 the build is using.</summary>
    private static string? Under(string relative)
    {
        string root = Environment.GetEnvironmentVariable("MSYS2_ROOT") is { Length: > 0 } set
            ? set
            : DefaultMsys2Root;

        string path = Path.Combine(root, relative);
        return File.Exists(path) ? path : null;
    }

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
