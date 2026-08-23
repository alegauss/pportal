using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP264: two null disciplines and an unstated length.
///
/// <see cref="TheLengthIsOmittedWhereItMattersMost"/> carries the task: the same header states a
/// buffer requirement for the smaller copy and omits it for the larger.
/// </summary>
public class HolepunchAccessorsTests
{
    /// <summary>
    /// THE CONTRAST. Forty-six bytes with no stated size, beside sixteen with one.
    /// </summary>
    [Fact]
    public void TheLengthIsOmittedWhereItMattersMost()
    {
        Assert.Equal(46, HolepunchAccessors.BytesWritten);
        Assert.Null(HolepunchAccessors.StatedLength);

        // The one the file does document is under a third the size.
        Assert.Equal(16, HolepunchAccessors.StatedElsewhere);
        Assert.True(HolepunchAccessors.StatedElsewhere * 2 < HolepunchAccessors.BytesWritten);
    }

    /// <summary>A caller sized for one family is written past its end, by a stated amount.</summary>
    [Fact]
    public void ACallerSizedForOneFamilyIsOverrun()
    {
        Assert.Equal(30, HolepunchAccessors.OverrunFor(16));
        Assert.Equal(0, HolepunchAccessors.OverrunFor(46));
        Assert.Equal(0, HolepunchAccessors.OverrunFor(64));
    }

    /// <summary>One of six guards anything, and the other five are next to it.</summary>
    [Fact]
    public void OneOfSixGuardsAnything()
    {
        Assert.Equal(6, HolepunchAccessors.All.Count);
        Assert.Equal(1, HolepunchAccessors.GuardCount);

        Accessor guarded = HolepunchAccessors.All.Single(a => a.ChecksNull);
        Assert.Equal("chiaki_holepunch_session_get_stun_allocation", guarded.Name);
    }

    /// <summary>
    /// And none of them states a length.
    ///
    /// PP316: the predicate stays with the assertion rather than being filtered away first. An
    /// <c>Assert.Empty</c> over a <c>Where</c> reports "the collection was not empty" and never
    /// names the accessor that broke it, which is the whole of what a red run has to say.
    /// </summary>
    [Fact]
    public void NoneOfThemStatesALength()
        => Assert.DoesNotContain(HolepunchAccessors.All, a => a.StatesALength);

    /// <summary>The socket getter hands back the session's own handle, not a copy.</summary>
    [Fact]
    public void TheSocketGetterHandsBackTheSessionsOwn()
    {
        Assert.True(HolepunchAccessors.ReturnsTheSessionsOwnHandle);
        Assert.Equal([PunchPort.Control, PunchPort.Data], HolepunchAccessors.AnsweredTypes);
    }

    /// <summary>
    /// Checked and not a defect: four fields copied, four declared - nothing left unset.
    /// </summary>
    [Fact]
    public void TheRegistInfoLeavesNothingUnset()
        => Assert.Equal(HolepunchAccessors.RegistFieldsDeclared, HolepunchAccessors.RegistFields.Count);

    /// <summary>Every rule above, still written the same way in the core it was read from.</summary>
    [Fact]
    public void TheSurfaceIsStillTheCores()
    {
        string? file = HolepunchAccessorsSource.Locate();
        string? headerFile = HolepunchAccessorsSource.LocateHeader();
        if (file is null || headerFile is null)
            return;

        string core = File.ReadAllText(file);
        string header = File.ReadAllText(headerFile);

        Assert.True(
            HolepunchAccessorsSource.TheGetterStillCopiesTheWholeField(core),
            "the address getter still copies the source's whole field");
        Assert.True(
            HolepunchAccessorsSource.TheHeaderStillOmitsTheLength(header),
            "and the header still says nothing about how big the caller's must be");
        Assert.True(
            HolepunchAccessorsSource.TheOtherLengthIsStillStated(core),
            "while the other buffer's requirement is still stated");

        Assert.Equal(
            HolepunchAccessors.GuardCount, HolepunchAccessorsSource.HowManyStillGuard(core));

        Assert.True(
            HolepunchAccessorsSource.TheReleaserStillDereferencesUnguarded(core),
            "the releaser still dereferences its argument unguarded");
        Assert.True(
            HolepunchAccessorsSource.TheSocketGetterStillReturnsTheSessionsOwn(core),
            "the socket getter still returns the session's own handle");

        Assert.True(
            HolepunchAccessorsSource.TheRegistInfoStillCopiesEveryField(core, header),
            "and the registration info still copies every field the struct declares");
    }
}
