// SPDX-License-Identifier: LicenseRef-AGPL-3.0-only-OpenSSL

#ifndef CHIAKI_SHIM_H
#define CHIAKI_SHIM_H

#include <stdbool.h>
#include <stdint.h>

/**
 * PP4: the seam, and the side of it that compiles C.
 *
 * lib/ is the part of this project that is not being ported, and it is a static archive whose
 * CHIAKI_EXPORT expands to nothing - so it has no exported symbols at all, and .NET cannot
 * P/Invoke it however the marshalling is written. That is not a difficulty with direct
 * P/Invoke, it is what rules it out: taking it would mean starting the port by editing the
 * half that is not being ported, giving 95 functions and 22 callback typedefs a
 * __declspec(dllexport) and a managed struct layout each.
 *
 * So the boundary is a shim: a DLL that statically links chiaki-lib and exports a flat surface
 * of its own. Three properties follow, and they are why this shape was chosen rather than
 * merely accepted.
 *
 *   - lib/ is untouched. Not one line, not one macro.
 *   - The surface is what the port needs and not what the library happens to have. streamsession
 *     .cpp already adapts libchiaki to a client; this is where a Qt-free copy of that adaptation
 *     goes, on the side of the boundary that already compiles it.
 *   - A ChiakiSession stays an opaque handle. Nothing here hands .NET a struct whose layout it
 *     would then have to track through a libchiaki upgrade.
 *
 * What is here so far is the seam itself and nothing that streams.
 *
 * That does not make the DLL free-standing, and it is worth writing down because the first
 * version of this comment claimed it was. Linking chiaki-lib pulls whole objects, and common.c -
 * which chiaki_error_string lives in - reaches OpenSSL through random.h and winsock through
 * winsock2.h. So chiaki-shim.dll imports libcrypto-3-x64.dll from the first export onwards, and
 * the deployment question a session function would have raised is raised already: the shim is
 * copied into the portable tree that holds those DLLs, and the managed host loads it from there
 * rather than from beside its own assembly. The compiler runtime is still linked in statically,
 * which removes the MinGW DLLs from that list but not the rest.
 */

#if defined(_WIN32)
#define CHIAKI_SHIM_API __declspec(dllexport)
#else
#define CHIAKI_SHIM_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

/**
 * Bumped whenever an exported signature here changes meaning.
 *
 * The managed side checks it on load and refuses a mismatch, because the failure it prevents is
 * the one with no symptom: a DLL left behind by an older build exports every name the new
 * assembly imports, and the arguments land in the wrong places quietly.
 */
#define CHIAKI_SHIM_ABI 47

CHIAKI_SHIM_API uint32_t chiaki_shim_abi_version(void);

/**
 * PP661: whether this shim carries the oracles PP655's flip removes.
 *
 * Always declared and always defined, whichever way CHIAKI_ENABLE_HOLEPUNCH went - which is what
 * makes them answerable. A managed guard that read the header for the wrappers' own names would be
 * reading text an #ifdef had already excluded from the build, and PP661's first mechanism did
 * exactly that.
 */
CHIAKI_SHIM_API bool chiaki_shim_has_holepunch(void);
CHIAKI_SHIM_API bool chiaki_shim_has_jsonc(void);

/**
 * PP670: and whether it carries the frame path's fourteen - the fec, frame processor and video
 * receiver wrappers that are PP286-PP291's oracles. True on every build today: the define behind it
 * is unconditional until PP295's flip makes it follow an option, and this export is what lets the
 * six differentials that call the fourteen be guarded BEFORE that flip rather than turned red by it.
 */
CHIAKI_SHIM_API bool chiaki_shim_has_framepath(void);

/**
 * PP694: and whether it carries libopus, which the encoder oracle below needs.
 *
 * CHIAKI_LIB_ENABLE_OPUS defaults ON and no build here has turned it off, so this reads true today.
 * Declared and defined either way, for the reason the three above are: a guard that answers from
 * anything but the build that produced the DLL is PP681's defect with a different subject.
 */
CHIAKI_SHIM_API bool chiaki_shim_has_opus(void);

/**
 * PP694: what opusencoder.c does to a microphone frame, as the oracle for a managed encoder.
 *
 * chiaki_opus_encoder_frame itself is out of reach - it needs an audio sender, which needs a
 * ChiakiSession, which needs a console. What it DOES is opus_encode with the module's own two
 * parameters, and those run with nothing behind them: the application mode it chooses, and the
 * forty-byte buffer whose size it insists the result equals.
 *
 * The application crosses as an export so the managed side does not write the number down. The
 * forty does not: it is a literal inside opusencoder.c and no header publishes it, so a source
 * model reads it from that file instead.
 *
 * chiaki_shim_opus_encode returns opus_encode's own code unchanged. Below one is an error and
 * anything that is not the buffer's size is what the C drops as a protocol violation, so a caller
 * needs the number rather than a success flag.
 *
 * Guarded by CHIAKI_SHIM_HAVE_OPUS. Ask chiaki_shim_has_opus first.
 */
CHIAKI_SHIM_API int32_t chiaki_shim_opus_encoder_application(void);
CHIAKI_SHIM_API void *chiaki_shim_opus_encoder_create(
		int32_t rate, int32_t channels, int32_t *error_out);
CHIAKI_SHIM_API void chiaki_shim_opus_encoder_destroy(void *encoder);
CHIAKI_SHIM_API int32_t chiaki_shim_opus_encode(
		void *encoder, const int16_t *pcm, int32_t frame_size, uint8_t *out, int32_t out_size);

/**
 * PP751: the decoder's four, which opusdecoder.c needs and the encoder's did not provide.
 *
 * chiaki_shim_opus_decode returns opus_decode's own code: the SAMPLE COUNT per channel, with
 * anything below one an error the C logs and drops.
 *
 * A size of zero decodes a NULL packet, which is Opus's loss concealment rather than an empty
 * frame - and it is exactly what audioreceiver.c's concealed frame becomes. Passing an empty
 * buffer instead would be a different call with a different result.
 *
 * Guarded by CHIAKI_SHIM_HAVE_OPUS, like the encoder's. Ask chiaki_shim_has_opus first.
 */
CHIAKI_SHIM_API void *chiaki_shim_opus_decoder_create(
		int32_t rate, int32_t channels, int32_t *error_out);
CHIAKI_SHIM_API void chiaki_shim_opus_decoder_destroy(void *decoder);
CHIAKI_SHIM_API int32_t chiaki_shim_opus_decode(
		void *decoder, const uint8_t *data, int32_t size, int16_t *pcm, int32_t frame_size);

/**
 * PP753: the seam a session thread hands its stream phase across, and takes an outcome back on.
 *
 * PP752 decided that exactly one of the session thread's seven steps becomes managed - the run -
 * and that the C thread WAITS rather than returns, because its steps five to seven still have to
 * happen on it. What it did not have was any way for the two sides to meet.
 *
 * NOT A MANAGED FUNCTION POINTER, for the reason ChiakiShimLogCb gives above: every one of
 * libchiaki's twenty-two callbacks is installed as a C trampoline, because an enum's underlying
 * type is the compiler's choice and a pinned managed object is one GC compaction away from a call
 * into freed memory. A run installed that way would be the same bet held for the length of a
 * session rather than for one log line.
 *
 * SO THE THREAD BLOCKS AND THE MANAGED SIDE SIGNALS. Two waits, one each way:
 *
 *   - `started` is raised by the C thread when it reaches the stream phase, so the managed side
 *     knows when to begin rather than polling for it.
 *   - `finished` is raised by the managed side when its run returns, carrying the two values the
 *     session thread needs: the error code, and the remote disconnect reason it compares against
 *     the shutdown phrase to choose between two quit reasons.
 *
 * THE REASON IS COPIED IN, not borrowed. It is a managed string on the other side of the seam, and
 * the session thread reads it after the run is over - so a pointer would outlive whatever held it.
 * The copy is the handover's and is freed with it.
 *
 * Nothing here edits session.c. Wiring this in place of the run, and deleting what the run drives,
 * is the one commit that touches lib.
 */
CHIAKI_SHIM_API void *chiaki_shim_stream_handover_create(void);
CHIAKI_SHIM_API void chiaki_shim_stream_handover_free(void *handover);

/** Raised by the C session thread when it reaches the stream phase. */
CHIAKI_SHIM_API int32_t chiaki_shim_stream_handover_start(void *handover);

/** Waits for that, so the managed side begins when the thread is there. False on timeout. */
CHIAKI_SHIM_API bool chiaki_shim_stream_handover_await_start(void *handover, int32_t timeout_ms);

/** Raised by the managed side when its run returns, with what the session thread has to write. */
CHIAKI_SHIM_API int32_t chiaki_shim_stream_handover_finish(
		void *handover, int32_t error, const char *reason);

/**
 * What the session thread calls in place of the run: blocks until finish, then answers its error.
 *
 * The reason is left where `chiaki_shim_stream_handover_reason` can read it, rather than returned,
 * because a NULL reason is a case the C already has - PP371 found both reads dereferencing it -
 * and an out parameter would make the absent one indistinguishable from an empty one.
 */
CHIAKI_SHIM_API int32_t chiaki_shim_stream_handover_await_finish(void *handover, int32_t timeout_ms);

/** The remote disconnect reason the managed side reported, or NULL where it reported none. */
CHIAKI_SHIM_API const char *chiaki_shim_stream_handover_reason(void *handover);

/**
 * PP696: install a handover as a session's stream phase, and as what its stop reaches.
 *
 * The session thread's run step is a callback now, because streamconnection.c has left the build.
 * This is what fills it: a C trampoline over `handover`, which starts it, waits in slices until it
 * finishes or the session is stopped, and hands back the reason the handover holds - borrowed, for
 * the session to copy.
 *
 * Both callbacks take the same handover, so one install is the whole wiring. A session with none
 * reaches the stream phase and answers UNINITIALIZED rather than pretending to stream.
 */
/**
 * PP766: the three things a managed BIG needs out of a live session.
 *
 * The BIG is the message that starts a stream, and it is the one part of a run host that cannot be
 * composed from managed pieces: the session id comes out of ctrl's handshake, the mtu and round
 * trip out of senkusha, and the ecdh public key and its signature exist only for the span the
 * stream does.
 *
 * `chiaki_shim_session_id` writes a zero-terminated id and refuses a buffer too small for one,
 * rather than truncating - a short id is a different id.
 *
 * `chiaki_shim_session_transport` answers all three numbers or none: the launch spec spends them
 * together, and a caller holding two would describe a link nobody measured.
 *
 * `chiaki_shim_session_ecdh_material` COPIES the key and the signature out, because session.c
 * creates the pair on the line before the run and frees it on the line after. Both sizes are in and
 * out: the caller offers room and is told what was written. It takes a session rather than an ecdh
 * because the signature is over the session's handshake key, and the two are only meaningful
 * together.
 */
CHIAKI_SHIM_API bool chiaki_shim_session_id(void *session, char *out, int32_t capacity);

CHIAKI_SHIM_API bool chiaki_shim_session_transport(
		void *session, uint32_t *out_mtu_in, uint32_t *out_mtu_out, uint64_t *out_rtt_us);

/**
 * The handshake key, which is the fourth and was not in this task's first reading.
 *
 * It signs the ecdh material AND is base64'd into the launch spec's JSON, so the managed side needs
 * it in its own right and not only inside a signature. Sixteen bytes; a smaller buffer is refused
 * rather than partly filled, because a short key base64s to a shorter string and a console refuses
 * the spec with nothing said about why.
 */
CHIAKI_SHIM_API bool chiaki_shim_session_handshake_key(void *session, uint8_t *out, int32_t capacity);

CHIAKI_SHIM_API bool chiaki_shim_session_ecdh_material(
		void *session,
		uint8_t *out_pub_key, int32_t *pub_key_size,
		uint8_t *out_sig, int32_t *sig_size);

/**
 * PP773: the other half of the same pair - the secret the console's bang derives.
 *
 * `chiaki_shim_session_ecdh_material` sends the local public key out in the BIG and this takes the
 * console's answer back in. It has to be the SESSION's ecdh and not a fresh one: the private key
 * that signs the outbound half is the only one that can derive against the console's reply, and
 * `chiaki_shim_ecdh_create` makes a pair no console has ever seen.
 *
 * The handshake key is the session's for the same reason the material function gives - it is what
 * both signatures are over - so this takes a session and never asks the caller for one.
 *
 * CHIAKI_ECDH_SECRET_SIZE bytes out, and a smaller buffer is refused rather than partly filled: a
 * short secret keys a session the console cannot read and nothing says why.
 */
CHIAKI_SHIM_API bool chiaki_shim_session_derive_secret(
		void *session,
		const uint8_t *remote_key, int32_t remote_key_size,
		const uint8_t *remote_sig, int32_t remote_sig_size,
		uint8_t *out_secret, int32_t secret_capacity);

CHIAKI_SHIM_API void chiaki_shim_stream_run_install(void *session, void *handover);

