/*
 * icxs.hpp
 * ICXS C++ wrapper (header-only)
 *
 * Status: Production-ready
 * License: MIT
 */

#ifndef IRONCFG_ICXS_HPP
#define IRONCFG_ICXS_HPP

#include <ironcfg/icxs.h>
#include <stdexcept>
#include <cstring>
#include <vector>

namespace ironcfg {

class IcxsException : public std::exception {
public:
    explicit IcxsException(icfg_status_t status) : status_(status) {}

    const char* what() const noexcept override {
        switch (status_) {
            case ICFG_OK: return "OK";
            case ICFG_ERR_MAGIC: return "Invalid magic";
            case ICFG_ERR_BOUNDS: return "Out of bounds";
            case ICFG_ERR_CRC: return "CRC mismatch";
            case ICFG_ERR_SCHEMA: return "Schema error";
            case ICFG_ERR_TYPE: return "Type mismatch";
            case ICFG_ERR_RANGE: return "Out of range";
            case ICFG_ERR_UNSUPPORTED: return "Unsupported";
            case ICFG_ERR_INVALID_ARGUMENT: return "Invalid argument";
            default: return "Unknown error";
        }
    }

    icfg_status_t status() const { return status_; }

private:
    icfg_status_t status_;
};

/**
 * ICXS Record view (RAII wrapper)
 */
class IcxsRecord {
public:
    IcxsRecord(const icxs_record_t& rec) : record_(rec) {}

    int64_t getI64(uint32_t field_id) const {
        int64_t value;
        auto status = icxs_get_i64(&record_, field_id, &value);
        if (status != ICFG_OK) throw IcxsException(status);
        return value;
    }

    uint64_t getU64(uint32_t field_id) const {
        uint64_t value;
        auto status = icxs_get_u64(&record_, field_id, &value);
        if (status != ICFG_OK) throw IcxsException(status);
        return value;
    }

    double getF64(uint32_t field_id) const {
        double value;
        auto status = icxs_get_f64(&record_, field_id, &value);
        if (status != ICFG_OK) throw IcxsException(status);
        return value;
    }

    bool getBool(uint32_t field_id) const {
        bool value;
        auto status = icxs_get_bool(&record_, field_id, &value);
        if (status != ICFG_OK) throw IcxsException(status);
        return value;
    }

    std::string getString(uint32_t field_id) const {
        const uint8_t* ptr;
        uint32_t len;
        auto status = icxs_get_str(&record_, field_id, &ptr, &len);
        if (status != ICFG_OK) throw IcxsException(status);
        return std::string(reinterpret_cast<const char*>(ptr), len);
    }

private:
    icxs_record_t record_;
};

/**
 * ICXS file view (zero-copy)
 */
class IcxsView {
public:
    explicit IcxsView(const uint8_t* data, size_t size) {
        auto status = icxs_open(data, size, &view_);
        if (status != ICFG_OK) throw IcxsException(status);
    }

    void validate() const {
        auto status = icxs_validate(&view_);
        if (status != ICFG_OK) throw IcxsException(status);
    }

    uint32_t recordCount() const {
        uint32_t count;
        icxs_record_count(&view_, &count);
        return count;
    }

    IcxsRecord getRecord(uint32_t index) const {
        icxs_record_t record;
        auto status = icxs_get_record(&view_, index, &record);
        if (status != ICFG_OK) throw IcxsException(status);
        return IcxsRecord(record);
    }

    std::vector<uint8_t> schemaHash() const {
        std::vector<uint8_t> hash(16);
        icxs_schema_hash(&view_, hash.data());
        return hash;
    }

private:
    icxs_view_t view_;
};

} /* namespace ironcfg */

#endif /* IRONCFG_ICXS_HPP */
