cmake_minimum_required(VERSION 3.15)
project(bjv-cpp)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# Include directories
include_directories(include)
include_directories(../bjv-c/include)

# Add C library
add_library(bjv-core
    ../bjv-c/src/bjv.c
)

# Platform-specific compiler flags
if(MSVC)
    target_compile_options(bjv-core PRIVATE /W4)
else()
    target_compile_options(bjv-core PRIVATE -Wall -Wextra -Werror=implicit-function-declaration)
endif()

# Test executable
add_executable(test_wrapper
    tests/test_wrapper.cpp
)
target_link_libraries(test_wrapper bjv-core)
target_include_directories(test_wrapper PRIVATE include ../bjv-c/include)

# Enable testing
enable_testing()
add_test(NAME wrapper_tests COMMAND test_wrapper)
