// SPDX-License-Identifier: LicenseRef-AGPL-3.0-only-OpenSSL

#ifndef CHIAKI_SESSIONBASELINE_H
#define CHIAKI_SESSIONBASELINE_H

#include "common.h"

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/**
 * The counters this application already computes are drawn on screen and thrown away
 * when the window closes, so a run cannot be compared with a run from another build.
 * This is the sink: one line of JSON per session, appended to a file, holding what the
 * stream already measured, the configuration that produced it, and the timestamp that
 * makes two of them comparable.
 *
 * It carries no console name, address, session id or account: the identifying fields
 * are exactly the ones the session log has a sanitizer to remove, so they are not
 * collected here at all rather than collected and then scrubbed.
 *
 * Local, on disk, and nothing here is sent anywhere
 * ------------------------------------------------
 * This is instrumentation for a port, not analytics about users, and that is a property
 * of the design rather than an accident of nobody having written an uploader yet. The
 * only sink is chiaki_session_baseline_append, which takes a path and opens a file; this
 * translation unit has no socket, no URL and no dependency that could acquire one.
 *
 * The field set below is therefore closed, and closed on purpose. A session record that
 * named a console or a network would be exactly the file that must not grow a
 * transmitter later without the decision being taken again - so the guard against that
 * is that there is no such field to transmit. test_baseline_field_set_is_closed pins the
 * record's keys for that reason: adding one is a decision, and it should have to break a
 * test that says so out loud rather than pass quietly.
 */

#define CHIAKI_SESSION_BASELINE_SCHEMA 5

/** Longest line chiaki_session_baseline_format can produce, including the newline. */
#define CHIAKI_SESSION_BASELINE_LINE_MAX 2048

/** Room for "YYYY-MM-DDTHH:MM:SSZ" and its terminator. */
#define CHIAKI_SESSION_BASELINE_TIME_SIZE 24

/**
 * The tail of a stage is read from a fixed histogram rather than from stored samples, so
 * the cost of a stage is the same whether a session lasts a minute or an evening: no
 * allocation, no per-frame retention, one increment per sample.
 *
 * Buckets are exact for 0..15us and then log-spaced, eight to the octave, up to 2^21us
 * (~2.1s); everything longer lands in one overflow bucket. Eight to the octave is what
 * fixes the resolution: within an octave a bucket is 1/8th of its own lower edge wide, so
 * a percentile read out of it is an upper bound that overshoots by at most 12.5% - and
 * chiaki_session_baseline_stat_p99_us clamps that bound to the observed maximum, which is
 * exact. That is precise enough to say the present stage grew by 6ms between two builds,
 * and not precise enough to quote a p99 to the microsecond. The first is the question this
 * record exists for; the second it must not be read as answering.
 */
#define CHIAKI_SESSION_BASELINE_HIST_SUB_BITS 3
#define CHIAKI_SESSION_BASELINE_HIST_SUB (1u << CHIAKI_SESSION_BASELINE_HIST_SUB_BITS)
/** Values below this are counted one bucket each, so a sub-microsecond stage stays exact. */
#define CHIAKI_SESSION_BASELINE_HIST_LINEAR (2u * CHIAKI_SESSION_BASELINE_HIST_SUB)
/** Octaves covered by the log-spaced part: exponents 4..20 inclusive. */
#define CHIAKI_SESSION_BASELINE_HIST_OCTAVES 17
/** The last bucket is the overflow, and is the only one with no upper edge. */
#define CHIAKI_SESSION_BASELINE_HIST_BUCKETS (CHIAKI_SESSION_BASELINE_HIST_LINEAR \
		+ CHIAKI_SESSION_BASELINE_HIST_OCTAVES * CHIAKI_SESSION_BASELINE_HIST_SUB + 1u)

/**
 * One timed stage, folded as it runs. A stage is kept as min/max/mean plus the histogram
 * above rather than as a last value because the number a stream is judged on is its worst
 * frame of a minute; the mean alone hides exactly that, and the maximum alone is a single
 * outlier rather than the tail a user feels.
 */
typedef struct chiaki_session_baseline_stat_t
{
	uint64_t min_us;
	uint64_t max_us;
	uint64_t sum_us;
	uint64_t samples;
	uint32_t buckets[CHIAKI_SESSION_BASELINE_HIST_BUCKETS];
} ChiakiSessionBaselineStat;

/** Fold one sample in. The first sample becomes the minimum, which a zeroed min would not. */
CHIAKI_EXPORT void chiaki_session_baseline_stat_push(ChiakiSessionBaselineStat *stat, uint64_t sample_us);

/** Mean of the folded samples, or 0 when nothing was sampled. */
CHIAKI_EXPORT uint64_t chiaki_session_baseline_stat_avg(const ChiakiSessionBaselineStat *stat);

