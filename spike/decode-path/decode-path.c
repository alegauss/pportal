// PP48: what the cuda decoder the client already prefers actually costs, against the two paths
// it is chosen over.
//
// qmlbackend picks cuda whenever qmlmainwindow reports an NVIDIA card and ffmpeg lists a cuda
// decoder (gui/src/qmlbackend.cpp:940, :973). That decision is made today, on hardware detection
// alone, with no decode time, no jitter and no dropped-frame count behind it. This harness
// produces those numbers.
//
// WHAT IT MEASURES, AND WHY EACH ONE
//
//   send      time inside avcodec_send_packet
//   pull      time inside an avcodec_receive_frame that returned a frame
//   readback  time inside av_hwframe_transfer_data on the frame that came out
//   fps       frames divided by the wall clock of the whole run
//
// The first two together are the "decoder send-to-pull" stage PP41 already times inside the
// client, so a number here and a number in chiaki_baseline.jsonl name the same interval.
//
// The third is the one this task was filed for. gui/src/qmlmainwindow.cpp:3355 calls
// snapshotLastFrame on every queued frame, and make_fallback_snapshot_frame (:2285) answers with
// av_hwframe_transfer_data for any hw frame whose format is not AV_PIX_FMT_VULKAN. So on the cuda
// and d3d11va paths the client copies every presented frame out of device memory and back through
// system memory, and on the vulkan path it does not. That is a per-frame cost the vendor
// preference silently opts into, and the readback column is what it costs.
//
// The decoder setup is copied from chiaki_ffmpeg_decoder_init (lib/src/ffmpegdecoder.c:74-105)
// rather than written afresh: av_hwdevice_find_type_by_name on the same names the settings
// accept, the first avcodec_get_hw_config offering HW_DEVICE_CTX for that type, and no
// get_format callback - the default one picks the hw format because hw_device_ctx is set. A
// harness that configured the decoder its own way would measure its own decoder.
//
// WHAT IT DOES NOT MEASURE. There is no console on this machine, so the stream is encoded here
// rather than captured from one (see make-stream.sh). Decode time follows resolution, profile and
// bitrate rather than content, so the cost transfers; a frame-drop count under real network
// jitter does not, and none is claimed.

#include <inttypes.h>
#include <stdbool.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

// PP66: before every other include, and that ordering is load-bearing. hwcontext_d3d11va.h pulls
// in d3d11.h itself, and d3d11.h only emits the ID3D11Device_* call macros a C caller needs if
// COBJMACROS was defined by the time it was first read. Defined afterwards it compiles to five
// implicit declarations and links to nothing.
#define COBJMACROS
#include <d3d11.h>
#include <dxgi.h>

#include <libavcodec/avcodec.h>
#include <libavformat/avformat.h>
#include <libavutil/hwcontext.h>
#include <libavutil/hwcontext_d3d11va.h>
#include <libavutil/pixdesc.h>
#include <libavutil/time.h>

#define MAX_SAMPLES 8192

// Every sample is kept and the percentile is exact. A run is a few thousand frames and the
// doubles cost nothing, which is the same trade spike/present-path/Stats.cs makes and for the
// same reason: the resolution of the number should not be arguable.
typedef struct {
	const char *name;
	double v[MAX_SAMPLES];
	int n;
	int dropped; // samples past MAX_SAMPLES, reported rather than silently absent
} Stats;

static void stats_push(Stats *s, double us)
{
	if (s->n < MAX_SAMPLES)
		s->v[s->n++] = us;
	else
		s->dropped++;
}

static int cmp_double(const void *a, const void *b)
{
	double x = *(const double *)a, y = *(const double *)b;
	return (x > y) - (x < y);
}

static double stats_mean(const Stats *s)
{
	if (!s->n)
		return 0;
	double sum = 0;
	for (int i = 0; i < s->n; i++)
		sum += s->v[i];
	return sum / s->n;
}

