/*
 * CRC32 Helper
 * Standard ISO/IEC CRC32 (reflected polynomial)
 */

#ifndef IRONFAMILY_CRC32_H
#define IRONFAMILY_CRC32_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/*
 * Compute CRC32-ISO of a data buffer.
 *
 * @param data      Input data buffer
 * @param len       Length of data in bytes
 * @return          CRC32 checksum
 */
uint32_t iron_crc32(const uint8_t* data, uint32_t len);

#ifdef __cplusplus
}
#endif

#endif /* IRONFAMILY_CRC32_H */
