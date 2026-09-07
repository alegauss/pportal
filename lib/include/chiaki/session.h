// SPDX-License-Identifier: LicenseRef-AGPL-3.0-only-OpenSSL

#ifndef CHIAKI_SESSION_H
#define CHIAKI_SESSION_H

#include "streamconnection.h"
#include "common.h"
#include "thread.h"
#include "log.h"
#include "ctrl.h"
#include "rpcrypt.h"
#include "takion.h"
#include "ecdh.h"
#include "audio.h"
#include "controller.h"
#include "stoppipe.h"
#include "remote/holepunch.h"
#include "remote/rudp.h"
#include "regist.h"

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

#define CHIAKI_RP_APPLICATION_REASON_REGIST_FAILED		0x80108b09
#define CHIAKI_RP_APPLICATION_REASON_INVALID_PSN_ID		0x80108b02
#define CHIAKI_RP_APPLICATION_REASON_IN_USE				0x80108b10
#define CHIAKI_RP_APPLICATION_REASON_CRASH				0x80108b15
#define CHIAKI_RP_APPLICATION_REASON_RP_VERSION			0x80108b11
#define CHIAKI_RP_APPLICATION_REASON_UNKNOWN			0x80108bff

CHIAKI_EXPORT const char *chiaki_rp_application_reason_string(uint32_t reason);

/**
 * @return RP-Version string or NULL
 */
CHIAKI_EXPORT const char *chiaki_rp_version_string(ChiakiTarget target);

CHIAKI_EXPORT ChiakiTarget chiaki_rp_version_parse(const char *rp_version_str, bool is_ps5);


#define CHIAKI_RP_DID_SIZE 32
#define CHIAKI_SESSION_ID_SIZE_MAX 80
#define CHIAKI_HANDSHAKE_KEY_SIZE 0x10

typedef struct chiaki_connect_video_profile_t
{
	unsigned int width;
	unsigned int height;
	unsigned int max_fps;
	unsigned int bitrate;
	ChiakiCodec codec;
} ChiakiConnectVideoProfile;

typedef enum {
	// values must not change
	CHIAKI_VIDEO_RESOLUTION_PRESET_360p = 1,
	CHIAKI_VIDEO_RESOLUTION_PRESET_540p = 2,
	CHIAKI_VIDEO_RESOLUTION_PRESET_720p = 3,
	CHIAKI_VIDEO_RESOLUTION_PRESET_1080p = 4
} ChiakiVideoResolutionPreset;

typedef enum {
	// values must not change
	CHIAKI_VIDEO_FPS_PRESET_30 = 30,
	CHIAKI_VIDEO_FPS_PRESET_60 = 60
} ChiakiVideoFPSPreset;

CHIAKI_EXPORT void chiaki_connect_video_profile_preset(ChiakiConnectVideoProfile *profile, ChiakiVideoResolutionPreset resolution, ChiakiVideoFPSPreset fps);

#define CHIAKI_SESSION_AUTH_SIZE 0x10

typedef struct chiaki_connect_info_t
{
	bool ps5;
	const char *host; // null terminated
	char regist_key[CHIAKI_SESSION_AUTH_SIZE]; // must be completely filled (pad with \0)
	uint8_t morning[0x10];
	ChiakiConnectVideoProfile video_profile;
	bool video_profile_auto_downgrade; // Downgrade video_profile if server does not seem to support it.
	bool enable_keyboard;
	bool enable_dualsense;
	ChiakiDisableAudioVideo audio_video_disabled;
	bool auto_regist;
	// PP632: holepunch_session stood here and is gone. Only the Qt client ever set it (PP596), and
	// PP598 retired that client's build - so the field named a path with no caller and session.c
	// asked nine questions nothing could reach.
	chiaki_socket_t *rudp_sock;
	uint8_t psn_account_id[CHIAKI_PSN_ACCOUNT_ID_SIZE];
	double packet_loss_max;
	bool enable_idr_on_fec_failure;
} ChiakiConnectInfo;


