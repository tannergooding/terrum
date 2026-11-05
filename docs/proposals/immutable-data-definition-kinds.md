# ImmutableData definition-kind assignments

> [!IMPORTANT]
> **Unapproved numeric-assignment proposal.** This preserves the separate
> allocation decision; it approves no record, Feature, compiler support or
> normative contract.

## Candidate assignment and owner

Recommend candidate DefId kinds `0x22=InterfaceConstDef`,
`0x23=WitnessAssociatedConst` and `0x24=StaticField` for the target sets in
[ImmutableData declaration references](immutable-data-declaration-reference.md).
That proposal is the complete semantic owner for direct-only references,
closed targets, owner substitution, original expectations, binding and
per-Module Feature 19 closure. This document records only the distinct
numeric-assignment decision.

| Candidate kind | Exact target | ImmutableData kind | Import/Export |
|---:|---|---:|---|
| `0x22` | InterfaceConstDef, section `0x0031`, 16-byte row | 1 | Both, with complete expected-definition/access and owner binding |
| `0x23` | WitnessAssociatedConst, section `0x0033`, 16-byte selected binding row | 1 | Neither; exact selected witness origin/binding graph |
| `0x24` | Distinct logical StaticField table; physical section unassigned | 2 | Both only with complete static expectation/provider representation |

Encode each DefId as `(kind << 24) | row`, little-endian, with a one-based row
in `1..0x00FF_FFFF` checked against the exact table. Existing `0x20`
AssociatedTypeDef and `0x21` UnionCaseDef remain unchanged; section
`0x0024=InterfaceMethod` is not DefId kind `0x24`. Kind `0x24` does not make
FieldDef static, and no StaticField physical section or publication lifecycle
is selected here. Section numbers, row values and equal payloads do not imply
a DefId assignment. Reconsidering an unpublished assignment is a separate
design decision; no reassignment, alias or compatibility rule is made here.

`0x23` identifies a selected associated-constant binding, not its declaration.
The separate
[AssociatedConstDef identity proposal](associated-constant-definition-identity.md)
owns declaration kind `0x25` and its reference/Import decision; that declaration
is not an ImmutableData kind-1 target. No `0x25` allocation or alias is made
by this proposal.

## Separate gates

The `0x22/0x23/0x24` assignments, the ImmutableData target-set contract and
Feature `19/1=ImmutableDataIdentities` registration/closure remain separately
unapproved. The accepted registry remains unchanged pending review and
adoption. Static storage/publication/load lifetime remains with
[immutable static-field persistence](immutable-static-field-persistence.md);
intrinsic identity/catalog admission remains with
[intrinsic registration identity](intrinsic-registration-identity.md).

Current support belongs in the
[implementation reference](../implementation.md) and
[conformance handoff](../implementation/conformance-gaps.md). The proposal
does not claim implementation evidence or Standard TRRM support.