// Nearest-rank on a sorted copy, ceil(p * n) - the same rank convention the C record uses, so at
// p99 exactly 1% may sit above.
static double stats_pct(const Stats *s, double p)
{
	if (!s->n)
		return 0;
	double *sorted = malloc((size_t)s->n * sizeof(double));
	if (!sorted)
		return 0;
	memcpy(sorted, s->v, (size_t)s->n * sizeof(double));
	qsort(sorted, (size_t)s->n, sizeof(double), cmp_double);
	int rank = (int)(p * s->n + 0.9999999);
	if (rank < 1)
		rank = 1;
	if (rank > s->n)
		rank = s->n;
	double out = sorted[rank - 1];
	free(sorted);
	return out;
}

static double stats_min(const Stats *s)
{
	if (!s->n)
		return 0;
	double m = s->v[0];
	for (int i = 1; i < s->n; i++)
		if (s->v[i] < m)
			m = s->v[i];
	return m;
}

static double stats_max(const Stats *s)
{
	if (!s->n)
		return 0;
	double m = s->v[0];
	for (int i = 1; i < s->n; i++)
		if (s->v[i] > m)
			m = s->v[i];
	return m;
}

// The first frames of a run pay for decoder and hwaccel initialisation - on this machine the
// largest single send in a cold cuda run was 39.7 ms against a 1.5 ms median, which is setup and
// not decode. Discarded rather than reported, the same count spike/video-upscale warms up by.
#define WARMUP_FRAMES 30

typedef struct {
	const char *name;   // as the settings spell it: "software", "cuda", "d3d11va", "vulkan"
	const char *label;  // what to print, which in the pool sweep is not the device name
	int extra_hw_frames;// PP65: added to the decoder's surface pool, 0 for the default
	int hold;           // PP65: frames kept referenced at once, 1 for "release immediately"
	bool paced;         // PP65: feed at 60 fps instead of as fast as the decoder will take it
	bool ran;
	const char *skipped_why;
	char pix_fmt[32];   // what came out of the decoder
	char sw_pix_fmt[32];// what a readback produced, or "-"
	bool device_memory; // frame->hw_frames_ctx was set
	int frames;         // frames counted after warmup, in the decode-only pass
	double wall_us;     // wall clock of those frames, with no readback in it
	Stats send;
	Stats pull;
	Stats readback;
} Path;

// Mirrors chiaki_ffmpeg_decoder_init. Returns NULL and fills *why on any refusal, because a path
// this machine cannot run has to be reported as absent rather than as slow.
static AVCodecContext *open_decoder(const char *hw_name, int extra_hw_frames,
                                    AVBufferRef **hw_device_ctx, const char **why)
{
	const AVCodec *codec = avcodec_find_decoder(AV_CODEC_ID_H264);
	if (!codec) {
		*why = "no h264 decoder in this ffmpeg";
		return NULL;
	}

	AVCodecContext *ctx = avcodec_alloc_context3(codec);
	if (!ctx) {
		*why = "avcodec_alloc_context3 failed";
		return NULL;
	}

	if (hw_name) {
		enum AVHWDeviceType type = av_hwdevice_find_type_by_name(hw_name);
		if (type == AV_HWDEVICE_TYPE_NONE) {
			*why = "av_hwdevice_find_type_by_name: this ffmpeg has no such device type";
			goto fail;
		}

		bool configured = false;
		for (int i = 0;; i++) {
			const AVCodecHWConfig *config = avcodec_get_hw_config(codec, i);
			if (!config)
				break;
			if ((config->methods & AV_CODEC_HW_CONFIG_METHOD_HW_DEVICE_CTX)
			    && config->device_type == type) {
				configured = true;
				break;
			}
		}
		if (!configured) {
			*why = "avcodec_get_hw_config: h264 offers no HW_DEVICE_CTX for this type";
			goto fail;
		}

		if (av_hwdevice_ctx_create(hw_device_ctx, type, NULL, NULL, 0) < 0) {
			*why = "av_hwdevice_ctx_create failed - the driver refused the device";
			goto fail;
		}
		ctx->hw_device_ctx = av_buffer_ref(*hw_device_ctx);
	}

	// PP65: the one knob this task varies. extra_hw_frames is added to the surface pool the
	// decoder allocates, so if the stall is the decoder waiting for a surface to come back,
	// raising it should move the p99 and nothing else here should change.
	ctx->extra_hw_frames = extra_hw_frames;

	// The client leaves get_format alone too: with hw_device_ctx set, ffmpeg's default picks the
	// hw format. Setting one here would be a second decoder configuration to explain.
	if (avcodec_open2(ctx, codec, NULL) < 0) {
		*why = "avcodec_open2 failed";
		goto fail;
	}
	return ctx;

fail:
	if (*hw_device_ctx)
		av_buffer_unref(hw_device_ctx);
	avcodec_free_context(&ctx);
	return NULL;
}

