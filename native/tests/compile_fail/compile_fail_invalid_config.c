#include "ironfamily/ironfamily.h"

#if IRONFAMILY_TEST_INVALID_CONFIG
#error "Invalid configuration macro value accepted"
#endif

int main(void)
{
    return 0;
}
