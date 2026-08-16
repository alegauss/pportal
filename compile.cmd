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
rem WHY THIS FILE MATTERS RIGHT NOW
rem This is the gate for the stripping work. The tree is being reduced to a
rem Windows-only, NVIDIA-first project that builds in Visual Studio, and every
rem deletion on the way there is a guess until something compiles. This script is
rem that something: it is the only build the repository still has, and it stays
rem until the .NET solution can take over.
rem
rem So the loop for a removal is:  delete  ->  compile.cmd configure  ->  keep or
rem revert. Configure is seconds and answers the only question a deletion asks -
rem does every path the build graph names still resolve. Run a full build before
rem committing, not before deciding.
rem
rem Usage:
rem   compile.cmd                 configure + build (client + tests + .NET host) + portable tree
rem   compile.cmd configure       configure only - the fast check after a deletion
rem   compile.cmd clean           wipe .\build (portable tree included) first
rem   compile.cmd nodeploy        build only, skip the portable tree (fast)
rem   compile.cmd notests         skip chiaki-unit - leaves ctest on a stale binary
rem   compile.cmd noapp           skip app\ - says nothing about whether the .NET host builds
rem   compile.cmd clean nodeploy  both
rem
rem A default build now links chiaki-unit as well as chiaki (PP56) and builds the .NET
rem host in app\ (PP74). It used to build the client alone, which meant ctest in .\build
rem ran whatever test binary had last been linked by hand - a green that reported on code
rem no longer in the tree - and, once PP1 landed, that a change to app\ was never
rem compiled by the one command run before committing.
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
rem Matched token by token rather than with `echo %ARGS% | find`, which is what
rem this did before and which fails open: on a PATH that reaches MSYS2 or Git for
rem Windows first, `find` is the Unix one, the pipeline errors, and the flag is
rem silently ignored - a `nodeploy` that deploys anyway. A for loop over the
rem arguments needs no external program and cannot be shadowed.
set "ARGS=%*"
set "DO_DEPLOY=1"
set "CONFIGURE_ONLY="
set "NO_TESTS="
set "BAD_ARG="
set "LOCKED="
for %%a in (%ARGS%) do (
    rem clean and deploy are the shell script's to act on; they are listed here
    rem only so that a typo is not mistaken for one of them.
    if /I "%%~a"=="configure" set "CONFIGURE_ONLY=1"
    if /I "%%~a"=="nodeploy"  set "DO_DEPLOY="
    if /I "%%~a"=="notests"   set "NO_TESTS=1"
    if /I "%%~a"=="noapp"     set "NO_APP=1"
    if /I "%%~a" neq "configure" if /I "%%~a" neq "nodeploy" if /I "%%~a" neq "notests" if /I "%%~a" neq "noapp" if /I "%%~a" neq "clean" if /I "%%~a" neq "deploy" set "BAD_ARG=%%~a"
)
rem The .NET host is not part of the cmake graph, so `configure` - which asks cmake whether
rem every path it names still resolves - has nothing to say about it either way.
if defined CONFIGURE_ONLY set "NO_APP=1"
if defined CONFIGURE_ONLY set "DO_DEPLOY="
if defined BAD_ARG (
    echo [compile] unknown argument: %BAD_ARG%
    echo [compile] usage: compile.cmd [clean] [notests] [noapp] [configure^|nodeploy]
    exit /b 2
)

rem ---- preflight: name the deletion, do not let cmake describe it ---------
rem Everything below is a file this build reads. A removal that takes one out
rem should be reported here, by name and in a second, rather than as a cmake
rem error in the middle of a configure or - worse for the two scripts - as a
rem deploy failure after a full compile has already been paid for.
rem
rem If a line here fails because the file was removed ON PURPOSE, this list is
rem what has to be updated in the same commit. That is the point of it: the gate
rem knows what it needs, so nobody has to remember.
set "MISSING="
call :need "scripts\build-windows.sh"      "the build steps; this file only launches them"
if defined DO_DEPLOY call :need "scripts\deploy-windows-msys2.sh" "collects the portable tree - without it a full build fails at the last step"
call :need "CMakeLists.txt"                "the build graph root"
call :need "gui\CMakeLists.txt"            "the Qt client"
call :need "lib\CMakeLists.txt"            "libchiaki"
if not defined NO_TESTS call :need "test\CMakeLists.txt" "chiaki-unit, which a default build links so ctest is not left on a stale binary"
if not defined NO_APP call :need "app\ChiakiNg.csproj" "the .NET host (PP1); compile.cmd noapp builds the Qt client alone"
if defined MISSING (
    echo.
    echo [compile] Cannot build: the file^(s^) above are read by this build.
    echo [compile] If a removal was deliberate, update the preflight list in
    echo [compile] compile.cmd in the same commit as the deletion.
    exit /b 1
)

