using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP228: the absence test that could only pass.
///
/// SdlPad is a readonly record struct, and FirstOrDefault over a sequence of those returns the ZERO
/// VALUE rather than nothing. Assigned to an SdlPad? that value is not null, so the guard beneath
/// it never runs and what reaches the parser is a default whose Mapping is null - which is where
/// the crash finally appeared, three statements from the cause and wearing somebody else's name.
///
/// These assert the language behaviour that makes the pattern wrong, because the pattern READS
/// correctly: the same three lines over a class do exactly what they say.
/// </summary>
public class PadAbsenceTests
{
    /// <summary>
    /// The whole defect in one assertion: nothing came back, and it is not null.
    /// </summary>
    [Fact]
    public void FirstOrDefaultOverAStructIsNotAnAbsenceTest()
    {
        IReadOnlyList<SdlPad> none = [];

        SdlPad? found = none.FirstOrDefault();

        // The line that reads like a guard and cannot fire.
        Assert.True(found is SdlPad);
        Assert.NotNull(found);
    }

    /// <summary>And what that zero value carries is what killed the run.</summary>
    [Fact]
    public void TheZeroValueCarriesANullMapping()
    {
        IReadOnlyList<SdlPad> none = [];
        SdlPad empty = none.FirstOrDefault();

        Assert.Null(empty.Mapping);
        Assert.Null(empty.Name);
        Assert.Equal(0, empty.Index);

        // Which the parser refuses, at a distance from anything that mentions a pad.
        Assert.Throws<ArgumentNullException>(
            () => ControllerMappingDocument.Parse(empty.Mapping!, "Pad"));
    }

    /// <summary>
    /// The count is the only thing that answers, and it answers the same for a struct as it would
    /// for anything else.
    /// </summary>
    [Fact]
    public void TheCountIsWhatAnswers()
    {
        IReadOnlyList<SdlPad> none = [];
        IReadOnlyList<SdlPad> one = [new SdlPad(0, "DualSense", ScreenCapture.SampleDualSense)];

        Assert.Empty(none);
        Assert.Single(one);
        Assert.Equal("DualSense", one[0].Name);
    }

    /// <summary>
    /// And the contrast that shows why it reads correctly: over a reference type the same call is
    /// an absence test, which is the habit the struct quietly breaks.
    /// </summary>
    [Fact]
    public void OverAReferenceTypeTheSameCallWouldHaveWorked()
    {
        IReadOnlyList<string> none = [];

        Assert.Null(none.FirstOrDefault());
    }
}
