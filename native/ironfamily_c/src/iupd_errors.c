#include "ironfamily/iupd_errors.h"

const char* iron_error_str(iron_error_t err) {
    switch (err) {
        case IRON_OK:
            return "IRON_OK";
        case IRON_E_IO:
            return "IRON_E_IO";
        case IRON_E_FORMAT:
            return "IRON_E_FORMAT";
        case IRON_E_UNSUPPORTED_VERSION:
            return "IRON_E_UNSUPPORTED_VERSION";
        case IRON_E_PROFILE_NOT_ALLOWED:
            return "IRON_E_PROFILE_NOT_ALLOWED";
        case IRON_E_DOS_LIMIT:
            return "IRON_E_DOS_LIMIT";
        case IRON_E_SIG_INVALID:
            return "IRON_E_SIG_INVALID";
        case IRON_E_SEQ_INVALID:
            return "IRON_E_SEQ_INVALID";
        case IRON_E_DIFF_LIMITS:
            return "IRON_E_DIFF_LIMITS";
        case IRON_E_DIFF_BASE_HASH:
            return "IRON_E_DIFF_BASE_HASH";
        case IRON_E_DIFF_TARGET_HASH:
            return "IRON_E_DIFF_TARGET_HASH";
        case IRON_E_DIFF_OUT_OF_RANGE:
            return "IRON_E_DIFF_OUT_OF_RANGE";
        default:
            return "UNKNOWN_ERROR";
    }
}
