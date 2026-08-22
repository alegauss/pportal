using ChiakiNg.Native;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP26: the session key stream, and the counter that runs the wrong way.
/// </summary>
public class GkKeyStreamTests(ITestOutputHelper output)
{
    /// <summary>
    /// The counter carries UPWARD from byte zero, which is not what CTR usually does.
    ///
    /// Adding one to an all-zero IV touches byte 0 and nothing else. A conventional counter would
    /// touch byte 15. This is the single assertion that separates the two, and everything else in
    /// the stream follows from it.
    /// </summary>
    [Fact]
    public void TheCounterCarriesUpwardFromByteZero()
    {
        byte[] one = GkKeyStream.CounterAdd(new byte[16], 1);

        Assert.Equal(1, one[0]);
        Assert.Equal(0, one[15]);
    }

    /// <summary>And it carries into the next byte rather than saturating.</summary>
    [Fact]
    public void TheCarryReachesTheNextByte()
    {
        var iv = new byte[16];
        iv[0] = 0xff;

        byte[] plus1 = GkKeyStream.CounterAdd(iv, 1);
        Assert.Equal(0, plus1[0]);
        Assert.Equal(1, plus1[1]);

        // ...and all the way along when every byte is full.
        byte[] full = [.. Enumerable.Repeat((byte)0xff, 16)];
        byte[] wrapped = GkKeyStream.CounterAdd(full, 1);
        Assert.All(wrapped, b => Assert.Equal(0, b));
    }

    /// <summary>
    /// Bytes past the carry are the IV unchanged, which is the C's early exit written the long way.
    /// </summary>
    [Fact]
    public void BytesPastTheCarryAreTheIvUnchanged()
    {
        byte[] iv = [.. Enumerable.Range(0, 16).Select(i => (byte)(0x40 + i))];
        byte[] added = GkKeyStream.CounterAdd(iv, 1);

        Assert.Equal(0x41, added[0]);
        for (int i = 1; i < 16; i++)
            Assert.True(iv[i] == added[i], $"byte {i} moved without a carry reaching it");
    }

    /// <summary>
    /// THE COMPARISON. The managed stream is the C's, at several positions and lengths.
    ///
    /// Position matters as much as length: the counter offset is keyPos/16, so a stream asked for
    /// at position zero would agree even with a port that ignored the position entirely.
    /// </summary>
    [Theory]
    [InlineData(0, 16)]
    [InlineData(0, 64)]
    [InlineData(16, 16)]
    [InlineData(160, 32)]
    [InlineData(4096, 128)]
    [InlineData(0x10000, 16)]
    public void TheManagedStreamIsTheCs(ulong keyPos, int length)
    {
        Assert.Equal(ChiakiError.Success, ChiakiSession.LibInit());

        byte[] handshakeKey = [.. Enumerable.Range(0, 16).Select(i => (byte)(i * 11))];
        byte[] secret = [.. Enumerable.Range(0, 32).Select(i => (byte)(i * 7))];

        using var native = new GkCrypt(2, 1, handshakeKey, secret);
        byte[] fromC = native.KeyStream(keyPos, length);

        // The key and IV the C derived, taken from it - this test is about the stream, and the
        // derivation that feeds it is gkcrypt's other half.
        (byte[] keyBase, byte[] iv) = native.KeyAndIv();

        byte[] managed = GkKeyStream.Generate(keyBase, iv, keyPos, length);

        Assert.True(fromC.SequenceEqual(managed),
            $"pos {keyPos} len {length}: C {Convert.ToHexString(fromC)}, managed {Convert.ToHexString(managed)}");

        output.WriteLine($"pos {keyPos} len {length}: {length} bytes agree");
    }

    /// <summary>A partial block is refused rather than rounded, at either argument.</summary>
    [Fact]
    public void PartialBlocksAreRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => GkKeyStream.Generate(new byte[16], new byte[16], 8, 16));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => GkKeyStream.Generate(new byte[16], new byte[16], 0, 24));
    }

    /// <summary>THE DRIFT CHECK. The C still counts upward and still uses ECB over it.</summary>
    [Fact]
    public void TheCStillDoesThis()
    {
        string? impl = SanitizerSource.LocateRelative(@"lib\src\gkcrypt.c");
        Assert.True(impl is not null, "no lib\\src\\gkcrypt.c - this file is describing nothing");

        string core = File.ReadAllText(impl);

        Assert.True(GkKeyStream.TheCounterIsStillLittleEndian(core),
            "counter_add no longer carries upward from byte zero");
        Assert.True(GkKeyStream.TheStreamIsStillEcbOverTheCounter(core),
            "the key stream is no longer AES-128 ECB over the counter");
    }
}
