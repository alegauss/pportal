// SPDX-License-Identifier: LicenseRef-AGPL-3.0-only-OpenSSL

// COBJMACROS lets C call COM methods as ID3D11Device_CreateTexture2D(...) instead of through a
// C++ vtable it has no syntax for. It has to be defined before ANY header that declares a COM
// interface, and libplacebo/d3d11.h includes d3d11.h and dxgi.h itself - so defining it lower
// down, next to the code that needs it, produced a file where ID3D11Device_CreateTexture2D
// existed and IDXGIResource_GetSharedHandle did not. Which is what happened.
#define COBJMACROS
#include <d3d11.h>
#include <d3d9.h>
#include <dxgi.h>

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

// ---- PP131: the D3D11 -> D3D9Ex share D3DImage requires ------------------------------------
//
// The COM headers themselves are pulled in at the top of this file, ahead of libplacebo's - see
// the note on COBJMACROS there.

typedef struct chiaki_render_share
{
	ID3D11Texture2D *texture;
	IDirect3D9Ex *d3d9;
	IDirect3DDevice9Ex *device9;
	IDirect3DTexture9 *texture9;
	IDirect3DSurface9 *surface9;
	HANDLE shared;
} chiaki_render_share;

static void chiaki_render_share_release(chiaki_render_share *self)
{
	if(!self)
		return;

	if(self->surface9)
		IDirect3DSurface9_Release(self->surface9);
	if(self->texture9)
		IDirect3DTexture9_Release(self->texture9);
	if(self->device9)
		IDirect3DDevice9Ex_Release(self->device9);
	if(self->d3d9)
		IDirect3D9Ex_Release(self->d3d9);
	if(self->texture)
		ID3D11Texture2D_Release(self->texture);

	free(self);
}

CHIAKI_RENDER_API void *chiaki_render_share_to_d3d9(
		void *d3d11, int32_t width, int32_t height, int32_t *out_stage)
{
#ifdef PL_HAVE_D3D11
	chiaki_render_share *self;
	chiaki_render_d3d11 *placebo = (chiaki_render_d3d11 *)d3d11;
	D3D11_TEXTURE2D_DESC desc;
	IDXGIResource *resource = NULL;
	D3DPRESENT_PARAMETERS present;
	HRESULT hr;

	if(out_stage)
		*out_stage = CHIAKI_RENDER_SHARE_NO_DEVICE;
	if(!placebo || !placebo->d3d11 || !placebo->d3d11->device || width <= 0 || height <= 0)
		return NULL;

	self = (chiaki_render_share *)calloc(1, sizeof(chiaki_render_share));
	if(!self)
		return NULL;

	memset(&desc, 0, sizeof(desc));
	desc.Width = (UINT)width;
	desc.Height = (UINT)height;
	desc.MipLevels = 1;
	desc.ArraySize = 1;
	// B8G8R8A8 and not R8G8B8A8: this is the one colour layout D3D9Ex and D3D11 agree on, and
	// D3DFMT_A8R8G8B8 below is the same bytes under the older name.
	desc.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
	desc.SampleDesc.Count = 1;
	desc.Usage = D3D11_USAGE_DEFAULT;
	desc.BindFlags = D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE;
	// The OLD share, deliberately. D3D11_RESOURCE_MISC_SHARED_KEYEDMUTEX is the better one for
	// D3D11-to-D3D11, and D3D9Ex cannot open it at all - which is the constraint PP9 accepted
	// without measuring, and the one that fails at the CreateTexture below rather than here.
	desc.MiscFlags = D3D11_RESOURCE_MISC_SHARED;

	if(out_stage)
		*out_stage = CHIAKI_RENDER_SHARE_TEXTURE;
	hr = ID3D11Device_CreateTexture2D(placebo->d3d11->device, &desc, NULL, &self->texture);
	if(FAILED(hr))
		goto fail;

	if(out_stage)
		*out_stage = CHIAKI_RENDER_SHARE_QUERY;
	hr = ID3D11Texture2D_QueryInterface(self->texture, &IID_IDXGIResource, (void **)&resource);
	if(FAILED(hr) || !resource)
		goto fail;

	if(out_stage)
		*out_stage = CHIAKI_RENDER_SHARE_HANDLE;
	hr = IDXGIResource_GetSharedHandle(resource, &self->shared);
	IDXGIResource_Release(resource);
	if(FAILED(hr) || !self->shared)
		goto fail;

	if(out_stage)
		*out_stage = CHIAKI_RENDER_SHARE_D3D9;
	hr = Direct3DCreate9Ex(D3D_SDK_VERSION, &self->d3d9);
	if(FAILED(hr) || !self->d3d9)
		goto fail;

	memset(&present, 0, sizeof(present));
	present.Windowed = TRUE;
	present.SwapEffect = D3DSWAPEFFECT_DISCARD;
	present.hDeviceWindow = GetDesktopWindow();
	present.PresentationInterval = D3DPRESENT_INTERVAL_IMMEDIATE;
	present.BackBufferFormat = D3DFMT_UNKNOWN;
	present.BackBufferWidth = 1;
	present.BackBufferHeight = 1;

	// The desktop window as the focus window. A real renderer passes its own, and what is being
	// answered here is whether the SHARE works - which does not depend on which window it is.
	hr = IDirect3D9Ex_CreateDeviceEx(self->d3d9, D3DADAPTER_DEFAULT, D3DDEVTYPE_HAL,
			GetDesktopWindow(),
			D3DCREATE_HARDWARE_VERTEXPROCESSING | D3DCREATE_MULTITHREADED | D3DCREATE_FPU_PRESERVE,
			&present, NULL, &self->device9);
	if(FAILED(hr) || !self->device9)
		goto fail;

	if(out_stage)
		*out_stage = CHIAKI_RENDER_SHARE_OPEN;
	// The same handle, opened on the other API. D3DUSAGE_RENDERTARGET and D3DPOOL_DEFAULT are
	// required for a shared surface; anything else is E_INVALIDARG here.
	hr = IDirect3DDevice9Ex_CreateTexture(self->device9, (UINT)width, (UINT)height, 1,
			D3DUSAGE_RENDERTARGET, D3DFMT_A8R8G8B8, D3DPOOL_DEFAULT, &self->texture9, &self->shared);
	if(FAILED(hr) || !self->texture9)
		goto fail;

	if(out_stage)
		*out_stage = CHIAKI_RENDER_SHARE_SURFACE;
	hr = IDirect3DTexture9_GetSurfaceLevel(self->texture9, 0, &self->surface9);
	if(FAILED(hr) || !self->surface9)
		goto fail;

	if(out_stage)
		*out_stage = CHIAKI_RENDER_SHARE_OK;
	return self;

fail:
	chiaki_render_share_release(self);
	return NULL;
#else
	(void)d3d11; (void)width; (void)height;
	if(out_stage)
		*out_stage = CHIAKI_RENDER_SHARE_NO_DEVICE;
	return NULL;
#endif
}

CHIAKI_RENDER_API void *chiaki_render_share_surface(void *share)
{
	chiaki_render_share *self = (chiaki_render_share *)share;
	return self ? self->surface9 : NULL;
}

CHIAKI_RENDER_API bool chiaki_render_share_has_handle(void *share)
{
	chiaki_render_share *self = (chiaki_render_share *)share;
	return self && self->shared != NULL;
}

CHIAKI_RENDER_API void chiaki_render_share_destroy(void *share)
{
	chiaki_render_share_release((chiaki_render_share *)share);
}
