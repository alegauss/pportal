
add_library(munit "${CMAKE_CURRENT_SOURCE_DIR}/munit/munit.c")
target_include_directories(munit PUBLIC "${CMAKE_CURRENT_SOURCE_DIR}/munit")

# munit is a submodule pinned at upstream, so its source cannot be corrected from this
# repository. It initialises an atomic with ATOMIC_VAR_INIT, which C17 deprecated and
# C23 removed - and C23 is the default from gcc 15 on, which makes the whole test target
# fail to compile before a single assertion runs. Building this one file as the C11 it
# was written for is the fix that stays inside a file this repository owns.
set_property(TARGET munit PROPERTY C_STANDARD 11)
set_property(TARGET munit PROPERTY C_STANDARD_REQUIRED ON)

set(CHIAKI_UNIT_SOURCES
				main.c
				http.c
				rpcrypt.c
				gkcrypt.c
				takion.c
				seqnum.c
				keystate.c
				reorderqueue.c
				fec.c
				test_log.c
				test_log.h
				bitstream.c
				regist.c
				sessionbaseline.c
				frameprocessor.c
				allocbudget.c)

if(CHIAKI_ENABLE_FFMPEG_DECODER)
	list(APPEND CHIAKI_UNIT_SOURCES ffmpegdecoder.c)
endif()

add_executable(chiaki-unit ${CHIAKI_UNIT_SOURCES})
add_test(NAME unit COMMAND chiaki-unit)

target_link_libraries(chiaki-unit chiaki-lib munit)

# PP44 counts allocations per packet, which needs the allocator itself to be observable. --wrap
# redirects every malloc/calloc/realloc call in the linked objects - libchiaki's included - to the
# counting wrappers in allocbudget.c. free is deliberately not wrapped: every pointer handed out
# comes from __real_malloc, so the real free is the correct one for it.
target_link_options(chiaki-unit PRIVATE
				-Wl,--wrap=malloc
				-Wl,--wrap=calloc
				-Wl,--wrap=realloc)

# Without this the compiler is entitled to treat malloc/calloc/realloc as builtins and emit a
# call the linker never sees as an undefined reference, so --wrap silently redirects nothing and
# the counter reads zero for every allocation - a green that measures absence of instrumentation.
target_compile_options(chiaki-unit PRIVATE
				-fno-builtin-malloc
				-fno-builtin-calloc
				-fno-builtin-realloc)

if(CHIAKI_ENABLE_FFMPEG_DECODER)
	target_link_libraries(chiaki-unit FFMPEG::avcodec FFMPEG::avutil)
endif()
