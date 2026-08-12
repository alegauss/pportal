// SPDX-License-Identifier: LicenseRef-AGPL-3.0-only-OpenSSL

#include <munit.h>

#include <chiaki/frameprocessor.h>

#include <string.h>

#include "test_log.h"

/**
 * The reassemble and correct stages of the frame path are timed inside the frame processor,
 * and neither can be exercised from a session baseline: one runs when the last unit of a
 * frame arrives and the other only when units are missing. These tests drive the processor
 * directly with synthesised units, so what is checked is the accounting - which frames are
 * charged, and how many times - rather than the clock.
 */

#define UNIT_PAYLOAD 32

typedef struct frame_unit_buf_t
{
	uint8_t data[2 + UNIT_PAYLOAD];
} FrameUnitBuf;

/**
 * One unit of a frame with `total` units of which `fec` are parity. The first two bytes are
 * the buffer-size extension the processor reads out of a video unit, and are left at zero so
 * the unit size is exactly what is handed in.
 */
static ChiakiTakionAVPacket unit_packet(FrameUnitBuf *buf, uint16_t unit_index, uint16_t total, uint16_t fec)
{
	memset(buf, 0, sizeof(*buf));
	for(size_t i = 0; i < UNIT_PAYLOAD; i++)
		buf->data[2 + i] = (uint8_t)(unit_index * 0x10 + i);

	ChiakiTakionAVPacket packet;
	memset(&packet, 0, sizeof(packet));
	packet.is_video = true;
	packet.frame_index = 1;
	packet.packet_index = unit_index;
	packet.unit_index = unit_index;
	packet.units_in_frame_total = total;
	packet.units_in_frame_fec = fec;
	packet.data = buf->data;
	packet.data_size = sizeof(buf->data);
	return packet;
}

/** A frame that arrives whole is one reassembly and no correction. */
static MunitResult test_frameprocessor_reassemble_charged_once(const MunitParameter params[], void *user)
{
	ChiakiFrameProcessor fp;
	chiaki_frame_processor_init(&fp, get_test_log());

	munit_assert_uint64(fp.stage_reassemble.samples, ==, 0);
	munit_assert_uint64(fp.stage_correct.samples, ==, 0);

	FrameUnitBuf buf;
	ChiakiTakionAVPacket unit0 = unit_packet(&buf, 0, 3, 1);
	munit_assert_int(chiaki_frame_processor_alloc_frame(&fp, &unit0), ==, CHIAKI_ERR_SUCCESS);
	munit_assert_int(chiaki_frame_processor_put_unit(&fp, &unit0), ==, CHIAKI_ERR_SUCCESS);

	FrameUnitBuf buf1;
	ChiakiTakionAVPacket unit1 = unit_packet(&buf1, 1, 3, 1);
	munit_assert_int(chiaki_frame_processor_put_unit(&fp, &unit1), ==, CHIAKI_ERR_SUCCESS);

	// Both source units are in, so this flush needs no reconstruction.
	munit_assert_true(chiaki_frame_processor_flush_possible(&fp));

	uint8_t *frame = NULL;
	size_t frame_size = 0;
	munit_assert_int(chiaki_frame_processor_flush(&fp, &frame, &frame_size), ==, CHIAKI_FRAME_PROCESSOR_FLUSH_RESULT_SUCCESS);

	munit_assert_uint64(fp.stage_reassemble.samples, ==, 1);
	// A frame that never lost a unit must not appear in the correct stage: averaging the
	// reconstruction over every frame would report a cost no lossy minute really pays.
	munit_assert_uint64(fp.stage_correct.samples, ==, 0);

	// The receiver flushes the same frame again when the next frame's head arrives first.
	// That is one reassembly, not two.
	chiaki_frame_processor_flush(&fp, &frame, &frame_size);
	munit_assert_uint64(fp.stage_reassemble.samples, ==, 1);

	chiaki_frame_processor_fini(&fp);
	return MUNIT_OK;
}

