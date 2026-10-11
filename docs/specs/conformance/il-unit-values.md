# IL unit-value conformance cases

These cases apply the coded Body's
[type and definition model](../il-instruction-set.md#type-and-definition-model-for-operands),
[scalar schemas](../il-instruction-set.md#operation-encoding-and-normalized-effects), and
[logical signatures](../il-metadata.md#metadata-logical-tables-and-row-semantics).
Assume otherwise well-formed descriptors, definitions, blocks, and selected
methods. They specify reader/writer obligations, not current implementation
support.

| Case | Situation | Required result |
|---|---|---|
| IRUNIT-01 | A kind-0 value descriptor names primitive `void` | Typed unit value; zero payload, not an absent definition |
| IRUNIT-02 | `ConstBits` has unit type and all-zero data at width 1, 2, 4, or 8 | Typed `()` with the selected on-wire byte extent; ordinary width/canonicality admission still applies |
| IRUNIT-03 | Any encoded data bit is nonzero for that unit constant | Reject; never reinterpret a number as unit |
| IRUNIT-04 | `ConstSigned` uses unit type | Reject noninteger literal type |
| IRUNIT-05 | Copy a live unit value | Define the same typed unit without payload or ownership duplication |
| IRUNIT-06 | A MethodSig has by-value parameters `(void, int32, void)` | Three ordered logical entry values/arguments, not one argument |
| IRUNIT-07 | A call omits a unit argument or encodes zero instead of its value ID | Reject argument arity/ID mismatch |
| IRUNIT-08 | A normal edge supplies a unit-typed block parameter | Preserve its complete type and logical position |
| IRUNIT-09 | A unit-result Call has result ID zero | No value definition; normal completion still has logical unit result |
| IRUNIT-10 | A unit-result Call defines the next nonzero typed unit ID | Define `()` on normal completion; no definition on failure |
| IRUNIT-11 | A non-unit Call uses result ID zero, including after source explicit discard | Reject; source discard cannot turn the selected result type into unit |
| IRUNIT-12 | Return from a unit-result method uses zero or a live unit value ID | Unit result; no payload carrier |
| IRUNIT-13 | Its Return instead uses a non-unit value ID | Reject type mismatch |
| IRUNIT-14 | Compare two live unit IDs with equality/inequality | Boolean true/false respectively |
| IRUNIT-15 | Unit is used by numeric/bitwise/order or integer conversion operations | Reject outside the operation's domain |
| IRUNIT-16 | A unit-typed logical place is loaded while vacant | Reject ordinary uninitialized access; zero payload does not initialize it |
| IRUNIT-17 | All operand widths are one byte; `t1` is unit; next definition is `r1` | `01 01 01 00` is the complete `ConstBits r1, t1, 0` instruction |
| IRUNIT-18 | Optimization removes unit payload transport | Preserve logical signature/argument positions, evaluation, initialization-state obligations, and effects |
| IRUNIT-19 | A zero-payload nominal result is called with result ID zero | Reject; only complete `void`, not zero size, permits the unit/no-definition form |

These cases allocate no new opcode, operand selector, or primitive type code.
Unit reuses atomic code `0x00`, typed value IDs, and the existing constant,
copy, call, equality, and edge schemas.
