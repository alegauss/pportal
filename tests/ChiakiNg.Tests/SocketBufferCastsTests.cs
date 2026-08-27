using ChiakiNg.Protocol;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP426: every socket call that hands over an unsigned buffer casts it.
///
/// Two of eleven did not, both in takion.c, and both printed a -Wpointer-sign warning on every
/// build. The bytes were never wrong - char and uint8_t have the same width - and what the two cost
/// was a build whose warning output a reader learns to skip.
/// </summary>
public class SocketBufferCastsTests(ITestOutputHelper output)
{
    /// <summary>
    /// THE CEILING. No socket call in lib hands an unsigned buffer over uncast.
    ///
    /// The ratchet rule: it may fall and may not rise. A call added without the cast turns this red
    /// in the commit that adds it, rather than adding a line to output nobody reads.
    /// </summary>
    [Fact]
    public void NoUnsignedBufferReachesASocketUncast()
    {
        if (SocketBufferCasts.RepositoryRoot() is not { } root)
            return;

        IReadOnlyList<SocketCall> all = SocketBufferCasts.All(root);
        IReadOnlyList<SocketCall> uncast = SocketBufferCasts.Uncast(root);

        output.WriteLine($"{all.Count} socket calls, {uncast.Count} uncast");

        // A sweep that finds nothing passes for PP271's reason, so the scan is asserted first.
        Assert.True(all.Count >= 8, $"only {all.Count} socket calls found - the scan is not working");

        Assert.True(
            uncast.Count <= SocketBufferCasts.UncastCeiling,
            "these hand an unsigned buffer to a socket without the cast winsock wants:\n  "
                + string.Join(
                    "\n  ", uncast.Select(c => $"{c.File}  {c.Call}(.., {c.Buffer}, ..)")));
    }

    /// <summary>
    /// And the two that were the task are cast now, named so a revert is legible.
    /// </summary>
    [Fact]
    public void TakionsTwoCallsAreCast()
    {
        if (SocketBufferCasts.RepositoryRoot() is not { } root)
            return;

        IReadOnlyList<SocketCall> takion =
            [.. SocketBufferCasts.All(root).Where(c => c.File.EndsWith("takion.c", StringComparison.Ordinal))];

        Assert.True(takion.Count >= 2, "takion.c's socket calls are not being found");

        Assert.All(
            takion,
            c => Assert.True(
                SocketBufferCasts.IsCast(c.Buffer),
                $"takion.c {c.Call}(.., {c.Buffer}, ..) is uncast again"));
    }

    /// <summary>
    /// UNSIGNED IS READ FROM THE DECLARATION, NOT THE NAME.
    ///
    /// The first version of this check kept a list of buffer names and reported http.c's
    /// recv(sock, buf, ..) - whose buf is `char *buf` and draws no warning at all. This tree calls
    /// both kinds "buf", so a rule that guesses the type from the identifier is a rule about naming.
    /// </summary>
    [Theory]
    [InlineData("uint8_t *buf, size_t buf_size", "buf", true)]
    [InlineData("uint8_t *buf = malloc(n);", "buf", true)]
    [InlineData("uint8_t confirm_buf[0x60];", "confirm_buf", true)]
    [InlineData("char *buf, size_t buf_size", "buf", false)]
    [InlineData("const char *src;", "src", false)]
    [InlineData("uint8_t *other;", "buf", false)]
    public void UnsignedIsReadFromTheDeclaration(string code, string name, bool unsigned)
    {
        Assert.Equal(unsigned, SocketBufferCasts.IsDeclaredUnsigned(code, name));
    }

    /// <summary>And the identifier is found past any cast the expression carries.</summary>
    [Theory]
    [InlineData("buf", "buf")]
    [InlineData("(CHIAKI_SOCKET_BUF_TYPE)buf", "buf")]
    [InlineData("(CHIAKI_SOCKET_BUF_TYPE) buf + buf_filled_size", "buf")]
    [InlineData("(CHIAKI_SOCKET_BUF_TYPE)ctrl->recv_buf + ctrl->recv_buf_size", "ctrl")]
    [InlineData("src", "src")]
    public void TheBareNameIsFoundPastACast(string buffer, string expected)
    {
        Assert.Equal(expected, SocketBufferCasts.BareName(buffer));
    }

    /// <summary>And whether one carries the cast.</summary>
    [Theory]
    [InlineData("buf", false)]
    [InlineData("(CHIAKI_SOCKET_BUF_TYPE)buf", true)]
    [InlineData("(const CHIAKI_SOCKET_BUF_TYPE)buf", true)]
    [InlineData("(char *)buf", false)]
    public void ACastIsTheMacroAndNothingElse(string buffer, bool cast)
    {
        // The macro, not a hand-written char* - so the two platforms' answer stays in one place.
        Assert.Equal(cast, SocketBufferCasts.IsCast(buffer));
    }

    /// <summary>
    /// The scan finds the bare calls and not the wrappers named after them.
    ///
    /// sendto, recvfrom, chiaki_takion_send_raw and takion_recv all contain "send" or "recv", and a
    /// scan that matched them would report buffers those functions never hand to a socket.
    /// </summary>
    [Fact]
    public void OnlyTheBareCallsAreFound()
    {
        if (SocketBufferCasts.RepositoryRoot() is not { } root)
            return;

        IReadOnlyList<SocketCall> all = SocketBufferCasts.All(root);

        Assert.All(all, c => Assert.Contains(c.Call, (string[])["send", "recv"]));

        // sendto_broadcast and takion_recv exist in the tree and are not counted as bare calls: if
        // they were, the count would be far higher than the eleven the survey found.
        Assert.True(all.Count < 40, $"{all.Count} calls found, so wrappers are being matched too");
    }

    /// <summary>PP272: and an empty checkout yields nothing rather than a pass about nothing.</summary>
    [Fact]
    public void AnEmptyTreeYieldsNoCalls()
    {
        string root = Path.Combine(
            Path.GetTempPath(), "pportal-sockets-" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(root);

            Assert.Empty(SocketBufferCasts.All(root));
            Assert.Empty(SocketBufferCasts.Uncast(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
