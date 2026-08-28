#!/usr/bin/env bash
#
# PP492: copy the transitive dependency closure of one or more binaries into a directory.
#
# usage: deploy-native-deps.sh <output-dir> <binary>...
#
# This is the walk deploy-windows-msys2.sh has always done, lifted out because the OTHER deploy
# path needs it and did not have it. PP269 gave the GUI-off build a native-only deploy - copy
# chiaki-shim.dll and chiaki-render.dll into the portable tree, because the .NET resolver looks
# there before it looks at the build directory - and copied only those two. On an incremental
# build that is enough, since the closure is already in the tree from whenever the client was last
# built with the GUI on; on a clean build the two land beside nothing and the host cannot load
# either of them.
#
# What that failure looks like is the reason this is worth a script rather than a second loop:
# ChiakiNative.Resolve tests File.Exists, finds the shim, and TryLoad fails on a missing import,
# so the error says the DLL "was not found" and lists the path it is sitting at.
#
# PATH is the caller's. The Qt path puts the cpp-steam-tools directory on it first so that ldd can
# resolve libcpp-steam-tools.dll, and this script does not second-guess that: a binary whose
# dependencies ldd cannot resolve is reported, not silently skipped.

set -euo pipefail

if [[ $# -lt 2 ]]; then
    echo "usage: $0 <output-dir> <binary>..." >&2
    exit 1
fi

output_dir="$1"
shift

mkdir -p "$output_dir"

ldd_timeout="${LDD_TIMEOUT:-10}"
ldd_timeout_cmd=()
if command -v timeout >/dev/null; then
    ldd_timeout_cmd=(timeout --kill-after=5s "${ldd_timeout}s")
else
    echo "warning: timeout(1) not found, ldd might hang" >&2
fi

declare -A queued_paths=()
declare -A scanned_paths=()

queue=()
for binary in "$@"; do
    if [[ ! -f "$binary" ]]; then
        echo "warning: $binary does not exist, so nothing it imports is collected" >&2
        continue
    fi
    queue+=("$binary")
    queued_paths["$binary"]=1
done

extract_dependencies() {
    local binary="$1"

    local ldd_output
    local ldd_status=0
    if [[ ${#ldd_timeout_cmd[@]} -gt 0 ]]; then
        set +e
        ldd_output="$(LC_ALL=C "${ldd_timeout_cmd[@]}" ldd "$binary" 2>&1)"
        ldd_status=$?
        set -e
        if [[ $ldd_status -eq 124 ]]; then
            echo "ldd timed out for $binary" >&2
        elif [[ $ldd_status -ne 0 ]]; then
            echo "ldd exited with status $ldd_status for $binary" >&2
        fi
    else
        ldd_output="$(LC_ALL=C ldd "$binary" 2>&1)" || true
    fi

    # A Windows system DLL is supplied by the machine and must not be bundled; everything else with
    # an absolute path is ours to carry.
    printf '%s\n' "$ldd_output" | awk '
        /=>/ && $(NF-1) ~ /^\// { print $(NF-1) }
        /^\// { print $1 }
    ' | grep -iv "system32" | grep -iv "windows" || true
}

enqueue_dependency() {
    local dependency="$1"
    local file_name

    [[ -n "$dependency" ]] || return 0

    file_name="${dependency##*/}"
    if [[ ! -e "$output_dir/$file_name" ]]; then
        echo "Copied $dependency"
        cp "$dependency" "$output_dir/"
    fi

    if [[ -z "${queued_paths["$dependency"]+x}" ]]; then
        queue+=("$dependency")
        queued_paths["$dependency"]=1
    fi
}

while [[ ${#queue[@]} -gt 0 ]]; do
    current="${queue[0]}"
    queue=("${queue[@]:1}")

    if [[ -n "${scanned_paths["$current"]+x}" ]]; then
        continue
    fi
    scanned_paths["$current"]=1

    while IFS= read -r dependency; do
        enqueue_dependency "$dependency"
    done < <(extract_dependencies "$current")
done
