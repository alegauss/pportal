// SPDX-License-Identifier: LicenseRef-AGPL-3.0-only-OpenSSL

#ifndef CHIAKI_FRAMEPROCESSOR_H
#define CHIAKI_FRAMEPROCESSOR_H

#include "common.h"
#include "takion.h"
#include "packetstats.h"
#include "sessionbaseline.h"

#include <stdint.h>
#include <stdbool.h>

#ifdef __cplusplus
extern "C" {
#endif

typedef struct chiaki_stream_stats_t
{
	uint64_t frames;
	uint64_t bytes;
} ChiakiStreamStats;

CHIAKI_EXPORT void chiaki_stream_stats_reset(ChiakiStreamStats *stats);
CHIAKI_EXPORT void chiaki_stream_stats_frame(ChiakiStreamStats *stats, uint64_t size);
CHIAKI_EXPORT uint64_t chiaki_stream_stats_bitrate(ChiakiStreamStats *stats, uint64_t framerate);

struct chiaki_frame_unit_t;
typedef struct chiaki_frame_unit_t ChiakiFrameUnit;

typedef struct chiaki_frame_processor_t
{
	ChiakiLog *log;
	uint8_t *frame_buf;
	size_t frame_buf_size;
	size_t buf_size_per_unit;
	size_t buf_stride_per_unit;
	unsigned int units_source_expected;
	unsigned int units_fec_expected;
	unsigned int units_source_received;
	unsigned int units_fec_received;
	ChiakiFrameUnit *unit_slots;
	size_t unit_slots_size;
	bool flushed; // whether we have already flushed the current frame, i.e. are only interested in stats, not data.
	ChiakiStreamStats stream_stats;

	/**
	 * When the current frame was allocated, i.e. when its first unit arrived. Zero once the
	 * frame has been charged for, so a second flush of the same frame is not counted twice.
	 */
	uint64_t frame_begun_us;
	/**
	 * The reassemble and correct stages of the frame path, for the session baseline. Written
	 * only from the takion thread that feeds this processor, so they carry no lock.
	 *
	 * They are separate accumulators because they answer different questions: reassemble is
	 * how long a frame waited for its last unit, which is the network, and correct is what
	 * the FEC reconstruction itself cost, which is this CPU. Only the frames that actually
	 * ran FEC appear in correct - averaging the reconstruction over every frame would report
	 * a cost far below what a lossy minute really pays.
	 */
	ChiakiSessionBaselineStat stage_reassemble;
	ChiakiSessionBaselineStat stage_correct;
} ChiakiFrameProcessor;

typedef enum chiaki_frame_flush_result_t {
	CHIAKI_FRAME_PROCESSOR_FLUSH_RESULT_SUCCESS = 0,
	CHIAKI_FRAME_PROCESSOR_FLUSH_RESULT_FEC_SUCCESS = 1,
	CHIAKI_FRAME_PROCESSOR_FLUSH_RESULT_FEC_FAILED = 2,
	CHIAKI_FRAME_PROCESSOR_FLUSH_RESULT_FAILED = 3
} ChiakiFrameProcessorFlushResult;

CHIAKI_EXPORT void chiaki_frame_processor_init(ChiakiFrameProcessor *frame_processor, ChiakiLog *log);
CHIAKI_EXPORT void chiaki_frame_processor_fini(ChiakiFrameProcessor *frame_processor);

CHIAKI_EXPORT void chiaki_frame_processor_report_packet_stats(ChiakiFrameProcessor *frame_processor, ChiakiPacketStats *packet_stats);
CHIAKI_EXPORT ChiakiErrorCode chiaki_frame_processor_alloc_frame(ChiakiFrameProcessor *frame_processor, ChiakiTakionAVPacket *packet);
CHIAKI_EXPORT ChiakiErrorCode chiaki_frame_processor_put_unit(ChiakiFrameProcessor *frame_processor, ChiakiTakionAVPacket *packet);

/**
 * @param frame unless CHIAKI_FRAME_PROCESSOR_FLUSH_RESULT_FAILED returned, will receive a pointer into the internal buffer of frame_processor.
 * MUST NOT be used after the next call to this frame processor!
 */
CHIAKI_EXPORT ChiakiFrameProcessorFlushResult chiaki_frame_processor_flush(ChiakiFrameProcessor *frame_processor, uint8_t **frame, size_t *frame_size);

static inline bool chiaki_frame_processor_flush_possible(ChiakiFrameProcessor *frame_processor)
{
	return frame_processor->units_source_received + frame_processor->units_fec_received
		>= frame_processor->units_source_expected;
}

#ifdef __cplusplus
}
#endif

#endif // CHIAKI_FRAMEPROCESSOR_H
