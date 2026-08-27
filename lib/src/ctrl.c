// SPDX-License-Identifier: LicenseRef-AGPL-3.0-only-OpenSSL

#include <chiaki/ctrl.h>
#include <chiaki/session.h>
#include <chiaki/base64.h>
#include <chiaki/http.h>
#include <chiaki/time.h>
#include <chiaki/messagetap.h>

#include "utils.h"

#include <stdlib.h>
#include <string.h>
#include <stdio.h>
#include <errno.h>
#include <assert.h>
#include <inttypes.h>

#include <winsock2.h>
#include <ws2tcpip.h>

#define SESSION_OSTYPE "Win10.0.0"

#define SESSION_CTRL_PORT 9295

#define CTRL_EXPECT_TIMEOUT 5000

typedef enum ctrl_message_type_t {
	CTRL_MESSAGE_TYPE_SESSION_ID = 0x33,
	CTRL_MESSAGE_TYPE_HEARTBEAT_REQ = 0xfe,
	CTRL_MESSAGE_TYPE_HEARTBEAT_REP = 0x1fe,
	CTRL_MESSAGE_TYPE_LOGIN_PIN_REQ = 0x4,
	CTRL_MESSAGE_TYPE_LOGIN_PIN_REP = 0x8004,
	CTRL_MESSAGE_TYPE_LOGIN = 0x5,
	CTRL_MESSAGE_TYPE_GOTO_BED = 0x50,
	CTRL_MESSAGE_TYPE_KEYBOARD_ENABLE = 0xd,
	CTRL_MESSAGE_TYPE_KEYBOARD_ENABLE_TOGGLE = 0x20,
	CTRL_MESSAGE_TYPE_KEYBOARD_OPEN = 0x21,
	CTRL_MESSAGE_TYPE_KEYBOARD_CLOSE_REMOTE = 0x22,
	CTRL_MESSAGE_TYPE_KEYBOARD_TEXT_CHANGE_REQ = 0x23,
	CTRL_MESSAGE_TYPE_KEYBOARD_TEXT_CHANGE_RES = 0x24,
	CTRL_MESSAGE_TYPE_KEYBOARD_CLOSE_REQ = 0x25,
	CTRL_MESSAGE_TYPE_ENABLE_DUALSENSE_FEATURES = 0x13,
	CTRL_MESSAGE_TYPE_GO_HOME = 0x14,
	CTRL_MESSAGE_TYPE_DISPLAYA = 0x1,
	CTRL_MESSAGE_TYPE_DISPLAYB = 0x16,
	CTRL_MESSAGE_TYPE_MIC_CONNECT = 0x30,
	CTRL_MESSAGE_TYPE_MIC_TOGGLE = 0x36,
	CTRL_MESSAGE_TYPE_DISPLAY_DEVICES = 0x910,
	CTRL_MESSAGE_TYPE_SWITCH_TO_STREAM_CONNECTION = 0x34
} CtrlMessageType;

typedef enum ctrl_login_state_t {
	CTRL_LOGIN_STATE_SUCCESS = 0x0,
	CTRL_LOGIN_STATE_PIN_INCORRECT = 0x1
} CtrlLoginState;

struct chiaki_ctrl_message_queue_t
{
	ChiakiCtrlMessageQueue *next;
	uint16_t type;
	uint8_t *payload;
	size_t payload_size;
};

typedef struct ctrl_keyboard_open_t
{
	uint8_t unk[0x1C];
	uint32_t text_length;
} CtrlKeyboardOpenMessage;

typedef struct ctrl_keyboard_text_request_t
{
	uint32_t counter;
	uint32_t text_length1;
	uint8_t unk1[0x8];
	uint8_t unk2[0x10];
	uint32_t text_length2;
} CtrlKeyboardTextRequestMessage;

typedef struct ctrl_keyboard_text_response_t
{
	uint32_t counter;
	uint32_t unk;
	uint32_t text_length1;
	uint32_t unk2;
	uint8_t unk3[0x10];
	uint32_t unk4;
	uint32_t text_length2;
} CtrlKeyboardTextResponseMessage;

/**
 * The offset the ctrl message header starts at inside a rudp message of this subtype.
 *
 * PP414: the comment here used to be takion.c's, copied verbatim from
 * takion_packet_type_mac_offset - which returns a MAC offset and really does answer -1. Both
 * halves were wrong of this function. The value is where the ctrl header begins: the caller
 * reads a four-byte payload size at it and memcpys from it.
 *
 * AND THE DEFAULT IS AN ANSWER, NOT A FALLBACK. The caller is reached with subtype 0x12, 0x26,
 * 0x36 or 0x02; the last two land here and 2 is correct for them. So there is no unknown case
 * and no sentinel - a reader who added `if(offset < 0)` would be writing a dead branch, and a
 * port that returned "no offset" for the default would move where the two commonest subtypes
 * are read from.
 *
 * @return The ctrl header's offset. Always a valid offset; 2 where the subtype names no other.
 */
static int rudp_packet_type_data_offset(uint8_t subtype)
{
	switch(subtype)
	{
		case 0x12:
			return 8;
		case 0x26:
			return 6;
		default:
			return 2;
	}
}

void chiaki_session_send_event(ChiakiSession *session, ChiakiEvent *event);

static void *ctrl_thread_func(void *user);
static ChiakiErrorCode ctrl_message_send(ChiakiCtrl *ctrl, uint16_t type, const uint8_t *payload, size_t payload_size);
static void ctrl_message_received_session_id(ChiakiCtrl *ctrl, uint8_t *payload, size_t payload_size);
static void ctrl_message_received_heartbeat_req(ChiakiCtrl *ctrl, uint8_t *payload, size_t payload_size);
static void ctrl_message_received_login_pin_req(ChiakiCtrl *ctrl, uint8_t *payload, size_t payload_size);
static void ctrl_message_received_login(ChiakiCtrl *ctrl, uint8_t *payload, size_t payload_size);
static void ctrl_message_received_displaya(ChiakiCtrl *ctrl, uint8_t *payload, size_t payload_size);
static void ctrl_message_received_displayb(ChiakiCtrl *ctrl, uint8_t *payload, size_t payload_size);
static void ctrl_message_received_keyboard_open(ChiakiCtrl *ctrl, uint8_t *payload, size_t payload_size);
static void ctrl_message_received_keyboard_close(ChiakiCtrl *ctrl, uint8_t *payload, size_t payload_size);
static void ctrl_message_received_keyboard_text_change(ChiakiCtrl *ctrl, uint8_t *payload, size_t payload_size);
static void ctrl_message_received_switch_to_stream_connection(ChiakiCtrl *ctrl, uint8_t *payload, size_t payload_size);
static ChiakiErrorCode ctrl_connect_tcp(ChiakiCtrl *ctrl);
static void ctrl_disconnect_tcp(ChiakiCtrl *ctrl);
// PP355: declared here because fini is above its definition and now needs it.
static void ctrl_message_queue_free(ChiakiCtrlMessageQueue *queue);

CHIAKI_EXPORT ChiakiErrorCode chiaki_ctrl_init(ChiakiCtrl *ctrl, ChiakiSession *session)
{
	ChiakiErrorCode err = chiaki_mutex_init(&ctrl->notif_mutex, false);
	if(err != CHIAKI_ERR_SUCCESS)
		return err;
	chiaki_mutex_lock(&ctrl->notif_mutex);
	ctrl->session = session;

	ctrl->should_stop = false;
	ctrl->login_pin_entered = false;
	ctrl->login_pin_requested = false;
	ctrl->login_pin = NULL;
	ctrl->login_pin_size = 0;
	ctrl->cant_displaya = false;
	ctrl->cant_displayb = false;
	ctrl->rp_prohibit = false;
	ctrl->msg_queue = NULL;
	ctrl->keyboard_text_counter = 0;
	ctrl->sock = CHIAKI_INVALID_SOCKET;

	err = chiaki_stop_pipe_init(&ctrl->stop_pipe);
	if(err != CHIAKI_ERR_SUCCESS)
		goto error_mutex;

	err = chiaki_stop_pipe_init(&ctrl->notif_pipe);
	if(err != CHIAKI_ERR_SUCCESS)
		goto error_stop_pipe;

	chiaki_mutex_unlock(&ctrl->notif_mutex);
	return err;

error_stop_pipe:
	chiaki_stop_pipe_fini(&ctrl->stop_pipe);
error_mutex:
	chiaki_mutex_unlock(&ctrl->notif_mutex);
	chiaki_mutex_fini(&ctrl->notif_mutex);
	return err;
}

CHIAKI_EXPORT ChiakiErrorCode chiaki_ctrl_start(ChiakiCtrl *ctrl)
{
	ChiakiErrorCode err = chiaki_thread_create(&ctrl->thread, ctrl_thread_func, ctrl);
	if(err != CHIAKI_ERR_SUCCESS)
		return err;

	chiaki_thread_set_name(&ctrl->thread, "Chiaki Ctrl");
	return err;
}

CHIAKI_EXPORT void chiaki_ctrl_stop(ChiakiCtrl *ctrl)
{
	ChiakiErrorCode err = chiaki_mutex_lock(&ctrl->notif_mutex);
	assert(err == CHIAKI_ERR_SUCCESS);
	ctrl->should_stop = true;
	chiaki_stop_pipe_stop(&ctrl->stop_pipe);
	chiaki_stop_pipe_stop(&ctrl->notif_pipe);
	chiaki_mutex_unlock(&ctrl->notif_mutex);
}

CHIAKI_EXPORT ChiakiErrorCode chiaki_ctrl_join(ChiakiCtrl *ctrl)
{
	return chiaki_thread_join(&ctrl->thread, NULL);
}

CHIAKI_EXPORT void chiaki_ctrl_fini(ChiakiCtrl *ctrl)
{
	chiaki_stop_pipe_fini(&ctrl->stop_pipe);
	chiaki_stop_pipe_fini(&ctrl->notif_pipe);
	chiaki_mutex_fini(&ctrl->notif_mutex);
	free(ctrl->login_pin);

	// PP355: whatever is still queued. The drain in the thread's cancelled branch was the only
	// caller of this free, and every other exit from that loop - an overflow, a select error, a recv
	// error, a short rudp message, a finish message - skips it. So anything a screen had queued when
	// the socket died was a linked list nothing freed. login_pin above is the other thing an outside
	// caller allocates into ctrl; ownership at teardown was thought about and one of the two missed.
	while(ctrl->msg_queue)
	{
		ChiakiCtrlMessageQueue *msg = ctrl->msg_queue;
		ctrl->msg_queue = msg->next;
		ctrl_message_queue_free(msg);
	}
}