/**
 * PP768: end both waits and mark the handover stopped, so a waiter can be shut down.
 *
 * Without this a caller holding a thread inside await_start had no correct way to stop it: start
 * would end the wait and make a runner build a host and open a socket, and finish ends the other
 * wait. Freeing the object the thread is blocked on is what a caller did instead, and a wait on
 * freed memory fails intermittently rather than reliably.
 *
 * The stopped flag is set BEFORE either signal, because a waiter reads it the moment its wait
 * returns - the other order lets one see a start with stopped still false and go on to build.
 */
CHIAKI_SHIM_API int32_t chiaki_shim_stream_handover_cancel(void *handover);

/**
 * PP769: the socket the session handed the run, or -1 where it handed none.
 *
 * The C's stream connection does not open a socket - chiaki_takion_connect takes the caller's, and
 * for the stream phase that is data_sock, which senkusha established and measured the link on. A
 * managed run that opened its own started a second conversation on the well-known port and the
 * console did not answer it, which PP759's contract had reasoned would be fine.
 *
 * BORROWED. The session owns it and frees it after the run returns, so the far side wraps it
 * without owning it and closes nothing.
 */
CHIAKI_SHIM_API int64_t chiaki_shim_stream_handover_socket(void *handover);

/** Whether the session's stop has reached this handover. */
CHIAKI_SHIM_API bool chiaki_shim_stream_handover_stopped(void *handover);

/**
 * chiaki_error_string for a ChiakiErrorCode, as a UTF-8 string the caller does not own.
 *
 * Here because it is the smallest thing that proves a real property of the seam: a pointer to a
 * static string crossing into managed memory. An unknown code has an answer rather than a null,
 * which is libchiaki's behaviour and not a decision taken here.
 */
CHIAKI_SHIM_API const char *chiaki_shim_error_string(int32_t error_code);

/**
 * The hardware decoder chiaki_decoder_choice picks, flattened to scalars.
 *
 * The first real function across the seam is this one on purpose. PP77 made the choice a pure
 * function in lib/ so that the branch holding PP51's non-NVIDIA floor could be asserted at all,
 * and noted that whatever answered it should also serve the port rather than let it re-derive
 * the same decision in C#. This is that: the managed host asks the same function the Qt client
 * asks, and the assertion that d3d11va survives an OpenGL window on a machine with no NVIDIA
 * card is one assertion about one implementation rather than two about two.
 *
 * The struct is flattened into arguments rather than marshalled. Six scalars have no layout to
 * disagree about, and the seam is where a layout disagreement costs the most to find.
 *
 * `requested` may be NULL. The returned string is static and is not the caller's to free.
 */
CHIAKI_SHIM_API const char *chiaki_shim_decoder_choice(
		bool vulkan_listed,
		bool cuda_listed,
		bool d3d11va_listed,
		bool nvidia_card,
		int32_t renderer,
		const char *requested);

/** Whether that answer still needs a vulkan device context from the window. */
CHIAKI_SHIM_API bool chiaki_shim_decoder_choice_needs_vulkan_context(const char *choice);

/**
 * The log, and the first crossing in the other direction.
 *
 * Every one of the 22 callbacks libchiaki takes has this shape - a function pointer and a `void
 * *user` handed over once, called back from whichever thread the library is on - so the cheapest
 * one is worth getting exactly right before the session lifecycle is written on top of it.
 * ChiakiLogCb is that cheapest one: it needs no console, no socket and no key, and it is also the
 * first thing a ChiakiSession is handed, so nothing above it can be attempted without it.
 *
 * Three decisions are taken here rather than in C#.
 *
 *   - The ChiakiLog is allocated here and lives at a fixed address. libchiaki keeps the pointer
 *     it is given for the whole life of a session, so it cannot be a managed struct however it
 *     is pinned: the port would then be one GC compaction away from a callback into freed memory,
 *     and the symptom would arrive minutes into a stream rather than at the call that caused it.
 *   - What crosses is the handle, never the struct. The managed side never learns that a
 *     ChiakiLog has three fields, so a libchiaki that grows a fourth does not silently move the
 *     bytes a P/Invoke was reading.
 *   - The trampoline is C. libchiaki's callback takes a ChiakiLogLevel, and an enum's underlying
 *     type is the compiler's choice; casting a managed `int` function pointer into that slot
 *     would be a bet that MinGW picked `int` today and will tomorrow. So the shim installs a
 *     function of its own and re-emits the level as an int32_t, which has no such question.
 */
typedef void (*ChiakiShimLogCb)(int32_t level, const char *msg, void *user);

/**
 * A log whose messages arrive at `cb`, with `user` handed back untouched.
 *
 * `level_mask` is the OR of the CHIAKI_LOG_* bits to let through - the same mask libchiaki
 * filters on, applied before the callback rather than inside it, so a debug-level flood costs
 * nothing when it is off. NULL if the allocation failed.
 */
CHIAKI_SHIM_API void *chiaki_shim_log_create(uint32_t level_mask, ChiakiShimLogCb cb, void *user);

/** Releases the log. `cb` is not called again after this returns; NULL is a no-op. */
CHIAKI_SHIM_API void chiaki_shim_log_free(void *log);

/** Re-masks an existing log, which is what a verbosity setting changed mid-session does. */
CHIAKI_SHIM_API void chiaki_shim_log_set_level(void *log, uint32_t level_mask);

/** The mask currently in force, so the managed side can assert the filter rather than assume it. */
CHIAKI_SHIM_API uint32_t chiaki_shim_log_level_mask(void *log);

/**
 * Writes one message through chiaki_log, exactly as a library call would.
 *
 * Not a test hook: the port logs its own lines into the same log libchiaki writes to, so they
 * interleave in one file in the order they happened. It goes through chiaki_log rather than
 * straight to the callback so that the mask, the formatting and the over-long heap path are the
 * library's and not a second implementation - `msg` is passed as an argument to "%s" and never as
 * the format, so a message containing a percent sign is text and not a read off the stack.
 */
CHIAKI_SHIM_API void chiaki_shim_log_write(void *log, int32_t level, const char *msg);

/** chiaki_log_level_char: 'I', 'W', 'E'… the letter that build's log file is written with. */
CHIAKI_SHIM_API char chiaki_shim_log_level_char(int32_t level);

/**
 * PP323: the message tap, which is the log's opposite and the reason it exists.
 *
 * PP297 needs a recorded exchange to port the four untested modules against, and the log cannot
 * be the source. The session bytes reach a managed caller as a hexdump PP320 redacts WHOLE - it has
 * to, because a formatted row cannot be redacted by field without leaving the tail of a key on the
 * next one - and ctrl logs a type and a size and never a payload.
 *
 * What crosses here is not text. A direction, a channel, a message type and the bytes, so that the
 * thing which redacts can name a field instead of guessing at a row. lib/src emits at exactly four
 * points, each the moment the message is plaintext: see chiaki/messagetap.h.
 *
 * The trampoline is C for the reason the log's is (see ChiakiShimLogCb): libchiaki's callback takes
 * an enum whose underlying type is the compiler's choice, and this re-emits it as int32_t.
 *
 * THE PAYLOAD DOES NOT OUTLIVE THE CALL, and for the two ctrl sites it does not even outlive it
 * intact: the send site's buffer is encrypted in place a statement later. A handler that keeps the
 * pointer reads ciphertext rather than crashing, which is worth naming because it looks like
 * corruption and not like a bug.
 */
typedef void (*ChiakiShimTapCb)(
		int32_t direction,
		const char *channel,
		uint16_t type,
		const uint8_t *payload,
		int32_t payload_size,
		void *user);

/** Installs the tap, or clears it with NULL. Set it before a session starts; see the header. */
CHIAKI_SHIM_API void chiaki_shim_tap_set(ChiakiShimTapCb cb, void *user);

/** Whether anything is listening, so the managed side can assert the install rather than assume it. */
CHIAKI_SHIM_API bool chiaki_shim_tap_active(void);

/**
 * Emits one message through chiaki_message_tap_emit, exactly as a library site would.
 *
 * Not a test hook, for the same reason chiaki_shim_log_write is not one: it goes through the
 * library's own emit rather than straight to the callback, so what a caller exercises is the one
 * implementation the four sites use. Without it the tap could only be checked by running a session
 * against a console, which is the thing PP297 does not have and this exists to make possible.
 */
CHIAKI_SHIM_API void chiaki_shim_tap_emit(
		int32_t direction, const char *channel, uint16_t type,
		const uint8_t *payload, int32_t payload_size);

/**
 * chiaki_lib_init, which nothing on the managed side had called.
 *
 * It is not a formality. It seeds rand, builds jerasure's Galois field - which the frame
 * processor needs before the first FEC block - and calls WSAStartup, without which every socket
 * libchiaki opens fails with WSANOTINITIALISED. The Qt client calls it in main(); a .NET host has
 * no main() the library knows about, so the first session-shaped call from managed code would
 * have failed on a Windows error nothing in this tree names.
 *
 * Idempotent: WSAStartup is reference counted and the other two are writes, so calling it twice
 * is calling it once. Returns a ChiakiErrorCode; 0 is CHIAKI_ERR_SUCCESS.
 */
CHIAKI_SHIM_API int32_t chiaki_shim_lib_init(void);

/**
 * A ChiakiConnectInfo, built field by field from managed code and never marshalled.
 *
 * The struct has sixteen members, two of them fixed-size byte arrays, one a nested video profile
 * and two - the holepunch session and the rudp socket - types whose own layout the managed side
 * would then also be tracking. A [StructLayout] over that is a promise about MinGW's padding on
 * every future libchiaki, kept by nothing, and broken silently: the wrong bytes still parse as a
 * plausible resolution and a key that simply fails to open a session.
 *
 * So it is built here. The handle is opaque, the setters take scalars, and the two byte arrays are
 * copied under a length that is checked rather than trusted.
 *
 * What is not settable yet is the PSN path - holepunch session, rudp socket, account id - because
 * a connect info that carries them is a session opened through PSN's relay and that is PP7's
 * ground, not this one's. They stay zeroed, which is the local-network session the Qt client
 * builds when the same fields are absent.
 */
CHIAKI_SHIM_API void *chiaki_shim_connect_info_create(void);
CHIAKI_SHIM_API void chiaki_shim_connect_info_free(void *info);

/** The console's address. Copied here, so the caller's string need not outlive the call. */
CHIAKI_SHIM_API bool chiaki_shim_connect_info_set_host(void *info, const char *host);

/** PS5 or PS4, which is the only thing that picks the target the session negotiates with. */
CHIAKI_SHIM_API void chiaki_shim_connect_info_set_ps5(void *info, bool ps5);

/**
 * The registration key, zero-padded into its 16 bytes as libchiaki requires.
 *
 * `len` is checked rather than trusted: a key one byte over would otherwise write past a field
 * that sits directly in front of `morning`, and the resulting session fails at a handshake step
 * with no clue which of the two was wrong. False and nothing written when it does not fit.
 */
CHIAKI_SHIM_API bool chiaki_shim_connect_info_set_regist_key(
		void *info, const uint8_t *key, int32_t len);

/** The 16-byte morning key, which must be exactly that. False and nothing written otherwise. */
CHIAKI_SHIM_API bool chiaki_shim_connect_info_set_morning(
		void *info, const uint8_t *morning, int32_t len);

/**
 * chiaki_connect_video_profile_preset: the resolution and fps presets, resolved in C.
 *
 * The bitrate that comes with each preset is the part worth not re-deriving - 15000 for 1080p is
 * a number in one switch statement in session.c, and a port that copied it into C# would carry a
 * second copy that nothing compares.
 */
CHIAKI_SHIM_API void chiaki_shim_connect_info_set_video_preset(
		void *info, int32_t resolution, int32_t fps);

/**
 * The two overrides Settings applies on top of the preset, in the order it applies them.
 *
 * settings.cpp reads a preset, then replaces the bitrate when the stored one is non-zero and
 * replaces the codec unconditionally - which matters, because
 * chiaki_connect_video_profile_preset always writes CHIAKI_CODEC_H264. A port that took the
 * preset as final would stream H264 on every PS5, whose default is H265.
 */
CHIAKI_SHIM_API void chiaki_shim_connect_info_set_bitrate(void *info, uint32_t bitrate);
CHIAKI_SHIM_API void chiaki_shim_connect_info_set_codec(void *info, int32_t codec);

/** Reads the profile back out as scalars, so the preset above can be asserted and not assumed. */
CHIAKI_SHIM_API void chiaki_shim_connect_info_video_profile(
		void *info,
		uint32_t *width,
		uint32_t *height,
		uint32_t *max_fps,
		uint32_t *bitrate,
		int32_t *codec);

/** The four booleans a settings screen writes, set together because they are read together. */
CHIAKI_SHIM_API void chiaki_shim_connect_info_set_flags(
		void *info,
		bool video_profile_auto_downgrade,
		bool enable_keyboard,
		bool enable_dualsense,
		bool enable_idr_on_fec_failure);

/** settings/packet_loss_max, whose 0.05 default PP2 already carries on the managed side. */
CHIAKI_SHIM_API void chiaki_shim_connect_info_set_packet_loss_max(void *info, double packet_loss_max);

