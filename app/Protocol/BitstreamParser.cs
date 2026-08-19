using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>What a slice parse learned. Zeroed before every parse, for the reason in the class note.</summary>
public struct BitstreamSlice
{
    public BitstreamSliceType SliceType;
    public uint ReferenceFrame;
}

/// <summary>
/// PP23: the two slice-header parsers, in managed code over the bit reader PP152 checked.
///
/// bitstream.c reads just far enough into an SPS and a slice header to answer two questions - is
/// this an I frame, and which frame does it reference - and everything the video path does about
/// loss rests on those. It is 387 lines of straight-line reads, so it is a transcription, and
/// <see cref="Bitstream"/> is the oracle.
///
/// Four things here are behaviour rather than syntax, and none would be chosen:
///
///   a FAILED header parse zeroes the SPS. chiaki_bitstream_header memsets before parsing, not
///   after succeeding, so a bad header does not leave the previous good one in place - it leaves
///   zeroes, and the next slice parse reads its variable-width fields at the wrong widths;
///
///   the slice struct is never initialised by the C. slice_h264 sets reference_frame only for
///   nal_unit_type 1, so after an I slice the field holds whatever the caller passed in - which is
///   why the C's own test asserts only the type for I slices. The port's seam memsets it to zero
///   first, so that undefined value reads as 0 here; that is the SEAM's convention, stated so
///   nobody mistakes it for the library's;
///
///   the two codecs use different sentinels for "no reference frame found" - H264 sets 0 and H265
///   sets 0xff - so a caller cannot test one value against both;
///
///   and slice_h264's ref_pic_list_modification loop RETURNS TRUE early, on
///   modification_of_pic_nums_idc == 3, which skips the overrun check at the end of the function.
///   A slice whose modification list terminates properly is accepted even if the reader ran off the
///   end getting there.
///
/// The write path - <see cref="SetReferenceFrame"/> - is the one place the bitstream is edited
/// rather than described, and it carries PP69's guard with it.
/// </summary>
public sealed class BitstreamParser
{
    private uint log2MaxFrameNumMinus4;
    private uint log2MaxPicOrderCntLsbMinus4;

    public BitstreamParser(ChiakiCodec codec) => Codec = codec;

    /// <summary>Which codec's syntax to read. H264 is one parser, everything else the H265 one.</summary>
    public ChiakiCodec Codec { get; }

    /// <summary>log2_max_frame_num_minus4 from the last H264 SPS, or zero.</summary>
    public uint Log2MaxFrameNumMinus4 => log2MaxFrameNumMinus4;

    /// <summary>log2_max_pic_order_cnt_lsb_minus4 from the last H265 SPS, or zero.</summary>
    public uint Log2MaxPicOrderCntLsbMinus4 => log2MaxPicOrderCntLsbMinus4;

    /// <summary>
    /// Scan forward to a four-byte start code and consume it.
    ///
    /// At most 64 iterations, and each needs 32 bits left - so a payload with no start code in its
    /// first 64 bytes is refused rather than scanned to the end.
    /// </summary>
    public static bool SkipStartCode(VlVlc vlc)
    {
        ArgumentNullException.ThrowIfNull(vlc);

        vlc.FillBits();
        for (int i = 0; i < 64 && vlc.BitsLeft >= 32; i++)
        {
            if (vlc.PeekBits(32) == 1)
                break;

            vlc.EatBits(8);
            vlc.FillBits();
        }

        if (vlc.PeekBits(32) != 1)
            return false;

        vlc.EatBits(32);
        vlc.FillBits();
        return true;
    }

    /// <summary>
    /// An SPS. Zeroes the stored fields FIRST, so a refusal leaves zeroes rather than the previous
    /// header's values.
    /// </summary>
    public bool ReadHeader(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (Codec == ChiakiCodec.H264)
        {
            log2MaxFrameNumMinus4 = 0;
            return HeaderH264(data);
        }

        log2MaxPicOrderCntLsbMinus4 = 0;
        return HeaderH265(data);
    }

    /// <summary>A slice header. The out value is zeroed first, matching the port's seam.</summary>
    public bool ReadSlice(byte[] data, out BitstreamSlice slice)
    {
        ArgumentNullException.ThrowIfNull(data);

        slice = default;
        if (data.Length == 0)
            return false;

        return Codec == ChiakiCodec.H264 ? SliceH264(data, ref slice) : SliceH265(data, ref slice);
    }

