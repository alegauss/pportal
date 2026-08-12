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
	uint64_t handoff_us_min;
	uint64_t handoff_us_max;
	uint64_t handoff_us_sum;
	uint64_t handoff_samples;
} ChiakiSessionBaseline;

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
