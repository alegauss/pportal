@echo off
rem =====================================================================
rem test.cmd - run the unit suite and read the verdict
rem
rem Thin launcher around scripts\test-windows.sh, and it exists for the same
rem reason compile.cmd does: the command that answers lives in an MSYS2 MinGW64
rem shell. ctest is not on a plain Windows PATH, it is /mingw64/bin/ctest, and
rem the chiaki-unit.exe it starts needs the MinGW runtime beside it. Quoting that
rem through cmd every time is the papercut this pair removes (PP67).
rem
rem compile.cmd builds chiaki-unit by default since PP56, so a green here is
rem about the tree that is checked out - which is the whole point of running it.
rem
rem Usage:
rem   test.cmd                 the whole suite through ctest
rem   test.cmd <pattern>       run the suite, print the results matching <pattern>
rem   test.cmd --list          every test name the binary carries
rem
rem Environment overrides:
rem   MSYS2_ROOT    MSYS2 install dir              (default C:\msys64)
rem   BUILD_DIR     build directory, repo-relative (default build)
rem   TEST_TIMEOUT  seconds before a run is called hung (default 120)
rem =====================================================================
setlocal EnableExtensions

if not defined MSYS2_ROOT set "MSYS2_ROOT=C:\msys64"

set "BASH=%MSYS2_ROOT%\usr\bin\bash.exe"
if not exist "%BASH%" (
    echo [test] MSYS2 not found at "%MSYS2_ROOT%".
    echo [test] Install it with:  winget install --id MSYS2.MSYS2 -e
    echo [test] or set MSYS2_ROOT to your existing installation.
    exit /b 1
)

if not exist "%~dp0scripts\test-windows.sh" (
    echo [test] MISSING  scripts\test-windows.sh
    echo [test]          the steps; this file only launches them
    exit /b 1
)

set "REPO=%~dp0"
set "REPO=%REPO:\=/%"
if "%REPO:~-1%"=="/" set "REPO=%REPO:~0,-1%"

set "MSYSTEM=MINGW64"
set "CHERE_INVOKING=1"

"%BASH%" -l "%REPO%/scripts/test-windows.sh" %*
exit /b %errorlevel%
