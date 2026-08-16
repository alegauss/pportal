// SPDX-License-Identifier: LicenseRef-AGPL-3.0-only-OpenSSL

#include <munit.h>

#include <chiaki/bitstream.h>
#include <stdio.h>

#include "test_log.h"

#define ARRAY_SIZE(a) sizeof(a) / sizeof(a[0])

static MunitResult test_bitstream_parse_h264(const MunitParameter params[], void *fixture)
{
	ChiakiBitstream bs;
	ChiakiBitstreamSlice slice;

	chiaki_bitstream_init(&bs, NULL, CHIAKI_CODEC_H264);

	uint8_t header[] = {
		0x00, 0x00, 0x00, 0x01, 0x67, 0x4d, 0x40, 0x32, 0x91, 0x8a, 0x01, 0xe0, 0x08, 0x9f, 0x97, 0x01,
		0x6a, 0x02, 0x02, 0x02, 0x80, 0x00, 0x03, 0xe9, 0x00, 0x01, 0xd4, 0xc0, 0x44, 0xd0, 0xf1, 0xf1,
		0x50, 0x00, 0x00, 0x00, 0x01, 0x68, 0xee, 0x3c, 0x80,
	};
	memset(&bs.h264, -1, sizeof(bs.h264));
	munit_assert(chiaki_bitstream_header(&bs, header, ARRAY_SIZE(header)));
	munit_assert(bs.h264.sps.log2_max_frame_num_minus4 == 3);

	uint8_t slice_i[] = {
		0x00, 0x00, 0x00, 0x01, 0x65, 0x88, 0x80, 0x82, 0x1f, 0x00, 0x49, 0xee, 0x03, 0x29, 0xff, 0xf8,
		0x7f, 0x88, 0x46, 0x44, 0x77, 0x17, 0xe7, 0x6d, 0xb3, 0xad, 0x38, 0x19, 0x74, 0x5a, 0xf1, 0x51,
	};
	memset(&slice, -1, sizeof(slice));
	munit_assert(chiaki_bitstream_slice(&bs, slice_i, ARRAY_SIZE(slice_i), &slice));
	munit_assert(slice.slice_type == CHIAKI_BITSTREAM_SLICE_I);

	uint8_t slice_p[] = {
		0x00, 0x00, 0x00, 0x01, 0x41, 0x9a, 0x04, 0x44, 0x3f, 0x41, 0x5b, 0xf4, 0x65, 0xb4, 0x3e, 0x1a,
		0xd3, 0xa0, 0x28, 0x1f, 0x83, 0x63, 0x0e, 0xc2, 0xfc, 0x9d, 0x7a, 0xc7, 0xc4, 0x7d, 0xf9, 0x18,
	};
	memset(&slice, -1, sizeof(slice));
	munit_assert(chiaki_bitstream_slice(&bs, slice_p, ARRAY_SIZE(slice_p), &slice));
	munit_assert(slice.slice_type == CHIAKI_BITSTREAM_SLICE_P);
	munit_assert(slice.reference_frame == 0);

	uint8_t slice_p_ref_5[] = {
		0x00, 0x00, 0x00, 0x01, 0x41, 0x9b, 0xfd, 0x98, 0x89, 0xdf, 0x00, 0x03, 0x24, 0x60, 0x47, 0x1a,
		0x90, 0x10, 0xb3, 0x2c, 0x4e, 0x45, 0xfc, 0xff, 0x45, 0x24, 0x8c, 0x79, 0xec, 0x12, 0xe5, 0x9b,
	};
	memset(&slice, -1, sizeof(slice));
	munit_assert(chiaki_bitstream_slice(&bs, slice_p_ref_5, ARRAY_SIZE(slice_p_ref_5), &slice));
	munit_assert(slice.slice_type == CHIAKI_BITSTREAM_SLICE_P);
	munit_assert(slice.reference_frame == 5);

	return MUNIT_OK;
}