// PP66: which card produced the numbers, read out of the device rather than off the filename.
// spike/video-upscale already does this - it creates a D3D11 device to do its work and prints
// DescribeAdapter beside every result - and the reason decode-path never did is that it asks
// libavcodec for a hardware context and never touches DXGI itself.
//
// d3d11va is the one of the four device types whose AVHWDeviceContext carries an ID3D11Device, and
// an ID3D11Device is what DXGI will answer about. So this is the adapter the driver handed ffmpeg,
// not one enumerated independently: on a machine with a discrete card and an iGPU there are two
// descriptions and only one of them is the run's. It is created and released before the first pass
// so no extra device is alive while anything is being timed.
//
// Every failure lands in the string rather than in a return code. A result that says why it could
// not name the card is still readable years later; one with the field missing is the defect.
static void describe_adapter(char *out, size_t n)
{
	AVBufferRef *ref = NULL;
	if (av_hwdevice_ctx_create(&ref, AV_HWDEVICE_TYPE_D3D11VA, NULL, NULL, 0) < 0) {
		snprintf(out, n, "unknown - av_hwdevice_ctx_create(d3d11va) refused");
		return;
	}

	AVHWDeviceContext *dev = (AVHWDeviceContext *)ref->data;
	AVD3D11VADeviceContext *d3d = dev->hwctx;
	IDXGIDevice *dxgi = NULL;
	IDXGIAdapter *adapter = NULL;
	DXGI_ADAPTER_DESC desc;

	if (SUCCEEDED(ID3D11Device_QueryInterface(d3d->device, &IID_IDXGIDevice, (void **)&dxgi))
	    && SUCCEEDED(IDXGIDevice_GetAdapter(dxgi, &adapter))
	    && SUCCEEDED(IDXGIAdapter_GetDesc(adapter, &desc))) {
		char name[128] = "?";
		if (WideCharToMultiByte(CP_UTF8, 0, desc.Description, -1, name, (int)sizeof(name),
		                        NULL, NULL) <= 0)
			snprintf(name, sizeof(name), "?");
		// The description is a fixed WCHAR[128] the driver pads, and a JSON string cannot carry
		// a quote it did not open. Both are the file's problem rather than the reader's.
		for (char *c = name; *c; c++)
			if (*c == '"' || *c == '\\')
				*c = '\'';
		for (size_t i = strlen(name); i > 0 && name[i - 1] == ' '; i--)
			name[i - 1] = '\0';
		snprintf(out, n, "%s (vendor 0x%04x, device 0x%04x)", name, (unsigned)desc.VendorId,
		         (unsigned)desc.DeviceId);
	} else {
		snprintf(out, n, "unknown - DXGI would not describe the d3d11va device");
	}

	if (adapter)
		IDXGIAdapter_Release(adapter);
	if (dxgi)
		IDXGIDevice_Release(dxgi);
	av_buffer_unref(&ref);
}

static void name_fmt(char *out, size_t n, int fmt)
{
	const char *s = av_get_pix_fmt_name((enum AVPixelFormat)fmt);
	snprintf(out, n, "%s", s ? s : "?");
}