/**
 * chiaki_session_init over that connect info, with the log from above.
 *
 * This is the lifecycle's first end and it is reachable with no console on the network:
 * chiaki_session_init resolves the host, allocates the ctrl and stream connection and starts no
 * thread, so it either builds or says why. `error_out` takes the ChiakiErrorCode - notably
 * CHIAKI_ERR_PARSE_ADDR for a host that does not resolve - and the return is NULL whenever that
 * is not CHIAKI_ERR_SUCCESS.
 *
 * `log` may be NULL, which is libchiaki's own "print to stdout"; passing one made by
 * chiaki_shim_log_create is what puts the session's own lines in front of a managed handler.
 * The log must outlive the session, which is the first ownership rule this seam has that the
 * managed side cannot check for itself.
 */
CHIAKI_SHIM_API void *chiaki_shim_session_create(void *connect_info, void *log, int32_t *error_out);

/** chiaki_session_fini and the allocation with it. NULL is a no-op. */
CHIAKI_SHIM_API void chiaki_shim_session_free(void *session);

/**
 * PP700: the decoder a session decodes into, which nothing in this port had.
 *
 * The session's video_sample_cb is the join. libchiaki hands it every assembled frame, and
 * chiaki_ffmpeg_decoder_video_sample_cb is the C's own implementation of it - so installing that,
 * with a decoder as its user, is the whole of what makes a session decode. No path here did it, and
 * every stream reached the frame processor and stopped.
 *
 * `hw_decoder_name` is the setting's own string - "vulkan", "cuda", "d3d11va" - and NULL asks for
 * software. A name the machine has no device for is REFUSED rather than falling back, which is how
 * a missing driver says so instead of decoding on the CPU and looking slow.
 */
CHIAKI_SHIM_API void *chiaki_shim_decoder_create(
		void *log, int32_t codec, int32_t max_fps, const char *hw_decoder_name, int32_t *error_out);

/** chiaki_ffmpeg_decoder_fini and the allocation with it. NULL is a no-op. */
CHIAKI_SHIM_API void chiaki_shim_decoder_free(void *decoder);

/**
 * Installs the decoder as the session's video sink. Set it before chiaki_shim_session_start, for
 * the reason the event callback carries: the field is read by the stream connection's own thread,
 * and installing it after that thread exists is a race whose losing side decodes nothing.
 *
 * The session BORROWS the decoder and frees nothing - the same rule the log has.
 */
CHIAKI_SHIM_API bool chiaki_shim_session_set_decoder(void *session, void *decoder);

/**
 * PP76: an event set whenever a frame becomes available, so a reader waits rather than polls.
 *
 * chiaki_ffmpeg_decoder_pull_frame DRAINS the codec and returns only the last frame - its own
 * comment says so - and counts none of the ones it discards. A reader that polls therefore
 * accumulates frames between its ticks and loses them silently, which measures its own interval
 * under the decoder's name. The Qt client pulls from the frame-available callback and has no gap.
 *
 * A Win32 event and not a managed callback: this is set on libchiaki's own thread, and SetEvent
 * cannot throw, allocate or enter a runtime - which a delegate crossing the seam would do sixty
 * times a second inside the packet path.
 *
 * BORROWED. The caller owns the handle and must clear this to NULL before closing it.
 */
CHIAKI_SHIM_API void chiaki_shim_decoder_set_ready_event(void *decoder, void *event);

/** How many times the decoder reported a frame ready. Zero is a session that decoded nothing. */
CHIAKI_SHIM_API uint64_t chiaki_shim_decoder_frames_available(void *decoder);

/** Frames the codec has handed back - the total a reader's shown-plus-swallowed can close against. */
CHIAKI_SHIM_API uint64_t chiaki_shim_decoder_frames_decoded(void *decoder);

/**
 * The AVPixelFormat the decoder resolved, or -1 with no decoder.
 *
 * This is what says whether the hardware path was taken: PP48 measured the per-frame copy libchiaki
 * runs for any hardware frame that is NOT AV_PIX_FMT_VULKAN, so the value here is the difference
 * between a decoder that costs nothing to hand on and one that costs 2253us a frame.
 */
CHIAKI_SHIM_API int32_t chiaki_shim_decoder_pixel_format(void *decoder);

/**
 * Its NAME, written into `buf`, and the length. The managed side cannot name an AVPixelFormat:
 * pixfmt.h's enum is sequential and unnumbered, so a literal over there is a guess a different
 * ffmpeg quietly invalidates. av_get_pix_fmt_name is the C's own answer.
 */
CHIAKI_SHIM_API int32_t chiaki_shim_decoder_pixel_format_name(void *decoder, char *buf, int32_t buf_size);

/**
 * Whether libchiaki copies every frame out of this format.
 *
 * PP48 measured the per-frame copy make_fallback_snapshot_frame runs for any hardware frame that is
 * not AV_PIX_FMT_VULKAN - 793us on cuda, 2253us on d3d11va, nothing on vulkan. The comparison is
 * here rather than managed for the reason above: AV_PIX_FMT_VULKAN is an unnumbered enum member.
 *
 * A software format copies too and says so, which makes this "is the no-copy path" rather than "is
 * hardware" - two questions with the same answer on exactly one format.
 */
CHIAKI_SHIM_API bool chiaki_shim_decoder_copies_every_frame(void *decoder);

/**
 * The format a FRAME carries, which is the hardware one where there is a device.
 *
 * Not the same as chiaki_shim_decoder_pixel_format, and the difference is what made the first
 * version of copies_every_frame wrong: that one returns the format after a DOWNLOAD - NV12 or P010
 * with a hardware context, YUV420P otherwise - while a vulkan decoder's frames arrive as
 * AV_PIX_FMT_VULKAN. A caller comparing the wrong one gets "copied per frame" on a decoder that
 * copies nothing.
 */
CHIAKI_SHIM_API int32_t chiaki_shim_decoder_frame_format(void *decoder);

/** Any AVPixelFormat's name, so a caller can print one it did not expect. */
CHIAKI_SHIM_API int32_t chiaki_shim_pixel_format_name(int32_t format, char *buf, int32_t buf_size);

/**
 * PP700: one decoded frame's planes, BORROWED until the next pull.
 *
 * chiaki_ffmpeg_decoder_pull_frame hands over an AVFrame the caller owns. Handing its plane
 * pointers to managed code and letting that side free it would put an av_frame_free across the
 * seam, which is the ownership rule this shim exists to avoid - so the frame stays here and its
 * pointers are valid until the next pull or the free.
 *
 * True only for an NV12 frame, and that is a statement rather than a hidden limitation. The
 * presenter takes two planes; a software decoder resolves to yuv420p, which is three. `out_format`
 * carries the AVPixelFormat either way, so a caller that asked for hardware and got software sees
 * it rather than seeing a picture assembled by a converter nobody measured.
 *
 * `out_lost` is what the decoder accumulated - PP528's repaired counter - and the pull ZEROES it,
 * so this call is the only place it can ever be read.
 *
 * PP76: `out_superseded` is how many decoded frames this pull THREW AWAY.
 * chiaki_ffmpeg_decoder_pull_frame drains the codec and keeps only the last - its own comment says
 * so - and counts none of the rest. They are decoded frames nobody will ever see, which is exactly
 * what the C means by frames_dropped, and this subtraction against the callback count is the only
 * place the number exists at all. Without it a caller can infer a total and cannot attribute it.
 */
CHIAKI_SHIM_API bool chiaki_shim_decoder_pull(
		void *decoder,
		int32_t *out_w, int32_t *out_h,
		uint8_t **out_luma, int32_t *out_luma_stride,
		uint8_t **out_chroma, int32_t *out_chroma_stride,
		int32_t *out_format, int32_t *out_lost, int32_t *out_superseded);

/** chiaki_quit_reason_string, which is the sentence a disconnect screen shows. */
CHIAKI_SHIM_API const char *chiaki_shim_quit_reason_string(int32_t reason);

/**
 * The session's event callback, which is the one every screen above the stream is driven by.
 *
 * ChiakiEvent is a tagged union of seventeen shapes, three of them structs with their own arrays,
 * and it is exactly what must not cross: a managed union over that is a layout promise per arm,
 * and the arm that is wrong is the one nobody exercises until a console sends it.
 *
 * So the type crosses as an int32_t and the payload is decoded here, arm by arm, as the screens
 * that need each one land. What is decoded today is CHIAKI_EVENT_QUIT, because it is the arm that
 * ends every session and the one a disconnect screen shows. Every other type still arrives, with
 * its payload arguments zeroed, so a host can already know that CONNECTED happened; rumble goes
 * with the input path (PP8), the keyboard arms with the controls (PP12) and the nickname with the
 * console list (PP13).
 *
 * `quit_reason_str` is not the message. It is NULL on every failure that never reached a console,
 * because session.c only fills it from a disconnect reason the console itself sent - the sentence
 * a screen shows is chiaki_quit_reason_string(quit_reason), with this appended when it is there,
 * which is what qmlmainwindow's own dialog does. It is also only valid for the duration of the
 * call: it points at the session's storage and the event is gone when the callback returns.
 *
 * The callback runs on the session thread, which is a thread the caller never created.
 */
typedef void (*ChiakiShimEventCb)(
		int32_t type, int32_t quit_reason, const char *quit_reason_str, void *user);

/** Installs the callback. Set it before chiaki_shim_session_start or the first events are lost. */
CHIAKI_SHIM_API bool chiaki_shim_session_set_event_cb(
		void *session, ChiakiShimEventCb cb, void *user);

/**
 * chiaki_session_start: spawns the session thread and returns at once.
 *
 * Everything a session does after this happens on that thread and is reported through the event
 * callback. A start that returns CHIAKI_ERR_SUCCESS says a thread exists, not that a console
 * answered - the answer arrives as CHIAKI_EVENT_QUIT when it does not.
 */
CHIAKI_SHIM_API int32_t chiaki_shim_session_start(void *session);

/** chiaki_session_stop: asks the session thread to wind up. Does not wait for it. */
CHIAKI_SHIM_API int32_t chiaki_shim_session_stop(void *session);

/**
 * chiaki_session_join: waits for the session thread to end.
 *
 * Required before chiaki_shim_session_free on a session that was started, because fini tears down
 * the mutex and the stop pipe the thread is still using.
 */
CHIAKI_SHIM_API int32_t chiaki_shim_session_join(void *session);

/**
 * PP627: chiaki_session_set_login_pin - the answer to the one event that asks for one.
 *
 * CHIAKI_EVENT_LOGIN_PIN_REQUEST is the only event libchiaki raises that needs something back. The
 * session thread waits on it with no timeout at all - UINT64_MAX - because a person typing is not
 * something a network timeout should interrupt, so a session whose console asks and whose caller
 * never answers sits there until ctrl fails or somebody stops it.
 *
 * A null or empty pin is refused rather than forwarded. The C mallocs pin_size bytes and sets
 * login_pin_entered, so a zero-size call wakes the thread with nothing to read - and PP345 settled
 * that a spent PIN cannot be retried, so the cost of an empty one is a prompt nobody sees again.
 */
CHIAKI_SHIM_API int32_t chiaki_shim_session_set_login_pin(
	void *session, const uint8_t *pin, size_t pin_size);

/**
 * The controller state, which is what the session sends upstream sixty times a second.
 *
 * ChiakiControllerState is twenty-one scalars, a two-element touch array and ten floats of motion.
 * Marshalling it per frame would be the seam's hottest path AND its most detailed layout promise
 * at the same time, which is the worst combination available: an offset that is wrong by two
 * bytes turns into a stick drift nobody can trace to a struct.
 *
 * So it is built here and pushed by handle. The touch functions are libchiaki's own - the id is
 * allocated by chiaki_controller_state_start_touch and is -1 when both slots are taken - because
 * a port that allocated its own would disagree with the console about which finger left.
 *
 * chiaki_shim_session_controller_state_matches is what closes the round trip, and it closes it
 * with chiaki_controller_state_equals rather than with a field walk written here: the comparison
 * that decides whether the state arrived is the library's, so it cannot agree with a transcription
 * this file also made.
 */
CHIAKI_SHIM_API void *chiaki_shim_controller_state_create(void);
CHIAKI_SHIM_API void chiaki_shim_controller_state_free(void *state);

/** chiaki_controller_state_set_idle: the state a pad that is being held still reports. */
CHIAKI_SHIM_API void chiaki_shim_controller_state_set_idle(void *state);

/** The ChiakiControllerButton bitmask, plus the two analog-button bits above it. */
CHIAKI_SHIM_API void chiaki_shim_controller_state_set_buttons(void *state, uint32_t buttons);
CHIAKI_SHIM_API uint32_t chiaki_shim_controller_state_buttons(void *state);

/** L2 and R2, which are pressures and not the bits in the mask above. */
CHIAKI_SHIM_API void chiaki_shim_controller_state_set_triggers(void *state, uint8_t l2, uint8_t r2);
CHIAKI_SHIM_API void chiaki_shim_controller_state_triggers(void *state, uint8_t *l2, uint8_t *r2);

/** Both sticks, signed and centred on zero. */
CHIAKI_SHIM_API void chiaki_shim_controller_state_set_sticks(
		void *state, int16_t left_x, int16_t left_y, int16_t right_x, int16_t right_y);
CHIAKI_SHIM_API void chiaki_shim_controller_state_sticks(
		void *state, int16_t *left_x, int16_t *left_y, int16_t *right_x, int16_t *right_y);

