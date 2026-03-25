/*
 * CRC32 Helper
 * Standard ISO/IEC CRC32 (reflected polynomial)
 */

#ifndef CRC32_H
#define CRC32_H

#include <stdint.h>

/*
 * Compute CRC32-ISO of a data buffer.
 *
 * @param data      Input data buffer
 * @param len       Length of data in bytes
 * @return          CRC32 checksum
 */
uint32_t iron_crc32(const uint8_t* data, uint32_t len);

#endif /* CRC32_H */
