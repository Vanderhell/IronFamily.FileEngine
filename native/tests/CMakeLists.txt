set(NATIVE_REPO_ROOT "${CMAKE_SOURCE_DIR}/..")

add_executable(test_iupd_vectors
    test_iupd_vectors.c
    file_reader.c
)
target_link_libraries(test_iupd_vectors PRIVATE IronFamily::IUPD)

add_executable(derive_pubkey
    derive_pubkey.c
)
target_link_libraries(derive_pubkey PRIVATE IronFamily::IUPD)

add_executable(test_diff_vectors
    test_diff_vectors.c
    file_reader.c
    file_writer.c
)
target_link_libraries(test_diff_vectors PRIVATE IronFamily::IUPD)

add_executable(test_ota_bundle
    test_ota_bundle.c
    file_reader.c
    file_writer.c
)
target_link_libraries(test_ota_bundle PRIVATE IronFamily::IUPD)

add_executable(test_delta2_vectors
    test_delta2_vectors.c
    file_reader.c
    file_writer.c
)
target_link_libraries(test_delta2_vectors PRIVATE IronFamily::IUPD)

add_executable(test_crc32_kat
    test_crc32_kat.c
)
target_link_libraries(test_crc32_kat PRIVATE IronFamily::IUPD)

add_executable(test_incremental_metadata
    test_incremental_metadata.c
)
target_link_libraries(test_incremental_metadata PRIVATE IronFamily::IUPD)

add_executable(test_incremental_vectors
    test_incremental_vectors.c
    file_reader.c
    file_writer.c
)
target_link_libraries(test_incremental_vectors PRIVATE IronFamily::IUPD)

add_executable(test_iupd_security
    test_iupd_security.c
)
target_link_libraries(test_iupd_security PRIVATE IronFamily::IUPD)

add_executable(test_patch2_crc32
    test_patch2_crc32.c
)
target_link_libraries(test_patch2_crc32 PRIVATE IronFamily::IUPD)

add_executable(test_ironcfg
    test_ironcfg.c
)
target_link_libraries(test_ironcfg PRIVATE IronFamily::ICFG)
target_include_directories(test_ironcfg PRIVATE ${CMAKE_SOURCE_DIR}/../libs/ironcfg-c/include)

add_executable(test_ironcfg_determinism
    test_ironcfg_determinism.c
)
target_link_libraries(test_ironcfg_determinism PRIVATE IronFamily::ICFG)
target_include_directories(test_ironcfg_determinism PRIVATE ${CMAKE_SOURCE_DIR}/../libs/ironcfg-c/include)
if(UNIX)
    target_link_libraries(test_ironcfg_determinism PRIVATE m)
endif()

add_executable(test_icfg_golden_vectors
    test_icfg_golden_vectors.c
)
target_link_libraries(test_icfg_golden_vectors PRIVATE IronFamily::ICFG)
target_include_directories(test_icfg_golden_vectors PRIVATE ${CMAKE_SOURCE_DIR}/../libs/ironcfg-c/include)

add_executable(test_icfg_debug
    test_icfg_debug.c
)
target_link_libraries(test_icfg_debug PRIVATE IronFamily::ICFG)
target_include_directories(test_icfg_debug PRIVATE ${CMAKE_SOURCE_DIR}/../libs/ironcfg-c/include)

add_executable(smoke_icfg_consumer
    smoke_icfg_consumer.c
)
target_link_libraries(smoke_icfg_consumer PRIVATE IronFamily::ICFG)

add_executable(smoke_ilog_consumer
    smoke_ilog_consumer.c
)
target_link_libraries(smoke_ilog_consumer PRIVATE IronFamily::ILOG)

add_executable(smoke_iupd_consumer
    smoke_iupd_consumer.c
)
target_link_libraries(smoke_iupd_consumer PRIVATE IronFamily::IUPD)

add_executable(smoke_public_headers_cpp
    smoke_public_headers_cpp.cpp
)
target_link_libraries(smoke_public_headers_cpp PRIVATE IronFamily::ICFG IronFamily::ILOG IronFamily::IUPD)

target_compile_definitions(test_iupd_vectors PRIVATE IRONFAMILY_TRACE=1)

add_test(NAME test_iupd_vectors COMMAND $<TARGET_FILE:test_iupd_vectors>)
add_test(NAME test_diff_vectors COMMAND $<TARGET_FILE:test_diff_vectors>)
add_test(NAME test_ota_bundle COMMAND $<TARGET_FILE:test_ota_bundle>)
add_test(NAME test_delta2_vectors COMMAND $<TARGET_FILE:test_delta2_vectors>)
add_test(NAME test_incremental_metadata COMMAND $<TARGET_FILE:test_incremental_metadata>)
add_test(NAME test_incremental_vectors COMMAND $<TARGET_FILE:test_incremental_vectors>)
add_test(NAME test_iupd_security COMMAND $<TARGET_FILE:test_iupd_security>)
add_test(NAME test_ironcfg COMMAND $<TARGET_FILE:test_ironcfg>)
add_test(NAME test_ironcfg_determinism COMMAND $<TARGET_FILE:test_ironcfg_determinism>)
add_test(NAME test_icfg_golden_vectors COMMAND $<TARGET_FILE:test_icfg_golden_vectors>)
add_test(NAME prepare_vectors COMMAND dotnet run -c Release --project tools/IronFamily.Vectors -- --out artifacts/vectors/v1 --force)
add_test(NAME prepare_icfg_vectors COMMAND dotnet test -c Release libs/ironconfig-dotnet/tests/IronConfig.IronCfgTests/IronConfig.IronCfgTests.csproj --filter FullyQualifiedName~GenerateAllVectors -v minimal)

set_tests_properties(prepare_icfg_vectors PROPERTIES DEPENDS prepare_vectors)
set_tests_properties(test_iupd_vectors PROPERTIES DEPENDS prepare_vectors)
set_tests_properties(test_diff_vectors PROPERTIES DEPENDS prepare_vectors)
set_tests_properties(test_ota_bundle PROPERTIES DEPENDS prepare_vectors)
set_tests_properties(test_delta2_vectors PROPERTIES DEPENDS prepare_vectors)
set_tests_properties(test_iupd_security PROPERTIES DEPENDS prepare_vectors)
set_tests_properties(test_icfg_golden_vectors PROPERTIES DEPENDS prepare_icfg_vectors)

set_tests_properties(
    prepare_vectors
    prepare_icfg_vectors
    test_iupd_vectors
    test_diff_vectors
    test_ota_bundle
    test_delta2_vectors
    test_incremental_metadata
    test_incremental_vectors
    test_iupd_security
    test_ironcfg
    test_ironcfg_determinism
    test_icfg_golden_vectors
    PROPERTIES
    WORKING_DIRECTORY "${NATIVE_REPO_ROOT}"
)