rem Submodules CMake adds by default. Each has an opt-out, so a missing one is a
rem warning and not a refusal: it only fails the configure when the matching
rem option was not set with it.
call :warn_sub "third-party\nanopb"      "CHIAKI_USE_SYSTEM_NANOPB"
call :warn_sub "third-party\jerasure"    "CHIAKI_USE_SYSTEM_JERASURE"
call :warn_sub "third-party\gf-complete" "CHIAKI_USE_SYSTEM_JERASURE"
call :warn_sub "third-party\curl"        "CHIAKI_USE_SYSTEM_CURL"

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
if defined CONFIGURE_ONLY echo [compile] mode       : configure only (deletion check)
if not defined CONFIGURE_ONLY if defined NO_TESTS echo [compile] tests      : SKIPPED - ctest in %BUILD_DIR% will report on an older binary
if not defined CONFIGURE_ONLY if not defined NO_TESTS echo [compile] tests      : chiaki-unit built with the client
if not defined CONFIGURE_ONLY if defined NO_APP echo [compile] .NET host  : SKIPPED - nothing here says whether app\ still compiles
if not defined CONFIGURE_ONLY if not defined NO_APP echo [compile] .NET host  : app\ChiakiNg.csproj built after the Qt client
if defined DO_DEPLOY echo [compile] portable   : %DEPLOY_DISP%
if not defined DO_DEPLOY if not defined CONFIGURE_ONLY echo [compile] portable   : skipped
echo.

"%BASH%" -l "%REPO%/scripts/build-windows.sh" %ARGS%
if errorlevel 1 (
    echo.
    echo [compile] FAILED
    exit /b 1
)

rem ---- the .NET host (PP74) ----------------------------------------------
rem PP1 put a second executable in this tree and nothing built it, so a change under
rem app\ could be committed behind a full green - the same silence PP56 fixed for
rem chiaki-unit. This closes it, and it is deliberately NOT inside build-windows.sh:
rem dotnet is a Windows program that needs no MSYS2, and calling it through the MinGW
rem shell only adds a path translation that can go wrong.
rem
rem Debug and not Release, because Release turns SelfContained on for `dotnet build` as
rem well as for publish and lays the whole runtime down every time - the cost claude-tray
rem measured at 155.6 MB and 81 seconds on unchanged source. A gate has to be cheap enough
rem that nobody is tempted to pass noapp. Packaging the single-file exe is PP22's job.
if defined NO_APP goto app_done
where dotnet >nul 2>&1
if errorlevel 1 (
    echo.
    echo [compile] note: no .NET SDK on PATH, so app\ was not built. The Qt client does
    echo [compile]       not need one - install it only when working on the .NET host.
    goto app_done
)
echo.
echo [compile] building app\ChiakiNg.csproj ...
dotnet build "%~dp0app\ChiakiNg.csproj" -c Debug --nologo -v quiet
if errorlevel 1 (
    echo.
    echo [compile] FAILED - the Qt client built, the .NET host did not.
    exit /b 1
)
:app_done

echo.
if defined CONFIGURE_ONLY goto ok_configure
if defined DO_DEPLOY goto ok_deploy
echo [compile] OK -^> %~dp0%BUILD_DIR%\gui\chiaki.exe
echo [compile] NOTE: this binary only starts inside an MSYS2 MinGW64 shell.
if defined LOCKED echo [compile]       %DEPLOY_DISP%\chiaki.exe still holds the previous build.
if not defined LOCKED echo [compile]       Run compile.cmd without 'nodeploy' for a clickable build.
exit /b 0

:ok_configure
echo [compile] CONFIGURE OK - the build graph resolves after this deletion.
echo [compile] Run compile.cmd without 'configure' before committing: a path
echo [compile] that resolves is not yet a target that links.
exit /b 0

:ok_deploy
echo [compile] OK - run this one:
echo [compile]   %~dp0%DEPLOY_DISP%\chiaki.exe
echo.
echo [compile] ^(%~dp0%BUILD_DIR%\gui\chiaki.exe also exists, but it needs the
echo [compile]  MSYS2 MinGW64 shell - double-clicking it fails on missing DLLs.^)
exit /b 0

rem ---------------------------------------------------------------------------
rem  Preflight helpers. :need is a refusal, :warn_sub is a note - the difference
rem  is whether the build can proceed without the path, not how important it is.
:need
if exist "%~dp0%~1" exit /b 0
echo [compile] MISSING  %~1
echo [compile]          %~2
set "MISSING=1"
exit /b 0

:warn_sub
if exist "%~dp0%~1" exit /b 0
echo [compile] note: %~1 is gone - configure will fail unless %~2=ON
exit /b 0
