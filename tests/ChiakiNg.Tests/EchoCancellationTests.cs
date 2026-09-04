using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP52: the vendor path is not on the machine it was proposed for, and the in-box one is.
///
/// PP52 called NVIDIA's audio effects SDK "the first card in this port's audio". PP647's contract
/// binds a vendor path to an absence a user cannot see and PP648 found that a call which succeeds
/// is not a feature that ran - and both bind a path that exists.
///
/// spike/audio-effects asked whether it does. On an RTX 4060 with a current driver and the vendor's
/// own app installed it is not reachable at all: the SDK is not a driver feature but a
/// redistributable this port would ship. Windows's own Voice Capture DSP is registered and ships
/// nothing.
///
/// READ FROM THE SPIKE'S FILE, not transcribed. PP666's lesson applies hardest to a measurement: a
/// table copied out of one carries its authority and none of its checking.
/// </summary>
public class EchoCancellationTests(ITestOutputHelper output)
{
    /// <summary>
    /// THE CRITERION: both paths were read, and the reading says which is reachable.
    ///
    /// Two rows, one of each kind, and the finding is which way round they are.
    /// </summary>
    [Fact]
    public void TheVendorPathIsAbsentAndTheInBoxPathIsPresent()
    {
        if (EchoCancellation.RecordedPaths() is not { } paths)
            return;

        Assert.Equal(2, paths.Count);

        foreach (EffectPath path in paths)
        {
            output.WriteLine(
                $"{path.Name}: vendor {path.Vendor}, reachable {path.Reachable}, "
                    + $"ships {(EchoCancellation.ShipsSomething(path) ? path.Redistributable : "nothing")}");
        }

        EffectPath vendor = Assert.Single(paths, one => one.Vendor);
        EffectPath inBox = Assert.Single(paths, one => !one.Vendor);

        Assert.False(vendor.Reachable, "the vendor path is reachable now, which changes PP52's answer");
        Assert.True(inBox.Reachable, "the in-box transform is no longer registered, which changes it too");
    }

    /// <summary>
    /// And the cost axis is the one the answer turns on: one ships something and the other does not.
    ///
    /// This is a different question from which is a vendor path. A vendor path that arrived with
    /// the driver would ship nothing, and that is exactly what this one was assumed to be.
    /// </summary>
    [Fact]
    public void OnlyTheVendorPathCostsThePackageAnything()
    {
        if (EchoCancellation.RecordedPaths() is not { } paths)
            return;

        EffectPath vendor = Assert.Single(paths, one => one.Vendor);
        EffectPath inBox = Assert.Single(paths, one => !one.Vendor);

        Assert.True(EchoCancellation.ShipsSomething(vendor));
        Assert.False(EchoCancellation.ShipsSomething(inBox));
    }

    /// <summary>
    /// The reading was taken on a machine that HAS the card, which is what makes the absence mean
    /// something.
    ///
    /// A no on a machine with no NVIDIA adapter would say nothing at all about the SDK.
    /// </summary>
    [Fact]
    public void TheReadingWasTakenOnAMachineWithTheCard()
    {
        IReadOnlyList<string> adapters = EchoCancellation.RecordedAdapters();
        if (adapters.Count == 0)
            return;

        output.WriteLine("adapters: " + string.Join(", ", adapters));

        Assert.Contains(adapters, one => one.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Every row carries its evidence, so a no is refutable rather than asserted.
    ///
    /// PP648's rule one step earlier: a call that succeeds is not a feature that ran, and a probe
    /// that reports absence without saying what it looked at is not a reading either.
    /// </summary>
    [Fact]
    public void EveryRowSaysWhatWasLookedAt()
    {
        if (EchoCancellation.RecordedPaths() is not { } paths)
            return;

        Assert.All(paths, one => Assert.False(string.IsNullOrWhiteSpace(one.Evidence)));

        // The in-box row's evidence names the class id, which is the thing "registered" means.
        EffectPath inBox = Assert.Single(paths, one => !one.Vendor);
        Assert.Contains(EchoCancellation.VoiceCaptureDspClsid, inBox.Evidence, StringComparison.OrdinalIgnoreCase);

        // And the vendor row's names the variable that would have been set.
        EffectPath vendor = Assert.Single(paths, one => one.Vendor);
        Assert.Contains("NVAFX_SDK_DIR", vendor.Evidence, StringComparison.Ordinal);
    }

    /// <summary>
    /// THE ARCHITECTURE: filter mode takes two inputs, so PP652's capture stays.
    ///
    /// The second input is a reference of what is being played, which is how acoustic echo
    /// cancellation works at all. Source mode declares no inputs because the DSP opens the devices
    /// itself - which would replace WasapiCapture and take the device choice with it.
    ///
    /// Both are accepted here, so this is a choice rather than a constraint, and the counts are
    /// what make it readable.
    /// </summary>
    [Fact]
    public void FilterModeTakesTwoInputsAndKeepsTheCaptureThePortOwns()
    {
        if (EchoCancellation.RecordedDsp() is not { } dsp)
            return;

        output.WriteLine($"{dsp.Inputs} in, {dsp.Outputs} out - {dsp.Note}");

        Assert.True(dsp.Created);
        Assert.True(dsp.TakesFilterMode);
        Assert.Equal(2, dsp.Inputs);
        Assert.Equal(1, dsp.Outputs);

        // And the note carries BOTH modes' counts, because one of them is the road not taken and a
        // reader deciding later needs the number rather than the conclusion.
        Assert.Contains("filter:", dsp.Note, StringComparison.Ordinal);
        Assert.Contains("source:", dsp.Note, StringComparison.Ordinal);
    }

    /// <summary>
    /// The count was read AFTER the mode was set, which the note is the evidence for.
    ///
    /// An unconfigured DSP reports a shape it has not been told to take, and this spike's first
    /// reading did exactly that: "0 in, 1 out", which is neither mode. A filter row of two inputs
    /// is what says the mode was set first.
    /// </summary>
    [Fact]
    public void TheShapeIsTheConfiguredOneAndNotTheDefault()
    {
        if (EchoCancellation.RecordedDsp() is not { } dsp)
            return;

        Assert.DoesNotContain("filter: 0 in", dsp.Note, StringComparison.Ordinal);
        Assert.Contains("filter: 2 in", dsp.Note, StringComparison.Ordinal);
    }

    /// <summary>
    /// The two non-goals that bound this line are named, which is what the roadmap's lint asked for.
    ///
    /// A constraint may bound a line without forbidding it, and quoting the lead is how the answer
    /// is recorded rather than left to a reader to work out.
    /// </summary>
    [Fact]
    public void TheTwoBoundingNonGoalsAreQuotedByLead()
    {
        if (BacklogRequirements.LocateRoadmap() is not { } path)
            return;

        string roadmap = File.ReadAllText(path);

        Assert.All(
            EchoCancellation.Bounding,
            lead => Assert.Contains(lead, roadmap, StringComparison.Ordinal));
    }
}
