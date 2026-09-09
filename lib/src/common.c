// SPDX-License-Identifier: LicenseRef-AGPL-3.0-only-OpenSSL

#include <chiaki/common.h>
#include <chiaki/random.h>

#include <stdlib.h>
#include <time.h>

#include <winsock2.h>

CHIAKI_EXPORT const char *chiaki_error_string(ChiakiErrorCode code)
{
	switch(code)
	{
		case CHIAKI_ERR_SUCCESS:
			return "Success";
		case CHIAKI_ERR_PARSE_ADDR:
			return "Failed to parse host address";
		case CHIAKI_ERR_THREAD:
			return "Thread error";
		case CHIAKI_ERR_MEMORY:
			return "Memory error";
		case CHIAKI_ERR_NETWORK:
			return "Network error";
		case CHIAKI_ERR_CONNECTION_REFUSED:
			return "Connection Refused";
		case CHIAKI_ERR_HOST_DOWN:
			return "Host is down";
		case CHIAKI_ERR_HOST_UNREACH:
			return "No route to host";
		case CHIAKI_ERR_DISCONNECTED:
			return "Disconnected";
		case CHIAKI_ERR_INVALID_DATA:
			return "Invalid data";
		case CHIAKI_ERR_BUF_TOO_SMALL:
			return "Buffer too small";
		case CHIAKI_ERR_MUTEX_LOCKED:
			return "Mutex is locked";
		case CHIAKI_ERR_CANCELED:
			return "Canceled";
		case CHIAKI_ERR_TIMEOUT:
			return "Timeout";
		case CHIAKI_ERR_INVALID_RESPONSE:
			return "Invalid Response";
		case CHIAKI_ERR_INVALID_MAC:
			return "Invalid MAC";
		case CHIAKI_ERR_UNINITIALIZED:
			return "Uninitialized";
		case CHIAKI_ERR_FEC_FAILED:
			return "FEC failed";
		default:
			return "Unknown";
	}
}

CHIAKI_EXPORT void *chiaki_aligned_alloc(size_t alignment, size_t size)
{
	return _aligned_malloc(size, alignment);
}

CHIAKI_EXPORT void chiaki_aligned_free(void *ptr)
{
	_aligned_free(ptr);
}

CHIAKI_EXPORT ChiakiErrorCode chiaki_lib_init()
{
	unsigned int seed;
	chiaki_random_bytes_crypt((uint8_t *)&seed, sizeof(seed));
	srand(seed); // doesn't necessarily need to be secure for crypto

	// PP30: gf-complete's field was installed here, and this was the last thing holding it.
	//
	// galois_init_default_field(CHIAKI_FEC_WORDSIZE) built the GF(2^8) log tables jerasure's
	// arithmetic reads, and it was the FOURTH call site - the one no census of the FEC module
	// found, because it is not in the FEC module. PP692 is the reading that separated the two
	// questions: chiaki_fec_decode's callers all left with the frame path, and gf-complete stayed
	// linked anyway, by a function every session calls before it does anything.
	//
	// The tables are managed now. GaloisField builds the same two logs at type initialisation and
	// FecMatrix the same Cauchy matrix, both held entry for entry against what the library said
	// (PP286) before it went - so nothing has to be installed before a frame arrives, and there is
	// no library-init step for the FEC path to have missed.
	//
	// The include of chiaki/fec.h went with it. This file took ONE name from that header -
	// CHIAKI_FEC_WORDSIZE, to size the field - and never called the decode, which is why PP692's
	// includer sweep classified it apart from the other five.
	//
	// WSAStartup below is now the whole of what this function does that a caller could not, and it
	// is why the export stays rather than the shim's wrapper being deleted with the field.

	{
		WORD wsa_version = MAKEWORD(2, 2);
		WSADATA wsa_data;
		int err = WSAStartup(wsa_version, &wsa_data);
		if(err != 0)
			return CHIAKI_ERR_NETWORK;
	}

	return CHIAKI_ERR_SUCCESS;
}

CHIAKI_EXPORT const char *chiaki_codec_name(ChiakiCodec codec)
{
	switch(codec)
	{
		case CHIAKI_CODEC_H264:
			return "H264";
		case CHIAKI_CODEC_H265:
			return "H265";
		case CHIAKI_CODEC_H265_HDR:
			return "H265/HDR";
		default:
			return "unknown";
	}
}
