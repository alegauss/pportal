// SPDX-License-Identifier: LicenseRef-AGPL-3.0-only-OpenSSL

#ifndef CHIAKI_STOPPIPE_H
#define CHIAKI_STOPPIPE_H

#include "sock.h"

#include <stdint.h>
#include <stddef.h>
#include <stdbool.h>

#include <winsock2.h>

#ifdef __cplusplus
extern "C" {
#endif

typedef struct chiaki_stop_pipe_t
{
	WSAEVENT event;
} ChiakiStopPipe;

struct sockaddr;

CHIAKI_EXPORT ChiakiErrorCode chiaki_stop_pipe_init(ChiakiStopPipe *stop_pipe);
CHIAKI_EXPORT void chiaki_stop_pipe_fini(ChiakiStopPipe *stop_pipe);
CHIAKI_EXPORT void chiaki_stop_pipe_stop(ChiakiStopPipe *stop_pipe);
CHIAKI_EXPORT ChiakiErrorCode chiaki_stop_pipe_select_single(ChiakiStopPipe *stop_pipe, chiaki_socket_t fd, bool write, uint64_t timeout_ms);
/**
 * Like connect(), but can be canceled by the stop pipe. Only makes sense with a non-blocking socket.
 */
CHIAKI_EXPORT ChiakiErrorCode chiaki_stop_pipe_connect(ChiakiStopPipe *stop_pipe, chiaki_socket_t fd, struct sockaddr *addr, size_t addrlen, uint64_t timeout_ms);
static inline ChiakiErrorCode chiaki_stop_pipe_sleep(ChiakiStopPipe *stop_pipe, uint64_t timeout_ms) { return chiaki_stop_pipe_select_single(stop_pipe, CHIAKI_INVALID_SOCKET, false, timeout_ms); }
CHIAKI_EXPORT ChiakiErrorCode chiaki_stop_pipe_reset(ChiakiStopPipe *stop_pipe);

#ifdef __cplusplus
}
#endif

#endif // CHIAKI_STOPPIPE_H
