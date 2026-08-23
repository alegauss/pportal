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
#define CHIAKI_RENDER_ABI 7

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

/**
 * PP11: the same share, in a chosen format - which is how the HDR question gets an answer.
 *
 * PP9 picked D3DImage, and D3DImage is D3D9Ex. HDR needs more than eight bits per channel, and
 * nothing in that decision established that the path can carry ten. The formats are the whole
 * experiment: B8G8R8A8 is the one PP131 measured and 10-bit is the one HDR would need.
 *
 * The D3D9 side of each pairing is not a translation but a LAYOUT MATCH, and the two ten-bit
 * spellings are not interchangeable: DXGI_FORMAT_R10G10B10A2_UNORM puts red in the low bits, which
 * is D3DFMT_A2B10G10R10 and not D3DFMT_A2R10G10B10. Getting that backwards fails at the open with
 * E_INVALIDARG, and looks exactly like the format being unsupported.
 */
typedef enum chiaki_render_share_format
{
	/** The 8-bit pairing PP131 built: DXGI_FORMAT_B8G8R8A8_UNORM and D3DFMT_A8R8G8B8. */
	CHIAKI_RENDER_SHARE_BGRA8 = 0,

	/** The 10-bit one HDR would need: DXGI_FORMAT_R10G10B10A2_UNORM and D3DFMT_A2B10G10R10. */
	CHIAKI_RENDER_SHARE_RGB10A2 = 1,
} chiaki_render_share_format;

CHIAKI_RENDER_API void *chiaki_render_share_to_d3d9_format(
		void *d3d11, int32_t width, int32_t height, int32_t format, int32_t *out_stage);

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
 * PP163: whether the OTHER presentation path can carry HDR, measured rather than argued.
 *
 * PP11's HDR half stopped at a wall: D3DImage refuses any surface wider than eight bits, so the
 * composition path PP9 chose cannot show an HDR picture. The design named two ways out and said a
 * DirectComposition visual is the only one that leaves PP10's overlay standing. Neither was
 * priced.
 *
 * This prices the half that is a fact rather than a judgement. A composition swapchain is what
 * DirectComposition presents, and HDR needs two things of it that are asked separately:
 *
 *   the FORMAT. Ten bits per channel, which is where D3DImage stopped.
 *
 *   the COLOUR SPACE. A ten-bit swapchain is not an HDR one - the buffer is wide and the signal is
 *   still SDR until DXGI accepts G2084 with BT.2020 primaries, which is the ST.2084 transfer HDR10
 *   is. CheckColorSpaceSupport answers it per adapter and per output, and a port that stopped at
 *   the format would have a deeper buffer showing the same picture.
 *
 * Headless: a composition swapchain has no window, which is exactly why it is the one that can be
 * asked this without a display.
 */
typedef enum chiaki_render_swapchain_stage
{
	CHIAKI_RENDER_SWAPCHAIN_OK = 0,
	CHIAKI_RENDER_SWAPCHAIN_NO_DEVICE,
	CHIAKI_RENDER_SWAPCHAIN_DXGI_DEVICE,   /**< QueryInterface for IDXGIDevice */
	CHIAKI_RENDER_SWAPCHAIN_ADAPTER,       /**< IDXGIDevice::GetAdapter */
	CHIAKI_RENDER_SWAPCHAIN_FACTORY,       /**< IDXGIAdapter::GetParent for IDXGIFactory2 */
	CHIAKI_RENDER_SWAPCHAIN_CREATE,        /**< CreateSwapChainForComposition */
	CHIAKI_RENDER_SWAPCHAIN_QUERY3,        /**< QueryInterface for IDXGISwapChain3 */
} chiaki_render_swapchain_stage;

/**
 * Creates a composition swapchain in `format` and reports what it will accept.
 *
 * `out_hdr10` receives whether DXGI says this swapchain supports the HDR10 colour space, and
 * `out_srgb` whether it supports the ordinary SDR one - the second so that a false first answer can
 * be told apart from a check that answers false to everything.
 */
CHIAKI_RENDER_API bool chiaki_render_swapchain_probe(
		void *d3d11, int32_t format, bool *out_hdr10, bool *out_srgb, bool *out_scrgb, int32_t *out_stage);

/**
 * PP281: and whether DirectComposition will actually take that swapchain as a visual's content.
 *
 * PP163 priced half the path and asserted the rest. It measured that a composition swapchain carries
 * ten bits and an ST.2084 signal, and then said - without measuring - that "a DirectComposition
 * visual composes a swapchain with WPF content above it". That sentence is what the whole decision
 * rests on: it is the reason DirectComposition is named the only way out that leaves PP10's overlay
 * standing, and the reason the child-HWND path is rejected.
 *
 * The claim has two halves and only the second needs WPF. This is the first: that DComp accepts the
 * device, binds to a window, takes the swapchain as content and commits. A failure here would end
 * the argument before WPF is even asked, and nothing in the tree would have noticed.
 *
 * A WINDOW IS REQUIRED, unlike the swapchain probe. CreateTargetForHwnd wants a real top-level HWND,
 * so this creates a hidden one, uses it and destroys it. That is the honest cost of the question:
 * the composition swapchain could be priced headless precisely because it has no window, and the
 * visual that presents it cannot be.
 */