/** Gyro, accelerometer and orientation, in that order. */
CHIAKI_SHIM_API void chiaki_shim_controller_state_set_motion(
		void *state,
		float gyro_x, float gyro_y, float gyro_z,
		float accel_x, float accel_y, float accel_z,
		float orient_x, float orient_y, float orient_z, float orient_w);

/** The library's own touch allocation: a non-negative id, or -1 when both slots are taken. */
CHIAKI_SHIM_API int8_t chiaki_shim_controller_state_start_touch(
		void *state, uint16_t x, uint16_t y);
CHIAKI_SHIM_API void chiaki_shim_controller_state_stop_touch(void *state, uint8_t id);
CHIAKI_SHIM_API void chiaki_shim_controller_state_set_touch_pos(
		void *state, uint8_t id, uint16_t x, uint16_t y);

/** One touch slot, read back. False when `slot` is out of range; `id` is -1 for a finger that is up. */
CHIAKI_SHIM_API bool chiaki_shim_controller_state_touch(
		void *state, int32_t slot, uint16_t *x, uint16_t *y, int32_t *id);

/** chiaki_controller_state_equals, which is the only comparison this seam uses. */
CHIAKI_SHIM_API bool chiaki_shim_controller_state_equals(void *a, void *b);

/**
 * chiaki_controller_state_or: the union of two pads, and not a union at all in three places.
 *
 * A session with a pad, a keyboard and a touchpad has three states to send as one, and this is
 * what merges them. It is worth reaching for rather than rewriting because none of its three
 * interesting rules is what "or" suggests: the sticks take the larger MAGNITUDE and keep its sign,
 * a touch slot prefers whichever side has a finger in it, and the motion axes are taken WHOLE from
 * the first state that has any rather than combined - mixing gyro and accelerometer readings from
 * two devices produces an orientation that belongs to neither.
 *
 * `out` may alias `a`, which is how the Qt client folds a list of controllers into one state.
 */
CHIAKI_SHIM_API void chiaki_shim_controller_state_or(void *out, void *a, void *b);

/** chiaki_session_set_controller_state, under the lock the feedback sender reads it through. */
CHIAKI_SHIM_API int32_t chiaki_shim_session_set_controller_state(void *session, void *state);

/** Whether what the session is holding equals `state`, by the library's own comparator. */
CHIAKI_SHIM_API bool chiaki_shim_session_controller_state_matches(void *session, void *state);

/**
 * The session baseline: one JSON line per session, appended to the file both builds share.
 *
 * This is the ledger PP46 compares the two clients with, so the one thing the managed host must
 * not do is write its own JSON. A second formatter would drift a key or a rounding and the rows
 * would stop being comparable - which is the only thing the file is for. So the record is
 * libchiaki's struct, filled through these setters and formatted by
 * chiaki_session_baseline_format, and the .NET host contributes rows rather than a format.
 *
 * chiaki_shim_baseline_schema is what makes that checkable: the managed side pins the number it
 * was written against, and a libchiaki that bumps it turns an assertion red instead of appending
 * rows that a reader silently mixes with the old ones.
 *
 * Nothing here takes a console name, an address, a session id or an account. That is the record's
 * own design and not an omission at this seam: the identifying fields are exactly the ones the
 * session log needs a sanitiser to remove, so they are not collected.
 */
CHIAKI_SHIM_API uint32_t chiaki_shim_baseline_schema(void);

/** The longest line chiaki_session_baseline_format can produce, so a caller can size a buffer. */
CHIAKI_SHIM_API int32_t chiaki_shim_baseline_line_max(void);

CHIAKI_SHIM_API void *chiaki_shim_baseline_create(void);
CHIAKI_SHIM_API void chiaki_shim_baseline_free(void *baseline);

/** The start time, taken rather than read off the clock, so a record can be reproduced. */
CHIAKI_SHIM_API void chiaki_shim_baseline_set_started(void *baseline, uint64_t unix_seconds);
CHIAKI_SHIM_API void chiaki_shim_baseline_set_duration_ms(void *baseline, uint64_t duration_ms);
CHIAKI_SHIM_API void chiaki_shim_baseline_set_app_version(void *baseline, const char *version);

/** The picture: what was asked for, which is what measured_bitrate_mbps is a shortfall against. */
CHIAKI_SHIM_API void chiaki_shim_baseline_set_video(
		void *baseline,
		const char *codec,
		uint32_t width,
		uint32_t height,
		uint32_t fps,
		uint32_t bitrate_kbps);

/** The settings that explain the numbers: the decoder, the renderer that allowed it, and the two
 *  network knobs. A row naming one without the other cannot be compared to another row. */
CHIAKI_SHIM_API void chiaki_shim_baseline_set_config(
		void *baseline,
		const char *hw_decoder,
		const char *renderer,
		double packet_loss_max,
		bool idr_on_fec_failure);

/** What the session achieved. */
CHIAKI_SHIM_API void chiaki_shim_baseline_set_measured(
		void *baseline,
		double measured_bitrate_mbps,
		double average_packet_loss,
		uint64_t frames_presented,
		uint64_t frames_lost,
		uint64_t frames_dropped,
		uint64_t network_rtt_us);

/** One decoder-to-present handoff sample, folded into the histogram as it arrives. */
CHIAKI_SHIM_API void chiaki_shim_baseline_push_handoff(void *baseline, uint64_t handoff_us);

/** One controller-state-to-wire sample, which is the input half of the delay a client can see. */
CHIAKI_SHIM_API void chiaki_shim_baseline_push_input_to_wire(void *baseline, uint64_t input_us);

CHIAKI_SHIM_API uint64_t chiaki_shim_baseline_handoff_avg_us(void *baseline);

/** Input queueing plus the network round trip plus the handoff: a floor on glass to glass. */
CHIAKI_SHIM_API uint64_t chiaki_shim_baseline_latency_estimate_us(void *baseline);

/**
 * PP76: frames_dropped less frames_lost, which is the only decoder-attributable loss either
 * counter can give.
 *
 * Neither is a decoder's own: frames_lost is the video receiver's total, counted upstream of every
 * decoder, and frames_dropped is what the presenter never showed. Their difference is a FLOOR on
 * what the decoder lost rather than a count of it, and reading either alone is what §PP76 exists to
 * prevent. Clamped rather than wrapped - the two are sampled by different threads, so the receiver
 * can legitimately be ahead at the end of a session.
 */
CHIAKI_SHIM_API uint64_t chiaki_shim_baseline_decoder_drops(void *baseline);

/** The line, as the Qt build writes it. `written` may be NULL. Returns a ChiakiErrorCode. */
CHIAKI_SHIM_API int32_t chiaki_shim_baseline_format(
		void *baseline, char *buf, int32_t buf_size, int32_t *written);

/** Appends the line to the ledger at `path`, creating it if it is not there. */
CHIAKI_SHIM_API int32_t chiaki_shim_baseline_append(void *baseline, const char *path);

/**
 * PP23: one baseline statistic on its own, so the managed side can reach the PERCENTILE.
 *
 * The baseline handle above exposes an average and nothing else, and the average is the number
 * sessionbaseline.h itself warns about: ten stalls in a thousand frames drag the mean to 1990us
 * while ninety-nine percent of frames were at 1000. Reading it overstates the typical frame by two
 * and understates the worst by fifty, in one number - which is the whole reason the statistic keeps
 * a distribution rather than a running total.
 *
 * ChiakiSessionBaselineStat is a COMPLETE type in the public header, so a port could mirror its
 * layout and P/Invoke the library directly. It is behind a handle here for the reason every other
 * struct in this file is: a layout the managed side copies is a layout that goes wrong silently the
 * first time the C reorders a field, and nothing about a histogram makes it the exception.
 *
 * The bound is not the exact percentile and does not claim to be. The buckets are eight to the
 * octave, so the answer is the upper edge of the bucket the true value falls in - never below it,
 * and within 12.5%. Past the last bucket it is clamped to the observed maximum, which is why a
 * five-second stall reads as five seconds rather than as the top of the histogram.
 */
CHIAKI_SHIM_API void *chiaki_shim_baseline_stat_create(void);
CHIAKI_SHIM_API void chiaki_shim_baseline_stat_free(void *stat);

CHIAKI_SHIM_API void chiaki_shim_baseline_stat_push(void *stat, uint64_t sample_us);

CHIAKI_SHIM_API uint64_t chiaki_shim_baseline_stat_samples(void *stat);
CHIAKI_SHIM_API uint64_t chiaki_shim_baseline_stat_min_us(void *stat);
CHIAKI_SHIM_API uint64_t chiaki_shim_baseline_stat_max_us(void *stat);
CHIAKI_SHIM_API uint64_t chiaki_shim_baseline_stat_avg(void *stat);
CHIAKI_SHIM_API uint64_t chiaki_shim_baseline_stat_p50_us(void *stat);
CHIAKI_SHIM_API uint64_t chiaki_shim_baseline_stat_p99_us(void *stat);

/** Any percentile, so the two named ones above cannot drift from the general one. */
CHIAKI_SHIM_API uint64_t chiaki_shim_baseline_stat_percentile_us(void *stat, uint32_t percent);

/**
 * PP23: the five frame stages, which the baseline carries and nothing could fill.
 *
 * lib pushes these through the struct - `chiaki_session_baseline_stat_push(&b->stages.reorder, us)`
 * - so there is no function to bind and the port wrote five zeros whatever a session did. That is
 * worse than getting them wrong: test/sessionbaseline.c pushes one distinguishable value per stage
 * because "a stage filed under another stage's name" is the defect it exists to catch, and a row of
 * zeros cannot be caught by it.
 *
 * A selector rather than five entry points, because the five are one array in everything but C
 * syntax and five names here would be five places to bind the wrong member.
 *
 * There is NO sixth. The present stage is the handoff, which has its own push above - and a caller
 * that added it here would be counting it twice, once as a stage and once in the latency estimate.
 */
typedef enum chiaki_shim_baseline_stage
{
	CHIAKI_SHIM_BASELINE_STAGE_RECEIVE = 0,
	CHIAKI_SHIM_BASELINE_STAGE_REORDER = 1,
	CHIAKI_SHIM_BASELINE_STAGE_REASSEMBLE = 2,
	CHIAKI_SHIM_BASELINE_STAGE_CORRECT = 3,
	CHIAKI_SHIM_BASELINE_STAGE_DECODE = 4,
} chiaki_shim_baseline_stage;

/** One sample into one stage. Out-of-range selectors are ignored rather than folded into a stage. */
CHIAKI_SHIM_API void chiaki_shim_baseline_push_stage(
		void *baseline, int32_t stage, uint64_t sample_us);

/** How many samples one stage holds, so "separate" is assertable rather than assumed. */
CHIAKI_SHIM_API uint64_t chiaki_shim_baseline_stage_samples(void *baseline, int32_t stage);

/** And the same for the handoff, which is the present stage and not a sixth accumulator. */
CHIAKI_SHIM_API uint64_t chiaki_shim_baseline_handoff_samples(void *baseline);

/**
 * PP6: discovery, which is what fills the console list.
 *
 * What crosses here is the protocol and not the socket. .NET has UdpClient and libchiaki has its
 * own discovery service; whichever carries the datagram, the BYTES have to be the console's, and
 * they are the part a port gets wrong silently - a console that does not answer looks exactly like
 * a console that is switched off.
 *
 * So the packet is formatted by libchiaki, the ports and protocol versions are read from its
 * headers rather than copied, and the two classification rules come back through the functions
 * that already decide them for the Qt client.
 */
CHIAKI_SHIM_API int32_t chiaki_shim_discovery_port(bool ps5);

/** The device-discovery-protocol-version a search must carry, which is also what identifies a PS5. */
CHIAKI_SHIM_API const char *chiaki_shim_discovery_protocol_version(bool ps5);

/** The local reply port range, 9303..9319 inclusive. */
CHIAKI_SHIM_API int32_t chiaki_shim_discovery_local_port_min(void);
CHIAKI_SHIM_API int32_t chiaki_shim_discovery_local_port_max(void);

/**
 * chiaki_discovery_packet_fmt: the exact bytes of a SRCH or a WAKEUP.
 *
 * `cmd` is 0 for SRCH and 1 for WAKEUP. Returns what snprintf would - the length the packet WANTED,
 * so a value at or above `buf_size` means it was truncated - or negative for an unknown command or
 * a null protocol version.
 */
CHIAKI_SHIM_API int32_t chiaki_shim_discovery_packet_fmt(
		int32_t cmd,
		const char *protocol_version,
		uint64_t user_credential,
		char *buf,
		int32_t buf_size);

/**
 * Whether a reply came from a PS5, which is decided by the protocol version it announced and NOT
 * by its host type.
 */
CHIAKI_SHIM_API bool chiaki_shim_discovery_is_ps5(const char *device_discovery_protocol_version);

/**
 * The ChiakiTarget a reply resolves to, by libchiaki's own ladder over the two fields that decide
 * it. Built into a host struct here so the ladder is the library's rather than a copy of it.
 */
CHIAKI_SHIM_API int32_t chiaki_shim_discovery_target(
		const char *system_version, const char *device_discovery_protocol_version);

