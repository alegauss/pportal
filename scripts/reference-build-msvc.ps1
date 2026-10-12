<#
.SYNOPSIS
  PP63: build the Qt client WITH QtWebEngine, once, so PP46 has a "before" to measure.

.DESCRIPTION
  MSYS2 MinGW64 ships no qt6-webengine and no published Windows release carries Chromium
  (PP62), so the binary PP46 compares against has to be built - and Chromium on Windows
  needs MSVC or clang-cl. This script is that build and nothing else.

  It is NOT a second build system. compile.cmd stays the tree's only build and the only
  gate for a commit; nothing here is called from compile.cmd, test.cmd or package.cmd, and
  nothing in the gate calls this. Using it for ordinary work splits the build in two.

  What it builds is not the current tree. The Qt client was retired by PP632 and gui\ no
  longer compiles against lib\, so the source is a worktree of -Ref: by default e4e482e7,
  the commit tools\measure-startup\qt-build-webengine-absent.json was measured on. The two
  rows then differ in WebEngine and in toolchain, and in nothing else that this tree did.

  The recipe is upstream's own Windows VC workflow (deleted with the CI in 9f3c1cc8, last
  seen at 3036f3fb^:.github/workflows/build-windows.yml), with four departures, each
  because the upstream step no longer resolves:
    - vcpkg is pinned newer than upstream's baseline, which no longer installs (below).
    - Qt comes from -QtRoot. aqtinstall 3.3.0 cannot read Qt 6.11's per-arch repository
      layout; any msvc2022_64 Qt carrying WebEngineQuick will do.
    - shaderc and SPIRV-Cross come from the Vulkan SDK, which ships both, instead of a
      googleapis badge link and a source build.
    - libplacebo-pc.patch is not applied: upstream removed it in 82be58c7.

  Everything lands under build\reference\ - inside build\ because every check that walks the
  repository already skips that directory, and the worktree holds a full copy of gui\ and
  lib\ that SpeexBuildGate, for one, read as a second client when it sat beside build\.
  Each stage is skipped when its output exists, so a re-run resumes; -Clean deletes it, and
  so does compile.cmd clean (git worktree prune then drops the dangling worktree entry).

  Exit status is measure-startup's: 0 measured WITH WebEngine present, 2 measured without
  it (this script failed at its one job), 1 no cold-start figure. Anything else is a build
  stage failing, and the stage is named.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\reference-build-msvc.ps1
#>
[CmdletBinding()]
param(
    [string]$Ref = 'e4e482e7',
    [string]$QtRoot = 'D:\Qt\6.11.1\msvc2022_64',
    [string]$VulkanSdk = $env:VULKAN_SDK,
    [string]$VsRoot,
    [string]$CacheState = 'warm',
    [switch]$NoMeasure,
    [switch]$Clean
)
$ErrorActionPreference = 'Stop'

$Repo = Split-Path -Parent $PSScriptRoot
$Out = Join-Path $Repo 'build\reference'
$Src = Join-Path $Out 'src'
$Vcpkg = Join-Path $Out 'vcpkg'
$VcpkgInstalled = Join-Path $Out 'vcpkg_installed'
$Triplet = 'x64-windows'
$VcpkgTree = Join-Path $VcpkgInstalled $Triplet
$Deps = Join-Path $Out 'deps'
$Build = Join-Path $Out 'build'
$Deploy = Join-Path $Out 'chiaki-ng-Win'

# libplacebo is upstream's pin. vcpkg is NOT: upstream's 42bb0d9e (2024-02) fetches an msys2
# pkgconf package that every mirror now answers 404 for, so a baseline that old cannot
# install at all. This is master on 2026-10-10.
$VcpkgBaseline = 'e456309491fd875487bf02b8a0dca76c1b1b00cc'
$LibplaceboTag = 'v7.349.0'
$FfmpegUrl = 'https://github.com/r52/FFmpeg-Builds/releases/download/latest/ffmpeg-n7.1-latest-win64-gpl-shared-7.1.zip'

function Stage([string]$name) { Write-Host "`n[reference] == $name" -ForegroundColor Cyan }
function Fail([string]$why) { Write-Host "[reference] FAILED: $why" -ForegroundColor Red; exit 3 }
# A native command whose failure is an ANSWER rather than an error. Its stderr is discarded
# inside cmd, because PowerShell 5.1 under 'Stop' turns any native stderr line into a
# terminating error - a second run died on git apply --check reporting the patch as applied.
function Probe([string]$commandLine) {
    cmd /c "$commandLine >nul 2>&1"
    return ($LASTEXITCODE -eq 0)
}
function Run([string]$what, [scriptblock]$cmd) {
    & $cmd
    if ($LASTEXITCODE -ne 0) { Fail "$what (exit $LASTEXITCODE)" }
}

