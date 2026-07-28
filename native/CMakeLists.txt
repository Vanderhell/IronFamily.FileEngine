cmake_minimum_required(VERSION 3.20)
project(ironfamily_native C CXX)

set(CMAKE_C_STANDARD 99)
set(CMAKE_C_STANDARD_REQUIRED ON)
set(CMAKE_C_EXTENSIONS OFF)
set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)
set(IRONFAMILY_PACKAGE_VERSION 0.1.1)

include("${CMAKE_CURRENT_LIST_DIR}/../cmake/IronFamilyDevOptions.cmake")
include(GNUInstallDirs)
include(CMakePackageConfigHelpers)

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

set(IRONFAMILY_CONFIG_INSTALL_DIR "${CMAKE_INSTALL_LIBDIR}/cmake/IronFamily")

configure_package_config_file(
    "${CMAKE_CURRENT_LIST_DIR}/../cmake/IronFamilyConfig.cmake.in"
    "${CMAKE_CURRENT_BINARY_DIR}/IronFamilyConfig.cmake"
    INSTALL_DESTINATION "${IRONFAMILY_CONFIG_INSTALL_DIR}"
)

write_basic_package_version_file(
    "${CMAKE_CURRENT_BINARY_DIR}/IronFamilyConfigVersion.cmake"
    VERSION "${IRONFAMILY_PACKAGE_VERSION}"
    COMPATIBILITY SameMinorVersion
)

install(
    FILES
        "${CMAKE_CURRENT_BINARY_DIR}/IronFamilyConfig.cmake"
        "${CMAKE_CURRENT_BINARY_DIR}/IronFamilyConfigVersion.cmake"
    DESTINATION "${IRONFAMILY_CONFIG_INSTALL_DIR}"
)

install(
    EXPORT IronFamilyTargets
    NAMESPACE IronFamily::
    DESTINATION "${IRONFAMILY_CONFIG_INSTALL_DIR}"
)
