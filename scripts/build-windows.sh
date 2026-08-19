#!/usr/bin/env bash
# Build chiaki-ng in an MSYS2 MinGW64 environment and, by default, collect a
# portable tree that also runs outside MSYS2.
#
#   build-windows.sh                 configure + build + portable tree
#   build-windows.sh clean           wipe the build dir first
#   build-windows.sh nodeploy        build only, skip the portable tree
#   build-windows.sh notests         skip chiaki-unit, leaving ctest on a stale binary
#   build-windows.sh configure       configure only - the fast check that a
#                                    deletion did not break the build graph
#
# Env: BUILD_DIR (build), DEPLOY_DIR ($BUILD_DIR/chiaki-ng-Win), BUILD_TYPE (Release)
#
# compile.cmd in the repo root is a thin launcher around this script; the logic
# lives here because quoting a multi-step bash command through cmd is fragile.
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."

BUILD_DIR="${BUILD_DIR:-build}"
DEPLOY_DIR="${DEPLOY_DIR:-$BUILD_DIR/chiaki-ng-Win}"
BUILD_TYPE="${BUILD_TYPE:-Release}"

do_clean=0
do_deploy=1
do_build=1
do_tests=1
for arg in "$@"; do
    case "$arg" in
        clean)     do_clean=1 ;;
        deploy)    do_deploy=1 ;;
        nodeploy)  do_deploy=0 ;;
        notests)   do_tests=0 ;;
        # compile.cmd's, not this script's: the .NET host is built by dotnet from cmd and
        # never reaches cmake. Accepted and ignored rather than rejected, because
        # compile.cmd forwards every argument it was given, and one vocabulary that both
        # halves accept beats two that have to be kept in step. Rejecting it here is how
        # `compile.cmd noapp` failed with a usage error from a script that was not being
        # asked to do anything differently.
        noapp)     ;;
        # Configure answers the only question a deletion asks - is every file the
        # build graph names still there - and answers it in seconds instead of in
        # a full compile. It implies nodeploy: there is no exe to deploy.
        configure) do_build=0; do_deploy=0 ;;
        *) echo "usage: $(basename "$0") [clean] [notests] [noapp] [nodeploy|configure]" >&2; exit 2 ;;
    esac
done

if [[ $do_clean -eq 1 ]]; then
    rm -rf "$BUILD_DIR"
fi

cmake -S . -B "$BUILD_DIR" -G Ninja -DCMAKE_BUILD_TYPE="$BUILD_TYPE"

if [[ $do_build -eq 0 ]]; then
    echo "configure ok: every path the build graph names resolves"
    exit 0
fi

# chiaki-unit is built with the client, not instead of it and not on request.
#
# PP56: naming `chiaki` alone here was the whole of the defect. ninja builds what it is
# asked for, so the test target was never relinked, and `ctest` in build/ then ran
# whatever binary had last been linked by hand. That is not a suite that fails to catch a
# regression - it is a suite answering a question about code that is no longer there, and
# reporting it in the one place a developer looks in order to stop worrying. Measured
# while working PP41: a full build said OK, ctest said 100% passed, and the binary it ran
# did not contain a single one of the tests just added.
#
# So the honest default is to build them. `notests` keeps the fast path for someone who
# only wants the client, and is spelled out rather than implied so that the trade is made
# on purpose - the person who passes it knows ctest is now reporting on the past.
# chiaki-shim is in this list for the reason spelled out above, one step further along: the
# .NET host P/Invokes it, and dotnet build neither builds it nor notices that it is stale.
# Left out, a run would load whatever DLL the last build happened to leave behind and
# report on it - the same shape as PP56, with a managed assembly on the far side.
# PP9: chiaki-render is here for the same reason chiaki-shim is, and adding it was not
# optional - the first build after the target existed produced no DLL at all, because ninja
# builds what it is asked for. Left out, the managed side would load whichever
# chiaki-render.dll the last hand-run of ninja happened to leave, which is PP56 again with a
# renderer on the far side.
targets=(chiaki chiaki-shim chiaki-render)
if [[ $do_tests -eq 1 ]]; then
    # Absent when the tree was configured with -DCHIAKI_ENABLE_TESTS=OFF, and asking ninja
    # for a target that does not exist is a hard error. Read from the cache the configure
    # above just wrote, so the answer is this build dir's and not a guess.
    if grep -qx 'CHIAKI_ENABLE_TESTS:BOOL=ON' "$BUILD_DIR/CMakeCache.txt" 2>/dev/null; then
        targets+=(chiaki-unit)
    else
        echo "note: CHIAKI_ENABLE_TESTS is off in $BUILD_DIR, so there is no chiaki-unit to build" >&2
        echo "note: ctest in that directory has nothing current to run" >&2
    fi
fi

cmake --build "$BUILD_DIR" --target "${targets[@]}"

if [[ $do_tests -eq 0 ]]; then
    echo "note: chiaki-unit was not built (notests), so ctest in $BUILD_DIR reports on an older binary" >&2
fi

if [[ $do_deploy -eq 1 ]]; then
    # The tool dir goes on PATH inside the deploy script so that ldd can resolve
    # libcpp-steam-tools.dll, so it has to be a POSIX path: a Windows-style
    # "d:/..." would split on its own colon and silently drop the entry.
    ./scripts/deploy-windows-msys2.sh \
        "$DEPLOY_DIR" \
        "$BUILD_DIR/gui/chiaki.exe" \
        "$PWD/$BUILD_DIR/third-party/cpp-steam-tools" \
        /mingw64 \
        gui/src/qml
fi
