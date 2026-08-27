// SPDX-License-Identifier: LicenseRef-AGPL-3.0-only-OpenSSL

#include <chiaki/session.h>
#include <chiaki/ecdh.h>
#include <chiaki/base64.h>

#ifdef CHIAKI_LIB_ENABLE_MBEDTLS
#include "mbedtls/entropy.h"
#include "mbedtls/md.h"
#else
// PP427: openssl/ecdh.h is gone with ECDH_compute_key, and param_build.h and core_names.h arrive
// with the builders EVP_PKEY_fromdata needs. ec.h stays for EVP_EC_gen, which is declared there.
#include <openssl/evp.h>
#include <openssl/ec.h>
#include <openssl/hmac.h>
#include <openssl/bn.h>
#include <openssl/param_build.h>
#include <openssl/core_names.h>
#endif

// PP427: the curve, named once. EC_GROUP_new_by_curve_name took NID_secp256k1; EVP takes the name,
// and it is a parameter of every key rather than an object shared between them.
#define CHIAKI_ECDH_CURVE "secp256k1"

// memset
#include <string.h>

#include <stdio.h>

CHIAKI_EXPORT ChiakiErrorCode chiaki_ecdh_init(ChiakiECDH *ecdh)
{
	memset(ecdh, 0, sizeof(ChiakiECDH));
#ifdef CHIAKI_LIB_ENABLE_MBEDTLS
#define CHECK(err) if((err) != 0) { \
	chiaki_ecdh_fini(ecdh); \
	return CHIAKI_ERR_UNKNOWN; }
	// mbedtls ecdh example:
	// https://github.com/ARMmbed/mbedtls/blob/development/programs/pkey/ecdh_curve25519.c
	const char pers[] = "ecdh";
	mbedtls_entropy_context entropy;
	//init RNG Seed context
	mbedtls_entropy_init(&entropy);
	// init local key
	//mbedtls_ecp_keypair_init(&ecdh->key_local);
	mbedtls_ecdh_init(&ecdh->ctx);
	// init ecdh group
	// keep rng context in ecdh for later reuse
	mbedtls_ctr_drbg_init(&ecdh->drbg);

	// build RNG seed
	CHECK(mbedtls_ctr_drbg_seed(&ecdh->drbg, mbedtls_entropy_func, &entropy,
		(const unsigned char *) pers, sizeof pers));

	// build MBEDTLS_ECP_DP_SECP256K1 group
	CHECK(mbedtls_ecp_group_load(&ecdh->ctx.grp, MBEDTLS_ECP_DP_SECP256K1));
	// build key
	CHECK(mbedtls_ecdh_gen_public(&ecdh->ctx.grp, &ecdh->ctx.d,
		&ecdh->ctx.Q, mbedtls_ctr_drbg_random, &ecdh->drbg));

	// relese entropy ptr
	mbedtls_entropy_free(&entropy);
#undef CHECK

#else
#define CHECK(a) if(!(a)) { chiaki_ecdh_fini(ecdh); return CHIAKI_ERR_UNKNOWN; }
	// PP427: four calls become one. EVP_EC_gen names the curve, builds the key and generates it,
	// which is what EC_GROUP_new_by_curve_name, EC_KEY_new, EC_KEY_set_group and
	// EC_KEY_generate_key did between them.
	CHECK(ecdh->key_local = EVP_EC_gen(CHIAKI_ECDH_CURVE));

#undef CHECK
#endif

	return CHIAKI_ERR_SUCCESS;
}

CHIAKI_EXPORT void chiaki_ecdh_fini(ChiakiECDH *ecdh)
{
#ifdef CHIAKI_LIB_ENABLE_MBEDTLS
	mbedtls_ecdh_free(&ecdh->ctx);
	mbedtls_ctr_drbg_free(&ecdh->drbg);
#else
	// PP427: one free, and it tolerates NULL the way both of the two it replaces did - which is what
	// lets init's CHECK call this on a half-built struct.
	EVP_PKEY_free(ecdh->key_local);
#endif
}

