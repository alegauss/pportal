// SPDX-License-Identifier: LicenseRef-AGPL-3.0-only-OpenSSL

// PP281: the DirectComposition half of PP163, and the one file in this shim that is C++.
//
// It is C++ because it has to be, measured rather than chosen. mingw-w64's dcomp.h declares its
// interfaces with the old DECLARE_INTERFACE_IID_ macros rather than as MIDL output, and in C those
// expand to a vtable struct whose method declarations reference a typedef the same expansion has
// not made yet - so the header fails on its very first interface:
//
//   dcomp.h:26: error: unknown type name 'IDCompositionSurface';
//               did you mean 'IDCompositionSurfaceVtbl'?
//
// That is not this file's includes being wrong. A four-line translation unit containing only
// windows.h and dcomp.h fails identically, with -DCINTERFACE as well as without it, and the same
// four lines compile clean as C++. So the choice is a C++ translation unit or a hand-written set
// of vtable structs, and hand-written vtables are a silent crash the first time a slot order is
// off by one.
//
// Everything else in chiaki-render stays C. This file exports one function through the same
// extern "C" header the rest of the library uses, so nothing on the managed side can tell.

#include <windows.h>
#include <d3d11.h>
#include <d3d11_1.h>
#include <dxgi1_2.h>
#include <dcomp.h>

#include <new>

#include "chiaki_render.h"

// Windows 8's, and this mingw's winuser.h only declares it above a _WIN32_WINNT the rest of the
// build does not set. Defined here rather than raising the whole target's minimum, which would
// change what every other file in it sees. The window is per-pixel-alpha and has no redirection
// surface, which is what a composed visual wants - and is why it is asked for rather than omitted.
#ifndef WS_EX_NOREDIRECTIONBITMAP
#define WS_EX_NOREDIRECTIONBITMAP 0x00200000L
#endif

// The device handed in is chiaki_render_d3d11, whose first member is a pl_log and whose second is
// the pl_d3d11 carrying the ID3D11Device. Rather than include libplacebo's headers here - which
// would drag its C API through a C++ compiler for one pointer - the C side hands over the
// ID3D11Device directly. chiaki_render_d3d11_device() is that accessor.
extern "C" ID3D11Device *chiaki_render_d3d11_device(void *d3d11);

// PP284: what an attached tree owns, so it can outlive the call that made it. The probes release
// everything before returning, which is right for a question and useless for a window somebody is
// meant to look at.
struct chiaki_render_dcomp_session
{
	IDXGISwapChain1 *swapchain;
	IDCompositionDevice *dcomp;
	IDCompositionTarget *target;
	IDCompositionVisual *visual;

	// PP322: the second layer, null on a one-layer tree. Kept in the SAME struct rather than in one
	// of its own so there is one teardown path: a second detach would be a second place to get the
	// release order wrong, and the order is the part that shows on screen when it is wrong.
	IDCompositionVisual *container;
	IDCompositionVisual *overlay;
	IDCompositionSurface *surface;
};

