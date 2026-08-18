// SPDX-License-Identifier: LicenseRef-AGPL-3.0-only-OpenSSL

#include "chiaki_shim.h"

#include <chiaki/common.h>
#include <chiaki/decoderchoice.h>

CHIAKI_SHIM_API uint32_t chiaki_shim_abi_version(void)
{
	return CHIAKI_SHIM_ABI;
}

CHIAKI_SHIM_API const char *chiaki_shim_error_string(int32_t error_code)
{
	return chiaki_error_string((ChiakiErrorCode)error_code);
}

CHIAKI_SHIM_API const char *chiaki_shim_decoder_choice(
		bool vulkan_listed,
		bool cuda_listed,
		bool d3d11va_listed,
		bool nvidia_card,
		int32_t renderer,
		const char *requested)
{
	ChiakiDecoderChoiceInputs inputs;
	inputs.vulkan_listed = vulkan_listed;
	inputs.cuda_listed = cuda_listed;
	inputs.d3d11va_listed = d3d11va_listed;
	inputs.nvidia_card = nvidia_card;
	// Anything that is not the OpenGL enumerator is the vulkan renderer, which is what the Qt
	// caller's own boolean does. A managed caller cannot hand over an enum this side has not
	// defined, so it hands over an int and the widening happens here rather than in C#.
	inputs.renderer = renderer == (int32_t)CHIAKI_DECODER_RENDERER_OPENGL
			? CHIAKI_DECODER_RENDERER_OPENGL
			: CHIAKI_DECODER_RENDERER_VULKAN;
	inputs.requested = requested;
	return chiaki_decoder_choice(&inputs);
}

CHIAKI_SHIM_API bool chiaki_shim_decoder_choice_needs_vulkan_context(const char *choice)
{
	return chiaki_decoder_choice_needs_vulkan_context(choice);
}