typedef enum chiaki_render_dcomp_stage
{
	CHIAKI_RENDER_DCOMP_OK = 0,
	CHIAKI_RENDER_DCOMP_NO_DEVICE,
	CHIAKI_RENDER_DCOMP_DXGI_DEVICE,   /**< QueryInterface for IDXGIDevice */
	CHIAKI_RENDER_DCOMP_ADAPTER,       /**< IDXGIDevice::GetAdapter */
	CHIAKI_RENDER_DCOMP_FACTORY,       /**< IDXGIAdapter::GetParent for IDXGIFactory2 */
	CHIAKI_RENDER_DCOMP_SWAPCHAIN,     /**< CreateSwapChainForComposition */
	CHIAKI_RENDER_DCOMP_WINDOW,        /**< RegisterClass / CreateWindowEx for the hidden target */
	CHIAKI_RENDER_DCOMP_DEVICE,        /**< DCompositionCreateDevice */
	CHIAKI_RENDER_DCOMP_TARGET,        /**< IDCompositionDevice::CreateTargetForHwnd */
	CHIAKI_RENDER_DCOMP_VISUAL,        /**< IDCompositionDevice::CreateVisual */
	CHIAKI_RENDER_DCOMP_CONTENT,       /**< IDCompositionVisual::SetContent with the swapchain */
	CHIAKI_RENDER_DCOMP_ROOT,          /**< IDCompositionTarget::SetRoot */
	CHIAKI_RENDER_DCOMP_COMMIT,        /**< IDCompositionDevice::Commit */
} chiaki_render_dcomp_stage;

/**
 * Builds the whole DirectComposition path over a swapchain in `format` and reports where it stopped.
 *
 * Returns true only when Commit succeeded, which is the point at which the compositor has actually
 * accepted the tree rather than merely handed out interfaces.
 *
 * `topmost` is CreateTargetForHwnd's second argument and it is the whole point of the question, not
 * a detail (PP282). TRUE puts the visual tree ON TOP of the window's own content; FALSE puts it
 * BEHIND. PP163 wants the video behind and PP10's overlay above it, so FALSE is the arrangement the
 * design rests on - and PP281 measured TRUE, which is the one that hides the overlay it was trying
 * to keep. Both are worth building, because a path that only works in the useless direction is a
 * different answer from one that works in neither.
 */
CHIAKI_RENDER_API bool chiaki_render_dcomp_probe(
		void *d3d11, int32_t format, bool topmost, int32_t *out_stage);

/**
 * PP283: the same path over a window the CALLER owns, which is the only way to ask it of WPF.
 *
 * The probe above builds its own window with WS_EX_NOREDIRECTIONBITMAP - per-pixel alpha, no
 * redirection surface, exactly what a composed visual wants. A WPF window is not that window. It
 * owns a redirection bitmap that DWM composes, which is the whole reason PP163's overlay works at
 * all, and whether DirectComposition will bind a target to one is a different question from whether
 * it will bind to a window built to suit it.
 *
 * So this answers the narrow half of what is left: not what WPF DRAWS over the visual, which needs
 * eyes or a screenshot, but whether the compositor accepts the tree on that HWND in the first
 * place. A failure here would end PP163's remaining option without any of the harder work.
 *
 * The window is not destroyed - it belongs to the caller.
 */
CHIAKI_RENDER_API bool chiaki_render_dcomp_probe_hwnd(
		void *d3d11, int32_t format, bool topmost, void *hwnd, int32_t *out_stage);

/**
 * PP284: the same tree, KEPT, with the buffer filled - so a person can look at the answer.
 *
 * PP281 through PP283 measured everything the compositor can be asked and all of it holds. What is
 * left of PP163 is what WPF DRAWS over the visual, and that is a fact about pixels: a composed
 * window does not screenshot reliably, so a test claiming to read one would be reporting its own
 * capture stack. This builds the apparatus instead and leaves the reading to eyes.
 *
 * The colour is why the fill exists. An empty swapchain composes as nothing, so a window showing
 * the desktop through it looks exactly like one where the visual never arrived; a solid colour
 * tells those apart at a glance.
 *
 * The window belongs to the caller and is not destroyed. The returned handle owns the swapchain,
 * the device, the target and the visual, and must be given to chiaki_render_dcomp_detach.
 */
CHIAKI_RENDER_API void *chiaki_render_dcomp_attach(
		void *d3d11, int32_t format, bool topmost, void *hwnd,
		float r, float g, float b, int32_t *out_stage);

