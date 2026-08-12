// SPDX-License-Identifier: LicenseRef-AGPL-3.0-only-OpenSSL

#include <munit.h>

#include <chiaki/sessionbaseline.h>

#include <stdio.h>
#include <string.h>

static void fill_reference(ChiakiSessionBaseline *baseline)
{
	chiaki_session_baseline_init(baseline);
	chiaki_session_baseline_set_started(baseline, 1754944267); // 2025-08-11T20:31:07Z
	chiaki_session_baseline_set_app_version(baseline, "1.10.0");
	chiaki_session_baseline_set_video_codec(baseline, "h264");
	baseline->duration_ms = 754321;
	baseline->video_width = 1920;
	baseline->video_height = 1080;
	baseline->video_fps = 60;
	baseline->measured_bitrate_mbps = 27.5;
	baseline->average_packet_loss = 0.0125;
	baseline->frames_presented = 45210;
	baseline->frames_lost = 12;
	baseline->frames_dropped = 7;
	chiaki_session_baseline_push_handoff(baseline, 900);
	chiaki_session_baseline_push_handoff(baseline, 1500);
	chiaki_session_baseline_push_handoff(baseline, 1200);
	chiaki_session_baseline_push_input_to_wire(baseline, 400);
	chiaki_session_baseline_push_input_to_wire(baseline, 800);
	baseline->network_rtt_us = 36000;

	// One distinguishable value per stage: a stage filed under another stage's name is the
	// defect this fixture exists to catch, and identical numbers would hide it.
	chiaki_session_baseline_stat_push(&baseline->stages.receive, 40);
	chiaki_session_baseline_stat_push(&baseline->stages.receive, 60);
	chiaki_session_baseline_stat_push(&baseline->stages.reorder, 1100);
	chiaki_session_baseline_stat_push(&baseline->stages.reassemble, 3000);
	chiaki_session_baseline_stat_push(&baseline->stages.correct, 250);
	chiaki_session_baseline_stat_push(&baseline->stages.decode, 4200);
	chiaki_session_baseline_stat_push(&baseline->stages.decode, 9000);
}

/**
 * The record is a comparison between two runs, so the field names, their order and the
 * precision of the two doubles are the contract. Comparing the whole line rather than
 * probing fields is what makes a silent rename fail here instead of in a spreadsheet
 * six months from now.
 */
static MunitResult test_baseline_format_line(const MunitParameter params[], void *user)
{
	ChiakiSessionBaseline baseline;
	fill_reference(&baseline);

	char line[CHIAKI_SESSION_BASELINE_LINE_MAX];
	size_t written = 0;
	munit_assert_int(chiaki_session_baseline_format(&baseline, line, sizeof(line), &written), ==, CHIAKI_ERR_SUCCESS);

	static const char *expected =
			"{\"schema\":2"
			",\"started_utc\":\"2025-08-11T20:31:07Z\""
			",\"duration_ms\":754321"
			",\"app_version\":\"1.10.0\""
			",\"video\":{\"width\":1920,\"height\":1080,\"fps\":60,\"codec\":\"h264\"}"
			",\"measured_bitrate_mbps\":27.500"
			",\"average_packet_loss\":0.01250"
			",\"frames\":{\"presented\":45210,\"lost\":12,\"dropped\":7}"
			",\"handoff_us\":{\"min\":900,\"max\":1500,\"avg\":1200,\"p99\":1500,\"samples\":3}"
			",\"stages_us\":{"
			"\"receive\":{\"min\":40,\"max\":60,\"avg\":50,\"p99\":60,\"samples\":2}"
			",\"reorder\":{\"min\":1100,\"max\":1100,\"avg\":1100,\"p99\":1100,\"samples\":1}"
			",\"reassemble\":{\"min\":3000,\"max\":3000,\"avg\":3000,\"p99\":3000,\"samples\":1}"
			",\"correct\":{\"min\":250,\"max\":250,\"avg\":250,\"p99\":250,\"samples\":1}"
			",\"decode\":{\"min\":4200,\"max\":9000,\"avg\":6600,\"p99\":9000,\"samples\":2}"
			"}"
			",\"latency\":{\"estimate_us\":37800"
			",\"input_to_wire_us\":{\"min\":400,\"max\":800,\"avg\":600,\"p99\":800,\"samples\":2}"
			",\"network_rtt_us\":36000}"
			"}\n";

	munit_assert_string_equal(line, expected);
	munit_assert_size(written, ==, strlen(expected));

	return MUNIT_OK;
}

