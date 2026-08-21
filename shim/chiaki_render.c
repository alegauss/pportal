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
#include <libplacebo/renderer.h>
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
	return chiaki_render_share_to_d3d9_format(
			d3d11, width, height, CHIAKI_RENDER_SHARE_BGRA8, out_stage);
}

CHIAKI_RENDER_API void *chiaki_render_share_to_d3d9_format(
		void *d3d11, int32_t width, int32_t height, int32_t format, int32_t *out_stage)
{
#ifdef PL_HAVE_D3D11
	chiaki_render_share *self;
	chiaki_render_d3d11 *placebo = (chiaki_render_d3d11 *)d3d11;
	DXGI_FORMAT dxgi_format;
	D3DFORMAT d3d9_format;
	D3D11_TEXTURE2D_DESC desc;
	IDXGIResource *resource = NULL;
	D3DPRESENT_PARAMETERS present;
	HRESULT hr;

	if(out_stage)
		*out_stage = CHIAKI_RENDER_SHARE_NO_DEVICE;
	if(!placebo || !placebo->d3d11 || !placebo->d3d11->device || width <= 0 || height <= 0)
		return NULL;

	switch(format)
	{
		case CHIAKI_RENDER_SHARE_RGB10A2:
			// A2B10G10R10 and NOT A2R10G10B10. DXGI puts red in the low bits here, and the D3D9
			// name whose letters run the other way is the one that matches it.
			dxgi_format = DXGI_FORMAT_R10G10B10A2_UNORM;
			d3d9_format = D3DFMT_A2B10G10R10;
			break;
		case CHIAKI_RENDER_SHARE_BGRA8:
			// B8G8R8A8 and not R8G8B8A8: this is the one 8-bit layout D3D9Ex and D3D11 agree on,
			// and D3DFMT_A8R8G8B8 is the same bytes under the older name.
			dxgi_format = DXGI_FORMAT_B8G8R8A8_UNORM;
			d3d9_format = D3DFMT_A8R8G8B8;
			break;
		default:
			return NULL;
	}

	self = (chiaki_render_share *)calloc(1, sizeof(chiaki_render_share));
	if(!self)
		return NULL;

	memset(&desc, 0, sizeof(desc));
	desc.Width = (UINT)width;
	desc.Height = (UINT)height;
	desc.MipLevels = 1;
	desc.ArraySize = 1;
	desc.Format = dxgi_format;
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
			D3DUSAGE_RENDERTARGET, d3d9_format, D3DPOOL_DEFAULT, &self->texture9, &self->shared);
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
	(void)d3d11; (void)width; (void)height; (void)format;
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

CHIAKI_RENDER_API bool chiaki_render_share_clear_and_read(
		void *d3d11, void *share, const float *rgba, uint8_t *out_pixel, int32_t *out_caps)
{
#ifdef PL_HAVE_D3D11
	chiaki_render_d3d11 *placebo = (chiaki_render_d3d11 *)d3d11;
	chiaki_render_share *self = (chiaki_render_share *)share;
	struct pl_d3d11_wrap_params wrap;
	struct pl_tex_transfer_params xfer;
	pl_tex tex;
	bool ok;

	if(out_caps)
		*out_caps = 0;
	if(!placebo || !placebo->d3d11 || !placebo->d3d11->gpu || !self || !self->texture || !rgba
			|| !out_pixel)
		return false;

	memset(&wrap, 0, sizeof(wrap));
	wrap.tex = (ID3D11Resource *)self->texture;

	// The wrap refuses an incompatible format or flag rather than failing later, which is why the
	// texture above is created with BIND_RENDER_TARGET: pl_tex_clear is a blit, and libplacebo
	// will not offer blit_dst on a texture D3D11 would not accept as a render target.
	tex = pl_d3d11_wrap(placebo->d3d11->gpu, &wrap);
	if(!tex)
		return false;

	// Whether libplacebo will let this texture be drawn into and read back at all. Both are
	// properties of the D3D11 flags it was created with, resolved by the wrap - so asking here
	// separates "libplacebo cannot use this texture" from "the draw did not land", which one
	// boolean cannot.
	if(out_caps)
		*out_caps = (int32_t)(4 | (tex->params.blit_dst ? 1 : 0) | (tex->params.host_readable ? 2 : 0)
				| (tex->params.renderable ? 8 : 0) | (tex->params.sampleable ? 16 : 0));

	if(!tex->params.blit_dst)
	{
		pl_tex_destroy(placebo->d3d11->gpu, &tex);
		return false;
	}

	pl_tex_clear(placebo->d3d11->gpu, tex, rgba);

	if(!tex->params.host_readable)
	{
		// The draw happened; only the read-back is unavailable. Reported as a failure with the
		// caps saying which, rather than as a success nobody checked.
		pl_tex_destroy(placebo->d3d11->gpu, &tex);
		return false;
	}

	memset(&xfer, 0, sizeof(xfer));
	xfer.tex = tex;
	xfer.ptr = out_pixel;
	// One pixel, at the origin. The claim is that the clear reached THIS texture's memory, and a
	// full download would spend a frame's bandwidth to say the same thing.
	xfer.rc.x0 = 0;
	xfer.rc.y0 = 0;
	xfer.rc.x1 = 1;
	xfer.rc.y1 = 1;

	ok = pl_tex_download(placebo->d3d11->gpu, &xfer);
	pl_tex_destroy(placebo->d3d11->gpu, &tex);
	return ok;
#else
	(void)d3d11; (void)share; (void)rgba; (void)out_pixel;
	return false;
#endif
}

