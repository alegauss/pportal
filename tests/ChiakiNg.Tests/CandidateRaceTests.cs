using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP33: the race that decides the connection, and decides it on the first answer.
/// </summary>
public class CandidateRaceTests
{
    private static readonly byte[] TheId = [1, 2, 3, 4, 5];
    private static readonly byte[][] Ids = [TheId];

    private static Candidate Local(string address, ushort port)
        => new(CandidateType.Local, address, "0.0.0.0", port, 0);

    private static CandidateRace Race(params Candidate[] offered)
        => new(offered, Ids);

    /// <summary>
    /// THE FIRST TO ANSWER WINS. Not the local one, not the first offered - the count that selects
    /// is one round trip, so whichever datagram arrives first settles it.
    ///
    /// A port that sorted the candidates by type before racing them would pick a different console
    /// on a multi-homed network, and would look more sensible while doing it.
    /// </summary>
    [Fact]
    public void TheCandidateThatAnswersFirstIsSelectedEvenWhenItIsLast()
    {
        CandidateRace race = Race(Local("10.0.0.4", 9295), Local("203.0.113.9", 9295));

        RaceOutcome outcome = race.Receive("203.0.113.9", 9295, CandidateRace.ResponseType, TheId);

        Assert.Equal(RaceOutcome.Selected, outcome);
        Assert.Equal("203.0.113.9", race.Selected?.Address);
    }

    /// <summary>And the count really is one, which is what makes the above true.</summary>
    [Fact]
    public void OneRoundTripIsTheWholeCount()
    {
        Assert.Equal(1, CandidateRace.RequestNumber);

        CandidateRace race = Race(Local("10.0.0.4", 9295));
        Assert.Equal(0, race.ResponsesFrom(0));

        Assert.Equal(
            RaceOutcome.Selected, race.Receive("10.0.0.4", 9295, CandidateRace.ResponseType, TheId));
        Assert.Equal(1, race.ResponsesFrom(0));
    }

    /// <summary>A request from the other end is answered, and settles nothing.</summary>
    [Fact]
    public void ARequestIsAnsweredRatherThanCounted()
    {
        CandidateRace race = Race(Local("10.0.0.4", 9295));

        Assert.Equal(
            RaceOutcome.Answered, race.Receive("10.0.0.4", 9295, CandidateRace.RequestType, null));
        Assert.Equal(0, race.ResponsesFrom(0));
        Assert.Null(race.Selected);
    }

    /// <summary>
    /// A WRONG REQUEST ID IS IGNORED AND NOT COUNTED - it is a late reply to a round that has
    /// already passed, so it neither advances the candidate nor kills the race.
    /// </summary>
    [Fact]
    public void AResponseWithTheWrongIdIsIgnoredWithoutCounting()
    {
        CandidateRace race = Race(Local("10.0.0.4", 9295));

        Assert.Equal(
            RaceOutcome.WrongRequestId,
            race.Receive("10.0.0.4", 9295, CandidateRace.ResponseType, [9, 9, 9, 9, 9]));

        Assert.Equal(0, race.ResponsesFrom(0));
        Assert.Null(race.Selected);

        // And the right one still wins afterwards, which is what "not fatal" means here.
        Assert.Equal(
            RaceOutcome.Selected, race.Receive("10.0.0.4", 9295, CandidateRace.ResponseType, TheId));
    }

    /// <summary>
    /// AN UNEXPECTED TYPE IS FATAL - the exchange with a candidate the console offered has gone
    /// wrong, and the core jumps out of the whole race.
    /// </summary>
    [Fact]
    public void AnUnexpectedTypeFromAnOfferedCandidateIsFatal()
    {
        CandidateRace race = Race(Local("10.0.0.4", 9295));

        Assert.Equal(RaceOutcome.Fatal, race.Receive("10.0.0.4", 9295, 0x01000000, TheId));
    }

    /// <summary>
    /// But not from a DERIVED one, which is an address this client guessed at rather than one the
    /// console offered - so rubbish from it is expected and merely skipped.
    /// </summary>
    [Fact]
    public void AnUnexpectedTypeFromADerivedCandidateIsSkipped()
    {
        var derived = new Candidate(CandidateType.Derived, "203.0.113.9", "0.0.0.0", 9295, 0);
        CandidateRace race = Race(derived);

        Assert.Equal(RaceOutcome.Skipped, race.Receive("203.0.113.9", 9295, 0x01000000, TheId));
    }

    /// <summary>
    /// AN ADDRESS NOBODY OFFERED BECOMES A CANDIDATE - the NAT answering from somewhere other than
    /// where it was written to, typed DERIVED with a mapped port of zero.
    /// </summary>
    [Fact]
    public void AnUnofferedAddressIsTakenOnAsDerived()
    {
        CandidateRace race = Race(Local("10.0.0.4", 9295));

        Assert.Equal(
            RaceOutcome.Selected,
            race.Receive("198.51.100.7", 41234, CandidateRace.ResponseType, TheId));

        Assert.Equal(1, race.ExtraUsed);
        Assert.Equal(2, race.Candidates.Count);

        Candidate taken = race.Candidates[1];
        Assert.Equal(CandidateType.Derived, taken.Type);
        Assert.Equal("198.51.100.7", taken.Address);
        Assert.Equal(41234, taken.Port);
        Assert.Equal(0, taken.MappedPort);
        Assert.Equal(CandidateRace.DerivedMappedAddressV4, taken.MappedAddress);
    }

