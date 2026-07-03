add_library(ironfamily_blake3 STATIC
    ../third_party/blake3/blake3.c
)
target_include_directories(ironfamily_blake3
    PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/../third_party
)

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
        ${CMAKE_CURRENT_SOURCE_DIR}/include
    PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/..
        ${CMAKE_CURRENT_SOURCE_DIR}/../third_party
)
target_link_libraries(ironfamily_iupd
    PRIVATE
        ironfamily_blake3
        ironfamily_ed25519
)
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
