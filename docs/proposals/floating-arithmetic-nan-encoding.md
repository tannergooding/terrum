# Floating arithmetic NaN encoding

**Status: Not accepted**

The working profile now guarantees only that an arithmetic result which is
NaN remains a NaN; its sign and payload are unspecified. Implementations should
propagate and quiet an input NaN when practical, without promising a particular
payload or propagation choice across implementations.

The proposed fixed encodings (`0x7FC00000` for `float32` and
`0x7FF8000000000000` for `float64`) were not adopted. Requiring them would
discard input payloads even when an implementation can propagate them and
would impose an exact representation contract that arithmetic does not need.
Bit-preserving operations and numeric conversions retain their separate
contracts. This decision does not accept executable TRRM floating arithmetic.
