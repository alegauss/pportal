namespace ChiakiNg.Protocol;

/// <summary>
/// PP23: vl_vlc, the bit buffer both slice-header parsers read through, in managed code.
///
/// A 64-bit shift register with a signed count of how many of its top bits are NOT yet valid. Every
/// read is a shift off the top; every fill ORs bytes in at the bottom of the valid region. The
/// arithmetic is small and the two places it goes wrong are both silent - see
/// <see cref="ValidBits"/> and <see cref="Alignment"/>.
///
/// Transcribed from lib/src/vl_rbsp.h rather than implemented from a description of a bit reader,
/// with <see cref="NativeRbsp"/> kept as the oracle. It is header-only C, so nothing links it and
/// nothing would notice a disagreement.
/// </summary>
public sealed class VlVlc
{
    private readonly byte[] data;

    private ulong buffer;
    private int invalidBits;
    private int at;
    private int end;
    private uint bytesLeft;

    /// <param name="data">The payload. Not copied - read in place, as the C does.</param>
    /// <param name="alignment">
    /// The low two bits of the address the C would have had this payload at.
    ///
    /// vl_vlc_align_data_ptr consumes bytes ONE AT A TIME until the data pointer is dword-aligned,
    /// and only then does fillbits read whole dwords - so after init the buffer holds 32 bits of
    /// valid data at alignment 0 and 56 at alignment 1, for the same payload. That number bounds
    /// vl_rbsp_init's emulation-prevention scan, so the alignment is part of the input rather than
    /// an implementation detail, and it is a parameter here so the two implementations can be
    /// compared at each of the four.
    /// </param>
    public VlVlc(byte[] data, int alignment = 0)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentOutOfRangeException.ThrowIfNegative(alignment);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(alignment, 3);

        this.data = data;
        Alignment = alignment;

        buffer = 0;
        invalidBits = 32;
        at = 0;
        end = data.Length;
        bytesLeft = (uint)data.Length;

