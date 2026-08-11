#!/usr/bin/env bash
# Build chiaki-ng in an MSYS2 MinGW64 environment and, by default, collect a
# portable tree that also runs outside MSYS2.
#
#   build-windows.sh                 configure + build + portable tree
#   build-windows.sh clean           wipe the build dir first
#   build-windows.sh nodeploy        build only, skip the portable tree
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
for arg in "$@"; do
    case "$arg" in
        clean)     do_clean=1 ;;
        deploy)    do_deploy=1 ;;
        nodeploy)  do_deploy=0 ;;
        # Configure answers the only question a deletion asks - is every file the
        # build graph names still there - and answers it in seconds instead of in
        # a full compile. It implies nodeploy: there is no exe to deploy.
        configure) do_build=0; do_deploy=0 ;;
        *) echo "usage: $(basename "$0") [clean] [nodeploy|configure]" >&2; exit 2 ;;
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

cmake --build "$BUILD_DIR" --target chiaki

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
