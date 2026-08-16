#!/usr/bin/env bash
# Built against MSYS2's MinGW64 ffmpeg on purpose: that is the ffmpeg compile.cmd links the
# client to, so it is the one whose decoder list qmlbackend reads and whose cuda path this task
# is about. A spike built against a different ffmpeg would measure a decoder the client does not
# have.
set -euo pipefail

cd "$(dirname "$0")"

CC=${CC:-gcc}
OUT=${OUT:-decode-path.exe}

FLAGS=$(pkg-config --cflags --libs libavcodec libavformat libavutil)

# shellcheck disable=SC2086
"$CC" -O2 -Wall -Wextra -o "$OUT" decode-path.c $FLAGS

echo "built $(pwd)/$OUT"