// The path itself, over a window the CALLER owns. PP283: split out so a WPF window's own HWND can
// be handed in, which is the arrangement the design actually runs in - PP281 and PP282 both built
// the tree on a bare window this file created, and a bare window is not what WPF hands out.
static bool chiaki_render_dcomp_build(
		void *d3d11, int32_t format, bool topmost, HWND hwnd, int32_t *out_stage,
		chiaki_render_dcomp_session *keep)
{
	ID3D11Device *device = nullptr;
	IDXGIDevice *dxgi_device = nullptr;
	IDXGIAdapter *adapter = nullptr;
	IDXGIFactory2 *factory = nullptr;
	IDXGISwapChain1 *swapchain = nullptr;
	IDCompositionDevice *dcomp = nullptr;
	IDCompositionTarget *target = nullptr;
	IDCompositionVisual *visual = nullptr;
	bool ok = false;

	if(out_stage)
		*out_stage = CHIAKI_RENDER_DCOMP_NO_DEVICE;

	device = chiaki_render_d3d11_device(d3d11);
	if(!device)
		return false;

	if(out_stage)
		*out_stage = CHIAKI_RENDER_DCOMP_DXGI_DEVICE;
	if(FAILED(device->QueryInterface(__uuidof(IDXGIDevice), reinterpret_cast<void **>(&dxgi_device))))
		goto done;

	if(out_stage)
		*out_stage = CHIAKI_RENDER_DCOMP_ADAPTER;
	if(FAILED(dxgi_device->GetAdapter(&adapter)))
		goto done;

	if(out_stage)
		*out_stage = CHIAKI_RENDER_DCOMP_FACTORY;
	if(FAILED(adapter->GetParent(__uuidof(IDXGIFactory2), reinterpret_cast<void **>(&factory))))
		goto done;

	{
		// The same description the swapchain probe uses, for the same reasons: a composition
		// swapchain refuses anything but a FLIP model, and premultiplied alpha is what lets
		// something compose above it - which is the entire question being asked here.
		DXGI_SWAP_CHAIN_DESC1 desc = {};
		desc.Width = 1920;
		desc.Height = 1080;
		desc.Format = static_cast<DXGI_FORMAT>(format);
		desc.SampleDesc.Count = 1;
		desc.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
		desc.BufferCount = 2;
		desc.SwapEffect = DXGI_SWAP_EFFECT_FLIP_SEQUENTIAL;
		desc.AlphaMode = DXGI_ALPHA_MODE_PREMULTIPLIED;

		if(out_stage)
			*out_stage = CHIAKI_RENDER_DCOMP_SWAPCHAIN;
		if(FAILED(factory->CreateSwapChainForComposition(device, &desc, nullptr, &swapchain)))
			goto done;

	}

	if(out_stage)
		*out_stage = CHIAKI_RENDER_DCOMP_DEVICE;
	if(FAILED(DCompositionCreateDevice(
			dxgi_device, __uuidof(IDCompositionDevice), reinterpret_cast<void **>(&dcomp))))
		goto done;

	// PP282: the arrangement, and it is the question rather than a parameter. FALSE puts this visual
	// tree BEHIND the window's own content, which is where a video plane has to sit for PP10's XAML
	// overlay to be seen over it. PP281 hard-coded TRUE and so measured the opposite.
	if(out_stage)
		*out_stage = CHIAKI_RENDER_DCOMP_TARGET;
	if(FAILED(dcomp->CreateTargetForHwnd(hwnd, topmost ? TRUE : FALSE, &target)))
		goto done;

	if(out_stage)
		*out_stage = CHIAKI_RENDER_DCOMP_VISUAL;
	if(FAILED(dcomp->CreateVisual(&visual)))
		goto done;

	// The claim itself: a swapchain IS acceptable content for a visual. Everything above is
	// scaffolding that would fail loudly; this is the line PP163 asserted without measuring.
	if(out_stage)
		*out_stage = CHIAKI_RENDER_DCOMP_CONTENT;
	if(FAILED(visual->SetContent(swapchain)))
		goto done;

	if(out_stage)
		*out_stage = CHIAKI_RENDER_DCOMP_ROOT;
	if(FAILED(target->SetRoot(visual)))
		goto done;

	// Commit is where the compositor accepts or rejects the tree. Everything before it can succeed
	// against a tree the compositor will not take, so stopping short would be reporting that
	// interfaces were handed out rather than that the path works.
	if(out_stage)
		*out_stage = CHIAKI_RENDER_DCOMP_COMMIT;
	if(FAILED(dcomp->Commit()))
		goto done;

	if(out_stage)
		*out_stage = CHIAKI_RENDER_DCOMP_OK;
	ok = true;

	// Ownership moves to the caller and the cleanup below is skipped for these four. Nulling them
	// is what does the skipping - a second exit path would be a second place to get it wrong.
	if(keep)
	{
		keep->swapchain = swapchain;
		keep->dcomp = dcomp;
		keep->target = target;
		keep->visual = visual;
		swapchain = nullptr;
		dcomp = nullptr;
		target = nullptr;
		visual = nullptr;
	}

done:
	if(visual)
		visual->Release();
	if(target)
		target->Release();
	if(dcomp)
		dcomp->Release();
	if(swapchain)
		swapchain->Release();
	if(factory)
		factory->Release();
	if(adapter)
		adapter->Release();
	if(dxgi_device)
		dxgi_device->Release();

	return ok;
}

