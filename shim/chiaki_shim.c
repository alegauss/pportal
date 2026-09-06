// SPDX-License-Identifier: LicenseRef-AGPL-3.0-only-OpenSSL

#include "chiaki_shim.h"

#include <chiaki/base64.h>
#include <chiaki/common.h>
#include <chiaki/decoderchoice.h>
#include <chiaki/bitstream.h>
#include <chiaki/controller.h>
#include <chiaki/discovery.h>
#include <chiaki/discoveryservice.h>
#include <chiaki/ecdh.h>
#include <chiaki/feedback.h>
#include <chiaki/fec.h>
#include <chiaki/ffmpegdecoder.h>
#include <chiaki/frameprocessor.h>
#include <chiaki/messagetap.h>
#include <chiaki/packetstats.h>

#include <libavutil/frame.h>
#include <libavutil/hwcontext.h>
#include <libavutil/pixdesc.h>

#include <pb_decode.h>
#include <pb_encode.h>
#include <takion.pb.h>

// PP25: libchiaki's own nanopb callback helpers, which live in lib/src rather than in a public
// header. Reached by relative path, which is what test/allocbudget.c already does for
// takionreceive.h - and reusing them is what keeps the port's encoding the same as the client's
// rather than a second one that happens to agree today.
#include "../lib/src/pb_utils.h"
#include <chiaki/gkcrypt.h>
#include <chiaki/http.h>
/* PP23: the bit reader both slice-header parsers sit on. Header-only, so including it here is the
 * whole of reaching it - there is no symbol to link against. */
#include "../lib/src/vl_rbsp.h"
/* PP33: json-c comes in through chiaki-lib, which links it whole-object for holepunch.c.
 *
 * PP655: and only where that is being built. The fifteen wrappers below are an ORACLE - they let a
 * managed replacement be held against the library it replaces - which is why the nine holepunch
 * ones exist too, for PP33's other half. PP660 counted them after an attempt at this flip failed to
 * link on json_object_object_get_ex, having been sized from a linker answer taken while json-c was
 * still linked. */
#include <chiaki/regist.h>
#include <chiaki/reorderqueue.h>
#include <chiaki/rpcrypt.h>
#include <chiaki/seqnum.h>
#include <chiaki/log.h>
#include <chiaki/orientation.h>
#include <chiaki/session.h>
#include <chiaki/sessionbaseline.h>
#include <chiaki/takion.h>
#include <chiaki/takionsendbuffer.h>
#include <chiaki/videoreceiver.h>

#include <stdlib.h>
#include <string.h>

#ifdef CHIAKI_SHIM_HAVE_OPUS
#include <opus/opus.h>
#endif

CHIAKI_SHIM_API uint32_t chiaki_shim_abi_version(void)
{
	return CHIAKI_SHIM_ABI;
}

/*
 * PP661: whether this shim carries the oracles PP655's flip removes.
 *
 * ASKED OF THE BUILD, NOT OF THE SOURCE, and that distinction is the whole reason these exist. The
 * first mechanism read the shim's header for the wrappers' names and reported them present on a
 * build where they were inside an #ifdef nobody had defined - the declarations are still in the
 * FILE, and a text reader cannot see a preprocessor. 128 assertions went red at once saying so.
 *
 * These two always exist, whatever the option says, and answer for the build that produced the DLL
 * the host actually loaded. A managed guard that asks them cannot be wrong about which shim it has.
 */
CHIAKI_SHIM_API bool chiaki_shim_has_holepunch(void)
{
#ifdef CHIAKI_SHIM_HAVE_HOLEPUNCH
	return true;
#else
	return false;
#endif
}

CHIAKI_SHIM_API bool chiaki_shim_has_jsonc(void)
{
#ifdef CHIAKI_SHIM_HAVE_JSONC
	return true;
#else
	return false;
#endif
}

/* PP670: the frame path's oracles, asked the same way. The define is set unconditionally in
 * shim/CMakeLists.txt today; the flip that removes fec.c, frameprocessor.c and videoreceiver.c
 * from the build makes it follow an option and wraps the fourteen in it, and nothing managed
 * needs editing on that day because every caller already asks here first. */
CHIAKI_SHIM_API bool chiaki_shim_has_framepath(void)
{
#ifdef CHIAKI_SHIM_HAVE_FRAMEPATH
	return true;
#else
	return false;
#endif
}

/* PP694: libopus, asked the same way, for the encoder oracle below.
 *
 * CHIAKI_LIB_ENABLE_OPUS defaults ON and every build this tree has produced has it, so this reads
 * true today - and asking is still the right shape rather than a formality. The option exists, the
 * five wrappers below are inside it, and PP681's defect was exactly a guard that answered from
 * something other than the build that made the DLL. */
