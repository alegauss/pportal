
set(HEADER_FILES
		include/chiaki/session.h
		include/chiaki/common.h
		include/chiaki/sock.h
		include/chiaki/thread.h
		include/chiaki/base64.h
		include/chiaki/http.h
		include/chiaki/log.h
		include/chiaki/ctrl.h
		include/chiaki/rpcrypt.h
		include/chiaki/takion.h
		include/chiaki/senkusha.h
		include/chiaki/streamconnection.h
		include/chiaki/ecdh.h
		include/chiaki/launchspec.h
		include/chiaki/random.h
		include/chiaki/gkcrypt.h
		include/chiaki/audio.h
		include/chiaki/audioreceiver.h
		include/chiaki/audiosender.h
		include/chiaki/video.h
		include/chiaki/videoreceiver.h
		include/chiaki/frameprocessor.h
		include/chiaki/packetstats.h
		include/chiaki/sessionbaseline.h
		include/chiaki/messagetap.h
		include/chiaki/decoderchoice.h
		include/chiaki/seqnum.h
		include/chiaki/discovery.h
		include/chiaki/congestioncontrol.h
		include/chiaki/stoppipe.h
		include/chiaki/reorderqueue.h
		include/chiaki/discoveryservice.h
		include/chiaki/feedback.h
		include/chiaki/feedbacksender.h
		include/chiaki/controller.h
		include/chiaki/takionsendbuffer.h
		include/chiaki/time.h
		include/chiaki/fec.h
		include/chiaki/regist.h
		include/chiaki/opusdecoder.h
		include/chiaki/opusencoder.h
		include/chiaki/orientation.h
		include/chiaki/bitstream.h
		include/chiaki/remote/holepunch.h
		include/chiaki/remote/rudp.h
		include/chiaki/remote/rudpsendbuffer.h)

set(SOURCE_FILES
		src/common.c
		src/sock.c
		src/session.c
		src/thread.c
		src/base64.c
		src/http.c
		src/log.c
		src/ctrl.c
		src/rpcrypt.c
		src/utils.h
		src/pb_utils.h
		src/ecdh.c
		src/launchspec.c
		src/random.c
		src/gkcrypt.c
		src/audio.c
		src/audioreceiver.c
		src/packetstats.c
		src/sessionbaseline.c
		src/messagetap.c
		src/decoderchoice.c
		src/discovery.c
		src/stoppipe.c
		src/discoveryservice.c
		src/feedback.c
		src/controller.c
		src/time.c
		src/regist.c
		src/opusdecoder.c
		src/orientation.c
		src/bitstream.c
		src/remote/rudp.c
		src/remote/rudpsendbuffer.c)

# PP27: the transport is GONE, and the frame path went with it because the linker said it must.
#
# TWELVE FILES AND NOT THREE, and the difference is not scope creep - it is what asking the linker
# answered. takion.c, takionsendbuffer.c and reorderqueue.c are the three the criterion names. Six
# more have no other caller: senkusha.c and streamconnection.c lost theirs when PP783 and PP784 made
# session.c's two phases callbacks, feedbacksender.c and congestioncontrol.c were reached only by
# streamconnection.c, and audiosender.c only by opusencoder.c, which nothing reaches at all.
#
# THE TENTH IS videoreceiver.c AND IT IS THE ONE NOBODY SAW. Those nine leave libchiaki.a short
# exactly two symbols - stream_connection_send_corrupt_frame and stream_connection_send_idr_request -
# and videoreceiver.c calls them at three sites. A text search over lib/ finds them; PP780's census
# did not, because it asked about takion's symbols rather than about what the deletion strands. With
# it out, frameprocessor.c and fec.c have no caller inside lib either, and the archive closes: zero
# unresolved. So the frame path's flip (PP295's fourth criterion) is not a neighbour of this one, it
# is inside it - the two are one commit or neither.
#
# THE HEADERS STAY IN THE LIST ABOVE, and it is the same non-oversight PP33's note claims below.
# session.h carries ChiakiTakion by value and regist.c reads types out of others of them, so the
# declarations are load-bearing while the implementations are not - and a declaration with no
# definition costs a link error only where something calls it, which is the point.
#
# WHAT HELD THEM WAS THE ORACLE and not a caller: shim wrappers holding the managed replacements
# against the C. PP785 recorded takion's answers and PP842 the frame path's, into tests/oracles, so
# the differentials outlive the C; PP786 put the suite's own files behind their switch; PP841 put
# takion's wrappers behind CHIAKI_SHIM_TAKION_ORACLE and this commit adds the frame path's option
# beside it. Both are off now. Those four tasks are why this is a list edit and not forty red
# assertions.
#
# AND A DELETION HAS BROKEN THIS BEFORE. PP696 removed the frame path's four files, was green on
# every gate, and left a client that could not stream; PP763 put them back. What is different is not
# a better gate - it is that the port drives live sessions now, so what admitted this commit was a
# run against a console and not a passing suite.

