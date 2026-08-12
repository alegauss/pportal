// SPDX-License-Identifier: LicenseRef-AGPL-3.0-only-OpenSSL

#include <munit.h>

#include <chiaki/frameprocessor.h>
#include <chiaki/takion.h>

#include "../lib/src/takionreceive.h"

#include <stdlib.h>
#include <string.h>

#include "test_log.h"

/**
 * PP44: the allocation budget, as a test rather than a review.
 *
 * A managed transport that allocates per packet turns thousands of small packets a second into a
 * collection under load, and the symptom is the worst frame of a minute rather than the average -
 * which is to say it is invisible to every check that watches a mean. The defence is a number
 * that fails when it rises, and a number is only worth failing against if it was measured rather
 * than agreed in a meeting.
 *
 * This half measures what the C transport that exists actually does, so the budget the managed
 * rewrite inherits has a provenance. The result is the interesting part: after the first frame the
 * parse-and-reassemble path allocates *nothing* per packet. The buffers are sized once from the
 * frame's own header and then reused, so the steady-state cost is zero bytes and zero calls.
 *
 * That makes the budget unusually strict and unusually defensible: the bar is not "allocate
 * little", it is "allocate nothing", because that is what the code being replaced does.
 *
 * Scope, stated because the number is easy to over-read: this replays parse, alloc_frame, put_unit
 * and flush. It does *not* include takion's receive step, which is measured separately and is not
 * zero - see test_alloc_budget_receive_step below, added by PP59. The budget here is for parse and
 * reassembly.
 *
 * Counting works by wrapping the allocator at link time (see test/CMakeLists.txt). Only malloc,
 * calloc and realloc are wrapped; free is left alone, because every pointer handed out here comes
 * from __real_malloc and the real free is the correct one for it. Counting is gated on a flag so
 * that allocations made by munit itself, outside the measured window, are not charged to a packet.
 */

/** The budget, in bytes allocated per packet processed, in steady state. */
#define CHIAKI_ALLOC_BUDGET_BYTES_PER_PACKET 0

/** And in allocator calls, which is the term a GC would care about even at zero bytes. */
#define CHIAKI_ALLOC_BUDGET_CALLS_PER_PACKET 0

static int alloc_counting_enabled = 0;
static size_t alloc_bytes = 0;
static size_t alloc_calls = 0;

void *__real_malloc(size_t size);
void *__real_calloc(size_t count, size_t size);
void *__real_realloc(void *ptr, size_t size);

void *__wrap_malloc(size_t size)
{
	if(alloc_counting_enabled)
	{
		alloc_bytes += size;
		alloc_calls++;
	}
	return __real_malloc(size);
}

void *__wrap_calloc(size_t count, size_t size)
{
	if(alloc_counting_enabled)
	{
		alloc_bytes += count * size;
		alloc_calls++;
	}
	return __real_calloc(count, size);
}

void *__wrap_realloc(void *ptr, size_t size)
{
	if(alloc_counting_enabled)
	{
		alloc_bytes += size;
		alloc_calls++;
	}
	return __real_realloc(ptr, size);
}

/**
 * One real video AV packet, off a real console: 8 units in the frame of which 1 is parity, unit
 * index 6, 0x99 bytes of payload. Replaying real bytes rather than a synthesised header matters
 * because the frame buffer is sized from a field inside that payload - a made-up header would
 * measure a made-up buffer.
 */
static const uint8_t real_video_packet[] = {
		0x2, 0x0, 0x2d, 0x0, 0x5, 0x0, 0xc0, 0x1c, 0x1, 0x3, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0,
		0xe4, 0x10, 0x3, 0x67, 0x0, 0x29, 0xf3, 0x2f, 0x98, 0xf6, 0x99, 0x82, 0x83, 0x78, 0xdb, 0x29,
		0x43, 0xa9, 0xe5, 0x88, 0xf2, 0x11, 0x4, 0x20, 0xe6, 0x20, 0x96, 0xe9, 0x6, 0xee, 0xd, 0x27,
		0xa1, 0x83, 0x82, 0x88, 0xe6, 0x21, 0x49, 0x2, 0x75, 0x74, 0x32, 0x5b, 0xf6, 0xe9, 0xdc, 0x93,
		0xea, 0x31, 0x88, 0xd, 0x2b, 0x4b, 0x34, 0xf9, 0xec, 0x1b, 0x26, 0xcc, 0xbb, 0xbb, 0x81, 0xf2,
		0xd9, 0x2d, 0x8e, 0xa1, 0xb9, 0xe2, 0xb3, 0xca, 0xb2, 0x7d, 0xa3, 0x31, 0xf0, 0x42, 0xb7, 0xb6,
		0x1e, 0x8f, 0x6d, 0xa2, 0x70, 0x46, 0xfd, 0x7e, 0x9b, 0x60, 0x85, 0xb0, 0xed, 0x4f, 0x20, 0xb5,
		0x1, 0x71, 0xa9, 0xaa, 0x18, 0x6b, 0x2a, 0x90, 0xf3, 0xa7, 0x84, 0x36, 0xfd, 0x6d, 0x14, 0x83,
		0x68, 0xa3, 0x9b, 0x3a, 0xc8, 0xd4, 0x3a, 0x31, 0xa0, 0x9b, 0x61, 0xde, 0xa7, 0xed, 0x46, 0xb4,
		0xa3, 0xdf, 0x3f, 0x44, 0x8f, 0xad, 0x64, 0x9, 0xfc, 0x7a, 0xe7, 0x24, 0xf0, 0xd2, 0x42, 0xd3,
		0x57, 0x5a, 0x76, 0x0, 0xc5, 0xe0, 0x93, 0xa9, 0xf5, 0x32, 0x5d, 0xee, 0xf7, 0x9d
};