    /// <summary>And the fourth of them is dropped, with the race carrying on.</summary>
    [Fact]
    public void TheFourthUnofferedAddressIsDropped()
    {
        CandidateRace race = Race(Local("10.0.0.4", 9295));

        for (int i = 0; i < CandidateRace.ExtraCandidateAddresses; i++)
        {
            Assert.Equal(
                RaceOutcome.Answered,
                race.Receive($"198.51.100.{i}", 41234, CandidateRace.RequestType, null));
        }

        Assert.Equal(3, race.ExtraUsed);
        Assert.Equal(
            RaceOutcome.ExtraLimitReached,
            race.Receive("198.51.100.99", 41234, CandidateRace.RequestType, null));
        Assert.Equal(3, race.ExtraUsed);

        // Dropped, not fatal: the offered candidate can still win afterwards.
        Assert.Equal(
            RaceOutcome.Selected, race.Receive("10.0.0.4", 9295, CandidateRace.ResponseType, TheId));
    }

    /// <summary>
    /// The same address on a different port is a DIFFERENT candidate, which is the whole point of
    /// a NAT probe - the port is what moved.
    /// </summary>
    [Fact]
    public void TheSameAddressOnAnotherPortIsAnotherCandidate()
    {
        CandidateRace race = Race(Local("10.0.0.4", 9295));

        Assert.Equal(
            RaceOutcome.Answered, race.Receive("10.0.0.4", 9296, CandidateRace.RequestType, null));
        Assert.Equal(1, race.ExtraUsed);
    }

    /// <summary>
    /// The multi-round machinery, exercised past the constant the core ships with. The response is
    /// matched against the id for the round it ANSWERS - which is how many have been counted, not
    /// how many were sent - so a repeat of the first round's id does not finish the second.
    /// </summary>
    [Fact]
    public void TheIdIsTheOneForTheRoundBeingAnswered()
    {
        byte[] first = [1, 2, 3, 4, 5];
        byte[] second = [6, 7, 8, 9, 10];
        var race = new CandidateRace([Local("10.0.0.4", 9295)], [first, second]);

        // The count that SELECTS is still one, because that is a compile-time constant and not the
        // number of ids - so this still finishes on the first round, with the first round's id.
        Assert.Equal(
            RaceOutcome.Selected, race.Receive("10.0.0.4", 9295, CandidateRace.ResponseType, first));

        // What the second id proves is the indexing: a response carrying the id for a round that
        // has not been reached is the wrong id, not a shortcut to it.
        var strict = new CandidateRace([Local("10.0.0.4", 9295)], [first, second]);
        Assert.Equal(
            RaceOutcome.WrongRequestId,
            strict.Receive("10.0.0.4", 9295, CandidateRace.ResponseType, second));
        Assert.Equal(0, strict.ResponsesFrom(0));
        Assert.Null(strict.Selected);
    }

    /// <summary>IPv6 is not raced at all in this build, which is one fewer path to port.</summary>
    [Fact]
    public void SixIsNotEnabled()
        => Assert.False(CandidateRace.EnableIpv6);

    /// <summary>Every rule above, still stated the same way in the core.</summary>
    [Fact]
    public void TheRacesRulesAreStillTheQtCores()
    {
        string? path = CandidateRaceSource.Locate();
        if (path is null)
            return;

        string core = File.ReadAllText(path);

        Assert.True(CandidateRaceSource.TheConstantsAreStillTheseValues(core), "eight constants");
        Assert.True(CandidateRaceSource.TheFirstToAnswerStillWins(core), "one round trip decides");
        Assert.True(CandidateRaceSource.TheIdIsStillIndexedByTheCount(core), "the id for the round");
        Assert.True(CandidateRaceSource.AWrongIdIsStillIgnored(core), "a wrong id carries on");
        Assert.True(
            CandidateRaceSource.AnUnexpectedTypeIsStillFatalExceptForDerived(core),
            "fatal, except when guessed at");
        Assert.True(CandidateRaceSource.AnUnofferedAddressIsStillTakenOn(core), "three taken on");
    }

    /// <summary>
    /// And the constant check earns its green: a core whose count had been raised must turn it red,
    /// because that value is the difference between a race and a best-of.
    /// </summary>
    [Fact]
    public void TheConstantCheckFailsWhenTheCountChanges()
    {
        string? path = CandidateRaceSource.Locate();
        if (path is null)
            return;

        string core = File.ReadAllText(path);

        string raised = core.Replace(
            "#define CHECK_CANDIDATES_REQUEST_NUMBER 1",
            "#define CHECK_CANDIDATES_REQUEST_NUMBER 3",
            StringComparison.Ordinal);

        Assert.NotEqual(core, raised);
        Assert.False(CandidateRaceSource.TheConstantsAreStillTheseValues(raised));
    }
}