static void ctrl_message_queue_free(ChiakiCtrlMessageQueue *queue)
{
	free(queue->payload);
	free(queue);
}

CHIAKI_EXPORT ChiakiErrorCode chiaki_ctrl_send_message(ChiakiCtrl *ctrl, uint16_t type, const uint8_t *payload, size_t payload_size)
{
	ChiakiCtrlMessageQueue *queue = CHIAKI_NEW(ChiakiCtrlMessageQueue);
	if(!queue)
		return CHIAKI_ERR_MEMORY;
	queue->next = NULL;
	queue->type = type;
	if(payload)
	{
		queue->payload = malloc(payload_size);
		if(!queue->payload)
		{
			free(queue);
			return CHIAKI_ERR_MEMORY;
		}
		memcpy(queue->payload, payload, payload_size);
		queue->payload_size = payload_size;
	}
	else
	{
		queue->payload = NULL;
		queue->payload_size = 0;
	}
	ChiakiErrorCode err = chiaki_mutex_lock(&ctrl->notif_mutex);
	assert(err == CHIAKI_ERR_SUCCESS);
	if(!ctrl->msg_queue)
		ctrl->msg_queue = queue;
	else
	{
		ChiakiCtrlMessageQueue *c = ctrl->msg_queue;
		while(c->next)
			c = c->next;
		c->next = queue;
	}
	chiaki_mutex_unlock(&ctrl->notif_mutex);
	chiaki_stop_pipe_stop(&ctrl->notif_pipe);
	return CHIAKI_ERR_SUCCESS;
}

CHIAKI_EXPORT ChiakiErrorCode chiaki_ctrl_set_login_pin(ChiakiCtrl *ctrl, const uint8_t *pin, size_t pin_size)
{
	uint8_t *buf = malloc(pin_size);
	if(!buf)
	{
		// PP345: said out loud and answered. This return used to be silent and void, so the PIN
		// was dropped here, login_pin_entered was never set, the ctrl thread never sent it, and
		// the console asked again - which PP335 established is the only thing that says wrong.
		CHIAKI_LOGE(ctrl->session->log, "Ctrl failed to allocate %llu bytes for the Login PIN",
				(unsigned long long)pin_size);
		return CHIAKI_ERR_MEMORY;
	}
	memcpy(buf, pin, pin_size);
	ChiakiErrorCode err = chiaki_mutex_lock(&ctrl->notif_mutex);
	assert(err == CHIAKI_ERR_SUCCESS);
	if(ctrl->login_pin_entered)
		free(ctrl->login_pin);
	ctrl->login_pin_entered = true;
	ctrl->login_pin = buf;
	ctrl->login_pin_size = pin_size;
	chiaki_stop_pipe_stop(&ctrl->notif_pipe);
	chiaki_mutex_unlock(&ctrl->notif_mutex);
	return CHIAKI_ERR_SUCCESS;
}

CHIAKI_EXPORT ChiakiErrorCode chiaki_ctrl_goto_bed(ChiakiCtrl *ctrl)
{
	return chiaki_ctrl_send_message(ctrl, CTRL_MESSAGE_TYPE_GOTO_BED, NULL, 0);
}

CHIAKI_EXPORT ChiakiErrorCode chiaki_ctrl_keyboard_set_text(ChiakiCtrl *ctrl, const char *text)
{
	const uint32_t length = strlen(text);
	const size_t payload_size = sizeof(CtrlKeyboardTextRequestMessage) + length;

	uint8_t *payload = malloc(payload_size);
	if(!payload)
		return CHIAKI_ERR_MEMORY;
	memset(payload, 0, payload_size);
	memcpy(payload + sizeof(CtrlKeyboardTextRequestMessage), text, length);

	CtrlKeyboardTextRequestMessage *msg = (CtrlKeyboardTextRequestMessage *)payload;
	msg->counter = htonl(++ctrl->keyboard_text_counter);
	msg->text_length1 = htonl(length);
	msg->text_length2 = htonl(length);

	ChiakiErrorCode err;
	err = chiaki_ctrl_send_message(ctrl, CTRL_MESSAGE_TYPE_KEYBOARD_TEXT_CHANGE_REQ, payload, payload_size);

	free(payload);
	return err;
}

CHIAKI_EXPORT ChiakiErrorCode chiaki_ctrl_keyboard_accept(ChiakiCtrl *ctrl)
{
	const uint8_t accept[4] = { 0x00, 0x00, 0x00, 0x00 };
	return chiaki_ctrl_send_message(ctrl, CTRL_MESSAGE_TYPE_KEYBOARD_CLOSE_REQ, accept, 4);
}

CHIAKI_EXPORT ChiakiErrorCode chiaki_ctrl_keyboard_reject(ChiakiCtrl *ctrl)
{
	const uint8_t reject[4] = { 0x00, 0x00, 0x00, 0x01 };
	return chiaki_ctrl_send_message(ctrl, CTRL_MESSAGE_TYPE_KEYBOARD_CLOSE_REQ, reject, 4);
}

static ChiakiErrorCode ctrl_connect(ChiakiCtrl *ctrl);
static void ctrl_message_received(ChiakiCtrl *ctrl, uint16_t msg_type, uint8_t *payload, size_t payload_size);

static void ctrl_failed(ChiakiCtrl *ctrl, ChiakiQuitReason reason)
{
	ChiakiErrorCode mutex_err = chiaki_mutex_lock(&ctrl->session->state_mutex);
	assert(mutex_err == CHIAKI_ERR_SUCCESS);

	// PP348: guarded, the way session_thread_func's ctrl_failed label already guards. This assigned
	// unconditionally on all six paths that reach it, so a session refused for a reason the user
	// could act on - the console already in use, a version mismatch - had that replaced with
	// CTRL_UNKNOWN by the ctrl connection failing afterwards, which it will, since there is no
	// session left to carry it.
	//
	// The guard costs nothing it was doing: a ctrl failure on a healthy session finds NONE here and
	// records itself as before. What it stops is only the overwrite.
	if(ctrl->session->quit_reason == CHIAKI_QUIT_REASON_NONE)
		ctrl->session->quit_reason = reason;

	// Unconditional: this is how the session thread learns ctrl died, whatever the reason says.
	ctrl->session->ctrl_failed = true;
	chiaki_mutex_unlock(&ctrl->session->state_mutex);
	chiaki_cond_signal(&ctrl->session->state_cond);
}

// PP385: a send that failed anywhere in a void handler, answered the way PP383 answers it.
//
// ctrl_message_send spends crypt_counter_local at ENCRYPT time, so any failure of it has already
// taken a counter value the console never saw and the two sides no longer agree. That is true of
// the queued drain, the login PIN and the heartbeat reply alike - none of them is a feature that
// merely did not happen. These handlers are void and cannot report, so ctrl_failed is what they
// have, and it is the honest answer rather than carrying on until the first unreadable message
// arrives and is blamed on the protocol.
#define CTRL_SEND_OR_FAIL(call, what) do { \
		ChiakiErrorCode ctrl_send_err = (call); \
		if(ctrl_send_err != CHIAKI_ERR_SUCCESS) \
		{ \
			CHIAKI_LOGE(ctrl->session->log, "Ctrl failed to send %s: %s", \
					(what), chiaki_error_string(ctrl_send_err)); \
			ctrl_failed(ctrl, CHIAKI_QUIT_REASON_CTRL_UNKNOWN); \
		} \
	} while(0)

// PP385: and the fallback session id, which is a different failure with a different owner.
//
// It sends nothing - it generates an id locally and stores it - so no counter moves and nothing
// desyncs. What a failure means is that the session has no id at all, and the session thread
// ALREADY ends on that: its `if(!session->ctrl_session_id_received)` is what carries this. So what
// was missing here is only the sentence saying which of the two happened, on all four rungs.
#define CTRL_FALLBACK_SESSION_ID(ctrl) do { \
		ChiakiErrorCode fallback_err = ctrl_message_set_fallback_session_id(ctrl); \
		if(fallback_err != CHIAKI_ERR_SUCCESS) \
			CHIAKI_LOGE((ctrl)->session->log, \
					"Ctrl could not generate a fallback session id: %s", \
					chiaki_error_string(fallback_err)); \
	} while(0)

static void ctrl_disconnect_tcp(ChiakiCtrl *ctrl)
{
	if(!CHIAKI_SOCKET_IS_INVALID(ctrl->sock))
	{
		CHIAKI_SOCKET_CLOSE(ctrl->sock);
		ctrl->sock = CHIAKI_INVALID_SOCKET;
	}
}

