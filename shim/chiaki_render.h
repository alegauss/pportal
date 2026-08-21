// SPDX-License-Identifier: LicenseRef-AGPL-3.0-only-OpenSSL

#ifndef CHIAKI_RENDER_H
#define CHIAKI_RENDER_H

#include <stdbool.h>
#include <stdint.h>

/**
 * PP9: the renderer's own seam, and why it is not the protocol's.
 *
 * PP9 decided that libplacebo runs on D3D11 here rather than on Vulkan, because the shaders live
 * above pl_gpu and the only backend calls without a D3D11 counterpart are the ones handing frames
 * to QtQuick - which the port does not have. That decision was taken from the source and has not
 * been built. This is what builds it.
 *
 * A SECOND DLL rather than more of chiaki-shim, and the reason is what each one drags in.
 * chiaki-shim.dll exists so managed code can reach the protocol; it is loaded by every run of the
 * selftest, on machines with no GPU and in CI. Linking libplacebo into it would make a decoder
 * and a graphics driver a precondition for parsing a discovery reply, and the first symptom would
 * be a test host that cannot start.
 *
 * They also change for different reasons. The protocol seam moves when libchiaki's surface does;
 * this moves when the renderer decision does, which PP9 says is the expensive one to revisit.
 * One ABI covering both would make every renderer experiment an ABI bump for the protocol.
 */

#if defined(_WIN32)
#define CHIAKI_RENDER_API __declspec(dllexport)
#else
#define CHIAKI_RENDER_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

/** Bumped whenever an exported signature here changes meaning. Independent of CHIAKI_SHIM_ABI. */
#define CHIAKI_RENDER_ABI 5

CHIAKI_RENDER_API uint32_t chiaki_render_abi_version(void);

/**
 * Whether this libplacebo was built with the D3D11 backend at all.
 *
 * PL_HAVE_D3D11 is a compile-time property of the library, not of the project, so the answer is
 * about the copy this DLL was linked against. PP9's whole decision rests on it, and a build that
 * lost it should say so here rather than at the first frame.
 */
CHIAKI_RENDER_API bool chiaki_render_has_d3d11(void);

/** And the Vulkan backend, so the two can be compared rather than assumed. */
CHIAKI_RENDER_API bool chiaki_render_has_vulkan(void);

/**
 * Creates a libplacebo D3D11 device, or NULL.
 *
 * This is the call PP9 is a bet on. It creates its own ID3D11Device - the port has no window yet,
 * and a device without one is exactly what a headless check needs.
 *
 * `force_software` selects the WARP adapter, which is what makes this answerable on a machine
 * with no GPU at all: a CI runner still gets a real pl_gpu, and the difference between "the
 * backend does not work" and "this box has no hardware" stops being a guess.
 */
CHIAKI_RENDER_API void *chiaki_render_d3d11_create(bool force_software);

CHIAKI_RENDER_API void chiaki_render_d3d11_destroy(void *d3d11);

/**
 * The GPU's reported limits, as far as the port needs them. False when there is no device.
 *
 * Asked rather than assumed because they are what a renderer is written against: a maximum
 * texture dimension below 3840 would refuse a 4K stream, and finding that out at the first frame
 * of a session is finding it out from a user.
 */
CHIAKI_RENDER_API bool chiaki_render_d3d11_limits(
		void *d3d11, int32_t *out_max_texture_2d, int32_t *out_max_buffer_bytes);

/** The API version libplacebo reports for the device, so a log line can name it. */
CHIAKI_RENDER_API const char *chiaki_render_d3d11_description(void *d3d11);

