// SPDX-License-Identifier: LicenseRef-AGPL-3.0-only-OpenSSL

#include <chiaki/sessionbaseline.h>

#include <math.h>
#include <stdarg.h>
#include <stdio.h>
#include <string.h>

/**
 * The civil date is computed here rather than taken from gmtime because the record is
 * meant to be reproducible: the same Unix second has to produce the same line on any
 * host, whatever its timezone, and gmtime_r is not available on every toolchain this
 * library builds with.
 *
 * days -> y/m/d, from the shifted-epoch algorithm (era of 400 years starting in March).
 */
static void baseline_civil_from_days(int64_t days, int *year, unsigned *month, unsigned *day)
{
	days += 719468; // shift the epoch from 1970-01-01 to 0000-03-01
	const int64_t era = (days >= 0 ? days : days - 146096) / 146097;
	const uint64_t day_of_era = (uint64_t)(days - era * 146097);                                        // [0, 146096]
	const uint64_t year_of_era = (day_of_era - day_of_era / 1460 + day_of_era / 36524 - day_of_era / 146096) / 365; // [0, 399]
	const int64_t y = (int64_t)year_of_era + era * 400;
	const uint64_t day_of_year = day_of_era - (365 * year_of_era + year_of_era / 4 - year_of_era / 100); // [0, 365]
	const uint64_t mp = (5 * day_of_year + 2) / 153;                                                    // [0, 11], March = 0
	const unsigned d = (unsigned)(day_of_year - (153 * mp + 2) / 5 + 1);                                // [1, 31]
	const unsigned m = (unsigned)(mp < 10 ? mp + 3 : mp - 9);                                           // [1, 12]

	*year = (int)(y + (m <= 2));
	*month = m;
	*day = d;
}

/**
 * Copy at most size-1 bytes, replacing anything that is not printable ASCII - and the
 * two characters that would end a JSON string early - with '_'. Escaping at this end
 * rather than at format time is what makes the line valid JSON by construction: no
 * caller can hand in a codec name that closes the quote.
 */
static void baseline_set_text(char *dst, size_t size, const char *src)
{
	size_t i = 0;
	if(src)
	{
		for(; i < size - 1 && src[i]; i++)
		{
			const unsigned char c = (unsigned char)src[i];
			dst[i] = (c < 0x20 || c > 0x7e || c == '"' || c == '\\') ? '_' : (char)c;
		}
	}
	memset(dst + i, 0, size - i);
}

static double baseline_finite(double value)
{
	return isfinite(value) ? value : 0.0;
}

CHIAKI_EXPORT void chiaki_session_baseline_init(ChiakiSessionBaseline *baseline)
{
	memset(baseline, 0, sizeof(*baseline));
}

CHIAKI_EXPORT void chiaki_session_baseline_set_started(ChiakiSessionBaseline *baseline, uint64_t unix_seconds)
{
	const int64_t days = (int64_t)(unix_seconds / 86400);
	const unsigned seconds_of_day = (unsigned)(unix_seconds % 86400);
	int year;
	unsigned month, day;
	baseline_civil_from_days(days, &year, &month, &day);
	snprintf(baseline->started_utc, sizeof(baseline->started_utc),
			"%04d-%02u-%02uT%02u:%02u:%02uZ",
			year, month, day,
			seconds_of_day / 3600, (seconds_of_day / 60) % 60, seconds_of_day % 60);
}

CHIAKI_EXPORT void chiaki_session_baseline_set_app_version(ChiakiSessionBaseline *baseline, const char *version)
{
	baseline_set_text(baseline->app_version, sizeof(baseline->app_version), version);
}

CHIAKI_EXPORT void chiaki_session_baseline_set_video_codec(ChiakiSessionBaseline *baseline, const char *codec)
{
	baseline_set_text(baseline->video_codec, sizeof(baseline->video_codec), codec);
}

CHIAKI_EXPORT void chiaki_session_baseline_set_hw_decoder(ChiakiSessionBaseline *baseline, const char *hw_decoder)
{
	// Named here rather than at the call site, because "" and "no hardware decoder" are the
	// same fact and a row reading "" would look like a field nobody filled in. The library
	// refuses a named decoder it cannot open rather than falling back silently, so a session
	// that ran with no name ran on the CPU.
	if(!hw_decoder || !hw_decoder[0])
		hw_decoder = "software";
	baseline_set_text(baseline->hw_decoder, sizeof(baseline->hw_decoder), hw_decoder);
}

