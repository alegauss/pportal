using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP35: test/fec_test_cases.inl's sixty-four recorded erasure cases, one xUnit case each.
///
/// The suite already asserts these - <c>--selftest</c> runs all sixty-four and says "64 of 64".
/// What it cannot say is WHICH, and a single failing erasure pattern among sixty-four is exactly
/// the kind of thing that gets read as flakiness rather than as a bug. Here each carries its own
/// name: the unit count, the parity count, and the units that were lost.
///
/// No expected values live in this file. Every case is parsed out of the C at run time by
/// <see cref="FecVectors"/>, which is the same reader --selftest uses - one oracle, two runners.
/// Copying the cases in here would produce a second oracle that agrees with the first long after
/// either agrees with a console, which is the failure this whole harness exists to avoid.
/// </summary>
public class FecVectorTests
{
    /// <summary>
    /// The cases, or an empty sequence where there is no checkout to read them from.
    ///
    /// Empty rather than throwing, and <see cref="TheSuiteIsPresent"/> is what turns that into a
    /// failure. A Theory with no data is reported by xUnit as a warning that is easy to scroll
    /// past, so the presence of the vectors is asserted as its own Fact instead.
    /// </summary>
    public static TheoryData<int> CaseIndices()
    {
        var data = new TheoryData<int>();
        for (int i = 0; i < Cases.Count; i++)
            data.Add(i);
        return data;
    }

    private static IReadOnlyList<FecCase> Cases { get; } = Load();

    private static IReadOnlyList<FecCase> Load()
    {
        string? file = FecVectors.Locate();
        return file is null ? [] : FecVectors.Parse(file);
    }

    [Fact]
    public void TheSuiteIsPresent()
    {
        // Skipped rather than failed outside a checkout: an installed copy has no test/ to read,
        // and this project is only ever run from the tree. Inside one, sixty-four is the number.
        if (FecVectors.Locate() is null)
            return;

        Assert.Equal(64, Cases.Count);
    }

    /// <summary>
    /// Each case's buffer must hold k+m whole units before any of it is trusted. A buffer that
    /// did not would decode into the wrong places and still compare equal to itself.
    /// </summary>
    [Theory]
    [MemberData(nameof(CaseIndices))]
    public void TheCaseBufferHoldsWholeUnits(int index)
    {
        FecCase c = Cases[index];
        Assert.Equal(c.UnitSize * (int)(c.K + c.M), c.FrameBuffer.Length);
        Assert.NotEmpty(c.Erasures);
    }

    /// <summary>
    /// The recorded erasure is recovered byte for byte. This is the claim the C build already
    /// backed against a real console's stream, inherited rather than re-derived.
    /// </summary>
    [Theory]
    [MemberData(nameof(CaseIndices))]
    public void TheRecordedErasureIsRecovered(int index)
    {
        FecCase c = Cases[index];
        Assert.True(Fec.Recovers(c),
            $"k={c.K} m={c.M} unit={c.UnitSize} lost=[{string.Join(",", c.Erasures)}]");
    }

    /// <summary>
    /// And the negative that gives the rest of them meaning: blank one unit, tell the decoder a
    /// DIFFERENT one was lost, and the frame must not come back. Without this a decoder that
    /// returned its input untouched would pass all sixty-four above.
    /// </summary>
    [Theory]
    [MemberData(nameof(CaseIndices))]
    public void TheWrongDeclaredErasureDoesNotRecover(int index)
    {
        FecCase c = Cases[index];
        uint lied = (c.Erasures[0] + 1) % c.K;

        // Unless the lie names the same unit - possible where k is 1, and there the case has
        // nothing to say. Asserting on it would be asserting that a true statement is false.
        if (lied == c.Erasures[0])
            return;

        Assert.False(Fec.Recovers(c, [lied]),
            $"blanked {c.Erasures[0]}, declared {lied}, k={c.K}");
    }

    /// <summary>
    /// The stride the decoder addresses units at, which is a layout and not a convenience of the
    /// test: a rewrite that packed units tightly would decode the right bytes into wrong places.
    ///
    /// These five carry the whole of that claim, and measurably so. Rounding to 8 instead of 16
    /// leaves all sixty-four recorded cases green - every unit size they record is already a
    /// multiple of 16, so the two roundings agree on every one of them and the vectors cannot
    /// tell the difference. 1, 17 and 1400 are the sizes that can, and none of them is recorded.
    /// </summary>
    [Theory]
    [InlineData(1400, 1408)]
    [InlineData(1408, 1408)]
    [InlineData(1, 16)]
    [InlineData(16, 16)]
    [InlineData(17, 32)]
    public void TheStrideRoundsUpToSixteen(int unitSize, int expected)
        => Assert.Equal(expected, FecVectors.StrideFor(unitSize));
}