static ChiakiErrorCode ctrl_connect_tcp(ChiakiCtrl *ctrl)
{
	ChiakiSession *session = ctrl->session;
	struct addrinfo *addr = session->connect_info.host_addrinfo_selected;
	struct sockaddr *sa = malloc(addr->ai_addrlen);
	if(!sa)
	{
		CHIAKI_LOGE(session->log, "Ctrl failed to alloc sockaddr");
		// PP415: the reason, the way PP345 gave one to the other allocation on this path. This
		// returned without reporting, and ctrl_thread_func answers any error from ctrl_connect with
		// CTRL_CONNECT_FAILED - so a machine out of memory told the user the network had failed.
		// PP348's guard is what makes recording it here enough: the generic reason is then dropped.
		ctrl_failed(ctrl, CHIAKI_QUIT_REASON_CTRL_MEMORY);
		return CHIAKI_ERR_MEMORY;
	}
	memcpy(sa, addr->ai_addr, addr->ai_addrlen);

	if(sa->sa_family == AF_INET)
		((struct sockaddr_in *)sa)->sin_port = htons(SESSION_CTRL_PORT);
	else if(sa->sa_family == AF_INET6)
		((struct sockaddr_in6 *)sa)->sin6_port = htons(SESSION_CTRL_PORT);
	else
	{
		free(sa);
		CHIAKI_LOGE(session->log, "Ctrl got invalid sockaddr");
		return CHIAKI_ERR_INVALID_DATA;
	}

	chiaki_socket_t sock = socket(sa->sa_family, SOCK_STREAM, IPPROTO_TCP);
	if(CHIAKI_SOCKET_IS_INVALID(sock))
	{
		free(sa);
		CHIAKI_LOGE(session->log, "Session ctrl socket creation failed.");
		ctrl_failed(ctrl, CHIAKI_QUIT_REASON_CTRL_UNKNOWN);
		return CHIAKI_ERR_NETWORK;
	}

	ChiakiErrorCode err = chiaki_socket_set_nonblock(sock, true);
	if(err != CHIAKI_ERR_SUCCESS)
	{
		CHIAKI_LOGE(session->log, "Failed to set ctrl socket to non-blocking: %s", chiaki_error_string(err));
		free(sa);
		CHIAKI_SOCKET_CLOSE(sock);
		ctrl_failed(ctrl, CHIAKI_QUIT_REASON_CTRL_UNKNOWN);
		return err;
	}

	chiaki_mutex_unlock(&ctrl->notif_mutex);
	err = chiaki_stop_pipe_connect(&ctrl->stop_pipe, sock, sa, addr->ai_addrlen, 5000);
	chiaki_mutex_lock(&ctrl->notif_mutex);
	free(sa);
	if(err != CHIAKI_ERR_SUCCESS)
	{
		if(err == CHIAKI_ERR_CANCELED)
		{
			if(ctrl->should_stop)
				CHIAKI_LOGI(session->log, "Ctrl requested to stop while connecting");
			else
				CHIAKI_LOGE(session->log, "Ctrl notif pipe signaled without should_stop during connect");
			if(!CHIAKI_SOCKET_IS_INVALID(sock))
			{
				CHIAKI_SOCKET_CLOSE(sock);
				sock = CHIAKI_INVALID_SOCKET;
			}
		}
		else
		{
			CHIAKI_LOGE(session->log, "Ctrl connect failed: %s", chiaki_error_string(err));
			ChiakiQuitReason quit_reason = err == CHIAKI_ERR_CONNECTION_REFUSED ? CHIAKI_QUIT_REASON_CTRL_CONNECTION_REFUSED : CHIAKI_QUIT_REASON_CTRL_UNKNOWN;
			ctrl_failed(ctrl, quit_reason);
			if(!CHIAKI_SOCKET_IS_INVALID(sock))
			{
				CHIAKI_SOCKET_CLOSE(sock);
				sock = CHIAKI_INVALID_SOCKET;
			}
		}
		return err;
	}

	CHIAKI_LOGI(session->log, "Ctrl connected to %s:%d", session->connect_info.hostname, SESSION_CTRL_PORT);
	ctrl->sock = sock;
	return CHIAKI_ERR_SUCCESS;
}

static void *ctrl_thread_func(void *user)
{
	ChiakiCtrl *ctrl = user;
	chiaki_thread_set_affinity(CHIAKI_THREAD_NAME_CTRL);

	ChiakiErrorCode err = chiaki_mutex_lock(&ctrl->notif_mutex);
	assert(err == CHIAKI_ERR_SUCCESS);

	err = ctrl_connect(ctrl);
	if(err != CHIAKI_ERR_SUCCESS)
	{
		ctrl_failed(ctrl, CHIAKI_QUIT_REASON_CTRL_CONNECT_FAILED);
		chiaki_mutex_unlock(&ctrl->notif_mutex);
		return NULL;
	}

	CHIAKI_LOGI(ctrl->session->log, "Ctrl connected");

	while(true)
	{
		bool overflow = false;
		while(ctrl->recv_buf_size >= 8)
		{
			// PP382: aligned on purpose, and one of the four in lib/src that are. recv_buf carries
			// __attribute__((aligned(__alignof__(uint32_t)))) in ctrl.h for exactly this read, so
			// the plain cast is the guarantee being used rather than one being assumed.
			uint32_t payload_size = *((uint32_t *)ctrl->recv_buf);
			payload_size = ntohl(payload_size);

			// PP339/PP346: the bound is on payload_size ALONE, before anything is added to it.
			//
			// Both tests used to be written on `8 + payload_size`, and that sum is unsigned 32-bit:
			// an announced length of 0xFFFFFFF8 or more wrapped it to between zero and seven. The
			// loop only runs while recv_buf_size is at least eight, so the first test was false, the
			// overflow check was never reached, and the message was dispatched with the length as
			// announced - into an in-place decrypt over four gigabytes of a 512-byte buffer. The
			// header is plaintext, so whatever holds this connection chose that number.
			if(payload_size > sizeof(ctrl->recv_buf) - 8)
			{
				CHIAKI_LOGE(ctrl->session->log, "Ctrl buffer overflow!");
				overflow = true;
				break;
			}

			// Past the bound above, this sum cannot exceed the buffer and cannot wrap.
			if(ctrl->recv_buf_size < 8 + payload_size)
				break;

			uint16_t msg_type = *((chiaki_unaligned_uint16_t *)(ctrl->recv_buf + 4));
			msg_type = ntohs(msg_type);

			ctrl_message_received(ctrl, msg_type, ctrl->recv_buf + 8, (size_t)payload_size);
			ctrl->recv_buf_size -= 8 + payload_size;
			if(ctrl->recv_buf_size > 0)
				memmove(ctrl->recv_buf, ctrl->recv_buf + 8 + payload_size, ctrl->recv_buf_size);
		}

		if(overflow)
		{
			ctrl_failed(ctrl, CHIAKI_QUIT_REASON_CTRL_UNKNOWN);
			break;
		}

		if(ctrl->should_stop || ctrl->msg_queue || ctrl->login_pin_entered)
		{
			err = CHIAKI_ERR_CANCELED;
		}
		else
		{
			chiaki_stop_pipe_reset(&ctrl->notif_pipe);
			chiaki_mutex_unlock(&ctrl->notif_mutex);
			if(ctrl->session->rudp)
				err = chiaki_rudp_stop_pipe_select_single(ctrl->session->rudp, &ctrl->notif_pipe, UINT64_MAX);
			else
				err = chiaki_stop_pipe_select_single(&ctrl->notif_pipe, ctrl->sock, false, UINT64_MAX);
			chiaki_mutex_lock(&ctrl->notif_mutex);
		}

		if(err == CHIAKI_ERR_CANCELED)
		{
			while(ctrl->msg_queue)
			{
				ChiakiCtrlMessageQueue *msg = ctrl->msg_queue;
				ctrl->msg_queue = msg->next;
				chiaki_mutex_unlock(&ctrl->notif_mutex);
				// PP385: the drain's send, read. The node is already unlinked and is freed two
				// lines down, so there is nothing to put back and no retry to take - which is why
				// this one LEAVES rather than carrying on: the counter has moved and every message
				// still queued would spend another value into the same gap.
				//
				// The type is copied BEFORE the free, because the log below wants it and
				// ctrl_message_queue_free is what ends the node it lives in.
				uint16_t drain_type = msg->type;
				ChiakiErrorCode drain_err = ctrl_message_send(ctrl, msg->type, msg->payload, msg->payload_size);
				ctrl_message_queue_free(msg);
				chiaki_mutex_lock(&ctrl->notif_mutex);
				if(drain_err != CHIAKI_ERR_SUCCESS)
				{
					// PP416: the rest of the queue goes too, which is what PP385's rule above
					// actually requires. The break alone left the inner loop only - and the outer
					// loop's `should_stop || msg_queue || login_pin_entered` test is then true
					// BECAUSE the queue is not empty, so it took the cancelled branch and re-entered
					// this drain, sending the next message into the same gap. ctrl_failed does not
					// set should_stop, so how many still went out was a race with the session thread
					// getting round to stopping ctrl.
					//
					// Nothing is lost by dropping them: the node is unlinked and freed before its
					// error is read, so there was never a retry to take. Same shape as PP355's
					// teardown drain, for the same reason.
					size_t dropped = 0;
					while(ctrl->msg_queue)
					{
						ChiakiCtrlMessageQueue *rest = ctrl->msg_queue;
						ctrl->msg_queue = rest->next;
						ctrl_message_queue_free(rest);
						dropped++;
					}
					CHIAKI_LOGE(ctrl->session->log,
							"Ctrl failed to send queued message of type %#x, dropping it and %zu still queued, and leaving the drain: %s",
							(unsigned int)drain_type, dropped, chiaki_error_string(drain_err));
					chiaki_mutex_unlock(&ctrl->notif_mutex);
					ctrl_failed(ctrl, CHIAKI_QUIT_REASON_CTRL_UNKNOWN);
					chiaki_mutex_lock(&ctrl->notif_mutex);
					break;
				}
			}

			if(ctrl->login_pin_entered)
			{
				CHIAKI_LOGI(ctrl->session->log, "Ctrl received entered Login PIN, sending to console");
				uint8_t *login_pin = ctrl->login_pin;
				size_t login_pin_size = ctrl->login_pin_size;
				ctrl->login_pin_entered = false;
				ctrl->login_pin = NULL;
				ctrl->login_pin_size = 0;
				chiaki_mutex_unlock(&ctrl->notif_mutex);
				// PP385: the PIN on the wire. PP345 made the HANDOVER report a failure; this is the
				// send, and its failure has the same ending - the console never gets the PIN, asks
				// again, and PP335 says a second request is the only thing that says "wrong".
				CTRL_SEND_OR_FAIL(
						ctrl_message_send(ctrl, CTRL_MESSAGE_TYPE_LOGIN_PIN_REP, login_pin, login_pin_size),
						"the login PIN");
				free(login_pin);
				chiaki_mutex_lock(&ctrl->notif_mutex);
				continue;
			}

			if(ctrl->should_stop)
			{
				CHIAKI_LOGI(ctrl->session->log, "Ctrl requested to stop");
				break;
			}

			continue;
		}
		else if(err != CHIAKI_ERR_SUCCESS)
		{
			CHIAKI_LOGE(ctrl->session->log, "Ctrl select error: %s", chiaki_error_string(err));
			break;
		}

		CHIAKI_SSIZET_TYPE received = 0;
		if(ctrl->session->rudp)
		{
			RudpMessage message;
			uint16_t remote_counter = 0;
			uint16_t ack_counter = 0;
			// PP354: the whole datagram, not the datagram less how full a DIFFERENT buffer is. The
			// rudp socket is UDP, so a receive buffer shorter than the datagram truncates it and
			// discards the remainder - and recv_buf is at its fullest exactly while a ctrl message
			// is mid-reassembly, which is when this was shortest. What is copied OUT of the parsed
			// message into recv_buf is bounded by recv_buf's own room, below, where PP347 put it.
			err = chiaki_rudp_recv_only(ctrl->session->rudp, CHIAKI_CTRL_RUDP_DATAGRAM_SIZE, &message);
			if(err != CHIAKI_ERR_SUCCESS)
			{
				CHIAKI_LOGE(ctrl->session->log, "Failed to receive Rudp ctrl packet");
				ctrl_failed(ctrl, CHIAKI_QUIT_REASON_CTRL_UNKNOWN);
				break;
			}
			if(message.data_size < 4)
			{
				CHIAKI_LOGE(ctrl->session->log, "Rudp ctrl message response too small");
				chiaki_rudp_print_message(ctrl->session->rudp, &message);
				ctrl_failed(ctrl, CHIAKI_QUIT_REASON_CTRL_UNKNOWN);
				break;
			}
			remote_counter = message.remote_counter;
			while(true)
			{
				switch(message.subtype) // wrong but works ...
				{
					case 0x12:
					case 0x26:
					case 0x36:
						ack_counter = ntohs(*((chiaki_unaligned_uint16_t *)(message.data + 2)));
						chiaki_rudp_ack_packet(ctrl->session->rudp, ack_counter);
					case 0x02:
						chiaki_rudp_send_ack_message(ctrl->session->rudp, remote_counter);
						int offset = rudp_packet_type_data_offset(message.subtype);
						// ctrl message header is 8 bytes
						if((message.data_size - offset) < 8)
							break;
						// check if message is ctrl message by making sure the payload size (size of message - 8 byte header is correct)
						// PP382: message.data is a heap pointer and offset is 2, 6 or 8 off the wire,
						// so nothing here is four-byte aligned by anything but luck.
						uint32_t ctrl_payload_size = ntohl(*(chiaki_unaligned_uint32_t*)(message.data + offset));
						// PP347: the destination's remaining room, checked. The test above says the
						// message is well formed and nothing about whether it fits: rudp_recv_buf is
						// 520 bytes and recv_buf is 512, so one well-formed message can be larger
						// than the buffer it is copied into - and recv_buf_size is whatever the
						// framing loop left behind, which raises the offset it is copied to.
						if((message.data_size - offset - 8) == ctrl_payload_size
								&& (size_t)(message.data_size - offset) <= sizeof(ctrl->recv_buf) - ctrl->recv_buf_size)
						{
							memcpy(ctrl->recv_buf + ctrl->recv_buf_size, message.data + offset, message.data_size - offset);
							ctrl->recv_buf_size += message.data_size - offset;
						}
						break;
					case 0x24:
						ack_counter = ntohs(*((chiaki_unaligned_uint16_t *)(message.data + 2)));
						chiaki_rudp_ack_packet(ctrl->session->rudp, ack_counter);
						break;
					case 0xC0:
						CHIAKI_LOGI(ctrl->session->log, "Received rudp finish message, stopping ctrl.");
						ctrl_failed(ctrl, CHIAKI_QUIT_REASON_CTRL_UNKNOWN);
						break;
					default:
						CHIAKI_LOGI(ctrl->session->log, "Received message of unknown type: 0x%04x", message.type);
						// PP413: no chiaki_rudp_ack_packet here. It stood above this line acking
						// ack_counter, which this arm never reads - the arms above read it off
						// message.data + 2 first, and this one leaves whatever a sibling submessage
						// of the same datagram put there, or zero where there was none.
						//
						// Zero is not a harmless value: chiaki_rudp_send_buffer_ack frees every
						// buffered packet at or older than the acked seqnum, and older-than-zero is
						// true for 32769 through 65535 - so an unrecognised submessage arriving once
						// the send counter has passed halfway discarded nearly half the resend
						// buffer, and any packet in there the console never got was never resent.
						//
						// Reading message.data + 2 like the arms above would invent a wire layout
						// for the one case defined by not knowing it. The ack below is the one the
						// console actually sees, off a counter this arm did read.
						chiaki_rudp_send_ack_message(ctrl->session->rudp, remote_counter);
						// we already checked before if data size was at least 4
						int offset2 = 4;
						// ctrl message header is 8 bytes
						if((message.data_size - offset2) < 8)
							break;
						// PP382, as above.
						uint32_t ctrl_payload_size2 = ntohl(*(chiaki_unaligned_uint32_t*)(message.data + offset2));
						// PP347: the same bound as the arm above, for the same reason.
						if((message.data_size - offset2 - 8) == ctrl_payload_size2
								&& (size_t)(message.data_size - offset2) <= sizeof(ctrl->recv_buf) - ctrl->recv_buf_size)
						{
							memcpy(ctrl->recv_buf + ctrl->recv_buf_size, message.data + offset2, message.data_size - offset2);
							ctrl->recv_buf_size += message.data_size - offset2;
						}
						break;
				}
				if(message.subMessage)
				{
					if(message.data)
					{
						free(message.data);
						message.data = NULL;
					}
					RudpMessage *tmp = message.subMessage;
					memcpy(&message, message.subMessage, sizeof(RudpMessage));
					free(tmp);
				}
				else
				{
					chiaki_rudp_message_pointers_free(&message);
					break;
				}
			}
		}
		else
		{
			received = recv(ctrl->sock, (CHIAKI_SOCKET_BUF_TYPE)ctrl->recv_buf + ctrl->recv_buf_size, sizeof(ctrl->recv_buf) - ctrl->recv_buf_size, 0);
			if(received <= 0)
			{
				if(received < 0)
				{
					CHIAKI_LOGE(ctrl->session->log, "Ctrl failed to recv: " CHIAKI_SOCKET_ERROR_FMT, CHIAKI_SOCKET_ERROR_VALUE);
					ctrl_failed(ctrl, CHIAKI_QUIT_REASON_CTRL_UNKNOWN);
				}
				break;
			}
			CHIAKI_LOGI(ctrl->session->log, "CTRL RECEIVED");
			chiaki_log_hexdump(ctrl->session->log, CHIAKI_LOG_INFO, ctrl->recv_buf + ctrl->recv_buf_size, received);
		}


		ctrl->recv_buf_size += received;
	}

	chiaki_mutex_unlock(&ctrl->notif_mutex);
	if(!ctrl->session->rudp)
	{
		if(!CHIAKI_SOCKET_IS_INVALID(ctrl->sock))
		{
			CHIAKI_SOCKET_CLOSE(ctrl->sock);
			ctrl->sock = CHIAKI_INVALID_SOCKET;
		}
	}

	return NULL;
}

