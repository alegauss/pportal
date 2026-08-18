// SPDX-License-Identifier: LicenseRef-AGPL-3.0-only-OpenSSL

#include <chiaki/decoderchoice.h>

#include <string.h>

static bool listed(const ChiakiDecoderChoiceInputs *inputs, const char *name)
{
	if(!strcmp(name, CHIAKI_DECODER_NAME_VULKAN))
		return inputs->vulkan_listed;
	if(!strcmp(name, CHIAKI_DECODER_NAME_CUDA))
		return inputs->cuda_listed;
	if(!strcmp(name, CHIAKI_DECODER_NAME_D3D11VA))
		return inputs->d3d11va_listed;
	return false;
}

/**
 * cuda is preferred only where it can actually run. Everywhere else this returning false
 * is what leaves d3d11va as the answer, which is PP51's floor.
 */
static bool prefer_cuda(const ChiakiDecoderChoiceInputs *inputs)
{
	return inputs->nvidia_card && inputs->cuda_listed;
}

/**
 * The chain that runs whenever vulkan is unavailable or unusable, in the order the Qt
 * client has always used it. Three callers reached this by writing it out three times -
 * the automatic choice, the OpenGL fallback, and the retry after an empty vulkan device
 * context - and the third copy was the one no test could reach.
 */
static const char *without_vulkan(const ChiakiDecoderChoiceInputs *inputs)
{
	if(prefer_cuda(inputs))
		return CHIAKI_DECODER_NAME_CUDA;
	if(inputs->d3d11va_listed)
		return CHIAKI_DECODER_NAME_D3D11VA;
	return CHIAKI_DECODER_NAME_SOFTWARE;
}

CHIAKI_EXPORT const char *chiaki_decoder_choice(const ChiakiDecoderChoiceInputs *inputs)
{
	if(!inputs)
		return CHIAKI_DECODER_NAME_SOFTWARE;

	const char *requested = inputs->requested ? inputs->requested : "";

	// A name that is not on offer is demoted to a judgement rather than refused: the
	// settings file outlives the machine it was written on, and a card swap should not
	// be a stream that will not start.
	if(*requested
			&& strcmp(requested, CHIAKI_DECODER_NAME_AUTO)
			&& strcmp(requested, CHIAKI_DECODER_NAME_NONE)
			&& !listed(inputs, requested))
		requested = CHIAKI_DECODER_NAME_AUTO;

	// Passed through rather than translated to software, deliberately - see the header.
	if(!strcmp(requested, CHIAKI_DECODER_NAME_NONE))
		return CHIAKI_DECODER_NAME_NONE;

	if(!*requested)
		return CHIAKI_DECODER_NAME_SOFTWARE;

	const char *choice = requested;
	if(!strcmp(requested, CHIAKI_DECODER_NAME_AUTO))
	{
		// vulkan first, and only off OpenGL, because it is the one decoder whose frame the
		// renderer can take without a copy. On OpenGL it is not on the menu at all, so the
		// automatic choice there is the same chain as every fallback.
		if(inputs->renderer != CHIAKI_DECODER_RENDERER_OPENGL && inputs->vulkan_listed)
			choice = CHIAKI_DECODER_NAME_VULKAN;
		else
			choice = without_vulkan(inputs);
	}

	// An explicitly requested vulkan decoder meets the same wall as the automatic one, and
	// falls the same way. This is where "I picked vulkan and got d3d11va" comes from, and it
	// is correct: an OpenGL window has nowhere to put a vulkan frame.
	if(!strcmp(choice, CHIAKI_DECODER_NAME_VULKAN)
			&& inputs->renderer == CHIAKI_DECODER_RENDERER_OPENGL)
		choice = without_vulkan(inputs);

	return choice;
}

CHIAKI_EXPORT bool chiaki_decoder_choice_needs_vulkan_context(const char *choice)
{
	return choice && !strcmp(choice, CHIAKI_DECODER_NAME_VULKAN);
}