// A REAL top-level window, hidden. CreateTargetForHwnd refuses a message-only window, so there is
// no way to ask any of this without one - which is the difference between these probes and the
// swapchain one, and worth knowing before a build machine runs them.
static HWND chiaki_render_dcomp_window(void)
{
	HINSTANCE instance = GetModuleHandleW(nullptr);
	static const wchar_t *class_name = L"ChiakiNgDCompProbe";
	WNDCLASSEXW cls = {};

	cls.cbSize = sizeof(cls);
	cls.lpfnWndProc = DefWindowProcW;
	cls.hInstance = instance;
	cls.lpszClassName = class_name;
	// A class that is already registered is not an error: this runs more than once per process and
	// RegisterClassExW fails the second time with ERROR_CLASS_ALREADY_EXISTS.
	if(!RegisterClassExW(&cls) && GetLastError() != ERROR_CLASS_ALREADY_EXISTS)
		return nullptr;

	return CreateWindowExW(
			WS_EX_NOREDIRECTIONBITMAP, class_name, L"", WS_POPUP,
			0, 0, 16, 16, nullptr, nullptr, instance, nullptr);
}

extern "C" CHIAKI_RENDER_API bool chiaki_render_dcomp_probe(
		void *d3d11, int32_t format, bool topmost, int32_t *out_stage)
{
	HWND hwnd = nullptr;
	bool ok;

	if(out_stage)
		*out_stage = CHIAKI_RENDER_DCOMP_WINDOW;

	hwnd = chiaki_render_dcomp_window();
	if(!hwnd)
		return false;

	ok = chiaki_render_dcomp_build(d3d11, format, topmost, hwnd, out_stage, nullptr);
	DestroyWindow(hwnd);
	return ok;
}

extern "C" CHIAKI_RENDER_API bool chiaki_render_dcomp_probe_hwnd(
		void *d3d11, int32_t format, bool topmost, void *hwnd, int32_t *out_stage)
{
	// PP283: the same path over a window somebody else owns, which is the only way to ask it of a
	// WPF window. The caller keeps the window - destroying one WPF is still using would answer a
	// different question and crash on the way to it.
	if(out_stage)
		*out_stage = CHIAKI_RENDER_DCOMP_WINDOW;
	if(!hwnd || !IsWindow(static_cast<HWND>(hwnd)))
		return false;

	return chiaki_render_dcomp_build(d3d11, format, topmost, static_cast<HWND>(hwnd), out_stage, nullptr);
}

// The fill, and it is the whole point of the colour arguments: an EMPTY swapchain composes as
// nothing at all, so a window showing the desktop through it would be indistinguishable from a
// visual that never arrived. A solid colour tells those two apart at a glance.
//
// Failing here does not fail the attach. The tree is up and a caller can still see whether WPF
// covers it; a black plane is a worse answer than a red one and it is not no answer.
static void chiaki_render_dcomp_fill(
		ID3D11Device *device, chiaki_render_dcomp_session *self, const float *colour)
{
	ID3D11DeviceContext *context = nullptr;
	ID3D11Texture2D *back = nullptr;
	ID3D11RenderTargetView *rtv = nullptr;

	if(FAILED(self->swapchain->GetBuffer(0, __uuidof(ID3D11Texture2D), reinterpret_cast<void **>(&back))))
		return;

	if(SUCCEEDED(device->CreateRenderTargetView(back, nullptr, &rtv)))
	{
		device->GetImmediateContext(&context);
		if(context)
		{
			context->ClearRenderTargetView(rtv, colour);
			self->swapchain->Present(0, 0);
			context->Release();
		}
		rtv->Release();
	}
	back->Release();
}

extern "C" CHIAKI_RENDER_API void *chiaki_render_dcomp_attach(
		void *d3d11, int32_t format, bool topmost, void *hwnd,
		float r, float g, float b, int32_t *out_stage)
{
	// PP284: the same tree as the probes, kept, with the buffer filled so there is something to
	// look at. PP163's last question is what WPF DRAWS over this visual, and no screen capture of a
	// composed window answers it reliably - so the honest apparatus is a window a person can look
	// at, not a screenshot a test pretends to read.
	chiaki_render_dcomp_session *self = nullptr;
	ID3D11Device *device = nullptr;
	const float colour[4] = { r, g, b, 1.0f };

	if(out_stage)
		*out_stage = CHIAKI_RENDER_DCOMP_WINDOW;
	if(!hwnd || !IsWindow(static_cast<HWND>(hwnd)))
		return nullptr;

	device = chiaki_render_d3d11_device(d3d11);
	if(!device)
	{
		if(out_stage)
			*out_stage = CHIAKI_RENDER_DCOMP_NO_DEVICE;
		return nullptr;
	}

	self = new (std::nothrow) chiaki_render_dcomp_session{};
	if(!self)
		return nullptr;

	if(!chiaki_render_dcomp_build(
			d3d11, format, topmost, static_cast<HWND>(hwnd), out_stage, self))
	{
		delete self;
		return nullptr;
	}

	chiaki_render_dcomp_fill(device, self, colour);
	return self;
}

