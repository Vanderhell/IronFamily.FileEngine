#include "../include/bjv.hpp"

namespace bjv {

BjvReader::BjvReader(const std::vector<uint8_t>& data)
    : data_(data)
{
}

bool BjvReader::validate()
{
    return !data_.empty();
}

} /* namespace bjv */
