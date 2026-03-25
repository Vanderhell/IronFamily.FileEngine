add_library(ironfamily_c STATIC
    src/ironfamily_dummy.c
    src/iupd_errors.c
    src/iupd_reader.c
    src/iupd_incremental_metadata.c
    src/diff_apply.c
    src/delta2_apply.c
    src/crc32.c
    src/ota_apply.c
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
    ../third_party/blake3/blake3.c
)

target_include_directories(ironfamily_c
    PUBLIC
        ${CMAKE_CURRENT_SOURCE_DIR}/include
        ${CMAKE_CURRENT_SOURCE_DIR}/..
        ${CMAKE_CURRENT_SOURCE_DIR}/../third_party
)

# Prefer canonical implementation from libs/ironcfg-c when available.
if(TARGET ironcfg-c)
    target_link_libraries(ironfamily_c PUBLIC ironcfg-c)
else()
    target_sources(ironfamily_c PRIVATE
        src/ironcfg_common.c
        src/ironcfg_open.c
        src/ironcfg_validate.c
        src/ironcfg_encode.c
        src/ironcfg_view.c
    )
endif()

# Enable trace for debug builds (test-only)
if(CMAKE_BUILD_TYPE STREQUAL "Debug")
    target_compile_definitions(ironfamily_c PRIVATE IRONFAMILY_TRACE=1)
endif()