// PP319: the overlay's own dimensions. Smaller than the video plane on purpose - an overlay the size
// of the whole swapchain would compose correctly whether or not the compositor honours two layers,
// because there would be nothing of the lower one left to be wrong about.
#define CHIAKI_RENDER_LAYERS_OVERLAY_W 320
#define CHIAKI_RENDER_LAYERS_OVERLAY_H 180

// PP322: where the overlay sits, in the window's own coordinates. Not the origin - the reading is a
// comparison, and an overlay in the corner leaves nowhere to look for the plane it is supposed to be
// over. Offset in, so the video plane surrounds it on all four sides.
#define CHIAKI_RENDER_LAYERS_OVERLAY_X 120
#define CHIAKI_RENDER_LAYERS_OVERLAY_Y 90

// PP322: the overlay's fill, in two halves, and the second half is the whole reason it is two.
//
// Premultiplied is not a detail: the surface is DXGI_ALPHA_MODE_PREMULTIPLIED, so every channel is
// already multiplied by the alpha. OPAQUE green answers whether the overlay composes at all. HALF
// alpha - green at 0.5 with alpha 0.5, which is what premultiplied means here - answers the other
// question, and it is one no return value can: an alpha applied twice produces a right half visibly
// nearer the plane's colour than the blend it should be, and nothing errors.
static const float chiaki_render_layers_overlay_opaque[4] = { 0.0f, 1.0f, 0.0f, 1.0f };
static const float chiaki_render_layers_overlay_half[4] = { 0.0f, 0.5f, 0.0f, 0.5f };

// Draws into the overlay surface, which is the step that makes it a surface the compositor has
// anything for. Split out because BeginDraw hands back an ATLAS - the texture is shared with other
// surfaces and the offset says where this one starts - and clearing it whole would be clearing
// somebody else's.
static bool chiaki_render_layers_draw(
		ID3D11Device *device, IDCompositionSurface *surface, int32_t *out_stage)
{
	ID3D11Texture2D *atlas = nullptr;
	ID3D11RenderTargetView *rtv = nullptr;
	ID3D11DeviceContext *context = nullptr;
	ID3D11DeviceContext1 *context1 = nullptr;
	POINT offset = {};
	RECT left = {};
	RECT right = {};
	bool ok = false;

	if(out_stage)
		*out_stage = CHIAKI_RENDER_LAYERS_BEGIN;
	if(FAILED(surface->BeginDraw(
			nullptr, __uuidof(ID3D11Texture2D), reinterpret_cast<void **>(&atlas), &offset)))
		return false;

	if(out_stage)
		*out_stage = CHIAKI_RENDER_LAYERS_RTV;
	if(FAILED(device->CreateRenderTargetView(atlas, nullptr, &rtv)))
		goto done;

	device->GetImmediateContext(&context);
	if(!context)
		goto done;
	if(FAILED(context->QueryInterface(__uuidof(ID3D11DeviceContext1), reinterpret_cast<void **>(&context1))))
		goto done;

	// ClearView with a rect rather than ClearRenderTargetView, for the atlas reason above.
	left.left = offset.x;
	left.top = offset.y;
	left.right = offset.x + (CHIAKI_RENDER_LAYERS_OVERLAY_W / 2);
	left.bottom = offset.y + CHIAKI_RENDER_LAYERS_OVERLAY_H;

	right.left = left.right;
	right.top = left.top;
	right.right = offset.x + CHIAKI_RENDER_LAYERS_OVERLAY_W;
	right.bottom = left.bottom;

	context1->ClearView(rtv, chiaki_render_layers_overlay_opaque, &left, 1);
	context1->ClearView(rtv, chiaki_render_layers_overlay_half, &right, 1);
	ok = true;

done:
	if(context1)
		context1->Release();
	if(context)
		context->Release();
	if(rtv)
		rtv->Release();
	if(atlas)
		atlas->Release();

	// EndDraw whatever happened. A surface left open is one no later BeginDraw on this device will
	// be given, so a failure that skipped it would break every probe after it in the same process.
	if(out_stage && ok)
		*out_stage = CHIAKI_RENDER_LAYERS_END;
	if(FAILED(surface->EndDraw()))
		ok = false;

	return ok;
}

