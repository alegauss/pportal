// SPDX-License-Identifier: LicenseRef-AGPL-3.0-only-OpenSSL

#include <munit.h>

#include <chiaki/videoreceiver.h>
#include <chiaki/session.h>

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "test_log.h"

/**
 * PP57: the video receiver describes a frame from a slice the bitstream parser may never have
 * filled in.
 *
 * chiaki_bitstream_slice declines a frame with no startcode or an unexpected NAL unit type, and
 * says so with a warning. The receiver's `succ` is derived from the flush result alone, so a
 * declined frame still reaches video_sample_cb and still reaches the log line that names the
 * slice type. Before PP57 that read an uninitialised automatic - undefined, and visible as a
 * letter chosen at random.
 *
 * What is asserted here is that path being reachable and what it now says: a frame the parser
 * refused is delivered, and the log names no slice type for it. The second half is what fails
 * without the fix, because the line said 'I' or 'P' unconditionally.
 */

#define UNIT_EXT 2 // the buffer-size extension the frame processor reads off a video unit

typedef struct sample_capture_t
{
	unsigned frames;      // callback invocations that were not the profile header
	size_t last_size;
	uint8_t last_first_byte;
} SampleCapture;

/**
 * A real SPS and PPS, taken from test/bitstream.c so that the header this receiver is given is one
 * the parser is already known to accept. A hand-written approximation is worse than useless here:
 * chiaki_bitstream_header hands it to an RBSP reader that a truncated SPS can walk off the end of,
 * and what that costs is a hang rather than a refusal.
 */
static const uint8_t PROFILE_HEADER[] = {
	0x00, 0x00, 0x00, 0x01, 0x67, 0x4d, 0x40, 0x32, 0x91, 0x8a, 0x01, 0xe0, 0x08, 0x9f, 0x97, 0x01,
	0x6a, 0x02, 0x02, 0x02, 0x80, 0x00, 0x03, 0xe9, 0x00, 0x01, 0xd4, 0xc0, 0x44, 0xd0, 0xf1, 0xf1,
	0x50, 0x00, 0x00, 0x00, 0x01, 0x68, 0xee, 0x3c, 0x80,
};

static bool sample_cb(uint8_t *buf, size_t buf_size, int32_t frames_lost, bool frame_recovered, void *user)
{
	SampleCapture *capture = user;
	// stream_info hands the profile header to the same callback before any frame arrives, so it
	// is identified by its contents rather than by being first - counting it as a frame would
	// make the assertions below pass for the wrong reason.
	if(buf_size == sizeof(PROFILE_HEADER) && memcmp(buf, PROFILE_HEADER, buf_size) == 0)
		return true;
	capture->frames++;
	capture->last_size = buf_size;
	capture->last_first_byte = buf_size ? buf[0] : 0;
	return true;
}

/**
 * A session zeroed apart from what this path reads. The receiver needs a log and a codec at init,
 * and the callback and its user at flush; nothing else on the path a single complete frame takes
 * is touched, and frame index 1 is the one index that skips the corrupt-frame report which would
 * reach the stream connection.
 */
static void session_init(ChiakiSession *session, ChiakiLog *log, SampleCapture *capture)
{
	memset(session, 0, sizeof(*session));
	session->log = log;
	session->connect_info.video_profile.codec = CHIAKI_CODEC_H264;
	session->video_sample_cb = sample_cb;
	session->video_sample_cb_user = capture;
}

/** One profile, so the adaptive stream index check passes. The header is freed by fini. */
static void receiver_stream_info(ChiakiVideoReceiver *receiver)
{
	ChiakiVideoProfile profile;
	memset(&profile, 0, sizeof(profile));
	profile.width = 1920;
	profile.height = 1080;
	profile.header_sz = sizeof(PROFILE_HEADER);
	profile.header = malloc(sizeof(PROFILE_HEADER));
	munit_assert_not_null(profile.header);
	memcpy(profile.header, PROFILE_HEADER, sizeof(PROFILE_HEADER));
	chiaki_video_receiver_stream_info(receiver, &profile, 1);
}

/**
 * One whole frame in one unit, carrying `payload`. units_in_frame_total of 1 makes the unit the
 * last one, which is what makes the receiver flush inside this call rather than on the next frame.
 */
static ChiakiTakionAVPacket frame_packet(uint8_t *buf, const uint8_t *payload, size_t payload_size)
{
	memset(buf, 0, UNIT_EXT);
	memcpy(buf + UNIT_EXT, payload, payload_size);

	ChiakiTakionAVPacket packet;
	memset(&packet, 0, sizeof(packet));
	packet.is_video = true;
	packet.frame_index = 1;
	packet.packet_index = 0;
	packet.unit_index = 0;
	packet.units_in_frame_total = 1;
	packet.units_in_frame_fec = 0;
	packet.adaptive_stream_index = 0;
	packet.data = buf;
	packet.data_size = UNIT_EXT + payload_size;
	return packet;
}

