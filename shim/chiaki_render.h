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
#define CHIAKI_RENDER_ABI 1

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

#ifdef __cplusplus
}
#endif

#endif // CHIAKI_RENDER_H
