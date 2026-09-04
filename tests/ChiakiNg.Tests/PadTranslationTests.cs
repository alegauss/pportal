using ChiakiNg.Native;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP701: the pad's events folded into the state a console is sent.
///
/// The mapping is a fact about a wire, so <see cref="PadSourceTests"/> holds it against the client
/// that states it. What is here is the folding: a mask that latches, a trigger that scales, and a
/// change signal a caller sends on.
/// </summary>
public class PadTranslationTests
{
    private static SdlEvent Button(int which, bool down) => new(
        down ? Gamepads.EventType.ControllerButtonDown : Gamepads.EventType.ControllerButtonUp,
        Timestamp: 0, Which: 0, Index: (byte)which, Value: 0);

    private static SdlEvent Axis(int axis, short value) => new(
        Gamepads.EventType.ControllerAxisMotion,
        Timestamp: 0, Which: 0, Index: (byte)axis, Value: 0, AxisValue: value);

    /// <summary>A press sets its bit and a release clears it, and nothing else moves.</summary>
    [Fact]
    public void APressLatchesAndAReleaseClears()
    {
        var pad = new PadTranslation();

        Assert.True(pad.Offer(Button(PadTranslation.PadButton.A, down: true)));
        Assert.Equal(ChiakiControllerButton.Cross, pad.Buttons);

        Assert.True(pad.Offer(Button(PadTranslation.PadButton.B, down: true)));
        Assert.Equal(ChiakiControllerButton.Cross | ChiakiControllerButton.Moon, pad.Buttons);

        Assert.True(pad.Offer(Button(PadTranslation.PadButton.A, down: false)));
        Assert.Equal(ChiakiControllerButton.Moon, pad.Buttons);
    }

    /// <summary>
    /// A repeat says nothing changed, which is what a caller sends on.
    ///
    /// libchiaki's feedback sender reads whatever state it was last handed, so a translation that
    /// reported every event would push the same state sixty times a second.
    /// </summary>
    [Fact]
    public void ARepeatOfWhatIsAlreadyHeldIsNotAChange()
    {
        var pad = new PadTranslation();

        Assert.True(pad.Offer(Button(PadTranslation.PadButton.A, down: true)));
        Assert.False(pad.Offer(Button(PadTranslation.PadButton.A, down: true)));
        Assert.False(pad.Offer(Axis(Gamepads.ControllerAxis.LeftX, 0)));
    }

    /// <summary>
    /// A button this path does not send changes nothing and says so.
    ///
    /// The paddles and MISC1 belong to features that are not joined yet, and the client returns
    /// false for each; a translation that folded them into None would clear every held button.
    /// </summary>
    [Fact]
    public void AButtonWithNoPlaystationEquivalentIsNotAChange()
    {
        var pad = new PadTranslation();

        pad.Offer(Button(PadTranslation.PadButton.A, down: true));

        Assert.False(pad.Offer(Button(PadTranslation.PadButton.Paddle1, down: true)));
        Assert.False(pad.Offer(Button(PadTranslation.PadButton.Misc1, down: true)));
        Assert.Equal(ChiakiControllerButton.Cross, pad.Buttons);
    }

    /// <summary>SDL's 0..32767 becomes the wire's 0..255, full pull included.</summary>
    [Fact]
    public void ATriggerScalesToOneByte()
    {
        Assert.Equal(0, PadTranslation.Pressure(0));
        Assert.Equal(255, PadTranslation.Pressure(32767));
        Assert.Equal(128, PadTranslation.Pressure(16384));

        // A trigger reading below rest is not a negative pressure. The shift on a signed short
        // would carry the sign and 0xff would mean fully pulled.
        Assert.Equal(0, PadTranslation.Pressure(-1));
    }

    /// <summary>
    /// The triggers write the PRESSURES and never the bits of the same name.
    ///
    /// L2 and R2 exist in the mask for keyboard bindings - streamsession.cpp sets l2_state to 0xff
    /// from a key - and a pad that set both would report one pull as two things.
    /// </summary>
    [Fact]
    public void ATriggerSetsNoButtonBit()
    {
        var pad = new PadTranslation();
        using var state = new ChiakiControllerState();

        Assert.True(pad.Offer(Axis(Gamepads.ControllerAxis.TriggerLeft, 32767)));
        Assert.True(pad.Offer(Axis(Gamepads.ControllerAxis.TriggerRight, 16384)));
        pad.WriteTo(state);

        Assert.Equal(ChiakiControllerButton.None, state.Buttons);
        Assert.Equal(((byte)255, (byte)128), state.Triggers);
    }

    /// <summary>Both sticks reach the state signed, which is what a leftward push is.</summary>
    [Fact]
    public void TheSticksReachTheStateSigned()
    {
        var pad = new PadTranslation();
        using var state = new ChiakiControllerState();

        pad.Offer(Axis(Gamepads.ControllerAxis.LeftX, -32768));
        pad.Offer(Axis(Gamepads.ControllerAxis.LeftY, 12345));
        pad.Offer(Axis(Gamepads.ControllerAxis.RightX, 32767));
        pad.Offer(Axis(Gamepads.ControllerAxis.RightY, -1));
        pad.WriteTo(state);

        Assert.Equal(((short)-32768, (short)12345, (short)32767, (short)-1), state.Sticks);
    }

    /// <summary>An event that is not a pad's does nothing at all.</summary>
    [Fact]
    public void ADeviceEventIsNotInput()
    {
        var pad = new PadTranslation();

        Assert.False(pad.Offer(new SdlEvent(
            Gamepads.EventType.ControllerDeviceAdded, Timestamp: 0, Which: 0, Index: 0, Value: 0)));
    }
}