    /// <summary>
    /// Rewrite which frame a P slice references, in place. H264 is refused outright - the C's
    /// dispatcher returns false for it without looking at the data.
    ///
    /// This is the one place the bitstream is EDITED rather than described, and the edit is
    /// positioned by the reader: it rewrites the two 32-bit words ending wherever the reader has
    /// pulled bytes up to, with one bit flipped. Two things put that position somewhere the write
    /// does not belong, and PP69's guard refuses both rather than clamping - a reference frame index
    /// written to the wrong place is a corrupted frame either way:
    ///
    ///   an overrun parse, which since PP68 returns without consuming anything, so the position
    ///   stands still and every remaining iteration would rewrite the same two words;
    ///
    ///   and a slice short enough that fewer than eight bytes have been consumed, which puts the
    ///   lower word before the buffer the caller owns.
    ///
    /// Asked every iteration, because the position moves between them.
    /// </summary>
    /// <param name="data">The slice, edited in place.</param>
    /// <param name="size">How much of <paramref name="data"/> is the slice.</param>
    /// <param name="referenceFrame">The index to mark as used, clearing the others on the way.</param>
    public bool SetReferenceFrame(byte[] data, int size, uint referenceFrame)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (Codec == ChiakiCodec.H264)
            return false;
        if (size <= 0 || size > data.Length)
            return false;

        var vlc = new VlVlc(data, size, 0);
        if (!SkipStartCode(vlc))
            return false;

        vlc.EatBits(1);   // forbidden_zero_bit
        uint nalUnitType = vlc.GetUimsbf(6);
        vlc.EatBits(6);   // nuh_layer_id
        vlc.EatBits(3);   // nuh_temporal_id_plus1

        // Type 20 is accepted by the read path and refused here, which is the two functions
        // disagreeing about what a slice is rather than a simplification.
        if (nalUnitType != 1)
            return false;

        var rbsp = new VlRbsp(vlc);
        uint firstSliceSegmentInPic = rbsp.U(1);

        rbsp.Ue();        // slice_pic_parameter_set_id
        if (firstSliceSegmentInPic == 0)
            rbsp.Ue();    // slice_segment_address

        if (rbsp.Ue() != 1)   // not a P slice
            return false;

        rbsp.U(log2MaxPicOrderCntLsbMinus4 + 4);   // slice_pic_order_cnt_lsb

        if (rbsp.U(1) == 0)   // short_term_ref_pic_set_sps_flag
        {
            uint numNegativePics = rbsp.Ue();
            if (numNegativePics > 16)
                return false;

            rbsp.Ue();    // num_positive_pics
            for (uint i = 0; i < numNegativePics; i++)
            {
                rbsp.Ue();    // delta_poc_s0_minus1[i]

                int pos = rbsp.Nal.At;
                if (rbsp.Overrun || pos < 8 || pos > size)
                    return false;

                // The two words ending at the reader's position, as one 64-bit value: the high word
                // is the four bytes at pos-8 and the low word the four at pos-4, each big-endian.
                ulong hi = ReadBigEndian(data, pos - 8);
                ulong lo = ReadBigEndian(data, pos - 4);
                ulong buffer = lo | (hi << 32);

                // 64 - 1 - (64 - (32 - invalid_bits)) in the C, which is 31 - invalid_bits: the bit
                // the reader is about to read next, counted from the top of that 64-bit window.
                ulong mask = 1UL << (31 - rbsp.Nal.InvalidBits);

                if (i == referenceFrame)
                    buffer |= mask;
                else
                    buffer &= ~mask;

                // Both words are written back every time, even when the bit did not change.
                WriteBigEndian(data, pos - 8, (uint)(buffer >> 32));
                WriteBigEndian(data, pos - 4, (uint)(buffer & 0xffffffff));

                if (i == referenceFrame)
                    return true;

                rbsp.U(1);    // used_by_curr_pic_s0_flag[i]
            }
        }