/** Parse the captured packet, then walk its unit index the way an arriving frame does. */
static void replay_one_frame(ChiakiFrameProcessor *fp, ChiakiTakionAVPacket *parsed)
{
	unsigned int source_units = parsed->units_in_frame_total - parsed->units_in_frame_fec;

	ChiakiTakionAVPacket unit = *parsed;
	unit.unit_index = 0;
	chiaki_frame_processor_alloc_frame(fp, &unit);

	for(unsigned int i = 0; i < source_units; i++)
	{
		unit = *parsed;
		unit.unit_index = (ChiakiSeqNum16)i;
		chiaki_frame_processor_put_unit(fp, &unit);
	}

	uint8_t *frame = NULL;
	size_t frame_size = 0;
	chiaki_frame_processor_flush(fp, &frame, &frame_size);
}

/**
 * The gate. The first frame is allowed to allocate - it is where the buffers are sized - and every
 * frame after it is charged. A per-packet allocation introduced anywhere in parse, put or flush
 * lands here as a non-zero count.
 */
static MunitResult test_alloc_budget_per_packet(const MunitParameter params[], void *user)
{
	ChiakiKeyState key_state;
	chiaki_key_state_init(&key_state);

	ChiakiTakionAVPacket parsed;
	munit_assert_int(chiaki_takion_v9_av_packet_parse(&parsed, &key_state,
			(uint8_t *)real_video_packet, sizeof(real_video_packet)), ==, CHIAKI_ERR_SUCCESS);
	munit_assert_true(parsed.is_video);

	unsigned int source_units = parsed.units_in_frame_total - parsed.units_in_frame_fec;
	munit_assert_uint(source_units, >, 0);

	ChiakiFrameProcessor fp;
	chiaki_frame_processor_init(&fp, get_test_log());

	// Warmup: the sizing allocations happen here, and they are not what this budget is about.
	replay_one_frame(&fp, &parsed);

	const unsigned int frames = 200;
	alloc_bytes = 0;
	alloc_calls = 0;
	alloc_counting_enabled = 1;
	for(unsigned int f = 0; f < frames; f++)
		replay_one_frame(&fp, &parsed);
	alloc_counting_enabled = 0;

	const size_t packets = (size_t)frames * source_units;
	munit_logf(MUNIT_LOG_INFO, "replayed %zu packets in %u frames: %zu bytes in %zu allocations",
			packets, frames, alloc_bytes, alloc_calls);

	// Per packet rather than per frame, because a packet is the unit a transport is judged on and
	// the number a managed rewrite has to hold.
	munit_assert_size(alloc_bytes / packets, <=, CHIAKI_ALLOC_BUDGET_BYTES_PER_PACKET);
	munit_assert_size(alloc_calls / packets, <=, CHIAKI_ALLOC_BUDGET_CALLS_PER_PACKET);

	// Stated absolutely as well, so the division cannot hide a handful of large allocations behind
	// a big packet count: 200 frames that allocate nothing allocate nothing in total.
	munit_assert_size(alloc_calls, ==, 0);
	munit_assert_size(alloc_bytes, ==, 0);

	chiaki_frame_processor_fini(&fp);
	return MUNIT_OK;
}

/**
 * The counter has to be able to see an allocation, or the test above passes by being blind. This
 * asserts the instrument works before the instrument is trusted.
 */
static MunitResult test_alloc_counter_sees_allocations(const MunitParameter params[], void *user)
{
	alloc_bytes = 0;
	alloc_calls = 0;

	// Disabled: nothing is charged.
	alloc_counting_enabled = 0;
	void *quiet = malloc(1234);
	munit_assert_not_null(quiet);
	free(quiet);
	munit_assert_size(alloc_calls, ==, 0);
	munit_assert_size(alloc_bytes, ==, 0);

	// Enabled: exactly what was asked for is charged.
	alloc_counting_enabled = 1;
	void *counted = malloc(4096);
	munit_assert_not_null(counted);
	void *zeroed = calloc(16, 8);
	munit_assert_not_null(zeroed);
	alloc_counting_enabled = 0;

	munit_assert_size(alloc_calls, ==, 2);
	munit_assert_size(alloc_bytes, ==, 4096 + 128);

	free(counted);
	free(zeroed);
	return MUNIT_OK;
}

