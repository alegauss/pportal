using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP232: the two fetched lists, and the two rules that are not interchangeable.
///
/// The test that carries the task is <see cref="TheIpv4RuleWouldDestroyAnIpv6Line"/>: it runs the
/// wrong parser on the right input and shows what a port reusing one for both would have stored.
/// </summary>
public class StunServerListTests
{
    /// <summary>Shaped like the file: one host and port per line, newline separated.</summary>
    private const string Hosts = "stun.l.google.com:19302\nstun1.example.net:3478\n";

    /// <summary>And the other one, bracketed because an address is made of colons.</summary>
    private const string Ipv6 = "[2001:4860:4864:5:8000::1]:19302\n[2a00:1450:4010:c08::7f]:3478\n";

    /// <summary>An IPv4 line is host, colon, port.</summary>
    [Fact]
    public void AnIpv4LineIsCutOnTheColon()
    {
        IReadOnlyList<StunServer> servers = StunServerList.ParseHosts(Hosts);

        Assert.Equal(2, servers.Count);
        Assert.Equal(new StunServer("stun.l.google.com", 19302), servers[0]);
        Assert.Equal(new StunServer("stun1.example.net", 3478), servers[1]);
    }

    /// <summary>An IPv6 line is bracket, address, bracket, colon, port.</summary>
    [Fact]
    public void AnIpv6LineIsCutOnTheBracket()
    {
        IReadOnlyList<StunServer> servers = StunServerList.ParseIpv6(Ipv6);

        Assert.Equal(2, servers.Count);
        Assert.Equal(new StunServer("2001:4860:4864:5:8000::1", 19302), servers[0]);
        Assert.Equal(new StunServer("2a00:1450:4010:c08::7f", 3478), servers[1]);
    }

    /// <summary>
    /// The whole reason there are two rules. Cutting an IPv6 line on the first colon takes `[2001`
    /// as a host and the rest of the address as a port that reads as nothing - a server entry that
    /// is wrong in both fields and looks like an entry.
    /// </summary>
    [Fact]
    public void TheIpv4RuleWouldDestroyAnIpv6Line()
    {
        IReadOnlyList<StunServer> wrong = StunServerList.ParseHosts(Ipv6);

        Assert.Equal("[2001", wrong[0].Host);
        Assert.Equal(4860, wrong[0].Port);

        // And the right rule on the same line.
        Assert.Equal("2001:4860:4864:5:8000::1", StunServerList.ParseIpv6(Ipv6)[0].Host);
    }

    /// <summary>Ten of each, which is a fixed array in the core rather than a policy.</summary>
    [Fact]
    public void TenIsTheBound()
    {
        string many = string.Concat(Enumerable.Range(0, 25).Select(n => $"stun{n}.example.net:3478\n"));

        Assert.Equal(StunServerList.Most, StunServerList.ParseHosts(many).Count);
        Assert.Equal(10, StunServerList.Most);
    }

    /// <summary>
    /// A trailing newline is not an eleventh server. strtok treats consecutive separators as one,
    /// so a blank line is not a line at all to the core - and every one of these files ends in one.
    /// </summary>
    [Fact]
    public void ABlankLineIsNotAServer()
    {
        Assert.Single(StunServerList.ParseHosts("stun.example.net:3478\n\n\n"));
        Assert.Single(StunServerList.ParseIpv6("[::1]:3478\n\n"));
    }

    /// <summary>
    /// A port that is not a number is ZERO, not an error. strtol answers zero for text it cannot
    /// read and the core stores it without looking, so the entry survives and fails later.
    /// </summary>
    [Fact]
    public void APortThatIsNotANumberIsZero()
    {
        Assert.Equal(0, StunServerList.PortOf("nonsense"));
        Assert.Equal(0, StunServerList.PortOf(""));

        // And a number with something after it is read up to the something.
        Assert.Equal(3478, StunServerList.PortOf("3478 # comment"));

        IReadOnlyList<StunServer> servers = StunServerList.ParseHosts("stun.example.net:abc\n");
        Assert.Equal(new StunServer("stun.example.net", 0), servers[0]);
    }

    /// <summary>The parse stops at the first line it cannot read, keeping what came before.</summary>
    [Fact]
    public void ABadLineStopsTheParseAndKeepsWhatCameBefore()
    {
        IReadOnlyList<StunServer> servers =
            StunServerList.ParseHosts("stun.example.net:3478\nno-colon-here\nstun2.example.net:3478\n");

        Assert.Single(servers);
        Assert.Equal("stun.example.net", servers[0].Host);
    }

    /// <summary>And an IPv6 line without its bracket is refused rather than beheaded.</summary>
    [Fact]
    public void AnIpv6LineWithoutItsBracketIsRefused()
        => Assert.Empty(StunServerList.ParseIpv6("2001:db8::1]:3478\n"));

    /// <summary>Every rule above, still written the same way in the core it was read from.</summary>
    [Fact]
    public void TheFetchedListsAreStillTheCores()
    {
        string? file = StunServerListSource.Locate();
        if (file is null)
            return;

        string core = File.ReadAllText(file);

        Assert.True(StunServerListSource.TheListsStillComeFromThere(core), "both URLs");
        Assert.True(StunServerListSource.TenIsStillTheBound(core), "ten of each");
        Assert.True(StunServerListSource.TheDelimitersStillDiffer(core), "colon and bracket");
        Assert.True(StunServerListSource.TheBracketIsStillSkippedByHand(core), "by pointer arithmetic");
        Assert.True(
            StunServerListSource.TheIpv6ErrorsStillNameTheWrongUrl(core),
            "and the IPv6 failures still print the IPv4 URL");
    }
}