static ChiakiErrorCode ctrl_message_send(ChiakiCtrl *ctrl, uint16_t type, const uint8_t *payload, size_t payload_size)
{
	if(!(payload_size == 0 || payload))
		return CHIAKI_ERR_INVALID_DATA;

	CHIAKI_LOGV(ctrl->session->log, "Ctrl sending message type %x, size %llx\n",
			(unsigned int)type, (unsigned long long)payload_size);
	if(payload)
		chiaki_log_hexdump(ctrl->session->log, CHIAKI_LOG_VERBOSE, payload, payload_size);

	// PP323: here and not one line lower. The next statement encrypts into `enc`, and a recording
	// taken after it holds ciphertext that replays against nothing.
	chiaki_message_tap_emit(
			CHIAKI_MESSAGE_TAP_SENT, CHIAKI_MESSAGE_TAP_CHANNEL_CTRL, type, payload, payload_size);

	uint8_t *enc = NULL;
	if(payload)
	{
		ChiakiErrorCode err;
		enc = malloc(payload_size);
		if(!enc)
			return CHIAKI_ERR_MEMORY;
		if(ctrl->session->rudp && type == CTRL_MESSAGE_TYPE_LOGIN_PIN_REP)
		{
			uint16_t local_counter = ctrl->crypt_counter_local++;
			err = chiaki_rpcrypt_encrypt(&ctrl->session->rpcrypt, local_counter - 1, payload, enc, payload_size);
		}
		else
			err = chiaki_rpcrypt_encrypt(&ctrl->session->rpcrypt, ctrl->crypt_counter_local++, payload, enc, payload_size);
		if(err != CHIAKI_ERR_SUCCESS)
		{
			CHIAKI_LOGE(ctrl->session->log, "Ctrl failed to encrypt payload");
			free(enc);
			return err;
		}
	}

#ifdef __GNUC__
	__attribute__((aligned(__alignof__(uint32_t))))
#endif
	uint8_t header[8];
	// PP382: the other three deliberate ones. The attribute three lines above is what makes these
	// plain casts legal, and it is there because of them.
	*((uint32_t *)header) = htonl((uint32_t)payload_size);
	*((uint16_t *)(header + 4)) = htons(type);
	*((uint16_t *)(header + 6)) = 0;

	if(ctrl->session->rudp)
	{
		// PP341: size_t and not uint8_t. The sum was truncated into eight bits while both copies
		// below used the real lengths, so a payload of 248 wrapped the length to zero and the
		// header copy alone wrote eight bytes past the buffer. Reachable through
		// chiaki_session_set_login_pin, which takes an unbounded size_t from its caller.
		size_t buf_size = 8 + payload_size;
		uint8_t buf[buf_size];
		memcpy(buf, header, 8);
		if(enc)
			memcpy(buf + 8, enc, payload_size);
		free(enc);
		ChiakiErrorCode err;
		err = chiaki_rudp_send_ctrl_message(ctrl->session->rudp, buf, buf_size);
		if(err != CHIAKI_ERR_SUCCESS)
		{
			CHIAKI_LOGE(ctrl->session->log, "Failed to send Ctrl Message");
			return err;
		}
	}
	else
	{
			ChiakiErrorCode err = chiaki_send_fully(&ctrl->stop_pipe, ctrl->sock, header, sizeof(header), CTRL_EXPECT_TIMEOUT);
		if(err != CHIAKI_ERR_SUCCESS)
		{
			CHIAKI_LOGE(ctrl->session->log, "Failed to send Ctrl Message Header");
			free(enc);
			return err;
		}

		if(enc)
		{
				err = chiaki_send_fully(&ctrl->stop_pipe, ctrl->sock, enc, payload_size, CTRL_EXPECT_TIMEOUT);
			free(enc);
			if(err != CHIAKI_ERR_SUCCESS)
			{
				CHIAKI_LOGE(ctrl->session->log, "Failed to send Ctrl Message Payload");
				return err;
			}
		}
	}
	return CHIAKI_ERR_SUCCESS;
}

