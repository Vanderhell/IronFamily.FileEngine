set(NATIVE_REPO_ROOT "${CMAKE_SOURCE_DIR}/..")

add_executable(ironfamily_c_tests
    test_dummy.c
)

add_executable(test_iupd_vectors
    test_iupd_vectors.c
    file_reader.c
)

add_executable(derive_pubkey
    derive_pubkey.c
)

add_executable(test_diff_vectors
    test_diff_vectors.c
    file_reader.c
    file_writer.c
)

add_executable(test_ota_bundle
    test_ota_bundle.c
    file_reader.c
    file_writer.c
)

add_executable(test_delta2_vectors
    test_delta2_vectors.c
    file_reader.c
    file_writer.c
)

add_executable(test_crc32_kat
    test_crc32_kat.c
)

add_executable(test_incremental_metadata
    test_incremental_metadata.c
)

add_executable(test_incremental_vectors
    test_incremental_vectors.c
    file_reader.c
    file_writer.c
)

target_link_libraries(ironfamily_c_tests
    PRIVATE
        ironfamily_c
)

target_link_libraries(test_iupd_vectors
    PRIVATE
        ironfamily_c
)

target_link_libraries(derive_pubkey
    PRIVATE
        ironfamily_c
)

target_link_libraries(test_diff_vectors
    PRIVATE
        ironfamily_c
)

target_link_libraries(test_ota_bundle
    PRIVATE
        ironfamily_c
)

target_link_libraries(test_delta2_vectors
    PRIVATE
        ironfamily_c
)

target_link_libraries(test_crc32_kat
    PRIVATE
        ironfamily_c
)

target_link_libraries(test_incremental_metadata
    PRIVATE
        ironfamily_c
)

target_link_libraries(test_incremental_vectors
    PRIVATE
        ironfamily_c
)

target_include_directories(ironfamily_c_tests
    PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/../ironfamily_c/include
)

target_include_directories(test_iupd_vectors
    PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/../ironfamily_c/include
)

target_include_directories(test_diff_vectors
    PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/../ironfamily_c/include
)

target_include_directories(test_ota_bundle
    PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/../ironfamily_c/include
)

target_include_directories(test_delta2_vectors
    PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/../ironfamily_c/include
)

target_include_directories(test_crc32_kat
    PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/../ironfamily_c/include
)

target_include_directories(test_incremental_metadata
    PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/../ironfamily_c/include
)

target_include_directories(test_incremental_vectors
    PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/../ironfamily_c/include
)

# Enable trace logging for test executable
target_compile_definitions(test_iupd_vectors PRIVATE IRONFAMILY_TRACE=1)

add_test(NAME ironfamily_c_tests COMMAND $<TARGET_FILE:ironfamily_c_tests>)
add_test(NAME test_iupd_vectors COMMAND $<TARGET_FILE:test_iupd_vectors>)
add_test(NAME test_diff_vectors COMMAND $<TARGET_FILE:test_diff_vectors>)
add_test(NAME test_ota_bundle COMMAND $<TARGET_FILE:test_ota_bundle>)
add_test(NAME test_delta2_vectors COMMAND $<TARGET_FILE:test_delta2_vectors>)
add_test(NAME test_incremental_metadata COMMAND $<TARGET_FILE:test_incremental_metadata>)
add_test(NAME test_incremental_vectors COMMAND $<TARGET_FILE:test_incremental_vectors>)
add_executable(test_icfg_golden_vectors
    test_icfg_golden_vectors.c
)

add_executable(test_icfg_debug
    test_icfg_debug.c
)

target_link_libraries(test_icfg_golden_vectors
    PRIVATE
        ironfamily_c
)

target_include_directories(test_icfg_golden_vectors
    PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/../ironfamily_c/include
)

target_link_libraries(test_icfg_debug
    PRIVATE
        ironfamily_c
)

target_include_directories(test_icfg_debug
    PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/../ironfamily_c/include
)

add_test(NAME test_ironcfg COMMAND $<TARGET_FILE:test_ironcfg>)
add_test(NAME test_ironcfg_determinism COMMAND $<TARGET_FILE:test_ironcfg_determinism>)
add_test(NAME test_icfg_golden_vectors COMMAND $<TARGET_FILE:test_icfg_golden_vectors>)
add_test(
    NAME prepare_vectors
    COMMAND dotnet run -c Release --project tools/IronFamily.Vectors -- --out artifacts/vectors/v1 --force
)
add_test(
    NAME prepare_icfg_vectors
    COMMAND dotnet test -c Release libs/ironconfig-dotnet/tests/IronConfig.IronCfgTests/IronConfig.IronCfgTests.csproj --filter FullyQualifiedName~GenerateAllVectors -v minimal
)

set_tests_properties(prepare_icfg_vectors PROPERTIES DEPENDS prepare_vectors)
set_tests_properties(test_iupd_vectors PROPERTIES DEPENDS prepare_vectors)
set_tests_properties(test_diff_vectors PROPERTIES DEPENDS prepare_vectors)
set_tests_properties(test_ota_bundle PROPERTIES DEPENDS prepare_vectors)
set_tests_properties(test_delta2_vectors PROPERTIES DEPENDS prepare_vectors)
set_tests_properties(test_icfg_golden_vectors PROPERTIES DEPENDS prepare_icfg_vectors)

set_tests_properties(
    prepare_vectors
    prepare_icfg_vectors
    ironfamily_c_tests
    test_iupd_vectors
    test_diff_vectors
    test_ota_bundle
    test_delta2_vectors
    test_incremental_metadata
    test_incremental_vectors
    test_ironcfg
    test_ironcfg_determinism
    test_icfg_golden_vectors
    PROPERTIES
    WORKING_DIRECTORY "${NATIVE_REPO_ROOT}"
)

add_executable(test_patch2_crc32
    test_patch2_crc32.c
)

target_link_libraries(test_patch2_crc32
    PRIVATE
        ironfamily_c
)

target_include_directories(test_patch2_crc32
    PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/../ironfamily_c/include
)

# ICFG C tests
add_executable(test_ironcfg
    test_ironcfg.c
)

target_link_libraries(test_ironcfg
    PRIVATE
        ironfamily_c
)

target_include_directories(test_ironcfg
    PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/../ironfamily_c/include
)

add_executable(test_ironcfg_determinism
    test_ironcfg_determinism.c
)

target_link_libraries(test_ironcfg_determinism
    PRIVATE
        ironfamily_c
)

if(UNIX)
    target_link_libraries(test_ironcfg_determinism PRIVATE m)
endif()

target_include_directories(test_ironcfg_determinism
    PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/../ironfamily_c/include
)
