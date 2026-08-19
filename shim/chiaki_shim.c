// SPDX-License-Identifier: LicenseRef-AGPL-3.0-only-OpenSSL

#include "chiaki_shim.h"

#include <chiaki/common.h>
#include <chiaki/decoderchoice.h>
#include <chiaki/bitstream.h>
#include <chiaki/controller.h>
#include <chiaki/discovery.h>
#include <chiaki/ecdh.h>
#include <chiaki/fec.h>
#include <chiaki/gkcrypt.h>
#include <chiaki/http.h>
#include <chiaki/reorderqueue.h>
#include <chiaki/rpcrypt.h>
#include <chiaki/seqnum.h>
#include <chiaki/log.h>
#include <chiaki/session.h>
#include <chiaki/sessionbaseline.h>
#include <chiaki/takion.h>

#include <stdlib.h>
#include <string.h>

CHIAKI_SHIM_API uint32_t chiaki_shim_abi_version(void)
{
	return CHIAKI_SHIM_ABI;
}

CHIAKI_SHIM_API const char *chiaki_shim_error_string(int32_t error_code)
{
	return chiaki_error_string((ChiakiErrorCode)error_code);
}

CHIAKI_SHIM_API const char *chiaki_shim_decoder_choice(
		bool vulkan_listed,
		bool cuda_listed,
		bool d3d11va_listed,
		bool nvidia_card,
		int32_t renderer,
		const char *requested)
{
	ChiakiDecoderChoiceInputs inputs;
	inputs.vulkan_listed = vulkan_listed;
	inputs.cuda_listed = cuda_listed;
	inputs.d3d11va_listed = d3d11va_listed;
	inputs.nvidia_card = nvidia_card;
	// Anything that is not the OpenGL enumerator is the vulkan renderer, which is what the Qt
	// caller's own boolean does. A managed caller cannot hand over an enum this side has not
	// defined, so it hands over an int and the widening happens here rather than in C#.
	inputs.renderer = renderer == (int32_t)CHIAKI_DECODER_RENDERER_OPENGL
			? CHIAKI_DECODER_RENDERER_OPENGL
			: CHIAKI_DECODER_RENDERER_VULKAN;
	inputs.requested = requested;
	return chiaki_decoder_choice(&inputs);
}

CHIAKI_SHIM_API bool chiaki_shim_decoder_choice_needs_vulkan_context(const char *choice)
{
	return chiaki_decoder_choice_needs_vulkan_context(choice);
}

/**
 * The ChiakiLog is the first member on purpose: everything below hands `&self->log` to libchiaki,
 * and every session function exported later will do the same, so the address the library keeps is
 * this allocation's and stays valid until chiaki_shim_log_free.
 */
typedef struct chiaki_shim_log_t
{
	ChiakiLog log;
	ChiakiShimLogCb cb;
	void *user;
} chiaki_shim_log;

static void chiaki_shim_log_dispatch(ChiakiLogLevel level, const char *msg, void *user)
{
	chiaki_shim_log *self = (chiaki_shim_log *)user;
	if(self && self->cb)
		self->cb((int32_t)level, msg, self->user);
}

CHIAKI_SHIM_API void *chiaki_shim_log_create(uint32_t level_mask, ChiakiShimLogCb cb, void *user)
{
	chiaki_shim_log *self = (chiaki_shim_log *)calloc(1, sizeof(chiaki_shim_log));
	if(!self)
		return NULL;

	self->cb = cb;
	self->user = user;
	// The `user` libchiaki gets is this allocation, not the caller's: the caller's pointer is
	// re-attached in the dispatcher, which is what lets the level be re-emitted on the way past.
	chiaki_log_init(&self->log, level_mask, chiaki_shim_log_dispatch, self);
	return self;
}

CHIAKI_SHIM_API void chiaki_shim_log_free(void *log)
{
	chiaki_shim_log *self = (chiaki_shim_log *)log;
	if(!self)
		return;

	// Cleared before the free so that a message already inside chiaki_log on another thread
	// finds no callback rather than a freed one. It narrows the window; it does not close it,
	// and nothing here pretends otherwise - the caller frees a log no session is using.
	self->cb = NULL;
	chiaki_log_init(&self->log, 0, NULL, NULL);
	free(self);
}

CHIAKI_SHIM_API void chiaki_shim_log_set_level(void *log, uint32_t level_mask)
{
	chiaki_shim_log *self = (chiaki_shim_log *)log;
	if(self)
		chiaki_log_set_level(&self->log, level_mask);
}

CHIAKI_SHIM_API uint32_t chiaki_shim_log_level_mask(void *log)
{
	chiaki_shim_log *self = (chiaki_shim_log *)log;
	return self ? self->log.level_mask : 0;
}

CHIAKI_SHIM_API void chiaki_shim_log_write(void *log, int32_t level, const char *msg)
{
	chiaki_shim_log *self = (chiaki_shim_log *)log;
	if(!self || !msg)
		return;

	chiaki_log(&self->log, (ChiakiLogLevel)level, "%s", msg);
}

CHIAKI_SHIM_API char chiaki_shim_log_level_char(int32_t level)
{
	return chiaki_log_level_char((ChiakiLogLevel)level);
}

CHIAKI_SHIM_API int32_t chiaki_shim_lib_init(void)
{
	return (int32_t)chiaki_lib_init();
}

/**
 * The connect info, plus the string it points at.
 *
 * ChiakiConnectInfo::host is a borrowed `const char *`, and the caller of a P/Invoke owns its
 * marshalled string only for the duration of the call. So the host is copied in here and freed
 * with the builder, which makes the lifetime this side's rather than a rule the managed side
 * would have to keep.
 */
typedef struct chiaki_shim_connect_info_t
{
	ChiakiConnectInfo info;
	char *host;
} chiaki_shim_connect_info;

CHIAKI_SHIM_API void *chiaki_shim_connect_info_create(void)
{
	chiaki_shim_connect_info *self =
			(chiaki_shim_connect_info *)calloc(1, sizeof(chiaki_shim_connect_info));
	if(!self)
		return NULL;

	// 1080p60 rather than zeroes: a video profile of 0x0 is accepted by chiaki_session_init and
	// then negotiated with the console, so an unset profile would be a black stream and not an
	// error. This is the same preset the Qt client's own default resolves to.
	chiaki_connect_video_profile_preset(&self->info.video_profile,
			CHIAKI_VIDEO_RESOLUTION_PRESET_1080p, CHIAKI_VIDEO_FPS_PRESET_60);
	return self;
}

CHIAKI_SHIM_API void chiaki_shim_connect_info_free(void *info)
{
	chiaki_shim_connect_info *self = (chiaki_shim_connect_info *)info;
	if(!self)
		return;

	free(self->host);
	free(self);
}

