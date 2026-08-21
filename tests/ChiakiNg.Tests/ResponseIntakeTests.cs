using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP246: one free, one brace too far in.
///
/// <see cref="EveryExitButTheOrdinaryOneReleases"/> carries the task: the path with no release is
/// not an error path, it is the one every offered candidate's response takes.
/// </summary>
public class ResponseIntakeTests
{
    /// <summary>
    /// THE LEAK. Six exits release the address; the seventh - the ordinary one - does not.
    /// </summary>
    [Fact]
    public void EveryExitButTheOrdinaryOneReleases()
    {
        foreach (IntakeExit exit in Enum.GetValues<IntakeExit>())
        {
            if (exit == IntakeExit.KnownCandidate)
                continue;

            Assert.True(ResponseIntake.Releases(exit), $"{exit} should release");
        }

        Assert.False(ResponseIntake.Releases(IntakeExit.KnownCandidate));
    }

    /// <summary>And that exit is the one a working connection is made of.</summary>
    [Fact]
    public void TheOrdinaryExitIsAKnownCandidateAnswering()
    {
        IntakeExit exit = ResponseIntake.Exit(
            received: true, printable: true, supported: true, known: true, extrasUsed: 0);

        Assert.Equal(IntakeExit.KnownCandidate, exit);
        Assert.False(ResponseIntake.Releases(exit));
    }

    /// <summary>The family is read before the address is printed, so one never reaches the other.</summary>
    [Fact]
    public void TheFamilyIsDecidedBeforeThePrinting()
        => Assert.Equal(
            IntakeExit.UnsupportedFamily,
            ResponseIntake.Exit(received: true, printable: false, supported: false, known: false, 0));

    /// <summary>A new address is taken on until the extras are full, and then skipped.</summary>
    [Fact]
    public void ANewAddressIsTakenOnUntilTheExtrasAreFull()
    {
        for (int used = 0; used < ResponseIntake.ExtrasAllowed; used++)
        {
            Assert.Equal(
                IntakeExit.NewCandidate,
                ResponseIntake.Exit(true, true, true, known: false, extrasUsed: used));
        }

        Assert.Equal(
            IntakeExit.ExtrasFull,
            ResponseIntake.Exit(true, true, true, known: false, ResponseIntake.ExtrasAllowed));
    }

    /// <summary>
    /// THE INDEX. A search that matched nothing leaves it past the last used entry, and the only
    /// thing making that legal is the guard that ran first.
    /// </summary>
    [Fact]
    public void TheGuardIsWhatKeepsTheIndexInBounds()
    {
        const int offered = 5;

        // Every extras count the guard lets through indexes legally.
        for (int used = 0; used < ResponseIntake.ExtrasAllowed; used++)
        {
            Assert.Equal(offered + used, ResponseIntake.IndexAfterAMissedSearch(offered, used));
            Assert.True(
                ResponseIntake.IndexIsInBounds(offered, used),
                $"the index should be legal with {used} extras used");
        }

        // And the first count it refuses is the first one that would not be.
        Assert.False(ResponseIntake.IndexIsInBounds(offered, ResponseIntake.ExtrasAllowed));
        Assert.Equal(
            IntakeExit.ExtrasFull,
            ResponseIntake.Exit(true, true, true, false, ResponseIntake.ExtrasAllowed));
    }

    /// <summary>
    /// The address is copied whole, so bytes nothing wrote travel with it - and PP242 carries that
    /// same field into the session.
    /// </summary>
    [Fact]
    public void TheAddressIsCopiedWholeNotAsAString()
    {
        Assert.Equal(PunchAccept.AddressLength, ResponseIntake.AddressCopied);

        // Which is exactly what PP242 then copies forward, whole.
        byte[] candidate = new byte[ResponseIntake.AddressCopied];
        "10.0.0.1\0"u8.CopyTo(candidate);
        candidate[^1] = 0x5e;

        Assert.Equal(candidate, PunchAccept.Adopt(candidate));
    }

    /// <summary>Both placeholders are an exact fit for their string and terminator.</summary>
    [Fact]
    public void ThePlaceholdersFitExactly()
    {
        (string v4, int v4Len) = ResponseIntake.MappedPlaceholderFor(ipv4: true);
        (string v6, int v6Len) = ResponseIntake.MappedPlaceholderFor(ipv4: false);

        Assert.Equal(v4.Length + 1, v4Len);
        Assert.Equal(v6.Length + 1, v6Len);
        Assert.Equal(CandidateType.Derived, ResponseIntake.NewCandidateType);
    }

    /// <summary>Every rule above, still written the same way in the core it was read from.</summary>
    [Fact]
    public void TheIntakeIsStillTheCores()
    {
        string? file = ResponseIntakeSource.Locate();
        if (file is null)
            return;

        string core = File.ReadAllText(file);

        Assert.True(
            ResponseIntakeSource.TheAddressIsStillAllocatedPerResponse(core),
            "still allocated per response");

        // Seven release statements, and none of them covering the known-address path.
        Assert.Equal(7, ResponseIntakeSource.ReleaseCount(core));
        Assert.True(
            ResponseIntakeSource.TheReleaseIsStillInsideTheNewAddressBranch(core),
            "the last release is still inside the new-address branch, so the ordinary path has none");

        Assert.True(
            ResponseIntakeSource.TheSearchStillRunsToTheEnd(core), "the search still runs to the end");
        Assert.True(
            ResponseIntakeSource.TheGuardStillRunsBeforeTheIndex(core),
            "and the guard still runs before the index");

        Assert.True(
            ResponseIntakeSource.TheAddressCopyIsStillWholeBuffer(core), "the copy is still whole");
        Assert.True(
            ResponseIntakeSource.ThePlaceholdersAreStillExact(core), "and the placeholders still exact");
    }
}
