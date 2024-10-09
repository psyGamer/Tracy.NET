// Exposes Tracy's C++ API via the C ABI

#include <Tracy/tracy/Tracy.hpp>

extern "C" 
{

TRACY_API void TracyCSetProgramName(const char* name) {
    TracySetProgramName(name);
}

}