CHIAKI_EXPORT ChiakiErrorCode chiaki_ecdh_set_local_key(ChiakiECDH *ecdh, const uint8_t *private_key, size_t private_key_size, const uint8_t *public_key, size_t public_key_size)
{
#ifdef CHIAKI_LIB_ENABLE_MBEDTLS
	//https://tls.mbed.org/discussions/generic/publickey-binary-data-in-der
	// Load keys from buffers (i.e: config file)
	// TODO test

	// public
	int r = 0;
	r = mbedtls_ecp_point_read_binary(&ecdh->ctx.grp, &ecdh->ctx.Q, public_key, public_key_size);
	if(r != 0)
		return CHIAKI_ERR_UNKNOWN;

	// secret
	r = mbedtls_mpi_read_binary(&ecdh->ctx.d, private_key, private_key_size);
	if(r != 0)
		return CHIAKI_ERR_UNKNOWN;

	// regen key
	r = mbedtls_ecdh_gen_public(&ecdh->ctx.grp, &ecdh->ctx.d, &ecdh->ctx.Q, mbedtls_ctr_drbg_random, &ecdh->drbg);
	if(r != 0)
		return CHIAKI_ERR_UNKNOWN;

	return CHIAKI_ERR_SUCCESS;
#else
	// PP427: EC_KEY_set_private_key and EC_KEY_set_public_key amended a key in place; EVP has no
	// setters, so the key is BUILT from both halves and swapped in. The private half is a BIGNUM as
	// before and the public half stays the octet string it arrives as - EC_POINT_new and
	// EC_POINT_oct2point are gone with the point object they existed to fill.
	ChiakiErrorCode err = CHIAKI_ERR_UNKNOWN;

	OSSL_PARAM_BLD *bld = NULL;
	OSSL_PARAM *params = NULL;
	EVP_PKEY_CTX *ctx = NULL;
	EVP_PKEY *key = NULL;

	BIGNUM *private_key_bn = BN_bin2bn(private_key, (int)private_key_size, NULL);
	if(!private_key_bn)
		return CHIAKI_ERR_UNKNOWN;

	bld = OSSL_PARAM_BLD_new();
	if(!bld)
		goto out;

	if(!OSSL_PARAM_BLD_push_utf8_string(bld, OSSL_PKEY_PARAM_GROUP_NAME, CHIAKI_ECDH_CURVE, 0)
			|| !OSSL_PARAM_BLD_push_BN(bld, OSSL_PKEY_PARAM_PRIV_KEY, private_key_bn)
			|| !OSSL_PARAM_BLD_push_octet_string(bld, OSSL_PKEY_PARAM_PUB_KEY, public_key, public_key_size))
		goto out;

	params = OSSL_PARAM_BLD_to_param(bld);
	if(!params)
		goto out;

	ctx = EVP_PKEY_CTX_new_from_name(NULL, "EC", NULL);
	if(!ctx || EVP_PKEY_fromdata_init(ctx) <= 0)
		goto out;

	if(EVP_PKEY_fromdata(ctx, &key, EVP_PKEY_KEYPAIR, params) <= 0)
		goto out;

	// Only now: the key init generated is replaced wholesale, so a failure above leaves the caller
	// with the generated one rather than with half of theirs.
	EVP_PKEY_free(ecdh->key_local);
	ecdh->key_local = key;
	key = NULL;
	err = CHIAKI_ERR_SUCCESS;

out:
	EVP_PKEY_free(key);
	EVP_PKEY_CTX_free(ctx);
	OSSL_PARAM_free(params);
	OSSL_PARAM_BLD_free(bld);
	BN_free(private_key_bn);
	return err;
#endif
}

