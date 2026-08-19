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
#define CHIAKI_SHIM_ABI 13

CHIAKI_SHIM_API uint32_t chiaki_shim_abi_version(void);

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

/** The line, as the Qt build writes it. `written` may be NULL. Returns a ChiakiErrorCode. */
CHIAKI_SHIM_API int32_t chiaki_shim_baseline_format(
		void *baseline, char *buf, int32_t buf_size, int32_t *written);

/** Appends the line to the ledger at `path`, creating it if it is not there. */
CHIAKI_SHIM_API int32_t chiaki_shim_baseline_append(void *baseline, const char *path);

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

CHIAKI_SHIM_API void chiaki_shim_rpcrypt_free(void *rpcrypt);

/** chiaki_rpcrypt_generate_iv, which is what every counter's block is encrypted under. */
CHIAKI_SHIM_API int32_t chiaki_shim_rpcrypt_generate_iv(void *rpcrypt, uint64_t counter, uint8_t *iv);

CHIAKI_SHIM_API int32_t chiaki_shim_rpcrypt_encrypt(
		void *rpcrypt, uint64_t counter, const uint8_t *in, uint8_t *out, int32_t size);

CHIAKI_SHIM_API int32_t chiaki_shim_rpcrypt_decrypt(
		void *rpcrypt, uint64_t counter, const uint8_t *in, uint8_t *out, int32_t size);

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

#ifdef __cplusplus
}
#endif

#endif // CHIAKI_SHIM_H