# PP33: holepunch.c is gone, and curl and json-c with it.
#
# rudp.c and rudpsendbuffer.c stay and were never part of this. PP430 grouped the three because
# they arrived together, and only one of them called curl and json-c; the rudp pair is the takion
# path's and reaches no PSN at all.
#
# The HEADER stays in the list above and is not an oversight. regist.c reads
# ChiakiHolepunchRegistInfo on the PSN registration path - always NULL since PP632, and still
# compiled - so the types are load-bearing while the implementation is not.

# decoderchoice.c is deliberately outside this gate. It names decoders but includes nothing
# from ffmpeg and links nothing: it is a decision over four booleans, and gating it would
# make the assertion that holds PP51's non-NVIDIA floor conditional on a build option.
if(CHIAKI_ENABLE_FFMPEG_DECODER)
	list(APPEND HEADER_FILES include/chiaki/ffmpegdecoder.h)
	list(APPEND SOURCE_FILES src/ffmpegdecoder.c)
endif()
set(CHIAKI_LIB_ENABLE_FFMPEG_DECODER "${CHIAKI_ENABLE_FFMPEG_DECODER}")

add_subdirectory(protobuf)
set_source_files_properties(${CHIAKI_LIB_PROTO_SOURCE_FILES} ${CHIAKI_LIB_PROTO_HEADER_FILES} PROPERTIES GENERATED TRUE)
include_directories("${CHIAKI_LIB_PROTO_INCLUDE_DIR}")

if(CHIAKI_LIB_ENABLE_OPUS)
	find_package(Opus REQUIRED)
	include_directories(${Opus_INCLUDE_DIRS})
endif()

add_library(chiaki-lib ${HEADER_FILES} ${SOURCE_FILES} ${CHIAKI_LIB_PROTO_SOURCE_FILES} ${CHIAKI_LIB_PROTO_HEADER_FILES})
configure_file(config.h.in include/chiaki/config.h)
target_include_directories(chiaki-lib PUBLIC "${CMAKE_CURRENT_BINARY_DIR}/include")

if(CMAKE_C_COMPILER_ID STREQUAL GNU OR CMAKE_C_COMPILER_ID STREQUAL Clang)
	target_compile_options(chiaki-lib PRIVATE -Wall)
endif()

add_dependencies(chiaki-lib chiaki-pb)
set_target_properties(chiaki-lib PROPERTIES OUTPUT_NAME chiaki)

target_link_libraries(chiaki-lib wsock32 ws2_32 bcrypt iphlpapi)

target_include_directories(chiaki-lib PUBLIC "${CMAKE_CURRENT_SOURCE_DIR}/include")

find_package(Threads REQUIRED)
target_link_libraries(chiaki-lib Threads::Threads)

# PP33: json-c is gone. holepunch.c was the only file in lib/ that included it.

