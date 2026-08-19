using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP126: what the mapping screen records when the user presses something.
///
/// The tokens are a wire format between two clients sharing one settings store, so the formats
/// are held against controllermanager.cpp's own; the three rules around them are asserted here,
/// because each is a decision that a rewrite drops by simplification rather than by disagreement.
///
/// No pad required, and that is not a compromise: the capture is a function from an event to a
/// token, and PP118 established that SDL rewrites `which` on device events but leaves the payload
/// of an event alone. What a real pad would add is whether SDL emits these events at all, which
/// is SDL's business rather than the port's.
/// </summary>
public class MappingCaptureTests
{
    private static SdlEvent Button(byte index) => new(Gamepads.EventType.JoyButtonDown, 0, 0, index, 1);
    private static SdlEvent ButtonUp(byte index) => new(Gamepads.EventType.JoyButtonUp, 0, 0, index, 0);
    private static SdlEvent Axis(byte index) => new(Gamepads.EventType.JoyAxisMotion, 0, 0, index, 0);
    private static SdlEvent Hat(byte index, byte value) => new(Gamepads.EventType.JoyHatMotion, 0, 0, index, value);

    [Fact]
    public void NothingIsCapturedUntilTheScreenArmsOne()
    {
        var capture = new MappingCapture();

        Assert.False(capture.IsArmed);
        Assert.Null(capture.Offer(Button(3)));
    }

    [Fact]
    public void AButtonPressBecomesItsToken()
    {
        var capture = new MappingCapture();
        capture.Arm();

        Assert.Equal("b3", capture.Offer(Button(3)));
    }

    /// <summary>
    /// A release captures nothing. controllermanager.cpp returns on SDL_JOYBUTTONUP without
    /// looking, so the press takes the binding and the release that always follows it belongs to
    /// nobody - a rewrite folding the two cases together captures twice per press.
    /// </summary>
    [Fact]
    public void AButtonReleaseCapturesNothingAndLeavesTheCaptureArmed()
    {
        var capture = new MappingCapture();
        capture.Arm();

        Assert.Null(capture.Offer(ButtonUp(3)));
        Assert.True(capture.IsArmed);
    }

    /// <summary>
    /// An axis is ignored unless analog mapping was asked for. Most sticks rest slightly off
    /// centre, so without this the binding is taken by drift before the user touches anything.
    /// </summary>
    [Fact]
    public void AnAxisIsIgnoredUnlessAnalogMappingWasAskedFor()
    {
        var capture = new MappingCapture();
        capture.Arm();

        Assert.Null(capture.Offer(Axis(0)));
        Assert.True(capture.IsArmed);

        capture.AllowAnalogStick = true;
        Assert.Equal("a0", capture.Offer(Axis(0)));
    }

    /// <summary>A hat carries its direction as well as its index, and the two are separated by a dot.</summary>
    [Theory]
    [InlineData(0, 1, "h0.1")]
    [InlineData(0, 8, "h0.8")]
    [InlineData(1, 4, "h1.4")]
    public void AHatCarriesItsDirection(byte hat, byte value, string expected)
    {
        var capture = new MappingCapture();
        capture.Arm();

        Assert.Equal(expected, capture.Offer(Hat(hat, value)));
    }

    /// <summary>
    /// One arming captures one control. Events keep arriving while a finger is still on the pad,
    /// and without this the last of them overwrites what the user meant to bind.
    /// </summary>
    [Fact]
    public void OneArmingCapturesOneControl()
    {
        var capture = new MappingCapture();
        capture.Arm();

        Assert.Equal("b3", capture.Offer(Button(3)));
        Assert.False(capture.IsArmed);
        Assert.Null(capture.Offer(Button(4)));
    }

    /// <summary>The formats are the Qt client's, read from its source rather than remembered.</summary>
    [Fact]
    public void TheTokenFormatsAreTheQtClientsOwn()
    {
        string? file = MappingSource.Locate();
        if (file is null)
            return;

        string text = File.ReadAllText(file);

        Assert.Equal<string[]>(["a%1", "b%1", "h%1.%2"],
            [.. MappingSource.TokenFormats(text).Order(StringComparer.Ordinal)]);
    }

    /// <summary>
    /// And so are the two rules that are guards rather than formats. If either stops being true
    /// in the Qt client, the two clients have started capturing differently.
    /// </summary>
    [Fact]
    public void TheTwoGuardsAreStillInTheQtClient()
    {
        string? file = MappingSource.Locate();
        if (file is null)
            return;

        string text = File.ReadAllText(file);

        Assert.True(MappingSource.AxisIsBehindTheAnalogOptIn(text), "the analog opt-in guard");
        Assert.True(MappingSource.ButtonUpCapturesNothing(text), "the button-up case");
    }
}