CHIAKI_SHIM_API bool chiaki_shim_connect_info_set_host(void *info, const char *host)
{
	chiaki_shim_connect_info *self = (chiaki_shim_connect_info *)info;
	if(!self || !host)
		return false;

	size_t len = strlen(host);
	char *copy = (char *)malloc(len + 1);
	if(!copy)
		return false;
	memcpy(copy, host, len + 1);

	free(self->host);
	self->host = copy;
	self->info.host = copy;
	return true;
}

CHIAKI_SHIM_API void chiaki_shim_connect_info_set_ps5(void *info, bool ps5)
{
	chiaki_shim_connect_info *self = (chiaki_shim_connect_info *)info;
	if(self)
		self->info.ps5 = ps5;
}

CHIAKI_SHIM_API bool chiaki_shim_connect_info_set_regist_key(
		void *info, const uint8_t *key, int32_t len)
{
	chiaki_shim_connect_info *self = (chiaki_shim_connect_info *)info;
	if(!self || !key || len < 0 || (size_t)len > sizeof(self->info.regist_key))
		return false;

	// Zeroed first: the field must be "completely filled (pad with \0)", and a second call with a
	// shorter key would otherwise leave the tail of the first one behind it.
	memset(self->info.regist_key, 0, sizeof(self->info.regist_key));
	memcpy(self->info.regist_key, key, (size_t)len);
	return true;
}

CHIAKI_SHIM_API bool chiaki_shim_connect_info_set_morning(
		void *info, const uint8_t *morning, int32_t len)
{
	chiaki_shim_connect_info *self = (chiaki_shim_connect_info *)info;
	if(!self || !morning || (size_t)len != sizeof(self->info.morning))
		return false;

	memcpy(self->info.morning, morning, sizeof(self->info.morning));
	return true;
}

CHIAKI_SHIM_API void chiaki_shim_connect_info_set_video_preset(
		void *info, int32_t resolution, int32_t fps)
{
	chiaki_shim_connect_info *self = (chiaki_shim_connect_info *)info;
	if(self)
		chiaki_connect_video_profile_preset(&self->info.video_profile,
				(ChiakiVideoResolutionPreset)resolution, (ChiakiVideoFPSPreset)fps);
}

CHIAKI_SHIM_API void chiaki_shim_connect_info_set_bitrate(void *info, uint32_t bitrate)
{
	chiaki_shim_connect_info *self = (chiaki_shim_connect_info *)info;
	if(self)
		self->info.video_profile.bitrate = bitrate;
}

CHIAKI_SHIM_API void chiaki_shim_connect_info_set_codec(void *info, int32_t codec)
{
	chiaki_shim_connect_info *self = (chiaki_shim_connect_info *)info;
	if(self)
		self->info.video_profile.codec = (ChiakiCodec)codec;
}

CHIAKI_SHIM_API void chiaki_shim_connect_info_video_profile(
		void *info,
		uint32_t *width,
		uint32_t *height,
		uint32_t *max_fps,
		uint32_t *bitrate,
		int32_t *codec)
{
	chiaki_shim_connect_info *self = (chiaki_shim_connect_info *)info;
	const ChiakiConnectVideoProfile *p = self ? &self->info.video_profile : NULL;

	if(width)
		*width = p ? (uint32_t)p->width : 0;
	if(height)
		*height = p ? (uint32_t)p->height : 0;
	if(max_fps)
		*max_fps = p ? (uint32_t)p->max_fps : 0;
	if(bitrate)
		*bitrate = p ? (uint32_t)p->bitrate : 0;
	if(codec)
		*codec = p ? (int32_t)p->codec : 0;
}

CHIAKI_SHIM_API void chiaki_shim_connect_info_set_flags(
		void *info,
		bool video_profile_auto_downgrade,
		bool enable_keyboard,
		bool enable_dualsense,
		bool enable_idr_on_fec_failure)
{
	chiaki_shim_connect_info *self = (chiaki_shim_connect_info *)info;
	if(!self)
		return;

	self->info.video_profile_auto_downgrade = video_profile_auto_downgrade;
	self->info.enable_keyboard = enable_keyboard;
	self->info.enable_dualsense = enable_dualsense;
	self->info.enable_idr_on_fec_failure = enable_idr_on_fec_failure;
}

CHIAKI_SHIM_API void chiaki_shim_connect_info_set_packet_loss_max(
		void *info, double packet_loss_max)
{
	chiaki_shim_connect_info *self = (chiaki_shim_connect_info *)info;
	if(self)
		self->info.packet_loss_max = packet_loss_max;
}

/**
 * As with the log: the ChiakiSession is the first member, so the handle the managed side holds is
 * the address libchiaki was given, and the callback and its user pointer ride alongside it where
 * only this file can see them.
 */
typedef struct chiaki_shim_session_t
{
	ChiakiSession session;
	ChiakiShimEventCb cb;
	void *user;
} chiaki_shim_session;

static void chiaki_shim_session_dispatch(ChiakiEvent *event, void *user)
{
	chiaki_shim_session *self = (chiaki_shim_session *)user;
	if(!self || !self->cb || !event)
		return;

	// Decoded arm by arm rather than handed over whole. The quit arm is the one that ends every
	// session; the rest arrive as a type until the screen that reads their payload exists.
	if(event->type == CHIAKI_EVENT_QUIT)
		self->cb((int32_t)event->type, (int32_t)event->quit.reason, event->quit.reason_str,
				self->user);
	else
		self->cb((int32_t)event->type, 0, NULL, self->user);
}

CHIAKI_SHIM_API void *chiaki_shim_session_create(void *connect_info, void *log, int32_t *error_out)
{
	chiaki_shim_connect_info *info = (chiaki_shim_connect_info *)connect_info;
	chiaki_shim_log *log_self = (chiaki_shim_log *)log;

	if(error_out)
		*error_out = (int32_t)CHIAKI_ERR_INVALID_DATA;
	if(!info || !info->info.host)
		return NULL;

	chiaki_shim_session *self = (chiaki_shim_session *)calloc(1, sizeof(chiaki_shim_session));
	if(!self)
	{
		if(error_out)
			*error_out = (int32_t)CHIAKI_ERR_MEMORY;
		return NULL;
	}

	ChiakiErrorCode err = chiaki_session_init(&self->session, &info->info,
			log_self ? &log_self->log : NULL);
	if(error_out)
		*error_out = (int32_t)err;

	if(err != CHIAKI_ERR_SUCCESS)
	{
		// No chiaki_session_fini here on purpose: chiaki_session_init unwinds whatever it had
		// built before it failed, and the address-parse path calls fini itself before returning.
		// A second one would be a double free of the ctrl and the stop pipe.
		free(self);
		return NULL;
	}

	return self;
}

CHIAKI_SHIM_API bool chiaki_shim_session_set_event_cb(
		void *session, ChiakiShimEventCb cb, void *user)
{
	chiaki_shim_session *self = (chiaki_shim_session *)session;
	if(!self)
		return false;

	self->cb = cb;
	self->user = user;
	chiaki_session_set_event_cb(&self->session, cb ? chiaki_shim_session_dispatch : NULL, self);
	return true;
}