/** Tears the tree down and frees the handle. Null is accepted and does nothing. */
CHIAKI_RENDER_API void chiaki_render_dcomp_detach(void *session);

/**
 * PP319: the overlay ABOVE the video, in the compositor's own tree rather than in WPF's.
 *
 * PP284 read the pixel and it was a fourth outcome: the visual covered the whole client area with
 * topmost FALSE and TRUE alike, WPF's content nowhere. The entry called that flag "not honoured".
 * It is honoured; it is about something else. CreateTargetForHwnd's second argument orders the tree
 * against the window's CHILD WINDOWS, and a redirection bitmap is not a child - so a WPF window's
 * own drawing is under the tree in both arrangements, which is exactly what was seen.
 *
 * That closes the arrangement PP163 wanted and opens the only one left that keeps both: if nothing
 * WPF draws can be above the tree, then what has to be above the video is a SECOND VISUAL. This
 * builds that: a container, the ten-bit swapchain below it, and an eight-bit premultiplied
 * IDCompositionSurface above it with something drawn in.
 *
 * Two claims, and the second is the one nothing so far has asked. That a swapchain is content, which
 * PP281 measured. And that a surface of a DIFFERENT format and a different bit depth composes over
 * it in one tree - an SDR overlay above an HDR plane, which is what PP10's screen would become.
 *
 * The draw is not decoration. An IDCompositionSurface that was never drawn into is not a surface the
 * compositor has anything for, and BeginDraw is where a format it will not take is refused - so a
 * probe that created the surface and stopped would report on CreateSurface's argument checking.
 *
 * Its own hidden window, like chiaki_render_dcomp_probe: this asks about the tree's shape and not
 * about whose window it is, and PP283 already answered that a WPF HWND takes a target.
 */
typedef enum chiaki_render_layers_stage
{
	CHIAKI_RENDER_LAYERS_OK = 0,
	CHIAKI_RENDER_LAYERS_NO_DEVICE,
	CHIAKI_RENDER_LAYERS_WINDOW,     /**< the hidden top-level window the target needs */
	CHIAKI_RENDER_LAYERS_TREE,       /**< everything PP281 already measured, up to the video visual */
	CHIAKI_RENDER_LAYERS_SURFACE,    /**< IDCompositionDevice::CreateSurface for the overlay */
	CHIAKI_RENDER_LAYERS_BEGIN,      /**< IDCompositionSurface::BeginDraw */
	CHIAKI_RENDER_LAYERS_RTV,        /**< CreateRenderTargetView on the atlas texture it handed back */
	CHIAKI_RENDER_LAYERS_END,        /**< IDCompositionSurface::EndDraw */
	CHIAKI_RENDER_LAYERS_VISUAL,     /**< CreateVisual for the overlay */
	CHIAKI_RENDER_LAYERS_CONTENT,    /**< SetContent with the surface rather than a swapchain */
	CHIAKI_RENDER_LAYERS_ORDER,      /**< AddVisual twice: the video, then the overlay above it */
	CHIAKI_RENDER_LAYERS_ROOT,       /**< IDCompositionTarget::SetRoot with the container */
	CHIAKI_RENDER_LAYERS_COMMIT,     /**< where the compositor accepts the two-layer tree or does not */
} chiaki_render_layers_stage;

/**
 * Builds the two-layer tree over a swapchain in `format` and reports where it stopped.
 *
 * `overlay_format` is the surface's, asked separately on purpose: the interesting answer is the one
 * where the two differ, and passing the same value for both is how "they compose" gets confused with
 * "they match".
 *
 * Returns true only after Commit.
 */
CHIAKI_RENDER_API bool chiaki_render_layers_probe(
		void *d3d11, int32_t format, int32_t overlay_format, int32_t *out_stage);

/**
 * PP322: the same two-layer tree, KEPT, both planes filled - so the choice can be looked at.
 *
 * PP319 measured that the compositor accepts the tree and chose it on that. That is the same depth
 * PP281 to PP283 reached one layer down, and PP284 then read a pixel none of them had predicted -
 * so the acceptance is not the answer here either, and this is what turns it into one.
 *
 * The video plane is filled with `r`,`g`,`b`. The overlay is drawn in two halves and the second is
 * the reason there are two: one opaque, one at half alpha. Opaque says whether the overlay composes
 * at all; half-alpha says whether a premultiplied surface blends once or twice over the plane below,
 * which no return value anywhere reports and which looks like a slightly wrong colour.
 *
 * Torn down by chiaki_render_dcomp_detach, the same call the one-layer attach uses.
 */
CHIAKI_RENDER_API void *chiaki_render_layers_attach(
		void *d3d11, int32_t format, int32_t overlay_format, void *hwnd,
		float r, float g, float b, int32_t *out_stage);

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
