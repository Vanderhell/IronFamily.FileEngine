#include <assert.h>
#include "ironfamily/ironfamily.h"

int main(void)
{
    assert(ironfamily_dummy_add(2, 3) == 5);
    return 0;
}