typedef enum {
	CHIAKI_QUIT_REASON_NONE,
	CHIAKI_QUIT_REASON_STOPPED,
	CHIAKI_QUIT_REASON_SESSION_REQUEST_UNKNOWN,
	CHIAKI_QUIT_REASON_SESSION_REQUEST_CONNECTION_REFUSED,
	CHIAKI_QUIT_REASON_SESSION_REQUEST_RP_IN_USE,
	CHIAKI_QUIT_REASON_SESSION_REQUEST_RP_CRASH,
	CHIAKI_QUIT_REASON_SESSION_REQUEST_RP_VERSION_MISMATCH,
	CHIAKI_QUIT_REASON_CTRL_UNKNOWN,
	CHIAKI_QUIT_REASON_CTRL_CONNECT_FAILED,
	CHIAKI_QUIT_REASON_CTRL_CONNECTION_REFUSED,
	CHIAKI_QUIT_REASON_STREAM_CONNECTION_UNKNOWN,
	CHIAKI_QUIT_REASON_STREAM_CONNECTION_REMOTE_DISCONNECTED,
	CHIAKI_QUIT_REASON_STREAM_CONNECTION_REMOTE_SHUTDOWN, // like REMOTE_DISCONNECTED, but because the server shut down
	CHIAKI_QUIT_REASON_PSN_REGIST_FAILED,
	// PP345: appended rather than filed with the other CTRL_ reasons, because every value here is
	// marshalled by ordinal - the managed enum and the shim both count from None - and inserting
	// one would renumber the six below it into each other's meanings.
	CHIAKI_QUIT_REASON_CTRL_MEMORY,
} ChiakiQuitReason;

CHIAKI_EXPORT const char *chiaki_quit_reason_string(ChiakiQuitReason reason);

static inline bool chiaki_quit_reason_is_error(ChiakiQuitReason reason)
{
	return reason != CHIAKI_QUIT_REASON_STOPPED && reason != CHIAKI_QUIT_REASON_STREAM_CONNECTION_REMOTE_SHUTDOWN;
}

typedef struct chiaki_quit_event_t
{
	ChiakiQuitReason reason;
	const char *reason_str;
} ChiakiQuitEvent;

typedef struct chiaki_keyboard_event_t
{
	const char *text_str;
} ChiakiKeyboardEvent;

typedef struct chiaki_audio_stream_info_event_t
{
	ChiakiAudioHeader audio_header;
} ChiakiAudioStreamInfoEvent;

typedef struct chiaki_rumble_event_t
{
	uint8_t unknown;
	uint8_t left; // low-frequency
	uint8_t right; // high-frequency
} ChiakiRumbleEvent;

typedef struct chiaki_trigger_effects_event_t
{
	uint8_t type_left;
	uint8_t type_right;
	uint8_t left[10];
	uint8_t right[10];
} ChiakiTriggerEffectsEvent;

typedef struct chiaki_video_fec_failure_event_t
{
	int32_t frame_index;
	bool idr_request_sent;
} ChiakiVideoFecFailureEvent;

typedef enum {
	CHIAKI_EVENT_CONNECTED,
	CHIAKI_EVENT_LOGIN_PIN_REQUEST,
	CHIAKI_EVENT_HOLEPUNCH,
	CHIAKI_EVENT_REGIST,
	CHIAKI_EVENT_NICKNAME_RECEIVED,
	CHIAKI_EVENT_KEYBOARD_OPEN,
	CHIAKI_EVENT_KEYBOARD_TEXT_CHANGE,
	CHIAKI_EVENT_KEYBOARD_REMOTE_CLOSE,
	CHIAKI_EVENT_RUMBLE,
	CHIAKI_EVENT_QUIT,
	CHIAKI_EVENT_TRIGGER_EFFECTS,
	CHIAKI_EVENT_MOTION_RESET,
	CHIAKI_EVENT_LED_COLOR,
	CHIAKI_EVENT_PLAYER_INDEX,
	CHIAKI_EVENT_HAPTIC_INTENSITY,
	CHIAKI_EVENT_TRIGGER_INTENSITY,
	CHIAKI_EVENT_VIDEO_FEC_FAILURE,
} ChiakiEventType;