if(CHIAKI_LIB_MINIUPNPC_EXTERNAL_PROJECT)
	target_link_libraries(chiaki-lib libminiupnpc-static)
	target_include_directories(chiaki-lib PRIVATE "${MINIUPNPC_PREFIX_INCLUDE_DIR}")
else()
	find_package(PkgConfig REQUIRED)
	pkg_search_module(MINIUPNPC REQUIRED miniupnpc IMPORTED_TARGET)
	target_link_libraries(chiaki-lib PkgConfig::MINIUPNPC)
endif()

find_program(PKG_CONFIG_EXECUTABLE pkg-config)
if(PKG_CONFIG_EXECUTABLE)
	pkg_search_module(LIBEVENT libevent QUIET IMPORTED_TARGET)
endif()
if(TARGET PkgConfig::LIBEVENT)
	target_link_libraries(chiaki-lib PkgConfig::LIBEVENT)
else()
	find_package(libevent REQUIRED)
	target_include_directories(chiaki-lib PRIVATE ${LIBEVENT_INCLUDE_DIRS})
	target_link_libraries(chiaki-lib ${LIBEVENT_LIBRARIES})
endif()

# PP33: and curl, for the same reason and in the same commit.

if(CHIAKI_LIB_ENABLE_MBEDTLS)
	if(CHIAKI_LIB_MBEDTLS_EXTERNAL_PROJECT)
		target_link_libraries(chiaki-lib mbedtls mbedx509 mbedcrypto)
	else()
		# provided by mbedtls-static (mbedtls-devel)
		find_library(MBEDTLS mbedtls)
		find_library(MBEDX509 mbedx509)
		find_library(MBEDCRYPTO mbedcrypto)
		target_link_libraries(chiaki-lib ${MBEDTLS} ${MBEDX509} ${MBEDCRYPTO})
	endif()
elseif(CHIAKI_LIB_OPENSSL_EXTERNAL_PROJECT)
	target_link_libraries(chiaki-lib OpenSSL_Crypto)
else()
	# default
	find_package(OpenSSL REQUIRED)
	target_link_libraries(chiaki-lib OpenSSL::Crypto)
endif()

target_link_libraries(chiaki-lib Nanopb::nanopb)
target_link_libraries(chiaki-lib Jerasure::Jerasure)

if(CHIAKI_ENABLE_FFMPEG_DECODER)
	target_link_libraries(chiaki-lib FFMPEG::avcodec FFMPEG::avutil)
endif()

if(CHIAKI_LIB_ENABLE_OPUS)
	target_link_libraries(chiaki-lib ${Opus_LIBRARIES})
endif()

#set(CMAKE_C_FLAGS "${CMAKE_C_FLAGS} -fanalyzer")
# PP591: holepunch-test was declared here and is gone. It was upstream's manual PSN probe - it read
# an oauth token from /tmp/token.txt, which no Windows machine has, and nothing in this tree ever
# ran it: it is not a ctest case, it needs credentials and a console, and CHIAKI_ENABLE_TESTS built
# it on every build regardless. PP544 held the choice of porting it, deleting it, or keeping it as
# the hardware probe. The probe this port keeps is the managed one - PP479 drives the PSN sequence
# and PP508 shows all seven seam methods reached, over the nine shim wrappers PP481 wrote - so what
# was left here was a second consumer of the file PP33 deletes, and no reader of it.

# PP44's allocation gate counts libchiaki's allocations by wrapping malloc at link time in the
# test binary. Without this the compiler may emit builtin allocation calls the linker never sees
# as undefined references, so --wrap redirects nothing and the gate passes while blind. Keeping it
# on the library rather than only on the test is what makes the instrument measure this code.
target_compile_options(chiaki-lib PRIVATE
	-fno-builtin-malloc
	-fno-builtin-calloc
	-fno-builtin-realloc)