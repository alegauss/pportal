using System.Text;
using ChiakiNg.Native;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP29: the managed discovery protocol against the C, on the same bytes.
///
/// PP6 wrapped every one of these rather than porting them, on the grounds that a second reply
/// parser was the one piece of discovery a console would have to be present to disprove. It does
/// not: the wrapper is the oracle, and this file is the disproof it was waiting for.
/// </summary>
public class DiscoveryProtocolTests(ITestOutputHelper output)
{
    /// <summary>The system versions worth asking about, including the ones between the rungs.</summary>
    public static TheoryData<string, bool> Versions()
    {
        var data = new TheoryData<string, bool>();
        string[] versions =
        [
            "", "0", "1", "-1", "+8050001", " 8050001", "8050001abc", "abc",
            "6999999", "7000000", "7999999", "8000000", "8049999",
            "8050000", "8050001", "9000000", "08050001", "99999999999999999999",
        ];

        foreach (string v in versions)
        {
            data.Add(v, false);
            data.Add(v, true);
        }

        return data;
    }

    /// <summary>
    /// THE CLASSIFICATION, against the C's own ladder for every version worth asking about.
    ///
    /// The disagreement this would catch is a PS5 on early firmware: it announces the PS5 protocol,
    /// misses both PS5 rungs, and comes out PS4_10. A port that read "is a PS5" first would send it
    /// a PS5 session setup and get nothing back.
    /// </summary>
    [Theory]
    [MemberData(nameof(Versions))]
    public void TheLadderAgreesWithTheC(string systemVersion, bool ps5)
    {
        string protocol = DiscoveryProtocol.ProtocolVersion(ps5);

        ChiakiTarget fromC = Discovery.Target(systemVersion, protocol);
        ChiakiTarget managed = DiscoveryProtocol.TargetFor(systemVersion, protocol);

        Assert.Equal(fromC, managed);
        output.WriteLine($"{(ps5 ? "PS5" : "PS4")} \"{systemVersion}\" -> {managed}");
    }

    /// <summary>
    /// And the case the ladder is easiest to get wrong: a PS5 below its own first rung.
    ///
    /// Spelled out rather than left inside the matrix, because it is the one an assumption reaches
    /// for and the matrix would report it as one failure among thirty-six.
    /// </summary>
    [Fact]
    public void APs5BelowItsFirstRungIsAPs4()
    {
        string ps5 = DiscoveryProtocol.Ps5ProtocolVersion;

        Assert.Equal(ChiakiTarget.Ps4_10, DiscoveryProtocol.TargetFor("8000000", ps5));
        Assert.Equal(ChiakiTarget.Ps4_10, Discovery.Target("8000000", ps5));

        // ...and one rung up it is a PS5 again, so this is a boundary rather than the protocol
        // version being ignored.
        Assert.Equal(ChiakiTarget.Ps5Unknown, DiscoveryProtocol.TargetFor("8050000", ps5));
        Assert.Equal(ChiakiTarget.Ps5_1, DiscoveryProtocol.TargetFor("8050001", ps5));
    }

    /// <summary>Whether a reply is a PS5 is one exact comparison, and the C agrees.</summary>
    [Theory]
    [InlineData("00030010")]
    [InlineData("00020020")]
    [InlineData("00030011")]
    [InlineData("00030010 ")]
    [InlineData("00030")]
    [InlineData("")]
    public void TheFamilyTestAgreesWithTheC(string protocolVersion)
        => Assert.Equal(Discovery.IsPs5(protocolVersion), DiscoveryProtocol.IsPs5(protocolVersion));

