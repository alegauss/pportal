
# Build instructions

chiaki-ng is Windows-only. CMake refuses to configure on any other platform.

## Windows (MSYS2 / MinGW64)

Official builds run in MSYS2 with the `mingw64` environment. The CI workflows are the
authoritative reference and work as a template for building locally:

* [.github/workflows/build-msys2.yml](../.github/workflows/build-msys2.yml) - x64
* [.github/workflows/build-msys2-arm.yml](../.github/workflows/build-msys2-arm.yml) - arm64

In short:

```
git submodule update --init --recursive
scripts/build-libplacebo-windows.sh
cmake -S . -B build -G Ninja -DCMAKE_BUILD_TYPE=Release
cmake --build build --config Release --target chiaki
./scripts/deploy-windows-msys2.sh chiaki-ng-Win build/gui/chiaki.exe     "$PWD/build/third-party/cpp-steam-tools" /mingw64 gui/src/qml
```

## Windows (MSVC / vcpkg)

An MSVC build via vcpkg is described by [meson.ini](../meson.ini) and
[vcpkg.json](../vcpkg.json), driven by [scripts/appveyor-win.sh](../scripts/appveyor-win.sh).