CHIAKI_SHIM_API bool chiaki_shim_has_opus(void)
{
#ifdef CHIAKI_SHIM_HAVE_OPUS
	return true;
#else
	return false;
#endif
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

/**
 * The ChiakiLog is the first member on purpose: everything below hands `&self->log` to libchiaki,
 * and every session function exported later will do the same, so the address the library keeps is
 * this allocation's and stays valid until chiaki_shim_log_free.
 */
typedef struct chiaki_shim_log_t
{
	ChiakiLog log;
	ChiakiShimLogCb cb;
	void *user;
} chiaki_shim_log;

static void chiaki_shim_log_dispatch(ChiakiLogLevel level, const char *msg, void *user)
{
	chiaki_shim_log *self = (chiaki_shim_log *)user;
	if(self && self->cb)
		self->cb((int32_t)level, msg, self->user);
}

CHIAKI_SHIM_API void *chiaki_shim_log_create(uint32_t level_mask, ChiakiShimLogCb cb, void *user)
{
	chiaki_shim_log *self = (chiaki_shim_log *)calloc(1, sizeof(chiaki_shim_log));
	if(!self)
		return NULL;

	self->cb = cb;
	self->user = user;
	// The `user` libchiaki gets is this allocation, not the caller's: the caller's pointer is
	// re-attached in the dispatcher, which is what lets the level be re-emitted on the way past.
	chiaki_log_init(&self->log, level_mask, chiaki_shim_log_dispatch, self);
	return self;
}

CHIAKI_SHIM_API void chiaki_shim_log_free(void *log)
{
	chiaki_shim_log *self = (chiaki_shim_log *)log;
	if(!self)
		return;

	// Cleared before the free so that a message already inside chiaki_log on another thread
	// finds no callback rather than a freed one. It narrows the window; it does not close it,
	// and nothing here pretends otherwise - the caller frees a log no session is using.
	self->cb = NULL;
	chiaki_log_init(&self->log, 0, NULL, NULL);
	free(self);
}

CHIAKI_SHIM_API void chiaki_shim_log_set_level(void *log, uint32_t level_mask)
{
	chiaki_shim_log *self = (chiaki_shim_log *)log;
	if(self)
		chiaki_log_set_level(&self->log, level_mask);
}

CHIAKI_SHIM_API uint32_t chiaki_shim_log_level_mask(void *log)
{
	chiaki_shim_log *self = (chiaki_shim_log *)log;
	return self ? self->log.level_mask : 0;
}

CHIAKI_SHIM_API void chiaki_shim_log_write(void *log, int32_t level, const char *msg)
{
	chiaki_shim_log *self = (chiaki_shim_log *)log;
	if(!self || !msg)
		return;

	chiaki_log(&self->log, (ChiakiLogLevel)level, "%s", msg);
}

CHIAKI_SHIM_API char chiaki_shim_log_level_char(int32_t level)
{
	return chiaki_log_level_char((ChiakiLogLevel)level);
}

/**
 * PP323: the tap's trampoline and the pointer it hands on.
 *
 * The same shape as the log's, for the same reason: libchiaki's callback takes an enum whose
 * underlying type is the compiler's choice, and casting a managed function pointer into that slot
 * would be a bet on what MinGW picked today. So the shim installs a function of its own.
 *
 * The size re-narrows too. lib/src carries a size_t and a managed handler wants a length it can
 * make a span of, and a payload wider than int32 does not exist here - the ctrl receive buffer and
 * the session header buffer are both far below it. Clamped rather than truncated: a negative length
 * would be the one value a span constructor throws on, arriving from a code path nobody could find.
 */
static ChiakiShimTapCb chiaki_shim_tap_cb = NULL;
static void *chiaki_shim_tap_user = NULL;

static void chiaki_shim_tap_trampoline(
		int32_t direction, const char *channel, uint16_t type,
		const uint8_t *payload, size_t payload_size, void *user)
{
	ChiakiShimTapCb cb = chiaki_shim_tap_cb;
	(void)user;

	if(!cb)
		return;

	if(payload_size > (size_t)INT32_MAX)
		payload_size = (size_t)INT32_MAX;

	cb(direction, channel, type, payload, (int32_t)payload_size, chiaki_shim_tap_user);
}

CHIAKI_SHIM_API void chiaki_shim_tap_set(ChiakiShimTapCb cb, void *user)
{
	chiaki_shim_tap_user = user;
	chiaki_shim_tap_cb = cb;

	// Uninstalled at the LIBRARY when the managed side clears it, rather than left installed and
	// answering nothing. A tap that stays wired is a branch every ctrl message keeps paying for, and
	// chiaki_message_tap_active would then say yes to a caller that had turned it off.
	chiaki_message_tap_set(cb ? chiaki_shim_tap_trampoline : NULL, NULL);
}

CHIAKI_SHIM_API bool chiaki_shim_tap_active(void)
{
	return chiaki_message_tap_active();
}

CHIAKI_SHIM_API void chiaki_shim_tap_emit(
		int32_t direction, const char *channel, uint16_t type,
		const uint8_t *payload, int32_t payload_size)
{
	if(payload_size < 0)
		return;

	chiaki_message_tap_emit(
			(ChiakiMessageTapDirection)direction, channel, type, payload, (size_t)payload_size);
}

CHIAKI_SHIM_API int32_t chiaki_shim_lib_init(void)
{
	return (int32_t)chiaki_lib_init();
}

/**
 * The connect info, plus the string it points at.
 *
 * ChiakiConnectInfo::host is a borrowed `const char *`, and the caller of a P/Invoke owns its
 * marshalled string only for the duration of the call. So the host is copied in here and freed
 * with the builder, which makes the lifetime this side's rather than a rule the managed side
 * would have to keep.
 */
typedef struct chiaki_shim_connect_info_t
{
	ChiakiConnectInfo info;
	char *host;
} chiaki_shim_connect_info;

CHIAKI_SHIM_API void *chiaki_shim_connect_info_create(void)
{
	chiaki_shim_connect_info *self =
			(chiaki_shim_connect_info *)calloc(1, sizeof(chiaki_shim_connect_info));
	if(!self)
		return NULL;

	// 1080p60 rather than zeroes: a video profile of 0x0 is accepted by chiaki_session_init and
	// then negotiated with the console, so an unset profile would be a black stream and not an
	// error. This is the same preset the Qt client's own default resolves to.
	chiaki_connect_video_profile_preset(&self->info.video_profile,
			CHIAKI_VIDEO_RESOLUTION_PRESET_1080p, CHIAKI_VIDEO_FPS_PRESET_60);
	return self;
}

CHIAKI_SHIM_API void chiaki_shim_connect_info_free(void *info)
{
	chiaki_shim_connect_info *self = (chiaki_shim_connect_info *)info;
	if(!self)
		return;

	free(self->host);
	free(self);
}

CHIAKI_SHIM_API bool chiaki_shim_connect_info_set_host(void *info, const char *host)
{
	chiaki_shim_connect_info *self = (chiaki_shim_connect_info *)info;
	if(!self || !host)
		return false;

	size_t len = strlen(host);
	char *copy = (char *)malloc(len + 1);
	if(!copy)
		return false;
	memcpy(copy, host, len + 1);

	free(self->host);
	self->host = copy;
	self->info.host = copy;
	return true;
}

CHIAKI_SHIM_API void chiaki_shim_connect_info_set_ps5(void *info, bool ps5)
{
	chiaki_shim_connect_info *self = (chiaki_shim_connect_info *)info;
	if(self)
		self->info.ps5 = ps5;
}

CHIAKI_SHIM_API bool chiaki_shim_connect_info_set_regist_key(
		void *info, const uint8_t *key, int32_t len)
{
	chiaki_shim_connect_info *self = (chiaki_shim_connect_info *)info;
	if(!self || !key || len < 0 || (size_t)len > sizeof(self->info.regist_key))
		return false;

	// Zeroed first: the field must be "completely filled (pad with \0)", and a second call with a
	// shorter key would otherwise leave the tail of the first one behind it.
	memset(self->info.regist_key, 0, sizeof(self->info.regist_key));
	memcpy(self->info.regist_key, key, (size_t)len);
	return true;
}

CHIAKI_SHIM_API bool chiaki_shim_connect_info_set_morning(
		void *info, const uint8_t *morning, int32_t len)
{
	chiaki_shim_connect_info *self = (chiaki_shim_connect_info *)info;
	if(!self || !morning || (size_t)len != sizeof(self->info.morning))
		return false;

	memcpy(self->info.morning, morning, sizeof(self->info.morning));
	return true;
}

CHIAKI_SHIM_API void chiaki_shim_connect_info_set_video_preset(
		void *info, int32_t resolution, int32_t fps)
{
	chiaki_shim_connect_info *self = (chiaki_shim_connect_info *)info;
	if(self)
		chiaki_connect_video_profile_preset(&self->info.video_profile,
				(ChiakiVideoResolutionPreset)resolution, (ChiakiVideoFPSPreset)fps);
}

CHIAKI_SHIM_API void chiaki_shim_connect_info_set_bitrate(void *info, uint32_t bitrate)
{
	chiaki_shim_connect_info *self = (chiaki_shim_connect_info *)info;
	if(self)
		self->info.video_profile.bitrate = bitrate;
}

CHIAKI_SHIM_API void chiaki_shim_connect_info_set_codec(void *info, int32_t codec)
{
	chiaki_shim_connect_info *self = (chiaki_shim_connect_info *)info;
	if(self)
		self->info.video_profile.codec = (ChiakiCodec)codec;
}

CHIAKI_SHIM_API void chiaki_shim_connect_info_video_profile(
		void *info,
		uint32_t *width,
		uint32_t *height,
		uint32_t *max_fps,
		uint32_t *bitrate,
		int32_t *codec)
{
	chiaki_shim_connect_info *self = (chiaki_shim_connect_info *)info;
	const ChiakiConnectVideoProfile *p = self ? &self->info.video_profile : NULL;

	if(width)
		*width = p ? (uint32_t)p->width : 0;
	if(height)
		*height = p ? (uint32_t)p->height : 0;
	if(max_fps)
		*max_fps = p ? (uint32_t)p->max_fps : 0;
	if(bitrate)
		*bitrate = p ? (uint32_t)p->bitrate : 0;
	if(codec)
		*codec = p ? (int32_t)p->codec : 0;
}

CHIAKI_SHIM_API void chiaki_shim_connect_info_set_flags(
		void *info,
		bool video_profile_auto_downgrade,
		bool enable_keyboard,
		bool enable_dualsense,
		bool enable_idr_on_fec_failure)
{
	chiaki_shim_connect_info *self = (chiaki_shim_connect_info *)info;
	if(!self)
		return;

	self->info.video_profile_auto_downgrade = video_profile_auto_downgrade;
	self->info.enable_keyboard = enable_keyboard;
	self->info.enable_dualsense = enable_dualsense;
	self->info.enable_idr_on_fec_failure = enable_idr_on_fec_failure;
}

CHIAKI_SHIM_API void chiaki_shim_connect_info_set_packet_loss_max(
		void *info, double packet_loss_max)
{
	chiaki_shim_connect_info *self = (chiaki_shim_connect_info *)info;
	if(self)
		self->info.packet_loss_max = packet_loss_max;
}

/**
 * As with the log: the ChiakiSession is the first member, so the handle the managed side holds is
 * the address libchiaki was given, and the callback and its user pointer ride alongside it where
 * only this file can see them.
 */
/*
 * PP700: the decoder a session decodes into, which nothing here had.
 *
 * The session's video_sample_cb is the join: libchiaki hands it every assembled frame, and
 * chiaki_ffmpeg_decoder_video_sample_cb is the C's own implementation of it. Installing that with a
 * decoder as its user is the whole of what makes a session decode - and no path in this port did
 * it, so every stream reached the frame processor and stopped.
 *
 * The count is here because it is the only thing a first slice can assert. A decoded frame's
 * PICTURE needs the render seam and a surface; that it decoded at all is a number, and a number a
 * live session produces is what tells a run from a hope.
 */
typedef struct chiaki_shim_decoder_t
{
	ChiakiFfmpegDecoder decoder;
	uint64_t frames_available;
	bool started;

	/*
	 * PP700: ONE frame is held, and the next pull frees it.
	 *
	 * chiaki_ffmpeg_decoder_pull_frame hands over an AVFrame the caller owns. Handing its plane
	 * POINTERS to managed code and letting that side free it would put an av_frame_free across the
	 * seam, which is the ownership rule this shim exists to avoid. So the frame stays here and its
	 * pointers are valid until the next pull - the same borrow the video sample callback already
	 * documents for its buffer.
	 */
	AVFrame *held;

	/*
	 * PP700: where a hardware frame is downloaded to.
	 *
	 * A vulkan decoder's frames arrive as AV_PIX_FMT_VULKAN, which are Vulkan images - and this
	 * port's presenter is D3D11. The two do not share a texture, so the frame comes down to
	 * system memory as NV12 and goes back up as a D3D11 texture.
	 *
	 * That download IS the per-frame copy PP48 measured, and it is libchiaki's own answer too:
	 * make_fallback_snapshot_frame does exactly this for any hardware frame the client cannot hand
	 * on. Reproducing it rather than avoiding it keeps the cost where the measurement put it.
	 */
	AVFrame *downloaded;

	/*
	 * PP76: signalled when a frame becomes available, so a reader waits rather than polls.
	 *
	 * Borrowed - the managed side owns the handle and closes it. Held as a raw HANDLE because the
	 * only thing done with it is SetEvent, which is what makes this safe to call from libchiaki's
	 * thread.
	 */
	HANDLE ready;

	/*
	 * PP76: what the CODEC's own frame counter stood at when the last pull ran.
	 *
	 * The difference is exactly what the drain swallowed. chiaki_ffmpeg_decoder_pull_frame keeps
	 * only the last frame and counts none of the rest, so nothing downstream can tell how many
	 * there were - but avcodec_receive_frame does, in AVCodecContext::frame_num, and the drain
	 * calls it once per frame it throws away.
	 *
	 * NOT frames_available, which was tried first and cannot close. That counter is the
	 * PRODUCER's, written from libchiaki's thread; sampling it around a drain this thread runs
	 * races the drain both ways - read before, and a frame that arrives mid-drain is returned
	 * without ever entering a difference; read after, and the same frame is charged as swallowed
	 * and then returned by the next pull, counted twice. Measured, that leaked a frame or two a
	 * session in whichever direction the sampling leaned. frame_num has no such race: it advances
	 * only inside the drain, which only this thread calls.
	 */
	int64_t consumed_at_last_pull;
} chiaki_shim_decoder;

typedef struct chiaki_shim_session_t
{
	ChiakiSession session;
	ChiakiShimEventCb cb;
	void *user;

	/* Borrowed. The caller owns the decoder and must outlive the session, which is the same rule
	 * the log already has and the reason neither is freed here. */
	chiaki_shim_decoder *decoder;
} chiaki_shim_session;

/*
 * PP76: the reader is WOKEN rather than left to poll.
 *
 * chiaki_ffmpeg_decoder_pull_frame drains the codec and returns only the last - its own comment
 * says "always try to pull as much as possible and return only the very last frame" - and it counts
 * none of the ones it throws away. So a reader that polls accumulates frames between its own ticks
 * and loses them silently, which measures its interval under the decoder's name. The Qt client
 * pulls from this callback and has no such gap.
 *
 * A Win32 event and not a managed callback. This runs on libchiaki's own thread, and SetEvent is a
 * syscall that cannot throw, cannot allocate and cannot enter a runtime - which a delegate crossing
 * the seam here would do sixty times a second inside the packet path.
 */
static void chiaki_shim_frame_available(ChiakiFfmpegDecoder *decoder, void *user)
{
	chiaki_shim_decoder *self = (chiaki_shim_decoder *)user;
	(void)decoder;
	if(!self)
		return;

	self->frames_available++;

	if(self->ready)
		SetEvent(self->ready);
}

static void chiaki_shim_session_dispatch(ChiakiEvent *event, void *user)
{
	chiaki_shim_session *self = (chiaki_shim_session *)user;
	if(!self || !self->cb || !event)
		return;

	// Decoded arm by arm rather than handed over whole. The quit arm is the one that ends every
	// session; the rest arrive as a type until the screen that reads their payload exists.
	if(event->type == CHIAKI_EVENT_QUIT)
		self->cb((int32_t)event->type, (int32_t)event->quit.reason, event->quit.reason_str,
				self->user);
	else
		self->cb((int32_t)event->type, 0, NULL, self->user);
}

CHIAKI_SHIM_API void *chiaki_shim_session_create(void *connect_info, void *log, int32_t *error_out)
{
	chiaki_shim_connect_info *info = (chiaki_shim_connect_info *)connect_info;
	chiaki_shim_log *log_self = (chiaki_shim_log *)log;

	if(error_out)
		*error_out = (int32_t)CHIAKI_ERR_INVALID_DATA;
	if(!info || !info->info.host)
		return NULL;

	chiaki_shim_session *self = (chiaki_shim_session *)calloc(1, sizeof(chiaki_shim_session));
	if(!self)
	{
		if(error_out)
			*error_out = (int32_t)CHIAKI_ERR_MEMORY;
		return NULL;
	}

	ChiakiErrorCode err = chiaki_session_init(&self->session, &info->info,
			log_self ? &log_self->log : NULL);
	if(error_out)
		*error_out = (int32_t)err;

	if(err != CHIAKI_ERR_SUCCESS)
	{
		// No chiaki_session_fini here on purpose: chiaki_session_init unwinds whatever it had
		// built before it failed, and the address-parse path calls fini itself before returning.
		// A second one would be a double free of the ctrl and the stop pipe.
		free(self);
		return NULL;
	}

	return self;
}

/*
 * PP700: create a decoder, or NULL and why.
 *
 * `hw_decoder_name` is the setting's own string - "vulkan", "cuda", "d3d11va" - and NULL asks
 * libchiaki for software. The C refuses a name it has no device for, which is how a machine
 * without the driver says so rather than decoding silently on the CPU.
 */
CHIAKI_SHIM_API void *chiaki_shim_decoder_create(
		void *log, int32_t codec, int32_t max_fps, const char *hw_decoder_name, int32_t *error_out)
{
	chiaki_shim_log *log_self = (chiaki_shim_log *)log;
	chiaki_shim_decoder *self = (chiaki_shim_decoder *)calloc(1, sizeof(chiaki_shim_decoder));

	if(error_out)
		*error_out = (int32_t)CHIAKI_ERR_MEMORY;
	if(!self)
		return NULL;

	ChiakiErrorCode err = chiaki_ffmpeg_decoder_init(
			&self->decoder,
			log_self ? &log_self->log : NULL,
			(ChiakiCodec)codec,
			(unsigned int)(max_fps > 0 ? max_fps : 60),
			(hw_decoder_name && hw_decoder_name[0]) ? hw_decoder_name : NULL,
			NULL,
			chiaki_shim_frame_available,
			self);

	if(error_out)
		*error_out = (int32_t)err;

	if(err != CHIAKI_ERR_SUCCESS)
	{
		free(self);
		return NULL;
	}

	self->started = true;
	return self;
}

CHIAKI_SHIM_API void chiaki_shim_decoder_free(void *decoder)
{
	chiaki_shim_decoder *self = (chiaki_shim_decoder *)decoder;
	if(!self)
		return;

	if(self->held)
		av_frame_free(&self->held);
	if(self->downloaded)
		av_frame_free(&self->downloaded);
	if(self->started)
		chiaki_ffmpeg_decoder_fini(&self->decoder);

	free(self);
}

/*
 * PP700: one decoded frame's planes, borrowed until the next pull.
 *
 * NV12 only, and that is a statement rather than a limitation this hides. The presenter takes two
 * planes; a software decoder here resolves to yuv420p, which is three. Reporting the format and
 * refusing rather than converting is what keeps a run honest: a caller that asked for hardware and
 * got a software frame should see that, not a picture assembled by a converter nobody measured.
 *
 * `out_lost` carries what the decoder accumulated, which PP528 repaired and which is zeroed by the
 * pull - so this is the only place it can be read.
 */
CHIAKI_SHIM_API bool chiaki_shim_decoder_pull(
		void *decoder,
		int32_t *out_w, int32_t *out_h,
		uint8_t **out_luma, int32_t *out_luma_stride,
		uint8_t **out_chroma, int32_t *out_chroma_stride,
		int32_t *out_format, int32_t *out_lost, int32_t *out_superseded)
{
	chiaki_shim_decoder *self = (chiaki_shim_decoder *)decoder;
	ChiakiFfmpegFrame pulled;
	int32_t lost = 0;
	AVFrame *shown;
	int64_t consumed;

	if(out_lost)
		*out_lost = 0;
	if(out_superseded)
		*out_superseded = 0;
	if(!self)
		return false;

	/* Freed before the next is taken, so exactly one frame is ever held. */
	if(self->held)
		av_frame_free(&self->held);

	pulled = chiaki_ffmpeg_decoder_pull_frame(&self->decoder, &lost);

	if(out_lost)
		*out_lost = lost;

	/*
	 * PP76: how many the drain swallowed, read off the codec once it has.
	 *
	 * frame_num is every frame avcodec_receive_frame has handed back. The drain calls it in a
	 * loop and keeps the last - its own comment says "return only the very last frame" - so the
	 * frames it advanced past, less the one it returned, are decoded frames nobody will ever see.
	 * That is exactly the C's frames_dropped, and this is the only place the number exists.
	 *
	 * ONE IS SUBTRACTED ONLY WHEN ONE IS RETURNED: a codec that has not filled its reorder window
	 * returns nothing for the first several packets, and charging it for a frame it never handed
	 * over leaves a decoded frame in no column at all.
	 */
	consumed = self->decoder.codec_context ? self->decoder.codec_context->frame_num : 0;

	if(out_superseded && consumed > self->consumed_at_last_pull)
	{
		int64_t swallowed =
				consumed - self->consumed_at_last_pull - (pulled.frame ? 1 : 0);
		if(swallowed > 0)
			*out_superseded = (int32_t)swallowed;
	}

	self->consumed_at_last_pull = consumed;

	if(!pulled.frame)
		return false;

	self->held = pulled.frame;
	shown = self->held;

	if(out_format)
		*out_format = self->held->format;
	if(out_w)
		*out_w = self->held->width;
	if(out_h)
		*out_h = self->held->height;

	/*
	 * A hardware frame comes DOWN first. Its format is the device's - AV_PIX_FMT_VULKAN on a
	 * vulkan decoder - and a Vulkan image is not something a D3D11 presenter can wrap. This is the
	 * copy PP48 measured and the one make_fallback_snapshot_frame makes for the same reason.
	 */
	if(self->held->hw_frames_ctx)
	{
		if(!self->downloaded)
		{
			self->downloaded = av_frame_alloc();
			if(!self->downloaded)
				return false;
		}

		/* NV12, because that is what the decoder says a downloaded frame is and what the
		 * presenter takes. Asking for it rather than accepting the default keeps the two ends
		 * agreeing about the format rather than about the luck of a driver. */
		av_frame_unref(self->downloaded);
		self->downloaded->format = AV_PIX_FMT_NV12;

		if(av_hwframe_transfer_data(self->downloaded, self->held, 0) < 0)
			return false;

		shown = self->downloaded;

		if(out_format)
			*out_format = shown->format;
	}

	if(shown->format != AV_PIX_FMT_NV12)
		return false;

	if(out_luma)
		*out_luma = shown->data[0];
	if(out_luma_stride)
		*out_luma_stride = shown->linesize[0];
	if(out_chroma)
		*out_chroma = shown->data[1];
	if(out_chroma_stride)
		*out_chroma_stride = shown->linesize[1];

	return true;
}

/*
 * PP700: THE JOIN. The session's video_sample_cb becomes the decoder's.
 *
 * Set before chiaki_shim_session_start, for the reason the event callback carries: the field is
 * read by the stream connection's own thread, and installing it after that thread exists is a race
 * whose losing side is a session that decodes nothing.
 */
CHIAKI_SHIM_API bool chiaki_shim_session_set_decoder(void *session, void *decoder)
{
	chiaki_shim_session *self = (chiaki_shim_session *)session;
	chiaki_shim_decoder *dec = (chiaki_shim_decoder *)decoder;

	if(!self)
		return false;

	self->decoder = dec;
	self->session.video_sample_cb = dec ? chiaki_ffmpeg_decoder_video_sample_cb : NULL;
	self->session.video_sample_cb_user = dec ? &dec->decoder : NULL;
	return true;
}

/*
 * PP76: the event to set when a frame is ready, or NULL to stop signalling.
 *
 * Borrowed. The caller owns the handle and closes it, and must clear this before doing so - a
 * decoder signalling a closed handle is the one way this can crash rather than merely stop working.
 */
CHIAKI_SHIM_API void chiaki_shim_decoder_set_ready_event(void *decoder, void *event)
{
	chiaki_shim_decoder *self = (chiaki_shim_decoder *)decoder;
	if(self)
		self->ready = (HANDLE)event;
}

/** How many times the decoder said a frame was ready. */
CHIAKI_SHIM_API uint64_t chiaki_shim_decoder_frames_available(void *decoder)
{
	chiaki_shim_decoder *self = (chiaki_shim_decoder *)decoder;
	return self ? self->frames_available : 0;
}

/**
 * PP76: frames the codec has actually handed back, which is the total a reader can account for.
 *
 * NOT frames_available, which counts the callback and is therefore what the decoder PRODUCED. The
 * gap between them is frames still inside the codec, and a comparison that reads the produced
 * total against what a reader shows and discards can never close: it is subtracting two clocks.
 * This is the one the shown-plus-swallowed identity is written against.
 */
CHIAKI_SHIM_API uint64_t chiaki_shim_decoder_frames_decoded(void *decoder)
{
	chiaki_shim_decoder *self = (chiaki_shim_decoder *)decoder;
	if(!self || !self->decoder.codec_context)
		return 0;
	return (uint64_t)self->decoder.codec_context->frame_num;
}

/** The pixel format the decoder resolved, which says whether the hardware path was taken. */
CHIAKI_SHIM_API int32_t chiaki_shim_decoder_pixel_format(void *decoder)
{
	chiaki_shim_decoder *self = (chiaki_shim_decoder *)decoder;
	return self ? (int32_t)chiaki_ffmpeg_decoder_get_pixel_format(&self->decoder) : -1;
}

/*
 * PP700: its NAME, which is what a reader and a recorded run both want.
 *
 * The managed side cannot name an AVPixelFormat: the enum is sequential and unnumbered in
 * pixfmt.h, so a literal on that side is a guess that a different ffmpeg quietly invalidates.
 * av_get_pix_fmt_name is the C's own answer and it is in scope here.
 */
CHIAKI_SHIM_API int32_t chiaki_shim_decoder_pixel_format_name(void *decoder, char *buf, int32_t buf_size)
{
	chiaki_shim_decoder *self = (chiaki_shim_decoder *)decoder;
	const char *name;
	size_t len;

	if(!buf || buf_size < 1)
		return 0;

	buf[0] = 0;
	if(!self)
		return 0;

	name = av_get_pix_fmt_name(chiaki_ffmpeg_decoder_get_pixel_format(&self->decoder));
	if(!name)
		return 0;

	len = strlen(name);
	if(len >= (size_t)buf_size)
		len = (size_t)buf_size - 1;

	memcpy(buf, name, len);
	buf[len] = 0;
	return (int32_t)len;
}

/*
 * PP700: whether libchiaki copies every frame out of this decoder.
 *
 * PP48 measured the per-frame copy make_fallback_snapshot_frame runs for any hardware frame that
 * is not AV_PIX_FMT_VULKAN - 793us on cuda, 2253us on d3d11va, nothing on vulkan.
 *
 * THE FIRST VERSION OF THIS ASKED THE WRONG FUNCTION, and a run said so.
 * chiaki_ffmpeg_decoder_get_pixel_format returns the format a frame has AFTER a download - NV12 or
 * P010 with a hardware context, YUV420P or YUV420P10 without - so it can never equal
 * AV_PIX_FMT_VULKAN and this reported "copied per frame" on a vulkan decoder whose frames arrived
 * as format 190. `hw_pix_fmt` is the frame's own, which is what the comparison is about.
 *
 * A software decoder has hw_pix_fmt AV_PIX_FMT_NONE and copies, which this reports.
 */
CHIAKI_SHIM_API bool chiaki_shim_decoder_copies_every_frame(void *decoder)
{
	chiaki_shim_decoder *self = (chiaki_shim_decoder *)decoder;
	if(!self)
		return true;

	return self->decoder.hw_pix_fmt != AV_PIX_FMT_VULKAN;
}

/** The format a FRAME carries, which is the hardware one where there is a device. */
CHIAKI_SHIM_API int32_t chiaki_shim_decoder_frame_format(void *decoder)
{
	chiaki_shim_decoder *self = (chiaki_shim_decoder *)decoder;
	return self ? (int32_t)self->decoder.hw_pix_fmt : (int32_t)AV_PIX_FMT_NONE;
}

/** Any AVPixelFormat's name, so a caller can print one it did not expect. */
CHIAKI_SHIM_API int32_t chiaki_shim_pixel_format_name(int32_t format, char *buf, int32_t buf_size)
{
	const char *name;
	size_t len;

	if(!buf || buf_size < 1)
		return 0;

	buf[0] = 0;
	name = av_get_pix_fmt_name((enum AVPixelFormat)format);
	if(!name)
		return 0;

	len = strlen(name);
	if(len >= (size_t)buf_size)
		len = (size_t)buf_size - 1;

	memcpy(buf, name, len);
	buf[len] = 0;
	return (int32_t)len;
}

CHIAKI_SHIM_API bool chiaki_shim_session_set_event_cb(
		void *session, ChiakiShimEventCb cb, void *user)
{
	chiaki_shim_session *self = (chiaki_shim_session *)session;
	if(!self)
		return false;

	self->cb = cb;
	self->user = user;
	chiaki_session_set_event_cb(&self->session, cb ? chiaki_shim_session_dispatch : NULL, self);
	return true;
}

CHIAKI_SHIM_API int32_t chiaki_shim_session_start(void *session)
{
	chiaki_shim_session *self = (chiaki_shim_session *)session;
	return self ? (int32_t)chiaki_session_start(&self->session) : (int32_t)CHIAKI_ERR_INVALID_DATA;
}

CHIAKI_SHIM_API int32_t chiaki_shim_session_stop(void *session)
{
	chiaki_shim_session *self = (chiaki_shim_session *)session;
	return self ? (int32_t)chiaki_session_stop(&self->session) : (int32_t)CHIAKI_ERR_INVALID_DATA;
}

CHIAKI_SHIM_API int32_t chiaki_shim_session_join(void *session)
{
	chiaki_shim_session *self = (chiaki_shim_session *)session;
	return self ? (int32_t)chiaki_session_join(&self->session) : (int32_t)CHIAKI_ERR_INVALID_DATA;
}

/* PP627: the answer to the one event that asks for one.
 *
 * A null or empty pin is refused here rather than passed on. chiaki_session_set_login_pin mallocs
 * pin_size bytes and sets login_pin_entered, so a zero-size call wakes the session thread with a
 * buffer it will read nothing out of - and PP345 settled that a spent PIN cannot be retried, so the
 * cost of the empty one is a prompt the user never sees again. */
CHIAKI_SHIM_API int32_t chiaki_shim_session_set_login_pin(
	void *session, const uint8_t *pin, size_t pin_size)
{
	chiaki_shim_session *self = (chiaki_shim_session *)session;
	if(!self || !pin || pin_size == 0)
		return (int32_t)CHIAKI_ERR_INVALID_DATA;

	return (int32_t)chiaki_session_set_login_pin(&self->session, pin, pin_size);
}

CHIAKI_SHIM_API void *chiaki_shim_controller_state_create(void)
{
	ChiakiControllerState *state = (ChiakiControllerState *)calloc(1, sizeof(ChiakiControllerState));
	if(state)
		chiaki_controller_state_set_idle(state);
	return state;
}

CHIAKI_SHIM_API void chiaki_shim_controller_state_free(void *state)
{
	free(state);
}

CHIAKI_SHIM_API void chiaki_shim_controller_state_set_idle(void *state)
{
	if(state)
		chiaki_controller_state_set_idle((ChiakiControllerState *)state);
}

CHIAKI_SHIM_API void chiaki_shim_controller_state_set_buttons(void *state, uint32_t buttons)
{
	if(state)
		((ChiakiControllerState *)state)->buttons = buttons;
}

CHIAKI_SHIM_API uint32_t chiaki_shim_controller_state_buttons(void *state)
{
	return state ? ((ChiakiControllerState *)state)->buttons : 0;
}

CHIAKI_SHIM_API void chiaki_shim_controller_state_set_triggers(void *state, uint8_t l2, uint8_t r2)
{
	ChiakiControllerState *self = (ChiakiControllerState *)state;
	if(!self)
		return;

	self->l2_state = l2;
	self->r2_state = r2;
}

CHIAKI_SHIM_API void chiaki_shim_controller_state_triggers(void *state, uint8_t *l2, uint8_t *r2)
{
	ChiakiControllerState *self = (ChiakiControllerState *)state;
	if(l2)
		*l2 = self ? self->l2_state : 0;
	if(r2)
		*r2 = self ? self->r2_state : 0;
}

CHIAKI_SHIM_API void chiaki_shim_controller_state_set_sticks(
		void *state, int16_t left_x, int16_t left_y, int16_t right_x, int16_t right_y)
{
	ChiakiControllerState *self = (ChiakiControllerState *)state;
	if(!self)
		return;

	self->left_x = left_x;
	self->left_y = left_y;
	self->right_x = right_x;
	self->right_y = right_y;
}

CHIAKI_SHIM_API void chiaki_shim_controller_state_sticks(
		void *state, int16_t *left_x, int16_t *left_y, int16_t *right_x, int16_t *right_y)
{
	ChiakiControllerState *self = (ChiakiControllerState *)state;
	if(left_x)
		*left_x = self ? self->left_x : 0;
	if(left_y)
		*left_y = self ? self->left_y : 0;
	if(right_x)
		*right_x = self ? self->right_x : 0;
	if(right_y)
		*right_y = self ? self->right_y : 0;
}

CHIAKI_SHIM_API void chiaki_shim_controller_state_set_motion(
		void *state,
		float gyro_x, float gyro_y, float gyro_z,
		float accel_x, float accel_y, float accel_z,
		float orient_x, float orient_y, float orient_z, float orient_w)
{
	ChiakiControllerState *self = (ChiakiControllerState *)state;
	if(!self)
		return;

	self->gyro_x = gyro_x;
	self->gyro_y = gyro_y;
	self->gyro_z = gyro_z;
	self->accel_x = accel_x;
	self->accel_y = accel_y;
	self->accel_z = accel_z;
	self->orient_x = orient_x;
	self->orient_y = orient_y;
	self->orient_z = orient_z;
	self->orient_w = orient_w;
}

CHIAKI_SHIM_API int8_t chiaki_shim_controller_state_start_touch(void *state, uint16_t x, uint16_t y)
{
	return state ? chiaki_controller_state_start_touch((ChiakiControllerState *)state, x, y) : -1;
}

CHIAKI_SHIM_API void chiaki_shim_controller_state_stop_touch(void *state, uint8_t id)
{
	if(state)
		chiaki_controller_state_stop_touch((ChiakiControllerState *)state, id);
}

CHIAKI_SHIM_API void chiaki_shim_controller_state_set_touch_pos(
		void *state, uint8_t id, uint16_t x, uint16_t y)
{
	if(state)
		chiaki_controller_state_set_touch_pos((ChiakiControllerState *)state, id, x, y);
}

CHIAKI_SHIM_API bool chiaki_shim_controller_state_touch(
		void *state, int32_t slot, uint16_t *x, uint16_t *y, int32_t *id)
{
	ChiakiControllerState *self = (ChiakiControllerState *)state;
	if(!self || slot < 0 || (size_t)slot >= CHIAKI_CONTROLLER_TOUCHES_MAX)
		return false;

	if(x)
		*x = self->touches[slot].x;
	if(y)
		*y = self->touches[slot].y;
	if(id)
		*id = self->touches[slot].id;
	return true;
}

CHIAKI_SHIM_API bool chiaki_shim_controller_state_equals(void *a, void *b)
{
	if(!a || !b)
		return false;

	return chiaki_controller_state_equals((ChiakiControllerState *)a, (ChiakiControllerState *)b);
}

CHIAKI_SHIM_API void chiaki_shim_controller_state_or(void *out, void *a, void *b)
{
	if(!out || !a || !b)
		return;

	chiaki_controller_state_or((ChiakiControllerState *)out, (ChiakiControllerState *)a,
			(ChiakiControllerState *)b);
}

CHIAKI_SHIM_API int32_t chiaki_shim_session_set_controller_state(void *session, void *state)
{
	chiaki_shim_session *self = (chiaki_shim_session *)session;
	if(!self || !state)
		return (int32_t)CHIAKI_ERR_INVALID_DATA;

	return (int32_t)chiaki_session_set_controller_state(&self->session, (ChiakiControllerState *)state);
}

CHIAKI_SHIM_API bool chiaki_shim_session_controller_state_matches(void *session, void *state)
{
	chiaki_shim_session *self = (chiaki_shim_session *)session;
	if(!self || !state)
		return false;

	return chiaki_controller_state_equals(&self->session.controller_state,
			(ChiakiControllerState *)state);
}

CHIAKI_SHIM_API uint32_t chiaki_shim_baseline_schema(void)
{
	return (uint32_t)CHIAKI_SESSION_BASELINE_SCHEMA;
}

CHIAKI_SHIM_API int32_t chiaki_shim_baseline_line_max(void)
{
	return (int32_t)CHIAKI_SESSION_BASELINE_LINE_MAX;
}

CHIAKI_SHIM_API void *chiaki_shim_baseline_create(void)
{
	ChiakiSessionBaseline *self = (ChiakiSessionBaseline *)calloc(1, sizeof(ChiakiSessionBaseline));
	if(!self)
		return NULL;

	chiaki_session_baseline_init(self);

	// The two fields whose "never empty" rule lives in their SETTERS rather than in the struct:
	// init is a memset, so both are empty strings until something calls them. The Qt client always
	// does; a managed caller that formats a baseline it never configured would write the two ""
	// rows test/sessionbaseline.c says must not exist. Called with NULL so the library applies its
	// own words - "software" and "unknown" - rather than this file naming them a second time.
	chiaki_session_baseline_set_hw_decoder(self, NULL);
	chiaki_session_baseline_set_renderer(self, NULL);

	return self;
}

CHIAKI_SHIM_API void chiaki_shim_baseline_free(void *baseline)
{
	free(baseline);
}

CHIAKI_SHIM_API void chiaki_shim_baseline_set_started(void *baseline, uint64_t unix_seconds)
{
	if(baseline)
		chiaki_session_baseline_set_started((ChiakiSessionBaseline *)baseline, unix_seconds);
}

CHIAKI_SHIM_API void chiaki_shim_baseline_set_duration_ms(void *baseline, uint64_t duration_ms)
{
	if(baseline)
		((ChiakiSessionBaseline *)baseline)->duration_ms = duration_ms;
}

CHIAKI_SHIM_API void chiaki_shim_baseline_set_app_version(void *baseline, const char *version)
{
	if(baseline && version)
		chiaki_session_baseline_set_app_version((ChiakiSessionBaseline *)baseline, version);
}

CHIAKI_SHIM_API void chiaki_shim_baseline_set_video(
		void *baseline,
		const char *codec,
		uint32_t width,
		uint32_t height,
		uint32_t fps,
		uint32_t bitrate_kbps)
{
	ChiakiSessionBaseline *self = (ChiakiSessionBaseline *)baseline;
	if(!self)
		return;

	if(codec)
		chiaki_session_baseline_set_video_codec(self, codec);
	self->video_width = width;
	self->video_height = height;
	self->video_fps = fps;
	self->bitrate_kbps = bitrate_kbps;
}

CHIAKI_SHIM_API void chiaki_shim_baseline_set_config(
		void *baseline,
		const char *hw_decoder,
		const char *renderer,
		double packet_loss_max,
		bool idr_on_fec_failure)
{
	ChiakiSessionBaseline *self = (ChiakiSessionBaseline *)baseline;
	if(!self)
		return;

	// UNCONDITIONAL, and that is a fix rather than a tidy-up. Both setters substitute a word for
	// a null or empty name - "software" for a decoder, "unknown" for a renderer - because
	// chiaki_session_baseline_init is a memset and the fields are EMPTY STRINGS until a setter
	// runs. Guarding the call on a non-null pointer skipped the substitution and left the "" that
	// test/sessionbaseline.c says a row must never contain.
	chiaki_session_baseline_set_hw_decoder(self, hw_decoder);
	chiaki_session_baseline_set_renderer(self, renderer);
	self->packet_loss_max = packet_loss_max;
	self->idr_on_fec_failure = idr_on_fec_failure;
}

CHIAKI_SHIM_API void chiaki_shim_baseline_set_measured(
		void *baseline,
		double measured_bitrate_mbps,
		double average_packet_loss,
		uint64_t frames_presented,
		uint64_t frames_lost,
		uint64_t frames_dropped,
		uint64_t network_rtt_us)
{
	ChiakiSessionBaseline *self = (ChiakiSessionBaseline *)baseline;
	if(!self)
		return;

	self->measured_bitrate_mbps = measured_bitrate_mbps;
	self->average_packet_loss = average_packet_loss;
	self->frames_presented = frames_presented;
	self->frames_lost = frames_lost;
	self->frames_dropped = frames_dropped;
	self->network_rtt_us = network_rtt_us;
}

CHIAKI_SHIM_API void chiaki_shim_baseline_push_handoff(void *baseline, uint64_t handoff_us)
{
	if(baseline)
		chiaki_session_baseline_push_handoff((ChiakiSessionBaseline *)baseline, handoff_us);
}

CHIAKI_SHIM_API void chiaki_shim_baseline_push_input_to_wire(void *baseline, uint64_t input_us)
{
	if(baseline)
		chiaki_session_baseline_push_input_to_wire((ChiakiSessionBaseline *)baseline, input_us);
}

CHIAKI_SHIM_API uint64_t chiaki_shim_baseline_handoff_avg_us(void *baseline)
{
	return baseline
			? chiaki_session_baseline_handoff_us_avg((const ChiakiSessionBaseline *)baseline)
			: 0;
}

CHIAKI_SHIM_API uint64_t chiaki_shim_baseline_latency_estimate_us(void *baseline)
{
	return baseline
			? chiaki_session_baseline_latency_estimate_us((const ChiakiSessionBaseline *)baseline)
			: 0;
}

CHIAKI_SHIM_API uint64_t chiaki_shim_baseline_decoder_drops(void *baseline)
{
	return baseline
			? chiaki_session_baseline_decoder_drops((const ChiakiSessionBaseline *)baseline)
			: 0;
}

// ---- PP23: the five frame stages ------------------------------------------------------------

/** The selector resolved to the member it names, or NULL - there is deliberately no sixth. */
static ChiakiSessionBaselineStat *chiaki_shim_baseline_stage_of(void *baseline, int32_t stage)
{
	ChiakiSessionBaseline *self = (ChiakiSessionBaseline *)baseline;

	if(!self)
		return NULL;

	switch(stage)
	{
		case CHIAKI_SHIM_BASELINE_STAGE_RECEIVE: return &self->stages.receive;
		case CHIAKI_SHIM_BASELINE_STAGE_REORDER: return &self->stages.reorder;
		case CHIAKI_SHIM_BASELINE_STAGE_REASSEMBLE: return &self->stages.reassemble;
		case CHIAKI_SHIM_BASELINE_STAGE_CORRECT: return &self->stages.correct;
		case CHIAKI_SHIM_BASELINE_STAGE_DECODE: return &self->stages.decode;
		default: return NULL;
	}
}

CHIAKI_SHIM_API void chiaki_shim_baseline_push_stage(
		void *baseline, int32_t stage, uint64_t sample_us)
{
	ChiakiSessionBaselineStat *target = chiaki_shim_baseline_stage_of(baseline, stage);

	// Ignored and not folded into a neighbour: a selector nobody meant is a sample that belongs
	// nowhere, and putting it in the first stage is exactly the mislabelling this exists to stop.
	if(target)
		chiaki_session_baseline_stat_push(target, sample_us);
}

CHIAKI_SHIM_API uint64_t chiaki_shim_baseline_stage_samples(void *baseline, int32_t stage)
{
	const ChiakiSessionBaselineStat *target = chiaki_shim_baseline_stage_of(baseline, stage);
	return target ? target->samples : 0;
}

CHIAKI_SHIM_API uint64_t chiaki_shim_baseline_handoff_samples(void *baseline)
{
	return baseline ? ((const ChiakiSessionBaseline *)baseline)->handoff.samples : 0;
}

// ---- PP23: one statistic on its own, so the percentile is reachable ------------------------

CHIAKI_SHIM_API void *chiaki_shim_baseline_stat_create(void)
{
	// calloc and not malloc: the C's own cases start from a memset(0) stat and every field of it
	// is read before anything is pushed, so an uninitialised histogram answers with whatever was
	// on the heap rather than with the zero the first assertion expects.
	return calloc(1, sizeof(ChiakiSessionBaselineStat));
}

CHIAKI_SHIM_API void chiaki_shim_baseline_stat_free(void *stat)
{
	free(stat);
}

CHIAKI_SHIM_API void chiaki_shim_baseline_stat_push(void *stat, uint64_t sample_us)
{
	if(stat)
		chiaki_session_baseline_stat_push((ChiakiSessionBaselineStat *)stat, sample_us);
}

CHIAKI_SHIM_API uint64_t chiaki_shim_baseline_stat_samples(void *stat)
{
	return stat ? ((const ChiakiSessionBaselineStat *)stat)->samples : 0;
}

CHIAKI_SHIM_API uint64_t chiaki_shim_baseline_stat_min_us(void *stat)
{
	return stat ? ((const ChiakiSessionBaselineStat *)stat)->min_us : 0;
}

CHIAKI_SHIM_API uint64_t chiaki_shim_baseline_stat_max_us(void *stat)
{
	return stat ? ((const ChiakiSessionBaselineStat *)stat)->max_us : 0;
}

CHIAKI_SHIM_API uint64_t chiaki_shim_baseline_stat_avg(void *stat)
{
	return stat ? chiaki_session_baseline_stat_avg((const ChiakiSessionBaselineStat *)stat) : 0;
}

CHIAKI_SHIM_API uint64_t chiaki_shim_baseline_stat_p50_us(void *stat)
{
	return stat ? chiaki_session_baseline_stat_p50_us((const ChiakiSessionBaselineStat *)stat) : 0;
}

CHIAKI_SHIM_API uint64_t chiaki_shim_baseline_stat_p99_us(void *stat)
{
	return stat ? chiaki_session_baseline_stat_p99_us((const ChiakiSessionBaselineStat *)stat) : 0;
}

CHIAKI_SHIM_API uint64_t chiaki_shim_baseline_stat_percentile_us(void *stat, uint32_t percent)
{
	return stat
			? chiaki_session_baseline_stat_percentile_us(
					(const ChiakiSessionBaselineStat *)stat, (unsigned int)percent)
			: 0;
}

CHIAKI_SHIM_API int32_t chiaki_shim_baseline_format(
		void *baseline, char *buf, int32_t buf_size, int32_t *written)
{
	size_t out = 0;
	ChiakiErrorCode err;

	if(written)
		*written = 0;
	if(!baseline || !buf || buf_size <= 0)
		return (int32_t)CHIAKI_ERR_INVALID_DATA;

	err = chiaki_session_baseline_format((const ChiakiSessionBaseline *)baseline, buf,
			(size_t)buf_size, &out);
	if(written)
		*written = (int32_t)out;
	return (int32_t)err;
}

CHIAKI_SHIM_API int32_t chiaki_shim_baseline_append(void *baseline, const char *path)
{
	if(!baseline || !path)
		return (int32_t)CHIAKI_ERR_INVALID_DATA;

	return (int32_t)chiaki_session_baseline_append((const ChiakiSessionBaseline *)baseline, path);
}

CHIAKI_SHIM_API int32_t chiaki_shim_discovery_port(bool ps5)
{
	return ps5 ? CHIAKI_DISCOVERY_PORT_PS5 : CHIAKI_DISCOVERY_PORT_PS4;
}

CHIAKI_SHIM_API const char *chiaki_shim_discovery_protocol_version(bool ps5)
{
	return ps5 ? CHIAKI_DISCOVERY_PROTOCOL_VERSION_PS5 : CHIAKI_DISCOVERY_PROTOCOL_VERSION_PS4;
}

CHIAKI_SHIM_API int32_t chiaki_shim_discovery_local_port_min(void)
{
	return CHIAKI_DISCOVERY_PORT_LOCAL_MIN;
}

CHIAKI_SHIM_API int32_t chiaki_shim_discovery_local_port_max(void)
{
	return CHIAKI_DISCOVERY_PORT_LOCAL_MAX;
}

CHIAKI_SHIM_API int32_t chiaki_shim_discovery_packet_fmt(
		int32_t cmd,
		const char *protocol_version,
		uint64_t user_credential,
		char *buf,
		int32_t buf_size)
{
	ChiakiDiscoveryPacket packet;
	if(!buf || buf_size <= 0)
		return -1;

	packet.cmd = (ChiakiDiscoveryCmd)cmd;
	// The field is a char* rather than a const char* in libchiaki and is only read, so the cast
	// is dropping a const the caller's string never lost.
	packet.protocol_version = (char *)protocol_version;
	packet.user_credential = user_credential;
	return (int32_t)chiaki_discovery_packet_fmt(buf, (size_t)buf_size, &packet);
}

/**
 * Fills only the two fields the classification reads, leaving the rest as a reply never had.
 *
 * PP299: system_version is passed THROUGH, null included. It used to be substituted with "" here,
 * which is PP6 having stepped around chiaki_discovery_host_system_version_target's unguarded atoi
 * without recording it - and the cost of that was not the substitution but the silence: a reply
 * with no system-version header crashed the Qt client, and no test through this port could reach
 * it, because this line answered for the case before the library ever saw it. The library guards
 * it now, so the workaround is what would hide the next regression.
 */
static void chiaki_shim_discovery_host_of(
		ChiakiDiscoveryHost *host, const char *system_version, const char *protocol_version)
{
	memset(host, 0, sizeof(*host));
	host->system_version = system_version;
	host->device_discovery_protocol_version = protocol_version;
}

CHIAKI_SHIM_API bool chiaki_shim_discovery_is_ps5(const char *device_discovery_protocol_version)
{
	ChiakiDiscoveryHost host;
	chiaki_shim_discovery_host_of(&host, "", device_discovery_protocol_version);
	return chiaki_discovery_host_is_ps5(&host);
}

CHIAKI_SHIM_API int32_t chiaki_shim_discovery_target(
		const char *system_version, const char *device_discovery_protocol_version)
{
	ChiakiDiscoveryHost host;
	chiaki_shim_discovery_host_of(&host, system_version, device_discovery_protocol_version);
	return (int32_t)chiaki_discovery_host_system_version_target(&host);
}

CHIAKI_SHIM_API const char *chiaki_shim_discovery_host_state_string(int32_t state)
{
	return chiaki_discovery_host_state_string((ChiakiDiscoveryHostState)state);
}

/**
 * PP6: declared here because libchiaki declares it nowhere.
 *
 * chiaki_discovery_srch_response_parse is CHIAKI_EXPORT in lib/src/discovery.c and appears in no
 * header, so it is reachable and unannounced. Reaching for it anyway is the lesser of two evils:
 * the alternative is a second reply parser on this side of the seam, which is the one piece of
 * discovery a console would have to be present to disprove.
 *
 * What that costs is a signature the compiler cannot check against its definition, which is the
 * same debt PP88's duplicated regex took on. It is paid the same way: the .NET selftest reads
 * lib/src/discovery.c and holds this declaration against the definition there, so a libchiaki that
 * changes the signature turns a test red rather than corrupting a stack quietly.
 *
 * lib/ stays untouched, which is the rule this works around rather than breaks.
 */
ChiakiErrorCode chiaki_discovery_srch_response_parse(ChiakiDiscoveryHost *response, struct sockaddr *addr, char *addr_buf, size_t addr_buf_size, char *buf, size_t buf_size);

/**
 * The parsed host plus the two buffers its strings point into.
 *
 * `reply` is this side's copy of the datagram, because the parse is in place and every string in
 * `host` is an offset into it. `addr` is where sockaddr_str wrote the sender's address, which
 * host_addr points at for the same reason. Both live exactly as long as the handle.
 */
typedef struct chiaki_shim_discovery_reply_t
{
	ChiakiDiscoveryHost host;
	char *reply;
	char addr[64];
} chiaki_shim_discovery_reply;

CHIAKI_SHIM_API void *chiaki_shim_discovery_reply_parse(
		const char *reply, int32_t reply_len, const char *from_addr, int32_t *error_out)
{
	struct sockaddr_in addr;
	chiaki_shim_discovery_reply *self;
	ChiakiErrorCode err;

	if(error_out)
		*error_out = (int32_t)CHIAKI_ERR_INVALID_DATA;
	if(!reply || reply_len <= 0 || !from_addr)
		return NULL;

	memset(&addr, 0, sizeof(addr));
	addr.sin_family = AF_INET;
	if(inet_pton(AF_INET, from_addr, &addr.sin_addr) != 1)
		return NULL;

	self = (chiaki_shim_discovery_reply *)calloc(1, sizeof(chiaki_shim_discovery_reply));
	if(!self)
	{
		if(error_out)
			*error_out = (int32_t)CHIAKI_ERR_MEMORY;
		return NULL;
	}

	// The parser writes NULs into what it is given, so it is given this copy and not the caller's
	// string. One byte over for a terminator the datagram may not carry.
	self->reply = (char *)malloc((size_t)reply_len + 1);
	if(!self->reply)
	{
		free(self);
		if(error_out)
			*error_out = (int32_t)CHIAKI_ERR_MEMORY;
		return NULL;
	}
	memcpy(self->reply, reply, (size_t)reply_len);
	self->reply[reply_len] = '\0';

	err = chiaki_discovery_srch_response_parse(&self->host, (struct sockaddr *)&addr, self->addr,
			sizeof(self->addr), self->reply, (size_t)reply_len);
	if(error_out)
		*error_out = (int32_t)err;

	if(err != CHIAKI_ERR_SUCCESS)
	{
		free(self->reply);
		free(self);
		return NULL;
	}

	return self;
}

CHIAKI_SHIM_API void chiaki_shim_discovery_reply_free(void *host)
{
	chiaki_shim_discovery_reply *self = (chiaki_shim_discovery_reply *)host;
	if(!self)
		return;

	free(self->reply);
	free(self);
}

CHIAKI_SHIM_API int32_t chiaki_shim_discovery_reply_state(void *host)
{
	chiaki_shim_discovery_reply *self = (chiaki_shim_discovery_reply *)host;
	return self ? (int32_t)self->host.state : (int32_t)CHIAKI_DISCOVERY_HOST_STATE_UNKNOWN;
}

CHIAKI_SHIM_API int32_t chiaki_shim_discovery_reply_request_port(void *host)
{
	chiaki_shim_discovery_reply *self = (chiaki_shim_discovery_reply *)host;
	return self ? (int32_t)self->host.host_request_port : 0;
}

CHIAKI_SHIM_API const char *chiaki_shim_discovery_reply_field(void *host, int32_t field)
{
	chiaki_shim_discovery_reply *self = (chiaki_shim_discovery_reply *)host;
	if(!self)
		return NULL;

	switch((ChiakiShimDiscoveryField)field)
	{
		case CHIAKI_SHIM_DISCOVERY_HOST_ADDR:
			return self->host.host_addr;
		case CHIAKI_SHIM_DISCOVERY_SYSTEM_VERSION:
			return self->host.system_version;
		case CHIAKI_SHIM_DISCOVERY_PROTOCOL_VERSION:
			return self->host.device_discovery_protocol_version;
		case CHIAKI_SHIM_DISCOVERY_HOST_NAME:
			return self->host.host_name;
		case CHIAKI_SHIM_DISCOVERY_HOST_TYPE:
			return self->host.host_type;
		case CHIAKI_SHIM_DISCOVERY_HOST_ID:
			return self->host.host_id;
		case CHIAKI_SHIM_DISCOVERY_RUNNING_APP_TITLEID:
			return self->host.running_app_titleid;
		case CHIAKI_SHIM_DISCOVERY_RUNNING_APP_NAME:
			return self->host.running_app_name;
		default:
			return NULL;
	}
}

CHIAKI_SHIM_API int32_t chiaki_shim_rpcrypt_key_size(void)
{
	return (int32_t)CHIAKI_RPCRYPT_KEY_SIZE;
}

CHIAKI_SHIM_API bool chiaki_shim_rpcrypt_bright_ambassador(
		int32_t target,
		uint8_t *bright,
		uint8_t *ambassador,
		const uint8_t *nonce,
		const uint8_t *morning)
{
	if(!bright || !ambassador || !nonce || !morning)
		return false;

	chiaki_rpcrypt_bright_ambassador((ChiakiTarget)target, bright, ambassador, nonce, morning);
	return true;
}

CHIAKI_SHIM_API void *chiaki_shim_rpcrypt_create_auth(
		int32_t target, const uint8_t *nonce, const uint8_t *morning)
{
	ChiakiRPCrypt *self;
	if(!nonce || !morning)
		return NULL;

	self = (ChiakiRPCrypt *)calloc(1, sizeof(ChiakiRPCrypt));
	if(!self)
		return NULL;

	chiaki_rpcrypt_init_auth(self, (ChiakiTarget)target, nonce, morning);
	return self;
}

/**
 * The registration-mode init, which derives from an ambassador and a PIN rather than from a nonce
 * and a morning key. Same struct out, entirely different schedule in - and the four recorded cases
 * for it are the ones the port could not reach until this existed.
 */
CHIAKI_SHIM_API void *chiaki_shim_rpcrypt_create_regist(
		int32_t target, const uint8_t *ambassador, int32_t key_0_off, uint32_t pin)
{
	ChiakiRPCrypt *self;
	if(!ambassador || key_0_off < 0)
		return NULL;

	self = (ChiakiRPCrypt *)calloc(1, sizeof(ChiakiRPCrypt));
	if(!self)
		return NULL;

	if(chiaki_rpcrypt_init_regist(self, (ChiakiTarget)target, ambassador, (size_t)key_0_off, pin)
			!= CHIAKI_ERR_SUCCESS)
	{
		free(self);
		return NULL;
	}
	return self;
}

/**
 * The derived key itself, copied out rather than the struct handed over.
 *
 * test/rpcrypt.c's registration cases assert on rpcrypt.bright directly, so reaching them means
 * reading one field - and reading one field is what an accessor is for. Letting ChiakiRPCrypt
 * cross as a layout would put the offset of `bright` into the managed side's marshalling, where a
 * libchiaki that reorders the struct becomes a wrong answer rather than a build error.
 */
CHIAKI_SHIM_API bool chiaki_shim_rpcrypt_bright(void *rpcrypt, uint8_t *bright_out)
{
	if(!rpcrypt || !bright_out)
		return false;

	memcpy(bright_out, ((ChiakiRPCrypt *)rpcrypt)->bright, CHIAKI_RPCRYPT_KEY_SIZE);
	return true;
}

CHIAKI_SHIM_API void chiaki_shim_rpcrypt_free(void *rpcrypt)
{
	free(rpcrypt);
}

CHIAKI_SHIM_API int32_t chiaki_shim_rpcrypt_generate_iv(void *rpcrypt, uint64_t counter, uint8_t *iv)
{
	if(!rpcrypt || !iv)
		return (int32_t)CHIAKI_ERR_INVALID_DATA;

	return (int32_t)chiaki_rpcrypt_generate_iv((ChiakiRPCrypt *)rpcrypt, iv, counter);
}

CHIAKI_SHIM_API int32_t chiaki_shim_rpcrypt_encrypt(
		void *rpcrypt, uint64_t counter, const uint8_t *in, uint8_t *out, int32_t size)
{
	if(!rpcrypt || !in || !out || size < 0)
		return (int32_t)CHIAKI_ERR_INVALID_DATA;

	return (int32_t)chiaki_rpcrypt_encrypt((ChiakiRPCrypt *)rpcrypt, counter, in, out, (size_t)size);
}

CHIAKI_SHIM_API int32_t chiaki_shim_rpcrypt_decrypt(
		void *rpcrypt, uint64_t counter, const uint8_t *in, uint8_t *out, int32_t size)
{
	if(!rpcrypt || !in || !out || size < 0)
		return (int32_t)CHIAKI_ERR_INVALID_DATA;

	return (int32_t)chiaki_rpcrypt_decrypt((ChiakiRPCrypt *)rpcrypt, counter, in, out, (size_t)size);
}

/* PP696: the frame path's fourteen wrappers, behind the define PP670 put here for this commit.
 *
 * They are ORACLES and nothing else - each exists so PP286 through PP291 could hold a managed port
 * against the C it replaces, and the C they call is fec.c, frameprocessor.c and videoreceiver.c,
 * which have just left the build. An unguarded wrapper here would be an undefined symbol at link.
 *
 * chiaki_shim_has_framepath answers for the build rather than for this text, which is PP661's
 * lesson: the declarations stay in the header inside the same define, so a reader keyed on the file
 * would say "wrapping" of a DLL that exports none of them. Every differential that calls these asks
 * that export first - PP670 converted six test files for exactly this day. */
#ifdef CHIAKI_SHIM_HAVE_FRAMEPATH

CHIAKI_SHIM_API int32_t chiaki_shim_fec_decode(
		uint8_t *frame_buf,
		int32_t unit_size,
		int32_t stride,
		uint32_t k,
		uint32_t m,
		const uint32_t *erasures,
		int32_t erasures_count)
{
	if(!frame_buf || unit_size <= 0 || stride <= 0 || erasures_count < 0)
		return (int32_t)CHIAKI_ERR_INVALID_DATA;
	if(erasures_count > 0 && !erasures)
		return (int32_t)CHIAKI_ERR_INVALID_DATA;

	return (int32_t)chiaki_fec_decode(frame_buf, (size_t)unit_size, (size_t)stride, k, m,
			(const unsigned int *)erasures, (size_t)erasures_count);
}

/* PP286: fec.c's own matrix builder, which chiaki/fec.h does not declare.
 *
 * Declared here rather than including <cauchy.h> so the shim does not grow a build dependency on
 * jerasure's headers for one call - and reached through chiaki-lib's function rather than the
 * vendored one beneath it, because create_matrix is what fec.c actually uses and is therefore what
 * a managed port has to agree with. The buffer is malloc'd by jerasure and freed with free(). */
extern int *create_matrix(unsigned int k, unsigned int m);

CHIAKI_SHIM_API int32_t chiaki_shim_fec_matrix(
		uint32_t k, uint32_t m, int32_t *out_matrix, int32_t capacity)
{
	int *matrix;
	size_t count;
	size_t i;

	if(!out_matrix || k == 0 || m == 0)
		return -1;

	count = (size_t)k * (size_t)m;
	if(capacity < 0 || (size_t)capacity < count)
		return -1;

	matrix = create_matrix(k, m);
	if(!matrix)
		return -1;

	/* Copied element by element rather than memcpy'd: jerasure's matrix is int and the seam's is
	 * int32_t, and on a platform where those differ a memcpy would hand back half a matrix that
	 * still looked like a matrix. */
	for(i = 0; i < count; i++)
		out_matrix[i] = (int32_t)matrix[i];

	free(matrix);
	return (int32_t)count;
}

#endif /* CHIAKI_SHIM_HAVE_FRAMEPATH - the fec pair */

CHIAKI_SHIM_API int32_t chiaki_shim_ecdh_secret_size(void)
{
	return (int32_t)CHIAKI_ECDH_SECRET_SIZE;
}

CHIAKI_SHIM_API void *chiaki_shim_ecdh_create(void)
{
	ChiakiECDH *self = (ChiakiECDH *)calloc(1, sizeof(ChiakiECDH));
	if(!self)
		return NULL;

	if(chiaki_ecdh_init(self) != CHIAKI_ERR_SUCCESS)
	{
		free(self);
		return NULL;
	}
	return self;
}

CHIAKI_SHIM_API void chiaki_shim_ecdh_free(void *ecdh)
{
	if(!ecdh)
		return;

	chiaki_ecdh_fini((ChiakiECDH *)ecdh);
	free(ecdh);
}

CHIAKI_SHIM_API int32_t chiaki_shim_ecdh_set_local_key(
		void *ecdh,
		const uint8_t *private_key, int32_t private_key_size,
		const uint8_t *public_key, int32_t public_key_size)
{
	if(!ecdh || !private_key || !public_key || private_key_size <= 0 || public_key_size <= 0)
		return (int32_t)CHIAKI_ERR_INVALID_DATA;

	return (int32_t)chiaki_ecdh_set_local_key((ChiakiECDH *)ecdh, private_key,
			(size_t)private_key_size, public_key, (size_t)public_key_size);
}

CHIAKI_SHIM_API int32_t chiaki_shim_ecdh_local_pub_key(
		void *ecdh,
		const uint8_t *handshake_key,
		uint8_t *key_out, int32_t *key_out_size,
		uint8_t *sig_out, int32_t *sig_out_size)
{
	size_t key_size;
	size_t sig_size;
	ChiakiErrorCode err;

	if(!ecdh || !handshake_key || !key_out || !key_out_size || !sig_out || !sig_out_size)
		return (int32_t)CHIAKI_ERR_INVALID_DATA;
	if(*key_out_size <= 0 || *sig_out_size <= 0)
		return (int32_t)CHIAKI_ERR_BUF_TOO_SMALL;

	key_size = (size_t)*key_out_size;
	sig_size = (size_t)*sig_out_size;
	err = chiaki_ecdh_get_local_pub_key((ChiakiECDH *)ecdh, key_out, &key_size, handshake_key,
			sig_out, &sig_size);
	*key_out_size = (int32_t)key_size;
	*sig_out_size = (int32_t)sig_size;
	return (int32_t)err;
}

CHIAKI_SHIM_API int32_t chiaki_shim_ecdh_derive_secret(
		void *ecdh,
		uint8_t *secret_out,
		const uint8_t *remote_key, int32_t remote_key_size,
		const uint8_t *handshake_key,
		const uint8_t *remote_sig, int32_t remote_sig_size)
{
	if(!ecdh || !secret_out || !remote_key || !handshake_key || !remote_sig)
		return (int32_t)CHIAKI_ERR_INVALID_DATA;
	if(remote_key_size <= 0 || remote_sig_size <= 0)
		return (int32_t)CHIAKI_ERR_INVALID_DATA;

	return (int32_t)chiaki_ecdh_derive_secret((ChiakiECDH *)ecdh, secret_out, remote_key,
			(size_t)remote_key_size, handshake_key, remote_sig, (size_t)remote_sig_size);
}

CHIAKI_SHIM_API void *chiaki_shim_gkcrypt_create(
		void *log,
		int32_t key_buf_chunks,
		uint8_t index,
		const uint8_t *handshake_key,
		const uint8_t *ecdh_secret)
{
	chiaki_shim_log *log_self = (chiaki_shim_log *)log;
	ChiakiGKCrypt *self;

	if(!handshake_key || !ecdh_secret || key_buf_chunks < 0)
		return NULL;

	self = (ChiakiGKCrypt *)calloc(1, sizeof(ChiakiGKCrypt));
	if(!self)
		return NULL;

	if(chiaki_gkcrypt_init(self, log_self ? &log_self->log : NULL, (size_t)key_buf_chunks, index,
			handshake_key, ecdh_secret) != CHIAKI_ERR_SUCCESS)
	{
		free(self);
		return NULL;
	}
	return self;
}

CHIAKI_SHIM_API void chiaki_shim_gkcrypt_free(void *gkcrypt)
{
	if(!gkcrypt)
		return;

	chiaki_gkcrypt_fini((ChiakiGKCrypt *)gkcrypt);
	free(gkcrypt);
}

CHIAKI_SHIM_API int32_t chiaki_shim_gkcrypt_gen_key_stream(
		void *gkcrypt, uint64_t key_pos, uint8_t *buf, int32_t buf_size)
{
	if(!gkcrypt || !buf || buf_size <= 0)
		return (int32_t)CHIAKI_ERR_INVALID_DATA;

	return (int32_t)chiaki_gkcrypt_gen_key_stream((ChiakiGKCrypt *)gkcrypt, key_pos, buf,
			(size_t)buf_size);
}

/**
 * PP130: the orientation tracker, which turns a pad's raw sensors into what the console is told.
 *
 * A DualSense sends accelerometer and gyroscope samples and the console expects an ORIENTATION -
 * a quaternion - alongside them. The fusion between the two is here rather than in the port
 * because it is a filter with state: each update depends on the last one and on the time between
 * them, so a managed reimplementation would be a second filter that drifts differently, and drift
 * is a picture that slowly tilts rather than an error anyone reports.
 *
 * The tracker and the accel zero are separate handles because they have separate lifetimes: the
 * zero survives a controller being unplugged, since it is the user's calibration and not the
 * device's state.
 */
CHIAKI_SHIM_API void *chiaki_shim_orientation_tracker_create(void)
{
	ChiakiOrientationTracker *self = (ChiakiOrientationTracker *)calloc(1, sizeof(ChiakiOrientationTracker));
	if(!self)
		return NULL;

	chiaki_orientation_tracker_init(self);
	return self;
}

CHIAKI_SHIM_API void chiaki_shim_orientation_tracker_free(void *tracker)
{
	free(tracker);
}

CHIAKI_SHIM_API void *chiaki_shim_accel_new_zero_create(void)
{
	return calloc(1, sizeof(ChiakiAccelNewZero));
}

CHIAKI_SHIM_API void chiaki_shim_accel_new_zero_free(void *accel_zero)
{
	free(accel_zero);
}

CHIAKI_SHIM_API void chiaki_shim_accel_new_zero_set_active(
		void *accel_zero, float accel_x, float accel_y, float accel_z, bool real_accel)
{
	if(!accel_zero)
		return;

	chiaki_accel_new_zero_set_active((ChiakiAccelNewZero *)accel_zero, accel_x, accel_y, accel_z,
			real_accel);
}

CHIAKI_SHIM_API void chiaki_shim_accel_new_zero_set_inactive(void *accel_zero, bool real_accel)
{
	if(!accel_zero)
		return;

	chiaki_accel_new_zero_set_inactive((ChiakiAccelNewZero *)accel_zero, real_accel);
}

/** The timestamp is MICROseconds; SDL reports milliseconds, so the caller multiplies by 1000. */
CHIAKI_SHIM_API void chiaki_shim_orientation_tracker_update(
		void *tracker, float gx, float gy, float gz, float ax, float ay, float az,
		void *accel_zero, bool accel_zero_applied, uint32_t timestamp_us)
{
	if(!tracker)
		return;

	chiaki_orientation_tracker_update((ChiakiOrientationTracker *)tracker, gx, gy, gz, ax, ay, az,
			(ChiakiAccelNewZero *)accel_zero, accel_zero_applied, timestamp_us);
}

/** The orientation a controller state currently carries - the quaternion the console reads. */
CHIAKI_SHIM_API bool chiaki_shim_controller_state_orient(void *state, float *out_orient)
{
	ChiakiControllerState *self = (ChiakiControllerState *)state;
	if(!self || !out_orient)
		return false;

	out_orient[0] = self->orient_x;
	out_orient[1] = self->orient_y;
	out_orient[2] = self->orient_z;
	out_orient[3] = self->orient_w;
	return true;
}

/**
 * PP756: the gyro and accelerometer a controller state carries, which had setters and no reader.
 *
 * chiaki_shim_controller_state_set_motion writes six floats and the orient getter above reads four
 * of the ten back. The other six were write-only, so a managed FeedbackSnapshot built from a state
 * carried zeroes for both - which is not a port of motion control, it is motion control switched
 * off on a path whose symptom is a game that does not tilt.
 *
 * Six out rather than two calls, because they are set together and a caller reading one without
 * the other has half a sample.
 */
CHIAKI_SHIM_API bool chiaki_shim_controller_state_motion(void *state, float *out_motion)
{
	ChiakiControllerState *self = (ChiakiControllerState *)state;
	if(!self || !out_motion)
		return false;

	out_motion[0] = self->gyro_x;
	out_motion[1] = self->gyro_y;
	out_motion[2] = self->gyro_z;
	out_motion[3] = self->accel_x;
	out_motion[4] = self->accel_y;
	out_motion[5] = self->accel_z;
	return true;
}

CHIAKI_SHIM_API void chiaki_shim_orientation_tracker_apply(void *tracker, void *state)
{
	if(!tracker || !state)
		return;

	chiaki_orientation_tracker_apply_to_controller_state((ChiakiOrientationTracker *)tracker,
			(ChiakiControllerState *)state);
}

/** The tracker's current sensors and orientation, flattened out rather than handed over. */
CHIAKI_SHIM_API bool chiaki_shim_orientation_tracker_read(
		void *tracker, float *out_gyro, float *out_accel, float *out_orient, uint32_t *out_timestamp)
{
	ChiakiOrientationTracker *self = (ChiakiOrientationTracker *)tracker;
	if(!self)
		return false;

	if(out_gyro)
	{
		out_gyro[0] = self->gyro_x;
		out_gyro[1] = self->gyro_y;
		out_gyro[2] = self->gyro_z;
	}
	if(out_accel)
	{
		out_accel[0] = self->accel_x;
		out_accel[1] = self->accel_y;
		out_accel[2] = self->accel_z;
	}
	if(out_orient)
	{
		out_orient[0] = self->orient.x;
		out_orient[1] = self->orient.y;
		out_orient[2] = self->orient.z;
		out_orient[3] = self->orient.w;
	}
	if(out_timestamp)
		*out_timestamp = self->timestamp;

	return true;
}

/**
 * PP125: the send buffer, which is what makes takion's retransmission work.
 *
 * Every reliable message the client sends is held here until the console acknowledges it. An ack
 * releases that packet AND every older one, which is the whole of the semantics and the whole of
 * what can go wrong: release too much and a message nobody received is never sent again; release
 * too little and the buffer fills, which the C reports as OVERFLOW and a session then stops
 * sending. Neither says anything about the send buffer when it happens.
 *
 * A NULL takion, as the C's own test passes: the buffer only needs one to retransmit on a timer,
 * and nothing here runs that timer. What is exercised is which packets remain.
 */
CHIAKI_SHIM_API void *chiaki_shim_takion_send_buffer_create(int32_t size)
{
	ChiakiTakionSendBuffer *self;

	if(size <= 0)
		return NULL;

	self = (ChiakiTakionSendBuffer *)calloc(1, sizeof(ChiakiTakionSendBuffer));
	if(!self)
		return NULL;

	if(chiaki_takion_send_buffer_init(self, NULL, (size_t)size) != CHIAKI_ERR_SUCCESS)
	{
		free(self);
		return NULL;
	}
	return self;
}

CHIAKI_SHIM_API void chiaki_shim_takion_send_buffer_free(void *send_buffer)
{
	if(!send_buffer)
		return;

	chiaki_takion_send_buffer_fini((ChiakiTakionSendBuffer *)send_buffer);
	free(send_buffer);
}

/**
 * Pushes a packet of `buf_size` bytes.
 *
 * The payload is allocated HERE because the send buffer takes ownership of it - a managed array
 * handed over would be freed by a C allocator that never allocated it, which is heap corruption
 * rather than an error.
 *
 * And ownership transfers ON FAILURE TOO, which is the opposite of what the shape of the call
 * suggests: chiaki_takion_send_buffer_push frees `buf` itself at its `beach:` label whenever it
 * returns anything but SUCCESS. Freeing it here as well is a double free, and it does not fault
 * where it happens - the first version of this function did exactly that, and the crash landed
 * two tests later in one that never overflows.
 */
CHIAKI_SHIM_API int32_t chiaki_shim_takion_send_buffer_push(
		void *send_buffer, uint32_t seq_num, int32_t buf_size)
{
	uint8_t *buf;

	if(!send_buffer || buf_size <= 0)
		return (int32_t)CHIAKI_ERR_INVALID_DATA;

	buf = (uint8_t *)calloc(1, (size_t)buf_size);
	if(!buf)
		return (int32_t)CHIAKI_ERR_MEMORY;

	return (int32_t)chiaki_takion_send_buffer_push((ChiakiTakionSendBuffer *)send_buffer,
			(ChiakiSeqNum32)seq_num, buf, (size_t)buf_size);
}

CHIAKI_SHIM_API int32_t chiaki_shim_takion_send_buffer_ack(void *send_buffer, uint32_t seq_num)
{
	if(!send_buffer)
		return (int32_t)CHIAKI_ERR_INVALID_DATA;

	return (int32_t)chiaki_takion_send_buffer_ack((ChiakiTakionSendBuffer *)send_buffer,
			(ChiakiSeqNum32)seq_num, NULL, NULL);
}

/**
 * How many packets are still waiting - and only that.
 *
 * Which packets is not askable from here. ChiakiTakionSendBufferPacket is an incomplete type in
 * the public header; its layout lives in takionsendbuffer.c, and the C's own test reaches it by
 * #including that file. The shim cannot: chiaki-lib is already linked in, so including it again
 * is a duplicate symbol, and declaring the layout here instead would be a guess that a field
 * reorder breaks silently - which costs more than knowing which sequence numbers remain is worth.
 *
 * Under the buffer's mutex, as the C's test does by hand and for the same reason: a retransmit
 * thread may be walking the same array while this reads its count.
 */
CHIAKI_SHIM_API int32_t chiaki_shim_takion_send_buffer_count(void *send_buffer)
{
	ChiakiTakionSendBuffer *self = (ChiakiTakionSendBuffer *)send_buffer;
	int32_t count;

	if(!self)
		return -1;
	if(chiaki_mutex_lock(&self->mutex) != CHIAKI_ERR_SUCCESS)
		return -1;

	count = (int32_t)self->packets_count;
	chiaki_mutex_unlock(&self->mutex);
	return count;
}

/**
 * chiaki_takion_format_congestion, with the struct flattened to its three fields.
 *
 * The first thing the port has that goes UPSTREAM. Everything else across this seam reads what a
 * console sent; this is what the client sends back - how many packets it received and how many it
 * lost, which is what the console's bitrate control reacts to. A wrong byte here is not a stream
 * that fails, it is one that quietly degrades, and the recording is fifteen bytes long.
 *
 * Three uint16s rather than a struct pointer, for the reason every builder here takes scalars:
 * fifteen bytes of output with a fixed layout have no argument to lose, and a struct crossing the
 * seam would put its packing into the managed side's marshalling.
 */
CHIAKI_SHIM_API int32_t chiaki_shim_takion_format_congestion(
		uint8_t *buf, int32_t buf_size, uint16_t word_0, uint16_t received, uint16_t lost,
		uint64_t key_pos)
{
	ChiakiTakionCongestionPacket packet;

	if(!buf || buf_size < (int32_t)CHIAKI_TAKION_CONGESTION_PACKET_SIZE)
		return (int32_t)CHIAKI_ERR_BUF_TOO_SMALL;

	packet.word_0 = word_0;
	packet.received = received;
	packet.lost = lost;
	chiaki_takion_format_congestion(buf, &packet, key_pos);
	return (int32_t)CHIAKI_ERR_SUCCESS;
}

/** CHIAKI_TAKION_CONGESTION_PACKET_SIZE, so the managed side holds no second copy of it. */
CHIAKI_SHIM_API int32_t chiaki_shim_takion_congestion_packet_size(void)
{
	return (int32_t)CHIAKI_TAKION_CONGESTION_PACKET_SIZE;
}

/**
 * chiaki_takion_packet_mac, which writes the MAC INTO the packet rather than beside it.
 *
 * That is the part worth carrying across rather than reimplementing: the four bytes go at a fixed
 * offset inside the buffer, over whatever was there, and the MAC is computed with those bytes
 * zeroed. A rewrite that appended it instead produces a packet of the right length that the
 * console silently ignores.
 *
 * Both out-pointers are optional here as they are in the C, and PP124's caller passes neither: what
 * it asserts is the buffer afterwards.
 *
 * PP517: A NULL GKCRYPT IS PASSED THROUGH, and it used to be refused. The C tests `if(crypt)` and
 * does the blanking either way - which is the whole of what PP497 calls the rewrite, and the only
 * half of this function a caller with no key can run. Refusing it here made that path unreachable
 * from managed code, so a model of it could only ever be held against the C's text.
 *
 * The size guard went with it. buf_size <= 0 is a case the C answers itself, with BUF_TOO_SMALL,
 * and returning INVALID_DATA for it here replaced the C's answer with this wrapper's. Only a null
 * buffer is still refused, because that one is a dereference rather than a disagreement.
 */
CHIAKI_SHIM_API int32_t chiaki_shim_takion_packet_mac(
		void *gkcrypt, uint8_t *buf, int32_t buf_size, uint64_t key_pos,
		uint8_t *mac_out, uint8_t *mac_old_out)
{
	if(!buf)
		return (int32_t)CHIAKI_ERR_INVALID_DATA;

	return (int32_t)chiaki_takion_packet_mac((ChiakiGKCrypt *)gkcrypt, buf, (size_t)buf_size,
			key_pos, mac_out, mac_old_out);
}

/**
 * chiaki_gkcrypt_decrypt, in place over the caller's buffer.
 *
 * The last piece the recorded video stream needed. Parsing an AV packet gives a payload and a key
 * position; turning that into the NALU a decoder can read is this, and without it the port could
 * check the header of a real packet and nothing inside it.
 *
 * In place because that is what the C does and what the caller wants: the payload is already a
 * span of the receive buffer, and copying it to decrypt would be a copy per packet on the one
 * path where PP113 measured zero.
 */
CHIAKI_SHIM_API int32_t chiaki_shim_gkcrypt_decrypt(
		void *gkcrypt, uint64_t key_pos, uint8_t *buf, int32_t buf_size)
{
	if(!gkcrypt || !buf || buf_size < 0)
		return (int32_t)CHIAKI_ERR_INVALID_DATA;

	return (int32_t)chiaki_gkcrypt_decrypt((ChiakiGKCrypt *)gkcrypt, key_pos, buf, (size_t)buf_size);
}

/** The block size a caller has to add to a packet's key_pos before decrypting its payload. */
/* PP26: the key and IV inside, for the managed key stream to be compared against. */
CHIAKI_SHIM_API bool chiaki_shim_gkcrypt_key_and_iv(
		void *gkcrypt, uint8_t *out_key_base, uint8_t *out_iv, int32_t capacity)
{
	ChiakiGKCrypt *self = (ChiakiGKCrypt *)gkcrypt;
	if(!self || !out_key_base || !out_iv || capacity < CHIAKI_GKCRYPT_BLOCK_SIZE)
		return false;

	memcpy(out_key_base, self->key_base, CHIAKI_GKCRYPT_BLOCK_SIZE);
	memcpy(out_iv, self->iv, CHIAKI_GKCRYPT_BLOCK_SIZE);
	return true;
}

CHIAKI_SHIM_API int32_t chiaki_shim_gkcrypt_block_size(void)
{
	return (int32_t)CHIAKI_GKCRYPT_BLOCK_SIZE;
}

CHIAKI_SHIM_API void chiaki_shim_gkcrypt_gen_gmac_key(
		uint64_t index, const uint8_t *key_base, const uint8_t *iv, uint8_t *key_out)
{
	if(!key_base || !iv || !key_out)
		return;

	chiaki_gkcrypt_gen_gmac_key(index, key_base, iv, key_out);
}

/**
 * A gkcrypt carrying only what a GMAC needs, which is not a gkcrypt any session produces.
 *
 * test/gkcrypt.c's recorded GMACs are taken against a struct built by hand: zeroed, then the
 * current GMAC key and the IV written straight in, with no key buffer at all. chiaki_gkcrypt_init
 * cannot produce that - it derives both from a handshake key and an ECDH secret it was never
 * given here - so the vector is unreachable through the ordinary constructor.
 *
 * Built on this side because the struct never crosses the seam. The managed half gets a handle
 * and the fields it may set, which is the same rule every other builder here follows; letting
 * ChiakiGKCrypt through as a layout would make the port's marshalling depend on a header it is
 * explicitly not allowed to include.
 */
CHIAKI_SHIM_API void *chiaki_shim_gkcrypt_create_for_gmac(
		const uint8_t *key_gmac_current, const uint8_t *iv)
{
	ChiakiGKCrypt *self;

	if(!key_gmac_current || !iv)
		return NULL;

	self = (ChiakiGKCrypt *)calloc(1, sizeof(ChiakiGKCrypt));
	if(!self)
		return NULL;

	memcpy(self->key_gmac_current, key_gmac_current, sizeof(self->key_gmac_current));
	memcpy(self->iv, iv, sizeof(self->iv));
	self->key_buf = NULL;
	self->key_buf_size = 0;
	self->key_gmac_index_current = 0;
	return self;
}

/**
 * Freed with plain free and not chiaki_gkcrypt_fini: nothing above was initialised through
 * chiaki_gkcrypt_init, so there is no key buffer and no thread for fini to take down, and calling
 * it on a struct it did not build is how a test harness acquires a crash of its own.
 */
CHIAKI_SHIM_API void chiaki_shim_gkcrypt_free_for_gmac(void *gkcrypt)
{
	free(gkcrypt);
}

CHIAKI_SHIM_API int32_t chiaki_shim_gkcrypt_gmac(
		void *gkcrypt, uint64_t key_pos, const uint8_t *buf, int32_t buf_size, uint8_t *gmac_out)
{
	if(!gkcrypt || !buf || buf_size <= 0 || !gmac_out)
		return (int32_t)CHIAKI_ERR_INVALID_DATA;

	return (int32_t)chiaki_gkcrypt_gmac((ChiakiGKCrypt *)gkcrypt, key_pos, buf, (size_t)buf_size,
			gmac_out);
}

CHIAKI_SHIM_API int32_t chiaki_shim_gkcrypt_gmac_size(void)
{
	return (int32_t)CHIAKI_GKCRYPT_GMAC_SIZE;
}

CHIAKI_SHIM_API bool chiaki_shim_seq_num_16_lt(uint16_t a, uint16_t b)
{
	return chiaki_seq_num_16_lt(a, b);
}

CHIAKI_SHIM_API bool chiaki_shim_seq_num_16_gt(uint16_t a, uint16_t b)
{
	return chiaki_seq_num_16_gt(a, b);
}

CHIAKI_SHIM_API bool chiaki_shim_seq_num_32_lt(uint32_t a, uint32_t b)
{
	return chiaki_seq_num_32_lt(a, b);
}

CHIAKI_SHIM_API bool chiaki_shim_seq_num_32_gt(uint32_t a, uint32_t b)
{
	return chiaki_seq_num_32_gt(a, b);
}

/** As with the log and the session: the library's struct first, the callback beside it. */
typedef struct chiaki_shim_reorder_queue_t
{
	ChiakiReorderQueue queue;
	ChiakiShimReorderDropCb cb;
	void *user;
} chiaki_shim_reorder_queue;

static void chiaki_shim_reorder_drop(uint64_t seq_num, void *elem_user, void *cb_user)
{
	chiaki_shim_reorder_queue *self = (chiaki_shim_reorder_queue *)cb_user;
	if(self && self->cb)
		self->cb(seq_num, elem_user, self->user);
}

CHIAKI_SHIM_API void *chiaki_shim_reorder_queue_create_16(
		int32_t size_exp, uint16_t seq_num_start, ChiakiShimReorderDropCb cb, void *user)
{
	chiaki_shim_reorder_queue *self;
	if(size_exp < 0)
		return NULL;

	self = (chiaki_shim_reorder_queue *)calloc(1, sizeof(chiaki_shim_reorder_queue));
	if(!self)
		return NULL;

	if(chiaki_reorder_queue_init_16(&self->queue, (size_t)size_exp, seq_num_start)
			!= CHIAKI_ERR_SUCCESS)
	{
		free(self);
		return NULL;
	}

	self->cb = cb;
	self->user = user;
	chiaki_reorder_queue_set_drop_cb(&self->queue, cb ? chiaki_shim_reorder_drop : NULL, self);
	return self;
}

/*
 * PP674: the other instantiation, which takion's DATA queue is.
 *
 * reorderqueue.c stamps one body out twice through REORDER_QUEUE_INIT, and the two differ only in
 * the three sequence functions injected - add, gt, lt at one width or the other. takion uses both:
 * the video queue is the sixteen-bit one and the data queue the thirty-two-bit one, seeded with
 * tag_remote. Only the sixteen-bit init had a wrapper, so a managed queue had nothing to be held
 * against at the width the data path actually uses.
 *
 * Everything below this - free, size, count, push, pull, peek, drop, the strategy - is width-blind
 * and already takes the handle, so this adds one entry point and no second family.
 */
CHIAKI_SHIM_API void *chiaki_shim_reorder_queue_create_32(
		int32_t size_exp, uint32_t seq_num_start, ChiakiShimReorderDropCb cb, void *user)
{
	chiaki_shim_reorder_queue *self;
	if(size_exp < 0)
		return NULL;

	self = (chiaki_shim_reorder_queue *)calloc(1, sizeof(chiaki_shim_reorder_queue));
	if(!self)
		return NULL;

	if(chiaki_reorder_queue_init_32(&self->queue, (size_t)size_exp, seq_num_start)
			!= CHIAKI_ERR_SUCCESS)
	{
		free(self);
		return NULL;
	}

	self->cb = cb;
	self->user = user;
	chiaki_reorder_queue_set_drop_cb(&self->queue, cb ? chiaki_shim_reorder_drop : NULL, self);
	return self;
}

CHIAKI_SHIM_API void chiaki_shim_reorder_queue_free(void *queue)
{
	chiaki_shim_reorder_queue *self = (chiaki_shim_reorder_queue *)queue;
	if(!self)
		return;

	// Cleared first, because fini drops what is still queued and every one of those is a callback
	// into managed code that is about to stop being interested.
	self->cb = NULL;
	chiaki_reorder_queue_set_drop_cb(&self->queue, NULL, NULL);
	chiaki_reorder_queue_fini(&self->queue);
	free(self);
}

CHIAKI_SHIM_API void chiaki_shim_reorder_queue_set_drop_strategy(void *queue, int32_t strategy)
{
	chiaki_shim_reorder_queue *self = (chiaki_shim_reorder_queue *)queue;
	if(self)
		chiaki_reorder_queue_set_drop_strategy(&self->queue,
				(ChiakiReorderQueueDropStrategy)strategy);
}

CHIAKI_SHIM_API int32_t chiaki_shim_reorder_queue_size(void *queue)
{
	chiaki_shim_reorder_queue *self = (chiaki_shim_reorder_queue *)queue;
	return self ? (int32_t)chiaki_reorder_queue_size(&self->queue) : 0;
}

CHIAKI_SHIM_API uint64_t chiaki_shim_reorder_queue_count(void *queue)
{
	chiaki_shim_reorder_queue *self = (chiaki_shim_reorder_queue *)queue;
	return self ? chiaki_reorder_queue_count(&self->queue) : 0;
}

CHIAKI_SHIM_API void chiaki_shim_reorder_queue_push(void *queue, uint64_t seq_num, void *elem_user)
{
	chiaki_shim_reorder_queue *self = (chiaki_shim_reorder_queue *)queue;
	if(self)
		chiaki_reorder_queue_push(&self->queue, seq_num, elem_user);
}

CHIAKI_SHIM_API bool chiaki_shim_reorder_queue_pull(
		void *queue, uint64_t *seq_num, void **elem_user)
{
	chiaki_shim_reorder_queue *self = (chiaki_shim_reorder_queue *)queue;
	if(!self || !seq_num || !elem_user)
		return false;

	return chiaki_reorder_queue_pull(&self->queue, seq_num, elem_user);
}

CHIAKI_SHIM_API bool chiaki_shim_reorder_queue_peek(
		void *queue, uint64_t index, uint64_t *seq_num, void **elem_user)
{
	chiaki_shim_reorder_queue *self = (chiaki_shim_reorder_queue *)queue;
	if(!self || !seq_num || !elem_user)
		return false;

	return chiaki_reorder_queue_peek(&self->queue, index, seq_num, elem_user);
}

CHIAKI_SHIM_API void chiaki_shim_reorder_queue_drop(void *queue, uint64_t index)
{
	chiaki_shim_reorder_queue *self = (chiaki_shim_reorder_queue *)queue;
	if(self)
		chiaki_reorder_queue_drop(&self->queue, index);
}

/** The parsed response plus the copy of the text its keys and values point into. */
typedef struct chiaki_shim_http_t
{
	ChiakiHttpResponse response;
	char *text;
} chiaki_shim_http;

CHIAKI_SHIM_API void *chiaki_shim_http_parse(
		const char *text, int32_t len, int32_t *code_out, int32_t *error_out)
{
	chiaki_shim_http *self;
	ChiakiErrorCode err;

	if(code_out)
		*code_out = 0;
	if(error_out)
		*error_out = (int32_t)CHIAKI_ERR_INVALID_DATA;
	if(!text || len <= 0)
		return NULL;

	self = (chiaki_shim_http *)calloc(1, sizeof(chiaki_shim_http));
	if(!self)
		return NULL;

	self->text = (char *)malloc((size_t)len + 1);
	if(!self->text)
	{
		free(self);
		return NULL;
	}
	memcpy(self->text, text, (size_t)len);
	self->text[len] = '\0';

	err = chiaki_http_response_parse(&self->response, self->text, (size_t)len);
	if(error_out)
		*error_out = (int32_t)err;

	if(err != CHIAKI_ERR_SUCCESS)
	{
		free(self->text);
		free(self);
		return NULL;
	}

	if(code_out)
		*code_out = self->response.code;
	return self;
}

CHIAKI_SHIM_API void chiaki_shim_http_free(void *response)
{
	chiaki_shim_http *self = (chiaki_shim_http *)response;
	if(!self)
		return;

	chiaki_http_response_fini(&self->response);
	free(self->text);
	free(self);
}

static ChiakiHttpHeader *chiaki_shim_http_at(chiaki_shim_http *self, int32_t index)
{
	ChiakiHttpHeader *header;
	if(!self || index < 0)
		return NULL;

	for(header = self->response.headers; header; header = header->next)
	{
		if(index-- == 0)
			return header;
	}
	return NULL;
}

CHIAKI_SHIM_API int32_t chiaki_shim_http_header_count(void *response)
{
	chiaki_shim_http *self = (chiaki_shim_http *)response;
	ChiakiHttpHeader *header;
	int32_t count = 0;

	if(!self)
		return 0;

	for(header = self->response.headers; header; header = header->next)
		count++;
	return count;
}

CHIAKI_SHIM_API const char *chiaki_shim_http_header_key(void *response, int32_t index)
{
	ChiakiHttpHeader *header = chiaki_shim_http_at((chiaki_shim_http *)response, index);
	return header ? header->key : NULL;
}

CHIAKI_SHIM_API const char *chiaki_shim_http_header_value(void *response, int32_t index)
{
	ChiakiHttpHeader *header = chiaki_shim_http_at((chiaki_shim_http *)response, index);
	return header ? header->value : NULL;
}


CHIAKI_SHIM_API void *chiaki_shim_bitstream_create(int32_t codec)
{
	ChiakiBitstream *self = (ChiakiBitstream *)calloc(1, sizeof(ChiakiBitstream));
	if(!self)
		return NULL;

	chiaki_bitstream_init(self, NULL, (ChiakiCodec)codec);
	return self;
}

CHIAKI_SHIM_API void chiaki_shim_bitstream_free(void *bitstream)
{
	free(bitstream);
}

CHIAKI_SHIM_API bool chiaki_shim_bitstream_header(void *bitstream, uint8_t *data, int32_t size)
{
	if(!bitstream || !data || size <= 0)
		return false;

	return chiaki_bitstream_header((ChiakiBitstream *)bitstream, data, (unsigned)size);
}

CHIAKI_SHIM_API bool chiaki_shim_bitstream_slice(
		void *bitstream, uint8_t *data, int32_t size,
		int32_t *slice_type, uint32_t *reference_frame)
{
	ChiakiBitstreamSlice slice;
	bool ok;

	if(slice_type)
		*slice_type = 0;
	if(reference_frame)
		*reference_frame = 0;
	if(!bitstream || !data || size <= 0)
		return false;

	memset(&slice, 0, sizeof(slice));
	ok = chiaki_bitstream_slice((ChiakiBitstream *)bitstream, data, (unsigned)size, &slice);
	if(!ok)
		return false;

	if(slice_type)
		*slice_type = (int32_t)slice.slice_type;
	if(reference_frame)
		*reference_frame = (uint32_t)slice.reference_frame;
	return true;
}

CHIAKI_SHIM_API bool chiaki_shim_bitstream_slice_set_reference_frame(
		void *bitstream, uint8_t *data, int32_t size, uint32_t reference_frame)
{
	if(!bitstream || !data || size <= 0)
		return false;

	return chiaki_bitstream_slice_set_reference_frame((ChiakiBitstream *)bitstream, data,
			(unsigned)size, (unsigned)reference_frame);
}

CHIAKI_SHIM_API void *chiaki_shim_key_state_create(void)
{
	ChiakiKeyState *self = (ChiakiKeyState *)calloc(1, sizeof(ChiakiKeyState));
	if(self)
		chiaki_key_state_init(self);
	return self;
}

CHIAKI_SHIM_API void chiaki_shim_key_state_free(void *state)
{
	free(state);
}

CHIAKI_SHIM_API uint64_t chiaki_shim_key_state_request_pos(void *state, uint32_t low, bool commit)
{
	return state ? chiaki_key_state_request_pos((ChiakiKeyState *)state, low, commit) : 0;
}

CHIAKI_SHIM_API int32_t chiaki_shim_takion_v9_av_packet_parse(
		void *key_state,
		uint8_t *buf,
		int32_t buf_size,
		bool *is_video,
		uint16_t *packet_index,
		uint16_t *frame_index,
		uint16_t *unit_index,
		uint16_t *units_in_frame_total,
		uint16_t *units_in_frame_fec,
		uint8_t *codec,
		uint8_t *adaptive_stream_index,
		uint64_t *key_pos,
		int32_t *data_offset,
		int32_t *data_size)
{
	ChiakiTakionAVPacket packet;
	ChiakiErrorCode err;

	if(!key_state || !buf || buf_size <= 0)
		return (int32_t)CHIAKI_ERR_INVALID_DATA;

	memset(&packet, 0, sizeof(packet));
	err = chiaki_takion_v9_av_packet_parse(&packet, (ChiakiKeyState *)key_state, buf,
			(size_t)buf_size);
	if(err != CHIAKI_ERR_SUCCESS)
		return (int32_t)err;

	if(is_video)
		*is_video = packet.is_video;
	if(packet_index)
		*packet_index = packet.packet_index;
	if(frame_index)
		*frame_index = packet.frame_index;
	if(unit_index)
		*unit_index = packet.unit_index;
	if(units_in_frame_total)
		*units_in_frame_total = packet.units_in_frame_total;
	if(units_in_frame_fec)
		*units_in_frame_fec = packet.units_in_frame_fec;
	if(codec)
		*codec = packet.codec;
	if(adaptive_stream_index)
		*adaptive_stream_index = packet.adaptive_stream_index;
	if(key_pos)
		*key_pos = packet.key_pos;

	// The payload is a pointer INTO the caller's buffer, so it crosses as the offset it sits at.
	// A pointer would be a second lifetime for the managed side to keep track of, and the buffer
	// it points into is one the caller already holds.
	if(data_offset)
		*data_offset = packet.data ? (int32_t)(packet.data - buf) : -1;
	if(data_size)
		*data_size = (int32_t)packet.data_size;

	return (int32_t)CHIAKI_ERR_SUCCESS;
}

#ifdef CHIAKI_SHIM_HAVE_OPUS
/* PP694: opusencoder.c's half of libopus, as the oracle a managed encoder is held to.
 *
 * NOT chiaki_opus_encoder_frame ITSELF, and the reason is a dependency rather than a preference:
 * that function needs an audio sender, which needs a ChiakiSession, which needs a console. What it
 * DOES to a frame is opus_encode with the module's own two parameters - the application mode it
 * picks and the forty-byte buffer it insists on - and those run with nothing behind them.
 *
 * So the parameters cross rather than being written down on the managed side. The application is
 * this export; the forty is read out of opusencoder.c by a source model, because it is a literal in
 * that file and no header publishes it.
 */
CHIAKI_SHIM_API int32_t chiaki_shim_opus_encoder_application(void)
{
	return OPUS_APPLICATION_RESTRICTED_LOWDELAY;
}

CHIAKI_SHIM_API void *chiaki_shim_opus_encoder_create(
		int32_t rate, int32_t channels, int32_t *error_out)
{
	int error = 0;
	OpusEncoder *encoder = opus_encoder_create(
			(opus_int32)rate, (int)channels, OPUS_APPLICATION_RESTRICTED_LOWDELAY, &error);

	if(error_out)
		*error_out = (int32_t)error;

	if(error != OPUS_OK)
	{
		/* opus_encoder_create allocates before it validates, so a refused configuration still
		 * leaves a pointer the caller has to free - which the C's own error path does too. */
		if(encoder)
			opus_encoder_destroy(encoder);
		return NULL;
	}

	return encoder;
}

CHIAKI_SHIM_API void chiaki_shim_opus_encoder_destroy(void *encoder)
{
	if(encoder)
		opus_encoder_destroy((OpusEncoder *)encoder);
}

/* opus_encode, with the return code handed back unchanged: below one is an error and anything
 * that is not the buffer's own size is what opusencoder.c drops as a protocol violation, so the
 * managed side has to see the number rather than a success flag. */
CHIAKI_SHIM_API int32_t chiaki_shim_opus_encode(
		void *encoder, const int16_t *pcm, int32_t frame_size, uint8_t *out, int32_t out_size)
{
	if(!encoder || !pcm || !out || frame_size <= 0 || out_size <= 0)
		return (int32_t)OPUS_BAD_ARG;

	return (int32_t)opus_encode((OpusEncoder *)encoder, (const opus_int16 *)pcm, (int)frame_size,
			out, (opus_int32)out_size);
}

/* PP751: the decoder's four, mirroring the encoder's above.
 *
 * opusdecoder.c creates its decoder from the STREAMINFO's rate and channels and rebuilds it every
 * time one arrives, so the create takes both rather than reading a header the managed side owns.
 */
CHIAKI_SHIM_API void *chiaki_shim_opus_decoder_create(
		int32_t rate, int32_t channels, int32_t *error_out)
{
	int error = 0;
	OpusDecoder *decoder = opus_decoder_create((opus_int32)rate, (int)channels, &error);

	if(error_out)
		*error_out = (int32_t)error;

	if(error != OPUS_OK)
	{
		/* Same shape as the encoder's: a refused configuration can still leave a pointer. */
		if(decoder)
			opus_decoder_destroy(decoder);
		return NULL;
	}

	return decoder;
}

CHIAKI_SHIM_API void chiaki_shim_opus_decoder_destroy(void *decoder)
{
	if(decoder)
		opus_decoder_destroy((OpusDecoder *)decoder);
}

/* opus_decode, with the return code handed back unchanged: it is the SAMPLE COUNT per channel and
 * the C treats anything below one as an error.
 *
 * A NULL data pointer is not a mistake here - it is Opus's packet loss concealment, and it is what
 * opusdecoder.c passes when audioreceiver.c hands it a frame with no buffer. So the size being
 * zero has to reach opus_decode as NULL rather than as an empty buffer, which is a different call.
 */
CHIAKI_SHIM_API int32_t chiaki_shim_opus_decode(
		void *decoder, const uint8_t *data, int32_t size, int16_t *pcm, int32_t frame_size)
{
	if(!decoder || !pcm || frame_size <= 0 || size < 0)
		return (int32_t)OPUS_BAD_ARG;

	return (int32_t)opus_decode((OpusDecoder *)decoder, size ? data : NULL, (opus_int32)size,
			(opus_int16 *)pcm, (int)frame_size, 0);
}
#endif

/* PP753: the stream handover. Two bool-pred conds, one each way, and a copied reason.
 *
 * ChiakiBoolPredCond is exactly this shape already - a flag, a mutex and a condition, with a
 * timed wait that re-checks the flag - so nothing here invents a primitive libchiaki has. */
typedef struct chiaki_shim_stream_handover_t
{
	ChiakiBoolPredCond started;
	ChiakiBoolPredCond finished;
	int32_t error;
	char *reason;
	/* PP769: the socket session.c hands the run, which the managed takion adopts rather than
	 * opening its own. Kept as the value the callback was given: the session owns it and frees it
	 * after the run, so nothing here closes it. */
	int64_t data_sock;
	/* PP696: set by the stop trampoline, read by the run's wait loop. A plain bool rather than a
	 * third condition: the loop is already waking every slice, so nothing needs to be signalled -
	 * and a stop that arrives between two slices is acted on at the next one either way. */
	volatile bool stopped;
} ChiakiShimStreamHandover;

/* PP759: one slice of the run's wait. The export refuses a negative timeout, so "until it is over"
 * is not something it can be asked - what makes this correct is the loop, not the number. */
#define CHIAKI_SHIM_STREAM_RUN_SLICE_MS 1000

CHIAKI_SHIM_API void *chiaki_shim_stream_handover_create(void)
{
	ChiakiShimStreamHandover *self = calloc(1, sizeof(ChiakiShimStreamHandover));
	if(!self)
		return NULL;

	/* PP769: -1 and not the zero calloc leaves. Zero is a socket handle a caller could believe in,
	 * and the far side has to tell "the session handed one" from "nobody has yet" - a run that
	 * adopted handle zero would ask the runtime about something that is not a socket. */
	self->data_sock = -1;

	if(chiaki_bool_pred_cond_init(&self->started) != CHIAKI_ERR_SUCCESS)
	{
		free(self);
		return NULL;
	}

	if(chiaki_bool_pred_cond_init(&self->finished) != CHIAKI_ERR_SUCCESS)
	{
		chiaki_bool_pred_cond_fini(&self->started);
		free(self);
		return NULL;
	}

	self->error = (int32_t)CHIAKI_ERR_UNKNOWN;
	return self;
}

CHIAKI_SHIM_API void chiaki_shim_stream_handover_free(void *handover)
{
	ChiakiShimStreamHandover *self = (ChiakiShimStreamHandover *)handover;
	if(!self)
		return;

	chiaki_bool_pred_cond_fini(&self->finished);
	chiaki_bool_pred_cond_fini(&self->started);
	free(self->reason);
	free(self);
}

CHIAKI_SHIM_API int32_t chiaki_shim_stream_handover_start(void *handover)
{
	ChiakiShimStreamHandover *self = (ChiakiShimStreamHandover *)handover;
	if(!self)
		return (int32_t)CHIAKI_ERR_INVALID_DATA;

	if(chiaki_bool_pred_cond_lock(&self->started) != CHIAKI_ERR_SUCCESS)
		return (int32_t)CHIAKI_ERR_UNKNOWN;

	self->started.pred = true;
	chiaki_bool_pred_cond_signal(&self->started);
	chiaki_bool_pred_cond_unlock(&self->started);
	return (int32_t)CHIAKI_ERR_SUCCESS;
}

CHIAKI_SHIM_API bool chiaki_shim_stream_handover_await_start(void *handover, int32_t timeout_ms)
{
	ChiakiShimStreamHandover *self = (ChiakiShimStreamHandover *)handover;
	bool started;

	if(!self || timeout_ms < 0)
		return false;

	if(chiaki_bool_pred_cond_lock(&self->started) != CHIAKI_ERR_SUCCESS)
		return false;

	/* The flag is re-checked rather than trusted: a wait that returns has said nothing until it
	 * is read, which is the same rule the session's own predicates follow. */
	if(!self->started.pred)
		chiaki_bool_pred_cond_timedwait(&self->started, (uint64_t)timeout_ms);

	started = self->started.pred;
	chiaki_bool_pred_cond_unlock(&self->started);
	return started;
}

CHIAKI_SHIM_API int32_t chiaki_shim_stream_handover_finish(
		void *handover, int32_t error, const char *reason)
{
	ChiakiShimStreamHandover *self = (ChiakiShimStreamHandover *)handover;
	char *copy = NULL;

	if(!self)
		return (int32_t)CHIAKI_ERR_INVALID_DATA;

	/* Copied before the lock is taken: a strdup under it would hold the session thread for an
	 * allocation, and a failed one still has to leave the handover consistent. */
	if(reason)
	{
		copy = strdup(reason);
		if(!copy)
			return (int32_t)CHIAKI_ERR_MEMORY;
	}

	if(chiaki_bool_pred_cond_lock(&self->finished) != CHIAKI_ERR_SUCCESS)
	{
		free(copy);
		return (int32_t)CHIAKI_ERR_UNKNOWN;
	}

	free(self->reason);
	self->reason = copy;
	self->error = error;
	self->finished.pred = true;

	chiaki_bool_pred_cond_signal(&self->finished);
	chiaki_bool_pred_cond_unlock(&self->finished);
	return (int32_t)CHIAKI_ERR_SUCCESS;
}

CHIAKI_SHIM_API int32_t chiaki_shim_stream_handover_await_finish(void *handover, int32_t timeout_ms)
{
	ChiakiShimStreamHandover *self = (ChiakiShimStreamHandover *)handover;
	int32_t error;

	if(!self || timeout_ms < 0)
		return (int32_t)CHIAKI_ERR_INVALID_DATA;

	if(chiaki_bool_pred_cond_lock(&self->finished) != CHIAKI_ERR_SUCCESS)
		return (int32_t)CHIAKI_ERR_UNKNOWN;

	if(!self->finished.pred)
		chiaki_bool_pred_cond_timedwait(&self->finished, (uint64_t)timeout_ms);

	/* A wait that ran out answers TIMEOUT rather than the error it was initialised with: the
	 * session thread has to tell "the run failed" from "the run never reported". */
	error = self->finished.pred ? self->error : (int32_t)CHIAKI_ERR_TIMEOUT;
	chiaki_bool_pred_cond_unlock(&self->finished);
	return error;
}

CHIAKI_SHIM_API const char *chiaki_shim_stream_handover_reason(void *handover)
{
	ChiakiShimStreamHandover *self = (ChiakiShimStreamHandover *)handover;

	return self ? self->reason : NULL;
}

/* PP696, to PP759's contract: the trampoline the C session runs the stream phase through.
 *
 * THE TRAMPOLINE IS C, as every one of libchiaki's callbacks is. A managed delegate installed here
 * would be a function pointer the collector may move, on a thread the CLR never created.
 *
 * THE WAIT IS A LOOP because chiaki_shim_stream_handover_await_finish refuses a negative timeout
 * and a session lasts as long as somebody plays. A single wait would end the stream at whatever
 * number was chosen; the slices are what make a stop act promptly without a busy loop.
 *
 * The reason is borrowed, which is the whole of the ownership rule: it is handed back as a pointer
 * into the handover, the session copies it with strdup, and the handover frees its own on free. A
 * trampoline that strdup'd here would leak one string for every session that ends this way. */
static ChiakiErrorCode chiaki_shim_stream_run_trampoline(
		chiaki_socket_t *data_sock, const char **disconnect_reason, void *user)
{
	ChiakiShimStreamHandover *self = (ChiakiShimStreamHandover *)user;
	int32_t err;

	if(!self)
		return CHIAKI_ERR_INVALID_DATA;

	/* PP769: THE SOCKET IS READ, and PP759 said it would not be. That contract reasoned the managed
	 * runner opens its own - which it did, and a second conversation on the well-known port is not
	 * the one the console is in the middle of. Recorded before the start so the far side finds it
	 * already there when its wait returns. */
	self->data_sock = data_sock ? (int64_t)*data_sock : -1;

	err = chiaki_shim_stream_handover_start(self);
	if(err != (int32_t)CHIAKI_ERR_SUCCESS)
		return (ChiakiErrorCode)err;

	do
	{
		err = chiaki_shim_stream_handover_await_finish(self, CHIAKI_SHIM_STREAM_RUN_SLICE_MS);
	}
	while(err == (int32_t)CHIAKI_ERR_TIMEOUT && !self->stopped);

	if(disconnect_reason)
		*disconnect_reason = self->reason;

	return (ChiakiErrorCode)err;
}

/* And the stop, which is chiaki_session_stop's fourth wake-up on this side of the handover. */
static void chiaki_shim_stream_stop_trampoline(void *user)
{
	ChiakiShimStreamHandover *self = (ChiakiShimStreamHandover *)user;

	if(self)
		self->stopped = true;
}

/* PP766: the three things a managed BIG needs out of a live session.
 *
 * PP765 measured the eleven parts a run host takes and found ten compose from work that shipped.
 * The eleventh is the BIG - the message that STARTS a stream - and BigMessage.Encode wants five
 * arguments, three of which belong to the C: the session id ctrl's handshake produced, the mtu and
 * round trip senkusha measured, and the ecdh public key with its signature.
 *
 * READERS AND NOT A STRUCT, which is PP4's rule at this seam: the session is twenty-odd fields and
 * an ECDH, and marshalling its layout would make an offset that is wrong by two bytes surface as a
 * console refusing a stream for no stated reason.
 *
 * AND THE ECDH IS COPIED, NOT POINTED AT. session.c creates the pair on the line before the run and
 * frees it on the line after, so a reader handing back a pointer would hand back something freed a
 * step later. chiaki_ecdh_get_local_pub_key writes into buffers the caller owns, which is what the
 * C's own send_big does with two stack arrays - so this does the same and copies out. */
CHIAKI_SHIM_API bool chiaki_shim_session_id(void *session, char *out, int32_t capacity)
{
	ChiakiSession *self = (ChiakiSession *)session;
	size_t len;

	if(!self || !out || capacity <= 0)
		return false;

	/* The field is zero-terminated by the C's own contract, and the bound is this side's: a session
	 * id longer than the caller's buffer is a refusal rather than a truncation somebody reads as an
	 * id. */
	len = strlen(self->session_id);
	if(len + 1 > (size_t)capacity)
		return false;

	memcpy(out, self->session_id, len + 1);
	return true;
}

CHIAKI_SHIM_API bool chiaki_shim_session_transport(
		void *session, uint32_t *out_mtu_in, uint32_t *out_mtu_out, uint64_t *out_rtt_us)
{
	ChiakiSession *self = (ChiakiSession *)session;

	if(!self || !out_mtu_in || !out_mtu_out || !out_rtt_us)
		return false;

	/* All three or none: the launch spec spends them together, and a caller that got two would
	 * build a spec describing a link nobody measured. */
	*out_mtu_in = self->mtu_in;
	*out_mtu_out = self->mtu_out;
	*out_rtt_us = self->rtt_us;
	return true;
}

/* And the handshake key, which is the fourth and was not in this task's first reading.
 *
 * It signs the ecdh material above AND goes into the launch spec's JSON, base64'd - so the managed
 * side needs it in its own right rather than only inside the signature. Sixteen bytes, and the
 * caller's buffer is checked against that rather than trusted: a short read here would base64 to a
 * shorter string and the console would refuse a spec for a reason nothing states. */
CHIAKI_SHIM_API bool chiaki_shim_session_handshake_key(void *session, uint8_t *out, int32_t capacity)
{
	ChiakiSession *self = (ChiakiSession *)session;

	if(!self || !out || capacity < CHIAKI_HANDSHAKE_KEY_SIZE)
		return false;

	memcpy(out, self->handshake_key, CHIAKI_HANDSHAKE_KEY_SIZE);
	return true;
}

CHIAKI_SHIM_API bool chiaki_shim_session_ecdh_material(
		void *session,
		uint8_t *out_pub_key, int32_t *pub_key_size,
		uint8_t *out_sig, int32_t *sig_size)
{
	ChiakiSession *self = (ChiakiSession *)session;
	size_t pub = 0, sig = 0;

	if(!self || !out_pub_key || !pub_key_size || !out_sig || !sig_size)
		return false;

	if(*pub_key_size <= 0 || *sig_size <= 0)
		return false;

	pub = (size_t)*pub_key_size;
	sig = (size_t)*sig_size;

	/* The signature is over the handshake key, which is the session's own and is why this takes a
	 * session rather than an ecdh: the two are only meaningful together, and a caller holding one
	 * without the other would sign with something the console never agreed to. */
	if(chiaki_ecdh_get_local_pub_key(
			&self->ecdh, out_pub_key, &pub, self->handshake_key, out_sig, &sig)
		!= CHIAKI_ERR_SUCCESS)
	{
		return false;
	}

	*pub_key_size = (int32_t)pub;
	*sig_size = (int32_t)sig;
	return true;
}

CHIAKI_SHIM_API bool chiaki_shim_session_derive_secret(
		void *session,
		const uint8_t *remote_key, int32_t remote_key_size,
		const uint8_t *remote_sig, int32_t remote_sig_size,
		uint8_t *out_secret, int32_t secret_capacity)
{
	ChiakiSession *self = (ChiakiSession *)session;

	if(!self || !remote_key || !remote_sig || !out_secret)
		return false;

	if(remote_key_size <= 0 || remote_sig_size <= 0)
		return false;

	/* Refused rather than partly filled. The derivation writes exactly CHIAKI_ECDH_SECRET_SIZE and
	 * takes no size, so a shorter buffer is a stack overrun and not a truncation. */
	if(secret_capacity < CHIAKI_ECDH_SECRET_SIZE)
		return false;

	/* The session's own pair, against the session's own handshake key. A fresh ecdh would derive a
	 * secret from a private key whose public half the console was never sent. */
	return chiaki_ecdh_derive_secret(
			&self->ecdh,
			out_secret,
			remote_key, (size_t)remote_key_size,
			self->handshake_key,
			remote_sig, (size_t)remote_sig_size)
		== CHIAKI_ERR_SUCCESS;
}

CHIAKI_SHIM_API bool chiaki_shim_session_auth_material(
		void *session,
		int32_t *out_target,
		uint8_t *out_nonce, int32_t nonce_capacity,
		uint8_t *out_morning, int32_t morning_capacity)
{
	ChiakiSession *self = (ChiakiSession *)session;
	int i;
	bool nonce_set = false;

	if(!self || !out_target || !out_nonce || !out_morning)
		return false;

	if(nonce_capacity < CHIAKI_RPCRYPT_KEY_SIZE || morning_capacity < (int32_t)sizeof(self->connect_info.morning))
		return false;

	/* All zeroes is what the nonce holds until ctrl's handshake base64-decodes one into it, and a
	 * crypt built from zeroes is valid, wrong, and silent about being either. */
	for(i = 0; i < CHIAKI_RPCRYPT_KEY_SIZE; i++)
	{
		if(self->nonce[i] != 0)
		{
			nonce_set = true;
			break;
		}
	}

	if(!nonce_set)
		return false;

	*out_target = (int32_t)self->target;
	memcpy(out_nonce, self->nonce, CHIAKI_RPCRYPT_KEY_SIZE);
	memcpy(out_morning, self->connect_info.morning, sizeof(self->connect_info.morning));

	return true;
}

CHIAKI_SHIM_API bool chiaki_shim_session_video_profile(
		void *session,
		uint32_t *out_width, uint32_t *out_height, uint32_t *out_max_fps,
		uint32_t *out_bitrate, int32_t *out_codec)
{
	ChiakiSession *self = (ChiakiSession *)session;

	if(!self || !out_width || !out_height || !out_max_fps || !out_bitrate || !out_codec)
		return false;

	*out_width = self->connect_info.video_profile.width;
	*out_height = self->connect_info.video_profile.height;
	*out_max_fps = self->connect_info.video_profile.max_fps;
	*out_bitrate = self->connect_info.video_profile.bitrate;
	*out_codec = (int32_t)self->connect_info.video_profile.codec;

	return true;
}

CHIAKI_SHIM_API void chiaki_shim_stream_run_install(void *session, void *handover)
{
	ChiakiSession *self = (ChiakiSession *)session;

	if(!self)
		return;

	chiaki_session_set_stream_run_cb(self, chiaki_shim_stream_run_trampoline, handover);
	chiaki_session_set_stream_stop_cb(self, chiaki_shim_stream_stop_trampoline, handover);
}

/* PP768: the way out of a wait, which this seam did not have.
 *
 * A caller holding a thread inside await_start had no correct way to stop it. Start would end the
 * wait and make the runner build a host and open a socket - worse than leaving it - and finish ends
 * the other wait. So a phase that wanted to shut down could only free the object its own thread was
 * blocked on, which is what PP762 did and what made the gate flaky rather than red.
 *
 * BOTH FLAGS TOGETHER, and stopped is set FIRST. The waiter reads it the moment its wait returns, so
 * setting started first would let a runner see a start with stopped still false and go on to build. */
CHIAKI_SHIM_API int32_t chiaki_shim_stream_handover_cancel(void *handover)
{
	ChiakiShimStreamHandover *self = (ChiakiShimStreamHandover *)handover;

	if(!self)
		return (int32_t)CHIAKI_ERR_INVALID_DATA;

	self->stopped = true;

	if(chiaki_bool_pred_cond_lock(&self->started) != CHIAKI_ERR_SUCCESS)
		return (int32_t)CHIAKI_ERR_UNKNOWN;

	self->started.pred = true;
	chiaki_bool_pred_cond_signal(&self->started);
	chiaki_bool_pred_cond_unlock(&self->started);

	/* And the other wait, so a thread that had already started is released too. */
	if(chiaki_bool_pred_cond_lock(&self->finished) != CHIAKI_ERR_SUCCESS)
		return (int32_t)CHIAKI_ERR_UNKNOWN;

	self->finished.pred = true;
	chiaki_bool_pred_cond_signal(&self->finished);
	chiaki_bool_pred_cond_unlock(&self->finished);

	return (int32_t)CHIAKI_ERR_SUCCESS;
}

/* PP769: the socket the run adopts, or -1 where the session handed none. */
CHIAKI_SHIM_API int64_t chiaki_shim_stream_handover_socket(void *handover)
{
	ChiakiShimStreamHandover *self = (ChiakiShimStreamHandover *)handover;

	return self ? self->data_sock : -1;
}

CHIAKI_SHIM_API bool chiaki_shim_stream_handover_stopped(void *handover)
{
	ChiakiShimStreamHandover *self = (ChiakiShimStreamHandover *)handover;

	return self ? self->stopped : false;
}

/* PP679: the v7 parse, whose key_state parameter the C declares and never reads.
 *
 * NULL is passed for it deliberately rather than forwarded. The v7 body takes its key position
 * straight off the wire as thirty-two bits - no ChiakiKeyState, no expansion, no ledger - and an
 * export that accepted a state would suggest the caller's one advances when it does not.
 *
 * word_at_0x18 crosses too, unlike the v9 export's. The formatter beside this writes it, so a
 * round trip that could not read it back would be comparing four fields out of five.
 */
CHIAKI_SHIM_API int32_t chiaki_shim_takion_v7_av_packet_parse(
		uint8_t *buf,
		int32_t buf_size,
		bool *is_video,
		bool *uses_nalu_info_structs,
		uint16_t *packet_index,
		uint16_t *frame_index,
		uint16_t *unit_index,
		uint16_t *units_in_frame_total,
		uint16_t *units_in_frame_fec,
		uint8_t *codec,
		uint16_t *word_at_0x18,
		uint8_t *adaptive_stream_index,
		uint64_t *key_pos,
		int32_t *data_offset,
		int32_t *data_size)
{
	ChiakiTakionAVPacket packet;
	ChiakiErrorCode err;

	if(!buf || buf_size <= 0)
		return (int32_t)CHIAKI_ERR_INVALID_DATA;

	memset(&packet, 0, sizeof(packet));
	err = chiaki_takion_v7_av_packet_parse(&packet, NULL, buf, (size_t)buf_size);
	if(err != CHIAKI_ERR_SUCCESS)
		return (int32_t)err;

	if(is_video)
		*is_video = packet.is_video;
	if(uses_nalu_info_structs)
		*uses_nalu_info_structs = packet.uses_nalu_info_structs;
	if(packet_index)
		*packet_index = packet.packet_index;
	if(frame_index)
		*frame_index = packet.frame_index;
	if(unit_index)
		*unit_index = packet.unit_index;
	if(units_in_frame_total)
		*units_in_frame_total = packet.units_in_frame_total;
	if(units_in_frame_fec)
		*units_in_frame_fec = packet.units_in_frame_fec;
	if(codec)
		*codec = packet.codec;
	if(word_at_0x18)
		*word_at_0x18 = packet.word_at_0x18;
	if(adaptive_stream_index)
		*adaptive_stream_index = packet.adaptive_stream_index;
	if(key_pos)
		*key_pos = packet.key_pos;

	/* The same ownership rule as the v9 export's: an offset into the caller's buffer, never a
	 * pointer the managed side would have to keep alive. */
	if(data_offset)
		*data_offset = packet.data ? (int32_t)(packet.data - buf) : -1;
	if(data_size)
		*data_size = (int32_t)packet.data_size;

	return (int32_t)CHIAKI_ERR_SUCCESS;
}

/* PP679: the file's only header FORMATTER, flattened the way the congestion one is.
 *
 * A ChiakiTakionAVPacket ends in a borrowed pointer, so the fields cross as scalars and the struct
 * is assembled here. Only the fields the formatter READS are taken; is_haptics, byte_at_0x2c and
 * the payload are not among them, and passing them would say this writes more than it does.
 *
 * header_size is written even where the buffer is too small - the C sets it before its bound check,
 * which is what lets senkusha.c assert the size it expected before looking at the error.
 */
CHIAKI_SHIM_API int32_t chiaki_shim_takion_v7_av_packet_format_header(
		uint8_t *buf,
		int32_t buf_size,
		int32_t *header_size_out,
		bool is_video,
		bool uses_nalu_info_structs,
		uint16_t packet_index,
		uint16_t frame_index,
		uint16_t unit_index,
		uint16_t units_in_frame_total,
		uint16_t units_in_frame_fec,
		uint8_t codec,
		uint16_t word_at_0x18,
		uint8_t adaptive_stream_index,
		uint64_t key_pos)
{
	ChiakiTakionAVPacket packet;
	ChiakiErrorCode err;
	size_t header_size = 0;

	if(!buf || buf_size < 0)
		return (int32_t)CHIAKI_ERR_INVALID_DATA;

	memset(&packet, 0, sizeof(packet));
	packet.is_video = is_video;
	packet.uses_nalu_info_structs = uses_nalu_info_structs;
	packet.packet_index = packet_index;
	packet.frame_index = frame_index;
	packet.unit_index = unit_index;
	packet.units_in_frame_total = units_in_frame_total;
	packet.units_in_frame_fec = units_in_frame_fec;
	packet.codec = codec;
	packet.word_at_0x18 = word_at_0x18;
	packet.adaptive_stream_index = adaptive_stream_index;
	packet.key_pos = key_pos;

	err = chiaki_takion_v7_av_packet_format_header(buf, (size_t)buf_size, &header_size, &packet);

	if(header_size_out)
		*header_size_out = (int32_t)header_size;

	return (int32_t)err;
}

/* PP696: the other twelve, and the two statics only they call. The helper goes inside with them -
 * left outside it would be a static nobody calls, which is a warning on the bare build and a
 * reader's question about why it is there. */
#ifdef CHIAKI_SHIM_HAVE_FRAMEPATH

static void chiaki_shim_unit_packet(
		ChiakiTakionAVPacket *packet,
		bool is_video,
		uint16_t frame_index,
		uint16_t packet_index,
		uint16_t unit_index,
		uint16_t units_in_frame_total,
		uint16_t units_in_frame_fec,
		uint8_t *data,
		int32_t data_size)
{
	memset(packet, 0, sizeof(*packet));
	packet->is_video = is_video;
	packet->frame_index = frame_index;
	packet->packet_index = packet_index;
	packet->unit_index = unit_index;
	packet->units_in_frame_total = units_in_frame_total;
	packet->units_in_frame_fec = units_in_frame_fec;
	packet->data = data;
	packet->data_size = (size_t)data_size;
}

CHIAKI_SHIM_API void *chiaki_shim_frame_processor_create(void *log)
{
	chiaki_shim_log *log_self = (chiaki_shim_log *)log;
	ChiakiFrameProcessor *self = (ChiakiFrameProcessor *)calloc(1, sizeof(ChiakiFrameProcessor));
	if(!self)
		return NULL;

	chiaki_frame_processor_init(self, log_self ? &log_self->log : NULL);
	return self;
}

CHIAKI_SHIM_API void chiaki_shim_frame_processor_free(void *processor)
{
	if(!processor)
		return;

	chiaki_frame_processor_fini((ChiakiFrameProcessor *)processor);
	free(processor);
}

CHIAKI_SHIM_API int32_t chiaki_shim_frame_processor_alloc_frame(
		void *processor,
		bool is_video,
		uint16_t frame_index,
		uint16_t packet_index,
		uint16_t unit_index,
		uint16_t units_in_frame_total,
		uint16_t units_in_frame_fec,
		uint8_t *data,
		int32_t data_size)
{
	ChiakiTakionAVPacket packet;
	if(!processor || !data || data_size <= 0)
		return (int32_t)CHIAKI_ERR_INVALID_DATA;

	chiaki_shim_unit_packet(&packet, is_video, frame_index, packet_index, unit_index,
			units_in_frame_total, units_in_frame_fec, data, data_size);
	return (int32_t)chiaki_frame_processor_alloc_frame((ChiakiFrameProcessor *)processor, &packet);
}

CHIAKI_SHIM_API int32_t chiaki_shim_frame_processor_put_unit(
		void *processor,
		bool is_video,
		uint16_t frame_index,
		uint16_t packet_index,
		uint16_t unit_index,
		uint16_t units_in_frame_total,
		uint16_t units_in_frame_fec,
		uint8_t *data,
		int32_t data_size)
{
	ChiakiTakionAVPacket packet;
	if(!processor || !data || data_size <= 0)
		return (int32_t)CHIAKI_ERR_INVALID_DATA;

	chiaki_shim_unit_packet(&packet, is_video, frame_index, packet_index, unit_index,
			units_in_frame_total, units_in_frame_fec, data, data_size);
	return (int32_t)chiaki_frame_processor_put_unit((ChiakiFrameProcessor *)processor, &packet);
}

CHIAKI_SHIM_API bool chiaki_shim_frame_processor_flush_possible(void *processor)
{
	return processor
			? chiaki_frame_processor_flush_possible((ChiakiFrameProcessor *)processor)
			: false;
}

CHIAKI_SHIM_API int32_t chiaki_shim_frame_processor_flush(
		void *processor, uint8_t *frame, int32_t *frame_size)
{
	uint8_t *out = NULL;
	size_t out_size = 0;
	ChiakiFrameProcessorFlushResult result;
	int32_t room;

	if(!processor || !frame_size)
		return (int32_t)CHIAKI_FRAME_PROCESSOR_FLUSH_RESULT_FAILED;

	room = *frame_size;
	*frame_size = 0;

	result = chiaki_frame_processor_flush((ChiakiFrameProcessor *)processor, &out, &out_size);
	if(result == CHIAKI_FRAME_PROCESSOR_FLUSH_RESULT_FAILED || !out)
		return (int32_t)result;

	// Copied out, because what flush hands back points into the processor's own buffer and stops
	// being valid at the next call to it. A managed caller that held the pointer would be reading
	// the next frame's bytes, or a reallocation's.
	if(frame && room > 0)
	{
		int32_t n = (int32_t)out_size < room ? (int32_t)out_size : room;
		memcpy(frame, out, (size_t)n);
		*frame_size = n;
	}
	else
	{
		*frame_size = (int32_t)out_size;
	}

	return (int32_t)result;
}

CHIAKI_SHIM_API uint64_t chiaki_shim_frame_processor_stage_samples(void *processor, int32_t stage)
{
	ChiakiFrameProcessor *self = (ChiakiFrameProcessor *)processor;
	if(!self)
		return 0;

	return stage == 0 ? self->stage_reassemble.samples : self->stage_correct.samples;
}

/**
 * The receiver, the session it reads four fields out of, and the managed callback.
 *
 * The session is zeroed apart from the log, the codec, the sample callback and its user - which is
 * exactly what test/videoreceiver.c does, and for the same reason: nothing else on the path a
 * single complete frame takes is touched.
 */
typedef struct chiaki_shim_video_receiver_t
{
	ChiakiVideoReceiver receiver;
	ChiakiSession session;
	ChiakiShimVideoSampleCb cb;
	void *user;
} chiaki_shim_video_receiver;

static bool chiaki_shim_video_sample(
		uint8_t *buf, size_t buf_size, int32_t frames_lost, bool frame_recovered, void *user)
{
	chiaki_shim_video_receiver *self = (chiaki_shim_video_receiver *)user;
	if(!self || !self->cb)
		return true;

	return self->cb(buf, (int32_t)buf_size, frames_lost, frame_recovered, self->user);
}

CHIAKI_SHIM_API void *chiaki_shim_video_receiver_create(
		void *log, int32_t codec, ChiakiShimVideoSampleCb cb, void *user)
{
	chiaki_shim_log *log_self = (chiaki_shim_log *)log;
	chiaki_shim_video_receiver *self =
			(chiaki_shim_video_receiver *)calloc(1, sizeof(chiaki_shim_video_receiver));
	if(!self)
		return NULL;

	self->cb = cb;
	self->user = user;
	self->session.log = log_self ? &log_self->log : NULL;
	self->session.connect_info.video_profile.codec = (ChiakiCodec)codec;
	self->session.video_sample_cb = chiaki_shim_video_sample;
	self->session.video_sample_cb_user = self;

	chiaki_video_receiver_init(&self->receiver, &self->session, NULL);
	return self;
}

CHIAKI_SHIM_API void chiaki_shim_video_receiver_free(void *receiver)
{
	chiaki_shim_video_receiver *self = (chiaki_shim_video_receiver *)receiver;
	if(!self)
		return;

	self->cb = NULL;
	chiaki_video_receiver_fini(&self->receiver);
	free(self);
}

CHIAKI_SHIM_API bool chiaki_shim_video_receiver_stream_info(
		void *receiver, const uint8_t *header, int32_t header_size, uint32_t width, uint32_t height)
{
	chiaki_shim_video_receiver *self = (chiaki_shim_video_receiver *)receiver;
	ChiakiVideoProfile profile;

	if(!self || !header || header_size <= 0)
		return false;

	memset(&profile, 0, sizeof(profile));
	profile.width = width;
	profile.height = height;
	profile.header_sz = (size_t)header_size;

	// Copied, because the receiver takes ownership of this buffer and frees it in fini. A managed
	// array is not something a C free() can be handed.
	profile.header = (uint8_t *)malloc((size_t)header_size);
	if(!profile.header)
		return false;
	memcpy(profile.header, header, (size_t)header_size);

	chiaki_video_receiver_stream_info(&self->receiver, &profile, 1);
	return true;
}

CHIAKI_SHIM_API void chiaki_shim_video_receiver_av_packet(
		void *receiver,
		uint16_t frame_index,
		uint16_t packet_index,
		uint16_t unit_index,
		uint16_t units_in_frame_total,
		uint16_t units_in_frame_fec,
		uint8_t adaptive_stream_index,
		uint8_t *data,
		int32_t data_size)
{
	chiaki_shim_video_receiver *self = (chiaki_shim_video_receiver *)receiver;
	ChiakiTakionAVPacket packet;

	if(!self || !data || data_size <= 0)
		return;

	chiaki_shim_unit_packet(&packet, true, frame_index, packet_index, unit_index,
			units_in_frame_total, units_in_frame_fec, data, data_size);
	packet.adaptive_stream_index = adaptive_stream_index;
	chiaki_video_receiver_av_packet(&self->receiver, &packet);
}

CHIAKI_SHIM_API int32_t chiaki_shim_video_receiver_frames_lost(void *receiver)
{
	chiaki_shim_video_receiver *self = (chiaki_shim_video_receiver *)receiver;
	return self ? chiaki_video_receiver_get_frames_lost_total(&self->receiver) : 0;
}

#endif /* CHIAKI_SHIM_HAVE_FRAMEPATH - the frame processor and the video receiver */

CHIAKI_SHIM_API void chiaki_shim_rpcrypt_aeropause_ps4_pre10(
		const uint8_t *ambassador, uint8_t *aeropause)
{
	if(ambassador && aeropause)
		chiaki_rpcrypt_aeropause_ps4_pre10(aeropause, ambassador);
}

// PP445: the PS4-from-10 and PS5 derivation. regist.c reaches it through init_regist, and the first
// version of this wrapper did too - taking key_0_off and the pin, because that call does. A test
// asserting each changed the answer failed: init_regist copies the ambassador through untouched and
// spends both on `bright`, which the aeropause never reads. Two parameters that did nothing.
//
// The 0x20 bound is this wrapper's own. chiaki_rpcrypt_aeropause indexes keys_1[i * 0x20 +
// key_1_off] with i to 15 over 512 bytes, and validates the target but not the offset - so 0x20
// reads past the end. Unreachable from regist.c, where the value is buf[0] >> 3, and reachable from
// here the moment this takes an int32. init_regist rejects its own offset the same way.
CHIAKI_SHIM_API bool chiaki_shim_rpcrypt_aeropause(
		int32_t target, const uint8_t *ambassador, int32_t key_1_off, uint8_t *aeropause)
{
	if(!ambassador || !aeropause || key_1_off < 0 || key_1_off >= 0x20)
		return false;

	return chiaki_rpcrypt_aeropause((ChiakiTarget)target, (size_t)key_1_off,
			aeropause, ambassador) == CHIAKI_ERR_SUCCESS;
}

CHIAKI_SHIM_API void chiaki_shim_rpcrypt_regist_bright_ps4_pre10(
		const uint8_t *ambassador, uint32_t pin, uint8_t *bright)
{
	ChiakiRPCrypt rpcrypt;
	if(!ambassador || !bright)
		return;

	chiaki_rpcrypt_init_regist_ps4_pre10(&rpcrypt, ambassador, pin);
	memcpy(bright, rpcrypt.bright, CHIAKI_RPCRYPT_KEY_SIZE);
}

CHIAKI_SHIM_API bool chiaki_shim_takion_message_decode(
		const uint8_t *buf,
		int32_t size,
		int32_t *type,
		bool *has_bang,
		uint32_t *server_version,
		uint32_t *token,
		bool *encrypted_key_accepted,
		bool *version_accepted)
{
	tkproto_TakionMessage msg;
	pb_istream_t stream;

	if(type)
		*type = 0;
	if(has_bang)
		*has_bang = false;
	if(server_version)
		*server_version = 0;
	if(token)
		*token = 0;
	if(encrypted_key_accepted)
		*encrypted_key_accepted = false;
	if(version_accepted)
		*version_accepted = false;

	if(!buf || size < 0)
		return false;

	memset(&msg, 0, sizeof(msg));
	stream = pb_istream_from_buffer(buf, (size_t)size);
	if(!pb_decode(&stream, tkproto_TakionMessage_fields, &msg))
		return false;

	if(type)
		*type = (int32_t)msg.type;
	if(has_bang)
		*has_bang = msg.has_bang_payload;

	if(msg.has_bang_payload)
	{
		if(server_version)
			*server_version = msg.bang_payload.server_version;
		if(token)
			*token = msg.bang_payload.token;
		if(encrypted_key_accepted)
			*encrypted_key_accepted = msg.bang_payload.encrypted_key_accepted;
		if(version_accepted)
			*version_accepted = msg.bang_payload.version_accepted;
	}

	return true;
}

/** A string field, written out through the callback nanopb asks for instead of storing it. */
static bool chiaki_shim_pb_encode_string(
		pb_ostream_t *stream, const pb_field_t *field, void *const *arg)
{
	const char *text = *arg;
	if(!pb_encode_tag_for_field(stream, field))
		return false;

	return pb_encode_string(stream, (const uint8_t *)text, strlen(text));
}

CHIAKI_SHIM_API bool chiaki_shim_takion_message_encode_bang(
		uint32_t server_version,
		uint32_t token,
		bool encrypted_key_accepted,
		bool version_accepted,
		const char *session_key,
		const uint8_t *ecdh_pub_key, int32_t ecdh_pub_key_size,
		const uint8_t *ecdh_sig, int32_t ecdh_sig_size,
		uint8_t *buf,
		int32_t *buf_size)
{
	tkproto_TakionMessage msg;
	pb_ostream_t stream;
	ChiakiPBBuf pub_key_buf;
	ChiakiPBBuf sig_buf;

	if(!session_key || !buf || !buf_size || *buf_size <= 0)
		return false;

	memset(&msg, 0, sizeof(msg));
	msg.type = tkproto_TakionMessage_PayloadType_BANG;
	msg.has_bang_payload = true;
	msg.bang_payload.server_version = server_version;
	msg.bang_payload.token = token;
	msg.bang_payload.encrypted_key_accepted = encrypted_key_accepted;
	msg.bang_payload.version_accepted = version_accepted;

	// Three callbacks, because nanopb stores none of these: it calls back as the field goes past
	// and the caller writes it. The pointers below have to outlive the encode, which they do -
	// they are the caller's, and pb_encode returns before this function does.
	msg.bang_payload.session_key.funcs.encode = chiaki_shim_pb_encode_string;
	msg.bang_payload.session_key.arg = (void *)session_key;

	if(ecdh_pub_key && ecdh_pub_key_size > 0)
	{
		pub_key_buf.buf = (uint8_t *)ecdh_pub_key;
		pub_key_buf.size = (size_t)ecdh_pub_key_size;
		msg.bang_payload.ecdh_pub_key.funcs.encode = chiaki_pb_encode_buf;
		msg.bang_payload.ecdh_pub_key.arg = &pub_key_buf;
	}

	if(ecdh_sig && ecdh_sig_size > 0)
	{
		sig_buf.buf = (uint8_t *)ecdh_sig;
		sig_buf.size = (size_t)ecdh_sig_size;
		msg.bang_payload.ecdh_sig.funcs.encode = chiaki_pb_encode_buf;
		msg.bang_payload.ecdh_sig.arg = &sig_buf;
	}

	stream = pb_ostream_from_buffer(buf, (size_t)*buf_size);
	if(!pb_encode(&stream, tkproto_TakionMessage_fields, &msg))
	{
		*buf_size = 0;
		return false;
	}

	*buf_size = (int32_t)stream.bytes_written;
	return true;
}

/** PP23: a driveable vl_rbsp, with its payload placed at a chosen address alignment. */
typedef struct chiaki_shim_rbsp_t
{
	uint8_t *block;   /* the allocation, freed on close */
	uint8_t *payload; /* where inside it the NAL was copied, at the requested alignment */
	struct vl_vlc vlc;
	struct vl_rbsp rbsp;
} chiaki_shim_rbsp;

CHIAKI_SHIM_API void *chiaki_shim_rbsp_create(
		const uint8_t *data, int32_t size, uint32_t num_bits, int32_t alignment)
{
	chiaki_shim_rbsp *self;
	uint8_t *at;

	if(!data || size < 0 || alignment < 0 || alignment > 3)
		return NULL;

	self = (chiaki_shim_rbsp *)calloc(1, sizeof(chiaki_shim_rbsp));
	if(!self)
		return NULL;

	/* Four bytes of slack, so any of the four alignments can be hit. */
	self->block = (uint8_t *)malloc((size_t)size + 8);
	if(!self->block)
	{
		free(self);
		return NULL;
	}

	at = self->block;
	while((((uintptr_t)at) & 3) != (uintptr_t)alignment)
		at++;

	memcpy(at, data, (size_t)size);
	self->payload = at;

	vl_vlc_init(&self->vlc, self->payload, (unsigned)size);
	vl_rbsp_init(&self->rbsp, &self->vlc, num_bits);
	return self;
}

CHIAKI_SHIM_API void chiaki_shim_rbsp_free(void *rbsp)
{
	chiaki_shim_rbsp *self = (chiaki_shim_rbsp *)rbsp;
	if(!self)
		return;

	free(self->block);
	free(self);
}

CHIAKI_SHIM_API int32_t chiaki_shim_rbsp_alignment(void *rbsp)
{
	chiaki_shim_rbsp *self = (chiaki_shim_rbsp *)rbsp;
	if(!self)
		return -1;
	return (int32_t)(((uintptr_t)self->payload) & 3);
}

CHIAKI_SHIM_API uint32_t chiaki_shim_rbsp_u(void *rbsp, uint32_t n)
{
	chiaki_shim_rbsp *self = (chiaki_shim_rbsp *)rbsp;
	if(!self)
		return 0;
	return (uint32_t)vl_rbsp_u(&self->rbsp, n);
}

CHIAKI_SHIM_API uint32_t chiaki_shim_rbsp_ue(void *rbsp)
{
	chiaki_shim_rbsp *self = (chiaki_shim_rbsp *)rbsp;
	if(!self)
		return 0;
	return (uint32_t)vl_rbsp_ue(&self->rbsp);
}

CHIAKI_SHIM_API int32_t chiaki_shim_rbsp_se(void *rbsp)
{
	chiaki_shim_rbsp *self = (chiaki_shim_rbsp *)rbsp;
	if(!self)
		return 0;
	return (int32_t)vl_rbsp_se(&self->rbsp);
}

CHIAKI_SHIM_API bool chiaki_shim_rbsp_overrun(void *rbsp)
{
	chiaki_shim_rbsp *self = (chiaki_shim_rbsp *)rbsp;
	if(!self)
		return false;
	return vl_rbsp_overrun(&self->rbsp);
}

CHIAKI_SHIM_API bool chiaki_shim_rbsp_has_bits(void *rbsp, uint32_t n)
{
	chiaki_shim_rbsp *self = (chiaki_shim_rbsp *)rbsp;
	if(!self)
		return false;
	return vl_rbsp_has_bits(&self->rbsp, n);
}

CHIAKI_SHIM_API bool chiaki_shim_rbsp_more_data(void *rbsp)
{
	chiaki_shim_rbsp *self = (chiaki_shim_rbsp *)rbsp;
	if(!self)
		return false;
	return vl_rbsp_more_data(&self->rbsp);
}

CHIAKI_SHIM_API uint32_t chiaki_shim_rbsp_valid_bits(void *rbsp)
{
	chiaki_shim_rbsp *self = (chiaki_shim_rbsp *)rbsp;
	if(!self)
		return 0;
	return (uint32_t)vl_vlc_valid_bits(&self->rbsp.nal);
}

CHIAKI_SHIM_API uint32_t chiaki_shim_rbsp_bits_left(void *rbsp)
{
	chiaki_shim_rbsp *self = (chiaki_shim_rbsp *)rbsp;
	if(!self)
		return 0;
	return (uint32_t)vl_vlc_bits_left(&self->rbsp.nal);
}

CHIAKI_SHIM_API int64_t chiaki_shim_ffmpeg_nopts(void)
{
	return (int64_t)AV_NOPTS_VALUE;
}

CHIAKI_SHIM_API bool chiaki_shim_ffmpeg_frame_timing(
		int64_t best_effort_timestamp,
		int64_t pts,
		int64_t duration,
		int32_t pkt_timebase_num, int32_t pkt_timebase_den,
		int32_t ctx_timebase_num, int32_t ctx_timebase_den,
		int32_t framerate_num, int32_t framerate_den,
		double *pts_out,
		double *duration_out)
{
	AVFrame *frame;
	AVRational pkt_timebase;
	AVRational ctx_timebase;
	AVRational framerate;
	double pts_value = 0.0;
	double duration_value = 0.0;

	if(pts_out)
		*pts_out = 0.0;
	if(duration_out)
		*duration_out = 0.0;

	frame = av_frame_alloc();
	if(!frame)
		return false;

	frame->best_effort_timestamp = best_effort_timestamp;
	frame->pts = pts;

	/* PP23: settable, because the duration > 0 branch was unreachable through this wrapper while
	 * av_frame_alloc's zero was the only value it could ever see - so half of the duration
	 * fallback chain had no oracle to be checked against. */
	frame->duration = duration;

	pkt_timebase.num = pkt_timebase_num;
	pkt_timebase.den = pkt_timebase_den;
	ctx_timebase.num = ctx_timebase_num;
	ctx_timebase.den = ctx_timebase_den;
	framerate.num = framerate_num;
	framerate.den = framerate_den;

	chiaki_ffmpeg_frame_get_timing(frame, pkt_timebase, ctx_timebase, framerate,
			&pts_value, &duration_value);

	if(pts_out)
		*pts_out = pts_value;
	if(duration_out)
		*duration_out = duration_value;

	av_frame_free(&frame);
	return true;
}

CHIAKI_SHIM_API int32_t chiaki_shim_regist_request_payload(
		int32_t target,
		const uint8_t *ambassador,
		const char *psn_online_id,
		const uint8_t *psn_account_id,
		uint32_t pin,
		uint8_t *buf,
		int32_t *buf_size)
{
	ChiakiRPCrypt rpcrypt;
	size_t size;
	ChiakiErrorCode err;

	if(!ambassador || !buf || !buf_size || *buf_size <= 0)
		return (int32_t)CHIAKI_ERR_INVALID_DATA;

	size = (size_t)*buf_size;
	*buf_size = 0;

	// The holepunch info is NULL: that is the local registration, which is the one the recorded
	// payload was taken from. A PSN registration carries a different tail and is PP7's ground.
	err = chiaki_regist_request_payload_format((ChiakiTarget)target, ambassador, buf, &size,
			&rpcrypt, psn_online_id, psn_account_id, pin, NULL);
	if(err != CHIAKI_ERR_SUCCESS)
		return (int32_t)err;

	*buf_size = (int32_t)size;
	return (int32_t)CHIAKI_ERR_SUCCESS;
}

typedef struct chiaki_shim_discovery_service_t
{
	ChiakiDiscoveryService service;
	ChiakiShimDiscoveryServiceCb cb;
	void *user;
} chiaki_shim_discovery_service;

static void chiaki_shim_discovery_service_dispatch(
		ChiakiDiscoveryHost *hosts, size_t hosts_count, void *user)
{
	chiaki_shim_discovery_service *self = (chiaki_shim_discovery_service *)user;
	if(self && self->cb)
		self->cb(hosts, (int32_t)hosts_count, self->user);
}

CHIAKI_SHIM_API void *chiaki_shim_discovery_service_create(
		void *log,
		const char *send_host,
		uint64_t ping_ms,
		int32_t hosts_max,
		ChiakiShimDiscoveryServiceCb cb,
		void *user)
{
	chiaki_shim_log *log_self = (chiaki_shim_log *)log;
	chiaki_shim_discovery_service *self;
	ChiakiDiscoveryServiceOptions options;
	struct sockaddr_storage addr;
	struct sockaddr_in *addr_in;

	if(!send_host || hosts_max <= 0)
		return NULL;

	memset(&addr, 0, sizeof(addr));
	addr_in = (struct sockaddr_in *)&addr;
	addr_in->sin_family = AF_INET;
	if(inet_pton(AF_INET, send_host, &addr_in->sin_addr) != 1)
		return NULL;

	self = (chiaki_shim_discovery_service *)calloc(1, sizeof(chiaki_shim_discovery_service));
	if(!self)
		return NULL;

	self->cb = cb;
	self->user = user;

	memset(&options, 0, sizeof(options));
	options.hosts_max = (size_t)hosts_max;
	options.host_drop_pings = 3;
	options.ping_ms = ping_ms;
	options.ping_initial_ms = ping_ms;
	options.send_addr = &addr;
	options.send_addr_size = sizeof(struct sockaddr_in);
	options.broadcast_addrs = NULL;
	options.broadcast_num = 0;
	options.send_host = NULL;
	options.cb = cb ? chiaki_shim_discovery_service_dispatch : NULL;
	options.cb_user = self;

	if(chiaki_discovery_service_init(&self->service, &options,
			log_self ? &log_self->log : NULL) != CHIAKI_ERR_SUCCESS)
	{
		free(self);
		return NULL;
	}

	return self;
}

CHIAKI_SHIM_API void chiaki_shim_discovery_service_free(void *service)
{
	chiaki_shim_discovery_service *self = (chiaki_shim_discovery_service *)service;
	if(!self)
		return;

	// Cleared before fini, because fini joins a thread that may be inside the callback - and what
	// it would be calling into is about to stop existing.
	self->cb = NULL;
	chiaki_discovery_service_fini(&self->service);
	free(self);
}

static ChiakiDiscoveryHost *chiaki_shim_discovery_host_at(void *hosts, int32_t index)
{
	return hosts && index >= 0 ? ((ChiakiDiscoveryHost *)hosts) + index : NULL;
}

CHIAKI_SHIM_API const char *chiaki_shim_discovery_service_host_field(
		void *hosts, int32_t index, int32_t field)
{
	ChiakiDiscoveryHost *host = chiaki_shim_discovery_host_at(hosts, index);
	if(!host)
		return NULL;

	switch((ChiakiShimDiscoveryField)field)
	{
		case CHIAKI_SHIM_DISCOVERY_HOST_ADDR:
			return host->host_addr;
		case CHIAKI_SHIM_DISCOVERY_SYSTEM_VERSION:
			return host->system_version;
		case CHIAKI_SHIM_DISCOVERY_PROTOCOL_VERSION:
			return host->device_discovery_protocol_version;
		case CHIAKI_SHIM_DISCOVERY_HOST_NAME:
			return host->host_name;
		case CHIAKI_SHIM_DISCOVERY_HOST_TYPE:
			return host->host_type;
		case CHIAKI_SHIM_DISCOVERY_HOST_ID:
			return host->host_id;
		case CHIAKI_SHIM_DISCOVERY_RUNNING_APP_TITLEID:
			return host->running_app_titleid;
		case CHIAKI_SHIM_DISCOVERY_RUNNING_APP_NAME:
			return host->running_app_name;
		default:
			return NULL;
	}
}

CHIAKI_SHIM_API int32_t chiaki_shim_discovery_service_host_state(void *hosts, int32_t index)
{
	ChiakiDiscoveryHost *host = chiaki_shim_discovery_host_at(hosts, index);
	return host ? (int32_t)host->state : 0;
}

CHIAKI_SHIM_API int32_t chiaki_shim_discovery_service_host_request_port(void *hosts, int32_t index)
{
	ChiakiDiscoveryHost *host = chiaki_shim_discovery_host_at(hosts, index);
	return host ? (int32_t)host->host_request_port : 0;
}

CHIAKI_SHIM_API int32_t chiaki_shim_duid_str_size(void)
{
	return (int32_t)CHIAKI_DUID_STR_SIZE;
}


CHIAKI_SHIM_API void chiaki_shim_session_free(void *session)
{
	chiaki_shim_session *self = (chiaki_shim_session *)session;
	if(!self)
		return;

	chiaki_session_fini(&self->session);
	free(self);
}

CHIAKI_SHIM_API const char *chiaki_shim_quit_reason_string(int32_t reason)
{
	return chiaki_quit_reason_string((ChiakiQuitReason)reason);
}

CHIAKI_SHIM_API int32_t chiaki_shim_base64_encode(
		const uint8_t *in,
		int32_t in_size,
		char *out,
		int32_t out_size)
{
	if(!in || !out || in_size < 0 || out_size <= 0)
		return (int32_t)CHIAKI_ERR_INVALID_DATA;

	/* Straight through: what this exists to show is what the encoder does to `out`, so nothing
	   here touches it before or after. */
	return (int32_t)chiaki_base64_encode(in, (size_t)in_size, out, (size_t)out_size);
}

/* PP607: a takion over loopback, so the receive loop PP601 could not reach has a way in.
 *
 * The whole of it is a sockaddr and a callback. chiaki_takion_connect takes NULL for the socket
 * and makes its own from the address - senkusha.c does exactly that and frees its sockaddr the
 * moment connect returns, which is what says a stack local is safe here too. */
typedef struct chiaki_shim_takion_t
{
	ChiakiTakion takion;
	/* Written on takion's thread and read on the caller's. Not a mutex: they are a flag and a
	   counter that only ever move one way, and a harness that took a lock to read them would be
	   measuring the lock. */
	volatile int connected;
	volatile int events;
} chiaki_shim_takion;

static void chiaki_shim_takion_event_cb(ChiakiTakionEvent *event, void *user)
{
	chiaki_shim_takion *self = (chiaki_shim_takion *)user;
	if(!self || !event)
		return;

	self->events++;

	if(event->type == CHIAKI_TAKION_EVENT_TYPE_CONNECTED)
		self->connected = 1;
}

CHIAKI_SHIM_API void *chiaki_shim_takion_connect_loopback(
		void *log,
		uint16_t port,
		uint8_t protocol_version,
		int32_t *error_out)
{
	chiaki_shim_log *log_self = (chiaki_shim_log *)log;

	if(error_out)
		*error_out = (int32_t)CHIAKI_ERR_INVALID_DATA;

	/* Port zero is not a peer. Without this the connect would go to whatever the OS picks. */
	if(port == 0)
		return NULL;

	chiaki_shim_takion *self = (chiaki_shim_takion *)calloc(1, sizeof(chiaki_shim_takion));
	if(!self)
	{
		if(error_out)
			*error_out = (int32_t)CHIAKI_ERR_MEMORY;
		return NULL;
	}

	struct sockaddr_in sa;
	memset(&sa, 0, sizeof(sa));
	sa.sin_family = AF_INET;
	sa.sin_port = htons(port);
	sa.sin_addr.s_addr = htonl(INADDR_LOOPBACK);

	ChiakiTakionConnectInfo info;
	memset(&info, 0, sizeof(info));
	info.log = log_self ? &log_self->log : NULL;
	info.sa = (struct sockaddr *)&sa;
	info.sa_len = sizeof(sa);
	info.ip_dontfrag = false;
	/* OFF, and it is the reason this harness is possible at all: with crypt on, takion checks a
	   MAC on every packet once a gkcrypt exists, and a handshake peer has none to give it. */
	info.enable_crypt = false;
	info.enable_dualsense = false;
	info.protocol_version = protocol_version;
	info.close_socket = true;
	info.cb = chiaki_shim_takion_event_cb;
	info.cb_user = self;

	ChiakiErrorCode err = chiaki_takion_connect(&self->takion, &info, NULL);
	if(error_out)
		*error_out = (int32_t)err;

	if(err != CHIAKI_ERR_SUCCESS)
	{
		free(self);
		return NULL;
	}

	return self;
}

CHIAKI_SHIM_API bool chiaki_shim_takion_connected(void *takion)
{
	chiaki_shim_takion *self = (chiaki_shim_takion *)takion;
	return self && self->connected != 0;
}

CHIAKI_SHIM_API int32_t chiaki_shim_takion_event_count(void *takion)
{
	chiaki_shim_takion *self = (chiaki_shim_takion *)takion;
	return self ? (int32_t)self->events : 0;
}

CHIAKI_SHIM_API void chiaki_shim_takion_close(void *takion)
{
	chiaki_shim_takion *self = (chiaki_shim_takion *)takion;
	if(!self)
		return;

	/* Joins the thread, so nothing below can run while the callback might. */
	chiaki_takion_close(&self->takion);
	free(self);
}



/* PP676: feedback.c's serialisers, reachable as an oracle. See the header for why. */

CHIAKI_SHIM_API int32_t chiaki_shim_feedback_state_size(bool v12)
{
	return v12 ? CHIAKI_FEEDBACK_STATE_BUF_SIZE_V12 : CHIAKI_FEEDBACK_STATE_BUF_SIZE_V9;
}

CHIAKI_SHIM_API void chiaki_shim_feedback_state_format(
		uint8_t *buf, int32_t buf_size, bool v12, const float *motion, const int16_t *sticks)
{
	ChiakiFeedbackState state;

	if(!buf || !motion || !sticks)
		return;
	if(buf_size < chiaki_shim_feedback_state_size(v12))
		return;

	state.gyro_x = motion[0];
	state.gyro_y = motion[1];
	state.gyro_z = motion[2];
	state.accel_x = motion[3];
	state.accel_y = motion[4];
	state.accel_z = motion[5];
	state.orient_x = motion[6];
	state.orient_y = motion[7];
	state.orient_z = motion[8];
	state.orient_w = motion[9];
	state.left_x = sticks[0];
	state.left_y = sticks[1];
	state.right_x = sticks[2];
	state.right_y = sticks[3];

	if(v12)
		chiaki_feedback_state_format_v12(buf, &state);
	else
		chiaki_feedback_state_format_v9(buf, &state);
}

CHIAKI_SHIM_API int32_t chiaki_shim_feedback_history_button(
		uint64_t button, uint8_t state, uint8_t *out, int32_t *out_len)
{
	ChiakiFeedbackHistoryEvent event;
	ChiakiErrorCode err;

	if(!out || !out_len)
		return (int32_t)CHIAKI_ERR_INVALID_DATA;

	memset(&event, 0, sizeof(event));
	err = chiaki_feedback_history_event_set_button(&event, button, state);
	if(err != CHIAKI_ERR_SUCCESS)
	{
		*out_len = 0;
		return (int32_t)err;
	}

	memcpy(out, event.buf, event.len);
	*out_len = (int32_t)event.len;
	return (int32_t)CHIAKI_ERR_SUCCESS;
}

CHIAKI_SHIM_API void chiaki_shim_feedback_history_touchpad(
		bool down, uint8_t pointer_id, uint16_t x, uint16_t y, uint8_t *out, int32_t *out_len)
{
	ChiakiFeedbackHistoryEvent event;

	if(!out || !out_len)
		return;

	memset(&event, 0, sizeof(event));
	chiaki_feedback_history_event_set_touchpad(&event, down, pointer_id, x, y);

	memcpy(out, event.buf, event.len);
	*out_len = (int32_t)event.len;
}

CHIAKI_SHIM_API int32_t chiaki_shim_feedback_history_format(
		int32_t size, const uint8_t *events, const int32_t *lens, int32_t count,
		uint8_t *out, int32_t *out_size)
{
	ChiakiFeedbackHistoryBuffer buffer;
	ChiakiFeedbackHistoryEvent event;
	ChiakiErrorCode err;
	size_t written;
	int32_t at = 0;
	int32_t i;

	if(!events || !lens || !out || !out_size || size <= 0 || count < 0)
		return (int32_t)CHIAKI_ERR_INVALID_DATA;

	err = chiaki_feedback_history_buffer_init(&buffer, (size_t)size);
	if(err != CHIAKI_ERR_SUCCESS)
		return (int32_t)err;

	for(i = 0; i < count; i++)
	{
		if(lens[i] < 0 || lens[i] > CHIAKI_HISTORY_EVENT_SIZE_MAX)
		{
			chiaki_feedback_history_buffer_fini(&buffer);
			return (int32_t)CHIAKI_ERR_INVALID_DATA;
		}

		memset(&event, 0, sizeof(event));
		memcpy(event.buf, events + at, (size_t)lens[i]);
		event.len = (size_t)lens[i];
		at += lens[i];

		chiaki_feedback_history_buffer_push(&buffer, &event);
	}

	written = (size_t)*out_size;
	err = chiaki_feedback_history_buffer_format(&buffer, out, &written);
	*out_size = (int32_t)written;

	chiaki_feedback_history_buffer_fini(&buffer);
	return (int32_t)err;
}

CHIAKI_SHIM_API int32_t chiaki_shim_packet_stats_run(
		const uint64_t *gen_received, const uint64_t *gen_lost, int32_t gen_count,
		const uint16_t *seqs, int32_t seq_count, int32_t seq_split,
		bool reset, uint64_t *received, uint64_t *lost)
{
	ChiakiPacketStats stats;
	ChiakiErrorCode err;
	int32_t i;

	if(!received || !lost || gen_count < 0 || seq_count < 0)
		return (int32_t)CHIAKI_ERR_INVALID_DATA;
	if(seq_split < 0 || seq_split > seq_count)
		return (int32_t)CHIAKI_ERR_INVALID_DATA;
	if((gen_count > 0 && (!gen_received || !gen_lost)) || (seq_count > 0 && !seqs))
		return (int32_t)CHIAKI_ERR_INVALID_DATA;

	err = chiaki_packet_stats_init(&stats);
	if(err != CHIAKI_ERR_SUCCESS)
		return (int32_t)err;

	for(i = 0; i < gen_count; i++)
		chiaki_packet_stats_push_generation(&stats, gen_received[i], gen_lost[i]);

	for(i = 0; i < seq_split; i++)
		chiaki_packet_stats_push_seq(&stats, seqs[i]);

	chiaki_packet_stats_get(&stats, reset, &received[0], &lost[0]);

	for(i = seq_split; i < seq_count; i++)
		chiaki_packet_stats_push_seq(&stats, seqs[i]);

	chiaki_packet_stats_get(&stats, false, &received[1], &lost[1]);

	chiaki_packet_stats_fini(&stats);
	return (int32_t)CHIAKI_ERR_SUCCESS;
}
