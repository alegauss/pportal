using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP221: where an axis is resting, which no event can say.
///
/// SDL raises an axis event when the value CHANGES, so a stick sitting off centre and perfectly
/// still is indistinguishable, in the event stream, from one sitting dead centre. Both are silent.
/// The reference below is SDL's own, out of the header, and it is what stops this report reading
/// as an accusation: a centred thumbstick is documented as within ~8000 of zero.
/// </summary>
public class RestingAxesTests
{
    /// <summary>The tolerance is the header's number, and it is a quarter of full scale.</summary>
    [Fact]
    public void TheToleranceIsTheHeadersOwn()
    {
        Assert.Equal(8000, RestingAxes.CentredWithin);
        Assert.Equal(0.244, RestingAxes.CentredWithin / AxisRanges.FullScale, 3);
    }

    /// <summary>A resting stick is not expected to read zero, and both signs count the same.</summary>
    [Theory]
    [InlineData((short)0, true)]
    [InlineData((short)7999, true)]
    [InlineData((short)8000, true)]
    [InlineData((short)-8000, true)]
    [InlineData((short)8001, false)]
    [InlineData((short)-8001, false)]
    [InlineData(short.MinValue, false)]
    public void CentredIsTheHeadersBand(short value, bool centred)
        => Assert.Equal(centred, RestingAxes.IsCentred(value));

    /// <summary>A stick's line says which side of that band it is on.</summary>
    [Fact]
    public void AStickLineNamesTheBand()
    {
        Assert.Contains("within the ~8000", RestingAxes.Line("left stick Y", 300, trigger: false), StringComparison.Ordinal);
        Assert.Contains("OUTSIDE the ~8000", RestingAxes.Line("left stick Y", -20000, trigger: false), StringComparison.Ordinal);
    }

    /// <summary>
    /// A trigger's does not, because through the controller call a trigger runs 0 to maximum -
    /// which is NOT the range the joystick layer reports for the same trigger, and the joystick
    /// layer is what the capture reads.
    /// </summary>
    [Fact]
    public void ATriggerHasNoBandToBeInside()
    {
        string line = RestingAxes.Line("L2", 0, trigger: true);

        Assert.Contains("0 is released", line, StringComparison.Ordinal);
        Assert.DoesNotContain("centred", line, StringComparison.Ordinal);
    }

    /// <summary>
    /// The six axes, in the order the header declares them. The enum starts at INVALID = -1, so
    /// the six that follow begin at zero - a port numbering them from the written line would read
    /// every axis one place along.
    /// </summary>
    [Fact]
    public void TheAxesAreNumberedFromZeroAfterInvalid()
    {
        Assert.Equal(0, Gamepads.ControllerAxis.LeftX);
        Assert.Equal(1, Gamepads.ControllerAxis.LeftY);
        Assert.Equal(5, Gamepads.ControllerAxis.TriggerRight);

        Assert.Equal(6, Gamepads.ControllerAxis.All.Count);
        Assert.Equal(
            [0, 1, 2, 3, 4, 5],
            Gamepads.ControllerAxis.All.Select(a => a.Axis));
    }

    /// <summary>And only the two triggers are triggers.</summary>
    [Fact]
    public void OnlyTheTriggersAreTriggers()
    {
        Assert.Equal(
            [Gamepads.ControllerAxis.TriggerLeft, Gamepads.ControllerAxis.TriggerRight],
            Gamepads.ControllerAxis.All.Where(a => Gamepads.ControllerAxis.IsTrigger(a.Axis)).Select(a => a.Axis));
    }

    /// <summary>Every rule above, still written that way in the header the build compiles against.</summary>
    [Fact]
    public void TheHeaderStillSaysThis()
    {
        string? path = SdlPadSource.LocateHeader();
        if (path is null)
            return;

        string header = File.ReadAllText(path);

        Assert.Contains(
            $"centered within ~{RestingAxes.CentredWithin} of zero",
            header,
            StringComparison.Ordinal);

        Assert.Contains(
            "Trigger axis values range from 0 (released)",
            header,
            StringComparison.Ordinal);

        Assert.Contains("SDL_CONTROLLER_AXIS_INVALID = -1", header, StringComparison.Ordinal);
    }
}
