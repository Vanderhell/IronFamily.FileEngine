cmake_minimum_required(VERSION 3.10)
project(bjv-c C)

set(CMAKE_C_STANDARD 99)
set(CMAKE_C_STANDARD_REQUIRED ON)

# BJV C Library
add_library(bjv-c STATIC
    src/bjv.c
)

target_include_directories(bjv-c PUBLIC
    ${CMAKE_CURRENT_SOURCE_DIR}/include
)

# Enable testing
enable_testing()

# BJV C Tests
add_executable(test_bjv_c tests/test_bjv_c.c)
target_link_libraries(test_bjv_c bjv-c)

add_test(NAME bjv_c_tests COMMAND test_bjv_c)
