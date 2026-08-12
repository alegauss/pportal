// SPDX-License-Identifier: LicenseRef-AGPL-3.0-only-OpenSSL

#include <chiaki/sessionbaseline.h>

#include <math.h>
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

CHIAKI_EXPORT void chiaki_session_baseline_push_handoff(ChiakiSessionBaseline *baseline, uint64_t handoff_us)
{
	if(baseline->handoff_samples == 0 || handoff_us < baseline->handoff_us_min)
		baseline->handoff_us_min = handoff_us;
	if(handoff_us > baseline->handoff_us_max)
		baseline->handoff_us_max = handoff_us;
	baseline->handoff_us_sum += handoff_us;
	baseline->handoff_samples++;
}

CHIAKI_EXPORT uint64_t chiaki_session_baseline_handoff_us_avg(const ChiakiSessionBaseline *baseline)
{
	if(baseline->handoff_samples == 0)
		return 0;
	return baseline->handoff_us_sum / baseline->handoff_samples;
}

CHIAKI_EXPORT ChiakiErrorCode chiaki_session_baseline_format(const ChiakiSessionBaseline *baseline, char *buf, size_t buf_size, size_t *written)
{
	char line[CHIAKI_SESSION_BASELINE_LINE_MAX];
	char started[CHIAKI_SESSION_BASELINE_TIME_SIZE + 2];

	if(baseline->started_utc[0])
		snprintf(started, sizeof(started), "\"%s\"", baseline->started_utc);
	else
		snprintf(started, sizeof(started), "null");

	const int n = snprintf(line, sizeof(line),
			"{\"schema\":%d"
			",\"started_utc\":%s"
			",\"duration_ms\":%llu"
			",\"app_version\":\"%s\""
			",\"video\":{\"width\":%u,\"height\":%u,\"fps\":%u,\"codec\":\"%s\"}"
			",\"measured_bitrate_mbps\":%.3f"
			",\"average_packet_loss\":%.5f"
			",\"frames\":{\"presented\":%llu,\"lost\":%llu,\"dropped\":%llu}"
			",\"handoff_us\":{\"min\":%llu,\"max\":%llu,\"avg\":%llu,\"samples\":%llu}"
			"}\n",
			CHIAKI_SESSION_BASELINE_SCHEMA,
			started,
			(unsigned long long)baseline->duration_ms,
			baseline->app_version,
			baseline->video_width, baseline->video_height, baseline->video_fps,
			baseline->video_codec,
			baseline_finite(baseline->measured_bitrate_mbps),
			baseline_finite(baseline->average_packet_loss),
			(unsigned long long)baseline->frames_presented,
			(unsigned long long)baseline->frames_lost,
			(unsigned long long)baseline->frames_dropped,
			(unsigned long long)(baseline->handoff_samples ? baseline->handoff_us_min : 0),
			(unsigned long long)baseline->handoff_us_max,
			(unsigned long long)chiaki_session_baseline_handoff_us_avg(baseline),
			(unsigned long long)baseline->handoff_samples);

	if(n < 0)
		return CHIAKI_ERR_UNKNOWN;
	if((size_t)n >= sizeof(line))
		return CHIAKI_ERR_OVERFLOW;
	if((size_t)n + 1 > buf_size)
		return CHIAKI_ERR_BUF_TOO_SMALL;

	memcpy(buf, line, (size_t)n + 1);
	if(written)
		*written = (size_t)n;
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
