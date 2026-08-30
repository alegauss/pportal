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
set "INTERACTION="
for %%a in (%*) do (
    if /I "%%~a"=="noapp" (set "NO_APP=1") else if /I "%%~a"=="interaction" (set "INTERACTION=1") else (set "FILTERED=1")
)

rem PP227: the interaction tests, and ONLY them.
rem They open the real application and drive its window, so they are neither part of the gate above
rem nor a C test name - the word is recognised here rather than passed down, which is what stops
rem `test.cmd interaction` being read as "run the C test called interaction" and reporting that no
rem such test exists.
if defined INTERACTION (
    echo [test] interaction - the real window, through UI Automation
    dotnet test "%~dp0ChiakiNg.slnx" --nologo -v normal --filter "Category=Interaction"
    exit /b %errorlevel%
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

rem ---- the tool's own selftest (PP569) -----------------------------------
rem PP75's shape a second time. compare-baselines ships `--self-test` - its README calls it "the
rem assertion this tool ships with" - and nothing ran it: not this gate, not CI. The tool is in the
rem solution so it BUILDS in both, which is what made the gap easy to miss - a green build over an
rem assertion nobody executes is the exact lie PP56, PP74 and PP75 are about.
rem
rem It matters more than a spare tool would, because the site's front page promises what this
rem prints (PP-era SiteProseClaims holds that join), so a regression here is a page that lies.
rem
rem Missing binary is a note and not a failure, the way the C suite's is: the tool is not what the
rem gate is for, and a machine that built the host but not the solution still ran everything above.
set "CB_EXE=%~dp0tools\compare-baselines\bin\Debug\net10.0\compare-baselines.exe"
if exist "%CB_EXE%" (
    echo.
    echo [test] compare-baselines selftest
    "%CB_EXE%" --self-test
    if errorlevel 1 set "CRC=1"
) else (
    echo [test] compare-baselines: not built, selftest not run
)

rem PP570: the same gap one project over. measure-startup ships the same flag and was not even in
rem the solution, so no gate built it - which is why PP569's sweep had to be finished rather than
rem stopped at the first tool.
set "MS_EXE=%~dp0tools\measure-startup\bin\Debug\net10.0-windows\measure-startup.exe"
if exist "%MS_EXE%" (
    echo.
    echo [test] measure-startup selftest
    "%MS_EXE%" --self-test
    if errorlevel 1 set "CRC=1"
) else (
    echo [test] measure-startup: not built, selftest not run
)

rem ---- the governed files (PP587) ----------------------------------------
rem PP36 put `roadkeep lint` in a workflow of its own and the local gate never gained it, so the
rem first reader of a drift was a push - and what PP572's line says about build.yml is true here
rem too: this repo has no green run to spare. The check itself is under a second.
rem
rem It was easy to leave out because the plugin's hook validates every WRITE, and nearly every drift
rem would have to get past that. The ones that can: a hand-edit that went round the hook, a merge,
rem and a rule the engine gained since the file was last written - the third is not hypothetical,
rem this tree grew two lint notes and lost them again inside one session as the engine moved.
rem
rem A NOTE WHEN THE TOOL IS ABSENT, a failure when it says so, which is the shape the two selftests
rem above use. roadkeep is not vendored here and a machine without it still ran everything above;
rem its exit code is the whole contract, as .github\workflows\roadkeep.yml says of the same call.
where roadkeep >nul 2>&1
if errorlevel 1 (
    echo [test] roadkeep: not on PATH, lint not run
) else (
    echo.
    echo [test] roadkeep lint
    roadkeep lint
    if errorlevel 1 set "CRC=1"
)

rem ---- the xUnit project (PP35) ------------------------------------------
rem Both, and not one instead of the other. The selftest above runs inside the shipped binary,
rem which is what PP22's CI runs against the published single file; this runs against the
rem assemblies with one case per recorded vector, so a failure names which vector. They share
rem their oracle - the readers that parse test/*.c - so neither is a second copy of the other.
rem PP227: the interaction tests are EXCLUDED here and run by `test.cmd interaction`.
rem They launch the real application, drive its window through UI Automation and - for the mapping
rem screen - need a controller plugged in. None of that belongs in the gate a build machine runs:
rem a suite that opens windows is a suite that fails for reasons about the desk it ran on. What
rem they are not is optional, which is why they are one word away rather than behind a comment.
echo.
echo [test] xUnit vectors
dotnet test "%~dp0ChiakiNg.slnx" --nologo -v quiet --filter "Category!=Interaction"
if errorlevel 1 set "CRC=1"

:after_app
exit /b %CRC%
