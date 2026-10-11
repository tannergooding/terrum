# Library contracts

[Library layering](../libraries.md) separates the freestanding `language`
floor from optional services. These chapters distinguish selected contracts
from design direction and candidate APIs:

| Chapter | Responsibility |
|---|---|
| [Adopted inventory](adopted-inventory.md) | Selected API/protocol/schema roles, canonical declarations, recognition authority and incomplete surfaces |
| [Design scenarios](design-scenarios.md) | Provider/consumer paths, observable failure and responsibility, Framework Design Guidelines comparison and deliberate departures |

The logical inventory rule is `library-contract-inventory/1`. It indexes
existing contracts; it adds no API, attribute constructor, capability,
metadata assignment or instruction. A selected logical role is not proof
of a complete artifact encoding or implemented runtime.
The [library cases](../conformance/source-library-contracts.md) check those
boundaries. [Implementation notes](../../implementation/libraries.md) and
[API sketches](../../proposals/core-library-reference.md) remain informative.
