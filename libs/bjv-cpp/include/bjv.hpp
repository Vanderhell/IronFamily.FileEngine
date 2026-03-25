#ifndef BJV_HPP
#define BJV_HPP

#include <cstdint>
#include <cstring>
#include <string_view>
#include <stdexcept>
#include <utility>

// Include C header
extern "C" {
#include "bjv.h"
}

namespace bjv {

/// Wraps bjv_val_t with convenient C++ interface
class value {
public:
    value() : doc_(nullptr) {}
    value(const bjv_doc_t* doc, bjv_val_t v) : doc_(doc), val_(v) {}

    /// Get underlying C value
    bjv_val_t c() const { return val_; }

    /// Type checking
    bool is_null() const { return bjv_type(val_) == BJV_NULL; }
    bool is_bool() const { uint8_t t = bjv_type(val_); return t == BJV_TRUE || t == BJV_FALSE; }
    bool is_int64() const { return bjv_type(val_) == BJV_I64; }
    bool is_uint64() const { return bjv_type(val_) == BJV_U64; }
    bool is_float64() const { return bjv_type(val_) == BJV_F64; }
    bool is_string() const { uint8_t t = bjv_type(val_); return t == BJV_STRING || t == BJV_STR_ID; }
    bool is_bytes() const { return bjv_type(val_) == BJV_BYTES; }
    bool is_array() const { return bjv_type(val_) == BJV_ARRAY; }
    bool is_object() const { return bjv_type(val_) == BJV_OBJECT; }

    /// Safe conversions (primitives can be used without doc context)
    int64_t as_int64() const {
        int64_t out;
        if (!bjv_get_i64(val_, &out)) throw std::runtime_error("not an int64");
        return out;
    }

    uint64_t as_uint64() const {
        uint64_t out;
        if (!bjv_get_u64(val_, &out)) throw std::runtime_error("not a uint64");
        return out;
    }

    double as_float64() const {
        double out;
        if (!bjv_get_f64(val_, &out)) throw std::runtime_error("not a float64");
        return out;
    }

    bool as_bool() const {
        bool out;
        if (!bjv_get_bool(val_, &out)) throw std::runtime_error("not a bool");
        return out;
    }

private:
    const bjv_doc_t* doc_;
    bjv_val_t val_;

    friend class doc;
};

/// Wraps bjv_doc_t with convenient C++ interface
class doc {
public:
    /// Open from binary data
    doc(const uint8_t* data, size_t size) {
        bjv_err_t rc = bjv_open(data, size, &doc_);
        if (rc != BJV_OK) {
            throw std::runtime_error("bjv_open failed");
        }
    }

    /// Constructor that takes ownership (moves)
    doc(doc&& other) noexcept : doc_(other.doc_) {
        other.doc_ = {};
    }

    /// No copy
    doc(const doc&) = delete;
    doc& operator=(const doc&) = delete;

    /// Destructor
    ~doc() {
        // No explicit close needed; document is stack-based
    }

    /// Get underlying C document
    const bjv_doc_t& c() const { return doc_; }
    bjv_doc_t& c() { return doc_; }

    /// Get root value
    value root() const {
        return value(&doc_, bjv_root(&doc_));
    }

    /// Validate (depth limit for safety)
    void validate(uint32_t max_depth = 256) const {
        bjv_err_t rc = bjv_validate_root(&doc_, max_depth);
        if (rc != BJV_OK) {
            throw std::runtime_error("validation failed");
        }
    }

    /// Array access (requires document context)
    uint32_t array_length(const value& v) const {
        if (!v.is_array()) throw std::runtime_error("not an array");
        uint32_t count;
        if (!bjv_arr_count(v.c(), &count)) throw std::runtime_error("not an array");
        return count;
    }

    value array_get(const value& v, uint32_t index) const {
        if (!v.is_array()) throw std::runtime_error("not an array");
        bjv_val_t elem;
        if (!bjv_arr_get(v.c(), index, &elem)) throw std::out_of_range("array index out of range");
        return value(&doc_, elem);
    }

    /// Object access (by keyId)
    uint32_t object_length(const value& v) const {
        if (!v.is_object()) throw std::runtime_error("not an object");
        uint32_t count;
        if (!bjv_obj_count(v.c(), &count)) throw std::runtime_error("not an object");
        return count;
    }

    value object_get(const value& v, uint32_t keyid) const {
        if (!v.is_object()) throw std::runtime_error("not an object");
        bjv_val_t out;
        if (!bjv_obj_get_by_keyid(v.c(), keyid, &out)) throw std::runtime_error("key not found");
        return value(&doc_, out);
    }

    /// Convenient object field access by key name
    value get(const value& obj, std::string_view key) const {
        if (!obj.is_object()) throw std::runtime_error("not an object");

        uint32_t keyid;
        if (!bjv_keyid_find(&doc_, key.data(), key.size(), &keyid)) {
            throw std::runtime_error("key not found");
        }
        return object_get(obj, keyid);
    }

    /// String access (returns string_view over UTF-8 bytes)
    std::string_view get_string(const value& v) const {
        if (!v.is_string()) throw std::runtime_error("not a string");
        bjv_slice_t slice;
        if (!bjv_get_string(v.c(), &slice)) throw std::runtime_error("failed to get string");
        return std::string_view(reinterpret_cast<const char*>(slice.ptr), slice.len);
    }

    /// Bytes access
    std::pair<const uint8_t*, size_t> get_bytes(const value& v) const {
        if (!v.is_bytes()) throw std::runtime_error("not bytes");
        bjv_slice_t slice;
        if (!bjv_get_bytes(v.c(), &slice)) throw std::runtime_error("failed to get bytes");
        return {slice.ptr, slice.len};
    }

    /// Dictionary access (get key by keyid)
    std::string_view get_key(uint32_t keyid) const {
        bjv_slice_t key;
        if (!bjv_keyid_to_key(&doc_, keyid, &key)) {
            throw std::out_of_range("invalid keyid");
        }
        return std::string_view(reinterpret_cast<const char*>(key.ptr), key.len);
    }

private:
    bjv_doc_t doc_;
};

} // namespace bjv

#endif // BJV_HPP
