// SPDX-License-Identifier: LicenseRef-AGPL-3.0-only-OpenSSL

#include <munit.h>

#include <chiaki/decoderchoice.h>

#include <string.h>

/**
 * The machine PP51's floor is written for: no NVIDIA card, so no cuda, and d3d11va is
 * the only hardware decoder there is. Every test below starts from this and changes one
 * thing, so what a test is about is the line that differs from here.
 */
static ChiakiDecoderChoiceInputs plain_machine(void)
{
	ChiakiDecoderChoiceInputs inputs;
	memset(&inputs, 0, sizeof(inputs));
	inputs.d3d11va_listed = true;
	inputs.renderer = CHIAKI_DECODER_RENDERER_VULKAN;
	inputs.requested = CHIAKI_DECODER_NAME_AUTO;
	return inputs;
}

#define assert_choice(inputs, expected) \
	munit_assert_string_equal(chiaki_decoder_choice(&(inputs)), (expected))

/**
 * The assertion PP77 exists for, and the one nothing could run before it: no NVIDIA
 * card, an OpenGL window, d3d11va on offer. The answer must be d3d11va and not
 * software, because that single branch is the whole of the guarantee PP51 wrote down
 * for machines without a vendor card. A refactor that quietly drops the d3d11va arm
 * still starts a stream, still draws a picture, and fails only here.
 */
static MunitResult test_non_nvidia_opengl_floor_is_d3d11va(const MunitParameter params[], void *user)
{
	ChiakiDecoderChoiceInputs inputs = plain_machine();
	inputs.renderer = CHIAKI_DECODER_RENDERER_OPENGL;
	assert_choice(inputs, CHIAKI_DECODER_NAME_D3D11VA);
	return MUNIT_OK;
}

/** The same floor when the user named vulkan by hand rather than leaving it automatic. */
static MunitResult test_non_nvidia_opengl_floor_survives_explicit_vulkan(const MunitParameter params[], void *user)
{
	ChiakiDecoderChoiceInputs inputs = plain_machine();
	inputs.renderer = CHIAKI_DECODER_RENDERER_OPENGL;
	inputs.vulkan_listed = true;
	inputs.requested = CHIAKI_DECODER_NAME_VULKAN;
	assert_choice(inputs, CHIAKI_DECODER_NAME_D3D11VA);
	return MUNIT_OK;
}

/**
 * Software is reached only when there is genuinely nothing else. Asserted next to the
 * two above because they are only meaningful if this one can tell the difference: a
 * function that always answered software would pass no test that never removed d3d11va.
 */
static MunitResult test_software_only_when_nothing_is_offered(const MunitParameter params[], void *user)
{
	ChiakiDecoderChoiceInputs inputs = plain_machine();
	inputs.renderer = CHIAKI_DECODER_RENDERER_OPENGL;
	inputs.d3d11va_listed = false;
	assert_choice(inputs, CHIAKI_DECODER_NAME_SOFTWARE);
	return MUNIT_OK;
}

/** Off OpenGL the vulkan decoder is reachable, and the automatic choice takes it first. */
static MunitResult test_auto_prefers_vulkan_off_opengl(const MunitParameter params[], void *user)
{
	ChiakiDecoderChoiceInputs inputs = plain_machine();
	inputs.vulkan_listed = true;
	assert_choice(inputs, CHIAKI_DECODER_NAME_VULKAN);

	// ...and ahead of cuda, even on the card cuda exists for.
	inputs.nvidia_card = true;
	inputs.cuda_listed = true;
	assert_choice(inputs, CHIAKI_DECODER_NAME_VULKAN);
	return MUNIT_OK;
}

/**
 * cuda outranks d3d11va, but only where both halves of prefer_cuda hold. The two
 * negative cases are the ones that keep an NVIDIA-only path from becoming the default
 * for everyone: a listed cuda on another vendor's card is not a card that can run it,
 * and an NVIDIA card whose ffmpeg build has no cuda decoder is not a decoder.
 */
static MunitResult test_cuda_needs_both_the_card_and_the_decoder(const MunitParameter params[], void *user)
{
	ChiakiDecoderChoiceInputs inputs = plain_machine();
	inputs.renderer = CHIAKI_DECODER_RENDERER_OPENGL;

	inputs.nvidia_card = true;
	inputs.cuda_listed = true;
	assert_choice(inputs, CHIAKI_DECODER_NAME_CUDA);

	inputs.nvidia_card = false;
	assert_choice(inputs, CHIAKI_DECODER_NAME_D3D11VA);

	inputs.nvidia_card = true;
	inputs.cuda_listed = false;
	assert_choice(inputs, CHIAKI_DECODER_NAME_D3D11VA);
	return MUNIT_OK;
}