if ($Clean -and (Test-Path $Out)) {
    Stage 'clean'
    # Not `git worktree remove`: git refuses to move or remove a worktree holding submodules,
    # so the directory goes and prune drops the entry that pointed at it.
    Remove-Item $Out -Recurse -Force
    git -C $Repo worktree prune
}
New-Item -ItemType Directory -Force $Out, $Deps | Out-Null

# ---- toolchain -----------------------------------------------------------------
Stage 'toolchain'
if (-not $VsRoot) {
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (-not (Test-Path $vswhere)) { Fail 'no Visual Studio installer (vswhere.exe)' }
    # 17.x first: Qt's msvc2022_64 binaries are built with it.
    $VsRoot = & $vswhere -version '[17.0,18.0)' -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath | Select-Object -First 1
    if (-not $VsRoot) { $VsRoot = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath }
}
$vcvars = Join-Path $VsRoot 'VC\Auxiliary\Build\vcvars64.bat'
if (-not (Test-Path $vcvars)) { Fail "no vcvars64.bat under $VsRoot - the C++ workload is missing" }
# Import the developer environment into this process.
cmd /c "`"$vcvars`" >nul && set" | ForEach-Object {
    if ($_ -match '^([^=]+)=(.*)$') { [Environment]::SetEnvironmentVariable($Matches[1], $Matches[2], 'Process') }
}
$llvm = Join-Path $VsRoot 'VC\Tools\Llvm\x64\bin'
if (-not (Test-Path "$llvm\clang-cl.exe")) { Fail "no clang-cl under $llvm - add the 'C++ Clang tools' component" }
$env:PATH = "$llvm;$env:PATH"
$env:CC = 'clang-cl.exe'; $env:CXX = 'clang-cl.exe'

if (-not $VulkanSdk) { $VulkanSdk = [Environment]::GetEnvironmentVariable('VULKAN_SDK', 'Machine') }
if (-not $VulkanSdk -or -not (Test-Path "$VulkanSdk\Lib\shaderc_shared.lib")) { Fail 'no Vulkan SDK carrying shaderc_shared (winget install KhronosGroup.VulkanSDK)' }
$env:VULKAN_SDK = $VulkanSdk
if (-not (Test-Path "$QtRoot\lib\cmake\Qt6WebEngineQuick")) { Fail "$QtRoot carries no Qt6WebEngineQuick - this build exists only to have one" }
foreach ($tool in 'ninja', 'meson', 'python') {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) { Fail "$tool is not on PATH (python -m pip install --user meson ninja)" }
}
# nanopb's generator imports google.protobuf, and with no protoc on PATH it compiles its own
# nanopb.proto through grpc_tools - the second is the one that was missing here.
if (-not (Probe 'python -c "import google.protobuf, grpc_tools"')) { Run 'pip install protobuf' { python -m pip install --user --quiet protobuf grpcio-tools } }
Write-Host "[reference] VS      $VsRoot"
Write-Host "[reference] Qt      $QtRoot"
Write-Host "[reference] Vulkan  $VulkanSdk"

# ---- source --------------------------------------------------------------------
Stage "source: worktree of $Ref"
if (-not (Test-Path "$Src\CMakeLists.txt")) {
    Run 'git worktree add' { git -C $Repo worktree add --detach $Src $Ref }
    Run 'submodules' { git -C $Src submodule update --init --recursive --depth 1 }
}
# gf-complete's cpuid needs intrin.h under MSVC; upstream applied this in CI.
$gf = Join-Path $Src 'third-party\gf-complete'
if (Probe "git -C `"$gf`" apply --check --ignore-whitespace `"$Src\scripts\windows-vc\gf-complete.patch`"") { Run 'gf-complete patch' { git -C $gf apply --ignore-whitespace "$Src\scripts\windows-vc\gf-complete.patch" } }

