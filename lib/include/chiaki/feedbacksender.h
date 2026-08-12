// SPDX-License-Identifier: LicenseRef-AGPL-3.0-only-OpenSSL

#ifndef CHIAKI_FEEDBACKSENDER_H
#define CHIAKI_FEEDBACKSENDER_H

#include "controller.h"
#include "takion.h"
#include "thread.h"
#include "common.h"
#include "sessionbaseline.h"

#define CHIAKI_FEEDBACK_HISTORY_PACKET_BUF_SIZE 0x300
#define CHIAKI_FEEDBACK_HISTORY_PACKET_QUEUE_SIZE 0x40

#ifdef __cplusplus
extern "C" {
#endif

typedef struct chiaki_feedback_sender_t
{
	ChiakiLog *log;
	ChiakiTakion *takion;
	ChiakiThread thread;

	ChiakiSeqNum16 state_seq_num;

	ChiakiSeqNum16 history_seq_num;
	ChiakiFeedbackHistoryBuffer history_buf;
	uint8_t history_packets[CHIAKI_FEEDBACK_HISTORY_PACKET_QUEUE_SIZE][CHIAKI_FEEDBACK_HISTORY_PACKET_BUF_SIZE];
	size_t history_packet_sizes[CHIAKI_FEEDBACK_HISTORY_PACKET_QUEUE_SIZE];
	size_t history_packet_begin;
	size_t history_packet_len;

	bool should_stop;
	ChiakiControllerState controller_state_prev;
	ChiakiControllerState controller_state_history_prev;
	ChiakiControllerState controller_state;
	bool controller_state_changed;
	bool history_dirty;
	ChiakiMutex state_mutex;
	ChiakiCond state_cond;

	/**
	 * When the pending controller state was handed over, and how long the handovers
	 * before it waited to reach the socket. This is the input half of the delay, and
	 * it is the only part of it this process can see: everything from the wire to the
	 * picture moving happens on the console or on the display.
	 */
	uint64_t state_handed_over_us;
	ChiakiSessionBaselineStat input_to_wire;
} ChiakiFeedbackSender;

CHIAKI_EXPORT ChiakiErrorCode chiaki_feedback_sender_init(ChiakiFeedbackSender *feedback_sender, ChiakiTakion *takion);
CHIAKI_EXPORT void chiaki_feedback_sender_fini(ChiakiFeedbackSender *feedback_sender);
CHIAKI_EXPORT ChiakiErrorCode chiaki_feedback_sender_set_controller_state(ChiakiFeedbackSender *feedback_sender, ChiakiControllerState *state);

#ifdef __cplusplus
}
#endif

#endif // CHIAKI_FEEDBACKSENDER_H
