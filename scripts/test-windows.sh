#!/usr/bin/env bash
# Run the unit suite in an MSYS2 MinGW64 environment and report what it said.
#
#   test-windows.sh              ctest over the whole suite, then the .NET host's selftest
#   test-windows.sh noapp        the C suite alone
#   test-windows.sh <pattern>    run the suite and print the results matching <pattern>
#   test-windows.sh --list       every test name the binary carries
#
# Env: BUILD_DIR (build), TEST_TIMEOUT (120), APP_DIR (app)
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
# PP439: how many munit cases the suite is expected to run, and its own file so the number is
# reviewable in a diff rather than buried in a script.
FLOOR_FILE="${FLOOR_FILE:-tests/c-suite-floor.txt}"
# PP75 runs the .NET host's selftest, and it does so from test.cmd rather than from here. The
# reason is the same one PP74 gave for building app\ outside build-windows.sh: this is a login
# shell, its PATH is MSYS2's, and `dotnet` is not on it even on a machine that has the SDK - the
# first version of PP75 lived here and reported "no .NET SDK on PATH" on exactly such a machine.
# Nothing about the managed half needs MSYS2, so nothing about it belongs in this file.
#
# `noapp` is accepted and ignored for the reason build-windows.sh accepts it: test.cmd forwards
# every argument, and one vocabulary both halves accept beats two kept in step.
if [[ "${1:-}" == "noapp" ]]; then
	shift
fi

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
# reporting a green over it: a suite run against the previous build is a green that answers
# about code nobody is looking at.
#
# PP720: THE QUESTION IS THE BUILD AND NOT THE TREE. This globbed lib and test for a .c newer
# than the executable, and lib/src/remote/holepunch.c left the build with PP33 and stayed in
# the checkout - the drift checks read C that no target compiles. So the warning fired on every
# run, and its own advice could not clear it: compile.cmd answers "ninja: no work to do",
# because ninja is right and the file is in no graph. A warning nobody can clear is a warning
# nobody reads, which is exactly the guard PP56 wanted.
#
# Ninja is asked instead. A dry run of the unit target says whether anything it is actually
# built from has moved - the same question, and one whose answer acting on it changes. Not a
# list of exceptions, which is PP279's finding: a hand-kept list guards what somebody thought
# of, and this file would have been added to it only after being noticed.
NINJA="${NINJA:-/mingw64/bin/ninja}"
if [[ ! -x "$NINJA" ]]; then
	echo "[test] note: no ninja at $NINJA, so the binary's freshness was not checked" >&2
else
	# Captured rather than piped into grep: with pipefail, grep -q closing the pipe early can
	# leave the pipeline non-zero on the very case that matched.
	freshness=$("$NINJA" -C "$BUILD_DIR" -n chiaki-unit 2>/dev/null)
	if [[ "$freshness" != *"no work to do"* ]]; then
		echo "[test] WARNING: $UNIT is out of date - ninja has work to do for it." >&2
		echo "[test]          Run compile.cmd - this result is about the previous build." >&2
	fi
fi

if [[ "${1:-}" == "--list" ]]; then
	exec "$UNIT" --list
fi

# Every invocation is bounded. Nothing configures a per-test timeout, so one test that
# hangs takes the whole run with it and prints nothing at all - which is how PP68 and PP70
# presented, and both cost a session before the cause was even visible. The last name the
# suite printed is the one that hung.
if [[ $# -eq 0 ]]; then
	# PP439: -V rather than --output-on-failure, because the number this gate needs is one
	# ctest discards on a green run. munit prints "N of N tests successful" and ctest reports
	# the whole suite as one test, so "100% tests passed out of 1" reads the same whether 145
	# cases ran or seven of them stopped being compiled in.
	#
	# Captured to a file rather than streamed, so the count can be read - and the failure and
	# timeout paths then print the WHOLE file. That is deliberate: --output-on-failure gave
	# everything on a red, and a hang printing nothing at all is what PP68 and PP70 each cost
	# a session to diagnose.
	ctest_out=$(mktemp)
	timeout "$TEST_TIMEOUT" "$CTEST" --test-dir "$BUILD_DIR" -V >"$ctest_out" 2>&1
	rc=$?

	if [[ $rc -eq 124 ]]; then
		cat "$ctest_out"
		echo "[test] TIMED OUT after ${TEST_TIMEOUT}s - a test is hanging, not slow." >&2
		echo "[test] The last name above is where it stopped." >&2
		rm -f "$ctest_out"
		exit $rc
	fi

	if [[ $rc -ne 0 ]]; then
		cat "$ctest_out"
		rm -f "$ctest_out"
		exit $rc
	fi

	# The green summary, which is what --output-on-failure used to leave on screen.
	grep -E '^ *[0-9]+/[0-9]+ Test|^[0-9]+% tests passed|^Total Test time' "$ctest_out"

	# PP439: the floor. munit's line is "1: 145 of 145 (100%) tests successful, ...", and the
	# SECOND number is the one that matters - a skipped case still exists, and a case that
	# stopped being compiled does not.
	cases=$(sed -nE 's/^1: [0-9]+ of ([0-9]+) \([0-9]+%\) tests successful.*/\1/p' "$ctest_out" | tail -1)
	floor=$(grep -vE '^[[:space:]]*#' "$FLOOR_FILE" 2>/dev/null | grep -oE '[0-9]+' | tail -1)
	rm -f "$ctest_out"

	if [[ -z "$cases" ]]; then
		# Not a pass and not a failure of the suite: the reader stopped matching. Said out
		# loud, because a floor nothing can read is a floor that is not there.
		echo "[test] WARNING: could not read the case count from ctest -V output." >&2
		echo "[test]          The floor in $FLOOR_FILE is unchecked for this run." >&2
		exit 0
	fi

	if [[ -z "$floor" ]]; then
		echo "[test] WARNING: no number in $FLOOR_FILE - the C suite ran $cases cases." >&2
		exit 0
	fi

	if (( cases < floor )); then
		echo "[test] the C suite ran $cases cases and the floor is $floor." >&2
		echo "[test] A suite got smaller. Check whether ffmpeg was found: " >&2
		echo "[test] CHIAKI_ENABLE_FFMPEG_DECODER is AUTO, and OFF drops seven cases." >&2
		exit 1
	fi

	if (( cases > floor )); then
		echo "[test] the C suite ran $cases cases and the floor is $floor." >&2
		echo "[test] Tests were added - raise the number in $FLOOR_FILE in this commit," >&2
		echo "[test] or the floor loosens by exactly what was just gained." >&2
		exit 1
	fi

	echo "[test] C suite: $cases munit cases (floor $floor)"
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