typedef struct chiaki_event_t
{
	ChiakiEventType type;
	union
	{
		ChiakiQuitEvent quit;
		ChiakiKeyboardEvent keyboard;
		ChiakiRumbleEvent rumble;
		ChiakiRegisteredHost host;
		ChiakiTriggerEffectsEvent trigger_effects;
		uint8_t led_state[0x3];
		uint8_t player_index;
		struct
		{
			bool pin_incorrect; // false on first request, true if the pin entered before was incorrect
		} login_pin_request;
		struct
		{
			bool finished; // false when punching hole, true when finished
		} data_holepunch;
		ChiakiDualSenseEffectIntensity intensity;
		char server_nickname[0x20];
		ChiakiVideoFecFailureEvent video_fec_failure;
	};
} ChiakiEvent;

typedef void (*ChiakiEventCallback)(ChiakiEvent *event, void *user);

/**
 * buf will always have an allocated padding of at least CHIAKI_VIDEO_BUFFER_PADDING_SIZE after buf_size
 * @return whether the sample was successfully pushed into the decoder. On false, a corrupt frame will be reported to get a new keyframe.
 */
typedef bool (*ChiakiVideoSampleCallback)(uint8_t *buf, size_t buf_size, int32_t frames_lost, bool frame_recovered, void *user);

/**
 * PP696: the stream phase, run by whoever installed this instead of by the stream connection.
 *
 * The session thread reaches the stream phase and has nothing left in this library to hand it to:
 * streamconnection.c, videoreceiver.c, frameprocessor.c and fec.c have left the build and the port
 * that replaced them lives above it. So the run becomes a callback, on the model of the two above.
 *
 * The error is the run's own and is read exactly as chiaki_stream_connection_run's was.
 *
 * disconnect_reason is written out rather than returned, and is BORROWED: the session copies it
 * with strdup and never frees it, so a callee returning owned memory leaks one string for every
 * session that ends with a remote disconnect. It may be left untouched, which is a run that had no
 * reason to give - the same NULL PP371 found both of the reads below dereferencing.
 *
 * data_sock is the socket senkusha left, passed for parity with the call this replaces. The port's
 * runner opens its own, so nothing reads it today; it crosses anyway, because a signature without
 * it is a different one the day a runner wants it.
 */
typedef ChiakiErrorCode (*ChiakiStreamRunCallback)(chiaki_socket_t *data_sock, const char **disconnect_reason, void *user);

/**
 * PP696: and what chiaki_session_stop's fourth wake-up becomes.
 *
 * Stopping is four pokes and not a flag, because the thread can be blocked in a condition wait, in
 * a socket select, or down in the run - and the third of those is now on the far side of a
 * callback. A session that stopped poking it hangs exactly when somebody quits a live stream.
 */
typedef void (*ChiakiStreamStopCallback)(void *user);

/**
 * PP792, under PP784: the senkusha phase, measured by whoever installed this instead of by
 * senkusha.c.
 *
 * PP753 gave the stream phase a handover and senkusha was left with none - session.c calls
 * chiaki_senkusha_run directly, three lines after it inits and two before it finis. So PP788 to
 * PP791 wrote a managed run that no session could reach, which is what this is for.
 *
 * THREE NUMBERS AND THE OUTCOME, because the outcome decides whether the numbers are used at all.
 * PP28's placement is the whole contract: SUCCESS keeps what the callee wrote, CANCELED ends the
 * session, and every other code falls back to 1454, 1454, a millisecond and the don't-fragment bit
 * cleared. A callback answering only an error code would lose the half the fallback turns on.
 *
 * THE THREE CROSS AS OUT PARAMETERS and not as a struct, which is the shape the call this replaces
 * has: they are the session's own fields, so a callee that measured nothing leaves them as they
 * were and the fallback overwrites them. A struct would need an answer for what an unmeasured field
 * holds, and the C has never had to give one.
 *
 * data_sock is the socket the session established, or NULL for the ordinary one - handed over
 * exactly as chiaki_senkusha_run is handed it.
 */
typedef ChiakiErrorCode (*ChiakiSenkushaRunCallback)(
		chiaki_socket_t *data_sock, uint32_t *mtu_in, uint32_t *mtu_out, uint64_t *rtt_us, void *user);

/**
 * PP792: and what a stop becomes when the measurement is not on this thread.
 *
 * chiaki_session_stop wakes the session thread three ways, and none of them reaches a run behind a
 * callback: senkusha's own stop is a flag its waits re-read, and a managed run has a flag of its
 * own that this library cannot see. Senkusha is ten pings and two binary searches long, so a
 * session that stopped poking here would wait all of that out before it noticed it had quit.
 */