#define BASELINE_HIST_LINEAR CHIAKI_SESSION_BASELINE_HIST_LINEAR
#define BASELINE_HIST_SUB CHIAKI_SESSION_BASELINE_HIST_SUB
#define BASELINE_HIST_SUB_BITS CHIAKI_SESSION_BASELINE_HIST_SUB_BITS
/** The highest exponent the log-spaced part covers; 4 is the lowest, LINEAR being 2^4. */
#define BASELINE_HIST_EXP_MAX (3 + CHIAKI_SESSION_BASELINE_HIST_OCTAVES)
#define BASELINE_HIST_OVERFLOW (CHIAKI_SESSION_BASELINE_HIST_BUCKETS - 1)

/** Index of the highest set bit. Only called with value >= BASELINE_HIST_LINEAR, so >= 4. */
static unsigned baseline_hist_exp(uint64_t value)
{
	unsigned e = 0;
	while(value >>= 1)
		e++;
	return e;
}

/**
 * Which bucket a sample belongs to. Monotone in the sample by construction: below LINEAR
 * the index is the value, and above it the exponent picks the octave and the next three
 * bits pick the eighth of it.
 */
static size_t baseline_hist_index(uint64_t sample_us)
{
	if(sample_us < BASELINE_HIST_LINEAR)
		return (size_t)sample_us;
	const unsigned e = baseline_hist_exp(sample_us);
	if(e > BASELINE_HIST_EXP_MAX)
		return BASELINE_HIST_OVERFLOW;
	const size_t sub = (size_t)((sample_us >> (e - BASELINE_HIST_SUB_BITS)) & (BASELINE_HIST_SUB - 1));
	return BASELINE_HIST_LINEAR + (size_t)(e - 4) * BASELINE_HIST_SUB + sub;
}

/** Largest sample this bucket can hold. The overflow bucket has none and reports UINT64_MAX. */
static uint64_t baseline_hist_upper_us(size_t index)
{
	if(index < BASELINE_HIST_LINEAR)
		return (uint64_t)index;
	if(index >= BASELINE_HIST_OVERFLOW)
		return UINT64_MAX;
	const size_t k = index - BASELINE_HIST_LINEAR;
	const unsigned e = 4 + (unsigned)(k / BASELINE_HIST_SUB);
	const uint64_t sub = (uint64_t)(k % BASELINE_HIST_SUB);
	const uint64_t width = (uint64_t)1 << (e - BASELINE_HIST_SUB_BITS);
	return ((BASELINE_HIST_SUB + sub) << (e - BASELINE_HIST_SUB_BITS)) + width - 1;
}

CHIAKI_EXPORT void chiaki_session_baseline_stat_push(ChiakiSessionBaselineStat *stat, uint64_t sample_us)
{
	if(stat->samples == 0 || sample_us < stat->min_us)
		stat->min_us = sample_us;
	if(sample_us > stat->max_us)
		stat->max_us = sample_us;
	stat->sum_us += sample_us;
	stat->samples++;
	// Saturate rather than wrap: a wrapped bucket would move the tail towards zero, which
	// is the direction that reads as an improvement.
	uint32_t *bucket = stat->buckets + baseline_hist_index(sample_us);
	if(*bucket != UINT32_MAX)
		(*bucket)++;
}

CHIAKI_EXPORT uint64_t chiaki_session_baseline_stat_avg(const ChiakiSessionBaselineStat *stat)
{
	if(stat->samples == 0)
		return 0;
	return stat->sum_us / stat->samples;
}

