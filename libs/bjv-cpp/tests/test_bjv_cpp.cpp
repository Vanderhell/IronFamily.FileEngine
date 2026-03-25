#include "../include/bjv.hpp"
#include <iostream>

int main() {
    std::cout << "BJV C++ tests\n";

    std::vector<uint8_t> test_data = {0x01};
    bjv::BjvReader reader(test_data);

    if (reader.validate()) {
        std::cout << "PASS: BjvReader initialization\n";
        return 0;
    }

    std::cout << "FAIL: BjvReader initialization\n";
    return 1;
}
