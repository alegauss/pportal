
# PP4: the managed seam. A shared library, because that is the whole point of it - chiaki-lib is
# a static archive with no exported symbols, and this is what gives the port something to call.
add_library(chiaki-shim SHARED chiaki_shim.c chiaki_shim.h)

target_include_directories(chiaki-shim PUBLIC "${CMAKE_CURRENT_SOURCE_DIR}")
target_link_libraries(chiaki-shim chiaki-lib)

# chiaki-shim.dll and not libchiaki-shim.dll. The name is what a DllImport carries, and a managed
# assembly that has to know it was built by MinGW is a seam that leaked.
set_target_properties(chiaki-shim PROPERTIES PREFIX "" OUTPUT_NAME "chiaki-shim")

# The compiler runtime goes inside. Everything exported so far is pure - no sockets, no OpenSSL,
# no threads - so the DLL can be self-contained, and a self-contained DLL loads beside a managed
# assembly with nothing from MSYS2 on PATH. That is what lets the .NET host's selftest call it on
# a machine that never opens an MSYS2 shell.
#
# It stops being true the first time a session function is exported here. That is a deployment
# decision, and it is filed rather than pre-empted.
if(MINGW)
	target_link_options(chiaki-shim PRIVATE
			-static-libgcc
			-Wl,-Bstatic,--whole-archive -lwinpthread -Wl,--no-whole-archive,-Bdynamic)
endif()