static MunitResult test_bitstream_parse_h265(const MunitParameter params[], void *fixture)
{
	ChiakiBitstream bs;
	ChiakiBitstreamSlice slice;

	chiaki_bitstream_init(&bs, NULL, CHIAKI_CODEC_H265);

	uint8_t header[] = {
		0x00, 0x00, 0x00, 0x01, 0x40, 0x01, 0x0c, 0x01, 0xff, 0xff, 0x01, 0x60, 0x00, 0x00, 0x03, 0x00,
		0xb0, 0x00, 0x00, 0x03, 0x00, 0x00, 0x03, 0x00, 0x96, 0x0a, 0xc0, 0x90, 0x00, 0x00, 0x00, 0x01,
		0x42, 0x01, 0x01, 0x01, 0x60, 0x00, 0x00, 0x03, 0x00, 0xb0, 0x00, 0x00, 0x03, 0x00, 0x00, 0x03,
		0x00, 0x96, 0xa0, 0x03, 0xc0, 0x80, 0x11, 0x07, 0xcb, 0xc2, 0xb9, 0x24, 0x29, 0x52, 0x70, 0x16,
		0xa0, 0x20, 0x20, 0x20, 0x80, 0x00, 0x07, 0xd2, 0x00, 0x01, 0xd4, 0xc0, 0x20, 0xe5, 0xa1, 0xe3,
		0xd0, 0x00, 0x00, 0x00, 0x01, 0x44, 0x01, 0xc0, 0xf3, 0xc0, 0x4c, 0x90,
	};
	memset(&bs.h265, -1, sizeof(bs.h265));
	munit_assert(chiaki_bitstream_header(&bs, header, ARRAY_SIZE(header)));
	munit_assert(bs.h265.sps.log2_max_pic_order_cnt_lsb_minus4 == 0);

	uint8_t slice_i[] = {
		0x00, 0x00, 0x00, 0x01, 0x28, 0x01, 0xac, 0x25, 0xcf, 0x83, 0xff, 0x23, 0x54, 0xab, 0x5c, 0xf5,
		0x7a, 0x06, 0x7c, 0x3f, 0x31, 0x9b, 0xe6, 0x10, 0x57, 0xe8, 0x0e, 0xcf, 0xdd, 0xda, 0xdb, 0x3f,
	};
	memset(&slice, -1, sizeof(slice));
	munit_assert(chiaki_bitstream_slice(&bs, slice_i, ARRAY_SIZE(slice_i), &slice));
	munit_assert(slice.slice_type == CHIAKI_BITSTREAM_SLICE_I);

	uint8_t slice_p[] = {
		0x00, 0x00, 0x00, 0x01, 0x02, 0x01, 0xd0, 0x97, 0x61, 0x28, 0x23, 0x2d, 0x8b, 0x80, 0x6f, 0xfd,
		0x2f, 0x2b, 0x11, 0xd4, 0x55, 0x04, 0x90, 0x18, 0x49, 0xe5, 0xbc, 0xc4, 0x97, 0xbc, 0x3d, 0xeb,
	};
	memset(&slice, -1, sizeof(slice));
	munit_assert(chiaki_bitstream_slice(&bs, slice_p, ARRAY_SIZE(slice_p), &slice));
	munit_assert(slice.slice_type == CHIAKI_BITSTREAM_SLICE_P);
	munit_assert(slice.reference_frame == 0);

	uint8_t slice_p_ref_5[] = {
		0x00, 0x00, 0x00, 0x01, 0x02, 0x01, 0xd7, 0x85, 0x6a, 0xae, 0xa6, 0x11, 0x80, 0x95, 0x80, 0x0a,
		0xec, 0x5e, 0xdf, 0x39, 0x86, 0xe6, 0xd9, 0x07, 0x49, 0x17, 0xe2, 0x62, 0x57, 0x14, 0xd7, 0x08,
	};
	memset(&slice, -1, sizeof(slice));
	munit_assert(chiaki_bitstream_slice(&bs, slice_p_ref_5, ARRAY_SIZE(slice_p_ref_5), &slice));
	munit_assert(slice.slice_type == CHIAKI_BITSTREAM_SLICE_P);
	munit_assert(slice.reference_frame == 5);

	return MUNIT_OK;
}