    /// <summary>The packets are byte-for-byte the C's, for both commands and both families.</summary>
    [Theory]
    [InlineData(DiscoveryCommand.Search, false, 0UL)]
    [InlineData(DiscoveryCommand.Search, true, 0UL)]
    [InlineData(DiscoveryCommand.Wakeup, false, 0UL)]
    [InlineData(DiscoveryCommand.Wakeup, true, 1UL)]
    [InlineData(DiscoveryCommand.Wakeup, true, 12345678901234567890UL)]
    [InlineData(DiscoveryCommand.Wakeup, false, ulong.MaxValue)]
    public void ThePacketsAreTheCs(DiscoveryCommand command, bool ps5, ulong credential)
    {
        byte[] fromC = Discovery.Packet(command, ps5, credential);
        byte[] managed = DiscoveryProtocol.Packet(command, ps5, credential);

        Assert.True(fromC.SequenceEqual(managed),
            $"C: {Encoding.UTF8.GetString(fromC)}\nmanaged: {Encoding.UTF8.GetString(managed)}");

        output.WriteLine(Encoding.UTF8.GetString(managed).Replace("\n", "\\n"));
    }

    /// <summary>
    /// The credential is decimal, which is the one field of a wake packet a port can silently ruin.
    ///
    /// It arrives as a registration key read as hexadecimal and is printed back as %llu, so a port
    /// formatting it as hex sends a console a number it has never issued - and a wake packet that
    /// is refused looks exactly like a console that is switched off.
    /// </summary>
    [Fact]
    public void TheWakeCredentialIsDecimal()
    {
        string text = DiscoveryProtocol.PacketText(DiscoveryCommand.Wakeup, ps5: true, 0xdeadbeef);

        Assert.Contains("user-credential:3735928559\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("deadbeef", text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// THE BYTE THE FORMATTER NEVER COUNTED. sendto is handed len + 1.
    ///
    /// The packet as formatted ends with a newline; the datagram on the wire ends with a newline
    /// and a zero. Nothing in the formatter says so and nothing in the wrapper returns it.
    /// </summary>
    [Fact]
    public void TheDatagramCarriesTheTerminator()
    {
        byte[] packet = DiscoveryProtocol.Packet(DiscoveryCommand.Search, ps5: false);
        byte[] wire = DiscoveryProtocol.OnTheWire(DiscoveryCommand.Search, ps5: false);

        Assert.Equal(packet.Length + 1, wire.Length);
        Assert.Equal((byte)'\n', wire[^2]);
        Assert.Equal(0, wire[^1]);
    }

    /// <summary>The ports and version strings are the header's, not a copy that drifted.</summary>
    [Fact]
    public void ThePortsAndVersionsAreTheCs()
    {
        Assert.Equal(Discovery.Port(false), DiscoveryProtocol.Port(false));
        Assert.Equal(Discovery.Port(true), DiscoveryProtocol.Port(true));
        Assert.Equal(Discovery.ProtocolVersion(false), DiscoveryProtocol.ProtocolVersion(false));
        Assert.Equal(Discovery.ProtocolVersion(true), DiscoveryProtocol.ProtocolVersion(true));
        Assert.Equal(
            (DiscoveryProtocol.LocalPortMin, DiscoveryProtocol.LocalPortMax), Discovery.LocalPortRange);
    }

    /// <summary>The three state words, including the default that covers everything else.</summary>
    [Theory]
    [InlineData(DiscoveryHostState.Ready)]
    [InlineData(DiscoveryHostState.Standby)]
    [InlineData(DiscoveryHostState.Unknown)]
    [InlineData((DiscoveryHostState)99)]
    public void TheStateWordsAreTheCs(DiscoveryHostState state)
        => Assert.Equal(Discovery.HostStateString(state), DiscoveryProtocol.StateString(state));

    /// <summary>The reply datagrams worth parsing, including the ones that are not quite replies.</summary>
    public static TheoryData<string> Replies() =>
    [
        // A ready PS5 with everything filled in.
        "HTTP/1.1 200 Ok\n"
            + "host-id:1234567890AB\n"
            + "host-type:PS5\n"
            + "host-name:Living Room\n"
            + "host-request-port:9295\n"
            + "device-discovery-protocol-version:00030010\n"
            + "system-version:08050001\n"
            + "running-app-titleid:CUSA00001\n"
            + "running-app-name:A Game\n",

        // 620, which is not an HTTP code and is what standby answers with.
        "HTTP/1.1 620 Server Standby\n"
            + "host-id:1234567890AB\n"
            + "host-type:PS4\n"
            + "host-name:Bedroom\n"
            + "host-request-port:997\n"
            + "device-discovery-protocol-version:00020020\n"
            + "system-version:08000000\n",

        // A code that is neither, which is "unknown" rather than a refusal.
        "HTTP/1.1 404 Not Found\nhost-name:Nothing\n",

        // A zero-padded port, which strtoul base 0 reads as OCTAL.
        "HTTP/1.1 200 Ok\nhost-request-port:0987\n",

        // 0x, likewise.
        "HTTP/1.1 200 Ok\nhost-request-port:0x2000\n",

        // Past sixteen bits, where the cast truncates rather than refusing.
        "HTTP/1.1 200 Ok\nhost-request-port:65537\n",

        // The same header twice, where which one wins depends on the list being reversed.
        "HTTP/1.1 200 Ok\nhost-name:First\nhost-name:Second\nhost-request-port:1\n",

        // Headers the C does not know, which are dropped in silence.
        "HTTP/1.1 200 Ok\nhost-name:Odd\nsomething-else:value\nHOST-NAME:upper\n",
    ];

    /// <summary>
    /// THE REPLY PARSER, field for field against the C on the same datagram.
    ///
    /// Every field, not just the ones a console usually sends: the port's base, the state's mapping
    /// and the eight strings all have to agree, and comparing only the ones that are populated is
    /// how a parser that drops a field passes.
    /// </summary>
    [Theory]
    [MemberData(nameof(Replies))]
    public void TheReplyParserAgreesWithTheC(string reply)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(reply);

        DiscoveredConsole? fromC = Discovery.ParseReply(bytes, "192.168.1.2", out ChiakiError error);
        DiscoveredConsole? managed = DiscoveryProtocol.ParseReply(bytes, "192.168.1.2");

        Assert.Equal(ChiakiError.Success, error);
        Assert.NotNull(fromC);
        Assert.Equal(fromC, managed);

        output.WriteLine($"{managed!.Value.State} port {managed.Value.RequestPort} name {managed.Value.Name}");
    }

    /// <summary>
    /// A zero-padded port really is read as octal, which is worth stating on its own.
    ///
    /// 987 is the PS4's discovery port, so a console announcing it with a leading zero is answered
    /// on 519. Reproduced rather than fixed - see PP231.
    /// </summary>
    [Fact]
    public void AZeroPaddedPortIsOctal()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("HTTP/1.1 200 Ok\nhost-request-port:0987\n");

        // "0987" stops at the 9, which is not an octal digit, so it is zero rather than 519 - and
        // that is worse, not better.
        Assert.Equal(0, DiscoveryProtocol.ParseReply(bytes, "1.2.3.4")!.Value.RequestPort);
        Assert.Equal(0, Discovery.ParseReply(bytes, "1.2.3.4", out _)!.Value.RequestPort);
    }

