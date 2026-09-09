
add_library(munit "${CMAKE_CURRENT_SOURCE_DIR}/munit/munit.c")
target_include_directories(munit PUBLIC "${CMAKE_CURRENT_SOURCE_DIR}/munit")

# munit is a submodule pinned at upstream, so its source cannot be corrected from this
# repository. It initialises an atomic with ATOMIC_VAR_INIT, which C17 deprecated and
# C23 removed - and C23 is the default from gcc 15 on, which makes the whole test target
# fail to compile before a single assertion runs. Building this one file as the C11 it
# was written for is the fix that stays inside a file this repository owns.
set_property(TARGET munit PROPERTY C_STANDARD 11)
set_property(TARGET munit PROPERTY C_STANDARD_REQUIRED ON)

# PP27: six files are gone, with the C they were about.
#
# takion.c and reorderqueue.c exercise modules that left lib/CMakeLists.txt in this commit - PP780's
# link named seven symbols in one and six in the other - and fec.c, frameprocessor.c and
# videoreceiver.c the three the frame path took with them. A suite keeping any of them would be munit
# cases compiled against nothing.
#
# allocbudget.c is the file claimed by BOTH prepared flips and leaves on whichever lands first, which
# is this one because they landed together. PP44's budget over the transport is not lost with it:
# ManagedTakionTests times the warm loop's allocations as a difference between two runs of equal
# length, which is the port's own instrument and the one the criterion is about.
#
# The two derived switches below now answer 0, and the --wrap block goes with allocbudget.c exactly
# as PP803 arranged.
set(CHIAKI_UNIT_SOURCES
				main.c
				http.c
				rpcrypt.c
				gkcrypt.c
				seqnum.c
				keystate.c
				test_log.c
				test_log.h
				bitstream.c
				regist.c
				sessionbaseline.c
				decoderchoice.c)

if(CHIAKI_ENABLE_FFMPEG_DECODER)
	list(APPEND CHIAKI_UNIT_SOURCES ffmpegdecoder.c)
endif()

# PP803: the suite's half of each arranged flip, BUILT rather than read.
#
# Two deletions are prepared and neither has landed, and every check on either one reads TEXT: a
# define beside this list, guards in main.c, readers in app that assert the two agree. PP786 built
# takion's bare shape once, by hand, and the build failed - --wrap makes every malloc in every
# linked object an undefined reference to __wrap_malloc, and the only definition is in the one file
# both flips remove. Every text check was green while that was true, and PP760's own trial had
# edited text and never rebuilt the C.
#
# WHAT THIS BUILDS IS THE SUITE'S SHAPE AND NOT THE LIBRARY'S, deliberately. chiaki-unit is the
# target PP786's failure was in, it links chiaki-lib and munit and not the shim, and it needs no
# edit to lib/ - so this asks a whole question without doing any of the flip. The library's own
# after-shape needs PP780's four files and the shim's wrappers behind their define, and both of
# those belong to the flip's commit.
#
# ALLOCBUDGET GOES ON EITHER, which is the case that found this: it is claimed by both flips and
# leaves on whichever lands first. The --wrap block below is already derived from the list, so
# taking the file out is the whole edit and the link options go with it.
#
# PP27: BOTH FLIPS LANDED, so the variable is spent and refuses rather than pretending.
#
# REMOVE_ITEM on a file that is not in the list is a silent no-op, so either value would still
# configure, still print its message and change nothing - a trial reporting the after-shape of a tree
# that is already in it, which is the exact failure PP803 built this to catch. Refusing names the
# reason at the one moment somebody would be misled. It is kept rather than deleted because a third
# deletion will want the same instrument, and what it needs then is this shape and a new value.
set(CHIAKI_TRIAL_FLIP "" CACHE STRING
		"Build chiaki-unit as an arranged flip would leave it - no flip is arranged, so: empty")

if(NOT CHIAKI_TRIAL_FLIP STREQUAL "")
	message(FATAL_ERROR
			"CHIAKI_TRIAL_FLIP has no values left: takion and framepath both landed under PP27, and "
			"this tree IS their after-shape")
endif()

add_executable(chiaki-unit ${CHIAKI_UNIT_SOURCES})
add_test(NAME unit COMMAND chiaki-unit)

# PP760: whether the four frame-path suites are compiled in, which main.c reads.
#
# On the test target rather than in config.h, where CHIAKI_LIB_ENABLE_FFMPEG_DECODER lives: that one
# is set in lib/CMakeLists.txt, and PP623's shape gives this deletion exactly one commit that edits
# lib/. So the switch sits next to the list it has to agree with - the four files above and this line
# turn off together, in the commit that removes them.
#
# PP803: DERIVED FROM THE LIST, which is the --wrap block's own rule below and for the same reason.
# A trial that removed the four files and left this at 1 would compile main.c's guarded suites
# against sources that are not there, and the failure would be about the trial rather than about
# the flip. The flip's own commit removes the files and this line follows them.
if("frameprocessor.c" IN_LIST CHIAKI_UNIT_SOURCES)
	target_compile_definitions(chiaki-unit PRIVATE CHIAKI_UNIT_HAVE_FRAMEPATH=1)
else()
	target_compile_definitions(chiaki-unit PRIVATE CHIAKI_UNIT_HAVE_FRAMEPATH=0)
endif()

# PP786: and the same switch for takion's three, which PP780's link named and nothing guarded.
#
# takion.c and reorderqueue.c exercise the modules PP27's end state deletes; allocbudget.c is
# claimed by BOTH flips and leaves on whichever lands first, which is why main.c nests its guard
# inside the frame path's rather than beside it.
#
# The two switches decide their OWN files, so one flip landing does not make the other look
# half-finished: takion.c and reorderqueue.c answer for this one, fec.c, frameprocessor.c and
# videoreceiver.c for that one, and allocbudget.c answers for neither because it answers to both.
if("takion.c" IN_LIST CHIAKI_UNIT_SOURCES)
	target_compile_definitions(chiaki-unit PRIVATE CHIAKI_UNIT_HAVE_TAKION=1)
else()
	target_compile_definitions(chiaki-unit PRIVATE CHIAKI_UNIT_HAVE_TAKION=0)
endif()

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
