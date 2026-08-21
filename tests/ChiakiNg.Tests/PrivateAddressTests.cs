using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP248: seven prefix tests, none of which can match.
///
/// <see cref="NoPrivateAddressIsEverRecognised"/> carries the task, and it PRODUCES the result -
/// <see cref="PrivateAddress.Strncmp"/> is a real strncmp, so these run the comparisons rather than
/// asserting a description of them.
/// </summary>
public class PrivateAddressTests
{
    /// <summary>The strncmp is a real one, including the case the finding lives in.</summary>
    [Fact]
    public void TheComparisonIsAFaithfulStrncmp()
    {
        // Equal within the count.
        Assert.Equal(0, PrivateAddress.Strncmp("10.0.0.1", "10.", 3));

        // One byte further and the terminator is in the comparison.
        Assert.NotEqual(0, PrivateAddress.Strncmp("10.0.0.1", "10.", 4));

        // A count past both ends stops at the shared terminator.
        Assert.Equal(0, PrivateAddress.Strncmp("10.", "10.", 40));
    }

    /// <summary>
    /// THE FINDING. Every private range, in every form the core tests, judged not private.
    /// </summary>
    [Theory]
    [InlineData("10.0.0.1")]
    [InlineData("10.255.255.254")]
    [InlineData("192.168.1.5")]
    [InlineData("192.168.0.1")]
    [InlineData("172.16.0.9")]
    [InlineData("172.20.10.1")]
    [InlineData("172.31.255.254")]
    [InlineData("fc00::1")]
    [InlineData("fd12:3456::1")]
    [InlineData("FD00::9")]
    public void NoPrivateAddressIsEverRecognised(string address)
    {
        Assert.False(
            PrivateAddress.IsLocalAsWritten(address),
            $"{address} was recognised, so the comparison lengths have changed");

        // And compared over each prefix's own length, every one of them is.
        Assert.True(
            PrivateAddress.IsLocalAsIntended(address),
            $"{address} is not recognised even by the correct comparison");
    }

    /// <summary>A public address is not private either way, so the shorter length is not a blunt yes.</summary>
    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("172.15.0.1")]
    [InlineData("172.32.0.1")]
    [InlineData("193.168.1.1")]
    [InlineData("2001:db8::1")]
    public void APublicAddressIsPrivateNeitherWay(string address)
    {
        Assert.False(PrivateAddress.IsLocalAsWritten(address));
        Assert.False(PrivateAddress.IsLocalAsIntended(address));
    }

    /// <summary>
    /// Every test overshoots, by two different amounts for two different reasons - which is why a
    /// port shortening every count by one would fix six and leave sixteen broken.
    /// </summary>
    [Fact]
    public void EveryTestOvershootsByOneOfTwoAmounts()
    {
        // The literals: the prefix plus its terminator.
        foreach ((string Prefix, int Length) test in
            PrivateAddress.Ipv4Tests.Take(2).Concat(PrivateAddress.Ipv6Tests))
        {
            Assert.Equal(1, PrivateAddress.Overshoot(test));
        }

        // The generated ones: the buffer's size, which is one more again.
        foreach ((string Prefix, int Length) test in PrivateAddress.Ipv4Tests.Skip(2))
        {
            Assert.Equal(2, PrivateAddress.Overshoot(test));
            Assert.Equal(PrivateAddress.CompareBuffer, test.Length);
        }

        // Two literals, sixteen generated, four spellings of the v6 prefix.
        Assert.Equal(18, PrivateAddress.Ipv4Tests.Count);
        Assert.Equal(4, PrivateAddress.Ipv6Tests.Count);
    }

    /// <summary>
    /// The consequence, stated narrowly: only a DISCOVERED private address is affected, and what it
    /// loses is which local candidate fills its mapped address.
    /// </summary>
    [Fact]
    public void OnlyADiscoveredPrivateAddressIsAffected()
    {
        // A candidate the console typed as local is unaffected - it never consults the test.
        Assert.Equal(
            MappedSource.Directly, PrivateAddress.FillFrom(CandidateType.Local, "10.0.0.1"));

        // A discovered private one takes the STUN candidate, and would have taken the other.
        Assert.Equal(
            MappedSource.ViaStun, PrivateAddress.FillFrom(CandidateType.Derived, "10.0.0.1"));
        Assert.Equal(
            MappedSource.Directly,
            PrivateAddress.FillFromIfTheTestsWorked(CandidateType.Derived, "10.0.0.1"));

        // And a discovered public one is unaffected too - both agree.
        Assert.Equal(
            PrivateAddress.FillFromIfTheTestsWorked(CandidateType.Derived, "8.8.8.8"),
            PrivateAddress.FillFrom(CandidateType.Derived, "8.8.8.8"));
    }

    /// <summary>
    /// The family is chosen by looking for a dot, so PP236's blind spot is here too: a mapped IPv6
    /// address is measured against the IPv4 prefixes.
    /// </summary>
    [Fact]
    public void TheFamilyIsStillChosenByADot()
    {
        Assert.Equal(
            System.Net.Sockets.AddressFamily.InterNetwork,
            PunchResponse.FamilyOf("::ffff:10.0.0.1"));

        // So it is tested against the v4 prefixes - and fails those, as everything does.
        Assert.False(PrivateAddress.IsLocalAsWritten("::ffff:10.0.0.1"));
    }

    /// <summary>Every rule above, still written the same way in the core it was read from.</summary>
    [Fact]
    public void TheClassificationIsStillTheCores()
    {
        string? file = PrivateAddressSource.Locate();
        if (file is null)
            return;

        string core = File.ReadAllText(file);

        Assert.True(
            PrivateAddressSource.EveryTestIsStillOneByteTooLong(core),
            "every literal test is still the prefix plus its terminator");
        Assert.True(
            PrivateAddressSource.TheGeneratedTestsAreStillTheSame(core),
            "and the sixteen generated ones too");
        Assert.True(
            PrivateAddressSource.TheFlagIsStillOnlyForDiscoveredCandidates(core),
            "the flag is still only computed for a discovered candidate");
        Assert.True(
            PrivateAddressSource.AnExplicitlyLocalCandidateStillTakesIt(core),
            "and an explicitly local one still takes the local branch");
        Assert.True(
            PrivateAddressSource.BothBranchesStillReadTheLocalArray(core),
            "both branches still read the local array, remote_candidate included");
    }
}
