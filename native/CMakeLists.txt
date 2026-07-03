cmake_minimum_required(VERSION 3.20)
project(ironfamily_native C CXX)

set(CMAKE_C_STANDARD 99)
set(CMAKE_C_STANDARD_REQUIRED ON)
set(CMAKE_C_EXTENSIONS OFF)
set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

include("${CMAKE_CURRENT_LIST_DIR}/../cmake/IronFamilyDevOptions.cmake")

include(CTest)
enable_testing()

if(NOT EXISTS "${CMAKE_CURRENT_LIST_DIR}/../libs/ironcfg-c/CMakeLists.txt")
    message(FATAL_ERROR "Missing libs/ironcfg-c canonical C99 implementation; silent native fallback is not allowed.")
endif()

set(SKIP_TESTS ON)
add_subdirectory(
    "${CMAKE_CURRENT_LIST_DIR}/../libs/ironcfg-c"
    "${CMAKE_BINARY_DIR}/libs/ironcfg-c"
)
unset(SKIP_TESTS)

add_subdirectory(ironfamily_c)
add_subdirectory(tests)