/** chiaki_discovery_host_state_string: what the console list shows beside a name. */
CHIAKI_SHIM_API const char *chiaki_shim_discovery_host_state_string(int32_t state);

/** Which string of a parsed reply to read, in the order ChiakiDiscoveryHost declares them. */
typedef enum chiaki_shim_discovery_field_t
{
	CHIAKI_SHIM_DISCOVERY_HOST_ADDR = 0,
	CHIAKI_SHIM_DISCOVERY_SYSTEM_VERSION,
	CHIAKI_SHIM_DISCOVERY_PROTOCOL_VERSION,
	CHIAKI_SHIM_DISCOVERY_HOST_NAME,
	CHIAKI_SHIM_DISCOVERY_HOST_TYPE,
	CHIAKI_SHIM_DISCOVERY_HOST_ID,
	CHIAKI_SHIM_DISCOVERY_RUNNING_APP_TITLEID,
	CHIAKI_SHIM_DISCOVERY_RUNNING_APP_NAME
} ChiakiShimDiscoveryField;

/**
 * A console's reply, parsed by libchiaki and owned here.
 *
 * The ownership is the reason this is a handle and not a set of out-parameters.
 * chiaki_http_response_parse works IN PLACE: it writes NULs into the caller's datagram and every
 * header value in the parsed host points into it, while chiaki_http_response_fini frees only the
 * list nodes. So a ChiakiDiscoveryHost is a set of pointers into a buffer somebody else owns, and
 * the buffer a shim function parsed from would be gone the moment it returned - handing .NET eight
 * pointers into freed memory that still read as the right strings for as long as nothing reused
 * the page.
 *
 * So the shim keeps its own copy of the datagram alive for exactly as long as the handle, and the
 * managed side copies each string out through the getter. Freeing the handle is what ends both.
 *
 * `from_addr` is the address the datagram came from, as text; it becomes the host_addr field, which
 * is what a connection is later made to. `error_out` takes a ChiakiErrorCode and the return is NULL
 * whenever that is not CHIAKI_ERR_SUCCESS.
 */
CHIAKI_SHIM_API void *chiaki_shim_discovery_reply_parse(
		const char *reply, int32_t reply_len, const char *from_addr, int32_t *error_out);

CHIAKI_SHIM_API void chiaki_shim_discovery_reply_free(void *host);

/** The ChiakiDiscoveryHostState the reply's status code mapped to. */
CHIAKI_SHIM_API int32_t chiaki_shim_discovery_reply_state(void *host);

/** host-request-port, which is the port a session is opened on. */
CHIAKI_SHIM_API int32_t chiaki_shim_discovery_reply_request_port(void *host);

/** One string field, or NULL where the reply did not carry it. */
CHIAKI_SHIM_API const char *chiaki_shim_discovery_reply_field(void *host, int32_t field);

/**
 * PP6, the rest of it: the discovery service - the socket, the search timer and the reply callback.
 *
 * This was filed as needing a console on the network. It does not: it needs an address that
 * answers, and the service sends its search to whatever `send_host` names rather than only to a
 * broadcast. Pointed at the loopback, a socket that replies is a console as far as it is
 * concerned - which is what makes the whole path testable with no hardware.
 *
 * `hosts` in the callback is libchiaki's own array, valid only for the duration of the call, and
 * read through chiaki_shim_discovery_service_host_field the way a reply is. Nothing here copies
 * it: a screen that wants to keep a console copies what it needs while it is being told.
 *
 * `ping_ms` is how often the search goes out, and `hosts_max` how many consoles may be remembered.
 */
typedef void (*ChiakiShimDiscoveryServiceCb)(void *hosts, int32_t hosts_count, void *user);

CHIAKI_SHIM_API void *chiaki_shim_discovery_service_create(
		void *log,
		const char *send_host,
		uint64_t ping_ms,
		int32_t hosts_max,
		ChiakiShimDiscoveryServiceCb cb,
		void *user);

CHIAKI_SHIM_API void chiaki_shim_discovery_service_free(void *service);

/** One field of one host in the array a callback was handed. Same field ids as a parsed reply. */
CHIAKI_SHIM_API const char *chiaki_shim_discovery_service_host_field(
		void *hosts, int32_t index, int32_t field);

/** That host's state and request port, which are not strings. */
CHIAKI_SHIM_API int32_t chiaki_shim_discovery_service_host_state(void *hosts, int32_t index);
CHIAKI_SHIM_API int32_t chiaki_shim_discovery_service_host_request_port(void *hosts, int32_t index);

/**
 * PP7: the client device id the PSN login carries.
 *
 * It identifies this installation to Sony's relay, and it is generated rather than chosen - which
 * is why it comes from libchiaki rather than from a Guid on this side. A port that made its own
 * would produce something of the right shape that the relay does not recognise.
 *
 * `size` is in/out: room offered, then characters written including the terminator. It needs at
 * least CHIAKI_SHIM_DUID_STR_SIZE.
 */
#define CHIAKI_SHIM_DUID_STR_SIZE 49

CHIAKI_SHIM_API int32_t chiaki_shim_duid_str_size(void);


/**
 * PP23: the registration crypto, reachable so that both implementations can be run on one input.
 *
 * The protocol has no specification, so the oracle for a managed rewrite is the C code it replaces
 * plus whatever real hardware already agreed to. For this module both exist: test/rpcrypt.c holds
 * nonces, morning keys and the exact bytes a console produced from them, and they are the closest
 * thing the key derivation has to a written-down truth.
 *
 * What crosses here is the derivation and not the struct. A ChiakiRPCrypt is a target and two
 * 16-byte keys, and handing that layout to .NET would put the port one libchiaki field away from
 * deriving a key that fails to open a session with no clue which of eight steps was wrong.
 */
#define CHIAKI_SHIM_RPCRYPT_KEY_SIZE 0x10

CHIAKI_SHIM_API int32_t chiaki_shim_rpcrypt_key_size(void);

/**
 * chiaki_rpcrypt_bright_ambassador: the two keys a nonce and a morning key derive to.
 *
 * `bright` and `ambassador` are each written with exactly CHIAKI_SHIM_RPCRYPT_KEY_SIZE bytes, and
 * `nonce` and `morning` are read for the same. False where any of them is null.
 */
CHIAKI_SHIM_API bool chiaki_shim_rpcrypt_bright_ambassador(
		int32_t target,
		uint8_t *bright,
		uint8_t *ambassador,
		const uint8_t *nonce,
		const uint8_t *morning);

/** A ChiakiRPCrypt initialised for the auth exchange, held here as an opaque handle. */
CHIAKI_SHIM_API void *chiaki_shim_rpcrypt_create_auth(
		int32_t target, const uint8_t *nonce, const uint8_t *morning);

/**
 * PP121: the registration-mode init, and the four recorded cases that needed it.
 *
 * Registration derives its keys from an ambassador and the PIN a user types, not from a nonce and
 * a morning key - a different schedule producing the same struct. test/rpcrypt.c records it on
 * both console generations, and none of those four cases was reachable from managed code until
 * this existed, which made registration the one flow with recorded answers and no comparison.
 *
 * key_0_off is an offset into the request payload the caller chose, not a constant: regist.c
 * takes it from a byte of the randomised header, so the same PIN on the same console derives
 * different keys per attempt. It is an argument here for the same reason.
 */
CHIAKI_SHIM_API void *chiaki_shim_rpcrypt_create_regist(
		int32_t target, const uint8_t *ambassador, int32_t key_0_off, uint32_t pin);

/** The derived bright key, copied out - the recorded registration cases assert on it directly. */
CHIAKI_SHIM_API bool chiaki_shim_rpcrypt_bright(void *rpcrypt, uint8_t *bright_out);

CHIAKI_SHIM_API void chiaki_shim_rpcrypt_free(void *rpcrypt);

/** chiaki_rpcrypt_generate_iv, which is what every counter's block is encrypted under. */
CHIAKI_SHIM_API int32_t chiaki_shim_rpcrypt_generate_iv(void *rpcrypt, uint64_t counter, uint8_t *iv);

CHIAKI_SHIM_API int32_t chiaki_shim_rpcrypt_encrypt(
		void *rpcrypt, uint64_t counter, const uint8_t *in, uint8_t *out, int32_t size);

CHIAKI_SHIM_API int32_t chiaki_shim_rpcrypt_decrypt(
		void *rpcrypt, uint64_t counter, const uint8_t *in, uint8_t *out, int32_t size);

/* PP696: declared only where the build exports them - the same define the bodies are behind.
 *
 * Left outside it, a header would promise fourteen entry points a bare DLL does not have, and the
 * first a caller reached would be a loader failure rather than a compile one. PP661 is why
 * chiaki_shim_has_framepath is exported on both sides: a reader keyed on this text cannot tell
 * which build it got, and that export answers for the DLL. */
#ifdef CHIAKI_SHIM_HAVE_FRAMEPATH

/**
 * PP23 and PP30: forward error correction, which is the module with the largest recorded oracle.
 *
 * test/fec_test_cases.inl is 3081 lines of erasure cases taken off a real stream - the frame
 * buffer as it arrived, which units were lost, and the bytes that had to come back. FEC runs on
 * every frame and is two vendored C libraries doing Galois field arithmetic, so a managed rewrite
 * is a port rather than a swap; these cases are what would judge it.
 *
 * `frame_buf` is decoded in place. `stride` is the distance between units, which the recorded
 * cases round up to a multiple of 16 - that padding is the layout the decoder expects, not a
 * convenience of the test. `erasures` names which unit indices were lost.
 *
 * chiaki_shim_lib_init must have run: the Galois field tables are built there.
 */
CHIAKI_SHIM_API int32_t chiaki_shim_fec_decode(
		uint8_t *frame_buf,
		int32_t unit_size,
		int32_t stride,
		uint32_t k,
		uint32_t m,
		const uint32_t *erasures,
		int32_t erasures_count);

/**
 * PP286: the coding matrix jerasure builds, exposed so a managed one can be held against it.
 *
 * fec.c calls cauchy_original_coding_matrix(k, m, 8) and hands the result straight to
 * jerasure_matrix_encode and _decode. Everything a managed port has to reproduce is in that one
 * call: the field's primitive polynomial, the log and antilog tables built from it, and the
 * inverse of every element - because each entry is 1/(i ^ (m + j)) and nothing else.
 *
 * So this is the smallest thing worth agreeing on first. A decoder written against a field that
 * disagrees here fails the recorded cases with no clue which of the two it was, and the recorded
 * cases are the only oracle the block has.
 *
 * Writes k*m entries in row-major order, m rows of k. Returns the count written, or -1 where the
 * buffer is too small or the matrix could not be built. chiaki_shim_lib_init must have run: the
 * Galois field tables are built there.
 */
CHIAKI_SHIM_API int32_t chiaki_shim_fec_matrix(
		uint32_t k, uint32_t m, int32_t *out_matrix, int32_t capacity);

#endif /* CHIAKI_SHIM_HAVE_FRAMEPATH - the fec pair */

/**
 * PP23: the handshake's key agreement and the session key stream it produces.
 *
 * test/gkcrypt.c records a complete exchange - a local key pair, its signature under a handshake
 * key, the console's public key and signature, and the 32-byte secret the two derived. It is the
 * one place in this tree where a real console's half of an ECDH is written down.
 *
 * This is also where PP26's warning lands hardest: a wrong byte here does not throw. It produces a
 * key that fails to open a session, with nothing to say which of eight steps was wrong. The
 * recorded exchange is what turns that into one failing assertion.
 */
CHIAKI_SHIM_API int32_t chiaki_shim_ecdh_secret_size(void);

CHIAKI_SHIM_API void *chiaki_shim_ecdh_create(void);
CHIAKI_SHIM_API void chiaki_shim_ecdh_free(void *ecdh);

/** Installs a recorded key pair, so a derivation can be repeated rather than generated afresh. */
CHIAKI_SHIM_API int32_t chiaki_shim_ecdh_set_local_key(
		void *ecdh,
		const uint8_t *private_key, int32_t private_key_size,
		const uint8_t *public_key, int32_t public_key_size);

/**
 * The local public key and its signature under `handshake_key`. Both sizes are in/out: the caller
 * says how much room it has and gets back how much was written.
 */
CHIAKI_SHIM_API int32_t chiaki_shim_ecdh_local_pub_key(
		void *ecdh,
		const uint8_t *handshake_key,
		uint8_t *key_out, int32_t *key_out_size,
		uint8_t *sig_out, int32_t *sig_out_size);

/** The shared secret, which is CHIAKI_ECDH_SECRET_SIZE bytes and is what keys the session. */
CHIAKI_SHIM_API int32_t chiaki_shim_ecdh_derive_secret(
		void *ecdh,
		uint8_t *secret_out,
		const uint8_t *remote_key, int32_t remote_key_size,
		const uint8_t *handshake_key,
		const uint8_t *remote_sig, int32_t remote_sig_size);

/**
 * A ChiakiGKCrypt over a handshake key and an ECDH secret. `log` may be NULL.
 *
 * `key_buf_chunks` of zero means no precomputed buffer, which is what the recorded case uses:
 * every key stream is then generated on demand rather than read out of a window.
 */