/**
 * PP131: the hop PP9 accepted a cost for, built.
 *
 * WPF composes through D3D9Ex. D3DImage takes an IDirect3DSurface9 and nothing else, so a D3D11
 * texture reaches the screen only by being SHARED: created with D3D11_RESOURCE_MISC_SHARED, its
 * handle taken from IDXGIResource, and that handle opened again as a D3D9Ex texture. PP9's design
 * named this as an accepted cost and an accepted risk, and naming a risk is not measuring it.
 *
 * The constraints are real and narrow, which is why this is worth building rather than assuming.
 * The share is the OLD kind - D3D9Ex cannot open a keyed-mutex resource - and the format has to be
 * one both APIs agree on, which in practice is B8G8R8A8. Get either wrong and the open fails with
 * E_INVALIDARG, at the first frame, in a renderer that is otherwise complete.
 *
 * Returns NULL on any failure and fills `out_stage` with the step that failed, because "sharing
 * did not work" is the answer this exists to improve on.
 */
typedef enum chiaki_render_share_stage
{
	CHIAKI_RENDER_SHARE_OK = 0,
	CHIAKI_RENDER_SHARE_NO_DEVICE,
	CHIAKI_RENDER_SHARE_TEXTURE,       /**< ID3D11Device::CreateTexture2D */
	CHIAKI_RENDER_SHARE_QUERY,         /**< QueryInterface for IDXGIResource */
	CHIAKI_RENDER_SHARE_HANDLE,        /**< IDXGIResource::GetSharedHandle */
	CHIAKI_RENDER_SHARE_D3D9,          /**< Direct3DCreate9Ex or CreateDeviceEx */
	CHIAKI_RENDER_SHARE_OPEN,          /**< IDirect3DDevice9Ex::CreateTexture on the handle */
	CHIAKI_RENDER_SHARE_SURFACE,       /**< IDirect3DTexture9::GetSurfaceLevel */
} chiaki_render_share_stage;

/**
 * Shares a texture of `width` x `height` from the D3D11 device into a fresh D3D9Ex device, and
 * hands back the IDirect3DSurface9 D3DImage would be given.
 *
 * The surface is what SetBackBuffer takes. Nothing here calls into WPF - what is being answered
 * is whether the pointer can exist at all, which is the half that fails in a driver rather than
 * in a dispatcher.
 */
CHIAKI_RENDER_API void *chiaki_render_share_to_d3d9(
		void *d3d11, int32_t width, int32_t height, int32_t *out_stage);

/** The IDirect3DSurface9 the share produced, or NULL. Owned by the share; do not release. */
CHIAKI_RENDER_API void *chiaki_render_share_surface(void *share);

/** Whether the shared handle was non-null, which is what D3D9Ex is asked to open. */
CHIAKI_RENDER_API bool chiaki_render_share_has_handle(void *share);

CHIAKI_RENDER_API void chiaki_render_share_destroy(void *share);

/**
 * PP132: the last link - libplacebo DRAWING into the texture WPF will show.
 *
 * The device exists (PP131) and the texture reaches D3D9Ex (this file, above). What neither says
 * is that libplacebo can render into that particular texture: pl_d3d11_wrap has to accept it,
 * which it will refuse for an incompatible format or flag, and the result has to land in the
 * bytes the shared handle points at rather than in a copy of them.
 *
 * So this clears the shared texture to a colour through pl_tex_clear and reads it back through
 * pl_tex_download. Reading back is the whole point: a wrap that succeeded and drew somewhere else
 * would pass every check that stopped at the return value.
 *
 * `rgba` is four floats in 0..1. `out_pixel` receives the four bytes at (0,0) as B,G,R,A - the
 * texture's own order, not the argument's, because that difference is exactly the kind of thing
 * a renderer gets wrong once and then cannot see.
 */
CHIAKI_RENDER_API bool chiaki_render_share_clear_and_read(
		void *d3d11, void *share, const float *rgba, uint8_t *out_pixel, int32_t *out_caps);