// One pass over the stream. `do_readback` is the whole reason there are two of them: with the
// transfer inside the loop there is no way to say how much of the wall clock was decode, because
// a hardware decoder is free to defer its synchronisation into whichever call next touches the
// surface - which is exactly the mistake spike/video-upscale made with Flush and had to measure
// its way out of. So the decode-only pass reports throughput and the readback pass reports the
// copy, and neither number is carrying the other.
static bool run_pass(Path *p, const char *file, bool do_readback)
{
	const char *hw = strcmp(p->name, "software") == 0 ? NULL : p->name;
	AVBufferRef *hw_device_ctx = NULL;
	const char *why = "unknown";
	AVCodecContext *ctx = open_decoder(hw, p->extra_hw_frames, &hw_device_ctx, &why);
	if (!ctx) {
		p->skipped_why = why;
		return false;
	}

	// PP65's other hypothesis: that the stall is the decoder waiting for a surface the caller is
	// sitting on. `hold` is how many decoded frames are kept referenced at once, so hold=1 is
	// "release it immediately" and a larger number starves the pool on purpose.
	int hold = p->hold > 0 ? p->hold : 1;
	AVFrame **held = calloc((size_t)hold, sizeof(*held));
	int held_next = 0;

	AVFormatContext *fmt = NULL;
	if (avformat_open_input(&fmt, file, NULL, NULL) < 0) {
		p->skipped_why = "cannot open the stream file";
		goto done_ctx;
	}
	if (avformat_find_stream_info(fmt, NULL) < 0) {
		p->skipped_why = "avformat_find_stream_info failed";
		goto done_fmt;
	}
	int vstream = av_find_best_stream(fmt, AVMEDIA_TYPE_VIDEO, -1, -1, NULL, 0);
	if (vstream < 0) {
		p->skipped_why = "no video stream in the file";
		goto done_fmt;
	}

	AVPacket *pkt = av_packet_alloc();
	AVFrame *frame = av_frame_alloc();
	AVFrame *sw = av_frame_alloc();
	if (!pkt || !frame || !sw) {
		p->skipped_why = "out of memory";
		goto done_alloc;
	}

	int64_t wall0 = 0;      // taken once warmup is over, not at the top of the loop
	int seen = 0;           // every frame, warmup included
	int counted = 0;        // frames the numbers are computed from
	bool flushed = false;
	// PP65: a real session hands the decoder one frame every 16.7 ms. This harness hands it the
	// next one the instant the last returned, which is about twelve times faster than that and
	// lets work pile up behind an asynchronous submission. Pacing is how that difference is
	// ruled in or out rather than argued about.
	int64_t paced_next = av_gettime_relative();
	for (;;) {
		if (p->paced) {
			int64_t now = av_gettime_relative();
			if (paced_next > now)
				av_usleep((unsigned)(paced_next - now));
			paced_next += 16667;
		}
		int ret;
		if (!flushed) {
			ret = av_read_frame(fmt, pkt);
			if (ret < 0) {
				flushed = true;
				av_packet_unref(pkt);
			} else if (pkt->stream_index != vstream) {
				av_packet_unref(pkt);
				continue;
			}
		}

		int64_t t0 = av_gettime_relative();
		ret = avcodec_send_packet(ctx, flushed ? NULL : pkt);
		int64_t t1 = av_gettime_relative();
		if (!flushed)
			av_packet_unref(pkt);
		if (ret < 0 && ret != AVERROR(EAGAIN) && ret != AVERROR_EOF)
			break;
		if (seen >= WARMUP_FRAMES && !do_readback)
			stats_push(&p->send, (double)(t1 - t0));

		for (;;) {
			int64_t t2 = av_gettime_relative();
			ret = avcodec_receive_frame(ctx, frame);
			int64_t t3 = av_gettime_relative();
			if (ret == AVERROR(EAGAIN) || ret == AVERROR_EOF)
				break;
			if (ret < 0)
				goto decode_done;

			if (seen == 0) {
				name_fmt(p->pix_fmt, sizeof(p->pix_fmt), frame->format);
				p->device_memory = frame->hw_frames_ctx != NULL;
			}
			seen++;
			if (seen == WARMUP_FRAMES)
				wall0 = av_gettime_relative();

			if (seen > WARMUP_FRAMES) {
				counted++;
				if (!do_readback)
					stats_push(&p->pull, (double)(t3 - t2));
			}

			// The copy the client makes on every queued frame for anything that is not
			// vulkan - gui/src/qmlmainwindow.cpp:2285, reached from :3355. Timed on the
			// frame that was just decoded, so the number is the one the present path pays.
			if (do_readback && frame->hw_frames_ctx) {
				int64_t t4 = av_gettime_relative();
				int tr = av_hwframe_transfer_data(sw, frame, 0);
				int64_t t5 = av_gettime_relative();
				if (tr >= 0) {
					if (seen > WARMUP_FRAMES)
						stats_push(&p->readback, (double)(t5 - t4));
					if (p->sw_pix_fmt[0] == '-')
						name_fmt(p->sw_pix_fmt, sizeof(p->sw_pix_fmt), sw->format);
				}
				av_frame_unref(sw);
			}

			// Released straight away when hold is 1, which is what every other measurement in
			// this spike does. Above that the frame goes into a ring and the one it displaces
			// is released, so exactly `hold` surfaces are out of the pool at any moment.
			if (hold == 1 || !held) {
				av_frame_unref(frame);
			} else {
				if (held[held_next])
					av_frame_free(&held[held_next]);
				held[held_next] = av_frame_clone(frame);
				held_next = (held_next + 1) % hold;
				av_frame_unref(frame);
			}
		}
		if (flushed)
			break;
	}
decode_done:
	if (!do_readback) {
		p->wall_us = wall0 ? (double)(av_gettime_relative() - wall0) : 0;
		p->frames = counted;
		p->ran = counted > 0 && p->wall_us > 0;
		if (!p->ran)
			p->skipped_why = counted > 0
				? "fewer frames than the warmup, so nothing was timed"
				: "the decoder produced no frames";
	}

done_alloc:
	if (held) {
		for (int i = 0; i < hold; i++)
			if (held[i])
				av_frame_free(&held[i]);
		free(held);
	}
	av_frame_free(&sw);
	av_frame_free(&frame);
	av_packet_free(&pkt);
done_fmt:
	avformat_close_input(&fmt);
done_ctx:
	avcodec_free_context(&ctx);
	if (hw_device_ctx)
		av_buffer_unref(&hw_device_ctx);
	return p->ran;
}

