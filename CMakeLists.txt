
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
option(CHIAKI_ENABLE_GUI "Enable Qt GUI" ON)
option(CHIAKI_LIB_ENABLE_OPUS "Use Opus as part of Chiaki Lib" ON)
tri_option(CHIAKI_ENABLE_SPEEX "Use speex for echo cancelling mic playback" AUTO)
tri_option(CHIAKI_ENABLE_RUDP "Enable Remote Play over Internet" AUTO)
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
tri_option(CHIAKI_USE_SYSTEM_CURL "Use system-provided curl instead of submodule. Has to be built with experimental WebSocket support!" OFF)
option(CHIAKI_LIB_JSONC_EXTERNAL_PROJECT "Build json-c from source via FetchContent" OFF)
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

if(CHIAKI_ENABLE_GUI)
	find_package(PkgConfig REQUIRED)
	pkg_check_modules(LIBPLACEBO REQUIRED libplacebo>=7.349.0 IMPORTED_TARGET)
endif()

if(CHIAKI_USE_SYSTEM_CURL)
	if (CHIAKI_USE_SYSTEM_CURL STREQUAL AUTO)
		find_package(CURL COMPONENTS HTTP HTTPS WS WSS QUIET)
		set(CHIAKI_USE_SYSTEM_CURL ${CURL_FOUND})
	else()
		find_package(CURL REQUIRED COMPONENTS HTTP HTTPS WS WSS)
		set(CHIAKI_USE_SYSTEM_CURL ON)
	endif()
endif()

if(CHIAKI_ENABLE_SPEEX)
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
endif()

find_package(PythonInterp 3 REQUIRED) # Make sure nanopb doesn't find Python 2.7 because Python 2 should just die.

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

if(CHIAKI_LIB_JSONC_EXTERNAL_PROJECT)
	include(JsoncExternalProject)
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