/**
 * PP133: pl_render_image into the shared texture, which is the call the port makes per frame.
 *
 * The Qt client builds its target from a SWAPCHAIN - pl_frame_from_swapchain - because it presents
 * to a window itself. This design does not present: WPF does, from the shared surface, so there is
 * no swapchain anywhere in it and the target is the texture directly. PP9's remaining scope still
 * named a swapchain; it does not have one.
 *
 * Rendered with a NULL image, which is not a shortcut. qmlmainwindow.cpp makes exactly that call
 * when it has no new frame to show, so it is a real path rather than an invented one - and it
 * exercises the renderer, the target frame and the wrapped texture without needing a decoder.
 */
CHIAKI_RENDER_API bool chiaki_render_share_render(void *d3d11, void *share);

/**
 * PP9's last unanswered link: a DECODED FRAME through pl_render_image.
 *
 * Everything before this rendered nothing. PP133 calls pl_render_image with a NULL image, which
 * exercises the renderer and the target and says nothing at all about the half that carries the
 * picture: an NV12 texture, wrapped as two planes, converted out of limited-range BT.709 and
 * landing in the target as RGB.
 *
 * That half is where a port is wrong silently. Three things have to be right together and none of
 * them returns an error when it is not:
 *
 * 1. THE PLANES. NV12 is one D3D11 texture and two pl_texs. pl_d3d11_wrap picks the plane from the
 *    `fmt` it is given - R8_UNORM is the luma at full size, R8G8_UNORM is the chroma at half - and
 *    a wrap that asked for the wrong one produces a texture, not an error.
 *
 * 2. THE ARRAY SLICE. A d3d11va decoder does not hand over a texture; it hands over a texture
 *    ARRAY and an index into it. So the texture here is an array of two, with a different picture
 *    in each, and the slice that is NOT the default is the one wrapped. Ignoring array_slice reads
 *    every frame of a session out of slice 0 and looks like a decoder that stopped producing.
 *
 * 3. THE RANGE. The console sends limited-range BT.709: 16 is black and 235 is white. A port that
 *    left pl_color_repr zeroed gets PL_COLOR_LEVELS_UNKNOWN, and the picture is washed out rather
 *    than broken - which nobody files a bug about and everybody sees.
 *
 * So this renders one flat NV12 frame and reads the pixel back. The target is NOT the shared
 * texture: sharing rules out CPU access (PP132 measured that), so a texture that can be read is a
 * texture that cannot be shown. The two are asserted separately for that reason - PP133 says the
 * render reaches the shared texture, and this says what the render produces.
 *
 * `out_rgba` receives four bytes at the origin, R,G,B,A - libplacebo's own order for the target
 * format, which is not the B,G,R,A the shared texture reads back in.
 */
typedef enum chiaki_render_frame_stage
{
	CHIAKI_RENDER_FRAME_OK = 0,
	CHIAKI_RENDER_FRAME_NO_DEVICE,
	CHIAKI_RENDER_FRAME_TEXTURE,    /**< CreateTexture2D of the NV12 array */
	CHIAKI_RENDER_FRAME_LUMA,       /**< pl_d3d11_wrap for the R8 view */
	CHIAKI_RENDER_FRAME_CHROMA,     /**< pl_d3d11_wrap for the R8G8 view */
	CHIAKI_RENDER_FRAME_TARGET,     /**< pl_tex_create for the readable target */
	CHIAKI_RENDER_FRAME_RENDERER,   /**< pl_renderer_create */
	CHIAKI_RENDER_FRAME_RENDER,     /**< pl_render_image */
	CHIAKI_RENDER_FRAME_DOWNLOAD,   /**< pl_tex_download */
} chiaki_render_frame_stage;

/**
 * Renders one flat NV12 frame of `luma`/`cb`/`cr` and reads back the pixel it produced.
 *
 * The values are the console's own encoding, not RGB: 16/128/128 is black, 235/128/128 is white.
 */
CHIAKI_RENDER_API bool chiaki_render_frame_nv12(
		void *d3d11, uint8_t luma, uint8_t cb, uint8_t cr, uint8_t *out_rgba, int32_t *out_stage);

#ifdef __cplusplus
}
#endif

#endif // CHIAKI_RENDER_H
