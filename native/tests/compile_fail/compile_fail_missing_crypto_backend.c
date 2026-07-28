#include "ironfamily/ota_apply.h"

#if IRONFAMILY_TEST_MISSING_CRYPTO_BACKEND
#error "Missing crypto backend accepted for secure IUPD"
#endif

int main(void)
{
    return 0;
}
