@echo off
rem =====================================================================
rem package.cmd - assemble what an installer would ship, and prove it runs
rem
rem The third of the three commands this tree has. compile.cmd answers "does it
rem build", test.cmd answers "do the assertions hold", and this answers the one
rem neither of them can: does what would be INSTALLED start on a machine that
rem has no checkout on it.
rem
rem That question has never been asked here, and it is not academic. PP22's
rem first half shipped because a single-file publish leaves Assembly.Location
rem empty, so the published host found neither its shim nor any drift source -
rem and every build straight out of the tree stayed green throughout, because a
rem host running inside a checkout walks up and finds both. The same walk hides
rem the next packaging defect just as well. So the payload is not merely built
rem here, it is COPIED OUT of the repository and run there: outside a checkout
rem the walk finds nothing, and the only libraries the host can load are the
rem ones this script put beside it.
rem
rem What it produces:
rem   build\chiaki-ng-package\   the payload - the published host, the three
rem                              native libraries it loads, and their imports
rem   build\chiaki-ng-windows-installer.exe   the installer over that payload
rem
rem Usage:
rem   package.cmd                publish, stage, prove, compile the installer
rem   package.cmd nopublish      stage and prove what is already published
rem   package.cmd nosetup        stop after the proof - no installer
rem
rem Environment overrides:
rem   MSYS2_ROOT    MSYS2 install dir              (default C:\msys64)
rem   BUILD_DIR     build directory, repo-relative (default build)
rem   NATIVE_DIR    the tree the native DLLs come from (default %BUILD_DIR%\chiaki-ng-Win)
rem   PACKAGE_DIR   payload output, repo-relative  (default %BUILD_DIR%\chiaki-ng-package)
rem =====================================================================
setlocal EnableExtensions

if not defined MSYS2_ROOT set "MSYS2_ROOT=C:\msys64"
if not defined BUILD_DIR set "BUILD_DIR=build"
if not defined NATIVE_DIR set "NATIVE_DIR=%BUILD_DIR%\chiaki-ng-Win"
if not defined PACKAGE_DIR set "PACKAGE_DIR=%BUILD_DIR%\chiaki-ng-package"

set "DO_PUBLISH=1"
set "NO_SETUP="
set "BAD_ARG="
for %%a in (%*) do (
    if /I "%%~a"=="nopublish" (set "DO_PUBLISH=") else if /I "%%~a"=="nosetup" (set "NO_SETUP=1") else (set "BAD_ARG=%%~a")
)
if defined BAD_ARG (
    echo [package] unknown argument: %BAD_ARG%
    echo [package] usage: package.cmd [nopublish] [nosetup]
    exit /b 2
)

rem ---- preflight ---------------------------------------------------------
rem Same contract as compile.cmd's: name the missing file here, in a second,
rem rather than let dotnet or a shell script describe it halfway through.
set "MISSING="
call :need "app\ChiakiNg.csproj"          "the .NET host; there is nothing to package without it"
call :need "scripts\package-windows.sh"   "the native payload walk; this file only launches it"
call :need "%NATIVE_DIR%\chiaki-shim.dll" "the native tree compile.cmd deploys - run it first"
if defined MISSING (
    echo.
    echo [package] Cannot package: the file^(s^) above are read by this script.
    exit /b 1
)

set "BASH=%MSYS2_ROOT%\usr\bin\bash.exe"
if not exist "%BASH%" (
    echo [package] MSYS2 not found at "%MSYS2_ROOT%".
    echo [package] Install it with:  winget install --id MSYS2.MSYS2 -e
    echo [package] or set MSYS2_ROOT to your existing installation.
    exit /b 1
)

rem The SDK is refused rather than noted, which is where this differs from
rem compile.cmd. There, a missing .NET SDK costs the .NET host and the Qt client
rem still builds, so it is a note. Here the .NET host IS the deliverable.
where dotnet >nul 2>&1
if errorlevel 1 (
    echo [package] No .NET SDK on PATH, and the host is what this packages.
    echo [package] Install it with:  winget install --id Microsoft.DotNet.SDK.10 -e
    exit /b 1
)

set "PUBLISH_DIR=%~dp0app\bin\Release\net10.0-windows\win-x64\publish"

echo [package] payload    : %PACKAGE_DIR%
echo [package] native from: %NATIVE_DIR%
echo.

rem ---- the managed half --------------------------------------------------
rem Release and not Debug: PublishSingleFile, SelfContained and the compression
rem are all conditioned on Release in the csproj, deliberately, so that a publish
rem cannot quietly stop being self-contained.
if not defined DO_PUBLISH goto published
echo [package] publishing the host ...
dotnet publish "%~dp0app\ChiakiNg.csproj" -c Release --nologo -v quiet
if errorlevel 1 (
    echo.
    echo [package] FAILED - the host did not publish.
    exit /b 1
)
:published
if not exist "%PUBLISH_DIR%\ChiakiNg.exe" (
    echo [package] no ChiakiNg.exe in %PUBLISH_DIR%
    echo [package] Run package.cmd without 'nopublish'.
    exit /b 1
)

