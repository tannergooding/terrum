# ImmutableData definition-kind assignments

> [!IMPORTANT]
> **Unapproved numeric-assignment proposal.** This document preserves the
> separate allocation decision; it approves no record, Feature, compiler
> support or normative contract.

## Decision and scope

Recommend candidate DefId kinds **`0x22=InterfaceConstDef`,
`0x23=WitnessAssociatedConst`, and `0x24=StaticField`** for the target sets
owned by
[ImmutableData declaration references](immutable-data-declaration-reference.md).
The complete target, ownership, direct-reference, Import/Export, owner
substitution and per-Module Feature rules are integrated in that proposal.
This document records only the distinct numeric assignment choice and its
approval boundary.

The mapping is not inferred from section numbers. Existing `0x20`
AssociatedTypeDef and `0x21` UnionCaseDef assignments remain distinct and
unchanged. No AssociatedConstDef requirement kind, new section, Feature,
TypeSpec form or compatibility alias is selected here.

## Candidate words and exact targets

All assignments below remain **unapproved**. Encode each DefId as
`(kind << 24) | row`, little-endian, with a one-based row in
`1..0x00FF_FFFF` checked against the exact table.

| Candidate kind | Exact target | ImmutableData kind | Import/Export |
|---:|---|---|---|
| `0x22` | InterfaceConstDef, section `0x0031`, 16-byte row | 1 | Both, with complete expected-definition/access and owner binding |
| `0x23` | WitnessAssociatedConst, section `0x0033`, 16-byte selected binding row | 1 | Neither; exact selected witness origin/binding graph |
| `0x24` | Distinct logical StaticField table; physical section unassigned | 2 | Both only with complete static expectation/provider representation |

Section `0x0024=InterfaceMethod` is not DefId kind `0x24`. Kind `0x24`
does not make existing FieldDef rows static, and no StaticField physical
section is assigned by this proposal. The separate
[static persistence proposal](immutable-static-field-persistence.md) owns
that physical schema/publication decision. Likewise, `0x23` is a selected
binding identity, not an associated-constant declaration identity or
standalone Import/Export target.

The recommendation is not additive-only: an intentional reassignment of
unpublished numbers would be reviewable on its merits. This candidate uses
22/23/24 because those explicit mappings preserve the existing 20/21
identities and closed target distinction without requiring a broader
definition-table redesign. The same section numbers, row values or equal
payloads do not establish another assignment.

### `0x23`: selected associated-constant binding

`0x23` identifies one selected binding row owned by its origin
InterfaceWitness. Its meaning retains the exact implementing type, selected
interface instance, inherited declaring interface and AssociatedConstDef
requirement. Its expected/provider correspondence follows the complete
witness/origin graph; no independent binding Import, Export or name-based
lookup is added. An unbound AssociatedConstDef is not a valid `0x23` target.
The separate
[AssociatedConstDef identity proposal](associated-constant-definition-identity.md)
owns its requirement identity/reference codec.

## Integration boundary and remaining gates

The direct-reference proposal includes these assignments but remains
unapproved. Numeric allocation, the 0041 target-set contract, and
Feature `19/1=ImmutableDataIdentities` admission/closure are separate review
and support gates. A DefId kind does not supply:

- immutable value or ConstantSource decoding;
- witness origin/expected-binding or associated-declaration codecs;
- StaticField storage, access, initialization, publication, load lifetime or
  cleanup;
- Import/Export codecs beyond the exact target sets above; or
- Feature registration, dependency closure or consumer implementation.

The accepted registry remains unchanged until the relevant proposals are
reviewed and adopted. Current implementation evidence and conformance gaps
belong in the [implementation reference](../implementation.md) and
[conformance handoff](../implementation/conformance-gaps.md), not in this
numeric-allocation record.