CHIAKI_RENDER_API bool chiaki_render_share_render(void *d3d11, void *share)
{
#ifdef PL_HAVE_D3D11
	chiaki_render_d3d11 *placebo = (chiaki_render_d3d11 *)d3d11;
	chiaki_render_share *self = (chiaki_render_share *)share;
	struct pl_d3d11_wrap_params wrap;
	struct pl_frame target;
	pl_renderer renderer;
	pl_tex tex;
	bool ok;

	if(!placebo || !placebo->d3d11 || !placebo->d3d11->gpu || !self || !self->texture)
		return false;

	memset(&wrap, 0, sizeof(wrap));
	wrap.tex = (ID3D11Resource *)self->texture;
	tex = pl_d3d11_wrap(placebo->d3d11->gpu, &wrap);
	if(!tex)
		return false;

	renderer = pl_renderer_create(placebo->log, placebo->d3d11->gpu);
	if(!renderer)
	{
		pl_tex_destroy(placebo->d3d11->gpu, &tex);
		return false;
	}

	// The target, built from the texture rather than from a swapchain. There is no swapchain in
	// this design: WPF presents, from the shared surface, so pl_frame_from_swapchain has nothing
	// to be given here.
	memset(&target, 0, sizeof(target));
	target.num_planes = 1;
	target.planes[0].texture = tex;
	target.planes[0].components = 3;
	target.planes[0].component_mapping[0] = PL_CHANNEL_R;
	target.planes[0].component_mapping[1] = PL_CHANNEL_G;
	target.planes[0].component_mapping[2] = PL_CHANNEL_B;
	target.crop.x0 = 0;
	target.crop.y0 = 0;
	target.crop.x1 = (float)tex->params.w;
	target.crop.y1 = (float)tex->params.h;
	target.repr = pl_color_repr_rgb;
	target.color = pl_color_space_srgb;

	// NULL image: the same call qmlmainwindow.cpp makes when it has no new frame, so this is a
	// path the client already takes rather than one invented to be testable.
	ok = pl_render_image(renderer, NULL, &target, &pl_render_default_params);

	pl_renderer_destroy(&renderer);
	pl_tex_destroy(placebo->d3d11->gpu, &tex);
	return ok;
#else
	(void)d3d11; (void)share;
	return false;
#endif
}

// ---- PP9: a decoded frame through pl_render_image ------------------------------------------

#ifdef PL_HAVE_D3D11

// 64x64, and even in both directions because NV12 has no way to be otherwise: the chroma plane is
// half the luma in each axis, so an odd dimension is a plane with half a pixel in it.
#define CHIAKI_RENDER_FRAME_W 64
#define CHIAKI_RENDER_FRAME_H 64

// Two slices, and the SECOND is the one rendered. A d3d11va decoder hands over a texture array
// and an index; wrapping slice 0 by accident works on every frame a decoder happens to put there
// and on no other, so slice 0 is filled with black here and never read.
#define CHIAKI_RENDER_FRAME_SLICES 2
#define CHIAKI_RENDER_FRAME_SLICE 1

// Y for the whole plane, then interleaved Cb,Cr for the half-sized one.
#define CHIAKI_RENDER_FRAME_BYTES \
	(CHIAKI_RENDER_FRAME_W * CHIAKI_RENDER_FRAME_H * 3 / 2)

static void chiaki_render_frame_fill(uint8_t *plane, uint8_t luma, uint8_t cb, uint8_t cr)
{
	int i;
	const int luma_bytes = CHIAKI_RENDER_FRAME_W * CHIAKI_RENDER_FRAME_H;

	memset(plane, luma, (size_t)luma_bytes);
	for(i = luma_bytes; i < CHIAKI_RENDER_FRAME_BYTES; i += 2)
	{
		plane[i] = cb;
		plane[i + 1] = cr;
	}
}

