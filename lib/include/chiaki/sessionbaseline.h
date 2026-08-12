// SPDX-License-Identifier: LicenseRef-AGPL-3.0-only-OpenSSL

#ifndef CHIAKI_SESSIONBASELINE_H
#define CHIAKI_SESSIONBASELINE_H

#include "common.h"

#include <stddef.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/**
 * The counters this application already computes are drawn on screen and thrown away
 * when the window closes, so a run cannot be compared with a run from another build.
 * This is the sink: one line of JSON per session, appended to a file, holding what the
 * stream already measured plus the timestamp that makes two of them comparable.
 *
 * It carries no console name, address, session id or account: the identifying fields
 * are exactly the ones the session log has a sanitizer to remove, so they are not
 * collected here at all rather than collected and then scrubbed.
 */

#define CHIAKI_SESSION_BASELINE_SCHEMA 1

/** Longest line chiaki_session_baseline_format can produce, including the newline. */
#define CHIAKI_SESSION_BASELINE_LINE_MAX 1024

/** Room for "YYYY-MM-DDTHH:MM:SSZ" and its terminator. */
#define CHIAKI_SESSION_BASELINE_TIME_SIZE 24

/**
 * One timed stage, folded as it runs. A stage is kept as min/max/mean rather than as a
 * last value because the number a stream is judged on is its worst frame of a minute,
 * and an average alone hides exactly that.
 */
typedef struct chiaki_session_baseline_stat_t
{
	uint64_t min_us;
	uint64_t max_us;
	uint64_t sum_us;
	uint64_t samples;
} ChiakiSessionBaselineStat;

/** Fold one sample in. The first sample becomes the minimum, which a zeroed min would not. */
CHIAKI_EXPORT void chiaki_session_baseline_stat_push(ChiakiSessionBaselineStat *stat, uint64_t sample_us);

/** Mean of the folded samples, or 0 when nothing was sampled. */
CHIAKI_EXPORT uint64_t chiaki_session_baseline_stat_avg(const ChiakiSessionBaselineStat *stat);

/** Free-text fields are truncated to this, so one long string cannot push out a number. */
#define CHIAKI_SESSION_BASELINE_TEXT_SIZE 32

typedef struct chiaki_session_baseline_t
{
	/** UTC start of the session, ISO-8601. Empty until set; written as null. */
	char started_utc[CHIAKI_SESSION_BASELINE_TIME_SIZE];
	uint64_t duration_ms;

	char app_version[CHIAKI_SESSION_BASELINE_TEXT_SIZE];
	char video_codec[CHIAKI_SESSION_BASELINE_TEXT_SIZE];
	uint32_t video_width;
	uint32_t video_height;
	uint32_t video_fps;

	/** ChiakiStreamConnection::measured_bitrate, as drawn by the stream menu. */
	double measured_bitrate_mbps;
	/** The smoothed congestion-control packet loss, 0..1, as drawn by the stream menu. */
	double average_packet_loss;

	/** chiaki_video_receiver_get_frames_lost_total over the session. */
	uint64_t frames_lost;
	/** Frames the window reported dropped, summed over the session rather than per second. */
	uint64_t frames_dropped;
	uint64_t frames_presented;

	/** Decoder-to-present handoff, from the delivery timestamp the window already takes. */
	ChiakiSessionBaselineStat handoff;

	/**
	 * A changed controller state handed to the feedback sender, until that state is on
	 * the wire. It is the only part of the input half of the delay the client can see.
	 */
	ChiakiSessionBaselineStat input_to_wire;

	/**
	 * Round trip time as the console reports it, in microseconds. Not measured here:
	 * recorded as the console's own number, because it is the only network term that
	 * runs for the length of a session.
	 */
	uint64_t network_rtt_us;
} ChiakiSessionBaseline;

/**
 * The terms above, summed: input queueing, the network round trip and the client's
 * decode-to-present handoff.
 *
 * It is a floor on glass to glass and not a measurement of it. The console's own input
 * handling, the game's render, the encoder and the display's pipeline are all outside
 * this process and none of them is in this number. What it is good for is comparing two
 * builds of this client on the same network, which is the question the port has to
 * answer; what it cannot do is tell a user how late their picture is.
 */
CHIAKI_EXPORT uint64_t chiaki_session_baseline_latency_estimate_us(const ChiakiSessionBaseline *baseline);

/** Zero every counter and clear every string. */
CHIAKI_EXPORT void chiaki_session_baseline_init(ChiakiSessionBaseline *baseline);

/**
 * Set the start timestamp from a Unix time in seconds. Deliberately takes the time
 * rather than reading the clock, so a record can be reproduced.
 */
CHIAKI_EXPORT void chiaki_session_baseline_set_started(ChiakiSessionBaseline *baseline, uint64_t unix_seconds);

/** Copy a string into one of the text fields, truncating rather than overflowing. */
CHIAKI_EXPORT void chiaki_session_baseline_set_app_version(ChiakiSessionBaseline *baseline, const char *version);
CHIAKI_EXPORT void chiaki_session_baseline_set_video_codec(ChiakiSessionBaseline *baseline, const char *codec);

/** Fold one decoder-to-present handoff sample into the min/max/mean. */
CHIAKI_EXPORT void chiaki_session_baseline_push_handoff(ChiakiSessionBaseline *baseline, uint64_t handoff_us);

/** Mean handoff, or 0 when nothing was sampled. */
CHIAKI_EXPORT uint64_t chiaki_session_baseline_handoff_us_avg(const ChiakiSessionBaseline *baseline);

/** Fold one input-handed-over-to-on-the-wire sample into the min/max/mean. */
CHIAKI_EXPORT void chiaki_session_baseline_push_input_to_wire(ChiakiSessionBaseline *baseline, uint64_t input_us);

/**
 * Write the record as one line of JSON, newline included, into buf.
 * Returns CHIAKI_ERR_BUF_TOO_SMALL and writes nothing when buf cannot hold the line.
 * When written is non-NULL it receives the length excluding the terminator.
 */
CHIAKI_EXPORT ChiakiErrorCode chiaki_session_baseline_format(const ChiakiSessionBaseline *baseline, char *buf, size_t buf_size, size_t *written);

/** Append the formatted line to the file at path, creating it if it does not exist. */
CHIAKI_EXPORT ChiakiErrorCode chiaki_session_baseline_append(const ChiakiSessionBaseline *baseline, const char *path);

#ifdef __cplusplus
}
#endif

#endif // CHIAKI_SESSIONBASELINE_H
