/*
 * IUPD Error Codes
 * Stable error enumeration for strict verifier
 */

#ifndef IUPD_ERRORS_H
#define IUPD_ERRORS_H

#include <stdint.h>

typedef enum {
    IRON_OK = 0,

    /* I/O Errors */
    IRON_E_IO = 1,

    /* Format Errors */
    IRON_E_FORMAT = 2,

    /* Version/Profile Errors */
    IRON_E_UNSUPPORTED_VERSION = 3,
    IRON_E_PROFILE_NOT_ALLOWED = 4,

    /* DoS/Limit Errors */
    IRON_E_DOS_LIMIT = 5,

    /* Signature Errors */
    IRON_E_SIG_INVALID = 6,

    /* UpdateSequence Errors */
    IRON_E_SEQ_INVALID = 7,

    /* Delta/Diff Errors */
    IRON_E_DIFF_LIMITS = 8,
    IRON_E_DIFF_BASE_HASH = 9,
    IRON_E_DIFF_TARGET_HASH = 10,
    IRON_E_DIFF_OUT_OF_RANGE = 11,

} iron_error_t;

const char* iron_error_str(iron_error_t err);

#endif /* IUPD_ERRORS_H */