rem ---- the payload -------------------------------------------------------
rem Wiped and not merged. A payload directory that keeps whatever a previous run
rem left in it is how a DLL that no longer belongs stays in an installer for a
rem release or two, and the whole point of walking the imports is to know exactly
rem what is in there.
if exist "%~dp0%PACKAGE_DIR%" rd /s /q "%~dp0%PACKAGE_DIR%"
mkdir "%~dp0%PACKAGE_DIR%" 2>nul

set "REPO=%~dp0"
set "REPO=%REPO:\=/%"
if "%REPO:~-1%"=="/" set "REPO=%REPO:~0,-1%"
set "MSYSTEM=MINGW64"
set "CHERE_INVOKING=1"

"%BASH%" -l "%REPO%/scripts/package-windows.sh" "%REPO%/%NATIVE_DIR:\=/%" "%REPO%/%PACKAGE_DIR:\=/%"
if errorlevel 1 (
    echo.
    echo [package] FAILED - the native payload could not be assembled.
    exit /b 1
)

copy /y "%PUBLISH_DIR%\ChiakiNg.exe" "%~dp0%PACKAGE_DIR%\" >nul
if errorlevel 1 (
    echo [package] FAILED - could not stage the published host.
    exit /b 1
)

rem ---- the proof ---------------------------------------------------------
rem OUTSIDE the repository, and that is the entire value of this step. Run in
rem place, the host would walk up out of build\ and find the shim, SDL and the
rem renderer in the tree it was built in - so a payload missing all three would
rem pass. Under %TEMP% there is nothing above it to find, which is the condition
rem an installed copy runs in.
set "PROOF=%TEMP%\chiaki-ng-package-proof"
if exist "%PROOF%" rd /s /q "%PROOF%"
robocopy "%~dp0%PACKAGE_DIR%" "%PROOF%" /e /njh /njs /ndl /nfl /np >nul
if errorlevel 8 (
    echo [package] FAILED - could not copy the payload to %PROOF%.
    exit /b 1
)

echo.
echo [package] selftest, out of %PROOF%
"%PROOF%\ChiakiNg.exe" --selftest
if errorlevel 1 (
    echo.
    echo [package] FAILED - the payload does not run where there is no checkout.
    echo [package]          That is what an installed copy is, so this is a red
    echo [package]          installer and not a red test.
    exit /b 1
)
rd /s /q "%PROOF%" 2>nul

echo.
echo [package] the payload runs with no checkout beneath it:
echo [package]   %~dp0%PACKAGE_DIR%

rem ---- the installer (PP274) ---------------------------------------------
rem Last, and only over a payload that has just been proved. ISCC reads the payload
rem directory twice at COMPILE time - once for the version off ChiakiNg.exe and once
rem for the file list - so compiling it against a stale or absent one produces an
rem installer carrying nothing, with a version of 0.0.0, and exit code 0. The script
rem has an #error guard for the absent case; running it here, after the proof, is
rem what covers the stale one.
if defined NO_SETUP (
    echo.
    echo [package] installer: SKIPPED ^(nosetup^) - the payload above is not packaged
    exit /b 0
)

rem Four places, and the order is the lesson. Inno Setup 6 installs per-user by
rem default, into %LOCALAPPDATA%\Programs - looking under Program Files alone reports
rem "not installed" on a machine that has it, which is exactly what happened when this
rem was first written by hand.
set "ISCC="
for %%p in (iscc.exe) do if not defined ISCC if exist "%%~$PATH:p" set "ISCC=%%~$PATH:p"
call :find_iscc "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe"
call :find_iscc "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
call :find_iscc "%ProgramFiles%\Inno Setup 6\ISCC.exe"
if not defined ISCC (
    echo.
    echo [package] No Inno Setup compiler found, and the installer is what this produces.
    echo [package] Install it with:  winget install --id JRSoftware.InnoSetup -e
    echo [package] or run package.cmd nosetup for the proved payload alone.
    exit /b 1
)

echo.
echo [package] compiling the installer ...
"%ISCC%" /Q "%~dp0scripts\chiaki-ng.iss"
if errorlevel 1 (
    echo.
    echo [package] FAILED - the payload is good and the installer script is not.
    exit /b 1
)

echo.
echo [package] OK:
echo [package]   %~dp0%BUILD_DIR%\chiaki-ng-windows-installer.exe
exit /b 0

rem ---------------------------------------------------------------------------
:need
if exist "%~dp0%~1" exit /b 0
echo [package] MISSING  %~1
echo [package]          %~2
set "MISSING=1"
exit /b 0

:find_iscc
if defined ISCC exit /b 0
if exist "%~1" set "ISCC=%~1"
exit /b 0
