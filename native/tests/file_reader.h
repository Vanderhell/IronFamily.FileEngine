/*
 * File-based reader for testing IUPD verifier
 * Uses FILE* for reading without full-file allocation
 */

#ifndef FILE_READER_H
#define FILE_READER_H

#include "ironfamily/io.h"
#include <stdint.h>
#include <stdio.h>

typedef struct {
    FILE* fp;
} file_reader_ctx_t;

/* Create a file reader from a path */
int file_reader_open(const char* path, file_reader_ctx_t* ctx, iron_reader_t* reader);

/* Close file reader */
void file_reader_close(file_reader_ctx_t* ctx);

#endif /* FILE_READER_H */
