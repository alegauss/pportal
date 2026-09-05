using System.Buffers;
using System.Security.Cryptography;

using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP26: the session key stream, which is AES-CTR with the counter running the wrong way.
///
/// Once a session is up, every takion payload is XORed against a stream generated from the key
/// derived at the handshake. chiaki_gkcrypt_gen_key_stream builds it a block at a time: add the
/// block index to the IV, encrypt that with AES-128-ECB, and the result is sixteen bytes of stream.
/// That is counter mode, and .NET has no counter mode - so the counter is written out here, which
/// is just as well, because it is not the counter anybody would write.
///
/// counter_add is LITTLE-endian
/// ----------------------------
/// Standard CTR increments the last byte of the block and carries downward. This one starts at byte
/// ZERO and carries upward:
///
///   i = 0; do { r = base[i] + v; out[i] = r; v = r >> 8; i++; } while(i &lt; 16 &amp;&amp; v);
///
/// A port that reached for a conventional counter would produce a stream that is correct for block
/// zero - where the counter is the IV unchanged - and wrong for every block after it. On a stream
/// that is XORed rather than checked, that is a picture that decodes for the first sixteen bytes
/// and is noise from there.
///
/// The early exit is not an optimisation
/// -------------------------------------
/// The loop stops as soon as the carry runs out and copies the rest of the IV across. Same answer
/// as adding zero to every remaining byte, so it can be written either way - but writing it the
/// short way means the loop bound and the copy have to agree, and writing it the long way means
/// they cannot disagree. The long way is here.
/// </summary>
public static class GkKeyStream
{
    /// <summary>CHIAKI_GKCRYPT_BLOCK_SIZE.</summary>
    public const int BlockSize = 0x10;

    /// <summary>
    /// counter_add: the IV plus <paramref name="value"/>, treating the block as little-endian.
    /// </summary>
    public static byte[] CounterAdd(ReadOnlySpan<byte> baseIv, ulong value)
    {
        var output = new byte[BlockSize];
        CounterAdd(baseIv, value, output);

        return output;
    }

    /// <summary>
    /// PP737: the same, into a caller's span.
    ///
    /// The overload that exists so the stream loop does not allocate a block per block. PP44's
    /// budget is the reason it is worth a second signature rather than a comment about reuse.
    /// </summary>
    public static void CounterAdd(ReadOnlySpan<byte> baseIv, ulong value, Span<byte> into)
    {
        if (baseIv.Length != BlockSize)
            throw new ArgumentException($"the IV is {BlockSize} bytes", nameof(baseIv));
        if (into.Length != BlockSize)
            throw new ArgumentException($"a counter block is {BlockSize} bytes", nameof(into));

        ulong carry = value;

        for (var i = 0; i < BlockSize; i++)
        {
            ulong sum = baseIv[i] + carry;
            into[i] = (byte)(sum & 0xff);
            carry = sum >> 8;
        }

        // The carry falling off the top is discarded, exactly as the C's loop bound discards it.
    }

    /// <summary>
    /// The stream for a key position, which must be a whole number of blocks - as must the length.
    /// </summary>
    /// <param name="keyBase">The AES key derived at the handshake.</param>
    /// <param name="iv">The session's IV.</param>
    /// <param name="keyPos">Where in the stream, in bytes.</param>
    /// <param name="length">How much, in bytes.</param>
    public static byte[] Generate(ReadOnlySpan<byte> keyBase, ReadOnlySpan<byte> iv, ulong keyPos, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);

        var stream = new byte[length];
        Generate(keyBase, iv, keyPos, stream);

        return stream;
    }

    /// <summary>
    /// PP737: the stream into a caller's span, which is the shape the packet path needs.
    ///
    /// The byte[] overload above is one allocation per call by signature, and this one is none -
    /// the counter block is a stack span reused for every block, where it used to be a fresh array
    /// each time round the loop. What is LEFT is the Aes: it holds a key schedule and a native
    /// handle, and one is made per call because a crypt that cached it would have something to
    /// release, which is the lifetime PP731 deliberately does not have.
    /// </summary>
    /// <param name="into">Exactly the bytes wanted, a whole number of blocks.</param>
    public static void Generate(
        ReadOnlySpan<byte> keyBase, ReadOnlySpan<byte> iv, ulong keyPos, Span<byte> into)
    {
        if (keyBase.Length != BlockSize)
            throw new ArgumentException($"the key is {BlockSize} bytes", nameof(keyBase));
        if (iv.Length != BlockSize)
            throw new ArgumentException($"the IV is {BlockSize} bytes", nameof(iv));

        // The C asserts both. Answered rather than asserted here: a caller asking for a partial
        // block is a caller with a bug, and a message beats an abort in a release build.
        if (keyPos % BlockSize != 0)
            throw new ArgumentOutOfRangeException(nameof(keyPos), "must be a whole number of blocks");
        if (into.Length % BlockSize != 0)
            throw new ArgumentOutOfRangeException(nameof(into), "must be a whole number of blocks");

        if (into.Length == 0)
            return;

        using Aes aes = Aes.Create();
        aes.Key = keyBase.ToArray();
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;

        // PP737: ONE cipher call for the whole run, not one per block. The measurement is why -
        // a call per block cost 88 bytes each, which is the one-shot API building a transform every
        // time, and it dwarfed the counter array this was first written to avoid. ECB encrypts each
        // block independently with nothing carried between them, so the counters concatenated and
        // encrypted together are byte for byte the blocks encrypted apart.
        //
        // The scratch is pooled, which is what PP44's budget names: rented and returned, so the
        // cost is a rent after the first call rather than a buffer per packet.
        byte[] counters = ArrayPool<byte>.Shared.Rent(into.Length);

        try
        {
            Span<byte> block = counters.AsSpan(0, into.Length);
            ulong counter = keyPos / BlockSize;

            for (var offset = 0; offset < block.Length; offset += BlockSize)
                CounterAdd(iv, counter++, block.Slice(offset, BlockSize));

            aes.EncryptEcb(block, into, PaddingMode.None);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(counters);
        }
    }

    /// <summary>PP26: whether the C's counter still starts at byte zero.</summary>
    public static bool TheCounterIsStillLittleEndian(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        return core.Contains("uint64_t r = base[i] + v;", StringComparison.Ordinal)
            && core.Contains("v = r >> 8;", StringComparison.Ordinal);
    }

    /// <summary>And whether the stream is still AES-128 in ECB over that counter.</summary>
    public static bool TheStreamIsStillEcbOverTheCounter(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        return core.Contains("EVP_aes_128_ecb()", StringComparison.Ordinal)
            && CCall.Happens(core, "counter_add(cur, gkcrypt->iv, counter_offset++)");
    }
}
