
# PP4: the managed seam. A shared library, because that is the whole point of it - chiaki-lib is
# a static archive with no exported symbols, and this is what gives the port something to call.
add_library(chiaki-shim SHARED chiaki_shim.c chiaki_shim.h)

target_include_directories(chiaki-shim PUBLIC "${CMAKE_CURRENT_SOURCE_DIR}")
target_link_libraries(chiaki-shim chiaki-lib)

# chiaki-shim.dll and not libchiaki-shim.dll. The name is what a DllImport carries, and a managed
# assembly that has to know it was built by MinGW is a seam that leaked.
set_target_properties(chiaki-shim PROPERTIES PREFIX "" OUTPUT_NAME "chiaki-shim")

# The compiler runtime goes inside, which removes the MinGW DLLs from what has to travel with
# this one. It does NOT make the DLL free-standing, and the first version of this comment said it
# did: chiaki-lib is linked whole-object, so OpenSSL arrived with chiaki_error_string and the
# session functions have since brought curl and the rest with them. chiaki-shim.dll therefore
# imports what the Qt client imports, and the answer is the same one the Qt client has - it is
# copied into the portable tree that already holds those DLLs, and the managed host loads it from
# there rather than from beside its own assembly (see the resolver in app\Native\ChiakiNative.cs).
if(MINGW)
	target_link_options(chiaki-shim PRIVATE
			-static-libgcc
			-Wl,-Bstatic,--whole-archive -lwinpthread -Wl,--no-whole-archive,-Bdynamic)
endif()
