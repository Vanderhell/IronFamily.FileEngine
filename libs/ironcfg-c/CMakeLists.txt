cmake_minimum_required(VERSION 3.10)
project(ironcfg-c C)

set(CMAKE_C_STANDARD 99)
set(CMAKE_C_STANDARD_REQUIRED ON)

# OpenSSL is required for BJX
find_package(OpenSSL REQUIRED)

# Library
add_library(ironcfg-c STATIC
    src/ironcfg_common.c
    src/ironcfg_open.c
    src/ironcfg_validate.c
    src/ironcfg_encode.c
    src/icxs.c
    src/icfx.c
    src/icf2.c
    src/ilog.c
    src/iupd.c
    src/bjx.c
)

target_include_directories(ironcfg-c PUBLIC
    ${CMAKE_CURRENT_SOURCE_DIR}/include
    ${CMAKE_CURRENT_SOURCE_DIR}/include/ironcfg
)

target_link_libraries(ironcfg-c PUBLIC OpenSSL::Crypto)

# Optional: test executables
if(NOT SKIP_TESTS)
    add_executable(test_ironcfg tests/test_ironcfg.c)
    target_link_libraries(test_ironcfg ironcfg-c)

    add_executable(test_ironcfg_determinism tests/test_ironcfg_determinism.c)
    target_link_libraries(test_ironcfg_determinism ironcfg-c)

    add_executable(test_icxs_basic tests/test_icxs_basic.c)
    target_link_libraries(test_icxs_basic ironcfg-c)

    add_executable(test_icxs_golden tests/test_icxs_golden.c)
    target_link_libraries(test_icxs_golden ironcfg-c)

    add_executable(test_icfx_golden tests/test_icfx_golden.c)
    target_link_libraries(test_icfx_golden ironcfg-c)

    add_executable(test_icfx_vsp_indexed tests/test_icfx_vsp_indexed.c)
    target_link_libraries(test_icfx_vsp_indexed ironcfg-c)

    add_executable(test_icf2_golden tests/test_icf2_golden.c)
    target_link_libraries(test_icf2_golden ironcfg-c)

    add_executable(test_iupd_basic tests/test_iupd_basic.c)
    target_link_libraries(test_iupd_basic ironcfg-c)

    add_executable(test_iupd_validation tests/test_iupd_validation.c)
    target_link_libraries(test_iupd_validation ironcfg-c)

    add_executable(test_iupd_apply tests/test_iupd_apply.c)
    target_link_libraries(test_iupd_apply ironcfg-c)

    add_executable(test_bjx_basic tests/test_bjx_basic.c)
    target_link_libraries(test_bjx_basic ironcfg-c)

    add_executable(test_bjx_validation tests/test_bjx_validation.c)
    target_link_libraries(test_bjx_validation ironcfg-c)

    enable_testing()
    add_test(NAME test_ironcfg COMMAND test_ironcfg)
    add_test(NAME test_ironcfg_determinism COMMAND test_ironcfg_determinism)
    add_test(NAME test_icxs_basic COMMAND test_icxs_basic)
    add_test(NAME test_icxs_golden COMMAND test_icxs_golden WORKING_DIRECTORY ${CMAKE_SOURCE_DIR}/../..)
    add_test(NAME test_icfx_golden COMMAND test_icfx_golden WORKING_DIRECTORY ${CMAKE_SOURCE_DIR}/../..)
    add_test(NAME test_icfx_vsp_indexed COMMAND test_icfx_vsp_indexed WORKING_DIRECTORY ${CMAKE_SOURCE_DIR}/../..)
    add_test(NAME test_icf2_golden COMMAND test_icf2_golden WORKING_DIRECTORY ${CMAKE_SOURCE_DIR}/../..)
    add_test(NAME test_iupd_basic COMMAND test_iupd_basic)
    add_test(NAME test_iupd_validation COMMAND test_iupd_validation)
    add_test(NAME test_iupd_apply COMMAND test_iupd_apply)
    add_test(NAME test_bjx_basic COMMAND test_bjx_basic)
    add_test(NAME test_bjx_validation COMMAND test_bjx_validation WORKING_DIRECTORY ${CMAKE_SOURCE_DIR}/../..)
endif()

# Diagnostic tools
add_executable(crc_diagnostic tools/crc_diagnostic.c)
target_link_libraries(crc_diagnostic ironcfg-c)

add_executable(test_crc tools/test_crc.c)
target_link_libraries(test_crc ironcfg-c)

add_executable(sanity_check tools/sanity_check.c)
target_link_libraries(sanity_check ironcfg-c)

add_executable(icxs_diagnostic tools/icxs_diagnostic.c)
target_link_libraries(icxs_diagnostic ironcfg-c)

add_executable(parity_icf2 tools/parity_icf2.c)
target_link_libraries(parity_icf2 ironcfg-c)

# Legacy BJV-C Library (frozen, for backward compatibility)
add_subdirectory(legacy/bjv-c)
