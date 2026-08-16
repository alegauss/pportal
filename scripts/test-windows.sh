#!/usr/bin/env bash
# Run the unit suite in an MSYS2 MinGW64 environment and report what it said.
#
#   test-windows.sh              ctest over the whole suite
#   test-windows.sh <pattern>    run the suite and print the results matching <pattern>
#   test-windows.sh --list       every test name the binary carries
#
# Env: BUILD_DIR (build), TEST_TIMEOUT (120)
#
# test.cmd in the repo root is a thin launcher around this script, for the reason
# compile.cmd is one: ctest is not on a plain Windows PATH - it lives in /mingw64/bin -
# and the binary it starts needs the MinGW runtime beside it. Reconstructing that from
# memory is the papercut this file exists to remove.
set -uo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."

BUILD_DIR="${BUILD_DIR:-build}"
TEST_TIMEOUT="${TEST_TIMEOUT:-120}"
CTEST="${CTEST:-/mingw64/bin/ctest}"
UNIT="$BUILD_DIR/test/chiaki-unit.exe"

# Named here rather than discovered inside cmake, so "you have not built it" is one line
# instead of a CMake error about a missing test file.
if [[ ! -d "$BUILD_DIR" ]]; then
	echo "[test] no build directory at $BUILD_DIR - run compile.cmd first" >&2
	exit 1
fi
if [[ ! -f "$UNIT" ]]; then
	echo "[test] $UNIT does not exist." >&2
	echo "[test] compile.cmd builds it by default (PP56); 'compile.cmd notests' does not." >&2
	exit 1
fi

# A stale binary is the failure PP56 fixed, and it is worth saying out loud rather than
# reporting a green over it: anything under lib/ or test/ newer than the executable means
# ctest is about to answer a question about code that is not in it.
newer=$(find lib test -name '*.c' -newer "$UNIT" -print -quit 2>/dev/null)
if [[ -n "$newer" ]]; then
	echo "[test] WARNING: $newer is newer than $UNIT." >&2
	echo "[test]          Run compile.cmd - this result is about the previous build." >&2
fi

if [[ "${1:-}" == "--list" ]]; then
	exec "$UNIT" --list
fi

# Every invocation is bounded. Nothing configures a per-test timeout, so one test that
# hangs takes the whole run with it and prints nothing at all - which is how PP68 and PP70
# presented, and both cost a session before the cause was even visible. The last name the
# suite printed is the one that hung.
if [[ $# -eq 0 ]]; then
	timeout "$TEST_TIMEOUT" "$CTEST" --test-dir "$BUILD_DIR" --output-on-failure
	rc=$?
	if [[ $rc -eq 124 ]]; then
		echo "[test] TIMED OUT after ${TEST_TIMEOUT}s - a test is hanging, not slow." >&2
		echo "[test] Run 'test.cmd <name>' to see how far the suite got." >&2
	fi
	exit $rc
fi

# One test by name, the long way round. chiaki-unit accepts a name argument and answers
# "No tests run, 0 (100%) skipped" to every one of them, including a name copied out of its
# own --list, so filtering has to happen on the output rather than at the binary. Reported
# rather than worked around silently: if that filter is ever fixed, this branch goes.
pattern="$1"
out=$(mktemp)
timeout "$TEST_TIMEOUT" "$UNIT" --log-visible info --show-stderr >"$out" 2>&1
rc=$?
if [[ $rc -eq 124 ]]; then
	echo "[test] TIMED OUT after ${TEST_TIMEOUT}s. The last name below is where it stopped." >&2
	tail -3 "$out" >&2
	rm -f "$out"
	exit 124
fi

if ! grep -A6 -- "$pattern" "$out"; then
	echo "[test] no test name matched '$pattern'. Try test.cmd --list." >&2
	rm -f "$out"
	exit 2
fi
rm -f "$out"
exit $rc