CHIAKI_SHIM_API int32_t chiaki_shim_session_start(void *session)
{
	chiaki_shim_session *self = (chiaki_shim_session *)session;
	return self ? (int32_t)chiaki_session_start(&self->session) : (int32_t)CHIAKI_ERR_INVALID_DATA;
}

CHIAKI_SHIM_API int32_t chiaki_shim_session_stop(void *session)
{
	chiaki_shim_session *self = (chiaki_shim_session *)session;
	return self ? (int32_t)chiaki_session_stop(&self->session) : (int32_t)CHIAKI_ERR_INVALID_DATA;
}

CHIAKI_SHIM_API int32_t chiaki_shim_session_join(void *session)
{
	chiaki_shim_session *self = (chiaki_shim_session *)session;
	return self ? (int32_t)chiaki_session_join(&self->session) : (int32_t)CHIAKI_ERR_INVALID_DATA;
}

CHIAKI_SHIM_API void *chiaki_shim_controller_state_create(void)
{
	ChiakiControllerState *state = (ChiakiControllerState *)calloc(1, sizeof(ChiakiControllerState));
	if(state)
		chiaki_controller_state_set_idle(state);
	return state;
}

CHIAKI_SHIM_API void chiaki_shim_controller_state_free(void *state)
{
	free(state);
}

CHIAKI_SHIM_API void chiaki_shim_controller_state_set_idle(void *state)
{
	if(state)
		chiaki_controller_state_set_idle((ChiakiControllerState *)state);
}

CHIAKI_SHIM_API void chiaki_shim_controller_state_set_buttons(void *state, uint32_t buttons)
{
	if(state)
		((ChiakiControllerState *)state)->buttons = buttons;
}

CHIAKI_SHIM_API uint32_t chiaki_shim_controller_state_buttons(void *state)
{
	return state ? ((ChiakiControllerState *)state)->buttons : 0;
}

CHIAKI_SHIM_API void chiaki_shim_controller_state_set_triggers(void *state, uint8_t l2, uint8_t r2)
{
	ChiakiControllerState *self = (ChiakiControllerState *)state;
	if(!self)
		return;

	self->l2_state = l2;
	self->r2_state = r2;
}

CHIAKI_SHIM_API void chiaki_shim_controller_state_triggers(void *state, uint8_t *l2, uint8_t *r2)
{
	ChiakiControllerState *self = (ChiakiControllerState *)state;
	if(l2)
		*l2 = self ? self->l2_state : 0;
	if(r2)
		*r2 = self ? self->r2_state : 0;
}

CHIAKI_SHIM_API void chiaki_shim_controller_state_set_sticks(
		void *state, int16_t left_x, int16_t left_y, int16_t right_x, int16_t right_y)
{
	ChiakiControllerState *self = (ChiakiControllerState *)state;
	if(!self)
		return;

	self->left_x = left_x;
	self->left_y = left_y;
	self->right_x = right_x;
	self->right_y = right_y;
}

CHIAKI_SHIM_API void chiaki_shim_controller_state_sticks(
		void *state, int16_t *left_x, int16_t *left_y, int16_t *right_x, int16_t *right_y)
{
	ChiakiControllerState *self = (ChiakiControllerState *)state;
	if(left_x)
		*left_x = self ? self->left_x : 0;
	if(left_y)
		*left_y = self ? self->left_y : 0;
	if(right_x)
		*right_x = self ? self->right_x : 0;
	if(right_y)
		*right_y = self ? self->right_y : 0;
}

CHIAKI_SHIM_API void chiaki_shim_controller_state_set_motion(
		void *state,
		float gyro_x, float gyro_y, float gyro_z,
		float accel_x, float accel_y, float accel_z,
		float orient_x, float orient_y, float orient_z, float orient_w)
{
	ChiakiControllerState *self = (ChiakiControllerState *)state;
	if(!self)
		return;

	self->gyro_x = gyro_x;
	self->gyro_y = gyro_y;
	self->gyro_z = gyro_z;
	self->accel_x = accel_x;
	self->accel_y = accel_y;
	self->accel_z = accel_z;
	self->orient_x = orient_x;
	self->orient_y = orient_y;
	self->orient_z = orient_z;
	self->orient_w = orient_w;
}

CHIAKI_SHIM_API int8_t chiaki_shim_controller_state_start_touch(void *state, uint16_t x, uint16_t y)
{
	return state ? chiaki_controller_state_start_touch((ChiakiControllerState *)state, x, y) : -1;
}

CHIAKI_SHIM_API void chiaki_shim_controller_state_stop_touch(void *state, uint8_t id)
{
	if(state)
		chiaki_controller_state_stop_touch((ChiakiControllerState *)state, id);
}

CHIAKI_SHIM_API void chiaki_shim_controller_state_set_touch_pos(
		void *state, uint8_t id, uint16_t x, uint16_t y)
{
	if(state)
		chiaki_controller_state_set_touch_pos((ChiakiControllerState *)state, id, x, y);
}

CHIAKI_SHIM_API bool chiaki_shim_controller_state_touch(
		void *state, int32_t slot, uint16_t *x, uint16_t *y, int32_t *id)
{
	ChiakiControllerState *self = (ChiakiControllerState *)state;
	if(!self || slot < 0 || (size_t)slot >= CHIAKI_CONTROLLER_TOUCHES_MAX)
		return false;

	if(x)
		*x = self->touches[slot].x;
	if(y)
		*y = self->touches[slot].y;
	if(id)
		*id = self->touches[slot].id;
	return true;
}

CHIAKI_SHIM_API bool chiaki_shim_controller_state_equals(void *a, void *b)
{
	if(!a || !b)
		return false;

	return chiaki_controller_state_equals((ChiakiControllerState *)a, (ChiakiControllerState *)b);
}

CHIAKI_SHIM_API void chiaki_shim_controller_state_or(void *out, void *a, void *b)
{
	if(!out || !a || !b)
		return;

	chiaki_controller_state_or((ChiakiControllerState *)out, (ChiakiControllerState *)a,
			(ChiakiControllerState *)b);
}

CHIAKI_SHIM_API int32_t chiaki_shim_session_set_controller_state(void *session, void *state)
{
	chiaki_shim_session *self = (chiaki_shim_session *)session;
	if(!self || !state)
		return (int32_t)CHIAKI_ERR_INVALID_DATA;

	return (int32_t)chiaki_session_set_controller_state(&self->session, (ChiakiControllerState *)state);
}

CHIAKI_SHIM_API bool chiaki_shim_session_controller_state_matches(void *session, void *state)
{
	chiaki_shim_session *self = (chiaki_shim_session *)session;
	if(!self || !state)
		return false;

	return chiaki_controller_state_equals(&self->session.controller_state,
			(ChiakiControllerState *)state);
}

CHIAKI_SHIM_API uint32_t chiaki_shim_baseline_schema(void)
{
	return (uint32_t)CHIAKI_SESSION_BASELINE_SCHEMA;
}

