// SPDX-License-Identifier: LicenseRef-AGPL-3.0-only-OpenSSL

#include "chiaki_shim.h"

#include <chiaki/common.h>
#include <chiaki/decoderchoice.h>
#include <chiaki/controller.h>
#include <chiaki/log.h>
#include <chiaki/session.h>

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