static MunitResult test_bitstream_issue_213(const MunitParameter params[], void *fixture)
{
	ChiakiBitstream bs;
	ChiakiBitstreamSlice slice;

	chiaki_bitstream_init(&bs, NULL, CHIAKI_CODEC_H265);

	uint8_t header[] = {
		0x00, 0x00, 0x00, 0x01, 0x40, 0x01, 0x0c, 0x01, 0xff, 0xff, 0x01, 0x60, 0x00, 0x00, 0x03, 0x00,
		0xb0, 0x00, 0x00, 0x03, 0x00, 0x00, 0x03, 0x00, 0x96, 0x0a, 0xc0, 0x90, 0x00, 0x00, 0x00, 0x01,
		0x42, 0x01, 0x01, 0x01, 0x60, 0x00, 0x00, 0x03, 0x00, 0xb0, 0x00, 0x00, 0x03, 0x00, 0x00, 0x03,
		0x00, 0x96, 0xa0, 0x03, 0xc0, 0x80, 0x11, 0x07, 0xcb, 0xc2, 0xb9, 0x24, 0x29, 0x52, 0x70, 0x16,
		0xa0, 0x20, 0x20, 0x20, 0x80, 0x00, 0x07, 0xd2, 0x00, 0x01, 0xd4, 0xc0, 0x20, 0xe5, 0xa1, 0xe3,
		0xd0, 0x00, 0x00, 0x00, 0x01, 0x44, 0x01, 0xc0, 0xf3, 0xc0, 0x4c, 0x90,
	};
	munit_assert(chiaki_bitstream_header(&bs, header, ARRAY_SIZE(header)));

	uint8_t slice_p[] = {
		0x00, 0x00, 0x00, 0x01, 0x02, 0x01, 0xd2, 0x0b, 0xea, 0x60, 0x86, 0x82, 0x3d, 0x00, 0x00, 0x03,
	};
	memset(&slice, -1, sizeof(slice));
	munit_assert(chiaki_bitstream_slice(&bs, slice_p, ARRAY_SIZE(slice_p), &slice));
	munit_assert(slice.slice_type == CHIAKI_BITSTREAM_SLICE_P);
	munit_assert(slice.reference_frame == 0);

	return MUNIT_OK;
}