CHIAKI_SHIM_API int32_t chiaki_shim_baseline_line_max(void)
{
	return (int32_t)CHIAKI_SESSION_BASELINE_LINE_MAX;
}

CHIAKI_SHIM_API void *chiaki_shim_baseline_create(void)
{
	ChiakiSessionBaseline *self = (ChiakiSessionBaseline *)calloc(1, sizeof(ChiakiSessionBaseline));
	if(self)
		chiaki_session_baseline_init(self);
	return self;
}

CHIAKI_SHIM_API void chiaki_shim_baseline_free(void *baseline)
{
	free(baseline);
}

CHIAKI_SHIM_API void chiaki_shim_baseline_set_started(void *baseline, uint64_t unix_seconds)
{
	if(baseline)
		chiaki_session_baseline_set_started((ChiakiSessionBaseline *)baseline, unix_seconds);
}

CHIAKI_SHIM_API void chiaki_shim_baseline_set_duration_ms(void *baseline, uint64_t duration_ms)
{
	if(baseline)
		((ChiakiSessionBaseline *)baseline)->duration_ms = duration_ms;
}

CHIAKI_SHIM_API void chiaki_shim_baseline_set_app_version(void *baseline, const char *version)
{
	if(baseline && version)
		chiaki_session_baseline_set_app_version((ChiakiSessionBaseline *)baseline, version);
}

CHIAKI_SHIM_API void chiaki_shim_baseline_set_video(
		void *baseline,
		const char *codec,
		uint32_t width,
		uint32_t height,
		uint32_t fps,
		uint32_t bitrate_kbps)
{
	ChiakiSessionBaseline *self = (ChiakiSessionBaseline *)baseline;
	if(!self)
		return;

	if(codec)
		chiaki_session_baseline_set_video_codec(self, codec);
	self->video_width = width;
	self->video_height = height;
	self->video_fps = fps;
	self->bitrate_kbps = bitrate_kbps;
}

CHIAKI_SHIM_API void chiaki_shim_baseline_set_config(
		void *baseline,
		const char *hw_decoder,
		const char *renderer,
		double packet_loss_max,
		bool idr_on_fec_failure)
{
	ChiakiSessionBaseline *self = (ChiakiSessionBaseline *)baseline;
	if(!self)
		return;

	if(hw_decoder)
		chiaki_session_baseline_set_hw_decoder(self, hw_decoder);
	if(renderer)
		chiaki_session_baseline_set_renderer(self, renderer);
	self->packet_loss_max = packet_loss_max;
	self->idr_on_fec_failure = idr_on_fec_failure;
}

CHIAKI_SHIM_API void chiaki_shim_baseline_set_measured(
		void *baseline,
		double measured_bitrate_mbps,
		double average_packet_loss,
		uint64_t frames_presented,
		uint64_t frames_lost,
		uint64_t frames_dropped,
		uint64_t network_rtt_us)
{
	ChiakiSessionBaseline *self = (ChiakiSessionBaseline *)baseline;
	if(!self)
		return;

	self->measured_bitrate_mbps = measured_bitrate_mbps;
	self->average_packet_loss = average_packet_loss;
	self->frames_presented = frames_presented;
	self->frames_lost = frames_lost;
	self->frames_dropped = frames_dropped;
	self->network_rtt_us = network_rtt_us;
}

CHIAKI_SHIM_API void chiaki_shim_baseline_push_handoff(void *baseline, uint64_t handoff_us)
{
	if(baseline)
		chiaki_session_baseline_push_handoff((ChiakiSessionBaseline *)baseline, handoff_us);
}

CHIAKI_SHIM_API void chiaki_shim_baseline_push_input_to_wire(void *baseline, uint64_t input_us)
{
	if(baseline)
		chiaki_session_baseline_push_input_to_wire((ChiakiSessionBaseline *)baseline, input_us);
}

CHIAKI_SHIM_API uint64_t chiaki_shim_baseline_handoff_avg_us(void *baseline)
{
	return baseline
			? chiaki_session_baseline_handoff_us_avg((const ChiakiSessionBaseline *)baseline)
			: 0;
}

CHIAKI_SHIM_API uint64_t chiaki_shim_baseline_latency_estimate_us(void *baseline)
{
	return baseline
			? chiaki_session_baseline_latency_estimate_us((const ChiakiSessionBaseline *)baseline)
			: 0;
}

CHIAKI_SHIM_API int32_t chiaki_shim_baseline_format(
		void *baseline, char *buf, int32_t buf_size, int32_t *written)
{
	size_t out = 0;
	ChiakiErrorCode err;

	if(written)
		*written = 0;
	if(!baseline || !buf || buf_size <= 0)
		return (int32_t)CHIAKI_ERR_INVALID_DATA;

	err = chiaki_session_baseline_format((const ChiakiSessionBaseline *)baseline, buf,
			(size_t)buf_size, &out);
	if(written)
		*written = (int32_t)out;
	return (int32_t)err;
}

CHIAKI_SHIM_API int32_t chiaki_shim_baseline_append(void *baseline, const char *path)
{
	if(!baseline || !path)
		return (int32_t)CHIAKI_ERR_INVALID_DATA;

	return (int32_t)chiaki_session_baseline_append((const ChiakiSessionBaseline *)baseline, path);
}

CHIAKI_SHIM_API int32_t chiaki_shim_discovery_port(bool ps5)
{
	return ps5 ? CHIAKI_DISCOVERY_PORT_PS5 : CHIAKI_DISCOVERY_PORT_PS4;
}

CHIAKI_SHIM_API const char *chiaki_shim_discovery_protocol_version(bool ps5)
{
	return ps5 ? CHIAKI_DISCOVERY_PROTOCOL_VERSION_PS5 : CHIAKI_DISCOVERY_PROTOCOL_VERSION_PS4;
}

CHIAKI_SHIM_API int32_t chiaki_shim_discovery_local_port_min(void)
{
	return CHIAKI_DISCOVERY_PORT_LOCAL_MIN;
}

CHIAKI_SHIM_API int32_t chiaki_shim_discovery_local_port_max(void)
{
	return CHIAKI_DISCOVERY_PORT_LOCAL_MAX;
}

CHIAKI_SHIM_API int32_t chiaki_shim_discovery_packet_fmt(
		int32_t cmd,
		const char *protocol_version,
		uint64_t user_credential,
		char *buf,
		int32_t buf_size)
{
	ChiakiDiscoveryPacket packet;
	if(!buf || buf_size <= 0)
		return -1;

	packet.cmd = (ChiakiDiscoveryCmd)cmd;
	// The field is a char* rather than a const char* in libchiaki and is only read, so the cast
	// is dropping a const the caller's string never lost.
	packet.protocol_version = (char *)protocol_version;
	packet.user_credential = user_credential;
	return (int32_t)chiaki_discovery_packet_fmt(buf, (size_t)buf_size, &packet);
}

/** Fills only the two fields the classification reads, leaving the rest as a reply never had. */
static void chiaki_shim_discovery_host_of(
		ChiakiDiscoveryHost *host, const char *system_version, const char *protocol_version)
{
	memset(host, 0, sizeof(*host));
	host->system_version = system_version ? system_version : "";
	host->device_discovery_protocol_version = protocol_version;
}