/**
 * PP59: the receive step, which the budget above does not cover.
 *
 * The number above is scoped to parse and reassembly and is zero. The step before it is not,
 * and this is where it is charged: takion_handle_packet_av is what a datagram passes through
 * between the socket and the reorder queue, and it mallocs a queue entry for every video
 * packet.
 *
 * What is measured and what is replayed, stated because the number is easy to over-read.
 * The entry allocation is measured - it happens inside the function under test. The buffer
 * pair is replayed: takion_av_thread_func mallocs 1500 bytes for every datagram and reallocs
 * it down to the received size (takion.c, the recv loop) before handing ownership over, and
 * driving that loop needs a connected session and a live socket. So the two calls are made
 * here in the same order and at the same sizes, and they are charged. This is the whole
 * receive step's cost with one of its three calls modelled rather than executed.
 */
#define CHIAKI_RECV_BUFFER_INITIAL_SIZE 1500

/**
 * The measured budget for the receive step, in allocator calls per video packet. Not zero,
 * and not agreed: three is what the C transport does, and PP27 inherits it as the number to
 * beat rather than as a bar it has already cleared.
 */
#define CHIAKI_RECV_BUDGET_CALLS_PER_PACKET 3

/** Push one datagram through the receive step, the way the socket thread hands one over. */
static void replay_one_datagram(ChiakiTakion *takion, uint16_t packet_index)
{
	uint8_t *buf = malloc(CHIAKI_RECV_BUFFER_INITIAL_SIZE);
	munit_assert_not_null(buf);
	memcpy(buf, real_video_packet, sizeof(real_video_packet));

	// The packet index rides in bytes 1-2, big endian. Varied per datagram so the queue sees a
	// stream rather than the same packet 200 times, which is a different code path.
	buf[1] = (uint8_t)(packet_index >> 8);
	buf[2] = (uint8_t)(packet_index & 0xff);

	uint8_t *resized = realloc(buf, sizeof(real_video_packet));
	munit_assert_not_null(resized);

	takion_handle_packet_av(takion, TAKION_PACKET_TYPE_VIDEO, resized, sizeof(real_video_packet));
}

static MunitResult test_alloc_budget_receive_step(const MunitParameter params[], void *user)
{
	ChiakiTakion takion;
	memset(&takion, 0, sizeof(takion));
	takion.log = get_test_log();
	takion.av_packet_parse = chiaki_takion_v9_av_packet_parse;
	chiaki_key_state_init(&takion.key_state);

	// Warmup: the reorder queue's own array is calloc'd on the first packet, once per session,
	// and it is not a per-packet cost.
	replay_one_datagram(&takion, 45);

	const unsigned int packets = 200;
	alloc_bytes = 0;
	alloc_calls = 0;
	alloc_counting_enabled = 1;
	for(unsigned int i = 0; i < packets; i++)
		replay_one_datagram(&takion, (uint16_t)(46 + i));
	alloc_counting_enabled = 0;

	const size_t entry_size = takion_av_packet_entry_size();
	munit_logf(MUNIT_LOG_INFO,
			"receive step: %u packets cost %zu bytes in %zu allocations "
			"(%zu bytes and %zu calls per packet; queue entry is %zu bytes)",
			packets, alloc_bytes, alloc_calls,
			alloc_bytes / packets, alloc_calls / packets, entry_size);

	munit_assert_size(alloc_calls, ==, (size_t)packets * CHIAKI_RECV_BUDGET_CALLS_PER_PACKET);

	// Stated as an identity rather than a constant, so a struct that grows moves the number
	// instead of turning the gate red for a reason the message cannot name.
	const size_t expected_bytes = (size_t)packets
			* (CHIAKI_RECV_BUFFER_INITIAL_SIZE + sizeof(real_video_packet) + entry_size);
	munit_assert_size(alloc_bytes, ==, expected_bytes);

	// And the point of the whole line: this step is not the zero the frame processor is.
	munit_assert_size(alloc_calls, >, 0);

	return MUNIT_OK;
}

MunitTest tests_alloc_budget[] = {
	{
		"/counter_sees_allocations",
		test_alloc_counter_sees_allocations,
		NULL, NULL,
		MUNIT_TEST_OPTION_NONE,
		NULL
	},
	{
		"/per_packet",
		test_alloc_budget_per_packet,
		NULL, NULL,
		MUNIT_TEST_OPTION_NONE,
		NULL
	},
	{
		"/receive_step",
		test_alloc_budget_receive_step,
		NULL, NULL,
		MUNIT_TEST_OPTION_NONE,
		NULL
	},
	{ NULL, NULL, NULL, NULL, MUNIT_TEST_OPTION_NONE, NULL }
};
