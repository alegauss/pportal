
if(NOT CHIAKI_USE_SYSTEM_NANOPB)
	##################
	# nanopb
	##################

	add_definitions(-DPB_C99_STATIC_ASSERT) # Fix PB_STATIC_ASSERT on msvc without using C11 for now
	add_subdirectory(nanopb EXCLUDE_FROM_ALL)
	set(NANOPB_GENERATOR_PY "${CMAKE_CURRENT_SOURCE_DIR}/nanopb/generator/nanopb_generator.py" PARENT_SCOPE)
	add_library(nanopb INTERFACE)
	target_link_libraries(nanopb INTERFACE protobuf-nanopb-static)
	target_include_directories(nanopb INTERFACE "${CMAKE_CURRENT_SOURCE_DIR}/nanopb")
	target_compile_definitions(nanopb INTERFACE -DPB_C99_STATIC_ASSERT) # see above
	add_library(Nanopb::nanopb ALIAS nanopb)
endif()

# PP30: gf-complete and jerasure are gone - sixteen vendored C files that built two static
# libraries so that a lost video packet could be reconstructed.
#
# Eleven gf-complete sources built the Galois field arithmetic for five word sizes, of which this
# tree used one: GF(2^8), the field CHIAKI_FEC_WORDSIZE names. Five jerasure sources sat on it, of
# which the port reached two functions - cauchy_original_coding_matrix and jerasure_matrix_decode.
#
# The replacement is three managed classes and it was written before this line was touched, which
# is PP623's order: GaloisField builds the two log tables (PP286), FecMatrix the Cauchy matrix
# (PP286), FecCodec the encode and decode over them (PP287). Every one of the three was held
# against this library's own answers while it was still here, and PP842 recorded them into
# tests/oracles/framepath.json so the comparison outlives the C.
#
# THE SUBMODULES STAY IN THE CHECKOUT, unbuilt, which is the choice PP33 made for holepunch.c and
# PP632 for gui/: what a deletion owes is that nothing compiles or links it, and a directory git
# already has costs nothing to leave. .gitmodules still declares both, and ForeignBinaries does not
# walk third-party/ for exactly this reason.

# PP33: curl is gone. holepunch.c was the only thing in lib/ that called it, and building curl
# from source was the slowest step this tree had.

if(CHIAKI_ENABLE_STEAM_SHORTCUT)
	add_subdirectory(cpp-steam-tools)
endif()