CHIAKI_EXPORT ChiakiErrorCode ctrl_message_go_home(ChiakiCtrl *ctrl)
{
	CHIAKI_LOGV(ctrl->session->log, "Ctrl sending go to home screen message");
	uint8_t home[0x10] = {0x00, 0xff, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00};
	ChiakiErrorCode err = ctrl_message_send(ctrl, CTRL_MESSAGE_TYPE_GO_HOME, home, 0x10);
	if(err != CHIAKI_ERR_SUCCESS)
	{
		CHIAKI_LOGE(ctrl->session->log, "Failed to go to home screen");
		return err;
	}
	return CHIAKI_ERR_SUCCESS;
}

CHIAKI_EXPORT ChiakiErrorCode ctrl_message_connect_microphone(ChiakiCtrl *ctrl)
{
	CHIAKI_LOGV(ctrl->session->log, "Ctrl sending microphone connect message");
	uint8_t connect[2] = {0x00, 0x00};
	ChiakiErrorCode err = ctrl_message_send(ctrl, CTRL_MESSAGE_TYPE_MIC_CONNECT, connect, 0x2);

	if(err != CHIAKI_ERR_SUCCESS)
	{
		CHIAKI_LOGE(ctrl->session->log, "Failed to connect mic");
		return err;
	}
	return CHIAKI_ERR_SUCCESS;
}

CHIAKI_EXPORT ChiakiErrorCode ctrl_message_toggle_microphone(ChiakiCtrl *ctrl, bool muted)
{
	// PP361: the sentence said the opposite of the bytes. muted writes zero into the third byte and
	// logged "unmute", so a verbose log read while chasing a microphone problem contradicted the
	// wire. The wire was right; only the word was wrong.
	CHIAKI_LOGV(ctrl->session->log, "Ctrl sending toggle microphone mute message: %s", muted ? "mute": "unmute");
	uint8_t toggle[0x4] = {0, 1, 1, 89};
	if(muted)
		toggle[2] = 0;
	ChiakiErrorCode err = ctrl_message_send(ctrl, CTRL_MESSAGE_TYPE_MIC_TOGGLE, toggle, 0x4);

	if(err != CHIAKI_ERR_SUCCESS)
	{
		CHIAKI_LOGE(ctrl->session->log, "Failed to toggle mic mute");
		return err;
	}
	return CHIAKI_ERR_SUCCESS;
}

CHIAKI_EXPORT ChiakiErrorCode ctrl_message_set_fallback_session_id(ChiakiCtrl *ctrl)
{
	char fallback_session_id[80];
	int64_t time_seconds = chiaki_time_now_monotonic_ms() / 1000;
	int len = snprintf(fallback_session_id, 16, "%"PRId64, time_seconds);
	if(len < 0)
	{
		CHIAKI_LOGI(ctrl->session->log, "Error writing time to fallback session id");
		return CHIAKI_ERR_UNKNOWN;
	}
	uint8_t rand_bytes[48];
	ChiakiErrorCode err = chiaki_random_bytes_crypt(rand_bytes, 48);
	if(err != CHIAKI_ERR_SUCCESS)
	{
		CHIAKI_LOGE(ctrl->session->log, "Couldn't generate random bytes to use for fallback session Id with error: %s.", chiaki_error_string(err));
		return err;
	}
	err = chiaki_base64_encode(rand_bytes, sizeof(rand_bytes), fallback_session_id + len, 65);
	if(err != CHIAKI_ERR_SUCCESS)
	{
		CHIAKI_LOGE(ctrl->session->log, "Couldn't base64 encode rand_bytes for fallback session Id with error: %s", chiaki_error_string(err));
		return err;
	}
	chiaki_mutex_lock(&ctrl->session->state_mutex);
	if(ctrl->session->ctrl_session_id_received)
	{
		CHIAKI_LOGW(ctrl->session->log, "Aleady received session Id don't need fallback.");
		chiaki_mutex_unlock(&ctrl->session->state_mutex);
		return err;
	}
	memcpy(ctrl->session->session_id, fallback_session_id, sizeof(fallback_session_id));
	CHIAKI_LOGI(ctrl->session->log, "Ctrl set fallback session Id %s", ctrl->session->session_id);
	ctrl->session->ctrl_session_id_received = true;
	chiaki_mutex_unlock(&ctrl->session->state_mutex);
	chiaki_cond_signal(&ctrl->session->state_cond);
	return err;
}

static void ctrl_message_received(ChiakiCtrl *ctrl, uint16_t msg_type, uint8_t *payload, size_t payload_size)
{
	if(payload_size > 0)
	{
		ChiakiErrorCode err = chiaki_rpcrypt_decrypt(&ctrl->session->rpcrypt, ctrl->crypt_counter_remote++, payload, payload, payload_size);
		if(err != CHIAKI_ERR_SUCCESS)
		{
			CHIAKI_LOGE(ctrl->session->log, "Failed to decrypt payload for Ctrl Message type %#x", msg_type);
			return;
		}
	}

	CHIAKI_LOGV(ctrl->session->log, "Ctrl received message of type %#x, size %#llx", (unsigned int)msg_type, (unsigned long long)payload_size);
	if(payload_size > 0)
		chiaki_log_hexdump(ctrl->session->log, CHIAKI_LOG_VERBOSE, payload, payload_size);

	// PP323: after the decrypt above and before the switch below, which is the only window in which
	// this message is plaintext AND still one thing rather than a handler's arguments.
	chiaki_message_tap_emit(
			CHIAKI_MESSAGE_TAP_RECEIVED, CHIAKI_MESSAGE_TAP_CHANNEL_CTRL,
			msg_type, payload, payload_size);

	switch(msg_type)
	{
		case CTRL_MESSAGE_TYPE_SESSION_ID:
			ctrl_message_received_session_id(ctrl, payload, payload_size);
			// PP383: a failed burst ends the channel. This handler is void and there is nothing to
			// return through, but a counter that has drifted means nothing further will decrypt -
			// so ctrl_failed is the honest answer rather than carrying on and reporting the first
			// unreadable message as a protocol error.
			if(ctrl_enable_features(ctrl) != CHIAKI_ERR_SUCCESS)
				ctrl_failed(ctrl, CHIAKI_QUIT_REASON_CTRL_UNKNOWN);
			break;
		case CTRL_MESSAGE_TYPE_HEARTBEAT_REQ:
			ctrl_message_received_heartbeat_req(ctrl, payload, payload_size);
			break;
		case CTRL_MESSAGE_TYPE_LOGIN_PIN_REQ:
			ctrl_message_received_login_pin_req(ctrl, payload, payload_size);
			break;
		case CTRL_MESSAGE_TYPE_LOGIN:
			ctrl_message_received_login(ctrl, payload, payload_size);
			break;
		case CTRL_MESSAGE_TYPE_KEYBOARD_OPEN:
			ctrl_message_received_keyboard_open(ctrl, payload, payload_size);
			break;
		case CTRL_MESSAGE_TYPE_KEYBOARD_TEXT_CHANGE_RES:
			ctrl_message_received_keyboard_text_change(ctrl, payload, payload_size);
			break;
		case CTRL_MESSAGE_TYPE_KEYBOARD_CLOSE_REMOTE:
			ctrl_message_received_keyboard_close(ctrl, payload, payload_size);
			break;
		case CTRL_MESSAGE_TYPE_DISPLAYA:
			ctrl_message_received_displaya(ctrl, payload, payload_size);
			break;
		case CTRL_MESSAGE_TYPE_DISPLAYB:
			ctrl_message_received_displayb(ctrl, payload, payload_size);
			break;
		case CTRL_MESSAGE_TYPE_SWITCH_TO_STREAM_CONNECTION:
			ctrl_message_received_switch_to_stream_connection(ctrl, payload, payload_size);
			break;
		default:
			// PP331: the number, at a level meaning unhandled. The naming line above was commented
			// out since the fork and the hexdump ran at WARNING, so this branch printed eight
			// anonymous bytes at a level meaning something is broken. It is reached on every
			// session: PP297's capture recorded type 0x41 shortly after DISPLAYB on a connection
			// that stayed up. Nothing is wrong here - the library simply has no name for it.
			CHIAKI_LOGI(ctrl->session->log, "Ctrl received unhandled message of type %#x, size %#llx",
					(unsigned int)msg_type, (unsigned long long)payload_size);
			if(payload_size > 0)
				chiaki_log_hexdump(ctrl->session->log, CHIAKI_LOG_INFO, payload, payload_size);
			break;
	}
}

// PP383: every send in the burst is read, and the first failure ends it.
//
// STOPPING IS THE POINT, not tidiness. ctrl_message_send spends crypt_counter_local at ENCRYPT
// time, before anything reaches the socket, so a send that fails has already consumed a counter
// value the console never saw. From there the two disagree, and every later ctrl message decrypts
// against the wrong counter - so carrying on with the remaining sends widens a break rather than
// salvaging a feature. What is reported is not "the keyboard did not enable"; it is that the
// channel is finished.
#define CTRL_FEATURE_SEND(call, what) do { \
		ChiakiErrorCode feature_err = (call); \
		if(feature_err != CHIAKI_ERR_SUCCESS) \
		{ \
			CHIAKI_LOGE(ctrl->session->log, \
					"Ctrl failed to send %s while enabling features: %s", \
					(what), chiaki_error_string(feature_err)); \
			return feature_err; \
		} \
	} while(0)

