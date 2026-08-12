// SPDX-License-Identifier: LicenseRef-AGPL-3.0-only-OpenSSL

#ifndef CHIAKI_TAKIONRECEIVE_H
#define CHIAKI_TAKIONRECEIVE_H

/**
 * PP59: the seam the receive step is charged through.
 *
 * takion_handle_packet_av is the step between a datagram leaving the socket and an entry
 * sitting in the reorder queue, and it holds the transport's per-packet allocations. It was
 * static and reached only from the socket thread, so nothing outside takion.c could call it
 * and no counter could charge it - which is why PP44's zero is scoped to parse and
 * reassembly, one stage further on, and says nothing about this one.
 *
 * The declaration lives here rather than being repeated in the test because a prototype C
 * cannot check across translation units is a silent way to measure the wrong function: a
 * signature that drifts links cleanly and reads garbage.
 *
 * Everything below is internal to libchiaki. This header is not installed and nothing in
 * gui/ includes it.
 */

#include <chiaki/takion.h>

#include <stddef.h>
#include <stdint.h>

/**
 * Base type of Takion packets. Lower nibble of the first byte in datagrams.
 */
typedef enum takion_packet_type_t {
	TAKION_PACKET_TYPE_CONTROL = 0,
	TAKION_PACKET_TYPE_FEEDBACK_HISTORY = 1,
	TAKION_PACKET_TYPE_VIDEO = 2,
	TAKION_PACKET_TYPE_AUDIO = 3,
	TAKION_PACKET_TYPE_HANDSHAKE = 4,
	TAKION_PACKET_TYPE_CONGESTION = 5,
	TAKION_PACKET_TYPE_FEEDBACK_STATE = 6,
	TAKION_PACKET_TYPE_CLIENT_INFO = 8,
} TakionPacketType;

/**
 * Parse one AV datagram and either dispatch it or queue it for reordering.
 *
 * Ownership of @p buf is taken: it is freed on every path that does not hand it to a queue
 * entry, and freed with the entry when the queue releases it.
 */
void takion_handle_packet_av(ChiakiTakion *takion, uint8_t base_type, uint8_t *buf, size_t buf_size);

/** The size one queued video packet costs beyond its own bytes. Measured by PP59. */
size_t takion_av_packet_entry_size(void);

#endif // CHIAKI_TAKIONRECEIVE_H