static MunitResult test_bitstream_set_ref_h265(const MunitParameter params[], void *fixture)
{
	ChiakiBitstream bs;
	ChiakiBitstreamSlice slice;

	chiaki_bitstream_init(&bs, NULL, CHIAKI_CODEC_H265);

	uint8_t header[] = {
		0x00, 0x00, 0x00, 0x01, 0x40, 0x01, 0x0c, 0x01, 0xff, 0xff, 0x01, 0x60, 0x00, 0x00, 0x03, 0x00,
		0xb0, 0x00, 0x00, 0x03, 0x00, 0x00, 0x03, 0x00, 0x96, 0x0a, 0xc0, 0x90, 0x00, 0x00, 0x00, 0x01,
		0x42, 0x01, 0x01, 0x01, 0x60, 0x00, 0x00, 0x03, 0x00, 0xb0, 0x00, 0x00, 0x03, 0x00, 0x00, 0x03,
		0x00, 0x96, 0xa0, 0x03, 0xc0, 0x80, 0x11, 0x07, 0xcb, 0xc2, 0xb9, 0x24, 0x29, 0x52, 0x70, 0x16,
		0xa0, 0x20, 0x20, 0x20, 0x80, 0x00, 0x07, 0xd2, 0x00, 0x01, 0xd4, 0xc0, 0x20, 0xe5, 0xa1, 0xe3,
		0xd0, 0x00, 0x00, 0x00, 0x01, 0x44, 0x01, 0xc0, 0xf3, 0xc0, 0x4c, 0x90,
	};
	memset(&bs.h265, -1, sizeof(bs.h265));
	munit_assert(chiaki_bitstream_header(&bs, header, ARRAY_SIZE(header)));
	munit_assert(bs.h265.sps.log2_max_pic_order_cnt_lsb_minus4 == 0);

	uint8_t slice_p[] = {
		0x00, 0x00, 0x00, 0x01, 0x02, 0x01, 0xd2, 0x85, 0x7a, 0xaa, 0xa6, 0x08, 0x60, 0x13, 0x55, 0x17,
		0x6b, 0x71, 0x72, 0xf9, 0x6e, 0xd4, 0xf2, 0x66, 0x78, 0x0c, 0x12, 0xe7, 0x79, 0xf0, 0xbc, 0xc9,
	};
	memset(&slice, -1, sizeof(slice));
	munit_assert(chiaki_bitstream_slice(&bs, slice_p, ARRAY_SIZE(slice_p), &slice));
	munit_assert(slice.slice_type == CHIAKI_BITSTREAM_SLICE_P);
	munit_assert(slice.reference_frame == 0);

	for(unsigned i=0; i<9; i++)
	{
		munit_assert(chiaki_bitstream_slice_set_reference_frame(&bs, slice_p, ARRAY_SIZE(slice_p), i));
		memset(&slice, -1, sizeof(slice));
		munit_assert(chiaki_bitstream_slice(&bs, slice_p, ARRAY_SIZE(slice_p), &slice));
		munit_assert(slice.slice_type == CHIAKI_BITSTREAM_SLICE_P);
		munit_assert(slice.reference_frame == i);
	}
	// Slice have 9 reference frames
	munit_assert(!chiaki_bitstream_slice_set_reference_frame(&bs, slice_p, ARRAY_SIZE(slice_p), 10));

	return MUNIT_OK;
}

/**
 * PP68: a header that ends in the middle of the parse is refused rather than chased off the end.
 *
 * Before the fix this test could not be written at all: vl_rbsp_ue looped until it read a 1 bit,
 * the exhausted bit buffer yielded zeroes for ever, and the call never returned. It did not fail
 * the suite, it hung it - which is why the case had to be measured with an external probe and a
 * 20-second timeout before it could be held here.
 *
 * The inputs are truncations rather than corruptions on purpose. profile_idc 0x42 (66, Baseline)
 * skips the chroma block, so the parse walks straight to the ue(v) fields with nothing left to
 * read them from - the shortest path to the loop that used to hang.
 */
static MunitResult test_bitstream_truncated_header_h264(const MunitParameter params[], void *fixture)
{
	(void)params; (void)fixture;
	ChiakiBitstream bs;
	chiaki_bitstream_init(&bs, NULL, CHIAKI_CODEC_H264);

	// Startcode, NAL type 7, and three bytes where a whole SPS should be.
	uint8_t truncated[] = { 0x00, 0x00, 0x00, 0x01, 0x67, 0x42, 0x00, 0x1e };
	munit_assert_false(chiaki_bitstream_header(&bs, truncated, ARRAY_SIZE(truncated)));

	// The startcode and the NAL header alone, with no payload behind them at all.
	uint8_t header_only[] = { 0x00, 0x00, 0x00, 0x01, 0x67 };
	munit_assert_false(chiaki_bitstream_header(&bs, header_only, ARRAY_SIZE(header_only)));

	return MUNIT_OK;
}

