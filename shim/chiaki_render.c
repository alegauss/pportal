// SPDX-License-Identifier: LicenseRef-AGPL-3.0-only-OpenSSL

#include "chiaki_render.h"

#include <libplacebo/config.h>
#include <libplacebo/d3d11.h>
#include <libplacebo/gpu.h>
#include <libplacebo/log.h>

#include <stdio.h>
#include <stdlib.h>

CHIAKI_RENDER_API uint32_t chiaki_render_abi_version(void)
{
	return CHIAKI_RENDER_ABI;
}

CHIAKI_RENDER_API bool chiaki_render_has_d3d11(void)
{
#ifdef PL_HAVE_D3D11
	return true;
#else
	return false;
#endif
}

CHIAKI_RENDER_API bool chiaki_render_has_vulkan(void)
{
#ifdef PL_HAVE_VULKAN
	return true;
#else
	return false;
#endif
}

/**
 * The device and the log it needs, kept together.
 *
 * pl_d3d11_create takes a pl_log and keeps the pointer, so the log has to outlive the device -
 * which is the same lifetime rule the protocol seam's ChiakiLog has, and the same reason: a
 * caller that owned only the device would be one free away from a callback into nothing.
 */
typedef struct chiaki_render_d3d11
{
	pl_log log;
	pl_d3d11 d3d11;
	char description[128];
} chiaki_render_d3d11;

CHIAKI_RENDER_API void *chiaki_render_d3d11_create(bool force_software)
{
#ifdef PL_HAVE_D3D11
	chiaki_render_d3d11 *self = (chiaki_render_d3d11 *)calloc(1, sizeof(chiaki_render_d3d11));
	struct pl_d3d11_params params;

	if(!self)
		return NULL;

	// A silent log rather than none. pl_d3d11_create accepts NULL, but then a device that fails
	// to create says nothing at all - and "returned NULL" is exactly the answer this exists to
	// improve on. The level is set low enough to stay quiet and high enough to exist.
	self->log = pl_log_create(PL_API_VER, pl_log_params(.log_level = PL_LOG_ERR));

	params = pl_d3d11_default_params;
	params.force_software = force_software;
	// WARP is allowed either way: on a machine with no GPU, Windows already presents it as the
	// default adapter, so refusing it would turn "no hardware here" into "the backend is broken".
	params.allow_software = true;

	self->d3d11 = pl_d3d11_create(self->log, &params);
	if(!self->d3d11)
	{
		pl_log_destroy(&self->log);
		free(self);
		return NULL;
	}

	snprintf(self->description, sizeof(self->description), "libplacebo %s, d3d11%s",
			pl_version(), self->d3d11->software ? " (software)" : "");
	return self;
#else
	(void)force_software;
	return NULL;
#endif
}

CHIAKI_RENDER_API void chiaki_render_d3d11_destroy(void *d3d11)
{
#ifdef PL_HAVE_D3D11
	chiaki_render_d3d11 *self = (chiaki_render_d3d11 *)d3d11;
	if(!self)
		return;

	// The device first and the log after it, which is the order the lifetime above requires.
	pl_d3d11_destroy(&self->d3d11);
	pl_log_destroy(&self->log);
	free(self);
#else
	(void)d3d11;
#endif
}

CHIAKI_RENDER_API bool chiaki_render_d3d11_limits(
		void *d3d11, int32_t *out_max_texture_2d, int32_t *out_max_buffer_bytes)
{
#ifdef PL_HAVE_D3D11
	chiaki_render_d3d11 *self = (chiaki_render_d3d11 *)d3d11;
	if(!self || !self->d3d11 || !self->d3d11->gpu)
		return false;

	if(out_max_texture_2d)
		*out_max_texture_2d = (int32_t)self->d3d11->gpu->limits.max_tex_2d_dim;
	if(out_max_buffer_bytes)
		*out_max_buffer_bytes = (int32_t)self->d3d11->gpu->limits.max_buf_size;

	return true;
#else
	(void)d3d11; (void)out_max_texture_2d; (void)out_max_buffer_bytes;
	return false;
#endif
}

CHIAKI_RENDER_API const char *chiaki_render_d3d11_description(void *d3d11)
{
	chiaki_render_d3d11 *self = (chiaki_render_d3d11 *)d3d11;
	return self ? self->description : "";
}
