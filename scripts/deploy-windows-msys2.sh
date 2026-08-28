#!/usr/bin/env bash

set -euo pipefail

if [[ $# -ne 5 ]]; then
    echo "usage: $0 <output-dir> <exe-path> <tool-dir> <msys-prefix> <qml-dir>" >&2
    exit 1
fi

output_dir="$1"
exe_path="$2"
tool_dir="$3"
msys_prefix="$4"
qml_dir="$5"

mkdir -p "$output_dir"
cp "$exe_path" "$output_dir/"

# PP4: the managed seam, put where its dependencies already are.
#
# chiaki-shim.dll statically links chiaki-lib, and chiaki-lib is not free-standing: common.c
# reaches OpenSSL through random.h and winsock through winsock2.h, so the DLL imports
# libcrypto-3-x64.dll like the client does. This tree is the one place that already holds it,
# which is why the .NET host loads the shim from here rather than from beside its own
# assembly. Copied and not ldd-walked: it is built by this repository, so its path is known
# rather than discovered.
shim_dll="$(dirname "$exe_path")/../shim/chiaki-shim.dll"
if [[ -f "$shim_dll" ]]; then
    cp "$shim_dll" "$output_dir/"
else
    echo "warning: chiaki-shim.dll not found at $shim_dll - the .NET host has nothing to call" >&2
fi

# PP9: the renderer's DLL, for the same reason and one that bit before it was written down.
#
# chiaki-render.dll links libplacebo, whose own DLLs live in this tree and nowhere the managed
# resolver looks. Left in build/shim it loads only when something ELSE has already put this
# directory on the process search path - which chiaki-shim does - so the render tests passed in a
# full run and failed every time they were run alone. Deploying it here is what makes its
# dependencies resolvable on its own terms rather than by accident of ordering.
render_dll="$(dirname "$exe_path")/../shim/chiaki-render.dll"
if [[ -f "$render_dll" ]]; then
    cp "$render_dll" "$output_dir/"
else
    echo "warning: chiaki-render.dll not found at $render_dll - the renderer probe cannot load" >&2
fi

export PATH="${tool_dir}:${msys_prefix}/share/qt6/bin:${PATH}"
export QT_PLUGIN_PATH="${msys_prefix}/share/qt6/plugins"
export QML2_IMPORT_PATH="${msys_prefix}/share/qt6/qml"

# PP492: the transitive walk moved into deploy-native-deps.sh, which is what the GUI-off path
# now calls too. It was here and only here, so a build with the client off got the two DLLs the
# host loads and none of what they import - fine on an incremental build, unloadable after a
# clean. The seed is the difference between the two callers and the only one: here it is the
# deployed exe, there it is the pair of libraries. PATH is exported above so that ldd inside the
# script can still resolve libcpp-steam-tools.dll.
"$(dirname "$0")/deploy-native-deps.sh" "$output_dir" "$output_dir/$(basename "$exe_path")"

# Ensure SDL3 is bundled even if ldd/Qt doesn't list it.
shopt -s nullglob
for dll_dir in "$msys_prefix/bin" "/clangarm64/bin" "$msys_prefix/mingw64/bin"; do
    for sdl3 in "$dll_dir"/SDL3*.dll; do
        if [[ -f "$sdl3" ]]; then
            echo "Staging SDL3 runtime $sdl3"
            cp "$sdl3" "$output_dir/"
        fi
    done
done
shopt -u nullglob

windeployqt6.exe --no-translations --qmldir="$qml_dir" "$output_dir/$(basename "$exe_path")"

# Remove system-provided DLLs that Windows already supplies and
# should not be bundled with the MSYS2 build (e.g. D3D compiler). Use
# case-insensitive globbing to catch variants like `D3Dcompiler_47`.
(
    shopt -s nocaseglob
    rm -f "$output_dir"/d3dcompiler*.dll
)