CHIAKI_SHIM_API bool chiaki_shim_discovery_is_ps5(const char *device_discovery_protocol_version)
{
	ChiakiDiscoveryHost host;
	chiaki_shim_discovery_host_of(&host, "", device_discovery_protocol_version);
	return chiaki_discovery_host_is_ps5(&host);
}

CHIAKI_SHIM_API int32_t chiaki_shim_discovery_target(
		const char *system_version, const char *device_discovery_protocol_version)
{
	ChiakiDiscoveryHost host;
	chiaki_shim_discovery_host_of(&host, system_version, device_discovery_protocol_version);
	return (int32_t)chiaki_discovery_host_system_version_target(&host);
}

CHIAKI_SHIM_API const char *chiaki_shim_discovery_host_state_string(int32_t state)
{
	return chiaki_discovery_host_state_string((ChiakiDiscoveryHostState)state);
}

/**
 * PP6: declared here because libchiaki declares it nowhere.
 *
 * chiaki_discovery_srch_response_parse is CHIAKI_EXPORT in lib/src/discovery.c and appears in no
 * header, so it is reachable and unannounced. Reaching for it anyway is the lesser of two evils:
 * the alternative is a second reply parser on this side of the seam, which is the one piece of
 * discovery a console would have to be present to disprove.
 *
 * What that costs is a signature the compiler cannot check against its definition, which is the
 * same debt PP88's duplicated regex took on. It is paid the same way: the .NET selftest reads
 * lib/src/discovery.c and holds this declaration against the definition there, so a libchiaki that
 * changes the signature turns a test red rather than corrupting a stack quietly.
 *
 * lib/ stays untouched, which is the rule this works around rather than breaks.
 */
ChiakiErrorCode chiaki_discovery_srch_response_parse(ChiakiDiscoveryHost *response, struct sockaddr *addr, char *addr_buf, size_t addr_buf_size, char *buf, size_t buf_size);

/**
 * The parsed host plus the two buffers its strings point into.
 *
 * `reply` is this side's copy of the datagram, because the parse is in place and every string in
 * `host` is an offset into it. `addr` is where sockaddr_str wrote the sender's address, which
 * host_addr points at for the same reason. Both live exactly as long as the handle.
 */
typedef struct chiaki_shim_discovery_reply_t
{
	ChiakiDiscoveryHost host;
	char *reply;
	char addr[64];
} chiaki_shim_discovery_reply;

CHIAKI_SHIM_API void *chiaki_shim_discovery_reply_parse(
		const char *reply, int32_t reply_len, const char *from_addr, int32_t *error_out)
{
	struct sockaddr_in addr;
	chiaki_shim_discovery_reply *self;
	ChiakiErrorCode err;

	if(error_out)
		*error_out = (int32_t)CHIAKI_ERR_INVALID_DATA;
	if(!reply || reply_len <= 0 || !from_addr)
		return NULL;

	memset(&addr, 0, sizeof(addr));
	addr.sin_family = AF_INET;
	if(inet_pton(AF_INET, from_addr, &addr.sin_addr) != 1)
		return NULL;

	self = (chiaki_shim_discovery_reply *)calloc(1, sizeof(chiaki_shim_discovery_reply));
	if(!self)
	{
		if(error_out)
			*error_out = (int32_t)CHIAKI_ERR_MEMORY;
		return NULL;
	}

	// The parser writes NULs into what it is given, so it is given this copy and not the caller's
	// string. One byte over for a terminator the datagram may not carry.
	self->reply = (char *)malloc((size_t)reply_len + 1);
	if(!self->reply)
	{
		free(self);
		if(error_out)
			*error_out = (int32_t)CHIAKI_ERR_MEMORY;
		return NULL;
	}
	memcpy(self->reply, reply, (size_t)reply_len);
	self->reply[reply_len] = '\0';

	err = chiaki_discovery_srch_response_parse(&self->host, (struct sockaddr *)&addr, self->addr,
			sizeof(self->addr), self->reply, (size_t)reply_len);
	if(error_out)
		*error_out = (int32_t)err;

	if(err != CHIAKI_ERR_SUCCESS)
	{
		free(self->reply);
		free(self);
		return NULL;
	}

	return self;
}

CHIAKI_SHIM_API void chiaki_shim_discovery_reply_free(void *host)
{
	chiaki_shim_discovery_reply *self = (chiaki_shim_discovery_reply *)host;
	if(!self)
		return;

	free(self->reply);
	free(self);
}

CHIAKI_SHIM_API int32_t chiaki_shim_discovery_reply_state(void *host)
{
	chiaki_shim_discovery_reply *self = (chiaki_shim_discovery_reply *)host;
	return self ? (int32_t)self->host.state : (int32_t)CHIAKI_DISCOVERY_HOST_STATE_UNKNOWN;
}

CHIAKI_SHIM_API int32_t chiaki_shim_discovery_reply_request_port(void *host)
{
	chiaki_shim_discovery_reply *self = (chiaki_shim_discovery_reply *)host;
	return self ? (int32_t)self->host.host_request_port : 0;
}

CHIAKI_SHIM_API const char *chiaki_shim_discovery_reply_field(void *host, int32_t field)
{
	chiaki_shim_discovery_reply *self = (chiaki_shim_discovery_reply *)host;
	if(!self)
		return NULL;

	switch((ChiakiShimDiscoveryField)field)
	{
		case CHIAKI_SHIM_DISCOVERY_HOST_ADDR:
			return self->host.host_addr;
		case CHIAKI_SHIM_DISCOVERY_SYSTEM_VERSION:
			return self->host.system_version;
		case CHIAKI_SHIM_DISCOVERY_PROTOCOL_VERSION:
			return self->host.device_discovery_protocol_version;
		case CHIAKI_SHIM_DISCOVERY_HOST_NAME:
			return self->host.host_name;
		case CHIAKI_SHIM_DISCOVERY_HOST_TYPE:
			return self->host.host_type;
		case CHIAKI_SHIM_DISCOVERY_HOST_ID:
			return self->host.host_id;
		case CHIAKI_SHIM_DISCOVERY_RUNNING_APP_TITLEID:
			return self->host.running_app_titleid;
		case CHIAKI_SHIM_DISCOVERY_RUNNING_APP_NAME:
			return self->host.running_app_name;
		default:
			return NULL;
	}
}

CHIAKI_SHIM_API int32_t chiaki_shim_rpcrypt_key_size(void)
{
	return (int32_t)CHIAKI_RPCRYPT_KEY_SIZE;
}

CHIAKI_SHIM_API bool chiaki_shim_rpcrypt_bright_ambassador(
		int32_t target,
		uint8_t *bright,
		uint8_t *ambassador,
		const uint8_t *nonce,
		const uint8_t *morning)
{
	if(!bright || !ambassador || !nonce || !morning)
		return false;

	chiaki_rpcrypt_bright_ambassador((ChiakiTarget)target, bright, ambassador, nonce, morning);
	return true;
}

