// SPDX-License-Identifier: LicenseRef-AGPL-3.0-only-OpenSSL

#ifndef CHIAKI_MESSAGETAP_H
#define CHIAKI_MESSAGETAP_H

#include "common.h"

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/**
 * PP323: the plaintext of a session, where it exists, handed to whoever asked for it.
 *
 * PP297 needs a recorded exchange to port session.c, ctrl.c, streamconnection.c and senkusha.c
 * against, and it was written as though a console were the only thing missing. It is not. What a
 * managed caller sees of a session is the log: the session bytes reach it as a hexdump that PP320
 * redacts whole, and ctrl logs a type and a size and never a payload. Turning recording on would
 * record nothing worth replaying.
 *
 * A TAP RATHER THAN A WIDER LOG, and the difference is redaction. A log line is text, so a
 * sanitiser has to find a field inside a formatted row and PP320 settled that it cannot - it
 * redacts a hexdump row whole because the alternative was leaving the tail of a key on the next
 * one. What crosses here instead is a direction, a channel, a message type and the bytes, so the
 * thing that redacts can name a field.
 *
 * FOUR SITES AND NOT MORE. The plaintext exists at exactly four points, each already marked by a
 * hexdump or a verbose line, which is how they were found rather than chosen:
 *
 *   ctrl_message_send, just before chiaki_rpcrypt_encrypt. After that call the bytes are ciphertext
 *   and a recording of them replays against nothing.
 *
 *   ctrl_message_received, just after chiaki_rpcrypt_decrypt and before the type switch. Before it
 *   they are ciphertext; after it they are gone into a handler.
 *
 *   The session request, as snprintf finishes it.
 *
 *   The session response header, as it arrives.
 *
 * GLOBAL AND NOT PER-SESSION. Three of the four sites are static functions with no handle in reach
 * that a caller ever named, and threading one through them would be a change to four signatures for
 * a diagnostic taken of one session at a time. The cost is stated rather than hidden: two sessions
 * recording at once interleave into one tap, and the channel field is what tells them apart only as
 * far as the channel names differ.
 *
 * OFF BY DEFAULT AND FREE WHEN OFF. The pointer is NULL until somebody sets it, and every site is
 * one predictable branch. ctrl_message_send already formats a verbose line and hexdumps the payload
 * at that exact point, so the tap is cheaper than the logging beside it.
 */

/** Which way a tapped message went. */
typedef enum chiaki_message_tap_direction_t
{
	CHIAKI_MESSAGE_TAP_SENT = 0,
	CHIAKI_MESSAGE_TAP_RECEIVED = 1,
} ChiakiMessageTapDirection;

/**
 * One message, as it crosses.
 *
 * `channel` is a static string naming the conversation - "ctrl" or "session" - and not the socket.
 * `type` is the ctrl message type where there is one and 0 where there is not, which is the session
 * request and its response: those are HTTP and their type is the channel.
 *
 * `payload` is valid only for the duration of the call. It points into libchiaki's own buffer, and
 * for the ctrl sites it points at a buffer that is about to be encrypted in place - so a
 * handler that keeps the pointer reads ciphertext a moment later rather than crashing, which is
 * the failure mode worth naming because it looks like corruption rather than like a bug.
 */
typedef void (*ChiakiMessageTapCb)(
		int32_t direction,
		const char *channel,
		uint16_t type,
		const uint8_t *payload,
		size_t payload_size,
		void *user);

/**
 * Installs the tap, or clears it with NULL. `user` is handed back untouched.
 *
 * Not thread-safe against a session that is already running: the sites read the pointer without a
 * lock, on the ctrl thread and on the session thread. Set it before chiaki_session_start and clear
 * it after the session has joined, which is what a recording does anyway.
 */
CHIAKI_EXPORT void chiaki_message_tap_set(ChiakiMessageTapCb cb, void *user);

/** Whether anything is listening, which is the branch each site takes. */
CHIAKI_EXPORT bool chiaki_message_tap_active(void);

/**
 * Hands one message to the tap, or does nothing.
 *
 * Called by the four sites. Exported rather than static so the seam above can drive it without a
 * console - the same reason chiaki_shim_log_write goes through chiaki_log rather than straight to
 * the callback: one implementation, exercised by the thing that will use it.
 */