#endif

CHIAKI_RENDER_API bool chiaki_render_frame_nv12(
		void *d3d11, uint8_t luma, uint8_t cb, uint8_t cr, uint8_t *out_rgba, int32_t *out_stage)
{
#ifdef PL_HAVE_D3D11
	chiaki_render_d3d11 *placebo = (chiaki_render_d3d11 *)d3d11;
	uint8_t planes[CHIAKI_RENDER_FRAME_SLICES][CHIAKI_RENDER_FRAME_BYTES];
	D3D11_SUBRESOURCE_DATA initial[CHIAKI_RENDER_FRAME_SLICES];
	D3D11_TEXTURE2D_DESC desc;
	ID3D11Texture2D *texture = NULL;
	struct pl_d3d11_wrap_params wrap;
	struct pl_tex_transfer_params xfer;
	struct pl_frame image, target;
	pl_renderer renderer = NULL;
	pl_tex source[2] = { NULL, NULL };
	pl_tex rendered = NULL;
	pl_fmt fmt;
	bool ok = false;
	int i;

	if(out_stage)
		*out_stage = CHIAKI_RENDER_FRAME_NO_DEVICE;
	if(!placebo || !placebo->d3d11 || !placebo->d3d11->gpu || !out_rgba)
		return false;

	// Slice 0 is black and slice 1 is the picture asked for, so a wrap that ignored array_slice
	// comes back black rather than coming back right.
	chiaki_render_frame_fill(planes[0], 16, 128, 128);
	chiaki_render_frame_fill(planes[CHIAKI_RENDER_FRAME_SLICE], luma, cb, cr);

	for(i = 0; i < CHIAKI_RENDER_FRAME_SLICES; i++)
	{
		memset(&initial[i], 0, sizeof(initial[i]));
		initial[i].pSysMem = planes[i];
		// The pitch is the LUMA row. The chroma plane that follows it has the same pitch - two
		// bytes per chroma pair, half as many pairs - which is why one number covers both.
		initial[i].SysMemPitch = CHIAKI_RENDER_FRAME_W;
	}

	memset(&desc, 0, sizeof(desc));
	desc.Width = CHIAKI_RENDER_FRAME_W;
	desc.Height = CHIAKI_RENDER_FRAME_H;
	desc.MipLevels = 1;
	desc.ArraySize = CHIAKI_RENDER_FRAME_SLICES;
	desc.Format = DXGI_FORMAT_NV12;
	desc.SampleDesc.Count = 1;
	// DEFAULT because pl_d3d11_wrap requires it, and SHADER_RESOURCE because the renderer samples
	// the planes. Not a render target: nothing draws into a decoded frame.
	desc.Usage = D3D11_USAGE_DEFAULT;
	desc.BindFlags = D3D11_BIND_SHADER_RESOURCE;

	if(out_stage)
		*out_stage = CHIAKI_RENDER_FRAME_TEXTURE;
	if(FAILED(ID3D11Device_CreateTexture2D(placebo->d3d11->device, &desc, initial, &texture)))
		return false;

	// The luma plane: the full-sized R8 view of an NV12 texture.
	if(out_stage)
		*out_stage = CHIAKI_RENDER_FRAME_LUMA;
	memset(&wrap, 0, sizeof(wrap));
	wrap.tex = (ID3D11Resource *)texture;
	wrap.array_slice = CHIAKI_RENDER_FRAME_SLICE;
	wrap.fmt = DXGI_FORMAT_R8_UNORM;
	wrap.w = CHIAKI_RENDER_FRAME_W;
	wrap.h = CHIAKI_RENDER_FRAME_H;
	source[0] = pl_d3d11_wrap(placebo->d3d11->gpu, &wrap);
	if(!source[0])
		goto done;

	// And the chroma plane: R8G8, half in each axis, out of the same texture and the same slice.
	if(out_stage)
		*out_stage = CHIAKI_RENDER_FRAME_CHROMA;
	wrap.fmt = DXGI_FORMAT_R8G8_UNORM;
	wrap.w = CHIAKI_RENDER_FRAME_W / 2;
	wrap.h = CHIAKI_RENDER_FRAME_H / 2;
	source[1] = pl_d3d11_wrap(placebo->d3d11->gpu, &wrap);
	if(!source[1])
		goto done;

	// The target is libplacebo's own texture and not the shared one. A shared texture cannot be
	// host_readable - PP132 measured exactly that - so the texture that can be shown and the
	// texture that can be checked are two different textures, and this is the second.
	if(out_stage)
		*out_stage = CHIAKI_RENDER_FRAME_TARGET;
	fmt = pl_find_fmt(placebo->d3d11->gpu, PL_FMT_UNORM, 4, 8, 8,
			PL_FMT_CAP_RENDERABLE | PL_FMT_CAP_HOST_READABLE);
	if(!fmt)
		goto done;

	rendered = pl_tex_create(placebo->d3d11->gpu, pl_tex_params(
			.w = CHIAKI_RENDER_FRAME_W,
			.h = CHIAKI_RENDER_FRAME_H,
			.format = fmt,
			.renderable = true,
			.host_readable = true));
	if(!rendered)
		goto done;

	if(out_stage)
		*out_stage = CHIAKI_RENDER_FRAME_RENDERER;
	renderer = pl_renderer_create(placebo->log, placebo->d3d11->gpu);
	if(!renderer)
		goto done;

	memset(&image, 0, sizeof(image));
	image.num_planes = 2;
	image.planes[0].texture = source[0];
	image.planes[0].components = 1;
	image.planes[0].component_mapping[0] = PL_CHANNEL_Y;
	image.planes[1].texture = source[1];
	image.planes[1].components = 2;
	image.planes[1].component_mapping[0] = PL_CHANNEL_CB;
	image.planes[1].component_mapping[1] = PL_CHANNEL_CR;
	image.crop.x0 = 0;
	image.crop.y0 = 0;
	image.crop.x1 = (float)CHIAKI_RENDER_FRAME_W;
	image.crop.y1 = (float)CHIAKI_RENDER_FRAME_H;
	// The console's encoding, stated rather than left zeroed. LEVELS_UNKNOWN is the washed-out
	// picture nobody reports; BT.709 limited with an 8-bit depth is what arrives.
	image.repr.sys = PL_COLOR_SYSTEM_BT_709;
	image.repr.levels = PL_COLOR_LEVELS_LIMITED;
	image.repr.alpha = PL_ALPHA_UNKNOWN;
	image.repr.bits.sample_depth = 8;
	image.repr.bits.color_depth = 8;
	image.color = pl_color_space_bt709;

	memset(&target, 0, sizeof(target));
	target.num_planes = 1;
	target.planes[0].texture = rendered;
	target.planes[0].components = 3;
	target.planes[0].component_mapping[0] = PL_CHANNEL_R;
	target.planes[0].component_mapping[1] = PL_CHANNEL_G;
	target.planes[0].component_mapping[2] = PL_CHANNEL_B;
	target.crop.x0 = 0;
	target.crop.y0 = 0;
	target.crop.x1 = (float)CHIAKI_RENDER_FRAME_W;
	target.crop.y1 = (float)CHIAKI_RENDER_FRAME_H;
	target.repr = pl_color_repr_rgb;
	target.color = pl_color_space_srgb;

	if(out_stage)
		*out_stage = CHIAKI_RENDER_FRAME_RENDER;
	if(!pl_render_image(renderer, &image, &target, &pl_render_default_params))
		goto done;

	if(out_stage)
		*out_stage = CHIAKI_RENDER_FRAME_DOWNLOAD;
	memset(&xfer, 0, sizeof(xfer));
	xfer.tex = rendered;
	xfer.ptr = out_rgba;
	// One pixel. The frame is flat, so every pixel carries the same claim and a full download
	// would spend a frame's bandwidth restating it.
	xfer.rc.x0 = 0;
	xfer.rc.y0 = 0;
	xfer.rc.x1 = 1;
	xfer.rc.y1 = 1;

	ok = pl_tex_download(placebo->d3d11->gpu, &xfer);
	if(ok && out_stage)
		*out_stage = CHIAKI_RENDER_FRAME_OK;

done:
	if(renderer)
		pl_renderer_destroy(&renderer);
	if(rendered)
		pl_tex_destroy(placebo->d3d11->gpu, &rendered);
	if(source[1])
		pl_tex_destroy(placebo->d3d11->gpu, &source[1]);
	if(source[0])
		pl_tex_destroy(placebo->d3d11->gpu, &source[0]);
	if(texture)
		ID3D11Texture2D_Release(texture);

	return ok;
#else
	(void)d3d11; (void)luma; (void)cb; (void)cr; (void)out_rgba;
	if(out_stage)
		*out_stage = CHIAKI_RENDER_FRAME_NO_DEVICE;
	return false;
#endif
}
