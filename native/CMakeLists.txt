cmake_minimum_required(VERSION 3.20)
project(ironfamily_native C)

set(CMAKE_C_STANDARD 99)
set(CMAKE_C_STANDARD_REQUIRED ON)
set(CMAKE_C_EXTENSIONS OFF)

include(CTest)
enable_testing()

# Reuse the canonical C engine from libs/ironcfg-c to avoid divergent native copies.
if(EXISTS "${CMAKE_CURRENT_LIST_DIR}/../libs/ironcfg-c/CMakeLists.txt")
    set(SKIP_TESTS ON)
    add_subdirectory(
        "${CMAKE_CURRENT_LIST_DIR}/../libs/ironcfg-c"
        "${CMAKE_BINARY_DIR}/libs/ironcfg-c"
    )
    unset(SKIP_TESTS)
endif()

add_subdirectory(ironfamily_c)
add_subdirectory(tests)