/**
 * PP72, and the branch its whole plan rests on.
 *
 * PP71 measured cuda last of the three at the rate a console sends, which is evidence against
 * the preference above and not enough to reverse it: one card, one synthetic stream, one
 * machine. What settles it is real sessions running each path on the fallback and the record
 * naming which - and that is only possible if a machine which would automatically take cuda
 * can be told to run d3d11va instead.
 *
 * So the request has to outrank prefer_cuda on exactly the machine the comparison needs: an
 * NVIDIA card, an OpenGL renderer, cuda listed. A refactor that moved the preference ahead of
 * the request would still start every stream, still draw a picture, and would quietly leave
 * PP72 unanswerable for good.
 */
static MunitResult test_explicit_d3d11va_outranks_the_cuda_preference(const MunitParameter params[], void *user)
{
	ChiakiDecoderChoiceInputs inputs = plain_machine();
	inputs.renderer = CHIAKI_DECODER_RENDERER_OPENGL;
	inputs.nvidia_card = true;
	inputs.cuda_listed = true;

	// Left to its own judgement this machine takes cuda - one half of the comparison.
	assert_choice(inputs, CHIAKI_DECODER_NAME_CUDA);

	// Asked for d3d11va it runs d3d11va, which is the other half. Both rows reach the
	// session record naming the renderer beside the decoder, which is what PP72 shipped
	// first and what makes the two comparable at all.
	inputs.requested = CHIAKI_DECODER_NAME_D3D11VA;
	assert_choice(inputs, CHIAKI_DECODER_NAME_D3D11VA);
	return MUNIT_OK;
}

/**
 * PP72's design said the cuda-over-d3d11va preference "governs one case: an OpenGL renderer",
 * and used that to argue the stakes were small. The code disagrees, and this is where the
 * correction is written down rather than left in a deleted design section.
 *
 * without_vulkan is reached three ways and prefer_cuda decides all three. Two of them are
 * Vulkan renderers: an ffmpeg build that lists no vulkan decoder, and the retry after a window
 * handed back no vulkan device context. Only the third is the OpenGL fallback. So a reader who
 * takes "one case" at its word looks for the disputed preference in a third of the places it
 * actually fires, and the two it hides are the ones a driver produces rather than a setting.
 */
static MunitResult test_the_cuda_preference_is_not_only_the_opengl_fallback(const MunitParameter params[], void *user)
{
	ChiakiDecoderChoiceInputs inputs = plain_machine();
	inputs.nvidia_card = true;
	inputs.cuda_listed = true;

	// One: the OpenGL fallback, which is the case the design named.
	inputs.renderer = CHIAKI_DECODER_RENDERER_OPENGL;
	assert_choice(inputs, CHIAKI_DECODER_NAME_CUDA);

	// Two: a Vulkan renderer whose ffmpeg build lists no vulkan decoder. Nothing here is
	// about OpenGL and the answer is still cuda.
	inputs.renderer = CHIAKI_DECODER_RENDERER_VULKAN;
	inputs.vulkan_listed = false;
	assert_choice(inputs, CHIAKI_DECODER_NAME_CUDA);

	// Three: the retry after an empty vulkan device context, on a Vulkan renderer that did
	// list the decoder. test_vulkan_context_retry_reruns_the_same_chain covers this arrival
	// on a machine with no NVIDIA card, where it lands on d3d11va; this is the same retry on
	// the card that makes it land on the preference PP71 argued against.
	inputs.vulkan_listed = true;
	assert_choice(inputs, CHIAKI_DECODER_NAME_VULKAN);
	inputs.vulkan_listed = false;
	assert_choice(inputs, CHIAKI_DECODER_NAME_CUDA);
	return MUNIT_OK;
}

/**
 * A settings file outlives the machine that wrote it. A decoder that is no longer on
 * offer is demoted to a judgement rather than honoured, so the stream still starts -
 * and the judgement lands on whatever this machine does have.
 */
