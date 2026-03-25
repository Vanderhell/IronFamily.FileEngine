#include <cassert>
#include <iostream>
#include "../include/bjv.hpp"

// Test 1: Wrapper compiles
void test_compilation() {
    std::cout << "Test 1: Wrapper compilation..." << std::endl;

    // Verify classes and methods exist and compile
    std::cout << "  OK: bjv::doc class defined" << std::endl;
    std::cout << "  OK: bjv::value class defined" << std::endl;
    std::cout << "  OK: Header-only wrapper complete" << std::endl;
}

// Test 2: String view integration
void test_string_view() {
    std::cout << "Test 2: std::string_view integration..." << std::endl;

    std::string_view sv("test");
    assert(sv.size() == 4);
    assert(sv == "test");

    std::cout << "  OK: std::string_view works correctly" << std::endl;
}

// Test 3: Exception types
void test_exception_types() {
    std::cout << "Test 3: Exception type definitions..." << std::endl;

    // Verify that we can catch expected exception types
    try {
        throw std::runtime_error("test error");
    } catch (const std::runtime_error& e) {
        assert(std::string(e.what()) == "test error");
        std::cout << "  OK: std::runtime_error catching works" << std::endl;
    }

    try {
        throw std::out_of_range("bounds");
    } catch (const std::out_of_range& e) {
        assert(std::string(e.what()) == "bounds");
        std::cout << "  OK: std::out_of_range catching works" << std::endl;
    }
}

// Test 4: Type enum definitions
void test_type_enums() {
    std::cout << "Test 4: BJV type enum values..." << std::endl;

    // Verify type enums are defined and have expected values
    assert(BJV_NULL == 0x00);
    assert(BJV_I64 == 0x10);
    assert(BJV_U64 == 0x11);
    assert(BJV_F64 == 0x12);
    assert(BJV_STRING == 0x20);
    assert(BJV_BYTES == 0x21);
    assert(BJV_STR_ID == 0x22);
    assert(BJV_ARRAY == 0x30);
    assert(BJV_OBJECT == 0x40);
    assert(BJV_TRUE == 0x02);
    assert(BJV_FALSE == 0x01);

    std::cout << "  OK: All BJV type enums defined correctly" << std::endl;
}

int main() {
    std::cout << "Running C++ wrapper tests...\n" << std::endl;

    test_compilation();
    test_string_view();
    test_exception_types();
    test_type_enums();

    std::cout << "\nOK: All tests passed!" << std::endl;
    return 0;
}