typedef void (*ChiakiSenkushaStopCallback)(void *user);



typedef struct chiaki_session_t
{
	struct
	{
		bool ps5;
		struct addrinfo *host_addrinfos;
		struct addrinfo *host_addrinfo_selected;
		char hostname[256];
		char regist_key[CHIAKI_RPCRYPT_KEY_SIZE];
		uint8_t morning[CHIAKI_RPCRYPT_KEY_SIZE];
		uint8_t did[CHIAKI_RP_DID_SIZE];
		ChiakiConnectVideoProfile video_profile;
		bool video_profile_auto_downgrade;
		ChiakiDisableAudioVideo disable_audio_video;
		bool enable_keyboard;
		bool enable_dualsense;
		uint8_t psn_account_id[CHIAKI_PSN_ACCOUNT_ID_SIZE];
		bool enable_idr_on_fec_failure;
	} connect_info;

	ChiakiTarget target;

	uint8_t nonce[CHIAKI_RPCRYPT_KEY_SIZE];
	ChiakiRPCrypt rpcrypt;
	char session_id[CHIAKI_SESSION_ID_SIZE_MAX]; // zero-terminated
	uint8_t handshake_key[CHIAKI_HANDSHAKE_KEY_SIZE];
	uint32_t mtu_in;
	uint32_t mtu_out;
	uint64_t rtt_us;
	bool dontfrag;
	ChiakiECDH ecdh;

	ChiakiQuitReason quit_reason;
	char *quit_reason_str; // additional reason string from remote

	ChiakiEventCallback event_cb;
	void *event_cb_user;
	ChiakiVideoSampleCallback video_sample_cb;
	void *video_sample_cb_user;
	// PP696: the stream phase and its stop, installed by whoever owns the run now.
	ChiakiStreamRunCallback stream_run_cb;
	void *stream_run_cb_user;
	ChiakiStreamStopCallback stream_stop_cb;
	void *stream_stop_cb_user;
	// PP792: and senkusha's pair, for whoever measures the link now.
	ChiakiSenkushaRunCallback senkusha_run_cb;
	void *senkusha_run_cb_user;
	ChiakiSenkushaStopCallback senkusha_stop_cb;
	void *senkusha_stop_cb_user;
	ChiakiAudioSink audio_sink;
	ChiakiAudioSink haptics_sink;
	ChiakiCtrlDisplaySink display_sink;

	ChiakiThread session_thread;

	ChiakiCond state_cond;
	ChiakiMutex state_mutex;
	ChiakiStopPipe stop_pipe;
	bool auto_regist;
	bool should_stop;
	bool ctrl_failed;
	bool ctrl_session_id_received;
	bool ctrl_login_pin_requested;
	bool ctrl_first_heartbeat_received;
	bool login_pin_entered;
	bool psn_regist_succeeded;
	bool stream_connection_switch_received;
	uint8_t *login_pin;
	size_t login_pin_size;

	ChiakiCtrl ctrl;
	// PP590: the ctrl port the console answered with, recorded rather than asked for a second time.
	// PP632: nothing records it now - the ask it was read from was one of the nine - so it is zero
	// on every path and ctrl.c's own default is what answers. Kept rather than deleted: the field
	// is what a future PSN path would fill in, and ctrl.c already reads it correctly.
	uint16_t ctrl_port;
	ChiakiRudp rudp;

	ChiakiLog *log;

	// PP696: nothing initialises this any more and nothing reads it. Kept rather than deleted, the
	// way PP33 kept holepunch.c: streamconnection.c stays in the tree as unbuilt source, and a
	// header that stopped declaring what that file is written against would make it uncompilable
	// text rather than source somebody could put back.
	ChiakiStreamConnection stream_connection;

	ChiakiControllerState controller_state;
} ChiakiSession;

CHIAKI_EXPORT ChiakiErrorCode chiaki_session_init(ChiakiSession *session, ChiakiConnectInfo *connect_info, ChiakiLog *log);
CHIAKI_EXPORT void chiaki_session_fini(ChiakiSession *session);
CHIAKI_EXPORT ChiakiErrorCode chiaki_session_start(ChiakiSession *session);
CHIAKI_EXPORT ChiakiErrorCode chiaki_session_stop(ChiakiSession *session);
CHIAKI_EXPORT ChiakiErrorCode chiaki_session_join(ChiakiSession *session);

