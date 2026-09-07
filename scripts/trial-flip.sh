#!/usr/bin/env bash
# ---------------------------------------------------------------------------
# PP803: build the shape an arranged flip leaves, instead of reading about it.
#
# Two deletions are prepared and neither has landed - PP696 takes the frame path's C files,
# PP27's end state takes takion's - and every check on either one reads TEXT: a define beside
# the source list, guards in main.c, readers in app/ that assert the two agree. PP786 built
# takion's bare shape once, by hand, and it failed to link: --wrap makes every malloc in every
# linked object an undefined reference to __wrap_malloc, and the only definition of that symbol
# is in allocbudget.c, the one file BOTH flips remove. Every text check was green while that was
# true, and PP760's own trial had edited text and never rebuilt the C.
#
# THIS EDITS NOTHING. The trial is a cache variable test/CMakeLists.txt reads, configured into a
# build directory of its own and thrown away - so an interrupted run leaves no half-flipped tree,
# which a script that backed files up and restored them could not promise.
#
# WHAT IT BUILDS IS chiaki-unit, which is the target PP786's failure was in and the one that needs
# no edit to lib/. The library's own after-shape wants PP780's four files and the shim's wrappers
# behind their define; both belong to the flip's own commit, and this is what a person runs before
# touching either arrangement.
#
# A FAILING BUILD IS AN ANSWER AND NOT AN ERROR IN THIS SCRIPT. It exits non-zero so a caller can
# tell, and says which flip it was about.
# ---------------------------------------------------------------------------
set -euo pipefail

FLIP="${1:-}"

case "$FLIP" in
	takion|framepath) ;;
	*)
		echo "[flip] usage: trial-flip.sh <takion|framepath>" >&2
		echo "[flip] builds chiaki-unit as that flip would leave it, in a build dir of its own" >&2
		exit 2
		;;
esac

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
BUILD="${TRIAL_BUILD_DIR:-$REPO/build-flip-$FLIP}"

# Fresh every time. A cache left from a previous trial answers about the flip it was configured
# for, which is the one failure this whole exercise exists to stop being possible.
rm -rf "$BUILD"

echo "[flip] $FLIP: configuring chiaki-unit as the flip leaves it, in $BUILD"

if ! cmake -S "$REPO" -B "$BUILD" -G Ninja \
		-DCMAKE_BUILD_TYPE=Release \
		-DCHIAKI_TRIAL_FLIP="$FLIP" \
		-DCHIAKI_ENABLE_GUI=OFF >"$BUILD.log" 2>&1; then
	echo "[flip] $FLIP: CONFIGURE FAILED - the tail of $BUILD.log:" >&2
	tail -n 30 "$BUILD.log" >&2
	exit 1
fi

echo "[flip] $FLIP: building"

if ! cmake --build "$BUILD" --target chiaki-unit >>"$BUILD.log" 2>&1; then
	echo "[flip] $FLIP: BUILD FAILED - the tail of $BUILD.log:" >&2
	tail -n 30 "$BUILD.log" >&2
	echo "[flip] This is the answer the trial exists for: the shape does not build yet." >&2
	exit 1
fi

echo "[flip] $FLIP: chiaki-unit builds and links without those files."

# Removed on success only. A failed trial leaves the directory and the log where somebody can
# read them, which is the whole of what a failed trial is for.
rm -rf "$BUILD" "$BUILD.log"
