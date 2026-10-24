# IL unit-value conformance cases

These cases apply the coded Body's
[register/value model](../il-instruction-set.md#names-values-and-storage),
[operation contracts](../il-instruction-set.md#operation-meanings),
[call forms](../il-instruction-set.md#calls) and
[logical signatures](../il-metadata.md#metadata-logical-tables-and-row-semantics).
Assume otherwise well-formed metadata, maps, records and blocks. They specify
reader/writer obligations, not current implementation support.

| Case | Situation | Required result |
|---|---|---|
| IRUNIT-01 | A complete MethodSig or TypeMap reference names primitive `void` | Typed unit value; zero payload does not mean absence |
| IRUNIT-02 | A ConstantMap entry has unit type and the admitted singleton value | Preserve a typed `()` value with no payload; it is not a missing map entry |
| IRUNIT-03 | A purported unit constant has nonzero data or an unassigned ConstantSource representation | Reject; never reinterpret a numeric value as unit |
| IRUNIT-04 | Copy/read a live unit value | Preserve its typed logical value without payload or ownership duplication |
| IRUNIT-05 | A MethodSig has by-value parameters `(void, int32, void)` | Preserve three ordered logical formals; unit formals do not disappear from arity or identity |
| IRUNIT-06 | `CallSite` supplies a unit formal | Use the explicit None selector only for the exact unit formal, with zero source-use and selector index |
| IRUNIT-07 | A non-unit formal uses the None selector, or a unit formal receives a non-unit selector with mismatched type | Reject arity/type mismatch; no success-shaped absent value |
| IRUNIT-08 | A bound/value window call includes unit parameters | Preserve every formal ordinal and evaluation contract even when a unit carries no payload |
| IRUNIT-09 | A normal-returning `CallSite` result selector is None for a non-unit result | Reject; normal completion requires the typed result destination |
| IRUNIT-10 | A `CallSite` result selector is None for an exact unit result | Normal completion produces the signature-known logical unit result; failure does not publish a result |
| IRUNIT-11 | Tiny `00` or short `18` returns from a unit-result method | Return the signature's unit without reading R0 or another value slot |
| IRUNIT-12 | A unit-result method uses a value-return form with a non-unit register/Home value | Reject type mismatch |
| IRUNIT-13 | A non-unit method returns a zero-size nominal value | Require its actual value/register/Home contract; zero size does not enable the unit-return form |
| IRUNIT-14 | Compare two live unit values for equality or inequality | Equality is true and inequality false under the singleton unit domain |
| IRUNIT-15 | Unit is used by arithmetic, bitwise, ordering or numeric conversion operations | Reject outside the operation's admitted type domain |
| IRUNIT-16 | A unit-typed Home is read while vacant | Reject ordinary uninitialized access; zero payload does not initialize it |
| IRUNIT-17 | A unit value is removed from physical transport | Preserve logical signature positions, evaluation, initialization/ownership obligations and effects |
| IRUNIT-18 | A normal-returning call of a zero-payload nominal result uses a None result selector | Reject; only complete `void`, not size/layout, permits a normal no-destination result |
| IRUNIT-19 | A verified `DoesNotReturn` CallSite declares a non-unit or zero-payload nominal result and uses None | Accept only as the terminal Block instruction; preserve the complete signature and publish no normal result or successor |

These cases allocate no opcode, operand selector or primitive type code.
Unit uses atomic code `0x00`, typed register/Home bindings and the assigned
CallSite/return contracts; no per-expression value ID or numeric literal
instruction is introduced.
