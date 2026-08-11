@echo off
rem =====================================================================
rem compile.cmd - configure and build chiaki-ng (Windows-only) via MSYS2
rem
rem Usage:
rem   compile.cmd                 configure + build into .\build
rem   compile.cmd clean           wipe .\build first, then build
rem   compile.cmd deploy          build, then collect a portable tree
rem                               into .\chiaki-ng-Win (runs outside MSYS2)
rem   compile.cmd clean deploy    both
rem
rem Environment overrides:
rem   MSYS2_ROOT   MSYS2 install dir            (default C:\msys64)
rem   BUILD_TYPE   CMake build type             (default Release)
rem   BUILD_DIR    build directory, repo-relative (default build)
rem =====================================================================
setlocal EnableExtensions

if not defined MSYS2_ROOT set "MSYS2_ROOT=C:\msys64"
if not defined BUILD_TYPE set "BUILD_TYPE=Release"
if not defined BUILD_DIR set "BUILD_DIR=build"

set "BASH=%MSYS2_ROOT%\usr\bin\bash.exe"
if not exist "%BASH%" (
    echo [compile] MSYS2 not found at "%MSYS2_ROOT%".
    echo [compile] Install it with:  winget install --id MSYS2.MSYS2 -e
    echo [compile] or set MSYS2_ROOT to your existing installation.
    exit /b 1
)

rem parse flags
set "DO_CLEAN="
set "DO_DEPLOY="
:parse
if "%~1"=="" goto parsed
if /i "%~1"=="clean"  set "DO_CLEAN=1"  & shift & goto parse
if /i "%~1"=="deploy" set "DO_DEPLOY=1" & shift & goto parse
echo [compile] unknown argument: %~1
echo [compile] usage: compile.cmd [clean] [deploy]
exit /b 2
:parsed

rem repo root as a bash-friendly path (forward slashes, no trailing slash)
set "REPO=%~dp0"
set "REPO=%REPO:\=/%"
if "%REPO:~-1%"=="/" set "REPO=%REPO:~0,-1%"

set "MSYSTEM=MINGW64"
set "CHERE_INVOKING=1"

rem Only single quotes inside the -lc string: cmd would otherwise swallow
rem nested double quotes.
set "STEPS=set -e; cd '%REPO%'"
if defined DO_CLEAN set "STEPS=%STEPS%; rm -rf '%BUILD_DIR%'"
set "STEPS=%STEPS%; cmake -S . -B '%BUILD_DIR%' -G Ninja -DCMAKE_BUILD_TYPE=%BUILD_TYPE%"
set "STEPS=%STEPS%; cmake --build '%BUILD_DIR%' --target chiaki"
if defined DO_DEPLOY set "STEPS=%STEPS%; rm -rf chiaki-ng-Win; ./scripts/deploy-windows-msys2.sh chiaki-ng-Win '%BUILD_DIR%/gui/chiaki.exe' \"$PWD/%BUILD_DIR%/third-party/cpp-steam-tools\" /mingw64 gui/src/qml"

echo [compile] MSYS2      : %MSYS2_ROOT%
echo [compile] build dir  : %BUILD_DIR%  (%BUILD_TYPE%)
if defined DO_CLEAN  echo [compile] clean      : yes
if defined DO_DEPLOY echo [compile] deploy     : chiaki-ng-Win
echo.

"%BASH%" -lc "%STEPS%"
if errorlevel 1 (
    echo.
    echo [compile] FAILED
    exit /b 1
)

echo.
echo [compile] OK -^> %~dp0%BUILD_DIR%\gui\chiaki.exe
if defined DO_DEPLOY echo [compile] portable -^> %~dp0chiaki-ng-Win\chiaki.exe
exit /b 0