// PP319: the second layer added to a tree that is already built, and PP322: the same code the
// window uses, so what a person looks at is what the assertion measured rather than a second
// arrangement written to be looked at.
//
// The session comes in holding the one-layer tree PP281 measured and goes out holding both, with
// the container as the target's root. On failure it is left exactly as it arrived - the caller's
// teardown releases whatever is set, which is the same for one layer and for two.
static bool chiaki_render_layers_build(
		ID3D11Device *device, chiaki_render_dcomp_session *tree,
		int32_t overlay_format, int32_t *out_stage)
{
	// The root has to be let go before the video visual can be given a parent: a visual belongs to
	// one place in one tree, and a target root is a place.
	tree->target->SetRoot(nullptr);

	if(out_stage)
		*out_stage = CHIAKI_RENDER_LAYERS_SURFACE;
	if(FAILED(tree->dcomp->CreateSurface(
			CHIAKI_RENDER_LAYERS_OVERLAY_W, CHIAKI_RENDER_LAYERS_OVERLAY_H,
			static_cast<DXGI_FORMAT>(overlay_format), DXGI_ALPHA_MODE_PREMULTIPLIED, &tree->surface)))
		return false;

	if(!chiaki_render_layers_draw(device, tree->surface, out_stage))
		return false;

	if(out_stage)
		*out_stage = CHIAKI_RENDER_LAYERS_VISUAL;
	if(FAILED(tree->dcomp->CreateVisual(&tree->overlay)))
		return false;

	// The second claim, and the one nothing before this asked: a SURFACE is content too, and it does
	// not have to be the format the swapchain is.
	if(out_stage)
		*out_stage = CHIAKI_RENDER_LAYERS_CONTENT;
	if(FAILED(tree->overlay->SetContent(tree->surface)))
		return false;

	// PP322: and it is moved off the origin, so the plane it is over surrounds it. A reading is a
	// comparison and an overlay in the corner leaves nothing to compare against.
	tree->overlay->SetOffsetX(static_cast<float>(CHIAKI_RENDER_LAYERS_OVERLAY_X));
	tree->overlay->SetOffsetY(static_cast<float>(CHIAKI_RENDER_LAYERS_OVERLAY_Y));

	if(out_stage)
		*out_stage = CHIAKI_RENDER_LAYERS_ORDER;
	if(FAILED(tree->dcomp->CreateVisual(&tree->container)))
		return false;
	// The order is the measurement. The video goes in first with no reference, and the overlay goes
	// in ABOVE it by naming it - insertAbove TRUE against a reference visual, rather than trusting
	// the order of two AddVisual calls, which the API does not promise.
	if(FAILED(tree->container->AddVisual(tree->visual, FALSE, nullptr)))
		return false;
	if(FAILED(tree->container->AddVisual(tree->overlay, TRUE, tree->visual)))
		return false;

	if(out_stage)
		*out_stage = CHIAKI_RENDER_LAYERS_ROOT;
	if(FAILED(tree->target->SetRoot(tree->container)))
		return false;

	if(out_stage)
		*out_stage = CHIAKI_RENDER_LAYERS_COMMIT;
	if(FAILED(tree->dcomp->Commit()))
		return false;

	if(out_stage)
		*out_stage = CHIAKI_RENDER_LAYERS_OK;
	return true;
}