CHIAKI_SHIM_API void *chiaki_shim_gkcrypt_create(
		void *log,
		int32_t key_buf_chunks,
		uint8_t index,
		const uint8_t *handshake_key,
		const uint8_t *ecdh_secret);

CHIAKI_SHIM_API void chiaki_shim_gkcrypt_free(void *gkcrypt);

/** The key stream at a position, which is what every takion packet is XORed against. */
CHIAKI_SHIM_API int32_t chiaki_shim_gkcrypt_gen_key_stream(
		void *gkcrypt, uint64_t key_pos, uint8_t *buf, int32_t buf_size);

/**
 * PP130: the orientation tracker, which turns a pad's raw sensors into what the console is told.
 *
 * A DualSense sends accelerometer and gyroscope samples and the console expects an ORIENTATION -
 * a quaternion - alongside them. The fusion is carried rather than ported because it is a filter
 * with state: each update depends on the previous one and on the time between them, so a managed
 * reimplementation would be a second filter that drifts differently. Drift is a picture that
 * slowly tilts, not an error anyone reports.
 *
 * The tracker and the accel zero are separate handles because their lifetimes differ - the zero
 * is the user's calibration and survives the pad being unplugged.
 *
 * The update's timestamp is MICROseconds. SDL reports milliseconds, so the caller multiplies by
 * 1000, which is the one unit conversion on this path and the kind that produces a filter running
 * a thousand times too slowly rather than an error.
 */
CHIAKI_SHIM_API void *chiaki_shim_orientation_tracker_create(void);
CHIAKI_SHIM_API void chiaki_shim_orientation_tracker_free(void *tracker);

CHIAKI_SHIM_API void *chiaki_shim_accel_new_zero_create(void);
CHIAKI_SHIM_API void chiaki_shim_accel_new_zero_free(void *accel_zero);
CHIAKI_SHIM_API void chiaki_shim_accel_new_zero_set_active(
		void *accel_zero, float accel_x, float accel_y, float accel_z, bool real_accel);
CHIAKI_SHIM_API void chiaki_shim_accel_new_zero_set_inactive(void *accel_zero, bool real_accel);

CHIAKI_SHIM_API void chiaki_shim_orientation_tracker_update(
		void *tracker, float gx, float gy, float gz, float ax, float ay, float az,
		void *accel_zero, bool accel_zero_applied, uint32_t timestamp_us);

/** The orientation a controller state carries, which is what the console is actually sent. */
CHIAKI_SHIM_API bool chiaki_shim_controller_state_orient(void *state, float *out_orient);

/**
 * PP756: gyro then accelerometer, six floats, which had setters and no reader.
 *
 * Six out in one call because they are set in one call: a caller reading gyro without accel has
 * half a sample, and the managed snapshot the deletion needs is built from both.
 */
CHIAKI_SHIM_API bool chiaki_shim_controller_state_motion(void *state, float *out_motion);

/** Writes the tracker's orientation into a controller state, which is what the console reads. */
CHIAKI_SHIM_API void chiaki_shim_orientation_tracker_apply(void *tracker, void *state);

/** The tracker's sensors and orientation, flattened. Any out-pointer may be NULL. */
CHIAKI_SHIM_API bool chiaki_shim_orientation_tracker_read(
		void *tracker, float *out_gyro, float *out_accel, float *out_orient, uint32_t *out_timestamp);

/**
 * PP125: takion's send buffer, which is what makes its retransmission work.
 *
 * Every reliable message the client sends is held here until the console acknowledges it, and an
 * ack releases that packet AND every older one. That is the whole of the semantics and the whole
 * of what can go wrong: release too much and a message nobody received is never sent again;
 * release too little and the buffer fills, which the C reports as OVERFLOW and after which the
 * session stops sending. Neither failure mentions a send buffer.
 *
 * The payload is allocated on this side because the buffer takes ownership and frees it on ack -
 * handing over a managed array would have a C allocator free memory it never allocated.
 *
 * WHICH packets remain is not askable. ChiakiTakionSendBufferPacket is an incomplete type in the
 * public header and its layout lives in takionsendbuffer.c, which the C's own test reaches by
 * #including that file - the shim cannot, because chiaki-lib is already linked in. Declaring the
 * layout here instead would be a guess a field reorder breaks silently. So the count is the
 * observable, and every property below is expressed in it.
 *
 * The count is read under the buffer's own mutex, as the C's test does by hand and for the same
 * reason: a retransmit thread may be walking the same array.
 */
CHIAKI_SHIM_API void *chiaki_shim_takion_send_buffer_create(int32_t size);
CHIAKI_SHIM_API void chiaki_shim_takion_send_buffer_free(void *send_buffer);

/** Pushes a packet of `buf_size` zero bytes. CHIAKI_ERR_OVERFLOW once the buffer is full. */
CHIAKI_SHIM_API int32_t chiaki_shim_takion_send_buffer_push(
		void *send_buffer, uint32_t seq_num, int32_t buf_size);

/** Acknowledges a sequence number, releasing it and everything older. */
CHIAKI_SHIM_API int32_t chiaki_shim_takion_send_buffer_ack(void *send_buffer, uint32_t seq_num);

/** How many packets are still waiting, or -1. */
CHIAKI_SHIM_API int32_t chiaki_shim_takion_send_buffer_count(void *send_buffer);

/**
 * PP124: the congestion report, which is the first thing this port sends rather than reads.
 *
 * Every other function across this seam parses what a console sent. This is the other direction:
 * how many packets the client received and how many it lost, which is what the console's bitrate
 * control reacts to. A wrong byte is not a stream that fails - it is one that quietly degrades,
 * and nothing on either side reports it.
 *
 * The struct is flattened to its three fields, as every builder here does. The MAC goes INSIDE
 * the packet at a fixed offset rather than after it, which is the detail a rewrite loses: a
 * packet of the right length with the MAC appended is one the console silently ignores.
 */
CHIAKI_SHIM_API int32_t chiaki_shim_takion_format_congestion(
		uint8_t *buf, int32_t buf_size, uint16_t word_0, uint16_t received, uint16_t lost,
		uint64_t key_pos);

/** CHIAKI_TAKION_CONGESTION_PACKET_SIZE. */
CHIAKI_SHIM_API int32_t chiaki_shim_takion_congestion_packet_size(void);

/** chiaki_takion_packet_mac, writing the MAC into the buffer. Both out-pointers may be NULL. */
CHIAKI_SHIM_API int32_t chiaki_shim_takion_packet_mac(
		void *gkcrypt, uint8_t *buf, int32_t buf_size, uint64_t key_pos,
		uint8_t *mac_out, uint8_t *mac_old_out);

/**
 * PP123: chiaki_gkcrypt_decrypt, in place, which is what turns a parsed AV packet into a NALU.
 *
 * test/takion_av_packet_parse_real_video.inl is 24 packets off a real stream with the NALU each
 * decrypts to. Without this the port could check the header of a real packet and nothing inside
 * it, which is the half where a wrong key position produces plausible garbage rather than an
 * error - the decoder then reports a corrupt frame and the fault reads as the network's.
 *
 * The key position a payload decrypts at is the packet's plus one block, and the block size is
 * asked for below rather than written down twice.
 */
CHIAKI_SHIM_API int32_t chiaki_shim_gkcrypt_decrypt(
		void *gkcrypt, uint64_t key_pos, uint8_t *buf, int32_t buf_size);

/** CHIAKI_GKCRYPT_BLOCK_SIZE. */
CHIAKI_SHIM_API int32_t chiaki_shim_gkcrypt_block_size(void);

/**
 * PP26: the key and IV a gkcrypt derived, so a managed key stream can be held against its own.
 *
 * chiaki_gkcrypt_gen_key_stream is AES-128 ECB over a counter, and the counter is the IV plus the
 * block index. Both live inside ChiakiGKCrypt, which the public header leaves incomplete - so a
 * managed port of the STREAM cannot be compared with the C's without them, and comparing the
 * derivation instead would be testing two things at once.
 *
 * Writes CHIAKI_GKCRYPT_BLOCK_SIZE bytes to each. Returns false where the handle is null or a
 * buffer is too small.
 */
CHIAKI_SHIM_API bool chiaki_shim_gkcrypt_key_and_iv(
		void *gkcrypt, uint8_t *out_key_base, uint8_t *out_iv, int32_t capacity);


/**
 * PP35: the GMAC that authenticates every takion packet, and its four recorded vectors.
 *
 * This is the other half of gkcrypt and the port had none of it. PP105 is where it matters: the
 * MAC is what takion checks once crypt is available, and until then it checks nothing - so a GMAC
 * the port computed differently from the C would be a session that authenticates every packet
 * against the wrong answer and reports a stream that will not start.
 *
 * The key derivation is a pure function and passes straight through. The GMAC itself needs a
 * gkcrypt, and the one test/gkcrypt.c records against is built by hand rather than by
 * chiaki_gkcrypt_init - hence the second constructor, which is the only way to reach the vector.
 */
CHIAKI_SHIM_API void chiaki_shim_gkcrypt_gen_gmac_key(
		uint64_t index, const uint8_t *key_base, const uint8_t *iv, uint8_t *key_out);

/** A gkcrypt carrying only a current GMAC key and an IV, as the recorded vectors are taken. */
CHIAKI_SHIM_API void *chiaki_shim_gkcrypt_create_for_gmac(
		const uint8_t *key_gmac_current, const uint8_t *iv);

/** Frees one of the above. NOT interchangeable with chiaki_shim_gkcrypt_free - see the note there. */
CHIAKI_SHIM_API void chiaki_shim_gkcrypt_free_for_gmac(void *gkcrypt);

/** chiaki_gkcrypt_gmac: the four bytes takion compares a received packet's tail against. */
CHIAKI_SHIM_API int32_t chiaki_shim_gkcrypt_gmac(
		void *gkcrypt, uint64_t key_pos, const uint8_t *buf, int32_t buf_size, uint8_t *gmac_out);

/** CHIAKI_GKCRYPT_GMAC_SIZE, so the managed side does not carry a second copy of it. */
CHIAKI_SHIM_API int32_t chiaki_shim_gkcrypt_gmac_size(void);

/**
 * PP23: RFC 1982 serial number comparison, which is the arithmetic the whole transport rests on.
 *
 * Sequence numbers wrap. 0xffff is followed by 0, and a packet numbered 1 is NEWER than one
 * numbered 0xfff5 even though the integer is smaller. Every reorder decision, every duplicate
 * check and every "have I already seen this" in takion is one of these two comparisons, so a
 * rewrite that spells them `a < b` works perfectly until the counter turns over - once every
 * 65536 packets on the 16-bit ones, which at a stream's packet rate is minutes.
 *
 * They are static inline in libchiaki's header rather than exported, so these wrap them: the
 * compiler checks the call, and what crosses is the answer.
 */
CHIAKI_SHIM_API bool chiaki_shim_seq_num_16_lt(uint16_t a, uint16_t b);
CHIAKI_SHIM_API bool chiaki_shim_seq_num_16_gt(uint16_t a, uint16_t b);
CHIAKI_SHIM_API bool chiaki_shim_seq_num_32_lt(uint32_t a, uint32_t b);
CHIAKI_SHIM_API bool chiaki_shim_seq_num_32_gt(uint32_t a, uint32_t b);

/**
 * PP23: the reorder queue, which is what turns a UDP arrival order back into a stream.
 *
 * It is the module a managed rewrite would most confidently write from scratch - it is a ring
 * buffer with a window, and it looks like one. What it actually is is a ring buffer indexed by the
 * wrapping arithmetic above, with a drop policy that fires on four different occasions: a packet
 * older than the window, a duplicate, an overflow at the low end and an overflow at the high end.
 * Which of the four a given push takes is the whole behaviour, and the C suite records it.
 *
 * The payload is an opaque pointer libchiaki only ever hands back. Nothing dereferences it, so the
 * managed side can pass an index rather than a handle and keep the GC out of it entirely.
 *
 * `drop_strategy` is 0 for BEGIN (drop the lowest) and 1 for END (drop the highest).
 */
typedef void (*ChiakiShimReorderDropCb)(uint64_t seq_num, void *elem_user, void *user);

CHIAKI_SHIM_API void *chiaki_shim_reorder_queue_create_16(
		int32_t size_exp, uint16_t seq_num_start, ChiakiShimReorderDropCb cb, void *user);

/*
 * PP674: the thirty-two-bit instantiation, which takion's DATA queue is. Everything below takes the
 * handle and is width-blind, so this adds an entry point and not a family.
 */
CHIAKI_SHIM_API void *chiaki_shim_reorder_queue_create_32(
		int32_t size_exp, uint32_t seq_num_start, ChiakiShimReorderDropCb cb, void *user);

CHIAKI_SHIM_API void chiaki_shim_reorder_queue_free(void *queue);

CHIAKI_SHIM_API void chiaki_shim_reorder_queue_set_drop_strategy(void *queue, int32_t strategy);

CHIAKI_SHIM_API int32_t chiaki_shim_reorder_queue_size(void *queue);
CHIAKI_SHIM_API uint64_t chiaki_shim_reorder_queue_count(void *queue);

CHIAKI_SHIM_API void chiaki_shim_reorder_queue_push(void *queue, uint64_t seq_num, void *elem_user);

