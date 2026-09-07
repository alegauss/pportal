@echo off
rem =====================================================================
rem flip.cmd - build the shape one arranged deletion leaves, before touching it
rem
rem Thin launcher around scripts\trial-flip.sh, the way compile.cmd is around
rem scripts\build-windows.sh. Two deletions are prepared in this tree and neither
rem has landed, and every check on either one reads TEXT: a define beside a source
rem list, guards in main.c, readers in app\ asserting the two agree.
rem
rem PP786 built takion's bare shape once, by hand, and it failed to link. -Wl,--wrap
rem makes every malloc in every linked object an undefined reference to __wrap_malloc,
rem and the only definition of that symbol is in allocbudget.c - the one file BOTH
rem flips remove. Every text check was green while that was true.
rem
rem So this is the thing a person runs before touching either arrangement, and the
rem flip's own task runs it as its first move. It is NOT part of the gate: compile.cmd
rem builds one shape and test.cmd is already the slowest thing in a commit.
rem
rem It edits nothing. The trial is a cmake cache variable configured into a build
rem directory of its own, and the directory is removed when the build succeeds and
rem LEFT where it can be read when it does not.
rem
rem Usage:
rem   flip.cmd takion       chiaki-unit as PP27's end state leaves it
rem   flip.cmd framepath    chiaki-unit as PP696 leaves it
rem
rem What it builds is chiaki-unit, which is the target PP786's failure was in and the
rem one that needs no edit to lib\. The library's own after-shape wants PP780's four
rem files and the shim's wrappers behind their define, and both of those are the
rem flip's own commit rather than a trial's.
rem
rem Environment overrides:
rem   MSYS2_ROOT        MSYS2 install dir          (default C:\msys64)
rem   TRIAL_BUILD_DIR   where the trial configures (default build-flip-<name>)
rem =====================================================================
setlocal EnableExtensions

if not defined MSYS2_ROOT set "MSYS2_ROOT=C:\msys64"

set "BASH=%MSYS2_ROOT%\usr\bin\bash.exe"
if not exist "%BASH%" (
    echo [flip] MSYS2 not found at "%MSYS2_ROOT%".
    echo [flip] Install it with:  winget install --id MSYS2.MSYS2 -e
    exit /b 1
)

if "%~1"=="" (
    echo [flip] usage: flip.cmd ^<takion^|framepath^>
    exit /b 2
)

if not exist "%~dp0scripts\trial-flip.sh" (
    echo [flip] MISSING  scripts\trial-flip.sh
    echo [flip]          the trial's steps; this file only launches them
    exit /b 1
)

set "REPO=%~dp0"
set "REPO=%REPO:\=/%"
if "%REPO:~-1%"=="/" set "REPO=%REPO:~0,-1%"

set "MSYSTEM=MINGW64"
set "CHERE_INVOKING=1"

"%BASH%" -l "%REPO%/scripts/trial-flip.sh" %1
rem PP682's pair, for the reason compile.cmd gives: `if errorlevel N` means N or above, so a
rem crash's negative code reads as success unless the other half is asked too.
if errorlevel 1 goto failed
if not errorlevel 0 goto failed
exit /b 0

:failed
echo.
echo [flip] the shape does not build. That is the trial's answer, not its failure.
exit /b 1
