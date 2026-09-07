
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
				allocbudget.c
				videoreceiver.c
				decoderchoice.c)

if(CHIAKI_ENABLE_FFMPEG_DECODER)
	list(APPEND CHIAKI_UNIT_SOURCES ffmpegdecoder.c)
endif()

add_executable(chiaki-unit ${CHIAKI_UNIT_SOURCES})
add_test(NAME unit COMMAND chiaki-unit)

# PP760: whether the four frame-path suites are compiled in, which main.c reads.
#
# On the test target rather than in config.h, where CHIAKI_LIB_ENABLE_FFMPEG_DECODER lives: that one
# is set in lib/CMakeLists.txt, and PP623's shape gives this deletion exactly one commit that edits
# lib/. So the switch sits next to the list it has to agree with - the four files above and this line
# turn off together, in the commit that removes them.
target_compile_definitions(chiaki-unit PRIVATE CHIAKI_UNIT_HAVE_FRAMEPATH=1)

# PP786: and the same switch for takion's three, which PP780's link named and nothing guarded.
#
# takion.c and reorderqueue.c exercise the modules PP27's end state deletes; allocbudget.c is
# claimed by BOTH flips and leaves on whichever lands first, which is why main.c nests its guard
# inside the frame path's rather than beside it.
#
# The two switches decide their OWN files, so one flip landing does not make the other look
# half-finished: takion.c and reorderqueue.c answer for this one, fec.c, frameprocessor.c and
# videoreceiver.c for that one, and allocbudget.c answers for neither because it answers to both.
target_compile_definitions(chiaki-unit PRIVATE CHIAKI_UNIT_HAVE_TAKION=1)

target_link_libraries(chiaki-unit chiaki-lib munit)

# PP44 counts allocations per packet, which needs the allocator itself to be observable. --wrap
# redirects every malloc/calloc/realloc call in the linked objects - libchiaki's included - to the
# counting wrappers in allocbudget.c. free is deliberately not wrapped: every pointer handed out
# comes from __real_malloc, so the real free is the correct one for it.
#
# PP786: CONDITIONAL ON THE FILE THAT DEFINES THE WRAPPERS, and it was not. --wrap makes every
# malloc call in every linked object - libchiaki's, munit's and jerasure's - an undefined reference
# to __wrap_malloc, and the only definition of that symbol is in allocbudget.c. Both flips remove
# that file, so both would have hit a link failure listing every allocation site in the tree, in
# the one commit that is not allowed to edit a test file. A trial of the bare shape is what found
# it: PP760's trial edited text and never rebuilt the C, which is exactly what this could hide in.
#
# Derived from the list rather than put behind a third switch, so the file leaving is the one edit.
if("allocbudget.c" IN_LIST CHIAKI_UNIT_SOURCES)
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
endif()

if(CHIAKI_ENABLE_FFMPEG_DECODER)
	target_link_libraries(chiaki-unit FFMPEG::avcodec FFMPEG::avutil)
endif()