/** True when something came out in order; the two out-parameters are only then meaningful. */
CHIAKI_SHIM_API bool chiaki_shim_reorder_queue_pull(
		void *queue, uint64_t *seq_num, void **elem_user);

/** `index` is an OFFSET from the window's start and not a sequence number. */
CHIAKI_SHIM_API bool chiaki_shim_reorder_queue_peek(
		void *queue, uint64_t index, uint64_t *seq_num, void **elem_user);

CHIAKI_SHIM_API void chiaki_shim_reorder_queue_drop(void *queue, uint64_t index);

/**
 * PP23 and PP33: libchiaki's HTTP response parser, exposed so a managed one can be compared to it.
 *
 * This is the first module the port replaces outright rather than calls: HttpClient and
 * System.Text.Json do what curl and json-c were vendored for. That makes it the first place a
 * managed implementation and the C one can be run on the same bytes and their answers compared,
 * which is the shape PP23 asks for and the shape every module after it inherits.
 *
 * Parsed in place, like the discovery reply, so the shim owns a copy of the text for as long as
 * the handle. `error_out` and `code_out` may be NULL.
 */
CHIAKI_SHIM_API void *chiaki_shim_http_parse(
		const char *text, int32_t len, int32_t *code_out, int32_t *error_out);

CHIAKI_SHIM_API void chiaki_shim_http_free(void *response);

/** How many headers the parser found. They come back in the order the list holds them. */
CHIAKI_SHIM_API int32_t chiaki_shim_http_header_count(void *response);

CHIAKI_SHIM_API const char *chiaki_shim_http_header_key(void *response, int32_t index);
CHIAKI_SHIM_API const char *chiaki_shim_http_header_value(void *response, int32_t index);

/**
 * PP33: json-c's accessors, exposed for the same reason the HTTP parser above is.
 *
 * holepunch.c is where json-c actually lives - 24 object_get_ex, 20 get_string, 8 get_int and 7
 * json_pointer_get - and System.Text.Json does not answer any of those the same way. get_string on
 * a number returns the number's text where GetString() throws; get_int on a string PARSES it where
 * System.Text.Json refuses; and JSON Pointer has no managed equivalent at all. So the managed side
 * cannot be written from the header and then trusted - it has to be run against this.
 *
 * Ownership is the trap and is why these are separate calls. json_tokener_parse returns a reference
 * the caller owns, and object_get_ex, pointer_get and array_get_idx return BORROWED ones - putting
 * a borrowed reference frees a subtree the root still points at. Only the handle from
 * chiaki_shim_json_parse is passed to chiaki_shim_json_free; every other handle here is valid only
 * while its root is.
 */

/**
 * PP23: the bitstream parser, which is what tells the client what kind of frame just arrived.
 *
 * It reads H.264 and H.265 slice headers far enough to answer two questions: is this an I frame,
 * and which frame does it reference. Everything the video path does about loss rests on those -
 * whether a gap needs an IDR request, whether a frame can be decoded at all - so a parser that is
 * subtly wrong shows up as stutter attributed to the network.
 *
 * test/bitstream.c records real headers and slices for both codecs, including one regression case
 * carrying an upstream issue number, which is the closest this module has to a specification.
 *
 * `codec` is ChiakiCodec: 0 H264, 1 H265, 2 H265 HDR. The data is read, never written, except by
 * set_reference_frame which rewrites it in place.
 */
CHIAKI_SHIM_API void *chiaki_shim_bitstream_create(int32_t codec);
CHIAKI_SHIM_API void chiaki_shim_bitstream_free(void *bitstream);

/** Parses the parameter sets a stream opens with. False when they are not understood. */
CHIAKI_SHIM_API bool chiaki_shim_bitstream_header(void *bitstream, uint8_t *data, int32_t size);

/** The slice type (0 unknown, 1 I, 2 P) and the frame it references. */
CHIAKI_SHIM_API bool chiaki_shim_bitstream_slice(
		void *bitstream, uint8_t *data, int32_t size,
		int32_t *slice_type, uint32_t *reference_frame);

/** Rewrites a slice's reference frame in place, which is how a lost frame is worked around. */
CHIAKI_SHIM_API bool chiaki_shim_bitstream_slice_set_reference_frame(
		void *bitstream, uint8_t *data, int32_t size, uint32_t reference_frame);

/**
 * PP23: the key position, which is the counter every encrypted byte of a session is keyed by.
 *
 * The wire carries 32 bits of it and the cipher needs 64. Expanding one to the other is the whole
 * of ChiakiKeyState: it remembers the high half and increments it when the low half wraps, so a
 * packet arriving at 0x1337 after one at 0xffff0000 is 0x100001337 and not a step backwards.
 *
 * Getting that wrong does not throw. It produces a key stream at the wrong offset, so every packet
 * after the first wrap decrypts to noise and the session dies with a MAC failure four gigabytes in.
 *
 * `commit` is what makes a request advance the state. A parse that later turns out to be garbage
 * asks without committing, so a corrupt packet cannot drag the counter forward with it.
 */
CHIAKI_SHIM_API void *chiaki_shim_key_state_create(void);
CHIAKI_SHIM_API void chiaki_shim_key_state_free(void *state);
CHIAKI_SHIM_API uint64_t chiaki_shim_key_state_request_pos(void *state, uint32_t low, bool commit);

/**
 * chiaki_takion_v9_av_packet_parse: one audio or video packet's header, flattened.
 *
 * ChiakiTakionAVPacket ends in a borrowed pointer into the datagram, which is the same ownership
 * shape as the discovery reply - so the payload comes back as an OFFSET and a length rather than
 * as a pointer, and the caller already has the buffer it indexes.
 *
 * Every out-parameter may be NULL. Returns a ChiakiErrorCode.
 */
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
		int32_t *data_size);

/**
 * PP679: chiaki_takion_v7_av_packet_parse, which is a body of its own rather than a mode of the
 * one above.
 *
 * Three fields of the header it walks are read differently. Its bound counts the nalu-info add for
 * video as well as audio; its packed word always takes the video layout whatever the base type;
 * and its key position is thirty-two raw bits with no ChiakiKeyState behind them, which is why
 * there is no state parameter here. NULL is passed for the C's own, which it declares and never
 * reads.
 *
 * word_at_0x18 crosses here and not on the v9 export, because the formatter below writes it.
 *
 * Every out-parameter may be NULL. Returns a ChiakiErrorCode.
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
		int32_t *data_size);

/**
 * PP679: chiaki_takion_v7_av_packet_format_header, takion.c's only header formatter.
 *
 * Its two callers are senkusha.c's - the ping and the MTU probe - and not takion's receive path,
 * so the oracle for a managed formatter is this rather than anything the stream sends.
 *
 * Flattened like chiaki_shim_takion_format_congestion: the packet ends in a borrowed pointer, and
 * only the fields the formatter reads are taken. header_size_out is written even when the buffer
 * is too small, because the C sets it before its bound check and senkusha.c asserts on it.
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
		uint64_t key_pos);

#ifdef CHIAKI_SHIM_HAVE_FRAMEPATH

/**
 * PP23: the frame processor, where units become a frame and FEC is driven.
 *
 * It is the join between the two modules already across this seam - takion hands it units, and it
 * hands FEC the ones that are missing. Its flush answers the only question the video path asks
 * about a frame: did it arrive whole, was it reconstructed, or is it gone.
 *
 * The unit is passed as scalars rather than as a ChiakiTakionAVPacket, for the reason every struct
 * at this seam is: the packet ends in a borrowed pointer, and building one on this side would put
 * .NET in charge of a layout it has no way to check.
 *
 * `flush` hands back a pointer into the processor's own buffer that is invalid after the next call
 * to it, so the frame is copied out here and the caller gets bytes it owns.
 */
CHIAKI_SHIM_API void *chiaki_shim_frame_processor_create(void *log);
CHIAKI_SHIM_API void chiaki_shim_frame_processor_free(void *processor);

/** Sizes the frame from the first unit of it. Must precede the units. */
CHIAKI_SHIM_API int32_t chiaki_shim_frame_processor_alloc_frame(
		void *processor,
		bool is_video,
		uint16_t frame_index,
		uint16_t packet_index,
		uint16_t unit_index,
		uint16_t units_in_frame_total,
		uint16_t units_in_frame_fec,
		uint8_t *data,
		int32_t data_size);

CHIAKI_SHIM_API int32_t chiaki_shim_frame_processor_put_unit(
		void *processor,
		bool is_video,
		uint16_t frame_index,
		uint16_t packet_index,
		uint16_t unit_index,
		uint16_t units_in_frame_total,
		uint16_t units_in_frame_fec,
		uint8_t *data,
		int32_t data_size);

/** Whether enough units are in for a flush to be worth trying. */
CHIAKI_SHIM_API bool chiaki_shim_frame_processor_flush_possible(void *processor);

/**
 * Flushes into `frame`, which the caller owns and sizes. 0 success, 1 reconstructed, 2 FEC failed,
 * 3 failed. `frame_size` is in/out: room offered, then bytes written.
 */
CHIAKI_SHIM_API int32_t chiaki_shim_frame_processor_flush(
		void *processor, uint8_t *frame, int32_t *frame_size);

/** How many samples each timed stage has taken, which is what a baseline row reports. */
CHIAKI_SHIM_API uint64_t chiaki_shim_frame_processor_stage_samples(void *processor, int32_t stage);

/**
 * PP87: the video sample callback, which is the last of PP4's four questions.
 *
 * "Who owns the buffers a video frame arrives in" was filed as unanswerable without a decoder to
 * feed. That was wrong, and test/videoreceiver.c is the proof: it drives this callback with a
 * synthesised session, a real profile header and one whole frame in one unit. No console, no
 * renderer, no decoder.
 *
 * The ownership is the point. `buf` is the frame processor's own storage, lent for the duration of
 * the call and reused after it - so the managed side reads it in place and copies what it wants to
 * keep. Returning false is how a client says it could not take the frame, which makes the receiver
 * report a corrupt frame and ask for a keyframe.
 *
 * The session this needs is built here and zeroed apart from the four fields the path reads, which
 * is what the C suite does and for the same reason: a ChiakiSession is not a thing to hand .NET.
 */
typedef bool (*ChiakiShimVideoSampleCb)(
		uint8_t *buf, int32_t buf_size, int32_t frames_lost, bool frame_recovered, void *user);

CHIAKI_SHIM_API void *chiaki_shim_video_receiver_create(
		void *log, int32_t codec, ChiakiShimVideoSampleCb cb, void *user);

CHIAKI_SHIM_API void chiaki_shim_video_receiver_free(void *receiver);

/**
 * The stream info a session opens with. The header is copied here, because the receiver takes
 * ownership of what it is given and frees it - which is not a thing a managed array can be.
 */
CHIAKI_SHIM_API bool chiaki_shim_video_receiver_stream_info(
		void *receiver, const uint8_t *header, int32_t header_size, uint32_t width, uint32_t height);

CHIAKI_SHIM_API void chiaki_shim_video_receiver_av_packet(
		void *receiver,
		uint16_t frame_index,
		uint16_t packet_index,
		uint16_t unit_index,
		uint16_t units_in_frame_total,
		uint16_t units_in_frame_fec,
		uint8_t adaptive_stream_index,
		uint8_t *data,
		int32_t data_size);

CHIAKI_SHIM_API int32_t chiaki_shim_video_receiver_frames_lost(void *receiver);

#endif /* CHIAKI_SHIM_HAVE_FRAMEPATH - the frame processor and the video receiver */

/**
 * PP23 and PP29: registration, which is the first thing a fresh install sends.
 *
 * A console that will not pair gives a user nothing to go on, and the request is one payload of
 * ciphertext - so every byte of it either matches what a console accepts or the pairing fails with
 * no clue which field was wrong. test/regist.c records that whole payload, which makes it the one
 * vector in this tree that pins an entire message rather than a key.
 *
 * `psn_account_id` may be NULL, which is the pre-firmware-10 PS4 case that takes an online id
 * instead. `buf_size` is in/out: room offered, then bytes written.
 */
CHIAKI_SHIM_API void chiaki_shim_rpcrypt_aeropause_ps4_pre10(
		const uint8_t *ambassador, uint8_t *aeropause);

/**
 * PP445: the same derivation for a PS4 from firmware 10 and for a PS5, which is the path the
 * console this project tests against actually takes.
 *
 * NO PIN AND NO key_0_off, and their absence is the surprising part. regist.c reaches this through
 * chiaki_rpcrypt_init_regist, so the first version of this wrapper took both - and a test asserting
 * that each changed the answer failed. init_regist copies the ambassador through UNTOUCHED and
 * spends key_0_off and the pin entirely on `bright`; the aeropause is computed over the ambassador.
 * So both were accepted and had no effect, which is worse than not offering them.
 *
 * `key_1_off` IS BOUNDED HERE, because the C does not bound it. chiaki_rpcrypt_aeropause indexes
 * `keys_1[i * 0x20 + key_1_off]` over a 512-byte table with i running to 15, so 0x20 reads one past
 * the end. regist.c can only pass `buf[0] >> 3`, which is 0..31 by construction - but this entry
 * point takes an int32 from managed code, and widening the seam without the bound would open a path
 * the C never had. Its sibling init_regist rejects `key_0_off >= 0x20`; this is the same rule.
 *
 * `aeropause` receives CHIAKI_RPCRYPT_KEY_SIZE bytes. False where the offset is out of range, a
 * pointer is NULL, or the C refuses the target.
 */
