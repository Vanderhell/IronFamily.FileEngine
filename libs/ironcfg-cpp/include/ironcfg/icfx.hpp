/*
 * icfx.hpp
 * ICFX C++ wrapper (header-only)
 *
 * Status: Production-ready
 * License: MIT
 */

#ifndef IRONCFG_ICFX_HPP
#define IRONCFG_ICFX_HPP

#include <ironcfg/icfx.h>
#include <stdexcept>
#include <string>
#include <cstring>
#include <cmath>

namespace ironcfg {

class IcfxException : public std::exception {
public:
    explicit IcfxException(icfg_status_t status) : status_(status) {}

    const char* what() const noexcept override {
        switch (status_) {
            case ICFG_OK: return "OK";
            case ICFG_ERR_MAGIC: return "Invalid magic";
            case ICFG_ERR_BOUNDS: return "Out of bounds";
            case ICFG_ERR_CRC: return "CRC mismatch";
            case ICFG_ERR_TYPE: return "Type mismatch";
            case ICFG_ERR_RANGE: return "Out of range";
            default: return "Unknown error";
        }
    }

    icfg_status_t status() const { return status_; }

private:
    icfg_status_t status_;
};

/**
 * ICFX Value wrapper
 */
class IcfxValue {
public:
    explicit IcfxValue(const icfx_value_t& val) : value_(val) {}

    bool isNull() const { return icfx_is_null(&value_); }
    bool isBool() const { return icfx_is_bool(&value_); }
    bool isNumber() const { return icfx_is_number(&value_); }
    bool isString() const { return icfx_is_string(&value_); }
    bool isArray() const { return icfx_is_array(&value_); }
    bool isObject() const { return icfx_is_object(&value_); }

    bool asBoolean() const {
        return icfx_get_bool(&value_);
    }

    int64_t asInt64() const {
        int64_t value;
        auto status = icfx_get_i64(&value_, &value);
        if (status != ICFG_OK) throw IcfxException(status);
        return value;
    }

    uint64_t asUInt64() const {
        uint64_t value;
        auto status = icfx_get_u64(&value_, &value);
        if (status != ICFG_OK) throw IcfxException(status);
        return value;
    }

    double asDouble() const {
        double value;
        auto status = icfx_get_f64(&value_, &value);
        if (status != ICFG_OK) throw IcfxException(status);
        return value;
    }

    std::string asString() const {
        const uint8_t* ptr;
        uint32_t len;
        auto status = icfx_get_str(&value_, &ptr, &len);
        if (status != ICFG_OK) throw IcfxException(status);
        return std::string(reinterpret_cast<const char*>(ptr), len);
    }

    uint32_t arrayLength() const {
        uint32_t len;
        auto status = icfx_array_len(&value_, &len);
        if (status != ICFG_OK) throw IcfxException(status);
        return len;
    }

    IcfxValue arrayGet(uint32_t index) const {
        icfx_value_t elem;
        auto status = icfx_array_get(&value_, index, &elem);
        if (status != ICFG_OK) throw IcfxException(status);
        return IcfxValue(elem);
    }

    uint32_t objectLength() const {
        uint32_t len;
        auto status = icfx_obj_len(&value_, &len);
        if (status != ICFG_OK) throw IcfxException(status);
        return len;
    }

    IcfxValue objectGetByKeyId(uint32_t key_id) const {
        icfx_value_t field;
        auto status = icfx_obj_try_get_by_keyid(&value_, key_id, &field);
        if (status != ICFG_OK) throw IcfxException(status);
        return IcfxValue(field);
    }

private:
    icfx_value_t value_;
};

/**
 * ICFX file view (zero-copy)
 */
class IcfxView {
public:
    explicit IcfxView(const uint8_t* data, size_t size) {
        auto status = icfx_open(data, size, &view_);
        if (status != ICFG_OK) throw IcfxException(status);
    }

    void validate() const {
        auto status = icfx_validate(&view_);
        if (status != ICFG_OK) throw IcfxException(status);
    }

    IcfxValue root() const {
        return IcfxValue(icfx_root(&view_));
    }

private:
    icfx_view_t view_;
};

} /* namespace ironcfg */

#endif /* IRONCFG_ICFX_HPP */
