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

extern "C" CHIAKI_RENDER_API bool chiaki_render_dcomp_probe(
		void *d3d11, int32_t format, bool topmost, int32_t *out_stage)
{
	// A REAL top-level window, hidden. CreateTargetForHwnd refuses a message-only window, so there
	// is no way to ask this without one - which is the difference between this probe and the
	// swapchain one, and worth knowing before a build machine runs it.
	HINSTANCE instance = GetModuleHandleW(nullptr);
	static const wchar_t *class_name = L"ChiakiNgDCompProbe";
	WNDCLASSEXW cls = {};
	HWND hwnd = nullptr;
	bool ok;

	if(out_stage)
		*out_stage = CHIAKI_RENDER_DCOMP_WINDOW;

	cls.cbSize = sizeof(cls);
	cls.lpfnWndProc = DefWindowProcW;
	cls.hInstance = instance;
	cls.lpszClassName = class_name;
	// A class that is already registered is not an error: this runs more than once per process and
	// RegisterClassExW fails the second time with ERROR_CLASS_ALREADY_EXISTS.
	if(!RegisterClassExW(&cls) && GetLastError() != ERROR_CLASS_ALREADY_EXISTS)
		return false;

	hwnd = CreateWindowExW(
			WS_EX_NOREDIRECTIONBITMAP, class_name, L"", WS_POPUP,
			0, 0, 16, 16, nullptr, nullptr, instance, nullptr);
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
	ID3D11DeviceContext *context = nullptr;
	ID3D11Texture2D *back = nullptr;
	ID3D11RenderTargetView *rtv = nullptr;
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

	// The fill, and it is the whole point of the colour arguments: an EMPTY swapchain composes as
	// nothing at all, so a window showing the desktop through it would be indistinguishable from a
	// visual that never arrived. A solid colour tells those two apart at a glance.
	//
	// Failing here does not fail the attach. The tree is up and a caller can still see whether WPF
	// covers it; a black plane is a worse answer than a red one and it is not no answer.
	if(SUCCEEDED(self->swapchain->GetBuffer(0, __uuidof(ID3D11Texture2D), reinterpret_cast<void **>(&back))))
	{
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

	return self;
}

extern "C" CHIAKI_RENDER_API void chiaki_render_dcomp_detach(void *session)
{
	chiaki_render_dcomp_session *self = static_cast<chiaki_render_dcomp_session *>(session);
	if(!self)
		return;

	// Root first. Releasing the visual while the target still points at it leaves the compositor
	// holding a tree whose contents are going away, and the window keeps showing the last frame
	// until something else repaints it - which reads as the visual having survived detach.
	if(self->target)
		self->target->SetRoot(nullptr);
	if(self->dcomp)
		self->dcomp->Commit();

	if(self->visual)
		self->visual->Release();
	if(self->target)
		self->target->Release();
	if(self->dcomp)
		self->dcomp->Release();
	if(self->swapchain)
		self->swapchain->Release();

	delete self;
}
