// SPDX-License-Identifier: LicenseRef-AGPL-3.0-only-OpenSSL

#include <chiaki/messagetap.h>

/**
 * PP323: the whole of it, which is a pointer and a branch.
 *
 * Deliberately this small. The tap exists so that PP297's recording has bytes to hold, and every
 * line of policy that could be written here - what to keep, what to redact, what a channel is
 * called - belongs on the other side of the seam where a sanitiser can name a field. A tap that
 * decided anything would be a second place the recording's format is defined.
 */

static ChiakiMessageTapCb chiaki_message_tap_cb = NULL;
static void *chiaki_message_tap_user = NULL;

CHIAKI_EXPORT void chiaki_message_tap_set(ChiakiMessageTapCb cb, void *user)
{
	// The user pointer first. The sites read `cb` to decide whether to call, so a cb visible with a
	// stale user is the one ordering that hands a handler somebody else's context.
	chiaki_message_tap_user = user;
	chiaki_message_tap_cb = cb;
}

CHIAKI_EXPORT bool chiaki_message_tap_active(void)
{
	return chiaki_message_tap_cb != NULL;
}

CHIAKI_EXPORT void chiaki_message_tap_emit(
		ChiakiMessageTapDirection direction,
		const char *channel,
		uint16_t type,
		const uint8_t *payload,
		size_t payload_size)
{
	ChiakiMessageTapCb cb = chiaki_message_tap_cb;
	if(!cb)
		return;

	// Read once into a local above and used here, so a clear racing an emit calls the old handler
	// rather than a null one. Clearing is still documented as belonging outside a running session -
	// this narrows the window, it does not close it, and pretending otherwise would be worse than
	// the sentence in the header.
	cb((int32_t)direction, channel, type, payload, payload_size, chiaki_message_tap_user);
}