CHIAKI_SHIM_API void *chiaki_shim_rpcrypt_create_auth(
		int32_t target, const uint8_t *nonce, const uint8_t *morning)
{
	ChiakiRPCrypt *self;
	if(!nonce || !morning)
		return NULL;

	self = (ChiakiRPCrypt *)calloc(1, sizeof(ChiakiRPCrypt));
	if(!self)
		return NULL;

	chiaki_rpcrypt_init_auth(self, (ChiakiTarget)target, nonce, morning);
	return self;
}

CHIAKI_SHIM_API void chiaki_shim_rpcrypt_free(void *rpcrypt)
{
	free(rpcrypt);
}

CHIAKI_SHIM_API int32_t chiaki_shim_rpcrypt_generate_iv(void *rpcrypt, uint64_t counter, uint8_t *iv)
{
	if(!rpcrypt || !iv)
		return (int32_t)CHIAKI_ERR_INVALID_DATA;

	return (int32_t)chiaki_rpcrypt_generate_iv((ChiakiRPCrypt *)rpcrypt, iv, counter);
}

CHIAKI_SHIM_API int32_t chiaki_shim_rpcrypt_encrypt(
		void *rpcrypt, uint64_t counter, const uint8_t *in, uint8_t *out, int32_t size)
{
	if(!rpcrypt || !in || !out || size < 0)
		return (int32_t)CHIAKI_ERR_INVALID_DATA;

	return (int32_t)chiaki_rpcrypt_encrypt((ChiakiRPCrypt *)rpcrypt, counter, in, out, (size_t)size);
}

CHIAKI_SHIM_API int32_t chiaki_shim_rpcrypt_decrypt(
		void *rpcrypt, uint64_t counter, const uint8_t *in, uint8_t *out, int32_t size)
{
	if(!rpcrypt || !in || !out || size < 0)
		return (int32_t)CHIAKI_ERR_INVALID_DATA;

	return (int32_t)chiaki_rpcrypt_decrypt((ChiakiRPCrypt *)rpcrypt, counter, in, out, (size_t)size);
}

CHIAKI_SHIM_API int32_t chiaki_shim_fec_decode(
		uint8_t *frame_buf,
		int32_t unit_size,
		int32_t stride,
		uint32_t k,
		uint32_t m,
		const uint32_t *erasures,
		int32_t erasures_count)
{
	if(!frame_buf || unit_size <= 0 || stride <= 0 || erasures_count < 0)
		return (int32_t)CHIAKI_ERR_INVALID_DATA;
	if(erasures_count > 0 && !erasures)
		return (int32_t)CHIAKI_ERR_INVALID_DATA;

	return (int32_t)chiaki_fec_decode(frame_buf, (size_t)unit_size, (size_t)stride, k, m,
			(const unsigned int *)erasures, (size_t)erasures_count);
}

CHIAKI_SHIM_API int32_t chiaki_shim_ecdh_secret_size(void)
{
	return (int32_t)CHIAKI_ECDH_SECRET_SIZE;
}

CHIAKI_SHIM_API void *chiaki_shim_ecdh_create(void)
{
	ChiakiECDH *self = (ChiakiECDH *)calloc(1, sizeof(ChiakiECDH));
	if(!self)
		return NULL;

	if(chiaki_ecdh_init(self) != CHIAKI_ERR_SUCCESS)
	{
		free(self);
		return NULL;
	}
	return self;
}

CHIAKI_SHIM_API void chiaki_shim_ecdh_free(void *ecdh)
{
	if(!ecdh)
		return;

	chiaki_ecdh_fini((ChiakiECDH *)ecdh);
	free(ecdh);
}

CHIAKI_SHIM_API int32_t chiaki_shim_ecdh_set_local_key(
		void *ecdh,
		const uint8_t *private_key, int32_t private_key_size,
		const uint8_t *public_key, int32_t public_key_size)
{
	if(!ecdh || !private_key || !public_key || private_key_size <= 0 || public_key_size <= 0)
		return (int32_t)CHIAKI_ERR_INVALID_DATA;

	return (int32_t)chiaki_ecdh_set_local_key((ChiakiECDH *)ecdh, private_key,
			(size_t)private_key_size, public_key, (size_t)public_key_size);
}

CHIAKI_SHIM_API int32_t chiaki_shim_ecdh_local_pub_key(
		void *ecdh,
		const uint8_t *handshake_key,
		uint8_t *key_out, int32_t *key_out_size,
		uint8_t *sig_out, int32_t *sig_out_size)
{
	size_t key_size;
	size_t sig_size;
	ChiakiErrorCode err;

	if(!ecdh || !handshake_key || !key_out || !key_out_size || !sig_out || !sig_out_size)
		return (int32_t)CHIAKI_ERR_INVALID_DATA;
	if(*key_out_size <= 0 || *sig_out_size <= 0)
		return (int32_t)CHIAKI_ERR_BUF_TOO_SMALL;

	key_size = (size_t)*key_out_size;
	sig_size = (size_t)*sig_out_size;
	err = chiaki_ecdh_get_local_pub_key((ChiakiECDH *)ecdh, key_out, &key_size, handshake_key,
			sig_out, &sig_size);
	*key_out_size = (int32_t)key_size;
	*sig_out_size = (int32_t)sig_size;
	return (int32_t)err;
}

CHIAKI_SHIM_API int32_t chiaki_shim_ecdh_derive_secret(
		void *ecdh,
		uint8_t *secret_out,
		const uint8_t *remote_key, int32_t remote_key_size,
		const uint8_t *handshake_key,
		const uint8_t *remote_sig, int32_t remote_sig_size)
{
	if(!ecdh || !secret_out || !remote_key || !handshake_key || !remote_sig)
		return (int32_t)CHIAKI_ERR_INVALID_DATA;
	if(remote_key_size <= 0 || remote_sig_size <= 0)
		return (int32_t)CHIAKI_ERR_INVALID_DATA;

	return (int32_t)chiaki_ecdh_derive_secret((ChiakiECDH *)ecdh, secret_out, remote_key,
			(size_t)remote_key_size, handshake_key, remote_sig, (size_t)remote_sig_size);
}

CHIAKI_SHIM_API void *chiaki_shim_gkcrypt_create(
		void *log,
		int32_t key_buf_chunks,
		uint8_t index,
		const uint8_t *handshake_key,
		const uint8_t *ecdh_secret)
{
	chiaki_shim_log *log_self = (chiaki_shim_log *)log;
	ChiakiGKCrypt *self;

	if(!handshake_key || !ecdh_secret || key_buf_chunks < 0)
		return NULL;

	self = (ChiakiGKCrypt *)calloc(1, sizeof(ChiakiGKCrypt));
	if(!self)
		return NULL;

	if(chiaki_gkcrypt_init(self, log_self ? &log_self->log : NULL, (size_t)key_buf_chunks, index,
			handshake_key, ecdh_secret) != CHIAKI_ERR_SUCCESS)
	{
		free(self);
		return NULL;
	}
	return self;
}