CHIAKI_EXPORT uint64_t chiaki_session_baseline_stat_percentile_us(const ChiakiSessionBaselineStat *stat, unsigned int percent)
{
	if(stat->samples == 0)
		return 0;
	if(percent < 1)
		percent = 1;
	if(percent > 100)
		percent = 100;

	// The rank of the percentile, rounded up: at 99 with 100 samples that is the 99th, so
	// exactly one sample is allowed to sit above what this returns.
	const uint64_t target = (stat->samples * percent + 99) / 100;
	uint64_t cumulative = 0;
	for(size_t i = 0; i < CHIAKI_SESSION_BASELINE_HIST_BUCKETS; i++)
	{
		cumulative += stat->buckets[i];
		if(cumulative < target)
			continue;
		const uint64_t upper = baseline_hist_upper_us(i);
		// The maximum is measured rather than bucketed, so it is the tighter bound whenever
		// the bucket is wider than the samples that landed in it.
		return upper < stat->max_us ? upper : stat->max_us;
	}
	// Unreachable while every push lands in a bucket, but a stat whose histogram saturated
	// must still answer with a bound rather than with zero.
	return stat->max_us;
}

CHIAKI_EXPORT uint64_t chiaki_session_baseline_stat_p50_us(const ChiakiSessionBaselineStat *stat)
{
	return chiaki_session_baseline_stat_percentile_us(stat, 50);
}

CHIAKI_EXPORT uint64_t chiaki_session_baseline_stat_p99_us(const ChiakiSessionBaselineStat *stat)
{
	return chiaki_session_baseline_stat_percentile_us(stat, 99);
}

CHIAKI_EXPORT void chiaki_session_baseline_push_handoff(ChiakiSessionBaseline *baseline, uint64_t handoff_us)
{
	chiaki_session_baseline_stat_push(&baseline->handoff, handoff_us);
}

CHIAKI_EXPORT uint64_t chiaki_session_baseline_handoff_us_avg(const ChiakiSessionBaseline *baseline)
{
	return chiaki_session_baseline_stat_avg(&baseline->handoff);
}

CHIAKI_EXPORT void chiaki_session_baseline_push_input_to_wire(ChiakiSessionBaseline *baseline, uint64_t input_us)
{
	chiaki_session_baseline_stat_push(&baseline->input_to_wire, input_us);
}

CHIAKI_EXPORT uint64_t chiaki_session_baseline_latency_estimate_us(const ChiakiSessionBaseline *baseline)
{
	return chiaki_session_baseline_stat_avg(&baseline->input_to_wire)
		+ baseline->network_rtt_us
		+ chiaki_session_baseline_stat_avg(&baseline->handoff);
}

/**
 * The line is assembled through a cursor rather than one snprintf because a record with
 * six stages in it is thirty arguments long, and a positional mistake in that list is a
 * number filed under another stage's name - a defect that reads as data.
 */
typedef struct baseline_writer_t
{
	char *buf;
	size_t size;
	size_t len;
	bool overflowed;
} BaselineWriter;

static void baseline_write(BaselineWriter *w, const char *fmt, ...)
{
	if(w->overflowed)
		return;
	va_list ap;
	va_start(ap, fmt);
	const int n = vsnprintf(w->buf + w->len, w->size - w->len, fmt, ap);
	va_end(ap);
	if(n < 0 || (size_t)n >= w->size - w->len)
	{
		w->overflowed = true;
		return;
	}
	w->len += (size_t)n;
}

/**
 * One stage as an object. The minimum is reported as 0 when nothing was sampled: the field
 * holds whatever the last push left, and an unsampled stage has to read as unsampled
 * rather than as the fastest one in the record.
 */
static void baseline_write_stat(BaselineWriter *w, const char *name, const ChiakiSessionBaselineStat *stat)
{
	baseline_write(w, "\"%s\":{\"min\":%llu,\"max\":%llu,\"avg\":%llu,\"p50\":%llu,\"p99\":%llu,\"samples\":%llu}",
			name,
			(unsigned long long)(stat->samples ? stat->min_us : 0),
			(unsigned long long)stat->max_us,
			(unsigned long long)chiaki_session_baseline_stat_avg(stat),
			(unsigned long long)chiaki_session_baseline_stat_p50_us(stat),
			(unsigned long long)chiaki_session_baseline_stat_p99_us(stat),
			(unsigned long long)stat->samples);
}

