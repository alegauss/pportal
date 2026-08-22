#!/usr/bin/env bash

# PP22: the native half of the installer's payload.
#
# The .NET host is a single self-contained file, so `dotnet publish` answers for every managed
# dependency it has. What publish says nothing about is the three native libraries the host
# resolves BESIDE ITSELF - chiaki-shim.dll, SDL2.dll and chiaki-render.dll - and the DLLs those
# three import in turn. On a developer's machine the resolver walks up into build\chiaki-ng-Win
# and finds them, which is why nothing has ever noticed: the one machine where that walk finds
# nothing is the machine an installer put the host on.
#
# Why not just copy the portable tree
# -----------------------------------
# build/chiaki-ng-Win is windeployqt's output. It carries 34 Qt DLLs and a chiaki.exe, all for the
# Qt client that PP21 turned off - so most of it is a build configuration this port does not ship,
# and some of it is older than the tree it sits in. Shipping that as "the payload" is shipping
# whatever was last deployed rather than what the host loads. This walks instead: three roots, ldd
# for the imports, and the transitive closure is 28 DLLs where the tree holds 83.
#
# The roots below are the values of ChiakiNative.NativeLibraries, which is the table the resolver
# itself dispatches on. They are spelled twice because a shell script cannot read C#, and the
# selftest holds the two spellings equal - a rename on either side is a red assertion rather than
# an installer that lays down a host which cannot start.

set -euo pipefail

if [[ $# -ne 2 ]]; then
    echo "usage: $0 <native-tree> <out-dir>" >&2
    exit 1
fi

native_tree="$1"
out_dir="$2"

payload_libraries=(chiaki-shim.dll SDL2.dll chiaki-render.dll)

# A missing root is a refusal and not a warning. deploy-windows-msys2.sh warns about the same two
# files because the portable tree is still usable without them - the Qt client does not call them.
# Here they ARE the deliverable: a payload assembled without one of these is an installer that
# fails on first launch, which is the least useful moment to find out.
missing=()
for dll in "${payload_libraries[@]}"; do
    [[ -f "$native_tree/$dll" ]] || missing+=("$dll")
done
if [[ ${#missing[@]} -gt 0 ]]; then
    echo "[package] MISSING from $native_tree: ${missing[*]}" >&2
    echo "[package]          Run compile.cmd, which builds them and refreshes the portable tree." >&2
    exit 1
fi

mkdir -p "$out_dir"

# Same guard deploy-windows-msys2.sh uses, for the same reason: ldd on a Windows DLL can hang, and
# a packaging step that hangs with no output is worse than one that reports a timeout and fails.
ldd_timeout="${LDD_TIMEOUT:-10}"
ldd_timeout_cmd=()
if command -v timeout >/dev/null; then
    ldd_timeout_cmd=(timeout --kill-after=5s "${ldd_timeout}s")
else
    echo "[package] warning: timeout(1) not found, ldd might hang" >&2
fi

# system32 and the Windows directory are excluded because those DLLs are the operating system's.
# Bundling one is at best redundant and at worst a version the machine did not choose.
imports_of() {
    local binary="$1"
    local output status=0

    if [[ ${#ldd_timeout_cmd[@]} -gt 0 ]]; then
        set +e
        output="$(LC_ALL=C "${ldd_timeout_cmd[@]}" ldd "$binary" 2>&1)"
        status=$?
        set -e
        if [[ $status -eq 124 ]]; then
            echo "[package] ldd timed out for $binary" >&2
            return 1
        fi
    else
        output="$(LC_ALL=C ldd "$binary" 2>&1)" || true
    fi

    printf '%s\n' "$output" \
        | awk '/=>/ && $(NF-1) ~ /^\// { print $(NF-1) }' \
        | grep -iv -e 'system32' -e '/c/windows' || true
}

declare -A seen=()
queue=()
for dll in "${payload_libraries[@]}"; do
    queue+=("$native_tree/$dll")
done

copied=0
while [[ ${#queue[@]} -gt 0 ]]; do
    current="${queue[0]}"
    queue=("${queue[@]:1}")

    name="${current##*/}"
    [[ -n "${seen["$name"]+x}" ]] && continue
    seen["$name"]=1

    cp "$current" "$out_dir/"
    copied=$((copied + 1))

    while IFS= read -r dependency; do
        [[ -n "$dependency" ]] || continue
        [[ -n "${seen["${dependency##*/}"]+x}" ]] && continue
        queue+=("$dependency")
    done < <(imports_of "$current")
done

echo "[package] $copied native libraries staged into $out_dir"
