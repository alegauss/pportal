@echo off
rem =====================================================================
rem run.cmd - PP48: build, generate the stream and measure every decode path
rem
rem Thin launcher around the two shell scripts beside it, for the same reason
rem compile.cmd is one: the toolchain that has to answer is MSYS2 MinGW64, because
rem that is the ffmpeg the client links and therefore the only one whose cuda,
rem d3d11va and vulkan support says anything about this application.
rem
rem Usage:
rem   run.cmd                 build if needed, generate the stream if absent, measure
rem   run.cmd rebuild         rebuild the harness first
rem   run.cmd restream        regenerate stream.h264 first
rem
rem Environment overrides:
rem   MSYS2_ROOT   MSYS2 install dir   (default C:\msys64)
rem =====================================================================
setlocal EnableExtensions

if not defined MSYS2_ROOT set "MSYS2_ROOT=C:\msys64"
set "BASH=%MSYS2_ROOT%\usr\bin\bash.exe"
if not exist "%BASH%" (
    echo [decode-path] MSYS2 not found at "%MSYS2_ROOT%".
    echo [decode-path] Install it with:  winget install --id MSYS2.MSYS2 -e
    exit /b 1
)

set "HERE=%~dp0"
set "HERE=%HERE:\=/%"
if "%HERE:~-1%"=="/" set "HERE=%HERE:~0,-1%"

set "REBUILD="
set "RESTREAM="
for %%a in (%*) do (
    if /I "%%~a"=="rebuild"  set "REBUILD=1"
    if /I "%%~a"=="restream" set "RESTREAM=1"
)

if defined RESTREAM del /q "%~dp0stream.h264" 2>nul
if defined REBUILD del /q "%~dp0decode-path.exe" 2>nul

set "MSYSTEM=MINGW64"
set "CHERE_INVOKING=1"

if not exist "%~dp0decode-path.exe" (
    "%BASH%" -l "%HERE%/build.sh" || exit /b 1
)
if not exist "%~dp0stream.h264" (
    "%BASH%" -l "%HERE%/make-stream.sh" || exit /b 1
)

rem Run it through the same shell that built it. decode-path.exe links MinGW64's ffmpeg DLLs
rem and cannot find them from a plain cmd PATH - it exits before main with no message, which
rem reads exactly like a harness that ran and measured nothing. compile.cmd carries the same
rem note about build\gui\chiaki.exe, for the same reason.
"%BASH%" -l -c "cd '%HERE%' && ./decode-path.exe stream.h264 result.json"
exit /b %errorlevel%
