// SPDX-License-Identifier: LicenseRef-AGPL-3.0-only-OpenSSL

#ifndef CHIAKI_DECODERCHOICE_H
#define CHIAKI_DECODERCHOICE_H

#include "common.h"

#include <stdbool.h>

#ifdef __cplusplus
extern "C" {
#endif

/**
 * Which hardware decoder a session runs on, as a pure function of what the machine
 * offers and what the user asked for.
 *
 * PP51 wrote down a floor - d3d11va decode, a vendor-neutral renderer, an SDR present
 * with no NGX - and could not make any of it fail a build, because the decode third of
 * it was seventy lines and a lambda inside QmlBackend::createSession, reachable only by
 * constructing a window. The branch that floor actually rests on is the one nobody
 * could run: no NVIDIA card, an OpenGL window, d3d11va listed, and the answer had
 * better be d3d11va rather than software.
 *
 * It lives in lib/ rather than beside its caller for the reason it was untestable in
 * the first place: chiaki-unit is a C suite over lib/ and no target links a line of
 * gui/, so a C++ function here would have had to grow a second harness to be asserted
 * at all. Here one harness covers it, and the WPF port inherits the same decision
 * already asserted instead of re-deriving it in C# (PP37).
 *
 * This is not a change to the choice. PP72 is where the ordering is argued and it is
 * paused waiting on real sessions; this pins the branch as it stands so that argument
 * happens against a fixed baseline.
 */

/** The names this returns, and the names the settings surface and the ledger already use. */
#define CHIAKI_DECODER_NAME_VULKAN "vulkan"
#define CHIAKI_DECODER_NAME_CUDA "cuda"
#define CHIAKI_DECODER_NAME_D3D11VA "d3d11va"
/**
 * No hardware decoder. Distinct from CHIAKI_DECODER_NAME_NONE below: this is what the
 * choice fell back to, that is what a user asked for. They differ downstream, and the
 * difference is currently a defect rather than a design - see the note there.
 */
#define CHIAKI_DECODER_NAME_SOFTWARE "software"
/** The literal the settings combo offers for "no hardware decoding", passed through unchanged. */
#define CHIAKI_DECODER_NAME_NONE "none"
/** The literal that asks for this function's judgement rather than naming a decoder. */
#define CHIAKI_DECODER_NAME_AUTO "auto"

/**
 * The renderer the window resolved to. It is an input and not a preference: an OpenGL
 * window cannot hold a vulkan frame, so on that renderer the vulkan decoder is not a
 * worse choice but an impossible one.
 */
typedef enum chiaki_decoder_renderer_t
{
	CHIAKI_DECODER_RENDERER_VULKAN = 0,
	CHIAKI_DECODER_RENDERER_OPENGL = 1,
} ChiakiDecoderRenderer;

/**
 * The four things the decision reads. Three booleans stand in for the decoder list
 * ffmpeg reports because only three names are ever candidates - the settings surface
 * filters av_hwdevice_iterate_types down to exactly these - and a list would invite a
 * fourth to be added here without the branch that would have to choose it.
 */
typedef struct chiaki_decoder_choice_inputs_t
{
	/** Whether ffmpeg lists this decoder AND it survived the runtime probe. */
	bool vulkan_listed;
	bool cuda_listed;
	bool d3d11va_listed;
	/**
	 * Whether the window reports an NVIDIA adapter. cuda is only preferred over d3d11va
	 * on a card that can run it; on every other card d3d11va is the floor, which is the
	 * whole of PP51's non-NVIDIA guarantee.
	 */
	bool nvidia_card;
	ChiakiDecoderRenderer renderer;
	/**
	 * What the user asked for: a decoder name, "auto", "none", or NULL/"" for neither.
	 *
	 * A name that is not currently listed is demoted to "auto" rather than refused, so a
	 * settings file written on a machine with a different card still starts a stream.
	 * NULL and "" are not "auto": they name no decoder and no judgement, and produce
	 * software - which is what the caller did with an empty string before this existed.
	 */
	const char *requested;
} ChiakiDecoderChoiceInputs;

/**
 * The chosen decoder, as one of the CHIAKI_DECODER_NAME_* literals above. Never NULL,
 * and always a static string the caller does not own.
 *
 * "none" is returned unchanged when it was asked for. It is the one output that is not
 * a decoder this function believes in: ffmpeg has no device type by that name, so the
 * session that receives it fails to initialise rather than decoding in software. That
 * is a defect on the far side of this function and it is filed separately; reproducing
 * it faithfully is the point, because a fix that silently rewrote it here would move
 * the bug rather than close it.
 */
CHIAKI_EXPORT const char *chiaki_decoder_choice(const ChiakiDecoderChoiceInputs *inputs);

/**
 * Whether the caller must ask the window for a vulkan device context before trusting
 * the answer - true exactly when chiaki_decoder_choice returned "vulkan".
 *
 * That context is the one input this function cannot take, because it is not a fact
 * about the machine but a call into a live window that can fail on a driver which
 * advertised the decoder. When it comes back empty the caller re-runs the choice with
 * vulkan_listed cleared, and the fallback chain that produces - cuda on an NVIDIA card,
 * else d3d11va, else software - is then the same code path as every other, rather than
 * a fourth copy of it written out by hand.
 */
CHIAKI_EXPORT bool chiaki_decoder_choice_needs_vulkan_context(const char *choice);

#ifdef __cplusplus
}
#endif

#endif // CHIAKI_DECODERCHOICE_H