        AlignDataPtr();
        FillBits();
    }

    private VlVlc(VlVlc other)
    {
        data = other.data;
        Alignment = other.Alignment;
        buffer = other.buffer;
        invalidBits = other.invalidBits;
        at = other.at;
        end = other.end;
        bytesLeft = other.bytesLeft;
    }

    /// <summary>The address alignment being emulated.</summary>
    public int Alignment { get; }

    /// <summary>A copy of the position, which is what vl_rbsp_init takes.</summary>
    public VlVlc Clone() => new(this);

    /// <summary>
    /// How many of the buffer's bits are valid, CLAMPED AT ZERO.
    ///
    /// invalidBits is signed and climbs by 8 on every eat, with nothing stopping it at the end of
    /// the payload. Once it passes 32 the obvious `32 - invalidBits` is negative, and read as
    /// unsigned it is about four billion - "plenty of bits left", returned at exactly the moment
    /// there are none. Every loop conditioned on it then never ends. The clamp is PP70's.
    /// </summary>
    public uint ValidBits => invalidBits >= 32 ? 0u : (uint)(32 - invalidBits);

    /// <summary>The signed count, which <see cref="VlRbsp.HasBits"/> has to ask on directly.</summary>
    public int InvalidBits => invalidBits;

    /// <summary>Bits left across the buffer and the remaining payload.</summary>
    public uint BitsLeft
    {
        get
        {
            int remaining = end - at;
            remaining += (int)bytesLeft;
            return (uint)((remaining * 8) + (int)ValidBits);
        }
    }

    /// <summary>Whether the payload is exhausted, which is what licenses a peek past the end.</summary>
    public bool Depleted => at >= end;

    /// <summary>
    /// The byte-at-a-time fill that runs until the data pointer is dword-aligned.
    ///
    /// Emulated from <see cref="Alignment"/>: the C's loop runs while `address &amp; 3`, so it
    /// consumes `(4 - alignment) % 4`... no - it consumes until the low bits are zero, which from
    /// alignment a is `(4 - a) % 4` bytes. At alignment 0 it consumes none.
    /// </summary>
    private void AlignDataPtr()
    {
        int toConsume = (4 - Alignment) % 4;
        for (int i = 0; i < toConsume && at != end; i++)
        {
            buffer |= (ulong)data[at] << (24 + invalidBits);
            at++;
            invalidBits -= 8;
        }
    }

    /// <summary>Fill until at least 32 bits are valid, or the payload runs out.</summary>
    public void FillBits()
    {
        while (invalidBits > 0)
        {
            int remaining = end - at;
            if (remaining == 0)
                return;

            if (remaining >= 4)
            {
                // ntohl of a dword: the payload is big-endian on the wire, so the four bytes go in
                // most significant first. Written as four reads rather than a cast, because a cast
                // would depend on the host's endianness where the C's ntohl already does not.
                ulong value = ((ulong)data[at] << 24)
                    | ((ulong)data[at + 1] << 16)
                    | ((ulong)data[at + 2] << 8)
                    | data[at + 3];

                buffer |= value << invalidBits;
                at += 4;
                invalidBits -= 32;
                break;
            }

            while (at < end)
            {
                buffer |= (ulong)data[at] << (24 + invalidBits);
                at++;
                invalidBits -= 8;
            }
        }
    }

    /// <summary>n bits from the top, without consuming them.</summary>
    public uint PeekBits(uint n) => n == 0 ? 0u : (uint)(buffer >> (64 - (int)n));

    /// <summary>Consume n bits.</summary>
    public void EatBits(uint n)
    {
        buffer <<= (int)n;
        invalidBits += (int)n;
    }

    /// <summary>n bits from the top, consumed.</summary>
    public uint GetUimsbf(uint n)
    {
        uint value = PeekBits(n);
        EatBits(n);
        return value;
    }

    /// <summary>Fast-forward to a byte value. num_bits of ~0 means "no limit".</summary>
    public bool SearchByte(uint numBits, byte value)
    {
        while (ValidBits > 0)
        {
            if (PeekBits(8) == value)
            {
                FillBits();
                return true;
            }

            EatBits(8);

            if (numBits != uint.MaxValue)
            {
                numBits -= 8;
                if (numBits == 0)
                    return false;
            }
        }

        while (true)
        {
            if (at == end)
                return false;

            if (data[at] == value)
            {
                AlignDataPtr();
                FillBits();
                return true;
            }

            at++;
            if (numBits != uint.MaxValue)
            {
                numBits -= 8;
                if (numBits == 0)
                {
                    AlignDataPtr();
                    return false;
                }
            }
        }
    }

    /// <summary>Splice num_bits out of the buffer at pos, closing the gap.</summary>
    public void RemoveBits(uint pos, uint numBits)
    {
        ulong lo = (buffer & (ulong.MaxValue >> (int)(pos + numBits))) << (int)numBits;
        ulong hi = buffer & (ulong.MaxValue << (int)(64 - pos));
        buffer = lo | hi;
        invalidBits += (int)numBits;
    }

    /// <summary>Cap how many bits may still be fetched.</summary>
    public void Limit(uint bitsLeftLimit)
    {
        FillBits();
        if (bitsLeftLimit < ValidBits)
        {
            invalidBits = (int)(32 - bitsLeftLimit);

            // ~0L << (invalid + 32) in the C: a SIGNED shift, so the vacated low bits are ones on
            // the left of the mask and the buffer keeps only its top bitsLeft. Written unsigned
            // here with the same result, because a shift count of 64 is what a mask of zero means
            // and C++'s undefined behaviour there is not something to reproduce.
            int shift = invalidBits + 32;
            buffer &= shift >= 64 ? 0UL : ulong.MaxValue << shift;
            end = at;
            bytesLeft = 0;
        }
        else
        {
            bytesLeft = (bitsLeftLimit - ValidBits) / 8;
            if (bytesLeft < (uint)(end - at))
            {
                end = at + (int)bytesLeft;
                bytesLeft = 0;
            }
            else
            {
                bytesLeft -= (uint)(end - at);
            }
        }
    }
}

/// <summary>
/// PP23: vl_rbsp, the RBSP reader over <see cref="VlVlc"/>.
///
/// RBSP is a NAL with its emulation-prevention bytes taken out: a 0x03 that follows two zero bytes
/// is an escape and not data. This does that by SPLICING the byte out of the bit buffer as it fills,
/// which is why the fill path and the escape scan are the same function and why `escaped` has to be
/// carried between calls.
///
/// The two exits PP68 added are transcribed with it. Upstream's ue(v) loop had no way out but
/// reading a 1 bit, and past the end of the NAL the buffer yields zeroes for ever - so a truncated
/// header did not fail to parse, it never returned. The cap at 32 is also a correctness bound:
/// `1 &lt;&lt; bits` is undefined past 31.
/// </summary>
public sealed class VlRbsp
{
    private readonly VlVlc nal;
    private uint escaped;
    private uint removed;