CHIAKI_SHIM_API bool chiaki_shim_rpcrypt_aeropause(
		int32_t target, const uint8_t *ambassador, int32_t key_1_off, uint8_t *aeropause);

/** The bright key a registration PIN derives, which is what encrypts the payload below. */
CHIAKI_SHIM_API void chiaki_shim_rpcrypt_regist_bright_ps4_pre10(
		const uint8_t *ambassador, uint32_t pin, uint8_t *bright);

/**
 * PP23 and PP31: when a decoded frame is due, and for how long.
 *
 * chiaki_ffmpeg_frame_get_timing turns a decoded frame's timestamps into a presentation time and a
 * duration, through three fallbacks: the best-effort timestamp or the raw one, the packet timebase
 * or the context's, the framerate or a default. Each fallback exists because some stream does not
 * carry the field above it, and picking the wrong one does not fail - it paces the picture wrong,
 * which reads as stutter and gets blamed on the network.
 *
 * The AVFrame is built here from scalars. It is ffmpeg's struct, so it is exactly the kind of
 * layout .NET must not be handed - and the two rationals are four ints, which have no padding to
 * disagree about.
 *
 * A timestamp of CHIAKI_SHIM_AV_NOPTS is "absent", which is what selects the next fallback.
 */
#define CHIAKI_SHIM_AV_NOPTS INT64_MIN

/**
 * PP25: one takion control message, decoded by nanopb.
 *
 * The wire format is the one part of this core that is regenerated rather than translated:
 * lib/protobuf/takion.proto becomes C through nanopb and C# through protoc, from the same file.
 * What that leaves open is whether the two generators agree on the bytes, and this is the answer -
 * the managed side encodes a message and nanopb, which is what the console's protocol is spoken
 * with today, is asked what it reads.
 *
 * The bang is the message worth checking: it is the one that carries the ECDH key and the flags a
 * session is refused on, and it is the message PP105 traced. Only its scalars come back; the
 * string and bytes fields are nanopb callbacks, which is a second ownership question and not this
 * one's.
 *
 * `type` is the PayloadType enum. Every out-parameter may be NULL. False when nanopb refuses.
 */
CHIAKI_SHIM_API bool chiaki_shim_takion_message_decode(
		const uint8_t *buf,
		int32_t size,
		int32_t *type,
		bool *has_bang,
		uint32_t *server_version,
		uint32_t *token,
		bool *encrypted_key_accepted,
		bool *version_accepted);

/**
 * PP25, the other direction: a bang encoded BY nanopb, for the managed generator to read.
 *
 * Decoding proves the managed encoder is understood; this proves the managed decoder understands
 * what a console's stack actually sends. Both are needed, and they fail differently - a message
 * this side cannot write is a session that never opens, and one it cannot read is a session that
 * opens and then stops.
 *
 * The string and bytes fields are the interesting part. nanopb does not store them: it hands the
 * caller a CALLBACK and asks it to write them as the field goes past, which is the second
 * ownership question this seam meets - and the reason those fields could not be checked by the
 * decode direction alone.
 *
 * `buf_size` is in/out: room offered, then bytes written.
 */
CHIAKI_SHIM_API bool chiaki_shim_takion_message_encode_bang(
		uint32_t server_version,
		uint32_t token,
		bool encrypted_key_accepted,
		bool version_accepted,
		const char *session_key,
		const uint8_t *ecdh_pub_key, int32_t ecdh_pub_key_size,
		const uint8_t *ecdh_sig, int32_t ecdh_sig_size,
		uint8_t *buf,
		int32_t *buf_size);

/**
 * PP23: vl_rbsp, the bit reader both slice-header parsers sit on, exposed so a managed one can be
 * compared to it.
 *
 * `alignment` is the low two bits of the address the payload is placed at, 0-3. It is a parameter
 * and not an accident because vl_vlc_align_data_ptr consumes bytes one at a time until the data
 * pointer is dword-aligned and only then reads whole dwords - so the number of bits valid in the
 * buffer after init depends on where malloc happened to put the NAL, and vl_rbsp_init's
 * emulation-prevention scan is bounded by exactly that number. Whether the OUTPUT depends on it is
 * the question this parameter exists to answer.
 *
 * The handle owns its copy of the payload, because vl_vlc keeps pointers into it.
 */
CHIAKI_SHIM_API void *chiaki_shim_rbsp_create(
		const uint8_t *data, int32_t size, uint32_t num_bits, int32_t alignment);

CHIAKI_SHIM_API void chiaki_shim_rbsp_free(void *rbsp);

/** The low two bits of the address the payload actually landed on. */
CHIAKI_SHIM_API int32_t chiaki_shim_rbsp_alignment(void *rbsp);

/** vl_rbsp_u. Zero and a set overrun flag where there are not n bits left. */
CHIAKI_SHIM_API uint32_t chiaki_shim_rbsp_u(void *rbsp, uint32_t n);

/** vl_rbsp_ue, the exp-Golomb read whose prefix is capped at 32 zeroes (PP68). */
CHIAKI_SHIM_API uint32_t chiaki_shim_rbsp_ue(void *rbsp);

/** vl_rbsp_se. */
CHIAKI_SHIM_API int32_t chiaki_shim_rbsp_se(void *rbsp);

/** vl_rbsp_overrun: whether any read has gone past the end. */
CHIAKI_SHIM_API bool chiaki_shim_rbsp_overrun(void *rbsp);

/** vl_rbsp_has_bits. */
CHIAKI_SHIM_API bool chiaki_shim_rbsp_has_bits(void *rbsp, uint32_t n);

/** vl_rbsp_more_data. */
CHIAKI_SHIM_API bool chiaki_shim_rbsp_more_data(void *rbsp);

/** vl_vlc_valid_bits of the RBSP's own vlc, which is what the escape scan is bounded by. */
CHIAKI_SHIM_API uint32_t chiaki_shim_rbsp_valid_bits(void *rbsp);

/** vl_vlc_bits_left of the RBSP's own vlc. */
CHIAKI_SHIM_API uint32_t chiaki_shim_rbsp_bits_left(void *rbsp);

CHIAKI_SHIM_API int64_t chiaki_shim_ffmpeg_nopts(void);

CHIAKI_SHIM_API bool chiaki_shim_ffmpeg_frame_timing(
		int64_t best_effort_timestamp,
		int64_t pts,
		int64_t duration,
		int32_t pkt_timebase_num, int32_t pkt_timebase_den,
		int32_t ctx_timebase_num, int32_t ctx_timebase_den,
		int32_t framerate_num, int32_t framerate_den,
		double *pts_out,
		double *duration_out);

CHIAKI_SHIM_API int32_t chiaki_shim_regist_request_payload(
		int32_t target,
		const uint8_t *ambassador,
		const char *psn_online_id,
		const uint8_t *psn_account_id,
		uint32_t pin,
		uint8_t *buf,
		int32_t *buf_size);

/**
 * PP269: the library's own base64 encoder, so a claim about it can be run rather than read.
 *
 * PP261 established by READING that a conversion which does not fit returns its error without
 * writing a terminator, leaving the destination partly written. That is the difference this entry
 * exists to measure: the caller fills @p out first, and what comes back tells what the encoder
 * wrote from what it left.
 *
 * @param in Bytes to encode.
 * @param in_size How many.
 * @param out Destination, which the caller may fill beforehand.
 * @param out_size Its size, INCLUDING the terminator the encoder writes when it fits.
 * @return The library's error code, zero being success.
 */
CHIAKI_SHIM_API int32_t chiaki_shim_base64_encode(
		const uint8_t *in,
		int32_t in_size,
		char *out,
		int32_t out_size);

/**
 * PP607: a takion connected to a peer on loopback, which is what makes its receive loop reachable.
 *
 * PP601 found that every receive path in takion.c is file-local and that exposing one would be the
 * local patch to vendored C a non-goal refuses - and that chiaki_takion_connect takes the caller's
 * socket, which is the way in that patches nothing. This is smaller than that even: pass NULL for
 * the socket and takion makes its own from the address, so all this owes is a loopback sockaddr and
 * a callback.
 *
 * The handshake runs on takion's own thread, so this returns as soon as the thread starts and
 * chiaki_shim_takion_connected reports whether CHIAKI_TAKION_EVENT_TYPE_CONNECTED has fired.
 * PP606's responder is what answers on the other end.
 *
 * Crypt is OFF: MACs are checked once a gkcrypt exists, and a handshake harness has none.
 */
CHIAKI_SHIM_API void *chiaki_shim_takion_connect_loopback(
		void *log,
		uint16_t port,
		uint8_t protocol_version,
		int32_t *error_out);

/** Whether the connected event has fired, which is the handshake having completed. */
CHIAKI_SHIM_API bool chiaki_shim_takion_connected(void *takion);

/** How many events the callback has seen, connected included. */
CHIAKI_SHIM_API int32_t chiaki_shim_takion_event_count(void *takion);

/** chiaki_takion_close, which joins the thread, and then the wrapper goes. */
CHIAKI_SHIM_API void chiaki_shim_takion_close(void *takion);

/**
 * PP676: feedback.c's serialisers, reachable so the managed ones can be held against them.
 *
 * The three sends outside PP497's MAC table carry these payloads, and none of them had a managed
 * counterpart - so there were no managed bytes to compare. These are the oracle the comparison
 * needs, and they are pure: no session, no socket, no key. What they format is a controller's
 * state and a history of its events, and the compression inside the first is where a port goes
 * quietly wrong.
 */

/** CHIAKI_FEEDBACK_STATE_BUF_SIZE_V9 and _V12, so the managed side reads them rather than typing them. */
CHIAKI_SHIM_API int32_t chiaki_shim_feedback_state_size(bool v12);

/**
 * chiaki_feedback_state_format_v9 or _v12, over ten floats and four sticks.
 *
 * @param motion gyro x,y,z then accel x,y,z then orient x,y,z,w - ten floats, in that order.
 * @param sticks left x,y then right x,y - four int16, in that order.
 */
CHIAKI_SHIM_API void chiaki_shim_feedback_state_format(
		uint8_t *buf, int32_t buf_size, bool v12, const float *motion, const int16_t *sticks);

/** chiaki_feedback_history_event_set_button. Writes up to 5 bytes and the length written. */
CHIAKI_SHIM_API int32_t chiaki_shim_feedback_history_button(
		uint64_t button, uint8_t state, uint8_t *out, int32_t *out_len);

/** chiaki_feedback_history_event_set_touchpad, which never fails. */
CHIAKI_SHIM_API void chiaki_shim_feedback_history_touchpad(
		bool down, uint8_t pointer_id, uint16_t x, uint16_t y, uint8_t *out, int32_t *out_len);

/**
 * The ring buffer, driven end to end: init at `size`, push each event in order, format, fini.
 *
 * One call because the ORDER is the finding. chiaki_feedback_history_buffer_push moves `begin`
 * BACKWARDS, so the newest event formats first and a port that appended would send a console its
 * history reversed. Driving it whole is what puts that in the bytes.
 *
 * @param events each event's bytes, laid end to end, `count` of them.
 * @param lens each event's length, `count` of them.
 */
CHIAKI_SHIM_API int32_t chiaki_shim_feedback_history_format(
		int32_t size, const uint8_t *events, const int32_t *lens, int32_t count,
		uint8_t *out, int32_t *out_size);

/**
 * PP714: a packet stats driven end to end - init, both kinds of push, get, get again, fini.
 *
 * ONE CALL BECAUSE THE RESET IS THE FINDING. chiaki_packet_stats_get with reset does not zero the
 * sequence floor, it moves seq_min UP to the current seq_max - so the second read of a stream that
 * kept arriving is not the first read repeated, and a port that reset to zero would report the
 * whole run's span as the next window's loss. Two reads out of one call is what puts that in the
 * numbers.
 *
 * The sequence numbers push on BOTH SIDES of that first get, split at `seq_split`, because the
 * second window is where a ceiling can end up numerically below its floor - and that subtraction,
 * done in int and widened, is the other half of what a port has to reproduce.
 *
 * @param gen_received each generation's received count, `gen_count` of them.
 * @param gen_lost each generation's lost count, `gen_count` of them.
 * @param seqs the sequence numbers, pushed in the order given, `seq_count` of them.
 * @param seq_split how many of them go before the first get. The rest go after it.
 * @param reset whether the FIRST get resets. The second always does not.
 * @param received the first get's received, then the second's.
 * @param lost the first get's lost, then the second's.
 */
CHIAKI_SHIM_API int32_t chiaki_shim_packet_stats_run(
		const uint64_t *gen_received, const uint64_t *gen_lost, int32_t gen_count,
		const uint16_t *seqs, int32_t seq_count, int32_t seq_split,
		bool reset, uint64_t *received, uint64_t *lost);

#ifdef __cplusplus
}
#endif

#endif // CHIAKI_SHIM_H