static MunitResult test_unavailable_request_falls_back_to_auto(const MunitParameter params[], void *user)
{
	ChiakiDecoderChoiceInputs inputs = plain_machine();
	inputs.requested = CHIAKI_DECODER_NAME_CUDA;
	assert_choice(inputs, CHIAKI_DECODER_NAME_D3D11VA);

	// And an available one is honoured, which is what makes the demotion above a rule
	// about availability rather than the function ignoring the request outright.
	inputs.nvidia_card = true;
	inputs.cuda_listed = true;
	inputs.renderer = CHIAKI_DECODER_RENDERER_OPENGL;
	assert_choice(inputs, CHIAKI_DECODER_NAME_CUDA);
	return MUNIT_OK;
}

/**
 * The retry after a window handed back no vulkan device context. The caller re-runs the
 * choice with vulkan cleared rather than writing the fallback chain out a third time,
 * so this asserts that the re-run lands where the hand-written copy used to.
 */
static MunitResult test_vulkan_context_retry_reruns_the_same_chain(const MunitParameter params[], void *user)
{
	ChiakiDecoderChoiceInputs inputs = plain_machine();
	inputs.vulkan_listed = true;

	const char *first = chiaki_decoder_choice(&inputs);
	munit_assert_string_equal(first, CHIAKI_DECODER_NAME_VULKAN);
	munit_assert_true(chiaki_decoder_choice_needs_vulkan_context(first));

	inputs.vulkan_listed = false;
	const char *retry = chiaki_decoder_choice(&inputs);
	munit_assert_string_equal(retry, CHIAKI_DECODER_NAME_D3D11VA);
	munit_assert_false(chiaki_decoder_choice_needs_vulkan_context(retry));

	// The explicit request takes the same retry, because the demotion to auto happens
	// before the ordering does.
	inputs.requested = CHIAKI_DECODER_NAME_VULKAN;
	munit_assert_string_equal(chiaki_decoder_choice(&inputs), CHIAKI_DECODER_NAME_D3D11VA);
	return MUNIT_OK;
}

/**
 * PP78, and this is the line PP77 pinned so that the fix would have to arrive here.
 *
 * "none" used to be returned as it was asked for. ffmpeg has no device type of that name, so
 * the session that received it failed to initialise instead of decoding in software, and the
 * user who suspects their hardware decoder and turns it off got a stream that would not start.
 * It now answers software, which is the same thing an empty request already answered and the
 * same thing the automatic choice falls back to when nothing is listed.
 */
static MunitResult test_none_is_software(const MunitParameter params[], void *user)
{
	ChiakiDecoderChoiceInputs inputs = plain_machine();
	inputs.requested = CHIAKI_DECODER_NAME_NONE;
	assert_choice(inputs, CHIAKI_DECODER_NAME_SOFTWARE);

	// A machine that could hardware-decode does not talk it out of the answer: "none" is a
	// user's instruction, not a report about the machine, and every listed decoder stays
	// unlisted-as-far-as-this-request-is-concerned.
	inputs.renderer = CHIAKI_DECODER_RENDERER_OPENGL;
	inputs.nvidia_card = true;
	inputs.cuda_listed = true;
	assert_choice(inputs, CHIAKI_DECODER_NAME_SOFTWARE);

	inputs.renderer = CHIAKI_DECODER_RENDERER_VULKAN;
	inputs.vulkan_listed = true;
	inputs.d3d11va_listed = true;
	assert_choice(inputs, CHIAKI_DECODER_NAME_SOFTWARE);

	// And it is not "auto" arriving at software by accident: the same machine, asked for a
	// judgement instead of for none, answers vulkan.
	inputs.requested = CHIAKI_DECODER_NAME_AUTO;
	assert_choice(inputs, CHIAKI_DECODER_NAME_VULKAN);
	return MUNIT_OK;
}

/**
 * The literal is no longer an output. Nothing downstream has to know that one of the three
 * names this function can answer is a trap, and this is what says so - a caller that maps
 * the result to an ffmpeg device type can do it without a special case.
 */