/** The same for a slice: NAL type 5 with nothing behind it is refused, not chased. */
static MunitResult test_bitstream_truncated_slice_h264(const MunitParameter params[], void *fixture)
{
	(void)params; (void)fixture;
	ChiakiBitstream bs;
	ChiakiBitstreamSlice slice;
	chiaki_bitstream_init(&bs, NULL, CHIAKI_CODEC_H264);

	uint8_t truncated[] = { 0x00, 0x00, 0x00, 0x01, 0x65 };
	memset(&slice, 0, sizeof(slice));
	munit_assert_false(chiaki_bitstream_slice(&bs, truncated, ARRAY_SIZE(truncated), &slice));

	return MUNIT_OK;
}

/**
 * PP70: an exhausted reader reports no bits left, so the loops that depend on that terminate.
 *
 * Five bytes at a four-byte aligned address: a startcode and one NAL header byte, and nothing
 * behind it. The parse gets past the NAL type check and into vl_rbsp_init, whose search for the
 * end of the NAL used to "find" a zero byte in the empty bit buffer for ever.
 *
 * Like PP68's tests, this one could not be written before the fix - it would have hung the suite
 * rather than failed it. The alignment is forced rather than hoped for, because vl_vlc_align_data_ptr
 * makes how far a prefix parses depend on the address it is given, and the hang was only reachable
 * at one of the four.
 */
static MunitResult test_bitstream_exhausted_reader_terminates(const MunitParameter params[], void *fixture)
{
	(void)params; (void)fixture;

	static const uint8_t truncated[] = { 0x00, 0x00, 0x00, 0x01, 0x02 };

	for(unsigned off = 0; off < 4; off++)
	{
		uint8_t arena[4 + ARRAY_SIZE(truncated)];
		uint8_t *slice;
		ChiakiBitstream bs;

		memset(arena, 0, sizeof(arena));
		// Walk to the next 4-byte boundary from the arena's own address, then step off it by
		// `off`, so all four alignments are covered whatever the arena landed on.
		slice = arena + ((4 - ((uintptr_t)arena & 3)) & 3);
		slice += off;
		if(slice + ARRAY_SIZE(truncated) > arena + sizeof(arena))
			continue;
		memcpy(slice, truncated, ARRAY_SIZE(truncated));

		chiaki_bitstream_init(&bs, get_test_log(), CHIAKI_CODEC_H265);
		memset(&bs.h265, 0, sizeof(bs.h265));

		// The assertion is that this returns at all. The value is incidental: no prefix this
		// short is a slice anyone can set a reference frame on.
		munit_assert_false(chiaki_bitstream_slice_set_reference_frame(&bs, slice, ARRAY_SIZE(truncated), 0));
	}

	return MUNIT_OK;
}

/**
 * PP69: the one function that writes to the caller's buffer refuses when the parse that chose
 * where to write ran out of input.
 *
 * Two assertions with different jobs, and the difference between them was measured rather than
 * assumed. Every truncation of a real P slice was run at every alignment, with and without the
 * guard, and the two runs differ at exactly one length.
 *
 * The loop is a guard-rail: no byte outside the slice is ever touched. That already held without
 * the guard, so it fails nothing today - it is here to keep holding, since vl_vlc_align_data_ptr
 * makes how far a prefix parses depend on the address the caller's buffer happens to have, and a
 * length that is comfortable at one alignment need not be at another.
 *
 * The eight-byte case is the assertion. There the parse overruns, and without the guard the
 * function returns true and edits a byte picked out of a parse that had run out of input - a
 * wrong reference frame index written into a real frame, reported as success.
 */
