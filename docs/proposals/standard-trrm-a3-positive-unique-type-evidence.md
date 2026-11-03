# A3 positive Unique evidence amendment

**Status: unapproved.** This remains a separate ownership-evidence decision
within the exact uniqueness-amended A3 proposal; documenting it does not
approve A3, allocate a normative TypeDef flag or establish compiler support.

## Decision and exact scope

The full source/metadata contract is integrated in
[Closed constructed nominal elements in standard fixed arrays](standard-trrm-closed-constructed-array-elements.md#positive-nominal-uniqueness-evidence).
It recommends TypeDef flag **`0x40=DeclaredUnique`** only under exact schema
identity **`trrm/1-draft-foreign-invocation-storage-fixed-arrays-3-unique-1`**.
The flag-free A3 candidate `trrm/1-draft-foreign-invocation-storage-fixed-arrays-3`
does not select it; A2 remains unchanged.

The integrated contract preserves A2 `0x20=DeclaredCopy`, Copy/Unique
exclusivity, neither-bit unknown, Unique without Cleanup, and universal
formal-domain promises. The positive declaration does not supply Copy,
Blittable, rootlessness, initialization, backing, cleanup, runtime or
per-instantiation evidence. Expected Unique must remain a retained positive
expectation and bind to a positive actual; do not overwrite a weaker stub.

This amendment changes no schema width, section, Feature, Constraint or
per-instance record. A2, flag-free A3 and the exact Unique-amended successor
remain distinct drafts. The separate
[A3 layout-failure-class proposal](standard-trrm-a3-layout-failure-classes.md)
continues to own host resource accounting and failure/diagnostic policy; its
ledger, bounded expansion and failure channels are not folded into this
ownership-evidence decision.

Use the closed-constructed-array owner for the exact flag mask, formal-domain
rules, producer/consumer obligations and conformance cases. This companion
retains the independent approval boundary only; it claims no test or
implementation result.