CHIAKI_EXPORT void chiaki_session_send_event(ChiakiSession *session, ChiakiEvent *event);

CHIAKI_EXPORT ChiakiErrorCode chiaki_session_request_idr(ChiakiSession *session);
CHIAKI_EXPORT ChiakiErrorCode chiaki_session_set_controller_state(ChiakiSession *session, ChiakiControllerState *state);
CHIAKI_EXPORT ChiakiErrorCode chiaki_session_set_login_pin(ChiakiSession *session, const uint8_t *pin, size_t pin_size);
CHIAKI_EXPORT ChiakiErrorCode chiaki_session_set_stream_connection_switch_received(ChiakiSession *session);
CHIAKI_EXPORT ChiakiErrorCode chiaki_session_goto_bed(ChiakiSession *session);
CHIAKI_EXPORT ChiakiErrorCode chiaki_session_toggle_microphone(ChiakiSession *session, bool muted);
CHIAKI_EXPORT ChiakiErrorCode chiaki_session_connect_microphone(ChiakiSession *session);
CHIAKI_EXPORT ChiakiErrorCode chiaki_session_keyboard_set_text(ChiakiSession *session, const char *text);
CHIAKI_EXPORT ChiakiErrorCode chiaki_session_keyboard_reject(ChiakiSession *session);
CHIAKI_EXPORT ChiakiErrorCode chiaki_session_keyboard_accept(ChiakiSession *session);
CHIAKI_EXPORT ChiakiErrorCode chiaki_session_go_home(ChiakiSession *session);

static inline void chiaki_session_set_event_cb(ChiakiSession *session, ChiakiEventCallback cb, void *user)
{
	session->event_cb = cb;
	session->event_cb_user = user;
}

static inline void chiaki_session_set_video_sample_cb(ChiakiSession *session, ChiakiVideoSampleCallback cb, void *user)
{
	session->video_sample_cb = cb;
	session->video_sample_cb_user = user;
}

/**
 * PP696: install the stream phase. Without one the session reaches it and stops there, which is
 * what a build with no host above it should do rather than pretending to stream.
 */
static inline void chiaki_session_set_stream_run_cb(ChiakiSession *session, ChiakiStreamRunCallback cb, void *user)
{
	session->stream_run_cb = cb;
	session->stream_run_cb_user = user;
}

static inline void chiaki_session_set_stream_stop_cb(ChiakiSession *session, ChiakiStreamStopCallback cb, void *user)
{
	session->stream_stop_cb = cb;
	session->stream_stop_cb_user = user;
}

/**
 * PP792: install the senkusha phase. Without one the session runs the C's, which is every build
 * this port has ever produced and is what PP763's lesson says must stay true: a measurement the
 * gate agrees with and a console does not is the shape that ships green and streams nothing, so the
 * C's run is not removed by the seam that makes it replaceable.
 */
static inline void chiaki_session_set_senkusha_run_cb(ChiakiSession *session, ChiakiSenkushaRunCallback cb, void *user)
{
	session->senkusha_run_cb = cb;
	session->senkusha_run_cb_user = user;
}

static inline void chiaki_session_set_senkusha_stop_cb(ChiakiSession *session, ChiakiSenkushaStopCallback cb, void *user)
{
	session->senkusha_stop_cb = cb;
	session->senkusha_stop_cb_user = user;
}

/**
 * @param sink contents are copied
 */
static inline void chiaki_session_set_audio_sink(ChiakiSession *session, ChiakiAudioSink *sink)
{
	session->audio_sink = *sink;
}

/**
 * @param sink contents are copied
 */
static inline void chiaki_session_set_haptics_sink(ChiakiSession *session, ChiakiAudioSink *sink)
{
	session->haptics_sink = *sink;
}

/**
 * @param sink contents are copied
 */
static inline void chiaki_session_ctrl_set_display_sink(ChiakiSession *session, ChiakiCtrlDisplaySink *sink)
{
	session->display_sink = *sink;
}

#ifdef __cplusplus
}
#endif

#endif // CHIAKI_SESSION_H