static MunitResult test_bitstream_set_ref_h265_truncated(const MunitParameter params[], void *fixture)
{
	(void)params; (void)fixture;

	// The P slice test_bitstream_set_ref_h265 rewrites nine times, in every prefix of itself.
	static const uint8_t full[] = {
		0x00, 0x00, 0x00, 0x01, 0x02, 0x01, 0xd2, 0x85, 0x7a, 0xaa, 0xa6, 0x08, 0x60, 0x13, 0x55, 0x17,
		0x6b, 0x71, 0x72, 0xf9, 0x6e, 0xd4, 0xf2, 0x66, 0x78, 0x0c, 0x12, 0xe7, 0x79, 0xf0, 0xbc, 0xc9,
	};
	const unsigned pad = 16;

	for(unsigned n = 1; n <= ARRAY_SIZE(full); n++)
	{
		for(unsigned off = 0; off < 4; off++)
		{
			uint8_t arena[16 + 4 + ARRAY_SIZE(full) + 16];
			memset(arena, 0xa5, sizeof(arena));
			uint8_t *slice = arena + pad + off;
			memcpy(slice, full, n);

			ChiakiBitstream bs;
			// The quiet log, not NULL: every prefix here is meant to be refused, and 128
			// refusals each printing a warning would bury the result of the run.
			chiaki_bitstream_init(&bs, get_test_log(), CHIAKI_CODEC_H265);
			memset(&bs.h265, 0, sizeof(bs.h265));

			// The return value is not asserted: whether a given prefix is a legal P slice is
			// not this test's business. Where the writes land is.
			chiaki_bitstream_slice_set_reference_frame(&bs, slice, n, 0);

			for(unsigned i = 0; i < pad + off; i++)
				munit_assert_uint8(arena[i], ==, 0xa5);
			for(unsigned i = pad + off + n; i < sizeof(arena); i++)
				munit_assert_uint8(arena[i], ==, 0xa5);
		}
	}

	// Eight bytes: the one length where the guard changes anything, at every alignment.
	for(unsigned off = 0; off < 4; off++)
	{
		uint8_t arena[16 + 4 + ARRAY_SIZE(full) + 16];
		ChiakiBitstream bs;

		memset(arena, 0xa5, sizeof(arena));
		uint8_t *slice = arena + pad + off;
		memcpy(slice, full, 8);

		chiaki_bitstream_init(&bs, get_test_log(), CHIAKI_CODEC_H265);
		memset(&bs.h265, 0, sizeof(bs.h265));

		munit_assert_false(chiaki_bitstream_slice_set_reference_frame(&bs, slice, 8, 0));
		// And it did not write on the way to refusing, which is the half a return value alone
		// would not catch.
		munit_assert_memory_equal(8, full, slice);
	}

	return MUNIT_OK;
}

MunitTest tests_bitstream[] = {
	{
		"/bitstream_exhausted_reader_terminates",
		test_bitstream_exhausted_reader_terminates,
		NULL,
		NULL,
		MUNIT_TEST_OPTION_NONE,
		NULL
	},
	{
		"/bitstream_set_ref_h265_truncated",
		test_bitstream_set_ref_h265_truncated,
		NULL,
		NULL,
		MUNIT_TEST_OPTION_NONE,
		NULL
	},
	{
		"/bitstream_truncated_header_h264",
		test_bitstream_truncated_header_h264,
		NULL,
		NULL,
		MUNIT_TEST_OPTION_NONE,
		NULL
	},
	{
		"/bitstream_truncated_slice_h264",
		test_bitstream_truncated_slice_h264,
		NULL,
		NULL,
		MUNIT_TEST_OPTION_NONE,
		NULL
	},
	{
		"/bitstream_parse_h264",
		test_bitstream_parse_h264,
		NULL,
		NULL,
		MUNIT_TEST_OPTION_NONE,
		NULL
	},
	{
		"/bitstream_parse_h265",
		test_bitstream_parse_h265,
		NULL,
		NULL,
		MUNIT_TEST_OPTION_NONE,
		NULL
	},
	{
		"/bitstream_issue_213",
		test_bitstream_issue_213,
		NULL,
		NULL,
		MUNIT_TEST_OPTION_NONE,
		NULL
	},
	{
		"/bitstream_set_ref_h265",
		test_bitstream_set_ref_h265,
		NULL,
		NULL,
		MUNIT_TEST_OPTION_NONE,
		NULL
	},
	{ NULL, NULL, NULL, NULL, MUNIT_TEST_OPTION_NONE, NULL }
};
