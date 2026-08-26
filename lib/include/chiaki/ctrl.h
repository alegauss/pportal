// SPDX-License-Identifier: LicenseRef-AGPL-3.0-only-OpenSSL

#ifndef CHIAKI_CTRL_H
#define CHIAKI_CTRL_H

#include "common.h"
#include "thread.h"
#include "stoppipe.h"

#include <stdint.h>
#include <stdbool.h>

#include <winsock2.h>

#ifdef __cplusplus
extern "C" {
#endif

/**
 * PP354: the largest rudp ctrl datagram this channel will receive - the 512-byte ctrl receive
 * buffer plus the eight-byte RUDP header that arrives in front of it.
 *
 * This used to be `sizeof(ctrl->rudp_recv_buf)`, a 520-byte array in ChiakiCtrl that NOTHING ever
 * read or wrote. chiaki_rudp_recv_only receives into a buffer of its own and hands back a parsed
 * message, so the field existed only to carry this number. The number is deliberate; the array
 * was not, and having one made it possible to subtract a different buffer's fill from it.
 */
#define CHIAKI_CTRL_RUDP_DATAGRAM_SIZE 520

typedef void (*ChiakiCantDisplayCb)(void *user, bool cant_display);

typedef struct chiaki_ctrl_message_queue_t ChiakiCtrlMessageQueue;

typedef struct chiaki_ctrl_display_sink_t
{
	void *user;
	ChiakiCantDisplayCb cantdisplay_cb;
} ChiakiCtrlDisplaySink;

typedef struct chiaki_ctrl_t
{
	struct chiaki_session_t *session;
	ChiakiThread thread;

	bool should_stop;
	bool login_pin_entered;
	uint8_t *login_pin;
	size_t login_pin_size;
	ChiakiCtrlMessageQueue *msg_queue;
	ChiakiStopPipe stop_pipe;
	ChiakiStopPipe notif_pipe;
	ChiakiMutex notif_mutex;

	bool login_pin_requested;
	bool cant_displaya;
	bool cant_displayb;
	// PP359: the third flag of the same machine. RP-Prohibit arrives once, in the ctrl response,
	// and used to hide the stream while both flags above read false - a state the client could be
	// talked out of by the first unrelated DisplayA 0x0. It is never lowered: a prohibition is a
	// property of the session the console granted, not of what is on screen.
	bool rp_prohibit;

	chiaki_socket_t sock;

#ifdef __GNUC__
	__attribute__((aligned(__alignof__(uint32_t))))
#endif
	uint8_t recv_buf[512];

	// PP354: recv_buf_size is recv_buf's fill and only ever was. It sat under two arrays and read
	// as though it served both, which is how it came to be subtracted from the other one's size.
	size_t recv_buf_size;
	uint64_t crypt_counter_local;
	uint64_t crypt_counter_remote;
	uint32_t keyboard_text_counter;
} ChiakiCtrl;

CHIAKI_EXPORT ChiakiErrorCode chiaki_ctrl_init(ChiakiCtrl *ctrl, struct chiaki_session_t *session);
CHIAKI_EXPORT ChiakiErrorCode chiaki_ctrl_start(ChiakiCtrl *ctrl);
CHIAKI_EXPORT void chiaki_ctrl_stop(ChiakiCtrl *ctrl);
CHIAKI_EXPORT ChiakiErrorCode chiaki_ctrl_join(ChiakiCtrl *ctrl);
CHIAKI_EXPORT void chiaki_ctrl_fini(ChiakiCtrl *ctrl);
CHIAKI_EXPORT ChiakiErrorCode chiaki_ctrl_send_message(ChiakiCtrl *ctrl, uint16_t type, const uint8_t *payload, size_t payload_size);
CHIAKI_EXPORT ChiakiErrorCode ctrl_message_toggle_microphone(ChiakiCtrl *ctrl, bool muted);
CHIAKI_EXPORT ChiakiErrorCode ctrl_message_connect_microphone(ChiakiCtrl *ctrl);
// PP345: returns a code because it can fail. It was void, and its only failure - a malloc for the
// PIN - returned early, so the console was never told and asked for the PIN again. The caller has
// to read this: a dropped PIN that looks like a refused one is an accusation, not a diagnosis.
CHIAKI_EXPORT ChiakiErrorCode chiaki_ctrl_set_login_pin(ChiakiCtrl *ctrl, const uint8_t *pin, size_t pin_size);
CHIAKI_EXPORT ChiakiErrorCode chiaki_ctrl_goto_bed(ChiakiCtrl *ctrl);
CHIAKI_EXPORT ChiakiErrorCode chiaki_ctrl_keyboard_set_text(ChiakiCtrl *ctrl, const char* text);
CHIAKI_EXPORT ChiakiErrorCode chiaki_ctrl_keyboard_accept(ChiakiCtrl *ctrl);
CHIAKI_EXPORT ChiakiErrorCode chiaki_ctrl_keyboard_reject(ChiakiCtrl *ctrl);
CHIAKI_EXPORT ChiakiErrorCode ctrl_message_go_home(ChiakiCtrl *ctrl);
CHIAKI_EXPORT ChiakiErrorCode ctrl_message_set_fallback_session_id(ChiakiCtrl *ctrl);
// PP383: returns a code because the burst it sends can fail, and a failure means the encryption
// counter has moved on without the console rather than that a feature is off.
CHIAKI_EXPORT ChiakiErrorCode ctrl_enable_features(ChiakiCtrl *ctrl);

#ifdef __cplusplus
}
#endif

#endif
