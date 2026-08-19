// SPDX-License-Identifier: LicenseRef-AGPL-3.0-only-OpenSSL

#include "chiaki_shim.h"

#include <chiaki/common.h>
#include <chiaki/decoderchoice.h>
#include <chiaki/log.h>

#include <stdlib.h>

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
