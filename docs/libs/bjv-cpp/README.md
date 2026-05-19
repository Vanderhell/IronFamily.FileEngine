# BJV C++ Header-Only Wrapper

Minimal, zero-allocation C++ wrapper over the BJV C core library.

## Features

- **Header-only**: Just `#include <bjv.hpp>`
- **No allocations**: All reads use references and `std::string_view`
- **Type-safe**: Convenient C++ interface with exceptions
- **Minimal overhead**: Direct delegates to C functions
- **C++17**: Requires C++17 for `std::string_view`

## Quick Start

```cpp
#include <bjv.hpp>
#include <fstream>

int main() {
    // Read BJV file
    std::vector<uint8_t> data = /* read file */;

    bjv::doc doc(data.data(), data.size());
    doc.validate();

    auto root = doc.root();
    auto name_val = doc.get(root, "name");
    if (name_val.is_string()) {
        std::string_view name = doc.get_string(name_val);
    }

    return 0;
}
```

## Class Reference

### bjv::value

Type checking and primitive access.

```cpp
if (val.is_null())     // Type predicates
val.as_int64()         // Safe conversion
```

### bjv::doc

Parsing and navigation.

```cpp
bjv::doc doc(data, size);     // Parse
doc.validate();                // Validate
bjv::value root = doc.root(); // Get root

doc.get(obj, "key");           // Lookup field by name
doc.array_length(arr);         // Array length
doc.get_string(val);           // Get string as string_view
```

## Building

```bash
cd libs/bjv-cpp
mkdir build && cd build
cmake ..
cmake --build .
ctest
```

## Design Notes

### No Allocations

- Returns `std::string_view` for strings (zero-copy)
- Returns `std::pair<ptr, size>` for bytes
- No `std::string` or `std::vector` in API

### Error Handling

- Exceptions for type mismatches
- `std::runtime_error` for parse/validation failures
- `std::out_of_range` for index errors

### Memory Safety

`std::string_view` lifetime tied to `bjv::doc` lifetime.

```cpp
// SAFE
bjv::doc doc(data, size);
std::string_view sv = doc.get_string(val);  // Valid while doc alive

// DANGEROUS
std::string_view sv;
{
    bjv::doc doc(data, size);
    sv = doc.get_string(val);
}
// sv now dangling - doc destroyed
```

## File Structure

```
libs/bjv-cpp/
├── include/bjv.hpp          # Header-only wrapper
├── tests/test_wrapper.cpp   # Tests
├── CMakeLists.txt           # Build config
└── README.md                # This file
```

## Compatibility

- C++17+
- POSIX/Windows
- Header-only integration

## Verified evidence

N/A - Documentation file. See primary source files in `libs/ironconfig-dotnet/` for implementation verification.