static bool run_path(Path *p, const char *file)
{
	p->send.name = "send";
	p->pull.name = "pull";
	p->readback.name = "readback";
	snprintf(p->pix_fmt, sizeof(p->pix_fmt), "-");
	snprintf(p->sw_pix_fmt, sizeof(p->sw_pix_fmt), "-");

	if (!run_pass(p, file, false))
		return false;
	// Only a path that landed in device memory has anything to copy back, and only those are
	// what the client's fallback snapshot acts on.
	if (p->device_memory)
		run_pass(p, file, true);
	return true;
}

// The client's own rule, transcribed rather than restated: make_fallback_snapshot_frame
// (gui/src/qmlmainwindow.cpp:2290) returns early when there is no hw_frames_ctx or the format is
// AV_PIX_FMT_VULKAN, and calls av_hwframe_transfer_data otherwise. So "vulkan" is the one
// hardware path exempt from the copy, and the preference for cuda is a preference for paying it.
static bool client_copies_back(const Path *p)
{
	return p->device_memory && strcmp(p->pix_fmt, "vulkan") != 0;
}

static void print_stats(const char *label, const Stats *s)
{
	if (!s->n) {
		printf("  %-9s -\n", label);
		return;
	}
	printf("  %-9s n=%-5d min=%8.1f  mean=%8.1f  p50=%8.1f  p99=%9.1f  max=%9.1f  (us)",
	       label, s->n, stats_min(s), stats_mean(s), stats_pct(s, 0.50), stats_pct(s, 0.99),
	       stats_max(s));
	if (s->dropped)
		printf("  [%d samples past the %d kept]", s->dropped, MAX_SAMPLES);
	printf("\n");
}

static void json_stats(FILE *f, const char *key, const Stats *s)
{
	if (!s->n) {
		fprintf(f, ",\"%s\":null", key);
		return;
	}
	fprintf(f, ",\"%s\":{\"samples\":%d,\"min_us\":%.1f,\"mean_us\":%.1f,\"p50_us\":%.1f"
	           ",\"p99_us\":%.1f,\"max_us\":%.1f}",
	        key, s->n, stats_min(s), stats_mean(s), stats_pct(s, 0.50), stats_pct(s, 0.99),
	        stats_max(s));
}