static MunitResult run_frame(const uint8_t *payload, size_t payload_size, SampleCapture *capture,
		char *log_out, size_t log_out_size)
{
	ChiakiLogSniffer sniffer;
	chiaki_log_sniffer_init(&sniffer, CHIAKI_LOG_ALL, get_test_log());

	ChiakiSession session;
	session_init(&session, chiaki_log_sniffer_get_log(&sniffer), capture);

	ChiakiVideoReceiver receiver;
	chiaki_video_receiver_init(&receiver, &session, NULL);
	receiver_stream_info(&receiver);

	uint8_t *buf = malloc(UNIT_EXT + payload_size);
	munit_assert_not_null(buf);
	ChiakiTakionAVPacket packet = frame_packet(buf, payload, payload_size);
	chiaki_video_receiver_av_packet(&receiver, &packet);

	snprintf(log_out, log_out_size, "%s", chiaki_log_sniffer_get_buffer(&sniffer));

	free(buf);
	chiaki_video_receiver_fini(&receiver);
	chiaki_log_sniffer_fini(&sniffer);
	return MUNIT_OK;
}

/**
 * An access unit delimiter: a startcode the parser accepts followed by NAL unit type 9, which it
 * does not. slice_h264 returns false at bitstream.c:173 without writing to the slice - the exact
 * case that used to leave the log reading an indeterminate value.
 */
static MunitResult test_videoreceiver_declined_frame_names_no_slice_type(const MunitParameter params[], void *user)
{
	(void)params; (void)user;
	static const uint8_t aud[] = { 0x00, 0x00, 0x00, 0x01, 0x09, 0x10 };

	SampleCapture capture;
	memset(&capture, 0, sizeof(capture));
	char log[8192];
	munit_assert_int(run_frame(aud, sizeof(aud), &capture, log, sizeof(log)), ==, MUNIT_OK);

	// The premise: the parser refused this frame and the receiver delivered it anyway. Without
	// both halves the rest of the test is asserting about a path nothing takes.
	munit_assert_not_null(strstr(log, "Unexpected NAL unit type 9"));
	munit_assert_uint(capture.frames, ==, 1);
	munit_assert_size(capture.last_size, ==, sizeof(aud));

	// The fix. Before PP57 this line named a letter read from an uninitialised slice, so one of
	// the two assertions below failed on every run and which one was not decided by this code.
	munit_assert_not_null(strstr(log, "Added reference frame 1 of unparsed slice type"));
	munit_assert_null(strstr(log, "Added reference I frame 1"));
	munit_assert_null(strstr(log, "Added reference P frame 1"));
	return MUNIT_OK;
}

/**
 * The other side of the same line: a frame the parser does accept still names its type. Without
 * this, zeroing the slice and always printing "unparsed" would pass the test above.
 */
static MunitResult test_videoreceiver_parsed_frame_names_its_slice_type(const MunitParameter params[], void *user)
{
	(void)params; (void)user;
	// The I slice from test/bitstream.c, which that suite already asserts parses to
	// CHIAKI_BITSTREAM_SLICE_I. Borrowed rather than invented for the reason above.
	static const uint8_t idr[] = {
		0x00, 0x00, 0x00, 0x01, 0x65, 0x88, 0x80, 0x82, 0x1f, 0x00, 0x49, 0xee, 0x03, 0x29, 0xff, 0xf8,
		0x7f, 0x88, 0x46, 0x44, 0x77, 0x17, 0xe7, 0x6d, 0xb3, 0xad, 0x38, 0x19, 0x74, 0x5a, 0xf1, 0x51,
	};

	SampleCapture capture;
	memset(&capture, 0, sizeof(capture));
	char log[8192];
	munit_assert_int(run_frame(idr, sizeof(idr), &capture, log, sizeof(log)), ==, MUNIT_OK);

	munit_assert_uint(capture.frames, ==, 1);
	munit_assert_null(strstr(log, "of unparsed slice type"));
	munit_assert_not_null(strstr(log, "Added reference I frame 1"));
	return MUNIT_OK;
}

MunitTest tests_video_receiver[] = {
	{
		"/declined_frame_names_no_slice_type",
		test_videoreceiver_declined_frame_names_no_slice_type,
		NULL, NULL, MUNIT_TEST_OPTION_NONE, NULL
	},
	{
		"/parsed_frame_names_its_slice_type",
		test_videoreceiver_parsed_frame_names_its_slice_type,
		NULL, NULL, MUNIT_TEST_OPTION_NONE, NULL
	},
	{ NULL, NULL, NULL, NULL, MUNIT_TEST_OPTION_NONE, NULL }
};