/** A session that never started still has to produce a parseable line. */
static MunitResult test_baseline_format_empty(const MunitParameter params[], void *user)
{
	ChiakiSessionBaseline baseline;
	chiaki_session_baseline_init(&baseline);

	char line[CHIAKI_SESSION_BASELINE_LINE_MAX];
	munit_assert_int(chiaki_session_baseline_format(&baseline, line, sizeof(line), NULL), ==, CHIAKI_ERR_SUCCESS);

	munit_assert_not_null(strstr(line, "\"started_utc\":null"));
	munit_assert_not_null(strstr(line, "\"handoff_us\":{\"min\":0,\"max\":0,\"avg\":0,\"p99\":0,\"samples\":0}"));
	munit_assert_not_null(strstr(line, "\"estimate_us\":0"));
	// An unsampled stage reports zero samples rather than being absent: a reader comparing
	// two runs has to be able to tell a stage that measured nothing from a stage that is not
	// in this schema at all.
	munit_assert_not_null(strstr(line, "\"receive\":{\"min\":0,\"max\":0,\"avg\":0,\"p99\":0,\"samples\":0}"));
	munit_assert_not_null(strstr(line, "\"decode\":{\"min\":0,\"max\":0,\"avg\":0,\"p99\":0,\"samples\":0}"));
	munit_assert_null(strstr(line, "nan"));
	munit_assert_null(strstr(line, "inf"));

	return MUNIT_OK;
}

/**
 * The tail is the point of the histogram, so what is checked is the one thing a mean cannot
 * do: a distribution whose mean and maximum are both far from its 99th percentile has to
 * report a p99 near neither.
 */
static MunitResult test_baseline_stat_p99(const MunitParameter params[], void *user)
{
	ChiakiSessionBaselineStat stat;
	memset(&stat, 0, sizeof(stat));

	// Nothing sampled is 0 rather than the fastest number in the record.
	munit_assert_uint64(chiaki_session_baseline_stat_p99_us(&stat), ==, 0);

	// Exact below the linear cutoff: a bucket per microsecond, so no bound is involved.
	chiaki_session_baseline_stat_push(&stat, 5);
	munit_assert_uint64(chiaki_session_baseline_stat_p99_us(&stat), ==, 5);

	// 990 frames at 1ms and 10 at 100ms: mean 1990us, maximum 100000us, true p99 1000us.
	memset(&stat, 0, sizeof(stat));
	for(unsigned i = 0; i < 990; i++)
		chiaki_session_baseline_stat_push(&stat, 1000);
	for(unsigned i = 0; i < 10; i++)
		chiaki_session_baseline_stat_push(&stat, 100000);

	munit_assert_uint64(stat.samples, ==, 1000);
	munit_assert_uint64(stat.min_us, ==, 1000);
	munit_assert_uint64(stat.max_us, ==, 100000);
	munit_assert_uint64(chiaki_session_baseline_stat_avg(&stat), ==, 1990);

	// The bound is the upper edge of the bucket 1000us falls in - never below the true p99,
	// and inside the 12.5% the eight-to-the-octave spacing promises.
	const uint64_t p99 = chiaki_session_baseline_stat_p99_us(&stat);
	munit_assert_uint64(p99, >=, 1000);
	munit_assert_uint64(p99, <, 1125);
	munit_assert_uint64(p99, <, stat.max_us);
	// And the mean is above it, not below: ten stalls in a thousand frames drag the average
	// to 1990us while 99% of frames were at 1000. That gap is the whole reason this stat
	// carries a distribution - reading the mean here overstates the typical frame by 2x and
	// understates the worst one by 50x, in the same number.
	munit_assert_uint64(p99, <, chiaki_session_baseline_stat_avg(&stat));

	// One more sample above the line moves the percentile into the tail: 11 of 1000 is over
	// 1%, so the answer must now come from the slow bucket and not from the fast one.
	memset(&stat, 0, sizeof(stat));
	for(unsigned i = 0; i < 989; i++)
		chiaki_session_baseline_stat_push(&stat, 1000);
	for(unsigned i = 0; i < 11; i++)
		chiaki_session_baseline_stat_push(&stat, 100000);
	munit_assert_uint64(chiaki_session_baseline_stat_p99_us(&stat), ==, 100000);

	return MUNIT_OK;
}