CHIAKI_EXPORT ChiakiErrorCode ctrl_enable_features(ChiakiCtrl *ctrl)
{
	if(ctrl->session->connect_info.enable_dualsense)
	{
		CHIAKI_LOGI(ctrl->session->log, "Enabling DualSense features");
		const uint8_t enable[3] = { 0x00, 0x40, 0x00 };
		CTRL_FEATURE_SEND(ctrl_message_send(ctrl, CTRL_MESSAGE_TYPE_ENABLE_DUALSENSE_FEATURES, enable, 3),
				"the DualSense enable");
		// PP383: fifteen initialisers for a sixteen-byte payload, so the last byte is an implicit
		// zero. Reproduced rather than corrected - the two readings differ only in whether a sixth
		// 0xff was meant, nothing in this tree says which, and PP297's capture was taken with
		// DualSense off so it does not hold this message.
		const uint8_t connect[0x10] = { 0xa0, 0xab, 0x51, 0xbd, 0xd1, 0x7e, 0x00, 0x00, 0xff, 0xff, 0xff, 0xff, 0xff, 0x00, 0x00 };
		CTRL_FEATURE_SEND(ctrl_message_send(ctrl, 0x11, connect, 0x10), "the DualSense connect");
	}
	if(ctrl->session->connect_info.enable_keyboard)
	{
		CHIAKI_LOGI(ctrl->session->log, "Enabling Keyboard");
		// TODO: Signature ?!
		uint8_t enable = 1;
		uint8_t signature[0x10] = { 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x05, 0xAE, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
		CTRL_FEATURE_SEND(ctrl_message_send(ctrl, CTRL_MESSAGE_TYPE_KEYBOARD_ENABLE, signature, 0x10),
				"the keyboard enable");
		CTRL_FEATURE_SEND(ctrl_message_send(ctrl, CTRL_MESSAGE_TYPE_KEYBOARD_ENABLE_TOGGLE, &enable, 1),
				"the keyboard toggle");
	}
	// Twice, both false, and PP342 asserts it stays twice - the capture has them 108 microseconds
	// apart. Two sends means two counter values, so the second is as load-bearing as the first.
	CTRL_FEATURE_SEND(ctrl_message_toggle_microphone(ctrl, false), "the first microphone toggle");
	CTRL_FEATURE_SEND(ctrl_message_toggle_microphone(ctrl, false), "the second microphone toggle");
	uint8_t display[0x4] = { 0x00, 0x00, 0x00, 0x00 };
	CTRL_FEATURE_SEND(ctrl_message_send(ctrl, CTRL_MESSAGE_TYPE_DISPLAY_DEVICES, display, 0x4),
			"the display devices request");
	return CHIAKI_ERR_SUCCESS;
}

static void ctrl_message_received_session_id(ChiakiCtrl *ctrl, uint8_t *payload, size_t payload_size)
{
	chiaki_mutex_lock(&ctrl->session->state_mutex);
	if(ctrl->session->ctrl_session_id_received)
	{
		CHIAKI_LOGW(ctrl->session->log, "Received another Session Id Message");
		chiaki_mutex_unlock(&ctrl->session->state_mutex);
		return;
	}
	chiaki_mutex_unlock(&ctrl->session->state_mutex);

	if(payload_size < 2)
	{
		// PP405: %.*s and not %s. payload is ctrl->recv_buf + 8, and this branch is the one where
		// payload_size is under two bytes - so the conversion that stops at a zero would print the
		// messages queued behind this one, on a channel that carries the session id and the PIN.
		CHIAKI_LOGE(ctrl->session->log, "Invalid Session Id \"%.*s\" received", (int)payload_size, payload);
		CTRL_FALLBACK_SESSION_ID(ctrl);
		return;
	}

	if(payload[0] != 0x4a)
	{
		CHIAKI_LOGW(ctrl->session->log, "Received presumably invalid Session Id:");
		chiaki_log_hexdump(ctrl->session->log, CHIAKI_LOG_WARNING, payload, payload_size);
	}

	// skip the size
	payload++;
	payload_size--;

	if(payload_size >= CHIAKI_SESSION_ID_SIZE_MAX - 1)
	{
		CHIAKI_LOGE(ctrl->session->log, "Received Session Id is too long");
		CTRL_FALLBACK_SESSION_ID(ctrl);
		return;
	}

	if(payload_size < 24)
	{
		CHIAKI_LOGE(ctrl->session->log, "Received Session Id is too short");
		CTRL_FALLBACK_SESSION_ID(ctrl);
		return;
	}

	for(uint8_t *cur=payload; cur<payload+payload_size; cur++)
	{
		char c = *cur;
		if(c >= 'a' && c <= 'z')
			continue;
		if(c >= 'A' && c <= 'Z')
			continue;
		if(c >= '0' && c <= '9')
			continue;
		CHIAKI_LOGE(ctrl->session->log, "Ctrl received Session Id contains invalid characters");
		CTRL_FALLBACK_SESSION_ID(ctrl);
		return;
	}

	chiaki_mutex_lock(&ctrl->session->state_mutex);
	memcpy(ctrl->session->session_id, payload, payload_size);
	ctrl->session->session_id[payload_size] = '\0';
	CHIAKI_LOGI(ctrl->session->log, "Ctrl received valid Session Id: %s", ctrl->session->session_id);
	ctrl->session->ctrl_session_id_received = true;
	chiaki_mutex_unlock(&ctrl->session->state_mutex);
	chiaki_cond_signal(&ctrl->session->state_cond);
}

static void ctrl_message_received_heartbeat_req(ChiakiCtrl *ctrl, uint8_t *payload, size_t payload_size)
{
	if(payload_size != 0)
		CHIAKI_LOGW(ctrl->session->log, "Ctrl received Heartbeat request with non-empty payload");

	CHIAKI_LOGI(ctrl->session->log, "Ctrl received Heartbeat, sending reply");
	// PP385: read. PP342 asserts this reply is unconditional and immediate - the capture answers
	// three heartbeats in 40, 19 and 18 microseconds - and a reply that does not go is a session
	// the console will drop on its own timeout. Ending here says why; waiting says nothing.
	CTRL_SEND_OR_FAIL(
			ctrl_message_send(ctrl, CTRL_MESSAGE_TYPE_HEARTBEAT_REP, NULL, 0),
			"the heartbeat reply");
}

static void ctrl_message_received_switch_to_stream_connection(ChiakiCtrl *ctrl, uint8_t *payload, size_t payload_size)
{
	if(payload_size != 0)
		CHIAKI_LOGW(ctrl->session->log, "Ctrl received Switch to Stream Connection Ack with non-empty payload");
	if(!ctrl->session->stream_connection_switch_received)
	{
		chiaki_session_set_stream_connection_switch_received(ctrl->session);
	}
	else
		CHIAKI_LOGI(ctrl->session->log, "Received an extra stream connection switch ACK, ignoring...");
}

static void ctrl_message_received_login_pin_req(ChiakiCtrl *ctrl, uint8_t *payload, size_t payload_size)
{
	if(payload_size != 0)
		CHIAKI_LOGW(ctrl->session->log, "Ctrl received Login PIN request with non-empty payload");

	CHIAKI_LOGI(ctrl->session->log, "Ctrl received Login PIN request");

	ctrl->login_pin_requested = true;

	ChiakiErrorCode err = chiaki_mutex_lock(&ctrl->session->state_mutex);
	assert(err == CHIAKI_ERR_SUCCESS);
	// If receive login pin request after starting session, quit session as this won't work
	if(ctrl->session->ctrl_session_id_received)
	{
		chiaki_mutex_unlock(&ctrl->session->state_mutex);
		ctrl_failed(ctrl, CHIAKI_QUIT_REASON_CTRL_UNKNOWN);
		return;
	}
	ctrl->session->ctrl_login_pin_requested = true;
	chiaki_mutex_unlock(&ctrl->session->state_mutex);
	chiaki_cond_signal(&ctrl->session->state_cond);
}

static void ctrl_message_received_displaya(ChiakiCtrl *ctrl, uint8_t *payload, size_t payload_size)
{
	// PP352: the check every other handler has. This read one byte and never looked at the size,
	// and payload points into a buffer that always has room - so a zero-length message read
	// whatever the previous one left there, and a stale 0x1 raised the flag that tells the display
	// sink the stream cannot be shown. Shaped like the login handler's: warn, and return only where
	// there is nothing to read.
	if(payload_size < 1)
	{
		CHIAKI_LOGW(ctrl->session->log, "Ctrl received DisplayA with an empty payload");
		return;
	}

	if(payload[0] == 0x1)
	{
		ctrl->cant_displaya = true;
	}
	// PP359: and on the prohibition, which is the third thing that can be hiding the stream. Same
	// shape as the guard beside it: the flag is not lowered either, so a later DisplayA is judged
	// against the same state this one was.
	else if (payload[0] == 0x0 && !ctrl->cant_displayb && !ctrl->rp_prohibit)
	{
		ctrl->cant_displaya = false;
		CHIAKI_LOGI(ctrl->session->log, "Ctrl received message that the stream can now display.");
		ctrl->session->display_sink.cantdisplay_cb(ctrl->session->display_sink.user, false);
	}
}

static void ctrl_message_received_displayb(ChiakiCtrl *ctrl, uint8_t *payload, size_t payload_size)
{
	// PP352: two bytes, because both tests below read payload[0] AND payload[1]. The recorded
	// value is 01-ff, which is the pair that clears the flag - so a short message read as
	// something other than 01-ff is the branch that RAISES it, and stops the stream.
	if(payload_size < 2)
	{
		CHIAKI_LOGW(ctrl->session->log, "Ctrl received DisplayB with a payload shorter than two bytes");
		return;
	}

	if(ctrl->cant_displaya == true)
	{
		if(!(payload[0] == 0x01 && payload[1] == 0xff) && !ctrl->cant_displayb)
		{
			ctrl->session->display_sink.cantdisplay_cb(ctrl->session->display_sink.user, true);
			CHIAKI_LOGI(ctrl->session->log, "Ctrl received message that the stream can't display due to displaying some content that can't be streamed.");
			ctrl->cant_displayb = true;
		}
	}
	if(ctrl->cant_displayb && payload[0] == 0x01 && payload[1] == 0xff)
		ctrl->cant_displayb = false;
}

static void ctrl_message_received_login(ChiakiCtrl *ctrl, uint8_t *payload, size_t payload_size)
{
	if(payload_size != 1)
	{
		CHIAKI_LOGW(ctrl->session->log, "Ctrl received Login message with payload of size %#llx", (unsigned long long)payload_size);
		if(payload_size < 1)
			return;
	}

	CtrlLoginState state = payload[0];
	switch(state)
	{
		case CTRL_LOGIN_STATE_SUCCESS:
			CHIAKI_LOGI(ctrl->session->log, "Ctrl received Login message: success");
			ctrl->login_pin_requested = false;
			break;
		case CTRL_LOGIN_STATE_PIN_INCORRECT:
			CHIAKI_LOGI(ctrl->session->log, "Ctrl received Login message: PIN incorrect");
			if(ctrl->login_pin_requested)
			{
				CHIAKI_LOGI(ctrl->session->log, "Ctrl requesting PIN from Session again");
				ChiakiErrorCode err = chiaki_mutex_lock(&ctrl->session->state_mutex);
				assert(err == CHIAKI_ERR_SUCCESS);
				ctrl->session->ctrl_login_pin_requested = true;
				chiaki_mutex_unlock(&ctrl->session->state_mutex);
				chiaki_cond_signal(&ctrl->session->state_cond);
			}
			else
				CHIAKI_LOGW(ctrl->session->log, "Ctrl Login PIN incorrect message, but PIN was not requested");
			break;
		default:
			CHIAKI_LOGI(ctrl->session->log, "Ctrl received Login message with state: %#x", state);
			break;
	}
}

static void ctrl_message_received_keyboard_open(ChiakiCtrl *ctrl, uint8_t *payload, size_t payload_size)
{
	if(payload_size < sizeof(CtrlKeyboardOpenMessage))
	{
		CHIAKI_LOGE(ctrl->session->log, "Ctrl received invalid message keyboard open with payload size %zu while expected size is at least %zu", payload_size, sizeof(CtrlKeyboardOpenMessage));
		return;
	}

	CtrlKeyboardOpenMessage *msg = (CtrlKeyboardOpenMessage *)payload;
	msg->text_length = ntohl(msg->text_length);

	// PP357: a check and not an assert. This project builds Release with -DNDEBUG, so the assert
	// that stood here was nothing in the shipped binary - and the guard above covers the header
	// only. A message announcing more text than it carried was malloc'd at that length and memcpy'd
	// out of a 512-byte buffer, into a string handed to a screen as what the user is editing.
	if(payload_size != sizeof(CtrlKeyboardOpenMessage) + msg->text_length)
	{
		CHIAKI_LOGE(ctrl->session->log,
				"Ctrl received keyboard open claiming %u bytes of text in a payload of %zu",
				(unsigned int)msg->text_length, payload_size);
		return;
	}

	uint8_t *buffer = msg->text_length > 0 ? malloc((size_t)msg->text_length + 1) : NULL;
	if(buffer)
	{
		buffer[msg->text_length] = '\0';
		memcpy(buffer, payload + sizeof(CtrlKeyboardOpenMessage), msg->text_length);
	}

	ChiakiEvent keyboard_event;
	keyboard_event.type = CHIAKI_EVENT_KEYBOARD_OPEN;
	keyboard_event.keyboard.text_str = (const char *)buffer;
	chiaki_session_send_event(ctrl->session, &keyboard_event);

	if(buffer)
		free(buffer);
}

static void ctrl_message_received_keyboard_close(ChiakiCtrl *ctrl, uint8_t *payload, size_t payload_size)
{
	(void)payload;
	(void)payload_size;

	ChiakiEvent keyboard_event;
	keyboard_event.type = CHIAKI_EVENT_KEYBOARD_REMOTE_CLOSE;
	keyboard_event.keyboard.text_str = NULL;
	chiaki_session_send_event(ctrl->session, &keyboard_event);
}

static void ctrl_message_received_keyboard_text_change(ChiakiCtrl *ctrl, uint8_t *payload, size_t payload_size)
{
	if(payload_size < sizeof(CtrlKeyboardTextResponseMessage))
	{
		CHIAKI_LOGE(ctrl->session->log, "Ctrl received invalid message keyboard text change with payload size %zu while expected size is at least %zu", payload_size, sizeof(CtrlKeyboardTextResponseMessage));
		return;
	}

	CtrlKeyboardTextResponseMessage *msg = (CtrlKeyboardTextResponseMessage *)payload;
	msg->text_length1 = ntohl(msg->text_length1);

	// PP357: the same swap as keyboard open above, for the same reason.
	if(payload_size != sizeof(CtrlKeyboardTextResponseMessage) + msg->text_length1)
	{
		CHIAKI_LOGE(ctrl->session->log,
				"Ctrl received keyboard text change claiming %u bytes of text in a payload of %zu",
				(unsigned int)msg->text_length1, payload_size);
		return;
	}

	uint8_t *buffer = msg->text_length1 > 0 ? malloc((size_t)msg->text_length1 + 1) : NULL;
	if(buffer)
	{
		buffer[msg->text_length1] = '\0';
		memcpy(buffer, payload + sizeof(CtrlKeyboardTextResponseMessage), msg->text_length1);
	}

	ChiakiEvent keyboard_event;
	keyboard_event.type = CHIAKI_EVENT_KEYBOARD_TEXT_CHANGE;
	keyboard_event.keyboard.text_str = (const char *)buffer;
	chiaki_session_send_event(ctrl->session, &keyboard_event);

	if(buffer)
		free(buffer);
}

typedef struct ctrl_response_t
{
	bool server_type_valid;
	uint8_t rp_server_type[0x10];
	bool rp_prohibit;
	bool success;
} CtrlResponse;

static void parse_ctrl_response(CtrlResponse *response, ChiakiHttpResponse *http_response)
{
	memset(response, 0, sizeof(CtrlResponse));

	if(http_response->code != 200)
	{
		response->success = false;
		return;
	}

	response->success = true;
	response->server_type_valid = false;
	response->rp_prohibit = false;
	for(ChiakiHttpHeader *header=http_response->headers; header; header=header->next)
	{
		// PP358: both case-insensitively, which is what an HTTP field name is - the same change
		// PP296 made to parse_session_response and did not reach here. A console spelling
		// RP-Server-Type otherwise lost both downgrades below, so a regular PS4 was asked for 1080p
		// and for H265, and the log said the header was not valid.
		if(strcasecmp(header->key, "RP-Server-Type") == 0)
		{
			size_t server_type_size = sizeof(response->rp_server_type);
			ChiakiErrorCode err = chiaki_base64_decode(header->value, strlen(header->value) + 1, response->rp_server_type, &server_type_size);
			if(err != CHIAKI_ERR_SUCCESS)
			{
				response->success = false;
				return;
			}
			response->server_type_valid = server_type_size == sizeof(response->rp_server_type);
		}
		else if(strcasecmp(header->key, "RP-Prohibit") == 0)
			response->rp_prohibit = atoi(header->value) == 1;
	}
}

static ChiakiErrorCode ctrl_connect(ChiakiCtrl *ctrl)
{
	ctrl->crypt_counter_local = 0;
	ctrl->crypt_counter_remote = 0;

	ChiakiSession *session = ctrl->session;
	uint16_t remote_counter = 0;
	ChiakiErrorCode err = CHIAKI_ERR_SUCCESS;

	if(session->rudp)
	{
		CHIAKI_LOGI(session->log, "CTRL - Starting RUDP session");
		RudpMessage message;
		ChiakiErrorCode err = chiaki_rudp_send_recv(session->rudp, &message, NULL, 0, 0, INIT_REQUEST, INIT_RESPONSE, 8, 3);
		if(err != CHIAKI_ERR_SUCCESS)
		{
			CHIAKI_LOGE(session->log, "CTRL - Failed to init rudp");
			goto error;
		}
		size_t init_response_size = message.data_size - 8;
		uint8_t init_response[init_response_size];
		memcpy(init_response, message.data + 8, init_response_size);
		chiaki_rudp_message_pointers_free(&message);
		err = chiaki_rudp_send_recv(session->rudp, &message, init_response, init_response_size, 0, COOKIE_REQUEST, COOKIE_RESPONSE, 2, 3);
		if(err != CHIAKI_ERR_SUCCESS)
		{
			CHIAKI_LOGE(session->log, "CTRL - Failed to pass rudp cookie");
			goto error;
		}
		remote_counter = message.remote_counter;
		chiaki_rudp_message_pointers_free(&message);
	}
	else
	{
		err = ctrl_connect_tcp(ctrl);
		if(err != CHIAKI_ERR_SUCCESS)
			goto error;
	}

	uint8_t auth_enc[CHIAKI_RPCRYPT_KEY_SIZE];
	err = chiaki_rpcrypt_encrypt(&session->rpcrypt, ctrl->crypt_counter_local++, (const uint8_t *)session->connect_info.regist_key, auth_enc, CHIAKI_RPCRYPT_KEY_SIZE);
	if(err != CHIAKI_ERR_SUCCESS)
		goto error;
	char auth_b64[CHIAKI_RPCRYPT_KEY_SIZE*2];
	err = chiaki_base64_encode(auth_enc, sizeof(auth_enc), auth_b64, sizeof(auth_b64));
	if(err != CHIAKI_ERR_SUCCESS)
		goto error;

	uint8_t did_enc[CHIAKI_RP_DID_SIZE];
	err = chiaki_rpcrypt_encrypt(&session->rpcrypt, ctrl->crypt_counter_local++, session->connect_info.did, did_enc, CHIAKI_RP_DID_SIZE);
	if(err != CHIAKI_ERR_SUCCESS)
		goto error;
	char did_b64[CHIAKI_RP_DID_SIZE*2];
	err = chiaki_base64_encode(did_enc, sizeof(did_enc), did_b64, sizeof(did_b64));
	if(err != CHIAKI_ERR_SUCCESS)
		goto error;

	uint8_t ostype_enc[128];
	size_t ostype_len = strlen(SESSION_OSTYPE) + 1;
	if(ostype_len > sizeof(ostype_enc))
		goto error;
	err = chiaki_rpcrypt_encrypt(&session->rpcrypt, ctrl->crypt_counter_local++, (const uint8_t *)SESSION_OSTYPE, ostype_enc, ostype_len);
	if(err != CHIAKI_ERR_SUCCESS)
		goto error;
	char ostype_b64[256];
	err = chiaki_base64_encode(ostype_enc, ostype_len, ostype_b64, sizeof(ostype_b64));
	if(err != CHIAKI_ERR_SUCCESS)
		goto error;

	char bitrate_b64[256];
	bool have_bitrate = session->target >= CHIAKI_TARGET_PS4_10;
	if(have_bitrate)
	{
		uint8_t bitrate[4] = { 0 };
		uint8_t bitrate_enc[4] = { 0 };
		err = chiaki_rpcrypt_encrypt(&session->rpcrypt, ctrl->crypt_counter_local++, (const uint8_t *)bitrate, bitrate_enc, 4);
		if(err != CHIAKI_ERR_SUCCESS)
			goto error;

		err = chiaki_base64_encode(bitrate_enc, 4, bitrate_b64, sizeof(bitrate_b64));
		if(err != CHIAKI_ERR_SUCCESS)
			goto error;
	}

	char streaming_type_b64[256];
	bool have_streaming_type = chiaki_target_is_ps5(session->target);
	if(have_streaming_type)
	{
		uint32_t streaming_type;
		switch(session->connect_info.video_profile.codec)
		{
			case CHIAKI_CODEC_H265:
				streaming_type = 2;
				break;
			case CHIAKI_CODEC_H265_HDR:
				streaming_type = 3;
				break;
			default:
				streaming_type = 1;
				break;
		}
		uint8_t streaming_type_buf[4] = {
			streaming_type & 0xff,
			(streaming_type >> 8) & 0xff,
			(streaming_type >> 0x10) & 0xff,
			(streaming_type >> 0x18) & 0xff
		};
		uint8_t streaming_type_enc[4] = { 0 };
		err = chiaki_rpcrypt_encrypt(&session->rpcrypt, ctrl->crypt_counter_local++,
				streaming_type_buf, streaming_type_enc, 4);
		if(err != CHIAKI_ERR_SUCCESS)
			goto error;

		err = chiaki_base64_encode(streaming_type_enc, 4, streaming_type_b64, sizeof(streaming_type_b64));
		if(err != CHIAKI_ERR_SUCCESS)
			goto error;
	}

	static const char request_fmt[] =
			"GET %s HTTP/1.1\r\n"
			"Host: %s:%d\r\n"
			"User-Agent: remoteplay Windows\r\n"
			"Connection: keep-alive\r\n"
			"Content-Length: 0\r\n"
			"RP-Auth: %s\r\n"
			"RP-Version: %s\r\n"
			"RP-Did: %s\r\n"
			"RP-ControllerType: 3\r\n"
			"RP-ClientType: 11\r\n"
			"RP-OSType: %s\r\n"
			"RP-ConPath: 1\r\n"
			"%s%s%s"
			"%s%s%s"
			"\r\n";

	const char *path;
	if(session->target == CHIAKI_TARGET_PS4_8 || session->target == CHIAKI_TARGET_PS4_9)
		path = "/sce/rp/session/ctrl";
	else if(chiaki_target_is_ps5(session->target))
		path = "/sie/ps5/rp/sess/ctrl";
	else
		path = "/sie/ps4/rp/sess/ctrl";
	const char *rp_version = chiaki_rp_version_string(session->target);
	int port = session->holepunch_session ? chiaki_get_ps_ctrl_port(session->holepunch_session) : SESSION_CTRL_PORT;
	char send_buf[512];
	int request_len = snprintf(send_buf, sizeof(send_buf), request_fmt,
			path, session->connect_info.hostname, port, auth_b64,
			rp_version ? rp_version : "", did_b64, ostype_b64,
			have_bitrate ? "RP-StartBitrate: " : "",
			have_bitrate ? bitrate_b64 : "",
			have_bitrate ? "\r\n" : "",
			have_streaming_type ? "RP-StreamingType: " : "",
			have_streaming_type ? streaming_type_b64 : "",
			have_streaming_type ? "\r\n" : "");
	if(request_len < 0 || request_len >= sizeof(send_buf))
		goto error;

	CHIAKI_LOGI(session->log, "Sending ctrl request");
	chiaki_log_hexdump(session->log, CHIAKI_LOG_VERBOSE, (const uint8_t *)send_buf, (size_t)request_len);

	if(session->rudp)
	{
		if(chiaki_target_is_ps5(session->target))
			ctrl->crypt_counter_local++;
	}

	bool ctrl_request_retry = false;
	char buf[512];
	size_t header_size;
	size_t received_size;

	while(true)
	{
		if(session->rudp)
		{
			err = chiaki_send_recv_http_header_psn(session->rudp, session->log, &remote_counter, send_buf, request_len, buf, sizeof(buf), &header_size, &received_size);
		}
		else
		{
			int sent = send(ctrl->sock, (CHIAKI_SOCKET_BUF_TYPE)send_buf, (size_t)request_len, 0);
			if(sent < 0)
			{
				CHIAKI_LOGE(session->log, "Failed to send ctrl request");
				goto error;
			}

			err = chiaki_recv_http_header(ctrl->sock, buf, sizeof(buf), &header_size, &received_size, &ctrl->stop_pipe, CTRL_EXPECT_TIMEOUT);
		}

		if(err == CHIAKI_ERR_TIMEOUT && !ctrl_request_retry)
		{
			CHIAKI_LOGI(session->log, "Initial ctrl startup request timed out, resending ...");
			memset(buf, 0, sizeof(buf));
			ctrl_request_retry = true;
			if(!session->rudp)
			{
				ctrl_disconnect_tcp(ctrl);
				err = ctrl_connect_tcp(ctrl);
				if(err != CHIAKI_ERR_SUCCESS)
					goto error;
			}
			continue;
		}

		break;
	}

	if(err != CHIAKI_ERR_SUCCESS)
	{
		if(err != CHIAKI_ERR_CANCELED)
		{
			int errsv = WSAGetLastError();
			CHIAKI_LOGE(session->log, "Failed to receive ctrl request response: %s", chiaki_error_string(err));
			if(err == CHIAKI_ERR_NETWORK)
			{
				CHIAKI_LOGE(session->log, "Ctrl request response network error: %d", errsv);
			}
		}
		else
		{
			CHIAKI_LOGI(session->log, "Ctrl canceled while receiving ctrl request response");
		}
		goto error;
	}

	if(session->rudp)
	{
		err = chiaki_rudp_send_ack_message(session->rudp, remote_counter);
		if(err != CHIAKI_ERR_SUCCESS)
		{
			CHIAKI_LOGE(session->log, "CTRL - Failed to send rudp ctrl request response ack message");
			session->quit_reason = CHIAKI_QUIT_REASON_SESSION_REQUEST_UNKNOWN;
			goto error;
		}
	}

	CHIAKI_LOGI(session->log, "Ctrl received http header as response");
	chiaki_log_hexdump(session->log, CHIAKI_LOG_VERBOSE, (const uint8_t *)buf, header_size);

	ChiakiHttpResponse http_response;
	err = chiaki_http_response_parse(&http_response, buf, header_size);
	if(err != CHIAKI_ERR_SUCCESS)
	{
		CHIAKI_LOGE(session->log, "Failed to parse ctrl request response");
		goto error;
	}

	CHIAKI_LOGI(session->log, "Ctrl received ctrl request http response");

	CtrlResponse response;
	parse_ctrl_response(&response, &http_response);
	if(!response.success)
	{
		CHIAKI_LOGE(session->log, "Ctrl http response was not successful. HTTP code was %d", http_response.code);
		chiaki_http_response_fini(&http_response);
		err = CHIAKI_ERR_UNKNOWN;
		goto error;
	}
	chiaki_http_response_fini(&http_response);

	if(response.server_type_valid)
	{
		ChiakiErrorCode err2 = chiaki_rpcrypt_decrypt(&session->rpcrypt,
				ctrl->crypt_counter_remote++,
				response.rp_server_type,
				response.rp_server_type,
				sizeof(response.rp_server_type));
		if(err2 != CHIAKI_ERR_SUCCESS)
		{
			CHIAKI_LOGE(session->log, "Ctrl failed to decrypt RP-Server-Type");
			response.server_type_valid = false;
		}
	}

	if(response.server_type_valid)
	{
		uint8_t server_type = response.rp_server_type[0]; // 0 = PS4, 1 = PS4 Pro, 2 = PS5
		CHIAKI_LOGI(session->log, "Ctrl got Server Type: %u", (unsigned int)server_type);
		if(server_type == 0
				&& session->connect_info.video_profile_auto_downgrade
				&& session->connect_info.video_profile.height == 1080)
		{
			// regular PS4 doesn't support >= 1080p
			CHIAKI_LOGI(session->log, "1080p was selected but server would not support it. Downgrading.");
			chiaki_connect_video_profile_preset(
				&session->connect_info.video_profile,
				CHIAKI_VIDEO_RESOLUTION_PRESET_720p,
				session->connect_info.video_profile.max_fps == 60
					? CHIAKI_VIDEO_FPS_PRESET_60
					: CHIAKI_VIDEO_FPS_PRESET_30);
		}
		if((server_type == 0 || server_type == 1)
				&& session->connect_info.video_profile.codec != CHIAKI_CODEC_H264)
		{
			// PS4 doesn't support anything except h264
			CHIAKI_LOGI(session->log, "A codec other than H264 was selected but server would not support it. Downgrading.");
			session->connect_info.video_profile.codec = CHIAKI_CODEC_H264;
		}
	}
	else
		CHIAKI_LOGE(session->log, "No valid Server Type in ctrl response");

	if(response.rp_prohibit)
	{
		// PP359: recorded as well as reported. This is the THIRD writer to the belief PP353's two
		// flags model, and it used to touch neither of them - so the client hid the stream while
		// cant_displaya and cant_displayb both read false, and the one branch that ever says the
		// stream is back was guarded on a flag this never raised. The first unrelated DisplayA 0x0
		// then un-hid a session the console had said was prohibited, with nothing left to remember
		// that it was.
		ctrl->rp_prohibit = true;
		ctrl->session->display_sink.cantdisplay_cb(ctrl->session->display_sink.user, true);
	}

	// if we already got more data than the header, put the rest in the buffer.
	ctrl->recv_buf_size = received_size - header_size;
	if(ctrl->recv_buf_size > 0)
		memcpy(ctrl->recv_buf, buf + header_size, ctrl->recv_buf_size);

	return CHIAKI_ERR_SUCCESS;

error:
	if(!ctrl->session->rudp)
	{
		if(!CHIAKI_SOCKET_IS_INVALID(ctrl->sock))
		{
			CHIAKI_SOCKET_CLOSE(ctrl->sock);
			ctrl->sock = CHIAKI_INVALID_SOCKET;
		}
	}
	return err;
}
