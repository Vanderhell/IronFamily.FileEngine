add_library(ironfamily_blake3 STATIC
    ../third_party/blake3/blake3.c
)
include(GNUInstallDirs)
target_include_directories(ironfamily_blake3
    PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/../third_party
)
set_target_properties(ironfamily_blake3 PROPERTIES EXPORT_NAME BLAKE3)

add_library(ironfamily_ed25519 STATIC
    ../third_party/ed25519/add_scalar.c
    ../third_party/ed25519/fe.c
    ../third_party/ed25519/ge.c
    ../third_party/ed25519/key_exchange.c
    ../third_party/ed25519/keypair.c
    ../third_party/ed25519/sc.c
    ../third_party/ed25519/seed.c
    ../third_party/ed25519/sha512.c
    ../third_party/ed25519/sign.c
    ../third_party/ed25519/verify.c
)
target_include_directories(ironfamily_ed25519
    PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/../third_party
)
set_target_properties(ironfamily_ed25519 PROPERTIES EXPORT_NAME ED25519)

add_library(ironfamily_iupd STATIC
    src/crc32.c
    src/delta2_apply.c
    src/diff_apply.c
    src/iupd_errors.c
    src/iupd_incremental_metadata.c
    src/iupd_reader.c
    src/ota_apply.c
)
target_include_directories(ironfamily_iupd
    PUBLIC
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
        $<INSTALL_INTERFACE:${CMAKE_INSTALL_INCLUDEDIR}>
    PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/..
        ${CMAKE_CURRENT_SOURCE_DIR}/../third_party
)
target_link_libraries(ironfamily_iupd
    PRIVATE
        ironfamily_blake3
        ironfamily_ed25519
)
set_target_properties(ironfamily_iupd PROPERTIES EXPORT_NAME IUPD)
ironfamily_apply_dev_options(ironfamily_iupd)
add_library(IronFamily::IUPD ALIAS ironfamily_iupd)

add_library(ironfamily_c INTERFACE)
target_link_libraries(ironfamily_c
    INTERFACE
        IronFamily::ICFG
        IronFamily::ILOG
        IronFamily::IUPD
)

add_custom_target(ironfamily_active_engines
    DEPENDS
        ironfamily_icfg
        ironfamily_ilog
        ironfamily_iupd
)

install(
    TARGETS
        ironfamily_blake3
        ironfamily_ed25519
        ironfamily_iupd
    EXPORT IronFamilyTargets
    ARCHIVE DESTINATION "${CMAKE_INSTALL_LIBDIR}"
    LIBRARY DESTINATION "${CMAKE_INSTALL_LIBDIR}"
    RUNTIME DESTINATION "${CMAKE_INSTALL_BINDIR}"
)

install(
    DIRECTORY "${CMAKE_CURRENT_SOURCE_DIR}/include/"
    DESTINATION "${CMAKE_INSTALL_INCLUDEDIR}"
    FILES_MATCHING PATTERN "*.h"
)