/**
 * A stage slower than the histogram's last bucket still has to report a bound. The overflow
 * bucket has no upper edge, so the measured maximum is what answers - which is exact.
 */
static MunitResult test_baseline_stat_p99_overflow(const MunitParameter params[], void *user)
{
	ChiakiSessionBaselineStat stat;
	memset(&stat, 0, sizeof(stat));

	chiaki_session_baseline_stat_push(&stat, 5000000); // 5s, past the 2^21us last bucket
	munit_assert_uint64(chiaki_session_baseline_stat_p99_us(&stat), ==, 5000000);

	// And it stays a bound over a mixed distribution: 99 fast frames and one 5s stall.
	memset(&stat, 0, sizeof(stat));
	for(unsigned i = 0; i < 99; i++)
		chiaki_session_baseline_stat_push(&stat, 800);
	chiaki_session_baseline_stat_push(&stat, 5000000);
	munit_assert_uint64(chiaki_session_baseline_stat_p99_us(&stat), <, 1000);

	return MUNIT_OK;
}

/**
 * Every stage is its own accumulator. A sample pushed into one must not appear in another,
 * which is the whole reason the record has five of them instead of one.
 */
static MunitResult test_baseline_frame_stages_are_separate(const MunitParameter params[], void *user)
{
	ChiakiSessionBaseline baseline;
	chiaki_session_baseline_init(&baseline);

	chiaki_session_baseline_stat_push(&baseline.stages.reorder, 2000);

	munit_assert_uint64(baseline.stages.reorder.samples, ==, 1);
	munit_assert_uint64(baseline.stages.reorder.max_us, ==, 2000);
	munit_assert_uint64(baseline.stages.receive.samples, ==, 0);
	munit_assert_uint64(baseline.stages.reassemble.samples, ==, 0);
	munit_assert_uint64(baseline.stages.correct.samples, ==, 0);
	munit_assert_uint64(baseline.stages.decode.samples, ==, 0);
	// The present stage is the handoff, and is not a sixth accumulator.
	munit_assert_uint64(baseline.handoff.samples, ==, 0);

	// A stage is not in the latency estimate: that sum is input, network and handoff, and a
	// reorder queue counted twice would inflate it.
	munit_assert_uint64(chiaki_session_baseline_latency_estimate_us(&baseline), ==, 0);

	return MUNIT_OK;
}

/**
 * The estimate is a sum of three terms and has to stay one: a build that got slower in
 * the input queue and faster in the handoff must not read as unchanged, so each term is
 * moved on its own and the total is checked to follow only that term.
 */
static MunitResult test_baseline_latency_estimate(const MunitParameter params[], void *user)
{
	ChiakiSessionBaseline baseline;
	chiaki_session_baseline_init(&baseline);
	munit_assert_uint64(chiaki_session_baseline_latency_estimate_us(&baseline), ==, 0);

	chiaki_session_baseline_push_input_to_wire(&baseline, 400);
	chiaki_session_baseline_push_input_to_wire(&baseline, 800);
	munit_assert_uint64(chiaki_session_baseline_latency_estimate_us(&baseline), ==, 600);

	baseline.network_rtt_us = 36000;
	munit_assert_uint64(chiaki_session_baseline_latency_estimate_us(&baseline), ==, 36600);

	chiaki_session_baseline_push_handoff(&baseline, 900);
	chiaki_session_baseline_push_handoff(&baseline, 1500);
	chiaki_session_baseline_push_handoff(&baseline, 1200);
	munit_assert_uint64(chiaki_session_baseline_latency_estimate_us(&baseline), ==, 37800);

	// The network term is not a stat and must not be averaged away by the two that are.
	baseline.network_rtt_us = 56000;
	munit_assert_uint64(chiaki_session_baseline_latency_estimate_us(&baseline), ==, 57800);

	return MUNIT_OK;
}

