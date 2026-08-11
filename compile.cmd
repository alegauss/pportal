@echo off
rem =====================================================================
rem compile.cmd - build chiaki-ng (Windows-only) through MSYS2 MinGW64
rem
rem Thin launcher around scripts\build-windows.sh, which holds the actual steps.
rem By default it produces a *runnable* .\build\chiaki-ng-Win\chiaki.exe, with
rem the MinGW runtime, Qt6 and FFmpeg DLLs next to it. The binary under
rem .\build\gui\ links against C:\msys64\mingw64\bin and only starts inside an
rem MSYS2 MinGW64 shell - double-clicking that one fails on missing DLLs.
rem
rem Usage:
rem   compile.cmd                 configure + build + portable tree
rem   compile.cmd clean           wipe .\build (portable tree included) first
rem   compile.cmd nodeploy        build only, skip the portable tree (fast)
rem   compile.cmd clean nodeploy  both
rem
rem Environment overrides:
rem   MSYS2_ROOT   MSYS2 install dir              (default C:\msys64)
rem   BUILD_TYPE   CMake build type               (default Release)
rem   BUILD_DIR    build directory, repo-relative (default build)
rem   DEPLOY_DIR   portable output, repo-relative (default build/chiaki-ng-Win)
rem =====================================================================
setlocal EnableExtensions

if not defined MSYS2_ROOT set "MSYS2_ROOT=C:\msys64"
if not defined BUILD_DIR set "BUILD_DIR=build"
if not defined DEPLOY_DIR set "DEPLOY_DIR=%BUILD_DIR%/chiaki-ng-Win"
if not defined BUILD_TYPE set "BUILD_TYPE=Release"
set "DEPLOY_DISP=%DEPLOY_DIR:/=\%"

set "BASH=%MSYS2_ROOT%\usr\bin\bash.exe"
if not exist "%BASH%" (
    echo [compile] MSYS2 not found at "%MSYS2_ROOT%".
    echo [compile] Install it with:  winget install --id MSYS2.MSYS2 -e
    echo [compile] or set MSYS2_ROOT to your existing installation.
    exit /b 1
)

rem ---- flags (validated by the shell script; parsed here only for messages)
set "ARGS=%*"
set "DO_DEPLOY=1"
set "LOCKED="
echo %ARGS% | find /I "nodeploy" >nul && set "DO_DEPLOY="

rem ---- a running instance locks the portable tree ------------------------
rem Otherwise the deploy step dies on "cp: Device or resource busy". Uses goto
rem instead of a parenthesised block: cmd expands variables inside a block at
rem parse time, so clearing DO_DEPLOY in there would not stick.
if not defined DO_DEPLOY goto lock_checked
tasklist /FI "IMAGENAME eq chiaki.exe" /NH 2>nul | find /I "chiaki.exe" >nul
if errorlevel 1 goto lock_checked
echo [compile] WARNING: chiaki.exe is running, and it locks the files in
echo [compile]          %DEPLOY_DISP%. Building only - close chiaki-ng and
echo [compile]          run again to refresh the portable tree.
echo.
set "ARGS=%ARGS% nodeploy"
set "DO_DEPLOY="
set "LOCKED=1"
:lock_checked

rem ---- repo root as a bash-friendly path (forward slashes, no trailing /)
set "REPO=%~dp0"
set "REPO=%REPO:\=/%"
if "%REPO:~-1%"=="/" set "REPO=%REPO:~0,-1%"

set "MSYSTEM=MINGW64"
set "CHERE_INVOKING=1"

echo [compile] MSYS2      : %MSYS2_ROOT%
echo [compile] build dir  : %BUILD_DIR%  (%BUILD_TYPE%)
if defined DO_DEPLOY echo [compile] portable   : %DEPLOY_DISP%
if not defined DO_DEPLOY echo [compile] portable   : skipped
echo.

"%BASH%" -l "%REPO%/scripts/build-windows.sh" %ARGS%
if errorlevel 1 (
    echo.
    echo [compile] FAILED
    exit /b 1
)

echo.
if defined DO_DEPLOY goto ok_deploy
echo [compile] OK -^> %~dp0%BUILD_DIR%\gui\chiaki.exe
echo [compile] NOTE: this binary only starts inside an MSYS2 MinGW64 shell.
if defined LOCKED echo [compile]       %DEPLOY_DISP%\chiaki.exe still holds the previous build.
if not defined LOCKED echo [compile]       Run compile.cmd without 'nodeploy' for a clickable build.
exit /b 0

:ok_deploy
echo [compile] OK - run this one:
echo [compile]   %~dp0%DEPLOY_DISP%\chiaki.exe
echo.
echo [compile] ^(%~dp0%BUILD_DIR%\gui\chiaki.exe also exists, but it needs the
echo [compile]  MSYS2 MinGW64 shell - double-clicking it fails on missing DLLs.^)
exit /b 0