# ---- vcpkg ---------------------------------------------------------------------
Stage "vcpkg @ $($VcpkgBaseline.Substring(0, 8))"
if (-not (Test-Path "$Vcpkg\vcpkg.exe")) {
    if (-not (Test-Path "$Vcpkg\.git")) { Run 'clone vcpkg' { git clone --quiet https://github.com/microsoft/vcpkg $Vcpkg } }
    Run 'checkout baseline' { git -C $Vcpkg checkout --quiet $VcpkgBaseline }
    Run 'bootstrap vcpkg' { & "$Vcpkg\bootstrap-vcpkg.bat" -disableMetrics }
}
if (-not (Test-Path "$VcpkgTree\lib\pkgconfig\opus.pc")) {
    Run 'vcpkg install' { & "$Vcpkg\vcpkg.exe" install --triplet $Triplet "--x-manifest-root=$Src" "--x-install-root=$VcpkgInstalled" --disable-metrics }
}

# ---- ffmpeg --------------------------------------------------------------------
Stage 'ffmpeg (prebuilt, n7.1 shared)'
if (-not (Test-Path "$Deps\lib\avcodec.lib")) {
    $zip = Join-Path $Out 'ffmpeg.zip'
    if (-not (Test-Path $zip)) { $ProgressPreference = 'SilentlyContinue'; Invoke-WebRequest -UseBasicParsing $FfmpegUrl -OutFile $zip }
    $tmp = Join-Path $Out 'ffmpeg-unpack'
    if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }
    Expand-Archive -LiteralPath $zip -DestinationPath $tmp
    $top = Get-ChildItem $tmp -Directory | Select-Object -First 1
    Copy-Item "$($top.FullName)\*" $Deps -Recurse -Force
    Remove-Item $tmp -Recurse -Force
}

# ---- libplacebo ----------------------------------------------------------------
Stage "libplacebo $LibplaceboTag (meson)"
$placeboSrc = Join-Path $Out 'libplacebo'
if (-not (Test-Path "$Deps\lib\pkgconfig\libplacebo.pc")) {
    if (-not (Test-Path "$placeboSrc\meson.build")) {
        Run 'clone libplacebo' { git clone --quiet --recursive --branch $LibplaceboTag --depth 1 --shallow-submodules https://github.com/haasn/libplacebo.git $placeboSrc }
    }
    # v7.349.0's generator hands ET.parse()'s ElementTree to an ElementTree constructor,
    # which Python 3.13 refuses ("expected an Element, not ElementTree"). Its root is what
    # the constructor wants; the clone is this script's own, so the edit is made in place.
    $gen = Join-Path $placeboSrc 'src\vulkan\utils_gen.py'
    $genText = [IO.File]::ReadAllText($gen)
    if ($genText.Contains('VkXML(ET.parse(xmlfile))')) {
        [IO.File]::WriteAllText($gen, $genText.Replace('VkXML(ET.parse(xmlfile))', 'VkXML(ET.parse(xmlfile).getroot())'))
    }
    # The Vulkan SDK ships shaderc_shared and spirv-cross-c-shared with no .pc files, and
    # libplacebo's meson asks pkg-config for both. The variable is NOT called prefix: pkgconf
    # on Windows redefines prefix to the .pc file's own location, which pointed the include
    # path at deps\Include.
    $sdkFwd = $VulkanSdk -replace '\\', '/'
    New-Item -ItemType Directory -Force "$Deps\lib\pkgconfig" | Out-Null
    foreach ($pc in @(@('shaderc', 'shaderc_shared', '2025.0', 'Include'),
                      @('spirv-cross-c-shared', 'spirv-cross-c-shared', '0.64.0', 'Include/spirv_cross'))) {
        @"
sdkroot=$sdkFwd

Name: $($pc[0])
Description: from the Vulkan SDK
Version: $($pc[2])
Libs: -L`${sdkroot}/Lib -l$($pc[1])
Cflags: -I`${sdkroot}/$($pc[3])
"@ | Set-Content -Encoding ascii "$Deps\lib\pkgconfig\$($pc[0]).pc"
    }
    $pkgconf = "$VcpkgTree\tools\pkgconf\pkgconf.exe"
    $native = Join-Path $Out 'meson-native.ini'
    @"
[binaries]
c = 'clang-cl'
cpp = 'clang-cl'
pkg-config = '$($pkgconf -replace '\\','/')'
"@ | Set-Content -Encoding ascii $native
    $sdk = $VulkanSdk -replace '\\', '/'
    $pcPath = "['$($VcpkgTree -replace '\\','/')/lib/pkgconfig','$($VcpkgTree -replace '\\','/')/share/pkgconfig','$($Deps -replace '\\','/')/lib/pkgconfig']"
    $cmPath = "['$($VcpkgTree -replace '\\','/')','$sdk','$($Deps -replace '\\','/')']"
    Push-Location $placeboSrc
    try {
        if (-not (Test-Path 'build\build.ninja')) {
            if (Test-Path 'build') { Remove-Item 'build' -Recurse -Force }  # a failed setup
            Run 'meson setup libplacebo' {
                meson setup --prefix $Deps --native-file $native --buildtype release `
                    "--pkg-config-path=$pcPath" "--cmake-prefix-path=$cmPath" `
                    "-Dc_args=/I$sdk/Include" "-Dcpp_args=/I$sdk/Include" `
                    "-Dc_link_args=/LIBPATH:$sdk/Lib" "-Dcpp_link_args=/LIBPATH:$sdk/Lib" `
                    -Ddemos=false -Dtests=false -Dvulkan=enabled -Dshaderc=enabled -Dd3d11=enabled `
                    build
            }
        }
        Run 'build libplacebo' { ninja -C build }
        Run 'install libplacebo' { ninja -C build install }
    } finally { Pop-Location }
}

# ---- chiaki --------------------------------------------------------------------
Stage 'configure chiaki (gui target only)'
$prefix = "$Deps;$QtRoot;$VulkanSdk"
if (-not (Test-Path "$Build\build.ninja")) {
    Run 'cmake configure' {
        cmake -S $Src -B $Build -G Ninja `
            "-DCMAKE_TOOLCHAIN_FILE=$Vcpkg\scripts\buildsystems\vcpkg.cmake" `
            "-DVCPKG_INSTALLED_DIR=$VcpkgInstalled" "-DVCPKG_TARGET_TRIPLET=$Triplet" `
            -DVCPKG_MANIFEST_INSTALL=OFF `
            -DCMAKE_BUILD_TYPE=Release `
            -DCHIAKI_ENABLE_TESTS=OFF `
            -DCHIAKI_GUI_ENABLE_SDL_GAMECONTROLLER=ON `
            "-DPYTHON_EXECUTABLE=$((Get-Command python).Source)" `
            "-DCMAKE_PREFIX_PATH=$prefix"
    }
}
$cache = Get-Content "$Build\CMakeCache.txt" -Raw
if ($cache -notmatch 'Qt6WebEngineQuick_DIR:PATH=(?!.*NOTFOUND)') { Fail 'configure did not find Qt6WebEngineQuick, so CHIAKI_HAVE_WEBENGINE would be off' }

Stage 'build chiaki'
Run 'cmake build' { cmake --build $Build --target chiaki }

# ---- deploy --------------------------------------------------------------------
Stage 'deploy portable tree'
if (Test-Path $Deploy) { Remove-Item $Deploy -Recurse -Force }
New-Item -ItemType Directory -Force $Deploy | Out-Null
Copy-Item "$Build\gui\chiaki.exe" $Deploy
Get-ChildItem $Build -Recurse -Filter 'cpp-steam-tools.dll' | Copy-Item -Destination $Deploy
foreach ($p in 'libcrypto-*-x64.dll', 'libssl-*-x64.dll', 'SDL2.dll', 'opus.dll', 'libspeexdsp.dll',
               'lcms2*.dll', 'miniupnpc.dll', 'json-c.dll', 'event*.dll', 'libprotobuf*.dll',
               'abseil_dll.dll') {
    Get-ChildItem "$VcpkgTree\bin" -Filter $p -ErrorAction SilentlyContinue | Copy-Item -Destination $Deploy
}
foreach ($p in 'swresample-*.dll', 'avcodec-*.dll', 'avutil-*.dll', 'avformat-*.dll', 'swscale-*.dll', 'libplacebo-*.dll') {
    Get-ChildItem "$Deps\bin" -Filter $p -ErrorAction SilentlyContinue | Copy-Item -Destination $Deploy
}
Copy-Item "$VulkanSdk\Bin\shaderc_shared.dll", "$VulkanSdk\Bin\spirv-cross-c-shared.dll" $Deploy
# The client imports QtWebEngine only inside a Qt.createQmlObject string (PSNLoginDialog,
# PSNTokenDialog), which windeployqt's QML scan cannot see, so it would deploy no WebEngine
# at all. Upstream copied a one-line import file in for that; this commit has none, so it
# is written here, into the worktree's qml dir and nowhere else.
$qmlDir = Join-Path $Src 'gui\src\qml'
"import QtQuick`nimport QtWebEngine`nItem {}`n" | Set-Content -Encoding ascii (Join-Path $qmlDir 'qtwebengine_import.qml')
Run 'windeployqt' { & "$QtRoot\bin\windeployqt.exe" --no-translations --release "--qmldir=$qmlDir" "$Deploy\chiaki.exe" }

# ---- measure (the assertion) ---------------------------------------------------
if ($NoMeasure) { Write-Host "`n[reference] built: $Deploy\chiaki.exe (not measured)"; exit 0 }
Stage 'measure-startup'
$report = Join-Path $Out 'qt-build-webengine-present.json'
dotnet run --project "$Repo\tools\measure-startup\MeasureStartup.csproj" -c Release -- `
    --exe "$Deploy\chiaki.exe" --tree $Deploy --cache-state $CacheState --out $report
$code = $LASTEXITCODE
switch ($code) {
    0 { Write-Host "[reference] OK - WebEngine present, report at $report" -ForegroundColor Green }
    2 { Write-Host '[reference] measured, but NO Chromium in the tree - this is not the before' -ForegroundColor Red }
    default { Write-Host "[reference] measure-startup exit $code" -ForegroundColor Red }
}
exit $code
