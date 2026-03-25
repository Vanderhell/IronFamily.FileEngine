cmake_minimum_required(VERSION 3.10)
project(ironcfg-cpp CXX)

set(CMAKE_CXX_STANDARD 11)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# Header-only library: just add include directory
add_library(ironcfg-cpp INTERFACE)

target_include_directories(ironcfg-cpp INTERFACE
    ${CMAKE_CURRENT_SOURCE_DIR}/include
)

# Link to C library for implementation
target_link_libraries(ironcfg-cpp INTERFACE ironcfg-c)

# Optional: example exe
if(NOT SKIP_TESTS)
    add_executable(example_icxs_cpp examples/icxs_example.cpp)
    target_link_libraries(example_icxs_cpp ironcfg-cpp ironcfg-c)
endif()