// Releases whatever a session holds, one layer or two, in the order the compositor needs. Root
// first: releasing a visual while the target still points at it leaves the compositor holding a
// tree whose contents are going away, and the window keeps showing the last frame until something
// else repaints it - which reads as the visual having survived detach.
static void chiaki_render_dcomp_release(chiaki_render_dcomp_session *self)
{
	if(self->target)
		self->target->SetRoot(nullptr);
	if(self->dcomp)
		self->dcomp->Commit();

	if(self->container)
		self->container->Release();
	if(self->overlay)
		self->overlay->Release();
	if(self->surface)
		self->surface->Release();
	if(self->visual)
		self->visual->Release();
	if(self->target)
		self->target->Release();
	if(self->dcomp)
		self->dcomp->Release();
	if(self->swapchain)
		self->swapchain->Release();
}

extern "C" CHIAKI_RENDER_API bool chiaki_render_layers_probe(
		void *d3d11, int32_t format, int32_t overlay_format, int32_t *out_stage)
{
	// PP319: two visuals, ordered, in one tree - the video below and an overlay of a different
	// format above it. PP284 established that nothing WPF draws gets above this tree, so this is the
	// only arrangement left that keeps both an HDR plane and something over it.
	chiaki_render_dcomp_session tree = {};
	ID3D11Device *device = nullptr;
	HWND hwnd = nullptr;
	int32_t build_stage = 0;
	bool ok = false;

	if(out_stage)
		*out_stage = CHIAKI_RENDER_LAYERS_NO_DEVICE;

	device = chiaki_render_d3d11_device(d3d11);
	if(!device)
		return false;

	if(out_stage)
		*out_stage = CHIAKI_RENDER_LAYERS_WINDOW;
	hwnd = chiaki_render_dcomp_window();
	if(!hwnd)
		return false;

	// Everything PP281 already measured, in one step: swapchain, device, target, the video visual,
	// and a Commit of the one-layer tree. Its own stages are not reported here - a failure in them
	// is PP281's question and not this one's, and the probe above answers it in its own vocabulary.
	if(out_stage)
		*out_stage = CHIAKI_RENDER_LAYERS_TREE;
	if(chiaki_render_dcomp_build(d3d11, format, false, hwnd, &build_stage, &tree))
		ok = chiaki_render_layers_build(device, &tree, overlay_format, out_stage);

	chiaki_render_dcomp_release(&tree);
	DestroyWindow(hwnd);
	return ok;
}

extern "C" CHIAKI_RENDER_API void *chiaki_render_layers_attach(
		void *d3d11, int32_t format, int32_t overlay_format, void *hwnd,
		float r, float g, float b, int32_t *out_stage)
{
	// PP322: the two-layer tree, KEPT, both planes filled - which is the apparatus the choice is
	// read from. Deliberately the same builder the probe calls: an apparatus written separately
	// from the thing it demonstrates is one that can agree with the assertion and disagree with the
	// product.
	chiaki_render_dcomp_session *self = nullptr;
	ID3D11Device *device = nullptr;
	int32_t build_stage = 0;
	const float colour[4] = { r, g, b, 1.0f };

	if(out_stage)
		*out_stage = CHIAKI_RENDER_LAYERS_WINDOW;
	if(!hwnd || !IsWindow(static_cast<HWND>(hwnd)))
		return nullptr;

	device = chiaki_render_d3d11_device(d3d11);
	if(!device)
	{
		if(out_stage)
			*out_stage = CHIAKI_RENDER_LAYERS_NO_DEVICE;
		return nullptr;
	}

	self = new (std::nothrow) chiaki_render_dcomp_session{};
	if(!self)
		return nullptr;

	if(out_stage)
		*out_stage = CHIAKI_RENDER_LAYERS_TREE;
	if(!chiaki_render_dcomp_build(d3d11, format, false, static_cast<HWND>(hwnd), &build_stage, self))
	{
		delete self;
		return nullptr;
	}

	// The plane first, then the layer over it. The other order would present the swapchain after the
	// tree was committed, which is a valid thing to do and not what is being demonstrated.
	chiaki_render_dcomp_fill(device, self, colour);

	if(!chiaki_render_layers_build(device, self, overlay_format, out_stage))
	{
		chiaki_render_dcomp_release(self);
		delete self;
		return nullptr;
	}

	return self;
}

extern "C" CHIAKI_RENDER_API void chiaki_render_dcomp_detach(void *session)
{
	chiaki_render_dcomp_session *self = static_cast<chiaki_render_dcomp_session *>(session);
	if(!self)
		return;

	chiaki_render_dcomp_release(self);
	delete self;
}
