
cmake_minimum_required(VERSION 3.10)

project(chiaki)

if(NOT WIN32)
	message(FATAL_ERROR "chiaki-ng is Windows-only. Configure this project with a Windows toolchain (MSYS2/MinGW or MSVC).")
endif()

# Like option(), but the value can also be AUTO
macro(tri_option name desc default)
    set("${name}" "${default}" CACHE STRING "${desc}")
    set_property(CACHE "${name}" PROPERTY STRINGS AUTO ON OFF)
endmacro()

option(CHIAKI_ENABLE_TESTS "Enable tests for Chiaki" ON)
# PP21: OFF, which is what "the port is finished" means in the one place a machine can read it.
#
# Eight Qt modules hung off this, WebEngineQuick among them - a whole Chromium for one login
# screen. While it defaulted ON a screen could be left behind and nobody would notice, because the
# tree still built; with it OFF the toolchain is the thing that says so.
#
# What this does NOT do is delete gui/. Thirty-two files under app/ read that source to assert the
# port still matches what it was ported from - the QML a screen came from, the switch a capture
# reads, the format strings a mapping is written with - and every one of those checks reports
# "could not run" rather than failing when the file is absent. Dropping the BUILD dependency is
# this task; deleting the corpus those checks read would silence them, which is the opposite of
# what this line is for. `-DCHIAKI_ENABLE_GUI=ON` still builds the Qt client from it.
option(CHIAKI_ENABLE_GUI "Enable Qt GUI" OFF)
option(CHIAKI_LIB_ENABLE_OPUS "Use Opus as part of Chiaki Lib" ON)
tri_option(CHIAKI_ENABLE_SPEEX "Use speex for echo cancelling mic playback" AUTO)
# PP430: CHIAKI_ENABLE_RUDP stood here and nothing read it. It appeared once in the whole build -
# its own declaration - while every other option here is referenced between three and fourteen
# times, so setting it OFF changed nothing: holepunch.c, rudp.c and rudpsendbuffer.c are in the
# unconditional source list and curl and json-c link unconditionally.
#
# It goes rather than getting wired up, and PP340 is why. Gating those three sources without
# gating their callers breaks the build - session.c drives the PSN path from nine call sites with
# no conditional compilation - so a real gate is that task's work and not a line here.
#
# This also removes PP313's second door rather than deciding it: there was never a door, because
# the option was inert.
if(CHIAKI_ENABLE_GUI)
	set(CHIAKI_FFMPEG_DEFAULT ON)
else()
	set(CHIAKI_FFMPEG_DEFAULT AUTO)
endif()
tri_option(CHIAKI_ENABLE_FFMPEG_DECODER "Enable FFMPEG video decoder" ${CHIAKI_FFMPEG_DEFAULT})
option(CHIAKI_LIB_ENABLE_MBEDTLS "Use mbedtls instead of OpenSSL as part of Chiaki Lib" OFF)
option(CHIAKI_LIB_MBEDTLS_EXTERNAL_PROJECT "Fetch Mbed TLS instead of using system-provided libs" OFF)
option(CHIAKI_LIB_OPENSSL_EXTERNAL_PROJECT "Use OpenSSL as CMake external project" OFF)
option(CHIAKI_GUI_ENABLE_SDL_GAMECONTROLLER "Use SDL Gamecontroller for Input" ON)
option(CHIAKI_ENABLE_STEAM_SHORTCUT "Add ability to create Steam shortcut" ON)
tri_option(CHIAKI_USE_SYSTEM_JERASURE "Use system-provided jerasure instead of submodule" AUTO)
tri_option(CHIAKI_USE_SYSTEM_NANOPB "Use system-provided nanopb instead of submodule" AUTO)
option(CHIAKI_LIB_MINIUPNPC_EXTERNAL_PROJECT "Build miniupnpc from source via FetchContent" OFF)

set(CHIAKI_VERSION_MAJOR 1)
set(CHIAKI_VERSION_MINOR 10)
set(CHIAKI_VERSION_PATCH 0)
set(CHIAKI_VERSION ${CHIAKI_VERSION_MAJOR}.${CHIAKI_VERSION_MINOR}.${CHIAKI_VERSION_PATCH})