        // Falling out of the loop, or the set coming from the SPS, is a refusal - there was no
        // flag to move.
        return false;
    }

    private static uint ReadBigEndian(byte[] data, int at)
        => ((uint)data[at] << 24) | ((uint)data[at + 1] << 16) | ((uint)data[at + 2] << 8) | data[at + 3];

    private static void WriteBigEndian(byte[] data, int at, uint value)
    {
        data[at] = (byte)(value >> 24);
        data[at + 1] = (byte)(value >> 16);
        data[at + 2] = (byte)(value >> 8);
        data[at + 3] = (byte)value;
    }

    /// <summary>The profile_idc values that carry the extended chroma fields.</summary>
    private static bool IsHighProfile(uint profileIdc) => profileIdc is
        100 or 110 or 122 or 244 or 44 or 83 or 86 or 118 or 128 or 138 or 139 or 134 or 135;

    private bool HeaderH264(byte[] data)
    {
        var vlc = new VlVlc(data);
        if (!SkipStartCode(vlc))
            return false;

        vlc.EatBits(1);   // forbidden_zero_bit
        vlc.EatBits(2);   // nal_ref_idc
        uint nalUnitType = vlc.GetUimsbf(5);

        if (nalUnitType != 7)
            return false;

        var rbsp = new VlRbsp(vlc);

        uint profileIdc = rbsp.U(8);
        rbsp.U(6);        // constraint_set_flags
        rbsp.U(2);        // reserved_zero_2bits
        rbsp.U(8);        // level_idc
        rbsp.Ue();        // seq_parameter_set_id

        if (IsHighProfile(profileIdc))
        {
            if (rbsp.Ue() == 3)   // chroma_format_idc
                rbsp.U(1);        // separate_colour_plane_flag

            rbsp.Ue();            // bit_depth_luma_minus8
            rbsp.Ue();            // bit_depth_chroma_minus8
            rbsp.U(1);            // qpprime_y_zero_transform_bypass_flag

            // A scaling matrix is refused outright rather than skipped: what follows it is
            // variable-length and this parser does not read it.
            if (rbsp.U(1) != 0)   // seq_scaling_matrix_present_flag
                return false;
        }

        log2MaxFrameNumMinus4 = rbsp.Ue();
        if (log2MaxFrameNumMinus4 > 12)
            return false;

        // Asked last, because it is one flag for the whole parse - any read past the end sets it.
        return !rbsp.Overrun;
    }

    private bool HeaderH265(byte[] data)
    {
        var vlc = new VlVlc(data);

        while (true)
        {
            if (!SkipStartCode(vlc))
                return false;

            vlc.EatBits(1);   // forbidden_zero_bit
            uint nalUnitType = vlc.GetUimsbf(6);
            vlc.EatBits(6);   // nuh_layer_id
            vlc.EatBits(3);   // nuh_temporal_id_plus1

            // A VPS is skipped and the scan restarted, which is the `goto sps_start` in the C -
            // the SPS this wants is behind it in the same payload.
            if (nalUnitType == 32)
                continue;

            if (nalUnitType != 33)
                return false;

            break;
        }

        var rbsp = new VlRbsp(vlc);

        rbsp.U(4);        // sps_video_parameter_set_id
        rbsp.U(3);        // sps_max_sub_layers_minus1
        rbsp.U(1);        // sps_temporal_id_nesting_flag

        rbsp.U(2);        // general_profile_space
        rbsp.U(1);        // general_tier_flag
        rbsp.U(5);        // general_profile_idc
        rbsp.U(32);       // general_profile_compatibility_flag[0-31]
        rbsp.U(1);        // general_progressive_source_flag
        rbsp.U(1);        // general_interlaced_source_flag
        rbsp.U(1);        // general_non_packed_constraint_flag
        rbsp.U(1);        // general_frame_only_constraint_flag
        rbsp.U(32);
        rbsp.U(11);       // general_reserved_zero_43bits
        rbsp.U(1);        // general_inbld_flag
        rbsp.U(8);        // general_level_idc

        rbsp.Ue();        // sps_seq_parameter_set_id
        if (rbsp.Ue() == 3)   // chroma_format_idc
            rbsp.U(1);        // separate_colour_plane_flag

        rbsp.Ue();        // pic_width_in_luma_samples
        rbsp.Ue();        // pic_height_in_luma_samples

        if (rbsp.U(1) != 0)   // conformance_window_flag
        {
            rbsp.Ue();
            rbsp.Ue();
            rbsp.Ue();
            rbsp.Ue();
        }

        rbsp.Ue();        // bit_depth_luma_minus8
        rbsp.Ue();        // bit_depth_chroma_minus8

        log2MaxPicOrderCntLsbMinus4 = rbsp.Ue();
        if (log2MaxPicOrderCntLsbMinus4 > 12)
            return false;

        return !rbsp.Overrun;
    }

    private bool SliceH264(byte[] data, ref BitstreamSlice slice)
    {
        var vlc = new VlVlc(data);
        if (!SkipStartCode(vlc))
            return false;

        vlc.EatBits(1);   // forbidden_zero_bit
        vlc.EatBits(2);   // nal_ref_idc
        uint nalUnitType = vlc.GetUimsbf(5);

        if (nalUnitType != 1 && nalUnitType != 5)
            return false;

        var rbsp = new VlRbsp(vlc);
        rbsp.Ue();        // first_mb_in_slice

        slice.SliceType = rbsp.Ue() switch
        {
            0 or 5 => BitstreamSliceType.P,
            2 or 7 => BitstreamSliceType.I,
            _ => BitstreamSliceType.Unknown,
        };

        if (nalUnitType == 1)
        {
            slice.ReferenceFrame = 0;
            rbsp.Ue();                              // pic_parameter_set_id
            rbsp.U(log2MaxFrameNumMinus4 + 4);      // frame_num

            // Two nested one-bit reads with the same comment in the C, where the syntax has one
            // flag. Reproduced: whatever it is, it is what the bit position downstream depends on.
            if (rbsp.U(1) != 0)
            {
                if (rbsp.U(1) != 0)
                    rbsp.Ue();                      // num_ref_idx_l0_active_minus1
            }

            if (rbsp.U(1) != 0)   // ref_pic_list_modification_flag_l0
            {
                uint i = 0;
                uint idc = rbsp.Ue();
                while (i++ < 3)
                {
                    if (idc == 0)
                    {
                        slice.ReferenceFrame = rbsp.Ue();   // abs_diff_pic_num_minus1
                    }
                    else if (idc < 3)
                    {
                        rbsp.Ue();
                    }
                    else if (idc == 3)
                    {
                        // Returns TRUE without asking about the overrun. A modification list that
                        // terminates properly is accepted even if the reader ran off the end on the
                        // way, which is the one path out of this function that skips the check.
                        return true;
                    }
                    else
                    {
                        break;
                    }

                    idc = rbsp.Ue();
                }

                // Three iterations without a terminator, or an idc above 3: refused.
                return false;
            }
        }

        return !rbsp.Overrun;
    }

    private bool SliceH265(byte[] data, ref BitstreamSlice slice)
    {
        var vlc = new VlVlc(data);
        if (!SkipStartCode(vlc))
            return false;

        vlc.EatBits(1);   // forbidden_zero_bit
        uint nalUnitType = vlc.GetUimsbf(6);
        vlc.EatBits(6);   // nuh_layer_id
        vlc.EatBits(3);   // nuh_temporal_id_plus1

        if (nalUnitType != 1 && nalUnitType != 20)
            return false;

        var rbsp = new VlRbsp(vlc);
        uint firstSliceSegmentInPic = rbsp.U(1);
        if (nalUnitType == 20)
            rbsp.U(1);    // no_output_of_prior_pics_flag

        rbsp.Ue();        // slice_pic_parameter_set_id
        if (firstSliceSegmentInPic == 0)
            rbsp.Ue();    // slice_segment_address

        slice.SliceType = rbsp.Ue() switch
        {
            1 => BitstreamSliceType.P,
            2 => BitstreamSliceType.I,
            _ => BitstreamSliceType.Unknown,
        };

        if (nalUnitType == 1)
        {
            // 0xff and not 0: the H264 parser uses zero for the same "not found", so the two
            // sentinels differ and a caller cannot test one against both.
            slice.ReferenceFrame = 0xff;
            rbsp.U(log2MaxPicOrderCntLsbMinus4 + 4);   // slice_pic_order_cnt_lsb

            if (rbsp.U(1) == 0)   // short_term_ref_pic_set_sps_flag
            {
                uint numNegativePics = rbsp.Ue();
                if (numNegativePics > 16)
                    return false;

                rbsp.Ue();        // num_positive_pics
                for (uint i = 0; i < numNegativePics; i++)
                {
                    rbsp.Ue();    // delta_poc_s0_minus1[i]
                    if (rbsp.U(1) != 0)   // used_by_curr_pic_s0_flag[i]
                    {
                        slice.ReferenceFrame = i;
                        break;
                    }
                }
            }
        }

        return !rbsp.Overrun;
    }
}