CHIAKI_EXPORT ChiakiErrorCode chiaki_session_baseline_format(const ChiakiSessionBaseline *baseline, char *buf, size_t buf_size, size_t *written)
{
	char line[CHIAKI_SESSION_BASELINE_LINE_MAX];
	char started[CHIAKI_SESSION_BASELINE_TIME_SIZE + 2];

	if(baseline->started_utc[0])
		snprintf(started, sizeof(started), "\"%s\"", baseline->started_utc);
	else
		snprintf(started, sizeof(started), "null");

	BaselineWriter w = { line, sizeof(line), 0, false };

	baseline_write(&w,
			"{\"schema\":%d"
			",\"started_utc\":%s"
			",\"duration_ms\":%llu"
			",\"app_version\":\"%s\""
			",\"video\":{\"width\":%u,\"height\":%u,\"fps\":%u,\"codec\":\"%s\"}"
			",\"settings\":{\"hw_decoder\":\"%s\",\"bitrate_kbps\":%u"
			",\"packet_loss_max\":%.5f,\"idr_on_fec_failure\":%s}"
			",\"measured_bitrate_mbps\":%.3f"
			",\"average_packet_loss\":%.5f"
			",\"frames\":{\"presented\":%llu,\"lost\":%llu,\"dropped\":%llu}"
			",",
			CHIAKI_SESSION_BASELINE_SCHEMA,
			started,
			(unsigned long long)baseline->duration_ms,
			baseline->app_version,
			baseline->video_width, baseline->video_height, baseline->video_fps,
			baseline->video_codec,
			baseline->hw_decoder,
			baseline->bitrate_kbps,
			baseline_finite(baseline->packet_loss_max),
			baseline->idr_on_fec_failure ? "true" : "false",
			baseline_finite(baseline->measured_bitrate_mbps),
			baseline_finite(baseline->average_packet_loss),
			(unsigned long long)baseline->frames_presented,
			(unsigned long long)baseline->frames_lost,
			(unsigned long long)baseline->frames_dropped);

	// The present stage, under the name it shipped with rather than moved into stages_us.
	baseline_write_stat(&w, "handoff_us", &baseline->handoff);

	baseline_write(&w, ",\"stages_us\":{");
	baseline_write_stat(&w, "receive", &baseline->stages.receive);
	baseline_write(&w, ",");
	baseline_write_stat(&w, "reorder", &baseline->stages.reorder);
	baseline_write(&w, ",");
	baseline_write_stat(&w, "reassemble", &baseline->stages.reassemble);
	baseline_write(&w, ",");
	baseline_write_stat(&w, "correct", &baseline->stages.correct);
	baseline_write(&w, ",");
	baseline_write_stat(&w, "decode", &baseline->stages.decode);
	baseline_write(&w, "}");

	baseline_write(&w, ",\"latency\":{\"estimate_us\":%llu,",
			(unsigned long long)chiaki_session_baseline_latency_estimate_us(baseline));
	baseline_write_stat(&w, "input_to_wire_us", &baseline->input_to_wire);
	baseline_write(&w, ",\"network_rtt_us\":%llu}",
			(unsigned long long)baseline->network_rtt_us);

	baseline_write(&w, "}\n");

	if(w.overflowed)
		return CHIAKI_ERR_OVERFLOW;
	if(w.len + 1 > buf_size)
		return CHIAKI_ERR_BUF_TOO_SMALL;

	memcpy(buf, line, w.len + 1);
	if(written)
		*written = w.len;
	return CHIAKI_ERR_SUCCESS;
}

CHIAKI_EXPORT ChiakiErrorCode chiaki_session_baseline_append(const ChiakiSessionBaseline *baseline, const char *path)
{
	char line[CHIAKI_SESSION_BASELINE_LINE_MAX];
	size_t line_size;

	const ChiakiErrorCode err = chiaki_session_baseline_format(baseline, line, sizeof(line), &line_size);
	if(err != CHIAKI_ERR_SUCCESS)
		return err;

	FILE *f = fopen(path, "ab");
	if(!f)
		return CHIAKI_ERR_UNKNOWN;

	const size_t wrote = fwrite(line, 1, line_size, f);
	// A short write leaves a truncated line behind, which is a record that reads as data
	// and is not, so it is reported rather than swallowed.
	const bool complete = wrote == line_size;
	if(fclose(f) != 0 || !complete)
		return CHIAKI_ERR_UNKNOWN;

	return CHIAKI_ERR_SUCCESS;
}
