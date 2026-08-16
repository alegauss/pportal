#!/usr/bin/env bash
# The stream this spike decodes, built here because no console is reachable from this machine.
#
# Encoded to look like what a PS5 sends rather than like a film: 1080p60 H.264 High, ~30 Mbps
# (the bitrate the session baseline records), one keyframe a second, and no B-frames - a remote
# play encoder cannot reorder frames it has not rendered yet, and a decoder given B-frames does
# different work from one that is not. `-tune zerolatency` is what turns those knobs together.
#
# testsrc2 is deterministic, so two people running this get the same bytes and can compare
# results. The picture is synthetic and that is a real limit on what the numbers cover: decode
# time follows resolution, profile and bitrate rather than content, so the cost transfers - a
# dropped-frame count under network jitter does not, and none is claimed from this.
set -euo pipefail

cd "$(dirname "$0")"

FFMPEG=${FFMPEG:-/mingw64/bin/ffmpeg.exe}
OUT=${1:-stream.h264}
SECONDS_LONG=${SECONDS_LONG:-10}

if [ ! -x "$FFMPEG" ]; then
	# The build links MSYS2's ffmpeg, so its encoder is the first choice; anything on PATH is a
	# fallback that only affects how the bytes were made, not how they are decoded.
	FFMPEG=$(command -v ffmpeg)
fi

"$FFMPEG" -hide_banner -y \
	-f lavfi -i "testsrc2=size=1920x1080:rate=60:duration=${SECONDS_LONG}" \
	-c:v libx264 -profile:v high -pix_fmt yuv420p \
	-tune zerolatency -bf 0 -g 60 -keyint_min 60 \
	-b:v 30M -maxrate 30M -bufsize 2M \
	-f h264 "$OUT"

ls -l "$OUT"
