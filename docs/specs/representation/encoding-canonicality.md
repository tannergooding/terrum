# Encoding canonicality

## 1. Ordinary reading, writing and auditing

Ordinary readers accept structurally valid, semantically equivalent
noncanonical encodings. For the register codec, this includes a longer
assigned instruction form for the same operation, and TypeMap, MethodMap,
FieldMap or ConstantMap entries with a different consistently referenced
order, duplicates or distinct unused entries. A set `mapMask` bit with a
zero count is also valid noncanonical framing. These differences do not change
selected types, values, operations, effects or declaration identities.

Writers emit the canonical instruction form and map order specified below.
An explicit canonicality audit rejects departures within its stated scope.
Ordinary acceptance is not a claim that the writer conformed, that the bytes
are canonical, or that the whole artifact was examined. The mandatory
[consumer-verification contract](reader-trust.md) still applies; canonicality
does not require an additional global scan beyond structural safe-consumption
checks and support admission. Semantic re-proof remains optional.

Other assigned writer requirements remain owned by their forms, including
[omission of zero-mask Effects](contract-clauses.md#3-effects).
This chapter does not select one String encoding, require global
deduplication or define a unique canonical artifact byte image.

Noncanonical is not another name for malformed or unsupported. Reserved
bits, unassigned codes, invalid references, missing required types,
contradictory indexes and invalid declared-type values
violate the producer's valid-artifact obligations. Mandatory consumer
verification rejects these invalid forms before affected use; this chapter
does not prescribe diagnostic wording or internal check order. No alternative
encoding relaxes binding, effects, ownership, unsafe authorization or the
selected execution profile for a valid artifact.

## 2. Instruction forms

`instructionEncoding=01` selects the codec whose first byte fixes the whole
instruction length: `00..0F` is one byte, `10..BF` is two bytes, and
`C0..FF` is four bytes. There are no width classes, prefixes, variable-length
integers, or alternate widths within an instruction.

For a logical operation with more than one assigned encoding, a canonical
writer selects the shortest assigned form that preserves the same operation,
typed operands and effects. A one-byte tuple is canonical only when its
implied operands exactly match; short forms require their assigned register,
map or record limits; otherwise use the appropriate long family. Equal-size
equivalent forms use the lower primary value. This choice does not rewrite an
operation tree, reassociate arithmetic, remove effects or substitute one
sequence of operations for another.

An ordinary reader accepts any assigned form whose complete operands and
semantics are valid, including a longer form where a shorter equivalent is
available. An explicit instruction-canonicality audit rejects that longer
form. Unassigned primary values, invalid family controls, nonzero reserved
fields and out-of-range indexes are malformed, not noncanonical.

Tiny and short Home stores, short reference stores and short place stores are
eligible only for the assigned `sourceUse=1` CopyRead contract and a
Copy-eligible source. Their destination must independently satisfy the
selected state and write/initialization authority. A full Home store uses
`D6` for its explicitly assigned CopyRead, Transfer or BorrowRead source-use
mode when the operation's complete dependencies are derivable from the
selected Home and current source facts. Other source-use modes, additional
per-store formal/dependency contracts and non-Home destinations require the
explicit SemanticStore record form; a writer must not encode them as compact
CopyRead stores.

## 3. Body maps and implementation forms

Body-local indexes are always zero-based `u16`; every value, including
`0xFFFF`, is valid. A corresponding count may range through 65,536. There is
no narrower or wider assigned index form; an out-of-range reference or
inventory is invalid, not a canonicality difference. Code offsets, byte
lengths and instruction ordinals remain independent `u32` domains.

TypeMap, MethodMap, FieldMap and ConstantMap entries are fixed-width records.
The stream prefix uses `mapMask` bits 0..4 for Home/Type/Method/Field/
Constant maps; only present maps serialize their count words. The exact
prefix is `17 + 4*popcount(mapMask)` bytes. Canonical writers clear the bit
and omit count/payload for every empty map. Ordinary readers accept a
set empty-present map as valid noncanonical framing. Writers order
TypeMap/MethodMap/FieldMap/ConstantMap entries by first logical use, reuse
identical tokens/values and omit unused entries. Ordinary readers accept
consistently reordered maps, duplicate entries and distinct valid unused
entries; a selected lookup need not scan the complete maps. HomeMap entries are distinct storage
identities and are not deduplicated. HomeMap order is not canonicalized by
this schema; a transformation that changes it remaps every Home reference
and preserves each storage identity/state. A whole-Body audit does not claim
a unique HomeMap ordering.

Typed records follow first logical use in a canonical Body. Canonical writers
omit unused records; ordinary readers accept reordered or duplicate records
when all record/instruction references preserve meaning.

A canonical writer selects the eligible compact Body descriptor or
instruction stream with the smallest complete serialized row. If equivalent
eligible forms have the same size, the lower `implementationKind` byte wins.
Ordinary readers accept any valid implementation form, whether or not a
smaller equivalent descriptor or stream exists. Required support and
semantic obligations do not change with the chosen form.

## 4. Body dependency maps and TypeSpecRef values

Each Body has upfront TypeMap, MethodMap and FieldMap tables. Entries are
fixed four-byte tokens, not instruction-width references. TypeMap entries
resolve complete types in the Body's selected module context; MethodMap
entries resolve a MethodDef or exact imported expectation. FieldMap entries
select a FieldDef DefId, FieldDef Import or global FieldPath token. Body-local
indexes are fixed zero-based `u16` values, not artifact row numbers or
variable-width fields. Required HomeMap, BlockEntryState, descriptor and
operation types must be present and agree exactly under selected formation
rules.

Canonical writers order TypeMap entries by first logical use in HomeMap,
BlockEntryState records and instructions; a TypeOf instruction visits its
complete subject. Carrier 2's required zero `typeMapIndex` is unused and is
neither a TypeMap use nor a remapping reference. A pseudo token's base
TypeMap dependency precedes the pseudo entry; ties preserve first-use order.
MethodMap, FieldMap and ConstantMap entries follow first logical use from
instructions and referenced records. FieldPath rows are shared by identical
complete root TypeSpecRefs and ordered FieldDef/Import identities, while
distinct Import expectations remain distinct. Canonical FieldPath rows follow
first use from the ordered Module/MethodDef/Body/Block/instruction/FieldMap
traversal. Unused map entries and unreferenced FieldPath rows are omitted by
canonical writers.
A canonical writer uses a direct FieldDef/Import token for an eligible
one-field path whose exact root is its non-generic declaring type; all
constructed, qualified, formal or multi-step paths use a FieldPath token.
An equivalent one-step FieldPath row may be accepted ordinarily but is
noncanonical when the direct form is valid.

Canonical token spelling follows the assigned identity forms: use `0xF2`
for an assigned well-known type, `0xF3` for an eligible built-in family over
an atomic argument, TypeDef and Import tokens for their exact unqualified
nominal identities, and `0xF0`/`0xF1` when their pseudo schema exactly
represents a constructed or qualified type. Use a TypeSpec token when no
direct, inline or pseudo token represents the complete type. A TypeSpec
token for an identity with an assigned direct/inline/pseudo form and a
pseudo token with no constructor or modifier are valid ordinary inputs but
noncanonical.
For pseudo tokens, `0xF0` is canonical when either constructor is absent;
`0xF1` is canonical only for the `generic<wrapper<T>>` order when both are
present. The writer still lists each base dependency once and remaps every
pseudo base index.

For non-Body complete-type fields, a canonical writer minimizes the complete
artifact size while considering TypeSpec rows still required by nested or
other references. If an eligible `0xF2`/`0xF3` reference and a row reference
have equal total size, the writer uses the inline token. A TypeSpec row may
remain when another field requires it; this does not make a row spelling
canonical in an equal-size complete-type field. Ordinary readers accept both
forms, and an audit reports noncanonical spelling only with the complete
logical row/use inventory.

Ordinary readers accept different orderings when all logical references are
consistently remapped, as well as valid unused entries and duplicates
resolving to the same type. They do not need to scan maps for duplicate
identities. This is not permission to omit required types, change a complete
nominal identity, ignore an accessed invalid TypeSpec or hide undeclared
required semantics. Complete declared retained requirements remain governed
by [admission](requirements-and-admission.md).

Map-order, duplicate-entry and unused-entry canonicality needs the relevant
complete logical use inventory. A selected lookup need not construct it. A
full canonicality audit does; a partial audit reports its narrower scope
instead of certifying the whole map.

## 5. Constant values and exact bits

Instructions load typed ConstantMap entries; they do not carry variable-width
literal bits. Constant identity is the complete type plus exact admitted
value/representation, not numerical equality alone. Canonical writers reuse
identical typed constants and omit unused entries. Ordinary readers accept
duplicate entries and preserve their exact references.

Boolean constants admit only the two valid Boolean values; unit admits only
its singleton value; `char16` admits every code unit including surrogates.
Floating and decimal constants preserve every declared-format bit, including
signed zero, NaN sign/state/payload and decimal cohort. Loading a constant
does not run arithmetic, quiet a NaN, normalize a payload or depend on host
floating-point state. Unadmitted constant formats remain unsupported even if
their storage size matches an admitted type.

## 6. Re-encoding and evidence

Changing an instruction form can change its byte extent. A rewriter
reconstructs typed operations, records and control flow, then repairs every
affected Block code offset/length, record offset, instruction ordinal and
containing-row offset. Changing a Body map order remaps every affected
zero-based reference in its actual context. Changing a token requires
resolving and rebuilding the token and its row/Import context; byte
substitution is not a valid remap. For stream Bodies, map changes rebuild
`mapMask`, conditional count words, all present map payloads, and the
`17 + 4*popcount(mapMask)` prefix extent. FieldMap remapping distinguishes
FieldDef DefIds, FieldDef Imports and FieldPath tokens; referenced FieldPath
rows remap their complete root TypeSpecRef and ordered FieldDef/Import steps.
`DE` kinds 1/2/5 remap through TypeMap, while kinds 3/4 remap through
FieldMap. Rewrites preserve TypeIdentity carrier/edge identity evidence,
comparison results and guarded refinements; carrier-2 zero fields are never
remapped as TypeMap[0]. Optional SourceMap step-role slots are
preserved only when the rewritten instruction has the same meaning and
semantic step; otherwise the section is regenerated or dropped under the producer's
[transformation obligations](reader-trust.md#5-transformations-verification-reuse-and-audit-claims).

These operations preserve selected identities, values and exact bits, not
merely storage size or a same-shaped signature. No effect may be dropped
because its result is unused, and no private-body optimization or nominal
substitution is justified by canonicalization.

An audit states whether it checked selected instructions, a Body's complete
instruction-form/map rules or every applicable object in a declared artifact scope.
Unvisited payloads are not certified. Unsupported required interpretation
or incomplete/exhausted work declines the requested claim; a trust decision
does not supply missing canonicality evidence.

The rules define `encoding-canonicality/1` under the exact
`trrm/1-draft-layout-queries-1` schema and out-of-band governing-chapter
agreement. They define canonical writers and scoped audits for the selected
masked Body prefix, FieldMap and FieldPath alongside the existing forms.
Numerical structural/semantic resource minima remain unassigned under the separate
[resource-accounting contract](resource-accounting.md).

The [Module manifest](semantic-manifests.md#3-rows-ranges-and-ordinary-encodings)
additionally distinguishes equivalent explicit Feature-list order/duplicates
from exact-revision conflicts. Its canonical writer/audit rule is local to each
list, not a globally unique interning or whole-artifact image requirement.