CHIAKI_EXPORT ChiakiErrorCode chiaki_ecdh_get_local_pub_key(ChiakiECDH *ecdh, uint8_t *key_out, size_t *key_out_size, const uint8_t *handshake_key, uint8_t *sig_out, size_t *sig_out_size)
{
#ifdef CHIAKI_LIB_ENABLE_MBEDTLS
	mbedtls_md_context_t ctx;
	mbedtls_md_init(&ctx);

#define GOTO_ERROR(err) do { \
	if((err) !=0){ \
		goto error; \
	}} while(0)
	// extract pub key to build dh shared secret
	// this key is sent to the remote server
	GOTO_ERROR(mbedtls_ecp_point_write_binary( &ecdh->ctx.grp, &ecdh->ctx.Q,
		MBEDTLS_ECP_PF_UNCOMPRESSED, key_out_size, key_out, *key_out_size ));

	// https://tls.mbed.org/module-level-design-hashing
	// HMAC
	GOTO_ERROR(mbedtls_md_setup(&ctx, mbedtls_md_info_from_type(MBEDTLS_MD_SHA256) , 1));
	GOTO_ERROR(mbedtls_md_hmac_starts(&ctx, handshake_key, CHIAKI_HANDSHAKE_KEY_SIZE));
	GOTO_ERROR(mbedtls_md_hmac_update(&ctx, key_out, *key_out_size));
	GOTO_ERROR(mbedtls_md_hmac_finish(&ctx, sig_out));
	// SHA256 = 8*32
	*sig_out_size = 32;
#undef GOTO_ERROR
	mbedtls_md_free(&ctx);
	return CHIAKI_ERR_SUCCESS;

error:
	mbedtls_md_free(&ctx);
	return CHIAKI_ERR_UNKNOWN;
#else
	// PP427: EC_KEY_get0_public_key handed back a point that EC_POINT_point2oct then encoded into
	// the caller's buffer. EVP_PKEY_get1_encoded_public_key does both and allocates, so the copy and
	// the room check are this function's now.
	//
	// UNCOMPRESSED EITHER WAY. point2oct was told POINT_CONVERSION_UNCOMPRESSED explicitly; the EVP
	// call uses the key's point-format parameter, which is uncompressed unless something sets it
	// otherwise, and nothing here does. The recorded vector proves the bytes rather than this
	// comment: test_ecdh's local_public_key is asserted byte for byte.
	unsigned char *pub = NULL;
	size_t pub_size = EVP_PKEY_get1_encoded_public_key(ecdh->key_local, &pub);
	if(!pub_size)
		return CHIAKI_ERR_UNKNOWN;

	// The same answer point2oct gave for a buffer it did not fit in: it returned 0, which this
	// function reported as UNKNOWN. Kept rather than sharpened, so the diff is the API change.
	if(pub_size > *key_out_size)
	{
		OPENSSL_free(pub);
		return CHIAKI_ERR_UNKNOWN;
	}

	memcpy(key_out, pub, pub_size);
	OPENSSL_free(pub);
	*key_out_size = pub_size;

	if(!HMAC(EVP_sha256(), handshake_key, CHIAKI_HANDSHAKE_KEY_SIZE, key_out, *key_out_size, sig_out, (unsigned int *)sig_out_size))
		return CHIAKI_ERR_UNKNOWN;
	return CHIAKI_ERR_SUCCESS;

#endif
}

