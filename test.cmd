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
rem   test.cmd                 the whole suite through ctest, then the .NET host's selftest
rem   test.cmd noapp           the C suite alone
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

set "NO_APP="
set "FILTERED="
for %%a in (%*) do (
    if /I "%%~a"=="noapp" (set "NO_APP=1") else (set "FILTERED=1")
)

"%BASH%" -l "%REPO%/scripts/test-windows.sh" %*
set "CRC=%errorlevel%"

rem ---- the .NET host's selftest (PP75) -----------------------------------
rem PP2 put eleven assertions in app\SelfTest.cs and nothing ran them - the same shape PP56 and
rem PP74 each closed for something else. Run here and not in test-windows.sh: that is an MSYS2
rem login shell whose PATH is MSYS2's, and the first version of this reported "no .NET SDK" on a
rem machine that has one. Running the .exe needs no SDK at all.
rem
rem Skipped for a name filter or --list, which are about one C test and have nothing to say here.
if defined FILTERED goto after_app
if defined NO_APP (
    echo [test] .NET host: SKIPPED ^(noapp^) - nothing here says whether its assertions hold
    goto after_app
)
if not exist "%~dp0app\ChiakiNg.csproj" goto after_app

set "APP_EXE=%~dp0app\bin\Debug\net10.0-windows\win-x64\ChiakiNg.exe"
if not exist "%APP_EXE%" (
    rem Two absences, and only one is the caller's fault. No SDK means this machine cannot
    rem produce the binary and its C suite still counts, so that is a note. An SDK with nothing
    rem built is a workflow slip, and a green over assertions nobody ran is the exact lie PP56,
    rem PP74 and PP75 are all about.
    where dotnet >nul 2>&1
    if errorlevel 1 (
        echo [test] .NET host: no .NET SDK here, selftest not run ^(the C suite above still counts^)
        goto after_app
    )
    echo [test] .NET host: app\bin\Debug\...\ChiakiNg.exe does not exist - run compile.cmd first.
    echo [test]            Refusing rather than reporting green over assertions nobody ran.
    exit /b 1
)

echo.
echo [test] .NET host selftest
"%APP_EXE%" --selftest
if errorlevel 1 set "CRC=1"

rem ---- the xUnit project (PP35) ------------------------------------------
rem Both, and not one instead of the other. The selftest above runs inside the shipped binary,
rem which is what PP22's CI runs against the published single file; this runs against the
rem assemblies with one case per recorded vector, so a failure names which vector. They share
rem their oracle - the readers that parse test/*.c - so neither is a second copy of the other.
echo.
echo [test] xUnit vectors
dotnet test "%~dp0ChiakiNg.slnx" --nologo -v quiet
if errorlevel 1 set "CRC=1"

:after_app
exit /b %CRC%