CHIAKI_EXPORT void chiaki_message_tap_emit(
		ChiakiMessageTapDirection direction,
		const char *channel,
		uint16_t type,
		const uint8_t *payload,
		size_t payload_size);

/** The channel name for the control conversation. */
#define CHIAKI_MESSAGE_TAP_CHANNEL_CTRL "ctrl"

/** And for the session request and its response, which are HTTP and carry no message type. */
#define CHIAKI_MESSAGE_TAP_CHANNEL_SESSION "session"

/**
 * PP394: and for senkusha's protobuf exchange, which had no channel and so could not be recorded.
 *
 * PP323's four sites are all in ctrl.c and session.c, which are two of the four modules PP23 names
 * as untested. PP391 and PP392 replayed those two; the other two had nothing to be judged by, and
 * PP393 said why. This is the first half of the answer for one of them.
 *
 * THE `type` HERE IS TAKION'S DATA TYPE, not a ctrl message type. Senkusha's protobufs cross as
 * takion data on channel 1, and what distinguishes them on the wire is the data type byte - 1 for
 * the version and big messages, 8 for the MTU and echo commands. A recording that dropped it would
 * hold a stream of protobufs with no way to tell which conversation each belonged to.
 */
#define CHIAKI_MESSAGE_TAP_CHANNEL_SENKUSHA "senkusha"

/**
 * PP395: and for the stream connection's protobufs, which completes PP23's four modules.
 *
 * The same `type` convention as senkusha - takion's data type, not a ctrl message type - because
 * this channel carries three different conversations under one wire format (PP366): the state the
 * machine is in decides what an arriving protobuf means, and the data type is what a recording has
 * to keep so a replay can tell them apart.
 *
 * THE BIG IS TAPPED WHOLE, before the fragmentation. PP375 established that a BIG is cut into as
 * many takion messages as the measured MTU requires, so a recording of fragments would replay only
 * against a run that negotiated the same link. What crosses here is the message; the slicing is the
 * transport's and is not protocol.
 */
#define CHIAKI_MESSAGE_TAP_CHANNEL_STREAM "stream"

/**
 * PP511: and for takion's datagrams, which are not framed messages and are not recorded like one.
 *
 * The four channels above all carry a message somebody framed. This carries what arrives on the
 * socket, and PP510 settled why the two cannot share an artefact: a message's count belongs to the
 * protocol, a datagram's belongs to the network, so a corpus built to be replayed message-for-
 * message has nothing to hold these with. What they are for is PP27's timing run.
 *
 * THE `type` IS THE BASE TYPE - the low nibble of byte zero, which is what PP490's dispatch decides
 * on. It follows the convention senkusha and stream set, of a takion field rather than a ctrl
 * message type, and it costs nothing: a number needs no formatting, where a per-datagram string
 * would allocate on the one path PP44 budgeted at zero.
 *
 * AND THE PAYLOAD IS A TRUNCATED HEAD, not the datagram. PP510 derived its length from the furthest
 * offset chiaki_takion_packet_mac reads, so it answers the dispatch and the MAC layout both and
 * carries no frame of anybody's screen. Truncating at the emit rather than in a consumer is what
 * makes that true of every consumer.
 */
#define CHIAKI_MESSAGE_TAP_CHANNEL_TAKION "takion"

/** PP511: how much of a datagram crosses. Derived in the port, spelled once here. */
#define CHIAKI_MESSAGE_TAP_TAKION_HEAD 18

/**
 * PP397: what a stream or senkusha message's `type` is when the protobuf would not decode.
 *
 * Not a payload type any message has - the highest tkproto knows is 25 - so a rule that names a
 * message by number cannot match it by accident. A message that did not decode is one nothing can
 * classify, and PP326's principle says a value goes because of the field it sits in: with no field
 * identified, the safe answer is that it may not be recorded.
 */
#define CHIAKI_MESSAGE_TAP_TYPE_UNKNOWN 0xffff

#ifdef __cplusplus
}
#endif

#endif // CHIAKI_MESSAGETAP_H