set(CPACK_PACKAGE_NAME "chiaki-ng")
set(CPACK_PACKAGE_DESCRIPTION "Open Source PlayStation Remote Play client for Windows")
set(CPACK_PACKAGE_VERSION_MAJOR ${CHIAKI_VERSION_MAJOR})
set(CPACK_PACKAGE_VERSION_MINOR ${CHIAKI_VERSION_MINOR})
set(CPACK_PACKAGE_VERSION_PATCH ${CHIAKI_VERSION_PATCH})
include(CPack)

list(APPEND CMAKE_MODULE_PATH "${CMAKE_CURRENT_SOURCE_DIR}/cmake")
set(CMAKE_CXX_STANDARD 11)

if(CHIAKI_USE_SYSTEM_JERASURE)
	if(CHIAKI_USE_SYSTEM_JERASURE STREQUAL AUTO)
		find_package(Jerasure QUIET)
		set(CHIAKI_USE_SYSTEM_JERASURE ${Jerasure_FOUND})
	else()
		find_package(Jerasure REQUIRED)
		set(CHIAKI_USE_SYSTEM_JERASURE ON)
	endif()
endif()

# PP21: libplacebo is the PORT's, not the Qt client's, and it was found inside the client's branch.
#
# chiaki-render (PP9) links PkgConfig::LIBPLACEBO and is built unconditionally beside the shim, for
# the reason PP4 gives: it is the half with no Qt in it. But its dependency was discovered under
# CHIAKI_ENABLE_GUI, so the first build with the GUI genuinely off failed at
# shim/CMakeLists.txt:53 - a target that has nothing to do with Qt, broken by removing Qt.
#
# That is the tail this task was filed for, arriving somewhere nobody predicted: not QString in a
# file nobody thought of as UI, but a find_package riding along in the block being deleted.
find_package(PkgConfig REQUIRED)
pkg_check_modules(LIBPLACEBO REQUIRED libplacebo>=7.349.0 IMPORTED_TARGET)

# PP32: and only where the client that links it is being built.
#
# PkgConfig::SpeexDSP is linked in exactly one place - gui/CMakeLists.txt, on the microphone path -
# and lib/ references speex nowhere at all. PP632 retired the Qt client's build, so CHIAKI_ENABLE_GUI
# is off by default and gui/ is not added; this probe went on running anyway and printing "Speex DSP
# found echo cancelling and noise suppression enabled" on every configure of a tree that links it
# nowhere. A configure line announcing a feature no target has is worse than silence: it is the
# report somebody checks before asking whether the dependency is still needed.
#
# So the probe follows the client. Building with -DCHIAKI_ENABLE_GUI=ON restores it unchanged, which
# is the point - this removes a dependency from the default build and not from the client.
if(CHIAKI_ENABLE_GUI AND CHIAKI_ENABLE_SPEEX)
	set(SpeexDSP_FOUND FALSE)
	find_package(SpeexDSP QUIET)
	pkg_check_modules(SpeexDSP speexdsp IMPORTED_TARGET)
	if(SpeexDSP_FOUND)
		set(CHIAKI_ENABLE_SPEEX ON)
		message(STATUS "Speex DSP found echo cancelling and noise suppression enabled")
	else()
		set(CHIAKI_ENABLE_SPEEX OFF)
		message(STATUS "Speex DSP not found echo cancelling and noise suppression disabled")
	endif()
elseif(NOT CHIAKI_ENABLE_GUI)
	set(CHIAKI_ENABLE_SPEEX OFF)
endif()

# PP431: FindPython3, because CMP0148 says FindPythonInterp is REMOVED - not deprecated. The old
# call worked only while cmake_minimum_required names 3.10 and so selects that policy's OLD
# behaviour; on a CMake where it is NEW, configure fails rather than warns.
#
# The comment that stood here said this was to stop nanopb finding Python 2.7, and it was not:
# FindPythonInterp set PYTHON_EXECUTABLE, nanopb reads Python_EXECUTABLE, and
# third-party/nanopb/CMakeLists.txt does its own find_package(Python) with the modern module.
# Nothing this sets reaches nanopb.
#
# What it IS for is lib/protobuf, which runs the nanopb generator as this interpreter and is the
# only consumer in the tree. Interpreter alone: no headers or libraries are wanted here.
find_package(Python3 3 REQUIRED COMPONENTS Interpreter)