/** The two stages are separate accumulators: a sample of one must not reach the other. */
static MunitResult test_baseline_stages_are_separate(const MunitParameter params[], void *user)
{
	ChiakiSessionBaseline baseline;
	chiaki_session_baseline_init(&baseline);

	chiaki_session_baseline_push_handoff(&baseline, 5000);
	munit_assert_uint64(baseline.handoff.samples, ==, 1);
	munit_assert_uint64(baseline.input_to_wire.samples, ==, 0);

	chiaki_session_baseline_push_input_to_wire(&baseline, 70);
	munit_assert_uint64(baseline.handoff.samples, ==, 1);
	munit_assert_uint64(baseline.handoff.min_us, ==, 5000);
	munit_assert_uint64(baseline.input_to_wire.samples, ==, 1);
	munit_assert_uint64(baseline.input_to_wire.min_us, ==, 70);

	return MUNIT_OK;
}

static MunitResult test_baseline_handoff(const MunitParameter params[], void *user)
{
	ChiakiSessionBaseline baseline;
	chiaki_session_baseline_init(&baseline);

	munit_assert_uint64(chiaki_session_baseline_handoff_us_avg(&baseline), ==, 0);

	// The first sample has to become the minimum: a min left at zero would report a
	// handoff no frame ever had, and zero is the value that looks best.
	chiaki_session_baseline_push_handoff(&baseline, 4000);
	munit_assert_uint64(baseline.handoff.min_us, ==, 4000);
	munit_assert_uint64(baseline.handoff.max_us, ==, 4000);
	munit_assert_uint64(chiaki_session_baseline_handoff_us_avg(&baseline), ==, 4000);

	chiaki_session_baseline_push_handoff(&baseline, 2000);
	chiaki_session_baseline_push_handoff(&baseline, 6000);
	munit_assert_uint64(baseline.handoff.min_us, ==, 2000);
	munit_assert_uint64(baseline.handoff.max_us, ==, 6000);
	munit_assert_uint64(baseline.handoff.samples, ==, 3);
	munit_assert_uint64(chiaki_session_baseline_handoff_us_avg(&baseline), ==, 4000);

	return MUNIT_OK;
}

/** A codec name carrying a quote must not be able to end the JSON string early. */
static MunitResult test_baseline_text_is_escaped(const MunitParameter params[], void *user)
{
	ChiakiSessionBaseline baseline;
	chiaki_session_baseline_init(&baseline);
	chiaki_session_baseline_set_video_codec(&baseline, "h2\"64\\,\"evil\":1");
	chiaki_session_baseline_set_app_version(&baseline, "1.0\n0");

	// The comma and the colon are legal inside a JSON string and survive; only the quote
	// and the backslash - the two that could end it early - are replaced.
	munit_assert_string_equal(baseline.video_codec, "h2_64_,_evil_:1");
	munit_assert_string_equal(baseline.app_version, "1.0_0");

	char line[CHIAKI_SESSION_BASELINE_LINE_MAX];
	munit_assert_int(chiaki_session_baseline_format(&baseline, line, sizeof(line), NULL), ==, CHIAKI_ERR_SUCCESS);
	munit_assert_null(strstr(line, "evil\":1"));

	return MUNIT_OK;
}

/** A long name is truncated, not written past the end of the field. */
static MunitResult test_baseline_text_is_truncated(const MunitParameter params[], void *user)
{
	ChiakiSessionBaseline baseline;
	chiaki_session_baseline_init(&baseline);
	chiaki_session_baseline_set_video_codec(&baseline, "0123456789012345678901234567890123456789");

	munit_assert_size(strlen(baseline.video_codec), ==, CHIAKI_SESSION_BASELINE_TEXT_SIZE - 1);
	munit_assert_char(baseline.video_codec[CHIAKI_SESSION_BASELINE_TEXT_SIZE - 1], ==, '\0');

	return MUNIT_OK;
}

/** A buffer too small is refused, and nothing is written into it. */
static MunitResult test_baseline_format_buf_too_small(const MunitParameter params[], void *user)
{
	ChiakiSessionBaseline baseline;
	fill_reference(&baseline);

	char line[CHIAKI_SESSION_BASELINE_LINE_MAX];
	size_t needed = 0;
	munit_assert_int(chiaki_session_baseline_format(&baseline, line, sizeof(line), &needed), ==, CHIAKI_ERR_SUCCESS);

	char small[CHIAKI_SESSION_BASELINE_LINE_MAX];
	memset(small, 0x7f, sizeof(small));
	munit_assert_int(chiaki_session_baseline_format(&baseline, small, needed, NULL), ==, CHIAKI_ERR_BUF_TOO_SMALL);
	for(size_t i = 0; i < sizeof(small); i++)
		munit_assert_char(small[i], ==, 0x7f);

	return MUNIT_OK;
}