CHIAKI_SHIM_API void chiaki_shim_gkcrypt_free(void *gkcrypt)
{
	if(!gkcrypt)
		return;

	chiaki_gkcrypt_fini((ChiakiGKCrypt *)gkcrypt);
	free(gkcrypt);
}

CHIAKI_SHIM_API int32_t chiaki_shim_gkcrypt_gen_key_stream(
		void *gkcrypt, uint64_t key_pos, uint8_t *buf, int32_t buf_size)
{
	if(!gkcrypt || !buf || buf_size <= 0)
		return (int32_t)CHIAKI_ERR_INVALID_DATA;

	return (int32_t)chiaki_gkcrypt_gen_key_stream((ChiakiGKCrypt *)gkcrypt, key_pos, buf,
			(size_t)buf_size);
}

CHIAKI_SHIM_API bool chiaki_shim_seq_num_16_lt(uint16_t a, uint16_t b)
{
	return chiaki_seq_num_16_lt(a, b);
}

CHIAKI_SHIM_API bool chiaki_shim_seq_num_16_gt(uint16_t a, uint16_t b)
{
	return chiaki_seq_num_16_gt(a, b);
}

CHIAKI_SHIM_API bool chiaki_shim_seq_num_32_lt(uint32_t a, uint32_t b)
{
	return chiaki_seq_num_32_lt(a, b);
}

CHIAKI_SHIM_API bool chiaki_shim_seq_num_32_gt(uint32_t a, uint32_t b)
{
	return chiaki_seq_num_32_gt(a, b);
}

/** As with the log and the session: the library's struct first, the callback beside it. */
typedef struct chiaki_shim_reorder_queue_t
{
	ChiakiReorderQueue queue;
	ChiakiShimReorderDropCb cb;
	void *user;
} chiaki_shim_reorder_queue;

static void chiaki_shim_reorder_drop(uint64_t seq_num, void *elem_user, void *cb_user)
{
	chiaki_shim_reorder_queue *self = (chiaki_shim_reorder_queue *)cb_user;
	if(self && self->cb)
		self->cb(seq_num, elem_user, self->user);
}

CHIAKI_SHIM_API void *chiaki_shim_reorder_queue_create_16(
		int32_t size_exp, uint16_t seq_num_start, ChiakiShimReorderDropCb cb, void *user)
{
	chiaki_shim_reorder_queue *self;
	if(size_exp < 0)
		return NULL;

	self = (chiaki_shim_reorder_queue *)calloc(1, sizeof(chiaki_shim_reorder_queue));
	if(!self)
		return NULL;

	if(chiaki_reorder_queue_init_16(&self->queue, (size_t)size_exp, seq_num_start)
			!= CHIAKI_ERR_SUCCESS)
	{
		free(self);
		return NULL;
	}

	self->cb = cb;
	self->user = user;
	chiaki_reorder_queue_set_drop_cb(&self->queue, cb ? chiaki_shim_reorder_drop : NULL, self);
	return self;
}

CHIAKI_SHIM_API void chiaki_shim_reorder_queue_free(void *queue)
{
	chiaki_shim_reorder_queue *self = (chiaki_shim_reorder_queue *)queue;
	if(!self)
		return;

	// Cleared first, because fini drops what is still queued and every one of those is a callback
	// into managed code that is about to stop being interested.
	self->cb = NULL;
	chiaki_reorder_queue_set_drop_cb(&self->queue, NULL, NULL);
	chiaki_reorder_queue_fini(&self->queue);
	free(self);
}

CHIAKI_SHIM_API void chiaki_shim_reorder_queue_set_drop_strategy(void *queue, int32_t strategy)
{
	chiaki_shim_reorder_queue *self = (chiaki_shim_reorder_queue *)queue;
	if(self)
		chiaki_reorder_queue_set_drop_strategy(&self->queue,
				(ChiakiReorderQueueDropStrategy)strategy);
}

CHIAKI_SHIM_API int32_t chiaki_shim_reorder_queue_size(void *queue)
{
	chiaki_shim_reorder_queue *self = (chiaki_shim_reorder_queue *)queue;
	return self ? (int32_t)chiaki_reorder_queue_size(&self->queue) : 0;
}

CHIAKI_SHIM_API uint64_t chiaki_shim_reorder_queue_count(void *queue)
{
	chiaki_shim_reorder_queue *self = (chiaki_shim_reorder_queue *)queue;
	return self ? chiaki_reorder_queue_count(&self->queue) : 0;
}

CHIAKI_SHIM_API void chiaki_shim_reorder_queue_push(void *queue, uint64_t seq_num, void *elem_user)
{
	chiaki_shim_reorder_queue *self = (chiaki_shim_reorder_queue *)queue;
	if(self)
		chiaki_reorder_queue_push(&self->queue, seq_num, elem_user);
}

CHIAKI_SHIM_API bool chiaki_shim_reorder_queue_pull(
		void *queue, uint64_t *seq_num, void **elem_user)
{
	chiaki_shim_reorder_queue *self = (chiaki_shim_reorder_queue *)queue;
	if(!self || !seq_num || !elem_user)
		return false;

	return chiaki_reorder_queue_pull(&self->queue, seq_num, elem_user);
}

CHIAKI_SHIM_API bool chiaki_shim_reorder_queue_peek(
		void *queue, uint64_t index, uint64_t *seq_num, void **elem_user)
{
	chiaki_shim_reorder_queue *self = (chiaki_shim_reorder_queue *)queue;
	if(!self || !seq_num || !elem_user)
		return false;

	return chiaki_reorder_queue_peek(&self->queue, index, seq_num, elem_user);
}

CHIAKI_SHIM_API void chiaki_shim_reorder_queue_drop(void *queue, uint64_t index)
{
	chiaki_shim_reorder_queue *self = (chiaki_shim_reorder_queue *)queue;
	if(self)
		chiaki_reorder_queue_drop(&self->queue, index);
}

/** The parsed response plus the copy of the text its keys and values point into. */
typedef struct chiaki_shim_http_t
{
	ChiakiHttpResponse response;
	char *text;
} chiaki_shim_http;

CHIAKI_SHIM_API void *chiaki_shim_http_parse(
		const char *text, int32_t len, int32_t *code_out, int32_t *error_out)
{
	chiaki_shim_http *self;
	ChiakiErrorCode err;

	if(code_out)
		*code_out = 0;
	if(error_out)
		*error_out = (int32_t)CHIAKI_ERR_INVALID_DATA;
	if(!text || len <= 0)
		return NULL;

	self = (chiaki_shim_http *)calloc(1, sizeof(chiaki_shim_http));
	if(!self)
		return NULL;

	self->text = (char *)malloc((size_t)len + 1);
	if(!self->text)
	{
		free(self);
		return NULL;
	}
	memcpy(self->text, text, (size_t)len);
	self->text[len] = '\0';

	err = chiaki_http_response_parse(&self->response, self->text, (size_t)len);
	if(error_out)
		*error_out = (int32_t)err;

	if(err != CHIAKI_ERR_SUCCESS)
	{
		free(self->text);
		free(self);
		return NULL;
	}

	if(code_out)
		*code_out = self->response.code;
	return self;
}

