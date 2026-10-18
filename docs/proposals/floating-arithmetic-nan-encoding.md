# Floating arithmetic NaN encoding

**Status: the fixed-runtime-encoding alternative is not adopted. Quiet
processing-result NaNs and the distinct deterministic constant recipe are
accepted in the owners below.**

## Disposition and owners

This file records the boundary of the declined alternative, not a second
floating-result policy:

| Subject | Accepted owner and result |
|---|---|
| Runtime arithmetic, nonidentity numeric conversion and numeric selection producing NaN | [Floating environment §3](../specs/language/floating-environment.md#3-nan-processing-results): valid **quiet** NaN; sign, payload and propagated input choice are unspecified |
| Admitted constant execution of those operations | [Floating environment §4](../specs/language/floating-environment.md#4-deterministic-constant-nans) and [constant execution](../specs/language/proof-and-constant-evaluation.md#deterministic-nans-and-frozen-results): positive quiet NaN with zero payload, chosen at the semantic operation |
| Copies, identity conversion, unary plus, negation and Abs | [Sign/representation rules](../specs/language/floating-environment.md#2-sign-only-operations): preserve the specified bits; negation toggles and Abs clears only the sign |

The original proposal would have required runtime binary32
`0x7FC0_0000` and binary64 `0x7FF8_0000_0000_0000`. That extra runtime
representation guarantee remains unselected. Those same encodings are the
accepted results of the corresponding admitted **constant NaN-processing**
operations; they are not rejected constant encodings. Runtime propagation
may retain an input payload, but quieting is mandatory, not best effort.
No preference among eligible input NaNs or new decimal encoding is proposed.

## Integration and conformance

Use the existing [floating conformance cases](../specs/conformance/source-floating-environment.md),
[primitive-operation contract](../specs/language/primitive-operators.md) and
[conversion contract](../specs/conversions.md). Do not maintain another
per-operation table here or canonicalize arbitrary inputs during serialization.

Physical integration belongs to the [logical floating record requirements](../specs/representation/logical-records.md#floating-representation-and-numeric-witnesses-encoding-pending),
the [registry](../specs/representation/registry.md), and the
[Body floating operation matrix](../specs/il-instruction-set.md#floating-operations).
An already admitted operation keeps its full result obligations; another
format, operation, constant program or value codec needs its own complete
admission. This disposition neither withdraws existing bounded floating
instructions nor admits the complete scalar floating catalog.

Qualification must distinguish quiet from signaling NaNs, not test only
`IsNaN`. Exercise NaNs created from non-NaN operands, both operand orders,
payload-bearing inputs, sign-only operations after canonical processing,
identity conversions versus nonidentity conversions, signed zeros and
subnormals. Runtime tests must not require the declined fixed payload;
constant tests must require the selected recipe and preserve explicitly
constructed bits through copies/sign operations. Preserve argument effects,
invalid Clamp-bound failure and native control-state duties separately.