/** Two sessions append two lines to one file: the shape the next run is compared against. */
static MunitResult test_baseline_append(const MunitParameter params[], void *user)
{
	static const char *path = "chiaki_baseline_test.jsonl";
	remove(path);

	ChiakiSessionBaseline baseline;
	fill_reference(&baseline);
	munit_assert_int(chiaki_session_baseline_append(&baseline, path), ==, CHIAKI_ERR_SUCCESS);

	baseline.duration_ms = 42;
	munit_assert_int(chiaki_session_baseline_append(&baseline, path), ==, CHIAKI_ERR_SUCCESS);

	FILE *f = fopen(path, "rb");
	munit_assert_not_null(f);
	char contents[4 * CHIAKI_SESSION_BASELINE_LINE_MAX];
	const size_t read = fread(contents, 1, sizeof(contents) - 1, f);
	fclose(f);
	contents[read] = '\0';

	unsigned lines = 0;
	for(size_t i = 0; i < read; i++)
	{
		if(contents[i] == '\n')
			lines++;
	}
	munit_assert_uint(lines, ==, 2);
	munit_assert_not_null(strstr(contents, "\"duration_ms\":754321"));
	munit_assert_not_null(strstr(contents, "\"duration_ms\":42"));

	remove(path);
	return MUNIT_OK;
}

/** An unwritable path is reported, so a lost baseline is not mistaken for no session. */
static MunitResult test_baseline_append_failure(const MunitParameter params[], void *user)
{
	ChiakiSessionBaseline baseline;
	fill_reference(&baseline);

	munit_assert_int(chiaki_session_baseline_append(&baseline, "no_such_directory_for_baseline/out.jsonl"), !=, CHIAKI_ERR_SUCCESS);

	return MUNIT_OK;
}

MunitTest tests_session_baseline[] = {
	{
		"/format_line",
		test_baseline_format_line,
		NULL, NULL,
		MUNIT_TEST_OPTION_NONE,
		NULL
	},
	{
		"/format_empty",
		test_baseline_format_empty,
		NULL, NULL,
		MUNIT_TEST_OPTION_NONE,
		NULL
	},
	{
		"/handoff",
		test_baseline_handoff,
		NULL, NULL,
		MUNIT_TEST_OPTION_NONE,
		NULL
	},
	{
		"/latency_estimate",
		test_baseline_latency_estimate,
		NULL, NULL,
		MUNIT_TEST_OPTION_NONE,
		NULL
	},
	{
		"/stages_are_separate",
		test_baseline_stages_are_separate,
		NULL, NULL,
		MUNIT_TEST_OPTION_NONE,
		NULL
	},
	{
		"/stat_p99",
		test_baseline_stat_p99,
		NULL, NULL,
		MUNIT_TEST_OPTION_NONE,
		NULL
	},
	{
		"/stat_p99_overflow",
		test_baseline_stat_p99_overflow,
		NULL, NULL,
		MUNIT_TEST_OPTION_NONE,
		NULL
	},
	{
		"/frame_stages_are_separate",
		test_baseline_frame_stages_are_separate,
		NULL, NULL,
		MUNIT_TEST_OPTION_NONE,
		NULL
	},
	{
		"/text_is_escaped",
		test_baseline_text_is_escaped,
		NULL, NULL,
		MUNIT_TEST_OPTION_NONE,
		NULL
	},
	{
		"/text_is_truncated",
		test_baseline_text_is_truncated,
		NULL, NULL,
		MUNIT_TEST_OPTION_NONE,
		NULL
	},
	{
		"/format_buf_too_small",
		test_baseline_format_buf_too_small,
		NULL, NULL,
		MUNIT_TEST_OPTION_NONE,
		NULL
	},
	{
		"/append",
		test_baseline_append,
		NULL, NULL,
		MUNIT_TEST_OPTION_NONE,
		NULL
	},
	{
		"/append_failure",
		test_baseline_append_failure,
		NULL, NULL,
		MUNIT_TEST_OPTION_NONE,
		NULL
	},
	{ NULL, NULL, NULL, NULL, MUNIT_TEST_OPTION_NONE, NULL }
};