CHIAKI_SHIM_API void chiaki_shim_http_free(void *response)
{
	chiaki_shim_http *self = (chiaki_shim_http *)response;
	if(!self)
		return;

	chiaki_http_response_fini(&self->response);
	free(self->text);
	free(self);
}

static ChiakiHttpHeader *chiaki_shim_http_at(chiaki_shim_http *self, int32_t index)
{
	ChiakiHttpHeader *header;
	if(!self || index < 0)
		return NULL;

	for(header = self->response.headers; header; header = header->next)
	{
		if(index-- == 0)
			return header;
	}
	return NULL;
}

CHIAKI_SHIM_API int32_t chiaki_shim_http_header_count(void *response)
{
	chiaki_shim_http *self = (chiaki_shim_http *)response;
	ChiakiHttpHeader *header;
	int32_t count = 0;

	if(!self)
		return 0;

	for(header = self->response.headers; header; header = header->next)
		count++;
	return count;
}

CHIAKI_SHIM_API const char *chiaki_shim_http_header_key(void *response, int32_t index)
{
	ChiakiHttpHeader *header = chiaki_shim_http_at((chiaki_shim_http *)response, index);
	return header ? header->key : NULL;
}

CHIAKI_SHIM_API const char *chiaki_shim_http_header_value(void *response, int32_t index)
{
	ChiakiHttpHeader *header = chiaki_shim_http_at((chiaki_shim_http *)response, index);
	return header ? header->value : NULL;
}

CHIAKI_SHIM_API void *chiaki_shim_bitstream_create(int32_t codec)
{
	ChiakiBitstream *self = (ChiakiBitstream *)calloc(1, sizeof(ChiakiBitstream));
	if(!self)
		return NULL;

	chiaki_bitstream_init(self, NULL, (ChiakiCodec)codec);
	return self;
}

CHIAKI_SHIM_API void chiaki_shim_bitstream_free(void *bitstream)
{
	free(bitstream);
}

CHIAKI_SHIM_API bool chiaki_shim_bitstream_header(void *bitstream, uint8_t *data, int32_t size)
{
	if(!bitstream || !data || size <= 0)
		return false;

	return chiaki_bitstream_header((ChiakiBitstream *)bitstream, data, (unsigned)size);
}

CHIAKI_SHIM_API bool chiaki_shim_bitstream_slice(
		void *bitstream, uint8_t *data, int32_t size,
		int32_t *slice_type, uint32_t *reference_frame)
{
	ChiakiBitstreamSlice slice;
	bool ok;

	if(slice_type)
		*slice_type = 0;
	if(reference_frame)
		*reference_frame = 0;
	if(!bitstream || !data || size <= 0)
		return false;

	memset(&slice, 0, sizeof(slice));
	ok = chiaki_bitstream_slice((ChiakiBitstream *)bitstream, data, (unsigned)size, &slice);
	if(!ok)
		return false;

	if(slice_type)
		*slice_type = (int32_t)slice.slice_type;
	if(reference_frame)
		*reference_frame = (uint32_t)slice.reference_frame;
	return true;
}

CHIAKI_SHIM_API bool chiaki_shim_bitstream_slice_set_reference_frame(
		void *bitstream, uint8_t *data, int32_t size, uint32_t reference_frame)
{
	if(!bitstream || !data || size <= 0)
		return false;

	return chiaki_bitstream_slice_set_reference_frame((ChiakiBitstream *)bitstream, data,
			(unsigned)size, (unsigned)reference_frame);
}

CHIAKI_SHIM_API void *chiaki_shim_key_state_create(void)
{
	ChiakiKeyState *self = (ChiakiKeyState *)calloc(1, sizeof(ChiakiKeyState));
	if(self)
		chiaki_key_state_init(self);
	return self;
}

CHIAKI_SHIM_API void chiaki_shim_key_state_free(void *state)
{
	free(state);
}

CHIAKI_SHIM_API uint64_t chiaki_shim_key_state_request_pos(void *state, uint32_t low, bool commit)
{
	return state ? chiaki_key_state_request_pos((ChiakiKeyState *)state, low, commit) : 0;
}

CHIAKI_SHIM_API int32_t chiaki_shim_takion_v9_av_packet_parse(
		void *key_state,
		uint8_t *buf,
		int32_t buf_size,
		bool *is_video,
		uint16_t *packet_index,
		uint16_t *frame_index,
		uint16_t *unit_index,
		uint16_t *units_in_frame_total,
		uint16_t *units_in_frame_fec,
		uint8_t *codec,
		uint8_t *adaptive_stream_index,
		uint64_t *key_pos,
		int32_t *data_offset,
		int32_t *data_size)
{
	ChiakiTakionAVPacket packet;
	ChiakiErrorCode err;

	if(!key_state || !buf || buf_size <= 0)
		return (int32_t)CHIAKI_ERR_INVALID_DATA;

	memset(&packet, 0, sizeof(packet));
	err = chiaki_takion_v9_av_packet_parse(&packet, (ChiakiKeyState *)key_state, buf,
			(size_t)buf_size);
	if(err != CHIAKI_ERR_SUCCESS)
		return (int32_t)err;

	if(is_video)
		*is_video = packet.is_video;
	if(packet_index)
		*packet_index = packet.packet_index;
	if(frame_index)
		*frame_index = packet.frame_index;
	if(unit_index)
		*unit_index = packet.unit_index;
	if(units_in_frame_total)
		*units_in_frame_total = packet.units_in_frame_total;
	if(units_in_frame_fec)
		*units_in_frame_fec = packet.units_in_frame_fec;
	if(codec)
		*codec = packet.codec;
	if(adaptive_stream_index)
		*adaptive_stream_index = packet.adaptive_stream_index;
	if(key_pos)
		*key_pos = packet.key_pos;

	// The payload is a pointer INTO the caller's buffer, so it crosses as the offset it sits at.
	// A pointer would be a second lifetime for the managed side to keep track of, and the buffer
	// it points into is one the caller already holds.
	if(data_offset)
		*data_offset = packet.data ? (int32_t)(packet.data - buf) : -1;
	if(data_size)
		*data_size = (int32_t)packet.data_size;

	return (int32_t)CHIAKI_ERR_SUCCESS;
}

CHIAKI_SHIM_API void chiaki_shim_session_free(void *session)
{
	chiaki_shim_session *self = (chiaki_shim_session *)session;
	if(!self)
		return;

	chiaki_session_fini(&self->session);
	free(self);
}

CHIAKI_SHIM_API const char *chiaki_shim_quit_reason_string(int32_t reason)
{
	return chiaki_quit_reason_string((ChiakiQuitReason)reason);
}
