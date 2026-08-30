using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP579: the SDL_Event size this port promises, against the header SDL states it in.
/// </summary>
public class SdlEventSizeTests
{
    /// <summary>
    /// SDL STILL SAYS 56 FOR A 64-BIT POINTER, which is the size SdlEventRaw is laid out at.
    ///
    /// Gamepads.cs says what a wrong one costs: "a queue read off by whole events rather than a
    /// compiler error". SDL asserts the size against its own union at compile time and nothing on
    /// this side did - the number was a literal inside a StructLayout attribute.
    ///
    /// Skipped where MSYS2 is not installed, the way every other header check here is: a machine
    /// without the toolchain cannot answer, and refusing there would fail on the .NET-only path.
    /// </summary>
    [Fact]
    public void SdlStillSaysTheEventIsThatSize()
    {
        if (SdlPadSource.LocateEventsHeader() is not { } path)
            return;

        Assert.True(
            SdlPadSource.TheEventSizeIsStill(File.ReadAllText(path), SdlPadSource.EventSize),
            $"SDL_events.h no longer says {SdlPadSource.EventSize} for a 64-bit pointer");
    }

    /// <summary>
    /// And the check reads the arm it means. The header writes the size as a ternary on pointer
    /// size, so a check that just looked for "56" anywhere would be satisfied by the 64-bit arm of
    /// some other declaration, or by a comment.
    /// </summary>
    [Fact]
    public void TheCheckReadsThePointerSizeArm()
    {
        const string real = "Uint8 padding[sizeof(void *) <= 8 ? 56 : sizeof(void *) == 16 ? 64 : 0];\n"
            + "SDL_COMPILE_TIME_ASSERT(SDL_Event, sizeof(SDL_Event) == 56);";

        Assert.True(SdlPadSource.TheEventSizeIsStill(real, 56));
        Assert.False(SdlPadSource.TheEventSizeIsStill(real, 64));

        // A header that mentions the number but not in the padding declaration is not an answer.
        Assert.False(SdlPadSource.TheEventSizeIsStill("/* 56 bytes, honest */", 56));

        // Nor is the declaration without SDL's own assertion tying the union to it.
        Assert.False(SdlPadSource.TheEventSizeIsStill(
            "Uint8 padding[sizeof(void *) <= 8 ? 56 : 0];", 56));
    }

    /// <summary>The struct is laid out at the constant, not at a literal of its own.</summary>
    [Fact]
    public void TheLayoutUsesTheNamedSize()
    {
        Assert.Equal(56, SdlPadSource.EventSize);

        string? gamepads = SanitizerSource.LocateRelative(@"app\Session\Gamepads.cs");
        if (gamepads is null)
            return;

        string text = File.ReadAllText(gamepads);
        Assert.Contains("Size = SdlPadSource.EventSize", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Size = 56", text, StringComparison.Ordinal);
    }
}