if(CHIAKI_USE_SYSTEM_NANOPB)
	if(CHIAKI_USE_SYSTEM_NANOPB STREQUAL AUTO)
		find_package(Nanopb QUIET)
		set(CHIAKI_USE_SYSTEM_NANOPB ${Nanopb_FOUND})
	else()
		find_package(Nanopb REQUIRED)
		set(CHIAKI_USE_SYSTEM_NANOPB ON)
	endif()
endif()

if(CHIAKI_LIB_OPENSSL_EXTERNAL_PROJECT)
	include(OpenSSLExternalProject)
endif()

add_subdirectory(third-party)

add_definitions(-DCHIAKI_VERSION_MAJOR=${CHIAKI_VERSION_MAJOR} -DCHIAKI_VERSION_MINOR=${CHIAKI_VERSION_MINOR} -DCHIAKI_VERSION_PATCH=${CHIAKI_VERSION_PATCH} -DCHIAKI_VERSION=\"${CHIAKI_VERSION}\")

if(CHIAKI_LIB_ENABLE_MBEDTLS)
	add_definitions(-DCHIAKI_LIB_ENABLE_MBEDTLS)
	if(CHIAKI_LIB_MBEDTLS_EXTERNAL_PROJECT)
		set(FETCHCONTENT_QUIET CACHE BOOL FALSE)
		include(FetchContent)
		set(ENABLE_TESTING CACHE INTERNAL OFF)
		set(ENABLE_PROGRAMS CACHE INTERNAL OFF)
		set(USE_SHARED_MBEDTLS_LIBRARY CACHE INTERNAL OFF)
		FetchContent_Declare(
			mbedtls
			GIT_REPOSITORY https://github.com/Mbed-TLS/mbedtls.git
			GIT_TAG 8b3f26a5ac38d4fdccbc5c5366229f3e01dafcc0 # v2.28.0
			GIT_PROGRESS TRUE
		)
		FetchContent_MakeAvailable(mbedtls)
	endif()
endif()

if(CHIAKI_ENABLE_STEAM_SHORTCUT)
	message(STATUS "Steam shortcut enabled")
else()
	message(STATUS "Steam shortcut disabled")
endif()

if(CHIAKI_ENABLE_FFMPEG_DECODER)
	find_package(FFMPEG COMPONENTS avcodec avutil avformat)
	if(FFMPEG_FOUND)
		set(CHIAKI_ENABLE_FFMPEG_DECODER ON)
	else()
		if(NOT CHIAKI_ENABLE_FFMPEG_DECODER STREQUAL AUTO)
			message(FATAL_ERROR "CHIAKI_ENABLE_FFMPEG_DECODER is set to ON, but ffmpeg could not be found.")
		endif()
		set(CHIAKI_ENABLE_FFMPEG_DECODER OFF)
	endif()
endif()

if(CHIAKI_ENABLE_FFMPEG_DECODER)
	message(STATUS "FFMPEG Decoder enabled")
else()
	message(STATUS "FFMPEG Decoder disabled")
endif()

if(CHIAKI_LIB_MINIUPNPC_EXTERNAL_PROJECT)
	include(MiniupnpcExternalProject)
endif()

add_subdirectory(lib)

# PP4: the seam the .NET host calls through. Built unconditionally and not behind
# CHIAKI_ENABLE_GUI - it is the half of the port that has no Qt in it at all.
add_subdirectory(shim)

if(CHIAKI_ENABLE_GUI)
	find_package(SDL2 MODULE REQUIRED)
	add_subdirectory(gui)
endif()

if(CHIAKI_ENABLE_TESTS)
	enable_testing()
	add_subdirectory(test)
endif()
