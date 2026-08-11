// SPDX-License-Identifier: LicenseRef-AGPL-3.0-only-OpenSSL

#ifndef CHIAKI_UTILS_H
#define CHIAKI_UTILS_H

#include <chiaki/sock.h>
#include <chiaki/log.h>
#include <chiaki/stoppipe.h>
#include <chiaki/time.h>
#include <ws2tcpip.h>

#include <stdint.h>
#include <limits.h>

static inline ChiakiErrorCode set_port(struct sockaddr *sa, uint16_t port)
{
	if(sa->sa_family == AF_INET)
		((struct sockaddr_in *)sa)->sin_port = port;
	else if(sa->sa_family == AF_INET6)
		((struct sockaddr_in6 *)sa)->sin6_port = port;
	else
		return CHIAKI_ERR_INVALID_DATA;
	return CHIAKI_ERR_SUCCESS;
}

static inline const char *sockaddr_str(struct sockaddr *addr, char *addr_buf, size_t addr_buf_size)
{
	void *addr_src;
	switch(addr->sa_family)
	{
		case AF_INET:
			addr_src = &((struct sockaddr_in *)addr)->sin_addr;
			break;
		case AF_INET6:
			addr_src = &((struct sockaddr_in6 *)addr)->sin6_addr;
			break;
		default:
			addr_src = NULL;
			break;
	}
	if(addr_src)
		return inet_ntop(addr->sa_family, addr_src, addr_buf, addr_buf_size);
	return NULL;
}

static inline int sendto_broadcast(ChiakiLog *log, chiaki_socket_t s, const void *msg, size_t len, int flags, const struct sockaddr *to, socklen_t tolen)
{
	(void)log;
	return sendto(s, (CHIAKI_SOCKET_BUF_TYPE) msg, len, flags, to, tolen);
}

static inline ChiakiErrorCode chiaki_send_fully(ChiakiStopPipe *stop_pipe, chiaki_socket_t sock, const uint8_t *buf, size_t buf_size, uint64_t timeout_ms)
{
	size_t sent_total = 0;
	uint64_t deadline_ms = timeout_ms == UINT64_MAX ? UINT64_MAX : chiaki_time_now_monotonic_ms() + timeout_ms;
	while(sent_total < buf_size)
	{
		size_t remaining = buf_size - sent_total;
		int chunk_size = remaining > (size_t)INT_MAX ? INT_MAX : (int)remaining;
		int sent = send(sock, (CHIAKI_SOCKET_BUF_TYPE)(buf + sent_total), chunk_size, 0);
		if(sent > 0)
		{
			sent_total += (size_t)sent;
			continue;
		}
		if(sent == 0)
			return CHIAKI_ERR_NETWORK;
		int err = WSAGetLastError();
		if(err == WSAEINTR)
			continue;
		if(err == WSAEWOULDBLOCK)
		{
			if(!stop_pipe)
				return CHIAKI_ERR_NETWORK;
			uint64_t wait_timeout_ms = UINT64_MAX;
			if(deadline_ms != UINT64_MAX)
			{
				uint64_t now_ms = chiaki_time_now_monotonic_ms();
				if(now_ms >= deadline_ms)
					return CHIAKI_ERR_TIMEOUT;
				wait_timeout_ms = deadline_ms - now_ms;
			}
			ChiakiErrorCode wait_err = chiaki_stop_pipe_select_single(stop_pipe, sock, true, wait_timeout_ms);
			if(wait_err != CHIAKI_ERR_SUCCESS)
				return wait_err;
			continue;
		}
		return CHIAKI_ERR_NETWORK;
	}
	return CHIAKI_ERR_SUCCESS;
}

static inline void xor_bytes(uint8_t *dst, uint8_t *src, size_t sz)
{
	while(sz > 0)
	{
		*dst ^= *src;
		dst++;
		src++;
		sz--;
	}
}

static inline int8_t nibble_value(char c)
{
	if(c >= '0' && c <= '9')
		return c - '0';
	if(c >= 'a' && c <= 'f')
		return c - 'a' + 0xa;
	if(c >= 'A' && c <= 'F')
		return c - 'A' + 0xa;
	return -1;
}

static inline ChiakiErrorCode parse_hex(uint8_t *buf, size_t *buf_size, const char *hex, size_t hex_size)
{
	if(hex_size % 2 != 0)
		return CHIAKI_ERR_INVALID_DATA;
	if(hex_size / 2 > *buf_size)
		return CHIAKI_ERR_BUF_TOO_SMALL;

	for(size_t i=0; i<hex_size; i+=2)
	{
		int8_t h = nibble_value(hex[i+0]);
		if(h < 0)
			return CHIAKI_ERR_INVALID_DATA;
		int8_t l = nibble_value(hex[i+1]);
		if(l < 0)
			return CHIAKI_ERR_INVALID_DATA;
		buf[i/2] = (h << 4) | l;
	}

	*buf_size = hex_size / 2;
	return CHIAKI_ERR_SUCCESS;
}

static inline char nibble_char(uint8_t v)
{
	if(v > 0xf)
		return '0';
	if(v < 0xa)
		return '0' + v;
	return 'a' + v;
}

static inline ChiakiErrorCode format_hex(char *hex_buf, size_t hex_buf_size, const uint8_t *buf, size_t buf_size)
{
	if(hex_buf_size < buf_size * 2 + 1)
		return CHIAKI_ERR_BUF_TOO_SMALL;

	for(size_t i=0; i<buf_size; i++)
	{
		uint8_t v = buf[i];
		hex_buf[i*2+0] = nibble_char(v >> 4);
		hex_buf[i*2+1] = nibble_char(v & 0xf);
	}
	hex_buf[buf_size*2] = '\0';

	return CHIAKI_ERR_SUCCESS;
}

#endif // CHIAKI_UTILS_H