static MunitResult test_none_is_never_returned(const MunitParameter params[], void *user)
{
	static const char *const requests[] = {
		CHIAKI_DECODER_NAME_NONE, CHIAKI_DECODER_NAME_AUTO, CHIAKI_DECODER_NAME_VULKAN,
		CHIAKI_DECODER_NAME_CUDA, CHIAKI_DECODER_NAME_D3D11VA, CHIAKI_DECODER_NAME_SOFTWARE,
		"", NULL, "quicksync",
	};

	// Every machine in the input space, against every request the settings surface can write.
	for(unsigned int bits = 0; bits < 16; bits++)
	{
		for(size_t i = 0; i < sizeof(requests) / sizeof(*requests); i++)
		{
			ChiakiDecoderChoiceInputs inputs = plain_machine();
			inputs.vulkan_listed = (bits & 1) != 0;
			inputs.cuda_listed = (bits & 2) != 0;
			inputs.d3d11va_listed = (bits & 4) != 0;
			inputs.nvidia_card = (bits & 8) != 0;
			inputs.renderer = (bits & 8) ? CHIAKI_DECODER_RENDERER_OPENGL
				: CHIAKI_DECODER_RENDERER_VULKAN;
			inputs.requested = requests[i];
			munit_assert_string_not_equal(chiaki_decoder_choice(&inputs), CHIAKI_DECODER_NAME_NONE);
		}
	}
	return MUNIT_OK;
}

/**
 * An empty request is not "auto". It names no decoder and asks for no judgement, and
 * before this function existed the caller left it empty all the way down to software.
 * A NULL pointer is the same absence expressed by a C caller.
 */
static MunitResult test_empty_request_is_not_auto(const MunitParameter params[], void *user)
{
	ChiakiDecoderChoiceInputs inputs = plain_machine();
	inputs.requested = "";
	assert_choice(inputs, CHIAKI_DECODER_NAME_SOFTWARE);

	inputs.requested = NULL;
	assert_choice(inputs, CHIAKI_DECODER_NAME_SOFTWARE);
	return MUNIT_OK;
}

MunitTest tests_decoderchoice[] = {
	{
		"/non_nvidia_opengl_floor_is_d3d11va",
		test_non_nvidia_opengl_floor_is_d3d11va,
		NULL, NULL, MUNIT_TEST_OPTION_NONE, NULL
	},
	{
		"/non_nvidia_opengl_floor_survives_explicit_vulkan",
		test_non_nvidia_opengl_floor_survives_explicit_vulkan,
		NULL, NULL, MUNIT_TEST_OPTION_NONE, NULL
	},
	{
		"/software_only_when_nothing_is_offered",
		test_software_only_when_nothing_is_offered,
		NULL, NULL, MUNIT_TEST_OPTION_NONE, NULL
	},
	{
		"/auto_prefers_vulkan_off_opengl",
		test_auto_prefers_vulkan_off_opengl,
		NULL, NULL, MUNIT_TEST_OPTION_NONE, NULL
	},
	{
		"/cuda_needs_both_the_card_and_the_decoder",
		test_cuda_needs_both_the_card_and_the_decoder,
		NULL, NULL, MUNIT_TEST_OPTION_NONE, NULL
	},
	{
		"/explicit_d3d11va_outranks_the_cuda_preference",
		test_explicit_d3d11va_outranks_the_cuda_preference,
		NULL, NULL, MUNIT_TEST_OPTION_NONE, NULL
	},
	{
		"/the_cuda_preference_is_not_only_the_opengl_fallback",
		test_the_cuda_preference_is_not_only_the_opengl_fallback,
		NULL, NULL, MUNIT_TEST_OPTION_NONE, NULL
	},
	{
		"/unavailable_request_falls_back_to_auto",
		test_unavailable_request_falls_back_to_auto,
		NULL, NULL, MUNIT_TEST_OPTION_NONE, NULL
	},
	{
		"/vulkan_context_retry_reruns_the_same_chain",
		test_vulkan_context_retry_reruns_the_same_chain,
		NULL, NULL, MUNIT_TEST_OPTION_NONE, NULL
	},
	{
		"/none_is_software",
		test_none_is_software,
		NULL, NULL, MUNIT_TEST_OPTION_NONE, NULL
	},
	{
		"/none_is_never_returned",
		test_none_is_never_returned,
		NULL, NULL, MUNIT_TEST_OPTION_NONE, NULL
	},
	{
		"/empty_request_is_not_auto",
		test_empty_request_is_not_auto,
		NULL, NULL, MUNIT_TEST_OPTION_NONE, NULL
	},
	{ NULL, NULL, NULL, NULL, MUNIT_TEST_OPTION_NONE, NULL }
};
