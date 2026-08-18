// SPDX-License-Identifier: LicenseRef-AGPL-3.0-only-OpenSSL

#ifndef CHIAKI_SHIM_H
#define CHIAKI_SHIM_H

#include <stdbool.h>
#include <stdint.h>

/**
 * PP4: the seam, and the side of it that compiles C.
 *
 * lib/ is the part of this project that is not being ported, and it is a static archive whose
 * CHIAKI_EXPORT expands to nothing - so it has no exported symbols at all, and .NET cannot
 * P/Invoke it however the marshalling is written. That is not a difficulty with direct
 * P/Invoke, it is what rules it out: taking it would mean starting the port by editing the
 * half that is not being ported, giving 95 functions and 22 callback typedefs a
 * __declspec(dllexport) and a managed struct layout each.
 *
 * So the boundary is a shim: a DLL that statically links chiaki-lib and exports a flat surface
 * of its own. Three properties follow, and they are why this shape was chosen rather than
 * merely accepted.
 *
 *   - lib/ is untouched. Not one line, not one macro.
 *   - The surface is what the port needs and not what the library happens to have. streamsession
 *     .cpp already adapts libchiaki to a client; this is where a Qt-free copy of that adaptation
 *     goes, on the side of the boundary that already compiles it.
 *   - A ChiakiSession stays an opaque handle. Nothing here hands .NET a struct whose layout it
 *     would then have to track through a libchiaki upgrade.
 *
 * What is here so far is the seam itself and nothing that streams.
 *
 * That does not make the DLL free-standing, and it is worth writing down because the first
 * version of this comment claimed it was. Linking chiaki-lib pulls whole objects, and common.c -
 * which chiaki_error_string lives in - reaches OpenSSL through random.h and winsock through
 * winsock2.h. So chiaki-shim.dll imports libcrypto-3-x64.dll from the first export onwards, and
 * the deployment question a session function would have raised is raised already: the shim is
 * copied into the portable tree that holds those DLLs, and the managed host loads it from there
 * rather than from beside its own assembly. The compiler runtime is still linked in statically,
 * which removes the MinGW DLLs from that list but not the rest.
 */

#if defined(_WIN32)
#define CHIAKI_SHIM_API __declspec(dllexport)
#else
#define CHIAKI_SHIM_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

/**
 * Bumped whenever an exported signature here changes meaning.
 *
 * The managed side checks it on load and refuses a mismatch, because the failure it prevents is
 * the one with no symptom: a DLL left behind by an older build exports every name the new
 * assembly imports, and the arguments land in the wrong places quietly.
 */
#define CHIAKI_SHIM_ABI 1

CHIAKI_SHIM_API uint32_t chiaki_shim_abi_version(void);

/**
 * chiaki_error_string for a ChiakiErrorCode, as a UTF-8 string the caller does not own.
 *
 * Here because it is the smallest thing that proves a real property of the seam: a pointer to a
 * static string crossing into managed memory. An unknown code has an answer rather than a null,
 * which is libchiaki's behaviour and not a decision taken here.
 */
CHIAKI_SHIM_API const char *chiaki_shim_error_string(int32_t error_code);

/**
 * The hardware decoder chiaki_decoder_choice picks, flattened to scalars.
 *
 * The first real function across the seam is this one on purpose. PP77 made the choice a pure
 * function in lib/ so that the branch holding PP51's non-NVIDIA floor could be asserted at all,
 * and noted that whatever answered it should also serve the port rather than let it re-derive
 * the same decision in C#. This is that: the managed host asks the same function the Qt client
 * asks, and the assertion that d3d11va survives an OpenGL window on a machine with no NVIDIA
 * card is one assertion about one implementation rather than two about two.
 *
 * The struct is flattened into arguments rather than marshalled. Six scalars have no layout to
 * disagree about, and the seam is where a layout disagreement costs the most to find.
 *
 * `requested` may be NULL. The returned string is static and is not the caller's to free.
 */
CHIAKI_SHIM_API const char *chiaki_shim_decoder_choice(
		bool vulkan_listed,
		bool cuda_listed,
		bool d3d11va_listed,
		bool nvidia_card,
		int32_t renderer,
		const char *requested);

/** Whether that answer still needs a vulkan device context from the window. */
CHIAKI_SHIM_API bool chiaki_shim_decoder_choice_needs_vulkan_context(const char *choice);

#ifdef __cplusplus
}
#endif

#endif // CHIAKI_SHIM_H