/**
 * An upper bound on the given percentile (1..100), or 0 when nothing was sampled. Read out of
 * the histogram, so it is the upper edge of the bucket the percentile falls in, clamped to the
 * observed maximum. It never under-reports: a p99 of 9000 means no more than 1% of samples were
 * above 9000us, and the true value may be as low as 8000. The same holds for the median.
 */
CHIAKI_EXPORT uint64_t chiaki_session_baseline_stat_percentile_us(const ChiakiSessionBaselineStat *stat, unsigned int percent);

/**
 * The median and the 99th, both bounds as above.
 *
 * The median is recorded alongside the mean rather than instead of it because they answer
 * different questions and disagree exactly when it matters: a stage with a heavy tail can have a
 * mean far above its median, so a comparison that reads only the mean reports a typical frame
 * that no frame resembled.
 */
CHIAKI_EXPORT uint64_t chiaki_session_baseline_stat_p50_us(const ChiakiSessionBaselineStat *stat);
CHIAKI_EXPORT uint64_t chiaki_session_baseline_stat_p99_us(const ChiakiSessionBaselineStat *stat);

/** Free-text fields are truncated to this, so one long string cannot push out a number. */
#define CHIAKI_SESSION_BASELINE_TEXT_SIZE 32

/**
 * The frame path, stage by stage. Until these existed the only per-frame timestamp in the
 * tree was the decoder's delivery, so a slower build produced one sentence - the port is
 * 8ms slower - and no address: receive, reassemble, correct, decode and present are five
 * places a millisecond can appear and a single number locates none of them.
 *
 * The sixth stage, present, is not repeated here: it is ChiakiSessionBaseline::handoff,
 * which shipped before these did and stays where a reader already found it.
 *
 * Each stage is timed where it happens, so the accumulators are written by the thread that
 * owns that stage - takion's receive thread for the first two, the decoder's lock for the
 * fifth - and copied into a baseline once, after that thread has been joined.
 */
typedef struct chiaki_session_baseline_stages_t
{
	/** Takion: an AV packet off the socket, until it is decrypted, parsed and queued. */
	ChiakiSessionBaselineStat receive;
	/** Dwell in the AV reorder queue: pushed, until pulled back out in order. */
	ChiakiSessionBaselineStat reorder;
	/** The first unit of a frame arriving, until that frame is flushed complete. */
	ChiakiSessionBaselineStat reassemble;
	/** FEC reconstruction only, and only over the frames that needed it. */
	ChiakiSessionBaselineStat correct;
	/** A packet handed to the decoder, until the frame it produced is pulled out. */
	ChiakiSessionBaselineStat decode;
} ChiakiSessionBaselineStages;

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

	/**
	 * The settings that decide the picture, so a row explains the numbers beside it rather
	 * than only reporting them. Each one governs something this record already measures:
	 * the decoder governs the decode stage, the requested bitrate is what
	 * measured_bitrate_mbps is a shortfall against, and the two network knobs govern the
	 * correct stage and frames_lost. Without them two rows can differ for a reason that is
	 * nowhere in either row.
	 */
	/** The decoder actually in use - "cuda", "d3d11va", "vulkan", or "software". */
	char hw_decoder[CHIAKI_SESSION_BASELINE_TEXT_SIZE];
	/**
	 * PP72: the renderer the window ran on - "vulkan", "opengl", or "unknown". It belongs
	 * beside the decoder because it is what decides which decoder the automatic choice can
	 * reach: an OpenGL window cannot hold a vulkan frame, so on that renderer the auto path
	 * picks between cuda and d3d11va and on the other one it does not. Two rows naming
	 * different decoders are only comparable once both name the renderer that allowed them.
	 */
	char renderer[CHIAKI_SESSION_BASELINE_TEXT_SIZE];
	/** Requested, not achieved. measured_bitrate_mbps is the achieved one. */
	uint32_t bitrate_kbps;
	/** Congestion control's loss ceiling, 0..1. */
	double packet_loss_max;
	/** Whether an IDR was requested on FEC failure, which changes what a loss costs. */
	bool idr_on_fec_failure;

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

	/** The four stages before the handoff, plus the decode itself. */
	ChiakiSessionBaselineStages stages;

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
/** NULL or empty becomes "software", which is the decoder that ran when none was named. */
CHIAKI_EXPORT void chiaki_session_baseline_set_hw_decoder(ChiakiSessionBaseline *baseline, const char *hw_decoder);
/** NULL or empty becomes "unknown": no renderer is not a state a session that drew a frame was in. */
CHIAKI_EXPORT void chiaki_session_baseline_set_renderer(ChiakiSessionBaseline *baseline, const char *renderer);

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
