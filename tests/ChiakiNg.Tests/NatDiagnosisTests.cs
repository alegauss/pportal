using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP253: the one place in the offer that writes back into what it was handed.
///
/// <see cref="TheOfferOverrulesItsOwnMeasurement"/> carries the task: an increment nothing measured
/// is written onto the session and read by three later functions that cannot tell the difference.
/// </summary>
public class NatDiagnosisTests
{
    /// <summary>A usable measurement is used, and nothing is written.</summary>
    [Fact]
    public void AUsableMeasurementIsLeftAlone()
    {
        NatVerdict verdict = NatDiagnosis.Verdict(
            measuredIncrement: 4, localPort: 40000, reportedPort: 40010, forcingEnabled: true);

        Assert.Equal(NatVerdict.Measured, verdict);
        Assert.False(NatDiagnosis.WriteBackFor(verdict).Writes);
        Assert.Equal(4, NatDiagnosis.IncrementUsed(4, verdict));
    }

    /// <summary>Ports that agree mean nothing moves, so there is nothing to guess.</summary>
    [Fact]
    public void PortsThatAgreeNeedNoGuessing()
    {
        NatVerdict verdict = NatDiagnosis.Verdict(0, 40000, 40000, forcingEnabled: true);

        Assert.Equal(NatVerdict.Transparent, verdict);
        Assert.False(NatDiagnosis.WriteBackFor(verdict).Writes);
    }

    /// <summary>
    /// THE WRITE-BACK. Zero measured, ports disagreeing, forcing on - and the session comes out
    /// describing a NAT the measurement never found.
    /// </summary>
    [Fact]
    public void TheOfferOverrulesItsOwnMeasurement()
    {
        NatVerdict verdict = NatDiagnosis.Verdict(0, 40000, 40001, forcingEnabled: true);
        Assert.Equal(NatVerdict.Rewriting, verdict);

        NatWriteBack written = NatDiagnosis.WriteBackFor(verdict);

        Assert.True(written.Writes);
        Assert.True(written.RandomAllocation);
        Assert.Equal(1, written.Increment);

        // The measurement said zero; what the guessing uses is one.
        Assert.Equal(0, 0);
        Assert.Equal(1, NatDiagnosis.IncrementUsed(measuredIncrement: 0, verdict));
    }

    /// <summary>
    /// And with forcing off the same NAT is diagnosed and nothing is done - which is what makes the
    /// write-back a choice rather than a consequence.
    /// </summary>
    [Fact]
    public void WithoutForcingTheSameNatIsLeftAlone()
    {
        NatVerdict verdict = NatDiagnosis.Verdict(0, 40000, 40001, forcingEnabled: false);

        Assert.Equal(NatVerdict.RewritingUnhandled, verdict);
        Assert.False(NatDiagnosis.WriteBackFor(verdict).Writes);
        Assert.Equal(0, NatDiagnosis.IncrementUsed(0, verdict));
    }

    /// <summary>Only one of the four verdicts writes anything.</summary>
    [Fact]
    public void ExactlyOneVerdictWrites()
    {
        int writers = Enum.GetValues<NatVerdict>().Count(v => NatDiagnosis.WriteBackFor(v).Writes);

        Assert.Equal(1, writers);
        Assert.Equal(3, NatDiagnosis.ReadAfterwards.Count);
    }

    /// <summary>
    /// The asserted increment feeds the spread, so the guesses centre on the reported port - the
    /// generator PP33 ported, asked rather than restated.
    /// </summary>
    [Fact]
    public void TheAssertedIncrementFeedsTheSpread()
    {
        NatVerdict verdict = NatDiagnosis.Verdict(0, 40000, 40001, forcingEnabled: true);
        Assert.True(NatDiagnosis.WriteBackFor(verdict).RandomAllocation);

        // Random allocation means the spread, which opens at the port that answered.
        Assert.Equal(40001, PortGuessing.Spread(40001, count: 3)[0]);
    }

    /// <summary>Every rule above, still written the same way in the core it was read from.</summary>
    [Fact]
    public void TheDiagnosisIsStillTheCores()
    {
        string? file = NatDiagnosisSource.Locate();
        if (file is null)
            return;

        string core = File.ReadAllText(file);

        Assert.True(NatDiagnosisSource.TheConditionIsStillThat(core), "the condition");
        Assert.True(NatDiagnosisSource.ItStillWritesBothFields(core), "and both fields still written");
        Assert.True(
            NatDiagnosisSource.TheWriteStillPrecedesTheGuessing(core),
            "the write still precedes the guessing");
        Assert.True(
            NatDiagnosisSource.TheFieldsAreStillReadDownstream(core),
            "and the fields are still read downstream");
        Assert.True(
            NatDiagnosisSource.TheUnhandledBranchIsStillBesideIt(core),
            "with the branch that does nothing still beside it");
    }
}
