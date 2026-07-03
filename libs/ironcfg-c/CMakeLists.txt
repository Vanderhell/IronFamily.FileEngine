cmake_minimum_required(VERSION 3.20)
project(ironcfg_c C)

set(CMAKE_C_STANDARD 99)
set(CMAKE_C_STANDARD_REQUIRED ON)
set(CMAKE_C_EXTENSIONS OFF)

option(IRONFAMILY_BUILD_LEGACY_CODECS "Build historical codecs and tests" OFF)
option(IRONFAMILY_BUILD_LEGACY_IUPD_COMPAT "Build legacy libs/ironcfg-c IUPD compatibility surface" OFF)

add_library(ironfamily_icfg STATIC
    src/ironcfg_common.c
    src/ironcfg_open.c
    src/ironcfg_validate.c
    src/ironcfg_encode.c
    src/ironcfg_view.c
)
target_include_directories(ironfamily_icfg
    PUBLIC
        ${CMAKE_CURRENT_SOURCE_DIR}/include
)
add_library(IronFamily::ICFG ALIAS ironfamily_icfg)

add_library(ironfamily_ilog STATIC
    src/ilog.c
)
target_include_directories(ironfamily_ilog
    PUBLIC
        ${CMAKE_CURRENT_SOURCE_DIR}/include
)
add_library(IronFamily::ILOG ALIAS ironfamily_ilog)

add_library(ironcfg-c INTERFACE)
target_link_libraries(ironcfg-c
    INTERFACE
        IronFamily::ICFG
        IronFamily::ILOG
)

if(IRONFAMILY_BUILD_LEGACY_IUPD_COMPAT)
    add_library(ironfamily_iupd_compat STATIC
        src/iupd.c
    )
    target_include_directories(ironfamily_iupd_compat
        PUBLIC
            ${CMAKE_CURRENT_SOURCE_DIR}/include
    )
endif()

if(IRONFAMILY_BUILD_LEGACY_CODECS)
    add_library(ironfamily_legacy_codecs STATIC
        src/icf2.c
        src/icfx.c
        src/icxs.c
    )
    target_include_directories(ironfamily_legacy_codecs
        PUBLIC
            ${CMAKE_CURRENT_SOURCE_DIR}/include
    )

    find_package(OpenSSL REQUIRED)
    add_library(ironfamily_legacy_bjx STATIC
        src/bjx.c
    )
    target_include_directories(ironfamily_legacy_bjx
        PUBLIC
            ${CMAKE_CURRENT_SOURCE_DIR}/include
    )
    target_link_libraries(ironfamily_legacy_bjx
        PUBLIC
            OpenSSL::Crypto
    )

    add_subdirectory(legacy/bjv-c)
endif()

add_executable(crc_diagnostic tools/crc_diagnostic.c)
target_link_libraries(crc_diagnostic PRIVATE IronFamily::ICFG)

add_executable(test_crc tools/test_crc.c)
target_link_libraries(test_crc PRIVATE IronFamily::ICFG)

if(IRONFAMILY_BUILD_LEGACY_CODECS)
    add_executable(sanity_check tools/sanity_check.c)
    target_link_libraries(sanity_check PRIVATE ironfamily_legacy_codecs)

    add_executable(icxs_diagnostic tools/icxs_diagnostic.c)
    target_link_libraries(icxs_diagnostic PRIVATE ironfamily_legacy_codecs)

    add_executable(parity_icf2 tools/parity_icf2.c)
    target_link_libraries(parity_icf2 PRIVATE ironfamily_legacy_codecs)
endif()

if(NOT SKIP_TESTS)
    enable_testing()

    add_executable(test_ironcfg tests/test_ironcfg.c)
    target_link_libraries(test_ironcfg PRIVATE IronFamily::ICFG)

    add_executable(test_ironcfg_determinism tests/test_ironcfg_determinism.c)
    target_link_libraries(test_ironcfg_determinism PRIVATE IronFamily::ICFG)

    add_executable(test_ilog_basic tests/test_ilog_basic.c)
    target_link_libraries(test_ilog_basic PRIVATE IronFamily::ILOG)

    add_executable(test_ilog_validation tests/test_ilog_validation.c)
    target_link_libraries(test_ilog_validation PRIVATE IronFamily::ILOG)

    add_test(NAME test_ironcfg COMMAND test_ironcfg)
    add_test(NAME test_ironcfg_determinism COMMAND test_ironcfg_determinism)
    add_test(NAME test_ilog_basic COMMAND test_ilog_basic)
    add_test(NAME test_ilog_validation COMMAND test_ilog_validation)

    if(IRONFAMILY_BUILD_LEGACY_IUPD_COMPAT)
        add_executable(test_iupd_basic tests/test_iupd_basic.c)
        target_link_libraries(test_iupd_basic PRIVATE ironfamily_iupd_compat)

        add_executable(test_iupd_validation tests/test_iupd_validation.c)
        target_link_libraries(test_iupd_validation PRIVATE ironfamily_iupd_compat)

        add_executable(test_iupd_apply tests/test_iupd_apply.c)
        target_link_libraries(test_iupd_apply PRIVATE ironfamily_iupd_compat)

        add_test(NAME test_iupd_basic COMMAND test_iupd_basic)
        add_test(NAME test_iupd_validation COMMAND test_iupd_validation)
        add_test(NAME test_iupd_apply COMMAND test_iupd_apply)
    endif()

    if(IRONFAMILY_BUILD_LEGACY_CODECS)
        add_executable(test_icxs_basic tests/test_icxs_basic.c)
        target_link_libraries(test_icxs_basic PRIVATE ironfamily_legacy_codecs)

        add_executable(test_icxs_golden tests/test_icxs_golden.c)
        target_link_libraries(test_icxs_golden PRIVATE ironfamily_legacy_codecs)

        add_executable(test_icfx_golden tests/test_icfx_golden.c)
        target_link_libraries(test_icfx_golden PRIVATE ironfamily_legacy_codecs)

        add_executable(test_icfx_vsp_indexed tests/test_icfx_vsp_indexed.c)
        target_link_libraries(test_icfx_vsp_indexed PRIVATE ironfamily_legacy_codecs)

        add_executable(test_icf2_golden tests/test_icf2_golden.c)
        target_link_libraries(test_icf2_golden PRIVATE ironfamily_legacy_codecs)

        add_executable(test_bjx_basic tests/test_bjx_basic.c)
        target_link_libraries(test_bjx_basic PRIVATE ironfamily_legacy_bjx)

        add_executable(test_bjx_validation tests/test_bjx_validation.c)
        target_link_libraries(test_bjx_validation PRIVATE ironfamily_legacy_bjx)

        add_test(NAME test_icxs_basic COMMAND test_icxs_basic)
        add_test(NAME test_icxs_golden COMMAND test_icxs_golden WORKING_DIRECTORY ${CMAKE_SOURCE_DIR}/../..)
        add_test(NAME test_icfx_golden COMMAND test_icfx_golden WORKING_DIRECTORY ${CMAKE_SOURCE_DIR}/../..)
        add_test(NAME test_icfx_vsp_indexed COMMAND test_icfx_vsp_indexed WORKING_DIRECTORY ${CMAKE_SOURCE_DIR}/../..)
        add_test(NAME test_icf2_golden COMMAND test_icf2_golden WORKING_DIRECTORY ${CMAKE_SOURCE_DIR}/../..)
        add_test(NAME test_bjx_basic COMMAND test_bjx_basic)
        add_test(NAME test_bjx_validation COMMAND test_bjx_validation WORKING_DIRECTORY ${CMAKE_SOURCE_DIR}/../..)
    endif()
endif()
