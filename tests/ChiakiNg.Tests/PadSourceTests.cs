using ChiakiNg.Native;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP701: the SDL-to-PlayStation mapping, held against the client that defines it.
///
/// Cross and circle swapped is a stream that works and cancels every confirmation, and it reads as
/// a console setting rather than a defect here. Nothing about that failure is loud, so the pairs
/// are read out of Controller::HandleButtonEvent rather than remembered.
/// </summary>
public class PadSourceTests
{
    /// <summary>The port's answer for every SDL button the client maps, by the client's own suffixes.</summary>
    private static readonly Dictionary<string, int> SdlButtons = new(StringComparer.Ordinal)
    {
        ["A"] = PadTranslation.PadButton.A,
        ["B"] = PadTranslation.PadButton.B,
        ["X"] = PadTranslation.PadButton.X,
        ["Y"] = PadTranslation.PadButton.Y,
        ["DPAD_LEFT"] = PadTranslation.PadButton.DpadLeft,
        ["DPAD_RIGHT"] = PadTranslation.PadButton.DpadRight,
        ["DPAD_UP"] = PadTranslation.PadButton.DpadUp,
        ["DPAD_DOWN"] = PadTranslation.PadButton.DpadDown,
        ["LEFTSHOULDER"] = PadTranslation.PadButton.LeftShoulder,
        ["RIGHTSHOULDER"] = PadTranslation.PadButton.RightShoulder,
        ["LEFTSTICK"] = PadTranslation.PadButton.LeftStick,
        ["RIGHTSTICK"] = PadTranslation.PadButton.RightStick,
        ["START"] = PadTranslation.PadButton.Start,
        ["BACK"] = PadTranslation.PadButton.Back,
        ["GUIDE"] = PadTranslation.PadButton.Guide,
        ["TOUCHPAD"] = PadTranslation.PadButton.Touchpad,
    };

    /// <summary>What the client calls each PlayStation button, against this port's enum.</summary>
    private static readonly Dictionary<string, ChiakiControllerButton> ChiakiButtons =
        new(StringComparer.Ordinal)
        {
            ["CROSS"] = ChiakiControllerButton.Cross,
            ["MOON"] = ChiakiControllerButton.Moon,
            ["BOX"] = ChiakiControllerButton.Box,
            ["PYRAMID"] = ChiakiControllerButton.Pyramid,
            ["DPAD_LEFT"] = ChiakiControllerButton.DpadLeft,
            ["DPAD_RIGHT"] = ChiakiControllerButton.DpadRight,
            ["DPAD_UP"] = ChiakiControllerButton.DpadUp,
            ["DPAD_DOWN"] = ChiakiControllerButton.DpadDown,
            ["L1"] = ChiakiControllerButton.L1,
            ["R1"] = ChiakiControllerButton.R1,
            ["L3"] = ChiakiControllerButton.L3,
            ["R3"] = ChiakiControllerButton.R3,
            ["OPTIONS"] = ChiakiControllerButton.Options,
            ["SHARE"] = ChiakiControllerButton.Share,
            ["PS"] = ChiakiControllerButton.Ps,
            ["TOUCHPAD"] = ChiakiControllerButton.Touchpad,
        };

    /// <summary>THE CHECK: every pair the client states, this port answers the same way.</summary>
    [Fact]
    public void EveryPairTheClientStatesIsThePairThisPortSends()
    {
        if (PadSource.Locate() is not { } path)
            return;

        IReadOnlyDictionary<string, string> mapped = PadSource.MappedIn(File.ReadAllText(path));

        Assert.NotEmpty(mapped);

        var wrong = new List<string>();
        foreach ((string sdl, string chiaki) in mapped)
        {
            // A suffix neither side knows is a case the client added; it is reported rather than
            // skipped, because an unmapped button on this side is exactly the defect being looked
            // for and silence about it would be the wrong answer.
            if (!SdlButtons.TryGetValue(sdl, out int index)
                || !ChiakiButtons.TryGetValue(chiaki, out ChiakiControllerButton expected))
            {
                wrong.Add($"SDL_CONTROLLER_BUTTON_{sdl} -> CHIAKI_CONTROLLER_BUTTON_{chiaki}: unknown here");
                continue;
            }

            ChiakiControllerButton got = PadTranslation.ButtonFor(index);
            if (got != expected)
                wrong.Add($"SDL_CONTROLLER_BUTTON_{sdl}: the client says {chiaki}, this port sends {got}");
        }

        Assert.True(wrong.Count == 0, string.Join("\n  ", wrong));
    }

    /// <summary>
    /// And the triggers are the client's two pressures, scaled the client's way.
    ///
    /// The FIELD is the half that matters: writing the L2 and R2 bits instead would send one pull
    /// as two things, and the mapping above would still pass.
    /// </summary>
    [Fact]
    public void TheTriggersAreThePressuresAndAreScaledTheSameWay()
    {
        if (PadSource.Locate() is not { } path)
            return;

        string source = File.ReadAllText(path);
        IReadOnlyDictionary<string, string> axes = PadSource.AxesIn(source);

        Assert.Equal("l2_state", axes["TRIGGERLEFT"]);
        Assert.Equal("r2_state", axes["TRIGGERRIGHT"]);
        Assert.Equal("left_x", axes["LEFTX"]);
        Assert.Equal("left_y", axes["LEFTY"]);
        Assert.Equal("right_x", axes["RIGHTX"]);
        Assert.Equal("right_y", axes["RIGHTY"]);

        Assert.True(
            PadSource.TriggersShiftBySeven(source),
            "the client no longer scales a trigger by shifting seven, so PadTranslation.Pressure "
                + "is measuring something else");
    }

    /// <summary>
    /// The reader finds cases, so a client that moved them cannot pass by returning nothing.
    ///
    /// PP271's rule. An empty read satisfies every comparison above.
    /// </summary>
    [Fact]
    public void TheReaderFindsTheClientsCases()
    {
        Assert.Empty(PadSource.MappedIn("int main(void) { return 0; }"));

        if (PadSource.Locate() is not { } path)
            return;

        string source = File.ReadAllText(path);

        Assert.True(PadSource.MappedIn(source).Count >= 16);
        Assert.True(PadSource.AxesIn(source).Count >= 6);
    }
}