    /// <param name="source">The vlc to read from. Its position is COPIED, as the C does.</param>
    /// <param name="numBits">The search limit for the end of the NAL; uint.MaxValue for none.</param>
    public VlRbsp(VlVlc source, uint numBits = uint.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(source);

        uint bitsLeft = source.BitsLeft;
        nal = source.Clone();

        escaped = 0;
        removed = 0;
        Overrun = false;

        // Find the end of the NAL: the next start code, if there is one inside the limit.
        while (source.SearchByte(numBits, 0x00))
        {
            if (source.PeekBits(24) == 0x000001 || source.PeekBits(32) == 0x00000001)
            {
                nal.Limit(bitsLeft - source.BitsLeft);
                break;
            }

            source.EatBits(8);
        }

        // The emulation-prevention scan, bounded by however many bits happen to be valid - which is
        // where the payload's address alignment reaches the output, if it reaches it at all.
        uint valid = nal.ValidBits;
        for (uint i = 24; i <= valid; i += 8)
        {
            if ((nal.PeekBits(i) & 0xffffff) == 0x3)
            {
                nal.RemoveBits(i - 8, 8);
                i += 8;
            }
        }

        valid = nal.ValidBits;
        escaped = valid >= 16 ? 16u : (valid >= 8 ? 8u : 0u);
    }

    /// <summary>
    /// Whether any read has been attempted with nothing left, or a ue(v) prefix ran too long. A
    /// parse ending with this set has been reading zeroes off the end and its output means nothing.
    /// </summary>
    public bool Overrun { get; private set; }

    /// <summary>How many escape bytes have been spliced out so far.</summary>
    public uint Removed => removed;

    /// <summary>The reader's own bit buffer, for the assertions that compare its state.</summary>
    public VlVlc Nal => nal;

    /// <summary>
    /// Whether n more bits can be read.
    ///
    /// Asked on the SIGNED invalidBits rather than through ValidBits: once a read has gone past the
    /// end invalidBits climbs above 32, and `ValidBits >= n` on the clamped-to-zero value would
    /// answer correctly while `32 - invalidBits` as unsigned would not. The C asks the signed one,
    /// and so does this.
    /// </summary>
    public bool HasBits(uint n) => nal.InvalidBits <= (int)(32 - n);

    /// <summary>Make at least 16 more bits available, splicing out any escape byte on the way.</summary>
    public void FillBits()
    {
        uint valid = nal.ValidBits;
        if (valid >= 32)
            return;

        nal.FillBits();

        if (nal.BitsLeft < 24)
            return;

        valid -= escaped;

        escaped = 16;
        uint bits = nal.ValidBits;
        for (uint i = valid + 24; i <= bits; i += 8)
        {
            if ((nal.PeekBits(i) & 0xffffff) == 0x3)
            {
                nal.RemoveBits(i - 8, 8);
                escaped = bits - i;
                bits -= 8;
                removed += 8;
                i += 8;
            }
        }
    }

    /// <summary>An unsigned integer from the next n bits. Zero, and an overrun, where there are not n.</summary>
    public uint U(uint n)
    {
        if (n == 0)
            return 0;

        FillBits();
        if (n > 16)
            FillBits();
        if (!HasBits(n))
        {
            Overrun = true;
            return 0;
        }

        return nal.GetUimsbf(n);
    }

    /// <summary>An unsigned exp-Golomb integer, with both of PP68's exits.</summary>
    public uint Ue()
    {
        uint bits = 0;

        FillBits();
        while (true)
        {
            if (!HasBits(1))
            {
                Overrun = true;
                return 0;
            }

            if (nal.GetUimsbf(1) != 0)
                break;

            if (++bits >= 32)
            {
                Overrun = true;
                return 0;
            }

            FillBits();
        }

        return (uint)((1u << (int)bits) - 1 + U(bits));
    }

    /// <summary>A signed exp-Golomb integer.</summary>
    public int Se()
    {
        int codeNum = (int)Ue();
        return (codeNum & 1) != 0 ? (codeNum + 1) >> 1 : -(codeNum >> 1);
    }

    /// <summary>
    /// Whether more data follows. The trailing-bits pattern is a single 1 followed by zeroes, so a
    /// buffer holding only that - or only zeroes - is the end.
    /// </summary>
    public bool MoreData()
    {
        if (nal.BitsLeft > 8)
            return true;

        uint bits = nal.ValidBits;
        uint value = nal.PeekBits(bits);
        return value != 0 && value != (1u << (int)(bits - 1));
    }
}