CHIAKI_EXPORT ChiakiErrorCode chiaki_ecdh_derive_secret(ChiakiECDH *ecdh, uint8_t *secret_out, const uint8_t *remote_key, size_t remote_key_size, const uint8_t *handshake_key, const uint8_t *remote_sig, size_t remote_sig_size)
{
	//compute DH shared key
#ifdef CHIAKI_LIB_ENABLE_MBEDTLS
	// https://github.com/ARMmbed/mbedtls/blob/development/programs/pkey/ecdh_curve25519.c#L151
#define GOTO_ERROR(err) do { \
	if((err) !=0){ \
		goto error;} \
	} while(0)

	GOTO_ERROR(mbedtls_mpi_lset(&ecdh->ctx.Qp.Z, 1));
	// load Qp point form remote PK
	GOTO_ERROR(mbedtls_ecp_point_read_binary(&ecdh->ctx.grp,
		&ecdh->ctx.Qp, remote_key, remote_key_size));

	// build shared secret (diffie-hellman)
	GOTO_ERROR(mbedtls_ecdh_compute_shared(&ecdh->ctx.grp,
		&ecdh->ctx.z, &ecdh->ctx.Qp, &ecdh->ctx.d,
		mbedtls_ctr_drbg_random, &ecdh->drbg));

	// export shared secret to data buffer
	GOTO_ERROR(mbedtls_mpi_write_binary(&ecdh->ctx.z,
		secret_out, CHIAKI_ECDH_SECRET_SIZE));

	return CHIAKI_ERR_SUCCESS;
error:
	return CHIAKI_ERR_UNKNOWN;

#else
	// PP427: ECDH_compute_key took the peer as an EC_POINT and the local key as an EC_KEY.
	// EVP_PKEY_derive takes two EVP_PKEYs, so the remote octet string becomes a public-only key
	// first - which is what EC_POINT_new and EC_POINT_oct2point were doing, one level down.
	//
	// PP105's BEHAVIOUR IS UNCHANGED, and deliberately: handshake_key and remote_sig are still
	// taken and still unused. A port that started verifying the remote signature here would differ
	// from the client every user already has, which is the thing PP105 asserted rather than fixed.
	ChiakiErrorCode err = CHIAKI_ERR_UNKNOWN;

	OSSL_PARAM_BLD *bld = NULL;
	OSSL_PARAM *params = NULL;
	EVP_PKEY_CTX *from = NULL;
	EVP_PKEY *peer = NULL;
	EVP_PKEY_CTX *derive = NULL;
	size_t secret_size = CHIAKI_ECDH_SECRET_SIZE;

	bld = OSSL_PARAM_BLD_new();
	if(!bld)
		goto out;

	if(!OSSL_PARAM_BLD_push_utf8_string(bld, OSSL_PKEY_PARAM_GROUP_NAME, CHIAKI_ECDH_CURVE, 0)
			|| !OSSL_PARAM_BLD_push_octet_string(bld, OSSL_PKEY_PARAM_PUB_KEY, remote_key, remote_key_size))
		goto out;

	params = OSSL_PARAM_BLD_to_param(bld);
	if(!params)
		goto out;

	from = EVP_PKEY_CTX_new_from_name(NULL, "EC", NULL);
	if(!from || EVP_PKEY_fromdata_init(from) <= 0)
		goto out;

	// PUBLIC_KEY and not KEYPAIR: the peer has no private half, and asking for one fails.
	if(EVP_PKEY_fromdata(from, &peer, EVP_PKEY_PUBLIC_KEY, params) <= 0)
		goto out;

	derive = EVP_PKEY_CTX_new_from_pkey(NULL, ecdh->key_local, NULL);
	if(!derive || EVP_PKEY_derive_init(derive) <= 0)
		goto out;

	if(EVP_PKEY_derive_set_peer(derive, peer) <= 0)
		goto out;

	// Raw X of the shared point, which is what ECDH_compute_key returned with a NULL KDF. The size
	// is checked the way the old return value was: anything but the secret size is a failure.
	if(EVP_PKEY_derive(derive, secret_out, &secret_size) <= 0
			|| secret_size != CHIAKI_ECDH_SECRET_SIZE)
		goto out;

	err = CHIAKI_ERR_SUCCESS;

out:
	EVP_PKEY_CTX_free(derive);
	EVP_PKEY_free(peer);
	EVP_PKEY_CTX_free(from);
	OSSL_PARAM_free(params);
	OSSL_PARAM_BLD_free(bld);
	return err;
#endif
}
