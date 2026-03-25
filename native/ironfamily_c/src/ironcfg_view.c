/* IRONCFG C89-compatible - Minimal view accessors for testing */

#include "ironcfg/ironcfg.h"
#include <stdbool.h>

/* Get header information (always available after open) */
const ironcfg_header_t *ironcfg_get_header(const ironcfg_view_t *view)
{
    if (view == NULL) return NULL;
    return &view->header;
}

/* Check if file has CRC32 */
bool ironcfg_has_crc32(const ironcfg_view_t *view)
{
    if (view == NULL) return 0;
    return (view->header.flags & 0x01) != 0;
}

/* Check if file has BLAKE3 */
bool ironcfg_has_blake3(const ironcfg_view_t *view)
{
    if (view == NULL) return 0;
    return (view->header.flags & 0x02) != 0;
}

/* Check if schema is embedded */
bool ironcfg_has_embedded_schema(const ironcfg_view_t *view)
{
    if (view == NULL) return 0;
    return (view->header.flags & 0x04) != 0;
}

/* Get file size */
uint32_t ironcfg_get_file_size(const ironcfg_view_t *view)
{
    if (view == NULL) return 0;
    return view->header.file_size;
}