int main(int argc, char **argv)
{
	const char *file = "stream.h264";
	const char *out = "result.json";
	bool pool_sweep = false;
	// PP71: the paced cuda number has to be reproducible in a process that has not just built and
	// torn down seven other decoders, because contamination is one of the two candidate causes.
	// --only picks configurations by a substring of their label, so "cuda    paced" runs alone.
	const char *only = NULL;
	int positional = 0;

	for (int i = 1; i < argc; i++) {
		if (strcmp(argv[i], "--pool-sweep") == 0)
			pool_sweep = true;
		else if (strcmp(argv[i], "--only") == 0 && i + 1 < argc)
			only = argv[++i];
		else if (positional++ == 0)
			file = argv[i];
		else
			out = argv[i];
	}

	// The order the client would try them in on an NVIDIA card, with software first as the
	// baseline every other row is read against.
	// static, not automatic: a Path carries three Stats and each of those keeps MAX_SAMPLES
	// doubles, so one is about 192 KB and eleven of them overflow a 1 MB stack before main does
	// anything. That is exactly how this failed first - exit 0xC00000FD, no output at all,
	// because the crash took the unflushed stdout buffer with it.
	static Path default_paths[] = {
		{ .name = "software", .label = "software" },
		{ .name = "cuda", .label = "cuda" },
		{ .name = "d3d11va", .label = "d3d11va" },
		{ .name = "vulkan", .label = "vulkan" },
	};

	// PP65: d3d11va's send is bimodal - a 103us median against a 26990us p99, which is 1.6 frame
	// intervals. The first thing to rule out is the decoder waiting on a surface, so this varies
	// the two quantities that would decide that and nothing else: the size of the pool, and how
	// many of its surfaces the caller is sitting on. cuda is carried through the same sweep as a
	// control, because a p99 that moves for both is not about the pool.
	static Path sweep_paths[] = {
		{ .name = "d3d11va", .label = "d3d11va pool+0  hold 1", .extra_hw_frames = 0,  .hold = 1 },
		{ .name = "d3d11va", .label = "d3d11va pool+4  hold 1", .extra_hw_frames = 4,  .hold = 1 },
		{ .name = "d3d11va", .label = "d3d11va pool+16 hold 1", .extra_hw_frames = 16, .hold = 1 },
		{ .name = "d3d11va", .label = "d3d11va pool+0  hold 8", .extra_hw_frames = 0,  .hold = 8 },
		{ .name = "d3d11va", .label = "d3d11va pool+16 hold 8", .extra_hw_frames = 16, .hold = 8 },
		{ .name = "cuda",    .label = "cuda    pool+0  hold 1", .extra_hw_frames = 0,  .hold = 1 },
		{ .name = "cuda",    .label = "cuda    pool+16 hold 1", .extra_hw_frames = 16, .hold = 1 },
		// The same two paths fed at the rate a console sends them, which is the one difference
		// between this harness and a session.
		{ .name = "d3d11va", .label = "d3d11va paced 60fps",    .hold = 1, .paced = true },
		{ .name = "cuda",    .label = "cuda    paced 60fps",    .hold = 1, .paced = true },
		{ .name = "vulkan",  .label = "vulkan  paced 60fps",    .hold = 1, .paced = true },
	};

	Path *paths = pool_sweep ? sweep_paths : default_paths;
	const int npaths = pool_sweep
		? (int)(sizeof(sweep_paths) / sizeof(sweep_paths[0]))
		: (int)(sizeof(default_paths) / sizeof(default_paths[0]));

	char adapter[192];
	describe_adapter(adapter, sizeof(adapter));

	printf("stream     : %s\n", file);
	printf("ffmpeg     : libavcodec %d.%d.%d\n", LIBAVCODEC_VERSION_MAJOR,
	       LIBAVCODEC_VERSION_MINOR, LIBAVCODEC_VERSION_MICRO);
	printf("adapter    : %s\n", adapter);
	printf("\n");

	int ran = 0;
	for (int i = 0; i < npaths; i++) {
		Path *p = &paths[i];
		const char *label = p->label ? p->label : p->name;
		if (only && !strstr(label, only))
			continue;
		printf("=== %s\n", label);
		fflush(stdout);
		if (!run_path(p, file)) {
			printf("  not available: %s\n\n", p->skipped_why);
			continue;
		}
		ran++;
		printf("  decode    %d frames in %.1f ms -> %.0f fps, no readback in the clock\n",
		       p->frames, p->wall_us / 1000.0, p->frames * 1e6 / p->wall_us);
		printf("  pix_fmt   %s%s, a readback yields %s\n", p->pix_fmt,
		       p->device_memory ? " (device memory)" : " (system memory)", p->sw_pix_fmt);
		print_stats("send", &p->send);
		print_stats("pull", &p->pull);
		print_stats("readback", &p->readback);
		if (p->readback.n) {
			double mean = stats_mean(&p->readback);
			printf("  client    %s - %s\n",
			       client_copies_back(p) ? "COPIES THIS BACK on every queued frame"
			                             : "does not copy this back",
			       client_copies_back(p)
			           ? "qmlmainwindow.cpp:2285 exempts vulkan and nothing else"
			           : "make_fallback_snapshot_frame returns early for AV_PIX_FMT_VULKAN");
			printf("            at 60 fps that is %.1f%% of a frame interval at the mean, "
			       "%.1f%% at p99%s\n",
			       100.0 * mean / 16666.7, 100.0 * stats_pct(&p->readback, 0.99) / 16666.7,
			       client_copies_back(p) ? "" : " - if it were paid");
		}
		printf("\n");
	}

	if (ran == 0) {
		fprintf(stderr, "!! No decode path ran. There is nothing to compare and no number below.\n");
		return 1;
	}

	FILE *f = fopen(out, "w");
	if (!f) {
		fprintf(stderr, "cannot write %s\n", out);
		return 2;
	}
	fprintf(f, "{\"spike\":\"decode-path\",\"task\":\"PP48\",\"stream\":\"%s\"", file);
	fprintf(f, ",\"adapter\":\"%s\"", adapter);
	fprintf(f, ",\"libavcodec\":\"%d.%d.%d\"", LIBAVCODEC_VERSION_MAJOR,
	        LIBAVCODEC_VERSION_MINOR, LIBAVCODEC_VERSION_MICRO);
	fprintf(f, ",\"paths\":[");
	bool first = true;
	for (int i = 0; i < npaths; i++) {
		Path *p = &paths[i];
		const char *label = p->label ? p->label : p->name;
		if (only && !strstr(label, only))
			continue;
		if (!first)
			fprintf(f, ",");
		first = false;
		fprintf(f, "{\"name\":\"%s\",\"ran\":%s", p->label ? p->label : p->name,
		        p->ran ? "true" : "false");
		fprintf(f, ",\"device\":\"%s\",\"extra_hw_frames\":%d,\"hold\":%d",
		        p->name, p->extra_hw_frames, p->hold ? p->hold : 1);
		if (!p->ran) {
			fprintf(f, ",\"why\":\"%s\"}", p->skipped_why ? p->skipped_why : "unknown");
			continue;
		}
		fprintf(f, ",\"frames\":%d,\"wall_ms\":%.1f,\"fps\":%.1f", p->frames,
		        p->wall_us / 1000.0, p->frames * 1e6 / p->wall_us);
		fprintf(f, ",\"pix_fmt\":\"%s\",\"device_memory\":%s,\"readback_pix_fmt\":\"%s\"",
		        p->pix_fmt, p->device_memory ? "true" : "false", p->sw_pix_fmt);
		fprintf(f, ",\"client_copies_back_per_frame\":%s",
		        client_copies_back(p) ? "true" : "false");
		json_stats(f, "send_us", &p->send);
		json_stats(f, "pull_us", &p->pull);
		json_stats(f, "readback_us", &p->readback);
		fprintf(f, "}");
	}
	fprintf(f, "]}\n");
	fclose(f);
	printf("json       : %s\n", out);
	return 0;
}