    /// <summary>
    /// Something that is not an HTTP response yields nothing, from both.
    ///
    /// The last of these is the one worth having: a status line with NOTHING after it is refused,
    /// because libchiaki wants a byte beyond the line and not merely a line. A reply that arrived
    /// exactly that truncated would be a console found and then dropped.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("nonsense")]
    [InlineData("SRCH * HTTP/1.1\n")]
    [InlineData("HTTP/1.1 200 Ok\n")]
    public void RubbishIsRefusedByBoth(string reply)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(reply);

        Assert.Null(Discovery.ParseReply(bytes, "1.2.3.4", out _));
        Assert.Null(DiscoveryProtocol.ParseReply(bytes, "1.2.3.4"));
    }

    /// <summary>
    /// atoi is base ten, which none of the other three numeric reads in this port are.
    ///
    /// The zero-padded version is the one that separates them: 08050001 is eight million here and
    /// would be an octal parse error to the header two lines above it in the same datagram.
    /// </summary>
    [Fact]
    public void TheVersionIsBaseTenWhereThePortIsNot()
    {
        Assert.Equal(8050001, DiscoveryProtocol.Atoi("08050001"));
        Assert.Equal(0u, RegistResponse.ParseAutoBase("08050001"));
    }

    /// <summary>
    /// PP299: a reply with no system-version header, which used to be a crash.
    ///
    /// The parser memsets the host and assigns only the headers that arrived, so the field is null
    /// whenever that header was absent, and the classifier reached it through atoi with no guard -
    /// two lines below a macro that guards every OTHER string. Anything on the LAN answering on 987
    /// or 9302 with a parseable response was enough to reach it, while the client was merely
    /// looking for consoles.
    ///
    /// PP231 says reproduce rather than fix; a null dereference is where that rule stops. The C is
    /// guarded now, so the managed answer and the C's are the same answer rather than a divergence.
    /// </summary>
    [Fact]
    public void AReplyWithNoVersionIsClassifiedRatherThanFatal()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("HTTP/1.1 200 Ok\nhost-name:Quiet\n");

        DiscoveredConsole? console = DiscoveryProtocol.ParseReply(bytes, "1.2.3.4");
        Assert.NotNull(console);
        Assert.Null(console.Value.SystemVersion);

        Assert.Equal(ChiakiTarget.Ps4Unknown,
            DiscoveryProtocol.TargetFor(console.Value.SystemVersion, console.Value.ProtocolVersion));
    }

    /// <summary>
    /// And the same null, all the way into the library.
    ///
    /// This is the assertion PP299 could not have: the shim substituted "" for a null
    /// system_version before calling in, so the crash was unreachable through the port and the
    /// managed side's divergence could only be argued rather than shown. The substitution is gone
    /// with the defect it was working around, and null now travels from here into
    /// chiaki_discovery_host_system_version_target.
    ///
    /// It does not fail if the guard is removed - it takes the test host down with an access
    /// violation, which is the honest report for what this is.
    /// </summary>
    [Fact]
    public void TheNullReachesTheLibraryAndIsClassifiedThere()
    {
        Assert.Equal(ChiakiTarget.Ps4Unknown, Discovery.Target(null, null));

        // The empty string the shim used to substitute lands on the same rung, which is why nothing
        // that classified before classifies differently now.
        Assert.Equal(ChiakiTarget.Ps4Unknown, Discovery.Target("", null));
    }

    /// <summary>THE DRIFT CHECK. The ladder's order, the port's base and the extra byte.</summary>
    [Fact]
    public void TheCStillDoesThis()
    {
        string? impl = SanitizerSource.LocateRelative(@"lib\src\discovery.c");
        Assert.True(impl is not null, "no lib\\src\\discovery.c - this file is describing nothing");

        string core = File.ReadAllText(impl);

        Assert.True(DiscoveryProtocol.ThePs5RungsAreStillFirst(core),
            "the PS5 rungs no longer come first, so an early-firmware PS5 classifies differently");
        Assert.True(DiscoveryProtocol.TheVersionIsStillGuardedBeforeAtoi(core),
            "PP299's guard is gone from the classifier, so a reply with no system-version header "
                + "dereferences null again - and any device on the LAN can send one");
        Assert.True(DiscoveryProtocol.TheRequestPortStillAutoDetectsItsBase(core),
            "host-request-port no longer uses base 0, so its octal case has changed meaning");
        Assert.True(DiscoveryProtocol.TheTerminatorIsStillSent(core),
            "the datagram no longer carries its terminator, so the wire format moved a byte");
    }
}