/** A frame missing a source unit is charged for the reconstruction it ran. */
static MunitResult test_frameprocessor_correct_charged_on_fec(const MunitParameter params[], void *user)
{
	ChiakiFrameProcessor fp;
	chiaki_frame_processor_init(&fp, get_test_log());

	FrameUnitBuf buf0;
	ChiakiTakionAVPacket unit0 = unit_packet(&buf0, 0, 3, 1);
	munit_assert_int(chiaki_frame_processor_alloc_frame(&fp, &unit0), ==, CHIAKI_ERR_SUCCESS);
	munit_assert_int(chiaki_frame_processor_put_unit(&fp, &unit0), ==, CHIAKI_ERR_SUCCESS);

	// Unit 1 never arrives; unit 2 is the parity unit, so the flush has to reconstruct.
	FrameUnitBuf buf2;
	ChiakiTakionAVPacket unit2 = unit_packet(&buf2, 2, 3, 1);
	munit_assert_int(chiaki_frame_processor_put_unit(&fp, &unit2), ==, CHIAKI_ERR_SUCCESS);

	uint8_t *frame = NULL;
	size_t frame_size = 0;
	chiaki_frame_processor_flush(&fp, &frame, &frame_size);

	// Whether the reconstruction succeeded is not what is asserted here: a build that fails
	// FEC slowly is not faster than one that fails it quickly, so both are timed.
	munit_assert_uint64(fp.stage_correct.samples, ==, 1);
	munit_assert_uint64(fp.stage_reassemble.samples, ==, 1);

	chiaki_frame_processor_fini(&fp);
	return MUNIT_OK;
}

/** Two frames are two reassemblies, and the second does not carry the first one's begin. */
static MunitResult test_frameprocessor_stages_span_frames(const MunitParameter params[], void *user)
{
	ChiakiFrameProcessor fp;
	chiaki_frame_processor_init(&fp, get_test_log());

	for(unsigned frame_no = 0; frame_no < 2; frame_no++)
	{
		FrameUnitBuf buf0, buf1;
		ChiakiTakionAVPacket unit0 = unit_packet(&buf0, 0, 3, 1);
		ChiakiTakionAVPacket unit1 = unit_packet(&buf1, 1, 3, 1);
		munit_assert_int(chiaki_frame_processor_alloc_frame(&fp, &unit0), ==, CHIAKI_ERR_SUCCESS);
		munit_assert_int(chiaki_frame_processor_put_unit(&fp, &unit0), ==, CHIAKI_ERR_SUCCESS);
		munit_assert_int(chiaki_frame_processor_put_unit(&fp, &unit1), ==, CHIAKI_ERR_SUCCESS);

		uint8_t *frame = NULL;
		size_t frame_size = 0;
		munit_assert_int(chiaki_frame_processor_flush(&fp, &frame, &frame_size), ==, CHIAKI_FRAME_PROCESSOR_FLUSH_RESULT_SUCCESS);
		munit_assert_uint64(fp.stage_reassemble.samples, ==, frame_no + 1);
	}

	munit_assert_uint64(fp.stage_correct.samples, ==, 0);

	chiaki_frame_processor_fini(&fp);
	return MUNIT_OK;
}

MunitTest tests_frame_processor[] = {
	{
		"/reassemble_charged_once",
		test_frameprocessor_reassemble_charged_once,
		NULL, NULL,
		MUNIT_TEST_OPTION_NONE,
		NULL
	},
	{
		"/correct_charged_on_fec",
		test_frameprocessor_correct_charged_on_fec,
		NULL, NULL,
		MUNIT_TEST_OPTION_NONE,
		NULL
	},
	{
		"/stages_span_frames",
		test_frameprocessor_stages_span_frames,
		NULL, NULL,
		MUNIT_TEST_OPTION_NONE,
		NULL
	},
	{ NULL, NULL, NULL, NULL, MUNIT_TEST_OPTION_NONE, NULL }
};
