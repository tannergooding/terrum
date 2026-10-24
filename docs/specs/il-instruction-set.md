# Terrum register instruction set

This chapter defines the current draft register-based Body encoding and its
operation contracts. It replaces the earlier evaluation-stack/SSA-value-ID
instruction envelope; unpublished earlier Body assignments are not aliases
or compatibility targets. The format pair remains `(1, 0)` under the
[pre-release versioning rules](representation/format-versioning.md); exchange
still requires exact out-of-band agreement on this complete draft schema.

The [allocation registry](representation/registry.md) is the sole authority
for opcode, family, record-kind and enum numbers. This chapter defines their
operand layouts and semantics. [IL metadata](il-metadata.md) owns the Body
row, maps, typed records and SourceMap section framing. An allocation does
not establish compiler/runtime support or complete a separately required
logical contract.

The instruction stream is register-based but does not promise machine
register allocation, purity, effect ordering beyond the operation rules,
or a register/stack ABI. A consumer may construct internal SSA identities
while importing; those identities are not serialized per-expression IDs.
Registers, method homes and places remain distinct logical carriers.

## Semantic owners

| Contract | Owner |
|---|---|
| Complete types and normalization | [Complete types](language/types-and-normalization.md) |
| Scalar domains, shifts and comparisons | [Primitive operators](language/primitive-operators.md) |
| Conversion pairs and policies | [Conversions](conversions.md) |
| NaNs, sign operations and floating observations | [Floating environment](language/floating-environment.md) |
| Abs/Min/Max/Clamp | [Abs](language/absolute-value.md), [numeric selection](language/numeric-selection.md) |
| Values, places and zero-data unit | [Values and execution](language/expressions-and-statements.md) |
| Initialization and joins | [Structural flow](language/initialization-and-flow.md) |
| Transfer, replacement and retirement | [Ownership and cleanup](language/ownership-and-cleanup.md) |
| References and backing | [References](language/references.md), [dependencies](language/dependencies-and-views.md), [lifetimes](lifetimes.md) |
| Call staging and publication | [Call contracts](language-contracts.md), [calls and inference](language/calls-and-inference.md) |
| Exact-schema access and admission | [Reader trust](representation/reader-trust.md), [format versioning](representation/format-versioning.md) |

IEEE exception handling remains disabled by default. This does not suppress
checked integer failures, runtime requirements or ordinary language throws.
Future opt-in IEEE status handling is outside this codec.

Operation inclusion does not register a source primitive or authorize
recognition by method spelling. Source overload selection remains owned by
the language and library contracts. Native calls retain their declared ABI
and caller-unsafety obligations. Current executable support is recorded in
[the implementation reference](../implementation.md), separately from this
target contract.

## Names, values and storage

| Concept | Meaning in this instruction model |
|---|---|
| Computed value | A typed result at a computation site. A consumer may give it an immutable SSA identity. |
| `R0..R15` | Sixteen encoded operand slots: fourteen general value bindings plus the selected zero and all-bits-set/discard roles described below. |
| Argument | A logical parameter binding described by the selected signature and parameter mode, not a permanent machine-register assignment. |
| Local | Typed logical storage with initialization, access, backing and lifetime state. Promotion does not change its source contract. |
| Place | Selected storage that can be read, initialized, replaced, borrowed or transferred under its own permissions. Not every place exposes a data address. |
| Immediate | A literal value encoded by an instruction form, not a read of a register or place. |
| Physical register/stack slot | A backend choice. One logical value may need several machine registers or backing storage. |
| SSA definition | A consumer's logical identity. Its numbering need not occupy the wire operand namespace. |

A class handle, a checked reference descriptor and a raw pointer are distinct
types with different authority and dependencies. Value types include all
primitives and aggregates; they are not restricted to machine-word payloads.
One large or generic value still occupies one logical register name.

Every ordinary operation reads its sources before publishing its destination
bindings. Rebinding a name does not itself write a local, retire a value,
release backing, or duplicate ownership.

All computations have ordered program positions. A consumer can immediately
create ordered value/operation nodes instead of deferring effectful trees.
Register selection does not authorize reordering a call, failing check or
memory observation. A stack format could also be imported this way; the
choice of register encoding is not a claim that stacks inherently require
effectful expression trees.

### Selected special operand slots

The two high-numbered special slots preserve R0/R1 for common compact
tuples. These are wire operand assignments, not reservations of physical
hardware registers.

| Encoding | Source role | Destination/unused role |
|---|---|---|
| R0..R13 | Currently available computed value | General result binding |
| R14 | Context-typed zero under the selected contract below | Not a writable general destination |
| R15 | All bits set in the admitted value representation; Boolean true as below | Discard/no destination where allowed; ordinary unused register field, with the recipe combined-token exception below |

The same R15 encoding has different meanings in different operand roles;
it does not hold mutable storage. A used source field means the constant.
An unused field is not read, and an admitted discard field does not publish
a register binding.

R14 is contextual zero. R15 is representation-oriented all bits set, not a
per-type maximum-value constructor. For an admitted same-width bit view,
its pattern is the one obtained by bitcasting an all-one integer into that
type. Floating-point is included, especially for masking and other
bit-oriented uses. Boolean retains its selected valid true contract.

| Admitted type | R14 source value | R15 source value |
|---|---|---|
| Signed integer | Zero in that complete integer type | All bits set, therefore -1 |
| Unsigned integer | Zero in that complete integer type | All bits set, therefore the maximum value |
| Bit-vector/mask domain | All zero | All one |
| Boolean | False in the valid Boolean representation | True in the valid Boolean representation |
| Binary floating-point | Positive zero | Negative quiet NaN with every payload bit set; the complete binary pattern is all ones |
| Decimal floating-point | Positive zero, coefficient 0 and exponent 0 | All bits set; under BID this is a negative signaling NaN with noncanonical raw payload |
| Eligible vector | The corresponding zero in every lane | The corresponding integer/mask/Boolean/floating constant in every lane |

Other types require their own admitted constant contracts; R14 does not
implicitly construct a null reference or default aggregate. Negative
floating zero and alternate decimal zero cohorts use their exact typed
ConstantMap entries. R15 is an explicit bit-pattern constant, not a
NaN-processing arithmetic result. Loading/copying it preserves its exact
sign, quiet/signaling state and raw payload; it does not quiet the value,
normalize a noncanonical decimal payload or substitute the positive
zero-payload constant-arithmetic recipe. An operation consuming it still
follows that operation's own result contract; admitting a constant does not
admit every operation on its type.

Under the current
[Windows x64 BID layout](targets/windows-x64-data-layout.md#3-ieee-decimal-bid),
the signaling bit has the opposite sense to the binary quiet bit. An all-one
decimal container is a negative signaling NaN. Its all-one raw payload also
exceeds the canonical decimal payload range and decodes to numeric payload
zero, without making the raw NaN container invalid.

For decimal32 BID the selected constant is exactly `0xFFFFFFFF`, as from
bitcasting signed integer -1. Decimal64/128 likewise use all one bits in their
complete containers. Do not clear the signaling bit or replace the pattern
with a largest canonical payload. Classification follows the selected value
representation, not host floating arithmetic.

These cannot be arbitrary typed storage registers containing universal
valid bit patterns:

- Integer zero and all-bits-set can mean zero and signed -1/unsigned maximum.
  A Boolean true is a Boolean value, not permission to create an invalid
  Boolean storage representation.
- The selected floating zero is a value/representation contract, not
  permission to substitute arbitrary numerically equal decimal cohorts.
  Reference and aggregate constants still need explicit admissibility
  and type information.
- If every input is a special constant, an untyped destination may not
  establish the result type. A typed constant entry or explicit typed form
  supplies that information; there is no default integer type.
  A previous destination binding does not permanently type that register.
  An RMW operation can obtain the type from its actual ordinary source.
- A discard destination does not cancel checks or effects, and cannot
  abandon an affine cleanup responsibility. Nontrivial results need their
  explicit retirement/transfer contract.
- An unused register field is not a value read. Its canonical encoding can
  reuse a constant slot without implying that the operation consumes that
  constant. Arithmetic recipes instead use their selected combined
  register/negation absent tokens.

Register-field parsing remains four bits. A writable general result must
name R0..R13. R15 is valid only when that result can be discarded under its
actual responsibility/effect contract. Pair loads/staging require general
destinations; an operation with independently discardable scalar results
can use R15 for those results. Distinct published general results must use
distinct bindings; repeated discard fields do not publish the same binding.
Other indices are not register encodings: local or method-map index 15
remains an ordinary valid index, not a discard sentinel.

### Cross-block values and phi-equivalent merges

A block declares its available incoming general-register bindings
(R0..R13), with their complete types and required state. General registers
not admitted by that entry state cannot be read merely because a predecessor
happened to leave bits in the corresponding location.

R14/R15 source constants are not incoming bindings or phi inputs. Their
admissibility follows their typed operand contracts at each use; they do not
need a predecessor to establish a mutable register value.

If `R0` is live into a join, an `int32` definition on one incoming edge and
a `float32` definition on another do not form one typed value. The producer
must convert both to an agreed type or explicitly construct a common
representation. Equal widths, nominal similarities and implicit boxing
do not supply that agreement.

If the join overwrites `R0` before any subsequent use, its incoming binding
is dead and the incoming types need not agree. A use in a later successor
before overwrite also makes the incoming binding live; this is not only
about the first instruction in the join.

The merge is phi-equivalent. Its result is an ordinary typed value and may
flow across further blocks. A consumer wanting SSA constructs the merge;
the wire need not serialize its version number. A definition counter handles
straight-line renaming, but joins and loop backedges still require merge
construction.

Type agreement does not erase distinct incoming roots, loans, ownership
responsibilities, reservations or place states. Their guarded relationships
must survive the merge. Promotable locals may require SSA merges too, even
if register flow were prohibited.

## Method implementation forms

Every Body row begins with an `implementationKind:u8`. Kinds `01..05` are
assigned. Their exact descriptors and eligibility rules appear in
[Body row layout and compact implementations](#body-row-layout-and-compact-implementations).
A bodyless import, abstract declaration or other declaration that has no
implementation has no Body row; it does not use an empty stream.

The compact forms are exact implementations of their own MethodDef, not
method or field aliases. They retain the method's identity, visibility,
complete signature, effects, requirements, cleanup, initialization and
output obligations. Producer truth is required; consumers validate target
kind, type/signature, access and contract compatibility before use. A member
name, attribute or matching signature alone does not establish eligibility.
Imported targets, constructed-owner adaptations, selected dispatch and
other non-identity mappings use an instruction stream unless an exact
bound-reference form is assigned.

### Body row layout and compact implementations

The Body section remains the offset-indexed variable table selected by
[metadata](il-metadata.md#body-table-and-coded-instruction-boundary).
Its row is either one of the compact descriptors above or an instruction
stream. The implementation-kind byte makes the distinction explicit; no
reader infers a body form from its length or contents.

The compact descriptor encodings are exact:

| Kind | Payload after the common kind byte | Eligibility |
|---:|---|---|
| `02` field read | `flags:u8=0, reserved:u16=0, fieldToken:u32` | The MethodDef receiver/static shape matches the FieldDef; it has no explicit value inputs and returns the exact readable field value. Non-Copy values require an independently admitted reference/transfer contract and cannot be copied by this form. |
| `03` field write | `flags:u8=0, reserved:u16=0, fieldToken:u32, formalOrdinal:u16, sourceUse:u8, reserved:u8=0` | Exactly one selected value formal maps to the field's complete type; the method returns unit. `sourceUse` is `1=copy`, `2=transfer`, or `3=borrow`. The source contract must support the selected access/retention and state transition. |
| `04` field reference | `flags:u8=0, reserved:u16=0, fieldToken:u32, dependencyContractRow:u32` | The MethodDef result is the exact checked reference to the field, and the referenced MethodDef Dependency clause supplies its result/backing relationship. It is not a pin or lifetime extension. |
| `05` identity forwarder | `flags:u8=0, reserved:u16=0, methodToken:u32` | Receiver, formal order/modes and complete result identity match the selected target after ordinary substitution. No conversion, argument reordering, added operation, or hidden destination adaptation is permitted. |

For kinds `02..04`, `fieldToken` is a fixed `u32` token with kind FieldDef
or Import; an Import's retained expected definition must be a FieldDef.
For kind `05`, `methodToken` has kind MethodDef or Import, and an Import's
expected definition must be a MethodDef. Token resolution and exact binding
validation remain mandatory. A direct descriptor applies in the owning
MethodDef's generic declaration context; any additional constructed-owner,
dispatch or witness adaptation requires an ordinary stream.

These forms retain the wrapper MethodDef's identity, signature, effects,
requirements, visibility, caller-unsafety and output/cleanup contracts.
Unsupported required descriptors reject; they are not treated as empty
methods or aliases.

An instruction-stream row is laid out in this exact order:

| Field | Width | Rule |
|---|---:|---|
| `implementationKind` | `u8` | `01` |
| `instructionEncoding` | `u8` | `01` selects this register codec |
| `mapMask` | `u8` | Bits 0..4 indicate the presence of HomeMap, TypeMap, MethodMap, FieldMap and ConstantMap in that order; bits 5..7 are zero |
| `reserved` | `u16` | Zero |
| Present map counts | `u32` each | Only maps whose corresponding bit is set serialize a count, in Home/Type/Method/Field/Constant order; a set bit with count zero is valid noncanonical input |
| `blockCount` | `u32` | Count of fixed 12-byte Block entries; 1..65,536 |
| `recordCount` | `u32` | Count of typed operand records; 1..65,536 |
| `codeByteLength` | `u32` | Exact final instruction-stream extent |

Let `K=popcount(mapMask)`. The prefix is exactly `17 + 4*K` bytes (17..37):
five initial bytes, one four-byte count per present map, then the three
mandatory counts. An absent map has no count word or entries. Canonical
writers clear bits for empty maps; a scoped prefix-canonicality audit rejects
empty-present maps, while ordinary readers accept them. After the prefix,
the row contains the present maps in Home/Type/Method/Field/Constant order,
the fixed Block table, typed-record offset index and data, and finally
exactly `codeByteLength` instruction bytes. No padding is inserted between
instructions or tables. A Body row has no unclaimed trailing bytes.

For effective map counts `H,T,M,F,C`, mandatory counts `B,R`, record-data
extent `D` and code extent `L`, the exact stream Body extent is
`17 + 4*K + 8*H + 4*T + 4*M + 4*F + 8*C + 12*B + 4*(R+1) + D + L`,
where `K=popcount(mapMask)`. Products and sums use checked arithmetic and
must fit the containing Body extent.

All Body-local map, home, block and typed-record indexes are zero-based
`u16` values; `0xFFFF` is an ordinary usable index. Each corresponding
inventory count is `u32` and ranges from zero through 65,536. An absent
reference is represented by its explicit kind/presence field, never by
stealing a valid index. Global table references remain their schema-owned
fixed-width references and do not inherit this local bound. Code offsets,
byte lengths and instruction ordinals are separate `u32` domains.

| Map | Entry format | Meaning |
|---|---|---|
| HomeMap | `u8 role, u8 flags, u16 typeMapIndex, u16 formalOrdinal, u16 reserved=0` | `role`: 1 receiver, 2 formal, 3 local. `formalOrdinal` is zero-based for role 2 and zero otherwise. Only role 3 may set `flags` bit 0 (`rebindable`); other bits reject. Locals begin vacant; receiver/formal storage begins according to the MethodSig and parameter mode. |
| TypeMap | `u32 token` | Fixed four-byte type token resolving a complete type under the owning module context. |
| MethodMap | `u32 token` | Fixed four-byte MethodDef or Import token retaining the exact selected signature/contract. |
| FieldMap | `u32 token` | Fixed four-byte FieldDef DefId, FieldDef Import token or FieldPath token; an entry grants no access or storage capability. |
| ConstantMap | `u32 TypeSpecRef, u32 ConstantSource row` | One complete type and one exact frozen/target-constant source; the source's complete type must match. |

HomeMap entries are distinct storage identities and are never deduplicated.
At most one entry has role `1` and it is valid only when the MethodSig has a
receiver. A role-`2` entry names an existing formal ordinal, occurs at most
once for that ordinal and has the complete substituted formal type. Role-`3`
entries have ordinal zero and represent distinct locals; they begin vacant.
Role-`1`/`2` entries have flags zero, and only role `3` may set the
`rebindable` flag.

Every TypeMap, MethodMap, FieldMap and ConstantMap index uses its own zero-based
namespace, regardless of equal numeric values. Map entries may be repeated;
ordinary readers preserve meaning, while canonical writers order by first
logical use, reuse identical tokens/values and omit unused entries.
HomeMap entries are distinct storage identities; their order is preserved
unless a transformation remaps every Home reference. This schema does not
claim one canonical ordering for HomeMap locals.

The Block table contains exactly `blockCount` fixed 12-byte entries in
zero-based Block-index order. Each entry is:

```text
u32 codeOffset, u32 codeLength, u16 entryStateRecord, u16 flags=0
```

`codeOffset` and `codeLength` select one nonempty instruction range within
the final code byte array. Block ranges are disjoint and in block-index order;
together they cover every instruction byte exactly once. The block index is
the branch target namespace. Each block ends with exactly one terminal
instruction; fallthrough is never inferred from adjacent byte ranges.
`entryStateRecord` names a typed `BlockEntryState` record.

A `BlockEntryState` payload is `u32 registerCount` followed by that many
five-byte rows `u8 register, u8 carrierKind, u16 typeMapIndex,
u8 accessKind`. Rows are sorted by register and name only available general
bindings R0..R13. R14/R15 are constant/discard roles, not incoming bindings.
`carrierKind=0` identifies an ordinary typed value and requires
`accessKind=0`; `typeMapIndex` names its complete value type.
`carrierKind=1` identifies an internal typed place handle and requires
`accessKind=1..5`, using the PlaceSelection access domain; `typeMapIndex`
then names its complete referent type. A checked reference remains an
ordinary typed value, not a place handle.
Carrier `2=TypeIdentity` identifies an opaque internal type identity,
not the queried type's value or a reflection object. It requires
`typeMapIndex=0` as an unused reserved field and `accessKind=0`; zero does
not reference TypeMap[0]. TypeIdentity is produced only by `DE` TypeOf and
may flow through `mov`, these entry bindings/merges, and its admitted
comparison/conditional-selection operations. It is not a stored or public
type, constant, call value, return value, pointer or branch condition.

The row declares the carrier, referent/value type and place authority class.
Dynamic selected-target identity, backing dependencies, loans, roots and
guarded state are reconstructed from the defining operations and incoming
edges by the mandatory per-function verifier. A merge must preserve those
facts; if it cannot represent a required relationship, the consumer rejects
affected use rather than treating a place handle as a value or reference.
For TypeIdentity, every live incoming edge must supply the internal carrier
and its exact identity evidence; the represented identities may differ across
edges, and the merge selects the actual edge's identity.
Home initialization, replacement, owner and cleanup states are likewise
verified from typed Homes, operations and declared contracts; these facts
are not assigned per-expression IDs. A consumer does not rescan a callee's
body at each call: it consumes the verified published contract.

`recordOffsets` is `u32[recordCount+1]`, relative to record data. It begins
at zero, is strictly increasing for each present record and ends at the exact
record-data extent. A record starts with
`u8 recordKind, u8 flags=0, u16 reserved=0`; the record index addresses that
typed extent and its kind fixes the payload. The assigned record kinds and
payloads are listed in [Typed operand records](#typed-operand-records).
Unknown kinds, flags, reserved values, truncated extents and trailing row
bytes reject before affected use.

`instructionEncoding=0x01` selects the mixed codec above. Every other selector
is unassigned. It does not select source arithmetic policy, runtime/target
requirements or semantic features. A future codec needs a new exact draft
schema and never receives guessed decoding from equal format bytes.

### Typed operand records

Typed-record references are zero-based `u16` indexes into the owning Body's
record map. The count is `u32`, at most 65,536. Each record has the four-byte
header described above; its kind fixes the exact payload and extent. A
`ValueSelector` is `kind:u8,index:u16`: kind `0=None` requires index zero,
kind `1=Register` selects R0..R15, and kind `2=Home` selects a HomeMap entry.
None is an explicit absent selector, not an implicit default, discard
register, or missing-index sentinel. In a CallSite input position it is valid
only for an exact unit formal. In a result position it is valid for an exact
unit result or a target verified to have `DoesNotReturn`; the latter has no
normal result even when the declared result type is non-unit. In a place
record it marks an unused base/index field. Special register roles remain
governed by the operation's source or destination position.

| Record kind | Payload |
|---:|---|
| `01` BlockEntryState | `u32 registerCount`, then `registerCount` rows `u8 register, u8 carrierKind, u16 typeMapIndex, u8 accessKind`; at most 14 unique sorted general registers |
| `02` CallSite | `u8 targetKind, u8 resultKind, u16 flags=0, u16 targetIndex, u16 resultIndex, u32 inputCount, u32 preparationCount`, then 4-byte input rows and `u16` preparation-record indexes |
| `03` DestinationPreparation | `u8 destinationKind, u8 priorState, u8 transition, u8 sourceUse, u16 destinationIndex, u8 hasFormal, u8 flags=0, u16 formalOrdinal, u16 reserved=0, u32 dependencyContractRow` |
| `04` PlaceSelection | `u8 targetKind, u8 accessKind, u8 destinationKind, u8 baseKind, u16 destinationIndex, u16 baseIndex, u8 indexKind, u8 targetRefKind, u16 indexIndex, u32 targetRef, u16 referentTypeMapIndex, u16 flags=0` |
| `05` SemanticStore | `u8 placeKind, u8 sourceKind, u8 transition, u8 sourceUse, u16 placeIndex, u16 sourceIndex, u32 dependencyContractRow, u8 hasFormal, u8 flags=0, u16 formalOrdinal` |
| `06` ConditionalEdge | `u16 trueBlock, u16 falseBlock` |
| `07` Switch | `u8 selectorKind, u8 flags=0, u16 selectorIndex, u16 typeMapIndex, u16 defaultBlock, u32 caseCount`, then `caseCount` rows `u16 constantMapIndex, u16 targetBlock` |
| `08` RequirementFailure | `u16 exceptionTypeMapIndex, u16 constructorBindingRecordIndex, u8 conditionKind, u8 conditionRegister, u8 predicateMask, u8 flags=0` |
| `09` Throw | `u8 payloadKind, u8 selectorKind, u16 selectorIndex, u16 lexicalRegionIndex, u16 flags=0` |
| `0A` RegionExit | `u8 exitKind, u8 hasTarget, u16 targetBlock, u32 regionCount`, then `regionCount` `u16` region indexes from inner to outer |
| `0B` ConstructorBinding | `u16 methodMapIndex, u16 exceptionTypeMapIndex, u32 suppliedCount=0, u32 formalCount, u32 bindingCount`, then `bindingCount` rows `u32 formalOrdinal, u8 bindingKind, u8 sourceUse, u16 constantMapIndex` |

`CallSite.targetKind` is `1=MethodMap`, `2=Register`, or `3=Home`;
`resultKind` is `0=None`, `1=Register`, or `2=Home`. A non-None target or
result uses the corresponding zero-based index; a None result requires
`resultIndex=0`. Each input row is
`u8 selectorKind, u8 sourceUse, u16 selectorIndex`; selector kinds are
`0=None`, `1=Register`, and `2=Home`. None is valid only for an exact `void`
formal and then `sourceUse`/`selectorIndex` are zero. Other `sourceUse` values
are `1=copy/read`, `2=ownership transfer`, `3=borrow read`,
`4=borrow write`, or `5=initialization destination`; the complete signature
and type determine which mode is valid. A special register constant is
selected through Register R14/R15, not an untyped constant selector. Inputs
are already evaluated and appear in formal order. The record does not
evaluate source expressions or reorder them.
`preparationCount` entries name DestinationPreparation records in the
dependency-valid order selected for that invocation.

When the bound target has a verified `DoesNotReturn` effect, the call is
terminal: it must use a general CallSite form, end its Block, have no normal
successor, and use the None result selector even if its complete signature
declares a non-unit result. The target retains that full signature. Compact
window forms encode normal-returning calls only; they do not encode a
DoesNotReturn call. A normal unit-returning CallSite also uses None but
continues normally; the resolved target contract distinguishes these cases.
A normal non-unit call requires a register or Home result selector.

`DestinationPreparation.destinationKind` is `1=Register` or `2=Home`;
`priorState` is `1=Vacant`, `2=Live`, `3=MaybeLive`, `4=Partial`,
or `5=Reserved`; `transition` is `1=Initialize`, `2=Replace`,
or `3=Publish`; and `sourceUse` has the CallSite source-use meanings above,
with zero allowed only for a result-only destination. `hasFormal` is zero or
one; when zero, `formalOrdinal` and `dependencyContractRow` are zero. A
nonzero dependency row is a typed Contract-row reference whose kind must be
applicable to that formal and transition. These records do not authorize
cleanup-before-evaluation, ignore failed preparation, or weaken reentrancy
confinement.

For `BlockEntryState`, `carrierKind` is `0=Value`, `1=PlaceHandle` or
`2=TypeIdentity`. Kind 2 requires `typeMapIndex=0` and `accessKind=0`;
the type field is unused, not TypeMap[0].
Value rows require `accessKind=0`; place rows use `accessKind=1=Read`,
`2=Write`, `3=Initialize`, `4=Transfer`, or `5=Reference`. The TypeMap
entry is the complete value type for a value row and the complete referent
type for a place row. The access class does not encode dynamic target,
backing or loan identity; mandatory verification carries those facts across
edges and rejects an unsupported merge before use.

`RequirementFailure.conditionKind` is `1=Boolean` or `2=UInt8PredicateMask`.
The record selects the current condition value through `conditionRegister`
(R0..R15). Kind 1 requires a complete `bool` value and `predicateMask=0`;
kind 2 requires `uint8` and tests `(value & predicateMask) != 0`, with
`predicateMask` in `0..15`. R14/R15 are valid condition sources only with
their selected, type-correct constant meanings. Both B3 and E7 read this
condition from the referenced record; their instruction bytes do not omit or
implicitly select a condition. Reserved flags must be zero, and other
condition kinds or type/mask combinations reject before affected use.

`PlaceSelection.targetKind` is `1=Home`, `2=Field`, `3=Element`,
or `4=StaticField`. `accessKind` is `1=Read`, `2=Write`, `3=Initialize`,
`4=Transfer`, or `5=Reference`. Selector kinds for destination/base/index
are `0=None`, `1=Register`, and `2=Home`; unused selectors use None with
index zero. `targetRefKind` is `0=None`, `1=FieldDef row`, or
`2=MethodMap index`; the selected target kind fixes which is required.
Home targets use the HomeMap index in `targetRef` and zero `targetRefKind`;
field targets use an exact FieldDef row; element targets use a bound MethodMap
entry for the admitted index operation, with upper `targetRef` bits zero.
The complete referent type is
`referentTypeMapIndex`. Place handles capture their selected inputs and
checks; they are not source references, pointers, pins or owner extensions.

`SemanticStore.placeKind` and `sourceKind` use the ValueSelector kinds, with
the destination required to designate a typed writable place: an internal
PlaceHandle or a checked writable reference whose own type/access contract
governs the referent. These carriers remain distinct; the selector does not
convert a reference to a PlaceHandle or expose a native address.
`transition` and `sourceUse` use the DestinationPreparation domains.
`hasFormal` is zero or one. When zero, `formalOrdinal` and
`dependencyContractRow` are zero; when one, `formalOrdinal` identifies the
correlated formal and a nonzero dependency row must apply to that formal and
MethodDef. This record performs one typed semantic store, not an arbitrary
instruction program.

`Switch` keys are exact typed ConstantMap values of the selected scalar type,
unique under that type's switch equality contract. `selectorKind` is
`1=Register` or `2=Home`; None is invalid. Targets are absolute Block
indexes. A default target is always present. `Throw.payloadKind` is
`1=FreshExceptionValue`, `2=ActiveHandlerEvent`, or `3=CapturedExceptionOwner`.
Fresh and captured payloads use a register/home selector; active-handler
rethrow uses `selectorKind=0, selectorIndex=0` and the exact lexical
`lexicalRegionIndex`. Fresh and captured payloads use `lexicalRegionIndex=0`.
No route synthesizes a copied event or thread-global current exception.
`RegionExit.hasTarget` is zero or one; absent targets use targetBlock zero,
while present targets may use any valid Block index. Region indexes are
ordered innermost to outermost.

The `Throw` and `RegionExit` record shapes do not assign the ExceptionRegion
inventory, handler/filter/finally rows or runtime event carrier. Consequently
`Throw.payloadKind=2` and all `RegionExit` records require that separately
assigned region carrier; they cannot be admitted or executed in its absence.
The same capability boundary applies to handler dispatch and cleanup for an
executed `unreachable`. These instruction/record encodings do not create a
hidden region table or establish support for exception handling.

`ConstructorBinding` identifies the exact ordinary zero-supplied-argument
constructor binding selected for `require`. Its method target's result is
the selected exception type. `formalCount` matches its signature;
`suppliedCount` is zero. Each supplied-to-formal binding row covers one formal: kind `1` selects an
optional default through ConstantMap, and kind
`2` selects an admitted empty params expansion through ConstantMap. Binding
rows are in formal order and `bindingCount` equals `formalCount`.
`sourceUse` uses the CallSite source-use domain; zero is valid only for an
exact unit formal, and the selected use must satisfy that formal's mode and
ownership contract. A
zero-parameter constructor has zero rows. Other construction recipes are
not implicit in this record; lower them to ordinary conditional control
flow or reject affected use. `RequirementFailure` binds that plan to the
exact exception type. A false predicate constructs once and creates the
selected fresh failure event; condition or construction failures propagate
unchanged.

Unknown record kinds, unsupported record/source-use/transition combinations,
unowned records that carry required semantics, malformed extents and
inconsistent references reject before affected use. Canonical writers omit
unused records and use first logical-use order; ordinary readers accept
consistent reordered or duplicate records where all references preserve
meaning.

## Width and operation-family allocation

The assigned codec uses sixteen one-byte tiny tuples, two-byte short
operations/restricted forms, and four-byte long families. It has no
two-windows-for-every-operation scheme.

The size study measured that earlier implied-window scheme, which consumed
128 first-byte values. Its results explain the potential benefit and the
concentration in a small subset; they are not a definition of the active
allocation or an optimized measurement of the new layout.

### Format-pressure study

A static study inspected 29 selected methods from the installed .NET 11
RC2 CoreLib and LINQ assemblies, including actual
`System.Numerics.Decimal32` methods and their generic IEEE-decimal helpers.
Both assemblies identify revision
`11.0.0-rc.2.26504.105+820230c386db5b0106c5430b3e5aee92a7826bf5`.

The study maps CLR evaluation-stack positions mechanically to registers,
without register allocation, inlining, local promotion or SSA optimization.
All models use the same resulting operation sequence. Consecutive argument
loads can form one load-pair operation. A stack pop emits no operation;
a duplicate emits a register transfer. This tends to favor low-register
windows and retains CIL local traffic that an optimized register lowering
might remove. It is not an optimal register allocation or a completed
Terrum instruction selection.

The following figures are **modeled instruction-stream bytes**, not actual
Terrum encodings, complete artifact sizes, native code sizes or runtime
measurements:

| Selected group | Methods | All operations four bytes | Mixed 2/4 | Current implied-window 1/2/4 | Entire block either two or four bytes |
|---|---:|---:|---:|---:|---:|
| Collection methods | 4 | 548 | 328 | 261 | 520 |
| LINQ selection/iteration | 3 | 612 | 364 | 301 | 550 |
| Scalar methods and wrappers | 15 | 496 | 284 | 202 | 386 |
| Generic IEEE-decimal helpers | 4 | 2,872 | 1,880 | 1,744 | 2,694 |
| CLR Decimal software helpers | 3 | 5,752 | 3,512 | 3,215 | 5,358 |
| Total of this selection | 29 | 10,280 | 6,368 | 5,723 | 9,508 |

Mixed 2/4 forms save about 34-43% against the same operations all using
four bytes in these groups. The implied one-byte windows save a further
7.2% in the generic IEEE-decimal helpers, 20.4% in the collection methods
and 10.1% across this selection. Tiny wrappers benefit more than the large
helpers. The total is not a representative frequency distribution of
CoreLib execution or of all applications.

#### Instruction counts and percentage bases

The modeled instruction sequence is held fixed. Supporting shorter forms
changes its encoded byte count, not its instruction count. Argument-pair
combining has already been applied equally to every model. These are not
counts of native instructions or unchanged original CIL instructions.

| Method | Modeled instructions in every format | All four-byte bytes | Mixed 2/4 bytes | Implied-window 1/2/4 bytes |
|---|---:|---:|---:|---:|
| `List<int>.get_Item` | 10 | 40 | 24 | 18 |
| `List<int>.Add` | 30 | 120 | 70 | 50 |
| `List<int>.Insert` | 52 | 208 | 124 | 101 |
| `List<int>.RemoveAt` | 45 | 180 | 110 | 92 |
| Collection subtotal | 137 | 548 | 328 | 261 |
| `Number.CompareDecimalIeee754` | 79 | 316 | 188 | 161 |
| `Number.AddDecimalIeee754` | 335 | 1,340 | 912 | 865 |
| `Number.MultiplyDecimalIeee754` | 98 | 392 | 244 | 222 |
| `Number.DivideDecimalIeee754` | 206 | 824 | 536 | 496 |
| Generic IEEE-decimal subtotal | 718 | 2,872 | 1,880 | 1,744 |

The width distributions are:

| Group | Encoding | One-byte instructions | Two-byte instructions | Four-byte instructions | Total instructions | Bytes |
|---|---|---:|---:|---:|---:|---:|
| Collections | All four | 0 | 0 | 137 | 137 | 548 |
| Collections | Mixed 2/4 | 0 | 110 | 27 | 137 | 328 |
| Collections | With 1/2/4 | 61 | 52 | 24 | 137 | 261 |
| Generic IEEE-decimal | All four | 0 | 0 | 718 | 718 | 2,872 |
| Generic IEEE-decimal | Mixed 2/4 | 0 | 496 | 222 | 718 | 1,880 |
| Generic IEEE-decimal | With 1/2/4 | 136 | 360 | 222 | 718 | 1,744 |

Collections save 220 bytes when two-byte forms become available, then another
67 bytes when one-byte windows become available. The 20.4% is that additional
67-byte reduction relative to the 328-byte mixed stream. Some implied
one-byte forms replace a four-byte form directly, not a two-byte form.

The IEEE-decimal group saves 992 bytes when two-byte forms become available,
then another 136 bytes with one-byte windows. The 7.2% is that additional
136-byte reduction relative to 1,880 bytes, not an increase in size and not
7.2 percentage points relative to the all-four-byte baseline.

The 1/2/4 stream is smaller in every comparison here. The separate concern
is primary opcode-space cost. The model does not spend the opcode space freed
by removing one-byte forms on making more operations short, so it does not
compare two independently optimized final encoders. The sixteen tiny tuples are assigned below after accounting for compact
method bodies; this study does not certify their optimality.

In the same model, the sixteen most useful potential one-byte forms account
for 519 of the 645 saved bytes: 80.5% of the one-byte benefit and 8.2% of
the mixed 2/4 stream. Thus one-byte forms need not be all-or-nothing.
A smaller set may retain much of the benefit without allocating both
windows for every one of 64 common operations. The study does not establish
an optimal final set.

The assigned sixteen tiny tuples are a bounded initial allocation, not a
claim of a complete .NET 11 CoreLib frequency study or a permanently optimal
ranking. Changing the allocation requires a reviewed draft-schema revision.

The ranking is for instruction-stream bodies. Field-backed and forwarding
implementation descriptors could remove argument loads, field accesses,
calls and returns from that population entirely. The sample contains fifteen
methods in its scalar/wrapper group, not fifteen certified forwarders.
`List<int>.get_Item` has bounds/access behavior and is not a plain automatic
field getter. Classify eligible compact implementations before extrapolating
the ranking; the change could be material rather than a small adjustment.

The study excludes compact method bodies, condition-consumer families,
arithmetic recipes, typed records and maps. Its figures remain explanatory
evidence only; they do not measure complete artifact size or runtime cost.

Important limitations:

- Maps, constants, operand/call/switch records, block-entry state, indexes,
  mode tags, source data and padding are excluded.
- The two width models share the same short-form eligibility; the study
  does not use newly available opcode space to compress more families.
- Its eligibility predates the full-only checked/saturating policy and
  any special-register reservation. It is not a measurement of the
  completed updated encoder or a development/release runtime comparison.
- Metadata indices use an explicitly chosen small-index policy and maps
  ordered by first appearance. A final codec or allocator can differ.
- Comparisons producing values use a general four-byte form; comparison
  branches are modeled as fused four-byte operations.
- Memory/address and prefixed operations conservatively use full forms.
  This is not a complete type-aware lowering or a proof that the
  mechanical transcription is executable Terrum.
- CLR shift, Boolean, initialization and ownership semantics are not
  silently adopted. The sample does not qualify Terrum's affine or
  first-class-unit contracts.

Detailed method, assembly-hash, model and form-frequency evidence is retained
as session research data. These figures contextualize the allocation; they
do not establish an optimal encoding or a performance claim.

### Long-family byte layout

A long instruction is exactly four bytes:

```text
primary:u8 | control:u8 | payload:u16 little-endian
```

The primary byte both selects the four-byte length and identifies its
registered family. The payload is not recursively decoded. For register
families `C0..CC`, its nibbles are result0/result1/source0/source1 from
least-significant to most-significant. `D0..D3` instead use source/
destination register nibbles and a TypeMap index; `D4..DD` and `E0..EB`
use the exact register, record or map layouts in the assigned long-family
table. Unused fields and unsupported controls reject.

The fixed sixteen-tuple tiny allocation and the assigned short/long
families above form the complete current codec. No other family, width
prefix, escape or vector/memory/atomic instruction follows from remaining
byte values.

### Ternary logic controls

For one bit from each of A, B and C, index the truth table as
`(A << 2) | (B << 1) | C`:

```text
index  A B C
  0    0 0 0
  1    0 0 1
  2    0 1 0
  3    0 1 1
  4    1 0 0
  5    1 0 1
  6    1 1 0
  7    1 1 1
```

Reading each input down the rows as an eight-bit column gives:

```text
A = F0
B = CC
C = AA
```

Apply a Boolean expression to those columns, restricting complement/results
to eight bits, to obtain its control byte. These values are truth-table
data, not instruction opcode assignments.

| Function | Control |
|---|---|
| Constant false | `00` |
| Constant true | `FF` |
| A, B, C | `F0`, `CC`, `AA` |
| `not A` | `0F` |
| `A and B` | `C0` |
| `A or B` | `FC` |
| `A xor B` | `3C` |
| `nand(A,B)` | `3F` |
| `nor(A,B)` | `03` |
| `xnor(A,B)` | `C3` |
| `(A and B) or ((not A) and C)` | `CA` |
| Majority: at least two inputs are one | `E8` |
| Minority: at most one input is one | `17` |
| `((not A) or B) xor C` | `65` |

Majority is `(A and B) or (A and C) or (B and C)`. Minority is its
complement. For the last example:

```text
((not F0) or CC) xor AA
= (0F or CC) xor AA
= CF xor AA
= 65
```

An immediate can therefore select every Boolean function of three inputs,
not merely a permutation of the inputs. The same construction extends to
more inputs, but requires one truth bit per input combination: three inputs
fit exactly into eight bits; four need sixteen bits.

On an admitted integer/bit-vector type, apply that function independently
at each bit position and preserve the declared width. Constant true then
means all bits set, not integer one. Scalar Boolean and vector-mask result
representations need their own admitted contracts. This does not make raw
bitwise operations valid on arbitrary owners, reference descriptors or
invalid value encodings.

The `CA` function is per-bit selection by A's mask. It is not the same as
whole-value conditional movement on one Boolean/condition result unless
the required full-width mask relationship has been established.

Named register operands must still be valid typed inputs. A control byte
that ignores one input does not erase a preceding effectful computation,
check or required cleanup. The instruction has no implicit memory operands,
short-circuit argument evaluation, or permission to reinterpret a reference
as bits.

### Arithmetic recipe controls

An analogous `ternarith` family can use the full control byte for selected
recipes built from `neg`, `add`, `sub` and `mul`. Unlike Boolean truth
tables, eight bits cannot describe every arithmetic function over arbitrary
numeric values. They select a finite, explicitly defined recipe catalog.

All four operations are eligible even when they can overflow. A recipe
compactly expresses the operations to perform; encoding it does not require
a proof that they succeed. JIT/AOT import reconstructs their typed, ordered
AST/IR nodes and then performs its normal canonicalization and optimization.
The recipe is not restricted to mathematical functions known to be total.

Failure/rounding distinctions below constrain semantics-preserving rewrites,
not whether a potentially failing operation can be represented. An importer
can normalize the tree or retain individual checked nodes as appropriate;
the wire need not serialize those intermediate node identities.

Both operation slots select all four operations, not only the binary three.
The recipe control byte uses this layout:

| Field | Bit positions | Meaning |
|---|---:|---|
| Inner operation | 0..1 | `0` neg, `1` add, `2` sub, `3` mul |
| Outer operation | 2..3 | The same operation-code convention |
| Outer operand order | 4 | Zero: inner result first; one: remaining input C first |
| A negation/modifier | 5 | Negate A, unless part of the absent token |
| B negation/modifier | 6 | Negate B, unless part of the absent token |
| C negation/modifier | 7 | Negate C, unless part of the absent token |

These are recipe-control data, not primary opcode assignments. The four
register nibbles remain destination plus A/B/C. Unary slots have explicit
roles:

```text
inner = innerOp(A, B)      // inner neg uses A; B is unused
result = outerOp(inner, C) // outer neg uses inner; C is unused
```

Absent operands use domain-specific combined source-selector/modifier
patterns:

| Recipe domain | Absent source pattern | Meaning |
|---|---|---|
| Integer | R14 with its negation/modifier bit one | Absent, not an evaluated negative zero |
| Floating | R15 with its negation/modifier bit one | Absent, not an evaluated negated NaN |

Recognize the combined token before reading or negating a value. Every
unused unary source must have that domain's token; a required source must
not have it. Inner negation therefore puts the token in B, and outer
negation puts it in C. Unmodified R14/R15 keep their constant meanings.
The integer encoding gives up no distinct zero value; an actual floating
negated all-one pattern remains expressible through a normal register or
typed ConstantMap.

This is a recipe-specific combined operand code, not the ordinary bare-R15
unused-field convention. It requires no extra bit or operand byte.

The order bit can instead make a binary outer operation
`outerOp(C, inner)`. Renaming the input registers then expresses right-nested
forms without a separate permutation selector. Outer `neg` ignores neither
the inner operation nor its failures; it negates the inner result.

Input-negation steps belong at their selected tree positions. For the
inner-first form, negate participating A/B, compute the inner operation,
then negate C if requested and perform the outer operation. For the
C-first form, perform its requested negation before the inner subtree.
The modifier bit belonging to an absent token is one, but causes no
negation step. Outer negation requires the irrelevant outer-order bit to
be zero. These unary rules admit 182 of the 256 raw control patterns;
equivalent recipes remain valid, so this is not a claim of 182 distinct
arithmetic functions.

The binary-only examples remain:

```text
(A op0 B) op1 C
A op0 (B op1 C)
```

Operand permutations can already be expressed by the three register input
fields. No on-wire local or SSA ID is required for the inner result.

Normalization is separate from encodability. In wrapping fixed-width
arithmetic, `x + (-y)` and `x - y` agree modulo the width. In checked
arithmetic they need not have the same failure behavior: for `int8`
`x = -1, y = -128`, `neg(y)` overflows, while `x - y` is the representable
value 127. Both recipes are valid to encode; the importer must preserve
the check or prove the rewrite valid rather than prohibit the recipe.

| Candidate recipe | Required distinction |
|---|---|
| `(A * B) + C` | Strict multiply followed by add is not single-rounding FMA. |
| `(A + B) * C` | Preserve its grouping, intermediate result and failure site. |
| `A - (B * C)` | Preserve noncommutative operand order. |
| `(neg A) + (B * C)` | Negation and the product have their own ordered semantics. |
| Input/result negation variants | Float sign-only behavior, signed zero, NaN state and checked minimum/unsigned failure cannot be replaced by algebra alone. |

Each selected control must define its operation tree, ordered steps, admitted
types, intermediate precision/rounding, overflow policy, floating signaling
effects, result publication and source/failure identities. A code meaning
negate C before the inner multiply is not generally interchangeable with a
source expression that multiplies first and only then negates C.

The primary selects one wrapping, checked, saturating or strict-floating
policy for the recipe's active nodes; the control byte does not silently
choose it. Mixed policies use separate recipe instructions. Fused operations
remain distinct, not another interpretation of a strict mul/add tree.
Recipe source/failure locations use their assigned SourceMap step roles.

Identity padding is not required. It is not universally equivalent even
without signaling NaNs: checked unsigned negation of all-ones fails rather
than supplying one; floating negation of all-ones supplies NaN rather than
one; adding negative decimal zero at quantum 0 can change a supplied
cohort. Producers may use actual identity operations where their complete
value/effect/representation contract permits, not as an implicit codec rule.

Fusion must also respect computations outside the recipe. In
`(a * b) + F()`, moving a checked multiply until after F's result exists
could execute F before an overflow that originally prevented it. Use a
compound form only when its complete ordering/effect contract matches,
or retain the ordinary sequence.

A fused multiply-add needs its own single-rounding contract. A compound
strict recipe must not contract to hardware FMA merely because the target
has that instruction. Future IEEE status and trap observations make the
intermediate steps significant even where ordinary numeric values coincide.

### Integer source profiles and explicit IL semantics

The standard source arithmetic profile selects the default policy for
ordinary integer add/subtract/multiply/negate and their compound/update
forms:

| Source compilation profile | Ordinary operation policy |
|---|---|
| Development or Debug | Checked |
| Release | Wrapping modulo the exact destination width |
| Explicit checked or wrapping operation | Its explicitly selected policy in every profile |

The compiler selects the profile before body checking and required constant
evaluation. A checked
Development/Debug operation can fail with the exact scalar failure; the
corresponding Release operation wraps. Division/remainder, shifts, conversions,
explicit API policies and floating operations keep their separate contracts.
The source profile does not create an ambient runtime arithmetic mode.

The wire distinguishes the selected arithmetic semantics and never defers
them to the consumer's build configuration. A JIT/AOT compiler importing a
checked artifact cannot reinterpret it as wrapping because it is optimizing
a Release build, or vice versa. Profile selection is compiled into the
explicit operation opcode/control; no ambient profile bit changes execution.
The Body does not need to retain a source-profile selector after this choice
has been resolved.

The IL supports both policies. Wrapping forms can use compact opcodes;
checked and saturating variants use full four-byte representations and
do not require matching one-/two-byte forms. Checked recipes remain valid
even when they may overflow. Internally, an
optimizer can use unchecked arithmetic with explicit required checks,
eliminate checks under valid proofs, or retain checked nodes. It must
preserve the selected failure and observation contract.

A proven-equivalent lowering can use a compact wrapping operation when a
checked result cannot overflow. This does not create a compact checked
opcode or let optimization discard a required failure.

Wrapping is not C-style undefined signed overflow or permission to mark a
potentially overflowing operation as impossible. Any separately proposed
assume/proven-no-overflow operation would need its own explicit contract.

An overflow analyzer remains useful, especially for unintended Release
wrapping, but it is not a substitute for checked semantics in
Development/Debug. A warning for every unknown-input operation that could
possibly overflow would be extremely noisy. Definite failures, suspicious
size/index calculations, arithmetic before widening and relevant flow facts
are better diagnostic candidates. Exact warning rules are not selected here.

Rust's [Cargo profile defaults](https://doc.rust-lang.org/cargo/reference/profiles.html)
enable overflow checks for development and disable them for release;
they are configurable. Its [overflow reference](https://doc.rust-lang.org/reference/expressions/operator-expr.html#overflow)
also retains checks for signed minimum division/remainder by -1 even when
overflow checks are disabled. The [overflow RFC](https://rust-lang.github.io/rfcs/0560-integer-overflow.html)
explicitly discusses the difficulty of static detection and runtime cost.
These sources explain the tradeoff; they are not measurements of Terrum's
performance.

Division by zero, signed-minimum division by -1, remainder, shift counts,
literal construction, numeric conversions and floating exception handling
remain separate contracts. Selecting Release wrapping for add/subtract/
multiply/negate does not change those behaviors. The static format-pressure
study measured encoded-size choices, not runtime costs of checking or
vectorization.
Runtime `require` and explicit throws do not become debug-only checks.

### Comparison conditions and consumers

The selected scalar comparison model produces ordinary `uint8` outcome
bits, with consumers for Boolean values, branches and conditional movement:

| Family | Intent |
|---|---|
| `cmp` | Compare admitted values and produce exactly one uint8 outcome bit under the selected comparison/signaling policy. |
| `setcc` | Materialize a uint8 predicate-mask test as ordinary `bool` or an admitted mask result. |
| `jcc` | Transfer control according to the uint8 predicate-mask test. |
| `movcc` | Conditionally replace a register binding; the nonselected path retains the old destination. |
| `selectcc` | Select one of two already available complete values into a destination. This can be a distinct form of the conditional-move family. |
| Floating classification | Test properties such as NaN, signaling NaN, finite, zero or sign without assuming the effects of an IEEE comparison. |

For a binary ordered numeric comparison, the natural condition information
is less/equal/greater/unordered. Integer/character comparisons do not produce
unordered; equality-only domains do not acquire ordering.

Condition-test mask bits 0..3 mean less, equal, greater and unordered,
respectively. Every subset is valid:

| Mask | Matching outcomes | Meaning for a single cmp outcome |
|---|---|---|
| `0` | None | Always false |
| `1` | Less | Ordered less than |
| `2` | Equal | Equal |
| `3` | Less/equal | Ordered less than or equal |
| `4` | Greater | Ordered greater than |
| `5` | Less/greater | Ordered not equal |
| `6` | Equal/greater | Ordered greater than or equal |
| `7` | Less/equal/greater | Ordered |
| `8` | Unordered | Unordered |
| `9` | Less/unordered | Less than or unordered |
| `A` | Equal/unordered | Equal or unordered |
| `B` | Less/equal/unordered | Less than or equal, or unordered |
| `C` | Greater/unordered | Greater than or unordered |
| `D` | Less/greater/unordered | Ordinary not equal |
| `E` | Equal/greater/unordered | Greater than or equal, or unordered |
| `F` | Every outcome | Always true for a cmp result |

These are predicate data values, not opcode allocations. In an admitted
domain that never produces unordered, the U bit simply never matches;
it is not an invalid control. Predicate 0/F does not erase effects of an
already executed comparison or its operand producers.

For two TypeIdentity operands, the existing binary relation result uses
only E=`2` for identical complete identities or U=`8` for known-distinct
identities. Thus ordinary `ne` uses mask `D` and matches distinct identities;
ordered-not-equal mask `5` does not. Ordered `lt/le/gt/ge` do not match U.
No numeric or floating comparison outcome changes, and U does not represent
missing type evidence.

The `cmp` result is exactly `uint8(1)`, `uint8(2)`, `uint8(4)` or
`uint8(8)`. Consumers accept every uint8 value, not only values with
comparison provenance, and use the test `(value & predicate) != 0`.
High uint8 bits do not match a four-bit predicate. Thus predicate F is
not unconditionally true for arbitrary masks: zero or high-bits-only masks
test false. Predicate 0 always tests false. This is a defined bit test,
not validation of a hidden enum or permission to infer a comparison.

These are four bits of predicate selection, not four independent possible
outcomes at once: a binary numeric relation has one of four outcomes.
Adding an independent signaling-policy flag gives five control bits and
32 truth/policy combinations, as distinct from a fifth relation state.
With IEEE exception handling disabled, the current predicate needs no such
in-band policy bit. Two four-bit predicates fit one control byte.

The outcome is an ordinary typed value, not a hidden architectural FLAGS/NZCV
register or a new opaque Body type. It uses existing uint8 storage, calls,
SSA construction, block-entry types, merges and liveness. A backend may use
native flags while their lifetime fits and the integer bits are not
observed; when observed, it must supply the promised uint8 value, not a
target-specific flags image. Scalar compound predicate chains instead use
the existing Boolean type.

```text
cmp   R3, R0, R1
setcc R4, R3, lt.ordered
jcc   R3, lt.ordered, Btrue, Bfalse
movcc R5, R3, eq, R6
```

These semantic forms use the assigned `C2..C5`, `A0..AF` and `C6` families.
The first comparison may supply a stored/returned Boolean through `setcc`, a
branch through `jcc`, or a whole-value selection. This addresses value
comparisons without requiring every relation to own an independent semantic
opcode.

`movcc` is register movement, not a conditional memory store. Its retaining
form requires a valid old destination on the nonselected path. Selection
does not execute deferred input expressions, implicitly drop a nonselected
owner, or replace checked reference/value semantics with bitwise masking.
Affine selection still needs its explicit responsibility-transfer contract.

The full control byte need not be in the same stage for every family.
A binary compare can carry signaling/evaluation controls while its consumer
carries the relation mask. `selectcc` fits a predicate byte and four register
nibbles. `setcc` needs fewer register fields and may have a compact form.
`jcc` also needs successors: arbitrary predicate/register/target combinations
cannot all be promised in four bytes. Use an explicit fallthrough contract,
target record, restricted form or bounded extension where necessary.

Compact relation spellings or fused compare/set/branch forms remain possible
physical encodings. They need not be available at every width. Their code
size and instruction-count effects require a new study; the published
format-pressure figures hold a different operation sequence fixed.

### Internal TypeIdentity values

`DE` TypeOf produces an opaque internal TypeIdentity for the selected
complete static type. It observes no Place value, existential payload,
hidden generic argument or object layout. The result is not the subject's
value, `System.Type`, a reflection object, metadata pointer or public type.
Metadata-only type recipes continue to bind their complete type reference
without executing TypeOf or materializing a register.

TypeIdentity equality uses complete type identity after the accepted
normalization rules: transparent aliases normalize to their targets; nominal
wrappers remain distinct despite equal layouts; qualifications and ordered
type/constant arguments retain their exact domains and values. Physical rows,
TypeMap indexes, names, assignability, layout equality and hashes alone never
establish identity.

TypeIdentity may be produced by TypeOf, aliased with `mov`, carried through
`BlockEntryState` carrier 2, compared by existing binary `C2` controls
`00..02`/short `40`, and selected with `C4`/`C5` under an ordinary uint8
predicate. Both comparison inputs must be available general bindings with
the TypeIdentity carrier; R14/R15 cannot manufacture an identity. For a live
open-generic value, the selected execution model must
supply exact complete identity evidence. Missing evidence rejects the
affected use; there is no unresolved-identity sentinel or reflection fallback.
The `C2` relation is total and non-signaling: identical means E; distinct
means U. `CB`/`CC` are not extended.

TypeIdentity cannot be stored in a Home, field, array, aggregate or optional;
passed to or returned from a call; captured, serialized, or placed in a
signature or ConstantMap; converted, bitcast, addressed, hashed, reflected or
used directly as a condition. Internal backend spills do not define an IL
storage or public ABI. Ordinary uint8/bool comparison results retain their
existing uses. Closed identity comparisons may fold to ordinary uint8/bool
constants or branches only when exact binding and any guarded refinement
obligations remain.

### Three-input comparisons

Three-input comparison families cover expressions such as `a < b && b < c`.
The selected control byte stores the A/B predicate in its low nibble and the
B/C predicate in its high nibble. AND versus OR belongs to the selected
family, not another bit stolen from those predicates.

Two ordered-less predicates give control `11`; ordered A<=B and B<C gives
`13`. This is not an arbitrary truth table over two four-way relations;
that would need sixteen result bits.

The three inputs need the same admitted complete comparison domain, or an
explicitly selected richer contract. The family does not invent numeric
promotions or replace arbitrary nominal comparison calls.

Scalar predicate chains produce an ordinary Boolean result:
apply the two selected predicates and combine them by the family's AND/OR
rule. Do not return a pair of raw relations or introduce another condition
kind. The result uses ordinary Boolean branching and selection; an extra
`setcc` is not required.

The semantic family labels `cmp3.and` and `cmp3.or` describe the two combining
rules without assigning primary opcode values. Producing Boolean IL does
not require a native Boolean register or `setcc`: a backend may retain the
result in flags and fuse its consumer when the operation/effect contract
permits. This is representation freedom, not a current support claim.

Signaling and evaluation order constrain this design:

- An eager predicate chain performs both primitive comparisons. It is not
  automatically a source short-circuit conjunction.
- A short-circuit chain must evaluate the first comparison, test it, and
  skip the second when required, including its invalid-operation effects.
- Producing C can itself have effects or fail. A three-input instruction
  consuming existing values cannot justify evaluating C's producer on a
  source path that never evaluated it.

With the default exception-disabled profile, the byte can be dedicated to
the two four-bit predicates. Conjunction/disjunction and eager/short-circuit
selection must still be specified by the family. Future exception-observable
operations can use an explicit helper/extended form rather than squeeze two
five-bit controls into eight bits.

### IEEE signaling and classification

IEEE exception handling is disabled by default. Ordinary floating operations
and comparisons do not produce language-visible NaN signals for either quiet
or signaling NaN inputs. Their specified values, quieting/sign-preserving
boundaries and representation rules still apply.

An implementation may incidentally set hardware flags under the selected
ABI, but those are not a portable default observation and software need not
emulate them. This does not disable checked integer failures, runtime
requirements or explicit language throws.

Future opt-in IEEE handling may be software-provided only. It need not
unmask native hardware traps or match the target's condition/status-register
representation. A special comparison/handling helper or explicit extended
operation can carry that policy without spending a fifth bit in each of
the default predicate nibbles. The helper API and activation mechanism are
not selected.

Keep three axes separate:

| Axis | Question |
|---|---|
| Predicate | Which comparison outcomes make the selected condition true? |
| NaN signaling | Does invalid arise for sNaN only, for any NaN, or under a separately defined value-only/classification operation? |
| Handling/observation | How is invalid represented, observed, masked or trapped under the selected floating environment? |

When IEEE exception-observable behavior is enabled, a quiet comparison
signals invalid for an sNaN; a qNaN
does not. A signaling comparison signals invalid for either kind of NaN.
The Boolean relation/unordered outcome is separate from that effect.
The SoftFloat [comparison functions](https://www.jhauser.us/arithmetic/SoftFloat-3/doc/SoftFloat.html#8.8)
describe both forms; AArch64
[FCMP](https://www.scs.stanford.edu/~zyedidia/arm64/fcmp_float.html) and
[FCMPE](https://www.scs.stanford.edu/~zyedidia/arm64/fcmpe_float.html)
provide a concrete quiet/signaling pair.

In that opt-in setting, signaling does not itself prescribe a Terrum
language exception or an unmasked hardware trap. The current accepted
[floating-environment chapter](language/floating-environment.md)
is value-oriented and exposes no ambient status/trap API. The intended
extension needs an explicit status/handling contract, mode availability,
effects and source/metadata/target requirements. A v1 implementation can
decline a declared unsupported mode; it cannot silently execute it quietly.
Encoding capacity alone does not establish full IEEE conformance.

If opt-in handling makes such effects observable, they occur at the selected
comparison/helper, not when `setcc`, `jcc` or `movcc` reads its condition.
Consumers do not repeat the original invalid event. Conversely, CSE or
fusion cannot discard, merge or move signaling comparisons merely because
their relation values are equal. Different signaling policies, intervening
status observations/clears, traps and required event origins matter.

A consumer selecting a less-than predicate does not change the signaling
policy of an earlier quiet compare. If the required operation has another
policy, produce that comparison or establish the exact effect equivalence;
relation equality alone is not sufficient.

Classification needs special care once exception behavior is observable.
In the default value-only profile, the unordered result of self-compare can
supply `is.nan` where the complete value contract agrees. In an IEEE
exception-observable profile, an sNaN would signal invalid for a quiet
self-compare but not for a non-signaling classification. The earlier warning
is about that effect distinction, not a prohibition on default no-signal
comparisons. A classification family remains useful for direct NaN/finite/
signaling-NaN/other tests. The RISC-V [FCLASS contract](https://docs.riscv.org/reference/isa/v20260120/unpriv/f-st-ext.html#20-1-9-single-precision-floating-point-classify-instruction)
is an example of classification without floating exception flags, not a
selection of its representation for Terrum.

The comparison condition must retain unordered as distinct from greater
or equal. `.NET CompareTo` can deliberately order/equate NaNs and therefore
is not a substitute for this raw relation contract.

### Per-block codec choices

Two proposals should not be conflated:

| Choice | Assessment |
|---|---|
| Pure two-byte blocks versus pure four-byte blocks | One operation that cannot use the narrow form widens the whole block. |
| Compact mixed 2/4 blocks versus full-word blocks | Avoids that narrow-block restriction, but introduces another decode/encoding table. Full-word mode must earn its benefit through capacity, fewer records/operations or measured import cost. |

For example, twenty narrow operations plus one full operation occupy 44
instruction bytes in a mixed stream, versus 84 when the entire block becomes
four-byte instructions. The study's fixed-by-block policy costs 49.3% more
stream bytes than mixed 2/4 across the selection, before mode tags or padding.
That does not measure possible reductions from a differently designed
full-word instruction repertoire.

This schema uses one mixed codec for every instruction stream. It has no
per-block width mode, and no block is split to manufacture a width boundary.
A future explicitly selected codec could define another scheme; no mode is
inferred from block contents.

### Semantic breadth and unassigned members

| Area | Representation pressure |
|---|---|
| Scalar arithmetic | Type-derived width/signedness/format, explicit checked/wrapping/fused policy; not one opcode per scalar identity. |
| Comparisons/consumers | Typed condition results, `setcc` Boolean/mask materialization, `jcc`, conditional movement, unordered and signaling behavior. |
| Classification | Non-signaling NaN/sign/finite tests or class results, distinct from comparison side effects. |
| Bit operations | Shifts/rotates, counts and bit selection; arbitrary ternary logic genuinely needs all eight truth-table bits. |
| Conversions | Source/destination identities and selected rounding/overflow/bit-preserving policy. |
| Storage and references | Locals, fields, indexed places, views, access authority and retained backing. Hardware addressing modes need not be copied wholesale. |
| Aggregates and calls | Multiple values, selected targets, larger lists and non-word-sized values; records remain legitimate. |
| Atomics | Operation, ordering and result contract. Some useful operations have more than four logical operands/results and need another form or aggregate result. |
| SIMD | Lane/mask semantics, shuffles, widening/narrowing and controls; constant tables or records handle controls that are not eight-bit immediates. |
| Lifetime/control | Transfers, cleanup, handlers, suspension and required state facts, without a register-class purity promise. |
| Cold target-specific functionality | Explicit registered intrinsics or calls; not every native mnemonic needs a primary portable opcode. |

.NET exposes much of its hardware breadth through callable intrinsics rather
than dedicated CIL instructions.
[FloatComparisonMode](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.intrinsics.x86.floatcomparisonmode)
includes ordered/unordered and signaling/non-signaling choices.
[VPTERNLOG](https://www.felixcloutier.com/x86/vpternlogd:vpternlogq)
uses all 256 truth-table values.
[VCMPPS](https://www.felixcloutier.com/x86/cmpps) has an immediate predicate
field. These facts motivate full selector fields and deliberate portable
signaling semantics, not a requirement to reproduce every native encoding
or make native condition/status registers part of the wire model.

AArch64
[FCMP](https://www.scs.stanford.edu/~zyedidia/arm64/fcmp_float.html)
records a four-way floating relation in NZCV, including an unordered flag
pattern. A portable IL can retain explicit values while a backend chooses
native condition flags. The
[RISC-V compressed extension](https://docs.riscv.org/reference/isa/v20260120/unpriv/c-st-ext.html)
provides a relevant precedent for intermixing 16/32-bit forms with operand
restrictions; its published compression results are not Terrum measurements.

The assigned schema uses family-specific four-byte forms and the exact
sixteen tiny tuples listed above. Other operation members do not acquire
assignments merely because a family or reserved range has capacity. A pure
fixed-width-per-block split and the old 128-value one-byte allocation are
not part of this codec.

## Active mixed instruction encoding

The current codec is `instructionEncoding=0x01`. It uses the first byte to
determine the complete instruction length; there are no prefixes, recursive
width selectors, variable-length integers or implicit per-expression IDs.
All multibyte fields are little-endian.

| First byte | Length | Capacity | Interpretation |
|---|---:|---:|---|
| `00..0F` | 1 byte | 16 | Assigned tiny tuples with all operands implied |
| `10..BF` | 2 bytes | 176 | Assigned short forms followed by one operand byte |
| `C0..FF` | 4 bytes | 64 | Assigned long families followed by a 24-bit payload |

Every unassigned primary value is invalid. A reader never guesses a family,
falls back to another codec, or recursively decodes a payload byte as an
instruction start.

```text
tiny:  opcode:u8
short: opcode:u8, operand:u8
long:  opcode:u8, control:u8, payload:u16
```

For short forms the operand byte is `y:x`, with `x` in the low nibble and
`y` in the high nibble. For ordinary long forms, the payload is
`nibble0 | nibble1<<4 | nibble2<<8 | nibble3<<12`; the named operation fixes
the role of each nibble. Long forms that carry a map/record index use its
complete little-endian `u16` payload and reserve all unrelated control bits
as zero.

### Assigned tiny tuples

The sixteen tiny encodings are the selected shortlist after accounting for
compact field-accessor and identity-forwarder bodies. These assignments are
not claimed to be a measured global optimum.

| Opcode | Exact tuple |
|---:|---|
| `00` | Signature-aware `ret R0`; for exact `void`, return the signature's unit value without reading R0 |
| `01` | `R0 = load.arg 0` |
| `02` | `R1 = load.arg 1` |
| `03` | `R2 = load.arg 2` |
| `04` | `R0:R1 = load.arg.pair 0:1` |
| `05` | `R0 = load.home 0` |
| `06` | `R1 = load.home 1` |
| `07` | `store.home 0, R0` |
| `08` | `store.home 1, R1` |
| `09` | `R0 = load.constant 0` |
| `0A` | `R0 = add.wrap(R0, R1)` |
| `0B` | `R0 = sub.wrap(R0, R1)` |
| `0C` | `R0 = mul.wrap(R0, R1)` |
| `0D` | `R1 = mul.wrap(R1, R2)` |
| `0E` | `R0 = and(R0, R1)` |
| `0F` | `R0 = xor(R0, R1)` |

Each tuple obeys the same type, access, source-use, failure and cleanup
rules as its full form. Tiny stores `07` and `08` use `sourceUse=1`
(copy/read) only. They are valid only when the source type is Copy-eligible
and the selected Home's actual state and authority permit the required
initialization or replacement. They never transfer or borrow the source.
Other Home source-use modes use the assigned full-width D6 form; modes or
destinations not represented by D6 require SemanticStore or an applicable
typed record. A tiny call is not inferred from a name.

### Assigned short forms

Short instructions have one operand byte. Unless a row says `byte:u8`, it
is split into `x=low nibble` and `y=high nibble`. `R0..R13` are writable
general destinations. R14 is not writable. R15 is a discard only in result
roles that explicitly admit discard; a source-role R15 is the selected
all-bits-set constant, and an unused field is not read.

| Opcode | Operand-byte layout | Meaning and constraints |
|---:|---|---|
| `10` | `x=destination register, y=source register` | `mov`; create a value alias, not a copy, transfer or cleanup |
| `11` | `x=destination register, y=formal ordinal 0..15` | Read/alias one eligible input formal; ownership and parameter mode still apply |
| `12` | `x=destination base register, y=first formal ordinal 0..14` | Load two adjacent formals into `Rx:R(x+1)`; `x<=12`, both formals exist |
| `13` | `x=destination register, y=HomeMap index 0..15` | Copy/read an eligible Home |
| `14` | `x=HomeMap index 0..15, y=source register` | Copy/read store (`sourceUse=1`) to an eligible Home; source must be Copy-eligible and the Home state/authority must admit initialization or replacement |
| `15` | `x=destination register, y=HomeMap index 0..15` | Transfer from an eligible Home, leaving it vacant |
| `16` | `x=destination register, y=ConstantMap index 0..15` | Load the exact typed constant entry |
| `17` | `x=result register, y=0` | Return the already computed non-unit result |
| `18` | `x=0, y=0` | Return exact unit |
| `19` | `x=destination register, y=source register` | Load through an admitted checked reference value |
| `1A` | `x=reference register, y=source register` | Copy/read store (`sourceUse=1`) through an admitted writable reference; source must be Copy-eligible |
| `20` | `x=destination register, y=source register` | Integer wrapping negate |
| `21` | `x=destination/left register, y=right register` | Integer wrapping add |
| `22` | `x=destination/left register, y=right register` | Integer wrapping subtract |
| `23` | `x=destination/left register, y=right register` | Integer wrapping multiply |
| `24` | `x=destination/left register, y=right register` | Integer divide with ordinary zero and quotient-overflow failures |
| `25` | `x=destination/left register, y=right register` | Integer remainder with its distinct min/-1 rule |
| `26` | `x=destination register, y=source register` | Strict floating sign negation |
| `27` | `x=destination/left register, y=right register` | Strict floating add |
| `28` | `x=destination/left register, y=right register` | Strict floating subtract |
| `29` | `x=destination/left register, y=right register` | Strict floating multiply |
| `2A` | `x=destination/left register, y=right register` | Strict floating divide |
| `2B` | `x=destination/left register, y=right register` | Strict floating remainder |
| `30` | `x=destination/left register, y=right register` | Integer/Boolean AND |
| `31` | `x=destination/left register, y=right register` | Integer/Boolean OR |
| `32` | `x=destination/left register, y=right register` | Integer/Boolean XOR |
| `33` | `x=destination register, y=source register` | Type-directed integer bitwise or Boolean logical NOT |
| `34` | `x=destination/data register, y=count register` | Integer shift left |
| `35` | `x=destination/data register, y=count register` | Integer signed arithmetic right shift |
| `36` | `x=destination/data register, y=count register` | Integer logical right shift |
| `40` | `x=destination/left register, y=right register` | Compare in place; output is the selected uint8 relation |
| `41` | `x=condition/result register, y=predicate mask 0..15` | Test a uint8 relation mask and replace it with ordinary bool |
| `50` | `x=in-place register, y=TypeMap index 0..15` | Total admitted ordinary conversion only |
| `51` | `x=in-place register, y=TypeMap index 0..15` | Explicit wrapping integer conversion |
| `52` | `x=in-place register, y=TypeMap index 0..15` | Admitted same-width bit reinterpretation |
| `60` | `x=destination place register, y=HomeMap index 0..15` | Form a typed place for a Home |
| `61` | `x=base/destination register, y=PlaceSelection record index 0..15` | Form an exact field place from the captured base |
| `62` | `x=base/destination register, y=index register` | Form an admitted element place from the captured base/index |
| `63` | `x=destination register, y=place register` | Read an admitted place |
| `64` | `x=place register, y=source register` | Copy/read store (`sourceUse=1`) to a selected place whose authority/state admits initialization or replacement; source must be Copy-eligible |
| `65` | `x=destination register, y=place register` | Take the value from an eligible place |
| `66` | `x=place register, y=0` | Retire the selected live value under its exact cleanup contract |
| `67` | `x=destination register, y=place register` | Form an explicit checked reference from an admitted place |
| `70..7F` | `x=destination register, y=aggregate register` | Extract field ordinal `opcode-70`; only complete Copy-eligible fields |
| `80` | `x=argument-window base, y=MethodMap index 0..15` | Bound call; non-unit result replaces the base register |
| `81` | `x=callable register, y=argument-window base` | Callable-value call; non-unit result replaces the target register |
| `82` | `x=argument-window base, y=MethodMap index 0..15` | Bound call with exact unit result |
| `83` | `x=callable register, y=argument-window base` | Callable-value call with exact unit result |
| `90` | `byte:u8=CallSite record index 0..255` | Execute the complete general call-site record |
| `91` | `x=destination register, y=PlaceSelection record index 0..15` | Apply a typed place-selection record |
| `92` | `byte:u8=SemanticStore record index 0..255` | Apply a typed state-aware store record |
| `A0..AF` | `x=condition register, y=ConditionalEdge record index 0..15` | `jcc`; primary low nibble is the four-bit predicate mask |
| `B0` | `byte:u8=absolute Block index 0..255` | Unconditional branch |
| `B1` | `x=Boolean register, y=ConditionalEdge record index 0..15` | Branch on ordinary Boolean truth |
| `B2` | `byte:u8=Switch record index 0..255` | Typed switch |
| `B3` | `byte:u8=RequirementFailure record index 0..255` | Runtime `require`; success continues, failure follows its record |
| `B4` | `byte:u8=Throw record index 0..255` | Throw the selected fresh/active/captured payload |
| `B5` | `byte:u8=0` | `failfast`; no ordinary unwind or cleanup promise |
| `B6` | `byte:u8=RegionExit record index 0..255` | Recognized shape only; no current exit-kind or region carrier is assigned |
| `B7` | `byte:u8=0` | `unreachable`; raises the selected ordinary Core failure |
| `B8` | `byte:u8=HomeMap index 0..255` | Return a non-unit Home value |

Short `14`, `1A`, and `64` are only compact CopyRead stores. A Home store
using Transfer or BorrowRead uses the explicit full-width `D6` mode. Other
Home source-use modes, and non-CopyRead reference/place stores, require the
full SemanticStore form (`92` or `D9`) with its explicit source-use and
transition fields. No form bypasses the destination's state or authority.

Every other primary value in the short band is unassigned. Short arithmetic
has fixed policy: the wrapping operations are modular, division/remainder
retain their source failure rules, and strict floating forms use the selected
format without implicit FMA. Checked and saturating arithmetic have no
one- or two-byte form.

### Assigned long families

The long payload is `control:u8, payload:u16`. The payload is little-endian.
For register-family forms the `u16` contains four nibbles, least significant
first: result0, result1, source0, source1. R15 in a role designated unused is
not read; in an admitted result role it discards only the result value and
does not abandon effects or cleanup responsibility.

| Opcode | Family | Control and payload |
|---:|---|---|
| `C0` | Scalar arithmetic | Control selects a registered operation; payload nibbles are result0/result1/source0/source1 |
| `C1` | Bit operation | Control selects the operation; same four-register payload |
| `C2` | Compare | Control selects raw relation, equality or inequality; same four-register payload |
| `C3` | Set condition | Control is a four-bit predicate; destination/condition/unused/unused |
| `C4` | Select condition | Control is a four-bit predicate; destination/condition/true/false |
| `C5` | Conditional move | Control is a four-bit predicate; destination/condition/value/unused |
| `C6` | Ternary logic | Control is the exact truth-table byte; destination/A/B/C |
| `C7` | Integer wrapping arithmetic recipe | Recipe control byte; destination/A/B/C |
| `C8` | Integer checked arithmetic recipe | Recipe control byte; destination/A/B/C |
| `C9` | Integer saturating arithmetic recipe | Recipe control byte; destination/A/B/C |
| `CA` | Strict floating arithmetic recipe | Recipe control byte; destination/A/B/C |
| `CB` | Three-input comparison AND | Low/high four-bit predicates; bool destination/A/B/C |
| `CC` | Three-input comparison OR | Low/high four-bit predicates; bool destination/A/B/C |
| `D0` | Numeric conversion | Control carries destination/source; payload is destination TypeMap index |
| `D1` | Wrapping integer conversion | Same layout; only admitted integer pairs |
| `D2` | Saturating conversion | Same layout; only admitted pairs |
| `D3` | Bit reinterpretation | Same layout; exact representation-valid pair |
| `D4` | Constant load | Control has destination register; payload is ConstantMap index |
| `D5` | Home load | Control has destination register; payload is Home index |
| `D6` | Home store | Control has source register/use mode; payload is Home index |
| `D7` | Home take | Control has destination register; payload is Home index |
| `D8` | Place selection | Control has destination register; payload is PlaceSelection record index |
| `D9` | Semantic store | Payload is SemanticStore record index; control is zero |
| `DA` | Place load | Control has destination register; payload is PlaceSelection record index |
| `DB` | Place take | Control has destination register; payload is PlaceSelection record index |
| `DC` | Place retirement | Payload is PlaceSelection record index; control is zero |
| `DD` | Reference formation | Control has destination register; payload is PlaceSelection record index |
| `DE` | Type identity and layout query | Control high nibble selects SizeOf, AlignOf, OffsetOfTypePath, OffsetOfPlace or TypeOf; payload is a TypeMap index for kinds 1/2/5 and FieldMap index for kinds 3/4 |
| `E0` | General call | Payload is CallSite record index; control is zero |
| `E1` | Bound method-window call | Payload is MethodMap index; control is argument-base/result register pair |
| `E2` | Callable-value window call | Control is target/argument-base pair; payload low nibble is result register |
| `E3` | Unconditional branch | Payload is absolute Block index; control is zero |
| `E4` | Boolean conditional branch | Payload is ConditionalEdge record; control low nibble is condition register |
| `E5` | Predicate conditional branch | Payload is ConditionalEdge record; control is condition register/predicate |
| `E6` | Switch | Payload is Switch record; control is zero |
| `E7` | Runtime requirement | Payload is RequirementFailure record; control is zero |
| `E8` | Throw | Payload is Throw record; control is zero |
| `EA` | Region exit | Recognized shape only; RegionExit record and zero control; no current exit-kind or region carrier is assigned |
| `EB` | Return home | Payload is Home index; control is zero |

All unlisted long-primary values are invalid. The registry fixes every
control selector, unused field and type matrix. A field combination that
fits these bits but is not assigned is invalid required input.

The `C0` ScalarArithmetic control byte is:

| Control | Operation and policy |
|---:|---|
| `01..04` | Checked integer add, subtract, multiply, negate |
| `05..08` | Wrapping integer add, subtract, multiply, negate |
| `09..0B` | Saturating integer add, subtract, multiply |
| `0C` | Integer divide |
| `0D` | Integer remainder |
| `0E` | Integer DivRem, publishing both results only on success |
| `0F` | Integer `mul.full` |
| `20..25` | Strict floating add, subtract, multiply, divide, remainder, negate |

The `C1` BitOperation control byte is `01=and`, `02=or`, `03=xor`,
`04=not`, `05=shift-left`, `06=signed-arithmetic-right`,
`07=logical-right`. Other controls reject. In `C2 Compare`,
`00=relation result` (ordered for numeric domains and E/U-only for
TypeIdentity), `01=Boolean equality`, and
`02=Boolean inequality`. The relation result is uint8 with exactly one of
Less=1, Equal=2, Greater=4, Unordered=8; equality/inequality results are
ordinary bool and do not manufacture an ordering for equality-only types.
The three existing controls also admit two TypeIdentity inputs: control `00`
produces `uint8(2)` for identical complete identities or `uint8(8)` for
distinct identities; controls `01`/`02` produce ordinary `bool`
equality/inequality results. TypeIdentity has no ordering and never produces
L/G. `U` here means known-distinct identities, not an unresolved answer.

For `D0..D3`, the control byte is `sourceRegister:4 | destinationRegister:4`
(low nibble first) and the payload is a zero-based TypeMap index. For `D4`
and `D5`, the control low nibble is the destination register and the high
nibble is zero. For `D6`, the control low nibble is the source register and
the high nibble selects `1=CopyRead`, `2=Transfer`, or `3=BorrowRead`; other
source-use modes reject. The source type, Home state, authority and retained
dependencies must admit the selected transition. If a per-store formal or
dependency contract cannot be derived from those existing facts, use
SemanticStore instead. For `D7`, `D8`, `DA`, `DB` and `DD`, the control low
nibble is the destination register and the high nibble is zero. `D9` and
`DC` require a zero control byte.

For `E1`, the control low nibble is argument-window base and the high nibble
is result register; R15 is allowed only for an exact unit result. For `E2`,
the control low nibble is callable register and the high nibble is argument
base; the payload low nibble is result register and its high twelve bits are
zero. For `E4`, control low nibble is condition register and high nibble is
zero. For `E5`, control low nibble is condition register and high nibble is
the selected four-bit predicate. Other record/control forms require zero
control unless their record kind assigns the field.

For `DE`, the control low nibble is the destination register and the high
nibble is the query kind. Kinds `1=SizeOf`, `2=AlignOf` and `5=TypeOf`
select a zero-based TypeMap index; kinds `3=OffsetOfTypePath` and
`4=OffsetOfPlace` select a zero-based FieldMap index. Kinds `1..4` produce
the exact selected-target `nint` result; kind `5` produces an opaque internal
TypeIdentity, not a value of its subject type or a reflection object. R0..R13
are destinations; R15 discards the responsibility-free result; R14 and
high-nibble kinds `0` and `6..F` reject. All 75 combinations of five query
kinds and fifteen destinations are assigned.

SizeOf/AlignOf observe the complete static type/Place representation without
reading or evaluating a Place. Offset queries resolve a direct FieldDef/Import
one-field token only when its exact root is the non-generic declaring type;
otherwise they use a FieldPath token whose global `0x004B` row carries the
complete root TypeSpecRef and ordered FieldDef/Import steps. Type-path
OffsetOf returns the cumulative inline displacement; Place OffsetOf returns
only the terminal field displacement in its immediate containing type. The
query does not execute dereferences, getters, indexers or PlaceSelection.
Layout kinds 1..4 retain one target-specific `TargetLayoutDependency`
observation per reached occurrence; TypeOf kind 5 is not a layout
dependency and does not read object layout.
The query instructions use direct TypeMap/FieldMap operands; no generic
LayoutQuery Body record is assigned, and record kind `0C` remains unassigned.

For the common register family, payload nibbles occupy bytes 2 and 3:

```text
byte 2: result0:4 | result1:4
byte 3: source0:4 | source1:4
```

This is the exact little-endian representation of the logical payload word
`result0 | result1<<4 | source0<<8 | source1<<12`. Operations with one
result require result1=R15; operations with one input require source1=R15.
Operations with different roles, such as conversion and `mul.full`, use
their separately registered payload layout above.

### Instruction codec selector

Every instruction-stream Body begins with the variable stream prefix specified
under [Body row layout](#body-row-layout-and-compact-implementations).
`instructionEncoding=0x01` selects this mixed codec. All other selector
values are unassigned and reject before affected use. The selector is not a
source arithmetic profile, semantic Feature, target ABI or compatibility
fallback.

### Maps, size and alignment

#### Explicit typed ConstantMap

The Body has a ConstantMap alongside its TypeMap, MethodMap and FieldMap. Loading a
constant names an existing typed entry; it does not require the decoder
to rediscover CSE or execute construction code.

| Property | Assigned rule |
|---|---|
| Identity | Complete type and exact admitted constant value/representation, not numerical equality alone. |
| Payload | A selected typed constant record or reference to one, with all required literal/data dependencies. Large values live in that record, not the instruction bytes. |
| Index | Selected unsigned sixteen-bit Body-local index, independently of register numbering. |
| Duplicates | Valid. Producer interning/deduplication is optional, not a consumer rescan or uniqueness requirement. |
| Construction | Compile-time/frozen construction must already satisfy its selected contract; loading does not invoke arbitrary constructors or factories. |
| Nonliteral identities | References, addresses, callable/data relocations or aggregates require their own admitted constant contract. Arbitrary bits do not manufacture an owner, checked reference or valid object. |

Same bits with different complete integer types remain different typed
values. Opposite signed zeros, distinguishable NaN payload/state, and
equal-valued decimal cohorts must not be merged by numerical comparison.
The map can contain both equivalent and identical entries without changing
operation typing or canonicality.

This supports deciding representation and reuse at the producing tool's
appropriate stage, rather than requiring one upfront global CSE or
constructor-normalization strategy. The selected register special constants
are only short operand forms; they do not replace the general ConstantMap.

#### Selected method-local index limits

Body-local map, home, block and record indexes are unsigned sixteen-bit
values. A zero-based index can select 0..65,535, so the corresponding
maximum count is 65,536, not 65,535.

| Item | Selected width/scope |
|---|---|
| Local and argument slots | Sixteen-bit method-local indices; flattened receiver/parameter inventory must fit. |
| ConstantMap, TypeMap, MethodMap, FieldMap, HomeMap, Block and typed-record references | Zero-based sixteen-bit Body-local indexes; metadata tokens retain their fixed global width. |
| Operand/call/failure/edge records | Sixteen-bit Body-local indexes; RegionExit indexes are not usable until the region carrier is assigned. |
| Inventory counts | Unsigned thirty-two-bit fields; the selected local-index codec admits counts 0..65,536 without an ambiguous sentinel. |
| Global/module references inside map records | Keep their selected global width; a local map does not cap global table identities to sixteen bits. |
| Code extents, offsets, instruction counts and internal SSA definitions | Separate namespaces and widths; not automatically limited to sixteen bits. |

More than 65,536 computed values does not imply more than that many locals.
Internal SSA, optimization and inlining need not use sixteen-bit counters.
Reserializing an oversized result must diagnose the codec's limit or use
an explicitly supported future codec, not wrap an index, silently truncate
the inventory or reinterpret this encoding.

Every index value remains usable, including `0xFFFF`. Where a schema permits
absence, its kind/presence contract represents that absence without stealing
a valid table index. An empty inventory has count zero, not a wrapped or
sentinel count for 65,536.

The header has no wider local-index variant. A future width requires a
separately assigned codec and cannot be inferred from an out-of-range value.
These limits apply to this Body schema, not to every source implementation
or native ABI.

Register capacity is independent of argument/local counts, map counts,
instruction counts and offsets. Ten thousand definitions using `R0` do not
widen its selector.

Instructions are not individually padded. A Body row may be aligned to
four bytes, with padding outside its instruction extent; this does not imply
that every four-byte instruction starts at a four-byte boundary.

No per-result type array is required solely to decode the common stream.
Types come from incoming bindings, selected metadata and operation schemas.
Selective block decoding still needs its entry types/state. Direct lookup
of every historical definition is not free: an optional index or prefix
decode may be needed.

## Semantic family inventory

This inventory summarizes the assigned codec. The 176-byte short range
contains common operations or restricted forms, while a long primary selects a
family whose control byte selects its registered members. There is no
global operation byte or logical-ID-to-primary-byte correspondence.

| Family | Members or scope | Assigned encoding |
|---|---|---|
| Scalar arithmetic | Add/subtract/multiply/divide/remainder/negate and full low/high integer products; explicit policies | Tiny wrapping tuples; short common forms; long `C0` controls and `C7..CA` recipes |
| Bit operations | AND/OR/XOR/NOT and shifts | Short `30..36` forms and long `C1` controls |
| Ternary logic | All 256 three-input Boolean functions | `C6` with the complete truth-table byte |
| Arithmetic recipes | Explicit three-input operation trees and policies | `C7..CA`; no silent FMA contraction |
| Comparisons and consumers | `cmp`, `setcc`, `jcc`, `movcc`/`selectcc`, compound comparisons; TypeIdentity uses E/U through existing binary controls | `40..41`, `A0..AF`, `C2..C5`, `CB..CC` |
| Classification | NaN/signaling-NaN/finite/infinite/sign queries | Unassigned; no comparison-effect substitution |
| Register transport | `mov` | Short `10` and the assigned tiny tuples |
| Arguments/homes/constants | Loads, stores, takes and typed constants | Tiny and short forms; larger indexes in long forms or records |
| Fields/references/indexing | Place selection/access, stores, takes, reference formation and FieldMap-backed layout offsets | `60..67`, `D8..DD`, `DE` with typed/global path references; no implicit native pointer or lifetime extension |
| Type identity and layout queries | TypeOf, SizeOf, AlignOf and both OffsetOf forms; Count lowers to constants or calls | `DE` kinds 1..5; no universal Count/Length opcode or reflection object |
| Conversions | Ordinary, wrapping, saturating and bit-preserving policies | `50..52`, `D0..D3`; checked/saturating forms use long encodings |
| Optional/aggregate values | Present/absent tests and construction, aggregate/field operations | Aggregate construction and non-Copy extraction are unassigned |
| Calls | Bound targets, callable values and argument staging | `80..83`, `90`, `E0..E2` and CallSite records |
| Control flow | Branches, switches, returns, throws, failfast, unreachable and require | Assigned forms `A0..AF`, `B0..B5`, `B7..B8`, `E3..E8`, `EB`; `B6`/`EA` require the unassigned region carrier |
| Lifetimes | Retirement, loans, backing, owners and required state transitions | Explicit assigned place/store operations; region/cleanup operations require their separately assigned carrier |
| Atomics and SIMD | Memory, atomic, lane, mask, shuffle and vector operations | Unassigned; reserved bytes do not assign semantics |
| Extension | Additional operation/operand records and future Body codecs | No extension or escape is assigned by this schema |

The registry fixes assigned members, controls and type domains. No operation
gains an executable contract merely because this inventory names it.
Unassigned members stay invalid; no legacy alias preserves a previous
draft's numbers.

## Operation identity, encoding and presentation

The selected source declaration, complete operand/result types, effects and
failure policy determine the semantic operation. A display name is a
human-readable projection of that bound operation; it is not an opcode,
overload-selection rule or permission to recognize a nominal method by name.
The registry and the instruction forms above remain the authority for wire
encoding.

| Bound operation | Display distinction |
|---|---|
| Checked versus wrapping integer addition | `add.checked` and `add.wrap` retain distinct overflow behavior and result obligations |
| Integer versus floating addition | Both may display as `add`, while their complete types and distinct arithmetic/rounding/failure contracts remain explicit |
| Source-selected nominal operator | Display the selected call and declaration identity; do not infer a primitive operation from its spelling |
| Required evaluation versus optional folding | Display the value; retain required `ConstantExecution` inputs/dependencies separately, with no optimizer-history record for optional folding |
| JIT/AOT or target lowering | Preserve the bound operation and dependencies; display does not prescribe a machine instruction, helper or runtime API |

The presentation does not change source selection, checked defaults,
ownership, effects or canonical wire bytes. A canonical writer uses the
registered instruction form and operands even when another display spelling
would be more familiar.

## Operation meanings

<a id="terminators-and-cfg"></a>
### Scalar arithmetic, logic and selection

Let `I` be the twelve source integer identities and `F` the eight binary
and IEEE decimal floating identities. Character identities, `bool` and
`void` retain their separate domains. Nominal operations use selected calls,
not a numeric instruction chosen by representation size or method spelling.

| Operation | Inputs and result | Intent and important behavior |
|---|---|---|
| `add`, `sub`, `mul` | Two identical complete `T` in `I` or `F`; result `T` | Explicitly selected checked/wrapping integer policy or selected-format floating arithmetic. The wire does not inherit a JIT build's source default. No promotion or implicit FMA. |
| `div` | Two identical numeric `T`; result `T` | Integers retain zero-divisor and signed-minimum/-1 failures. Floating inputs retain IEEE outcomes. |
| `rem` | Two identical numeric `T`; result `T` | Integer remainder and truncating-quotient floating remainder retain their distinct exact contracts. Signed minimum modulo -1 is zero, not quotient overflow. |
| `neg` | One numeric `T`; result `T` | Integer negation uses its selected checked/wrapping policy; unsigned checked negation is zero-minus-value. Floating negation changes only the sign component. |
| Saturating integer recipe | `C9` active `neg`, `add`, `sub` and `mul` nodes over one identical integer `T` | Clamp each mathematical intermediate to `T`'s value range. Saturating negation maps signed minimum to signed maximum; unsigned `0-x` saturates to zero. This is an IL recipe policy, not a source-level operator or ambient mode. |
| Wrapping arithmetic | Admitted identical integer `T`; result `T` | Modular declared-width arithmetic. No ambient unchecked mode or floating policy change. |
| `abs` | One admitted numeric `T`; result `T` | Signed minimum can fail. Floating Abs clears the sign only; it does not quiet or normalize the input. |
| `and`, `or`, `xor` | Same integer `T`, or two `bool`; same result type | Eager bitwise/Boolean value operations. Not source short-circuit evaluation. |
| `not` | Integer `T` or `bool`; same result type | Integer: complement exactly the declared-width bits. Boolean: logical complement of the Boolean value. |
| Shifts | Integer data and integer count; result has data type | Validate the exact source count before representation bridging. `shr.logical` zero-fills even signed data. |
| `select` | Existing `bool`, existing `T`, existing `T`; result `T` | Choose an already computed value. Does not suppress effects that produced either input. |
| `min`, `max`, `clamp` | Same admitted numeric `T`; result `T` | Retain NaN, signed-zero, decimal representation and invalid-bound rules. Not arbitrary hardware min/max. |
| `copysign` | Two same-format floating values; result that format | Sign-component transfer; no numeric conversion or permission to normalize payload bits. |
| `divrem` | Two same integer values; two same-type results | Checks required for the quotient remain even if only the remainder is later used. |

For the two-result `divrem`, a general payload can hold result0, result1,
left and right in its four nibbles. Published general destinations must be
distinct; independently discarded integer results may use R15. Both inputs
refer to their old bindings and both results publish only on success.
Discarding the quotient does not erase its required checks.
This is a different registered operand schema from a one-result
ternary operation, not a claim that every four-nibble payload has the
same roles.

Unifying IL `not` does not add source `~bool` or `!int32`. Source operator
spelling and eligibility remain in the language owner. A Boolean result is
still a valid `bool`; implementing it by complementing every byte of its
physical representation would generally be wrong.

`select` on affine values needs an explicit selected-input transfer and
remaining-responsibility contract. A nonselected owner does not disappear.
Until that contract is selected, such cases can use normal control flow;
the opcode table alone does not authorize ownership duplication.

### Full-product multiplication

The selected full low/high integer-product operation is spelled `mul.full`:

```text
mul.full Rlo, Rhi, Ra, Rb
```

For admitted W-bit integer inputs, compute the exact 2W-bit product and
publish its W-bit low/high parts. This covers the largest input width
without inventing an int256 primitive or allocating arbitrary precision.
For two inputs of the same complete primitive integer type `T`, the assigned
result types are unsigned low and input-typed high:

| Input T | Low result | High result |
|---|---|---|
| int8 | uint8 | int8 |
| uint8 | uint8 | uint8 |
| int16 | uint16 | int16 |
| uint16 | uint16 | uint16 |
| int32 | uint32 | int32 |
| uint32 | uint32 | uint32 |
| int64 | uint64 | int64 |
| uint64 | uint64 | uint64 |
| int128 | uint128 | int128 |
| uint128 | uint128 | uint128 |
| nint | nuint | nint |
| nuint | nuint | nuint |

Low is the unsigned low limb. High is the arithmetic upper part for signed
inputs and the unsigned upper part for unsigned inputs. Reconstructing the
mathematical product uses high times two to the W plus low. The complete
product always fits these two parts; no checked/wrapping/saturating variant
is required. Native width comes from the selected target; nint/nuint keep
their distinct identities even when a fixed-width type has the same size.
Mixed input types are not selected by this equal-type rule.

Two output selectors plus two input selectors fit the sixteen register bits
of a full four-byte form. Read both inputs before publishing outputs.
The independently unused integer halves may use discard R15; distinct
non-discard outputs must name distinct general bindings. Keeping only a
high or low half does not require another semantic multiply operation.

There is no separate widening-result IL multiply in the selected design.
Construct a wider primitive or aggregate from the halves where needed.
A backend may recognize that construction and select a natural whole-result
machine operation without changing the wire contract.

`MultiplyWidening` remains a possible library-level family name for a
constructed wider result, not another IL opcode. This instruction contract
does not select a Core library member.

SIMD full-product multiplication is unassigned. No vector operand/result
contract is admitted by this scalar definition.

Possible library Lower/Upper/Even/Odd spellings describe selected input
elements, not product bits. These remain context for a future SIMD review,
not selected vector IL operations or a widening-result multiply.

### Floating predicates

The ordinary source predicate truth values are:

| Predicate | Less | Equal | Greater | Unordered: either input NaN |
|---|---|---|---|---|
| `eq` | false | true | false | false |
| `ne` | true | false | true | true |
| `lt` | true | false | false | false |
| `le` | true | true | false | false |
| `gt` | false | false | true | false |
| `ge` | false | true | true | false |
| `cmp.unordered` | false | false | false | true |

An ordered test is the Boolean complement of `cmp.unordered`. Numeric
equality equates opposite signed zeros and equal-valued decimal cohorts;
it is not representation equality or IEEE totalOrder.

The selected predicate masks also express the alternate unordered outcomes.
Descriptive spellings are aliases for these tests, not a requirement for
independent comparison opcodes:

| Spelling | Mask | NaN result |
|---|---|---|
| `eq.ordered` | `2` | false; ordinary `eq` |
| `eq.unordered` | `A` | true |
| `ne.ordered` | `5` | false |
| `ne.unordered` | `D` | true; ordinary `ne` |
| `lt/le/gt/ge.ordered` | `1/3/4/6` | false; ordinary relations |
| `lt/le/gt/ge.unordered` | `9/B/C/E` | true |

These names can denote tests of a comparison condition rather than separate
comparison instructions. The suffix specifies the unordered outcome, not
a total-order relation.
In particular, `not lt` is not ordinary floating `ge`: it is also true for
unordered inputs.

A NaN-truth toggle alone is not a signaling design. Predicate controls
belong to the condition-consumer design, while signaling and invalid handling
need the separate contracts described above. Comparison does not mutate the
input value to quiet it; status/trap effects remain separate.

### Register transfer versus value copying

The codec assigns one register value-alias operation:

```text
mov Rd, Rs
```

This binds `Rd` to the already computed value named by `Rs`. It does not:

- Invoke a user copy/clone operation.
- Construct another owner or another cleanup responsibility.
- Vacate a local or parameter.
- Destroy the previous value merely because its destination name is reused.

Register `mov` creates another name for the same logical value, including
its resource/dependency identity. A consuming use updates availability for
all aliases; it cannot consume that identity twice through different names:

```text
mov R1, R0
consume R0
consume R1       // invalid if the first consumption transferred this identity
```

`consume` above is explanatory notation for a consuming operation, not a
new instruction assignment. Rebinding cannot lose an unretired owner
responsibility. Another valid alias or explicitly tracked storage/cleanup
holder must retain it, or the responsibility must be discharged before the
last available name is lost.

The importer reconstructs identity from definitions, alias edges and typed
control flow; it does not need serialized per-expression SSA numbers.
Independently formed loans/views keep their own backing/access contracts;
register aliases do not create those contracts or extend them.

A source-level Copy, transfer or clone is a different contract. A read
from storage, a store, a parameter binding or a selected callable must state
whether it copies, transfers or borrows. Register movement is not evidence
that any of those actions are legal.

### Storage reads, stores, takes and retirement

The names should describe what happens to storage, not copy familiar
source-language assignment terminology into the opcode list.

| Family | Intended meaning |
|---|---|
| `load.*` | Obtain an eligible nonconsuming value read from a live typed source. Copy eligibility and dependencies still apply. |
| `store.*` | Write a complete typed value to the selected destination under its actual initialization/replacement authority. |
| `take.*` | Transfer a live value out of eligible storage, leaving that source vacant, without cleaning the transferred value. |
| `retire.*` | End the selected live value under its ordinary cleanup/responsibility contract; not a byte clear or register pop. |
| Reference formation | Produce an admitted descriptor/loan for a place, not a copy of the pointee. |

Thus the working names replace `assign.local/ref/field/index` with
`store.local/ref/field/index`, and `move.local/ref/field/index` with
`take.local/ref/field/index`. A `take` still has a source-storage effect;
it is not register `mov`.

A non-Copy source does not become copyable because a load targets a
register. Appropriate take/reference operations or a selected copying
contract are required. The argument model must also distinguish reading
an incoming binding from taking or addressing a mutable parameter home.

The codec assigns one typed semantic store for initialization and
replacement, with explicit destination state/authority and source-use
contracts:

| Destination state | Transition | Obligation |
|---|---|---|
| Vacant | Initialize | No old live value exists to retire; require the actual initialization authority. |
| Live | Replace | Preserve required retirement, checks, commitment and confinement. |
| Maybe-live | Follow the admitted guarded transition | Do not assume vacant or unconditionally retire an absent value. |
| Partial | Preserve actual subobject state | Do not treat a partially initialized aggregate as vacant whole storage. |

An initialization-only loan does not grant replacement authority. The
source-use contract must also state whether a value is copied, transferred
or borrowed; the name `store` grants none of those permissions by itself.

Retain the semantic transition through verification and lowering. It may
become multiple machine operations, without promising a raw, nonthrowing,
no-call or physically atomic store.

Source replacement evaluates the destination once and the new value once,
then checks actual state/dependencies before commitment. Failed new-value
construction does not first destroy the old value. Retirement and
installation cannot expose an incomplete receiver to callbacks, reentrancy
or escaping failure. A naive unconstrained `drop old; store new` does not
establish that invariant.

Automatic authored Cleanup is checked NoThrow and nonsuspending, but may
have observable effects and reentrancy. Its complete/partial retirement
order and the surrounding checks still matter. Ending a reference
descriptor does not destroy its referent.

No dedicated `init.*` or hot `drop.*` allocation is selected here. Their
required semantic facts cannot disappear merely because the earlier names
were confusing.

### Fields, elements and ranges

| Operation | Intent |
|---|---|
| `load.field` | Read a selected stored field under its type/access contract. |
| `extract.field` | Obtain one field's value from an already computed aggregate value, without forming a storage address. |
| Field-place formation | Select a field's place without reading its payload. |
| `read.index` | Check the selected indexed access and read its value. |
| `store.index` | Perform the selected indexed write; source lowering must preserve when target formation/checks occur. |
| Element-place formation | Select and validate one element target without reading or initializing its payload. |
| Unassigned slice | No dedicated slice instruction or record is assigned. |

Element-place formation selects one element's target without reading its
value. For an update, the abstract sequence can be:

```text
target = form_element_place(base, index)
old    = read(target)
rhs    = invoke F()
new    = add(old, rhs)
write(target, new)
```

The assigned PlaceSelection record preserves this target and check order.
Base and index are evaluated once. Required bounds/place checks are not
silently delayed past `F`, and rereading a rebound source variable does not
select a different target.

An internal typed place handle retains the selected
target and captured inputs, target type, backing dependencies, access/
initialization authority and required check timing for load/store/take.
It is not a public source type, checked reference, raw pointer, native
address or pin.

A handle can occupy a logical register and participate in typed flow.
Register aliases refer to the same selected place and authority; they
cannot duplicate an exclusive right or conceal a storage-state transition
performed through another alias. The representation need not be a single
hardware address or register.

Reference/address formation is explicit and must establish its actual
descriptor, loan, access, backing and stability obligations. Selecting a
place does not silently promise those stronger capabilities. Rebinding a
source name does not retarget the captured place, and retaining its backing
dependency does not create ownership or an arbitrary lifetime extension.

A value-returning property/indexer does not become a borrowable backing
place. Accessor updates use their selected getter/setter contracts.
Actual reference formation retains access, roots, backing and invalidation
dependencies; it neither pins storage nor extends an owner's lifetime.
Not every logical place exposes an address, including zero-data `void`.

The former `checked.range` also combined two possible intents:

1. Check a range without producing a view.
2. Construct a bounded view after validating a range.

Those should not have an ambiguous shared name. The working recommendation
is `slice(base, start, count)` for view construction, if it warrants an
opcode at all. Validate nonnegative start/count where applicable and the
mathematical conditions:

```text
start <= length
count <= length - start
```

Do not overflow an unchecked `start + count` or truncate a wider source
index before checking. The empty range at the end remains a possible
valid case under the selected family contract.

A slice does not copy elements, allocate an owner, pin backing or extend
its lifetime. Its complete result type/access/index domains must be supplied
by an explicitly selected view contract. An ordinary selected library call
may be sufficient; a dedicated opcode and result carrier are not approved.

### Optional values, aggregates and queries

| Operation | Inputs/result and intent |
|---|---|
| `some` | Construct a present optional from an admitted complete payload, preserving its ownership and dependencies; it does not clone an affine payload. |
| `none` | Construct absence for an explicitly identified complete optional type. No payload is read or fabricated. |
| `is.present` | Inspect an optional discriminator and return `bool`, without reading an absent payload or implicitly consuming the optional. |
| Aggregate construction | Build the selected complete aggregate from already computed field values under its actual validity, construction and transfer rules. |
| `extract.field` | Obtain one field's value from an existing aggregate value; copying or transferring a non-Copy field cannot be inferred from its bits or field number. |
| Registered query | Use the exact query lowering below; do not recognize arbitrary methods by name or invent an opcode from a display name. |
| Loan/region events | Preserve specific logical access, backing and lifetime transitions; they are not automatically machine instructions or destruction of a referent. |

#### Registered query lowering

The accepted source contracts remain in
[registered queries](language/registered-queries.md). In this codec,
`typeof(Type)` and `typeof(Place)` use `DE` kind 5 to materialize the internal
TypeIdentity only when register-level identity comparison needs a value.
Metadata-only type recipes remain metadata references and do not execute this
instruction.

`sizeof` and `alignof` use `DE` kinds 1 and 2 with complete TypeMap subjects
and imply an exact `nint` result. `offsetof(Type, FieldPath)` and
`offsetof(Place)` use kinds 3 and 4 with a FieldMap token. A one-field token
is valid only when its exact root is the unconstructed non-generic declaring
type; otherwise the token selects a global FieldPath signature. Type-path
offset is cumulative from its root; Place offset is terminal-only in the
immediate containing type. These observations do not read or execute a
Place.

There is no universal Count or Length instruction. `countof(Type)` and
`countof(KnownShapeExpression)` become ordinary exact `nint` typed constants
after their fixed cardinality is resolved. `countof(Place)` invokes the
selected synchronous Pure `ICountable.Count : nint` getter through ordinary
bound-call machinery, retaining its witness, receiver, effects and
dependencies. Remove the former `length` inventory placeholder without an
alias; an unrelated `Count` or `Length` member remains an ordinary member.

A present optional with a unit payload is distinct from absence. A presence
test is not itself an unwrap. Accessing an optional payload needs the
selected present-state or checked extraction contract.

Aggregate construction does not bypass authored constructors, required
validation, partial-value restrictions or publication rules. Moving a field
out of an owning aggregate requires its exact structural contract; selecting
a field value does not expose the aggregate's hidden ABI address.

Here "extract" simply means selecting the stored field value, for example
the second component of a computed tuple. It is not pointer arithmetic,
reference formation or a write to the original aggregate. A storage-field
read and a value-field extraction can have different access/effect rules
even if a backend implements both as loads.

The Body record schemas carry the selected place, store, call-preparation and
transfer facts. Other logical records remain independently gated; spare
instruction/record capacity does not establish their encoding or execution.

### Conversions

| Operation | Intent |
|---|---|
| `convert` | The ordinary selected conversion for the complete source/destination pair. Integer narrowing and floating-to-integer checks are not interchangeable with floating-format rounding. |
| `convert.wrap` | An admitted explicit wrapping conversion, not an implicit change to the source operand. |
| `convert.saturate` | An admitted explicit saturating conversion under that pair's rules. |
| `bitcast` | An admitted representation-preserving conversion; size equality alone does not prove validity, authority or backing. |

The source type comes from the value. The destination type is named where
needed. A two-byte in-place form can hold a register and small type index.
A four-byte conversion-policy opcode can hold a sixteen-bit type index and
independent four-bit destination/source registers. A typed record handles
larger or other forms; no global width prefix is required.

Nominal conversion operators remain selected calls. The table does not
adopt conversion pairs absent from the chosen language/representation
contract.

## Calls

### Bound target versus callable value

`call.method` and `call.value` are distinct target carriers, not different
evaluation or ownership models:

| Name | Target |
|---|---|
| `call.method` | A selected bound callable/declaration reference, normally through MethodMap. |
| `call.value` | A captured, fully typed callable value in a register or argument home. |

The selected design uses one logical invocation protocol, with compact
windows and a general record form. These names do not expose native
instruction selection.

A bound call can still use virtual/interface/witness dispatch described by
its selected target. A callable value can be a function pointer with an
explicit foreign ABI. These are not respectively synonyms for one native
direct call and one native indirect call.

Overload resolution and source argument adaptation have already occurred.
No consumer selects a method from a matching name.

### Compact call forms

Logical inputs are the receiver, if present, then formal parameters in
declaration order. The signature supplies their complete types and modes.

| Call form | Assigned encoding | Target, inputs and result |
|---|---|---|
| Bound method, compact non-unit result | Short `80`, long `E1` | MethodMap target; consecutive input window; result replaces argument-window base |
| Bound method, exact unit result | Short `82` | MethodMap target and consecutive input window; no result register |
| Callable value, compact non-unit result | Short `81`, long `E2` | Captured callable register; consecutive input window; short result replaces target, long result uses the payload result nibble |
| Callable value, exact unit result | Short `83` | Captured callable register and consecutive input window; no result register |
| General call site | Short `90` for record indexes `0..255`; long `E0` for `u16` | CallSite record specifies captured target, formal-order inputs, result and restricted preparation |

Compact staging windows must fit R0..R13; they cannot stage arbitrary
arguments into the two special slots. General call records can name special
constants only where the selected formal provides a valid type/domain.
This does not limit callable arity: other completed inputs use argument
homes. The logical count includes unit parameters even when the ABI passes
no payload for them.

All sources and the target are captured before result publication. Calls
change their result binding and any consumed inputs according to the
signature; other logical register bindings remain intact. Physical ABI
clobbers and hidden return buffers are backend concerns.

### General call-site record

Record kind `02` (`CallSite`) has this assigned payload:

| Field | Required meaning |
|---|---|
| Selected target | Exact bound method/dispatch/witness target or captured callable value, including its complete signature/ABI obligations. |
| Input sources | Formal-order register values or completed argument homes, with any prepared initialization-destination entries identified. |
| Result destination | Register or proven-vacant result home; an exact unit result may use a unit-only sink. |
| Destination preparation | Restricted typed list of required dependency-valid retirement/vacancy preparation for reusable hidden observation/output homes. |

An argument home stores a completed argument value/descriptor. It is not a
recipe to reevaluate an argument expression or silently reread a changed
source variable. It can be an ordinary compiler-generated typed local;
this does not introduce an expression evaluation stack.

For `F(A(), B())`, selected calls can produce results directly into `R4`
and `R5`, followed by a call-site record mapping those inputs to F's formals.
More than fourteen simultaneously needed argument values can use argument
homes. Nonconsecutive register inputs need not be shuffled solely to fit
the compact form.

The ordinary signature has one complete result, including `void`. A tuple
result is one aggregate value. Initialization outputs follow their separate
state contracts; they are not arbitrary extra ordinary result registers.

### Invocation phases

| Phase | Obligation |
|---|---|
| Capture | Evaluate callable/receiver once at its source position, before arguments. |
| Stage | Evaluate written arguments in source order, including reference/place formation and actual copy/transfer effects. |
| Check | Perform invocation-only receiver/target checks at their required point. |
| Prepare | Retire previous hidden observation/destination values in a dependency-valid order and establish the required vacant destinations. |
| Invoke | Transfer ownership at the ordinary invocation boundary and enter the selected callable. |
| Publish | On normal completion, publish the result and declared result-correlated output states. |

Named arguments do not change source evaluation order. The record maps
already staged values into formal order.

The preparation phase is required by the
[hidden-destination contract](language/calls-and-inference.md#hidden-destination-instances-and-retirement).
It cannot simply be moved before receiver checks or argument staging.
The CallSite record carries a restricted typed preparation list in the general
call-site record. Each entry identifies its actual destination/place,
required transition and dependency ordering. It contains only the selected
invocation protocol's preparation actions, not arbitrary helper calls or
argument-expression recipes.

Capture and Stage are explicit earlier operations. At the call instruction,
the record's selectors name already completed values/descriptors in their
current register bindings or homes. The instruction performs Check, Prepare,
Invoke and normal Publish in that order; it does not recapture a changed
source variable or reevaluate an argument expression.

Compact forms are eligible only when their target/window/result and known
signature represent the complete protocol without an extra per-site list.
Otherwise use the general record, not an unchecked call plus guessed
preparation helpers.

If later argument evaluation fails, the caller cleans staged owners; moved
sources do not revive. Once hidden old values have been retired, later
failure does not roll them back. Borrowed materializations remain caller-owned
under their supporting scopes.

A by-reference argument is a descriptor, not a pointee snapshot. If permitted
aliasing lets a later argument mutate that pointee, the callee can observe
the mutation. An initialization destination's vacancy/authority and normal
outcome relationship remain explicit. Mode and state are not inferred from
an address or method spelling.

Exceptional completion has no normally published result. Handler entry
state does not automatically promise the old destination register or rollback
of mutated places/captures. Nonreturning calls have no normal result or
continuation; their terminal encoding is distinct from a normal unit result.

Caller-unsafety, required dependencies and foreign data/ABI admissibility
survive every call form. No form grants marshalling, pinning, rooting or
lifetime extension. Preserving a logical register across a call does not
promise a machine register survives it.

## Return and abnormal completion

### One return operation

One signature-aware `ret` is assigned; there is no separate `return.unit`:

| Method result | Required meaning |
|---|---|
| Non-unit `T` | Return an initialized complete `T` from the selected value operand. |
| Exact `void` | Return its known unit value without requiring a payload register. |
| No normal completion | A normal `ret` is not permitted by that callable's contract. |

For exact unit return, unused register/operand bits must have a defined
canonical value, rather than reading whichever unrelated value occupies R0.
The ordinary method signature is already available to establish this case.

This does not erase required effects or make an uninitialized unit-local
read valid. Other zero-data nominal types still require their own initialized
value and are not implicitly unit.

### Explicit failfast and unreachable

Use `failfast`, not a generic `fail` or `trap` mnemonic. It terminates normal
execution under the selected fail-fast target/runtime policy, without the
ordinary language unwind/cleanup promise. The assigned failfast forms carry
no reason payload; optional diagnostic presentation comes from SourceMap or
tooling data and is not part of its operation semantics.
It is not a false value, a checked numeric exception or an IEEE invalid event.

`unreachable` is an explicit abnormal terminal instruction with no normal
continuation. Execution raises the selected ordinary language failure:
prepare a fresh event at the instruction's origin, dispatch handlers and
perform cleanup under the normal event/unwinding rules. It is not failfast
or undefined behavior.

This is not a NoThrow operation merely because of its mnemonic. An optimizer
cannot use the instruction itself as proof that its incoming paths never
execute and erase their defined failure. Ordinary reachability/effect facts
still govern optimization and declared NoThrow guarantees.

The exact Core `Terrum.UnreachableException` payload is defined by the
[standard runtime failure contract](runtime/standard-failures.md#6-executed-unreachable):
a public nongeneric zero-data Exception/Copy struct with a public
zero-parameter constructor and no authored construction checks/effects.
The runtime event carries origin and diagnostic context, not fields invented
on that payload. This fixed mapping does not inherit a callable's
`RequireException` default and does not select a similarly spelled provider
type.

Actual event-preparation/transport failure retains its own admitted outcome;
do not fabricate an UnreachableException event if its preparation failed.
The zero-data payload is a `Copy` value but does not implement the ordinary
class-only `IException<TSelf>` contract. Its exact catch behavior and captured
event ownership are defined separately; this does not generalize value
exceptions.

Typed checked operations retain their own failures and source origins.
They do not need normal CFG edges after every check and cannot silently
be replaced with `failfast`.

### One throw family, explicit payload kinds

Rethrowing need not consume another primary opcode. One `throw` family can
select the propagation route from a payload kind, the normalized operand
type or an explicit typed record:

| Payload kind | Meaning |
|---|---|
| Fresh Exception value | Stage a complete eligible value and establish a fresh event/origin at this throw site. |
| Active lexical handler event | Continue that exact event while the selected handler activation still owns it. |
| Captured event owner | Consume exact Core CapturedException and transfer its existing event, origin, causes and dependencies. |

Source bare rethrow can use the active-handler payload. Source
`throw move saved` with exact CapturedException can use the captured-owner
payload. The existing [captured-exception contract](language/captured-exceptions.md)
supplies those eligibility and ownership rules; this is not a new public
handler-token API or thread-global current-exception lookup.

An ordinary copied payload is not the old event. Throwing a payload value
does not preserve provenance merely because its bytes or identity match
something caught earlier. Conversely, captured propagation does not invoke
a constructor, clone the payload, replay cleanup or manufacture a new
top-level origin. The distinction must remain explicit even with one opcode.

All routes retain their actual cleanup, causal replacement and delivery
contracts. Operand construction/evaluation failure propagates its real
failure without fabricating the requested throw. Consumed event owners and
handler responsibility cannot be revived through another register alias.

### Runtime require instruction

Represent `require<E> condition` explicitly instead of requiring an encoded
conditional block and constructor/throw sequence at every site.
The operand is a Boolean or an admitted comparison-condition test; a
failure-selector record identifies the complete E and its selected ordinary
zero-supplied-argument construction plan.

| Outcome | Behavior |
|---|---|
| Condition succeeds | Continue with the ordinary unit/output result; do not construct E or create a failure event. |
| Condition evaluation fails | Propagate that actual failure; do not construct E. |
| Condition is false | Execute the selected ordinary construction once, then propagate the complete result with this requirement's fresh event/origin. |
| Construction fails | Preserve the actual constructor/initialization/cleanup failure and cause rules, not a fabricated E result. |

The existing [requirement-failure owner](language/requirement-failures.md)
fixes selection: explicit E, otherwise the semantic owning callable's
resolved default, otherwise exact Core RequirementException. Resolve this
before serialization; an inlined or called helper does not acquire its
caller's default.

Zero supplied arguments does not mean exactly zero formal parameters.
Ordinary constructor lookup, accessibility, optional defaults or an admitted
empty params expansion apply. The failure selector retains that binding.
It is not a failure factory, invented diagnostic argument or guessed
default value. Generic Exception evidence alone does not prove constructor
availability.

The assigned short `B3` and long `E7` forms reference a
`RequirementFailure` record. That record selects an exact exception TypeMap
entry, `ConstructorBinding` record, condition kind, current register and
optional uint8 predicate mask. The condition is already computed; no
predicate expression is reevaluated by the requirement instruction.

Value-producing requirements over omitted initialization outputs must also
retain the selected availability checks before owning extraction. They may
use a richer typed requirement record or already lowered checks, but cannot
read a vacant payload while testing success. Completed outputs, mutations
and staged owners are not rolled back on a false requirement.

Runtime `require` is not debug-only `assert`, an optimizer assumption or
`static require`. It may be removed only under a valid semantics-preserving
proof, retaining effects that produced its condition. Runtime postconditions
keep their separate selected failure contract.

## Control-flow instructions and large operands

The assigned branch, conditional branch, predicate branch and switch forms
are `B0..B2` and `E3..E7`; they use absolute zero-based Block indexes, with
`ConditionalEdge`, `Switch`, `RequirementFailure` and `Throw` records where
the short form cannot carry the complete contract. `EB` returns a Home
value. `B6`/`EA` have registered instruction/record shapes but no usable
exit-kind or region inventory; they reject until that carrier is assigned.
The small forms and exact operands are listed in the opcode tables.

Every normal control-transfer target is a block index, not a machine
address, bytecode address or byte displacement. Region identifiers name
regions, not code addresses. Each Block's `codeOffset` and `codeLength` locate its bytes for decoding;
those offsets are not branch operands.
Native lowering can later choose native addresses/displacements.

The `ConditionalEdge` record explicitly holds both successors. No
fallthrough is implied by adjacent byte ranges. Transfers must satisfy the
target's entry types, state, regions and lifetime obligations without
reinterpreting register bits.

Every indexed record reference uses a zero-based `u16` Body-local index and
the operation-specific fields assigned to its family:

```text
primary:u8 | control:u8 | payload:u16
```

Records can hold full-width global references and bounded operand lists.
There is no wider index form in this codec; an out-of-range operand rejects.

Unknown operations, unsupported required contracts and invalid
operation/control/record combinations reject at their applicable
access/admission boundary. Exact codec selection is not a substitute for
these checks. Equal version bytes do not authorize guessing between this
draft and another schema.

## Worked expression and realistic workloads

For five by-value `int32` inputs under explicit wrapping/release policy, a sequence for
`(a * b * c) + (d * e)` is:

| Operation | Bytes with ordinary short forms | Bytes if its indicated tiny form is selected |
|---|---:|---:|
| Load `a0:a1` into `R0:R1` | 2 | 1 |
| `R0 = mul.wrap(R0, R1)` | 2 | 1 |
| Load `a2` into `R1` | 2 | 2 |
| `R0 = mul.wrap(R0, R1)` | 2 | 1 |
| Load `a3:a4` into `R1:R2` | 2 | 2 |
| `R1 = mul.wrap(R1, R2)` | 2 | 1 |
| `R0 = add.wrap(R0, R1)` | 2 | 1 |
| Return `R0` | 2 | 1 |
| Total | 16 | 10 |

Both columns have eight operations and three working registers, without a
per-intermediate local or encoded SSA ID. The ten-byte case uses five
assigned tiny tuples: the first argument pair, the two multiply tuples, the
add tuple and the return.

The operation bytes are assigned by the codec tables above. Counts exclude
the Body stream header, maps, indexes and padding. These are field-budget
examples, not compiler emission or runtime performance results.

The corresponding checked/development sequence uses four bytes for each
of its three multiplies and one add. With the ordinary two-byte loads and
return, that is 24 bytes for the same eight operations. Selecting tiny forms
for the first argument pair and return reduces it to 22 bytes; checked
arithmetic itself remains four bytes. Compound full-form recipes can have
different operation counts, so this is not a claim of an optimal encoding.

The workload comparison must include more than that expression:

| Workload | Relevant pressure |
|---|---|
| `List<T>.Add`, indexing and insertion | Retained array/size snapshots, field accesses, ordered checks/stores, cold calls and several call inputs. |
| `Enumerable.Select` and iterator advancement | Captured callable/receiver, generic values, state-machine branches, field-stored enumerators and explicit by-reference access to struct state. |
| `Int32` operations and DivRem | Tiny operations, noncommutative ordering, multiple uses of a quotient, aggregate/multi-result alternatives. |
| `Single` | Ordered/unordered predicates, NaNs, sign operations and type-changing comparison results. |
| .NET 11 `System.Numerics.Decimal32` | Actual IEEE decimal32 arithmetic/helper usage, not a substitution of CLR `System.Decimal`. |
| Larger software arithmetic | Buffers, addressable temporaries, repeated local updates, long streams with small transient working sets. |
| Reference/owner/generic code | Root/backing retention, actual storage identities, cleanup, calls and non-word-sized values. |

A stack can be a source/compiler implementation choice, but this schema
selects register instructions; it assigns no stack-body codec. The register
format does not claim that every stack representation requires deferred
effectful expression trees or that the current byte study proves an
application-wide size or runtime advantage.

## Allocation crosswalk and implementation boundaries

The following crosswalk summarizes the assigned codec and its unassigned
capacity. The opcode and record tables above are normative projections of the
[registry](representation/trrm-1.registry.json); this crosswalk does not
create a second allocation authority. The accepted design remains separate
from compiler/runtime support.

### Header and implementation envelope

| Item | Assigned rule |
|---|---|
| Method implementation kind | `01` stream, `02` field read, `03` field write, `04` field reference, `05` identity forwarder. |
| Stream codec | Separate `instructionEncoding:u8`; `01` is the mixed 1/2/4 register codec. |
| Compact implementation | Exact bound field/method token and required binding/use contract; no empty stream or stream codec. |
| Local inventories | Unsigned `u32` counts; each `u16`-indexed inventory contains at most 65,536 entries. |
| Global references | Retain their typed full-width references; compact descriptors do not truncate FieldDef/MethodDef identities to `u16`. |
| Code and source extents | `u32` byte lengths/offsets and instruction ordinals; independent of `u16` local indexes. |
| Unknown kind/codec | Reject affected use; no guessed decoding, legacy alias or empty-body fallback. |
| Arithmetic/IEEE policy | Encoded in the operation/family contract; never inferred from codec selector or consumer build configuration. |

The stream's first-byte partition remains 16 tiny, 176 short and 64 long
primary positions. Compact method bodies are another implementation kind,
not another instruction-width mode.

### Allocated tiny tuples

Worksheet positions T0..T15 map directly to opcodes `00..0F`. Their exact
semantics and signature/state constraints are normative.

| Position | Opcode | Implied operation and operands |
|---|---:|---|
| T0 | `00` | Signature-aware `ret R0`; exact void uses its known unit result |
| T1 | `01` | `R0 = load.arg 0` |
| T2 | `02` | `R1 = load.arg 1` |
| T3 | `03` | `R2 = load.arg 2` |
| T4 | `04` | `R0:R1 = load.arg.pair 0:1` |
| T5 | `05` | `R0 = load.home 0` |
| T6 | `06` | `R1 = load.home 1` |
| T7 | `07` | `store.home 0, R0` |
| T8 | `08` | `store.home 1, R1` |
| T9 | `09` | `R0 = load.constant 0` |
| T10 | `0A` | `R0 = add.wrap(R0, R1)` |
| T11 | `0B` | `R0 = sub.wrap(R0, R1)` |
| T12 | `0C` | `R0 = mul.wrap(R0, R1)` |
| T13 | `0D` | `R1 = mul.wrap(R1, R2)` |
| T14 | `0E` | `R0 = and(R0, R1)` |
| T15 | `0F` | `R0 = xor(R0, R1)` |

Loads/stores retain their full eligibility, state and source-use contracts.
Constant 0 means typed ConstantMap entry 0, not an untyped numeric zero.
Checked and saturating arithmetic are absent from this shortlist.
No call/field tiny tuple is assumed necessary after compact method bodies.

The measured 80.5% figure belongs to the earlier study, not this set. The
assigned tuples are a concrete initial codec allocation, not a measured
optimum or a complete CoreLib frequency ranking.

### Short-code allocation crosswalk

The first-byte bands group related semantics. Only the listed members are
assigned; every other value in the band is invalid.

| Assigned band | Assigned members | Operand rule |
|---|---|---|
| `10..1F` | `10..18`, `19..1A` | Register/formal/home/constant nibbles as specified above |
| `20..2F` | `20..2B` | Integer wrapping/divide/remainder and strict floating arithmetic |
| `30..3F` | `30..36` | Integer/Boolean bit operations and integer shifts |
| `40..4F` | `40..41` | In-place compare relation and `setcc` |
| `50..5F` | `50..52` | Total conversion, explicit wrapping conversion and bitcast only |
| `60..6F` | `60..67` | Home/field/element place selection, access and explicit reference formation |
| `70..7F` | `70..7F` | Field extraction ordinals 0..15 |
| `80..8F` | `80..83` | Compact bound/value calls with the selected argument window |
| `90..9F` | `90..92` | CallSite, PlaceSelection and SemanticStore record references |
| `A0..AF` | `A0..AF` | Predicate-specific short `jcc` forms |
| `B0..BF` | `B0..B5`, `B7..B8`; `B6` shape only | Small branches/control records, failfast, unreachable and Home return; region exit awaits its carrier |

This groups related semantics without dedicating a one-byte island to every
operation or pretending that every family has all three widths. A compact
operation can have inherent checks, such as ordinary division or a place
access; the full-only rule concerns the selected checked/saturating policy
variants, not every operation that can fail.

### Long-family allocation crosswalk

| Assigned band | Assigned members | Payload rule |
|---|---|---|
| `C0..C6` | Scalar arithmetic, bit operations, compare/setcc, conditional select/move, ternary logic | Registered selector/control byte and four typed register nibbles |
| `C7..CC` | Integer wrapping/checked/saturating recipes, strict floating recipes, scalar `cmp3.and/or` | Recipe/predicate control byte and destination/A/B/C nibbles |
| `CD..CF` | Unassigned | No higher-math or classification operation is implied |
| `D0..D3` | Numeric conversion policies and bit reinterpretation | TypeMap index `u16` plus source/destination registers |
| `D4..D7` | Constant and Home load/store/take | ConstantMap/Home index `u16` plus registered register/control fields |
| `D8..DD` | Place selection, semantic store, place load/take/retire and reference formation | Typed PlaceSelection/SemanticStore record indexes |
| `DF` | Unassigned | No additional place/view/query family is implied |
| `E0..E8`, `EB`; `EA` shape only | CallSite, call windows, branches, switch, require, throw and Home return; region exit awaits its carrier | Typed record/Body-local indexes or exact register fields |
| `E9`, `EC..FF` | Unassigned | No hidden escape, native-memory, atomic or SIMD operation is implied |

For example, full product and DivRem are arithmetic function members whose
four nibbles mean two results and two inputs. They are not ternary
one-result operations. General source loads, stores, reference formation
and native boundaries retain their actual contracts regardless of band.

There are no eight-byte instructions or recursively decoded width prefixes
in this codec. Larger operand lists live in indexed records.

### Common record fields

The record header and common value-selector layouts are assigned above.
These records are separate from instruction payload bytes; their indexes use
the Body's `u16` record namespace and never steal a valid record index for
absence.

| Field | Assigned shape and meaning |
|---|---|
| Value/location selector | `kind:u8,index:u16`; kinds None, Register and Home |
| Register selector | Index 0..15; operation role determines general, R14, R15 or discard validity |
| Home selector | Full zero-based `u16` HomeMap index, including `0xFFFF` |
| None selector | Explicit absent selector with index zero; use is fixed by its containing field |
| Typed record table | `u32` count and offsets, zero-based `u16` references, four-byte record headers and exact kind-specific payloads |
| Variable list | `u32` count followed by its fixed-width entries; no variable-length instruction |
| Optional record reference | Explicit kind/presence field; no reserved-index sentinel |

Selectors capture the current completed binding when their semantic
operation executes. They are not source-variable reevaluation recipes or
references to whichever value later occupies a reused register.
The registry assigns record-kind numbers. The Body row and offset-indexed
record extents are specified above and in [IL metadata](il-metadata.md).
No decoder guesses a record kind or Body layout from plausible payload bytes.

| Record | Required logical fields |
|---|---|
| Call site | Exact bound/value target, formal-order completed register/home inputs, result destination and restricted ordered preparation references |
| Destination preparation | Destination, prior state, transition, source-use mode, formal identity and applicable dependency contract |
| Place selection | Local/field/element/static kind, captured base/index, target identity, referent type and access |
| Semantic store | Typed place, source value, explicit source-use and initialization/replacement transition |
| Conditional edge | Absolute true/false block indexes; register/home state is checked against each block entry |
| Switch | Typed ConstantMap keys, default and absolute target blocks |
| Requirement failure | Exact exception type and zero-supplied-argument constructor binding |
| Throw | Fresh payload, active handler event or captured-owner propagation |
| Region exit | Exact exit kind, optional target and ordered cleanup region indexes |
| SourceMap site | Body implementation/instruction location plus optional arithmetic-recipe step role |

A kind-specific schema can omit fields that its kind makes implicit.
It cannot omit an obligation. Generic record execution accepts only known
operations with selected contracts; it is not a provider-defined helper
language or permission to execute an unassigned family.

Recipe step roles identify failure/source sites without serializing a
growing per-expression value namespace. Consumers reconstruct the ordered
nodes and retain the actual active steps; absent tokens have no step.

### Compact body descriptors

The implementation-kind tag selects the descriptor rather than a code
stream. Kinds `02..05` and their exact bound field or method token layouts
are defined in [Body row layout and compact implementations](#body-row-layout-and-compact-implementations).
The declaration and binding determine receiver/input/result modes,
authority, entry/exit contracts and invocation requirements.

An identity forwarder stages the wrapper's existing bindings under the
ordinary call protocol; it does not reevaluate its caller's expressions.
If additional mapping, preparation, adaptation or body operations cannot
be represented by that exact descriptor, use a stream. A reference getter
requires its real declared reference/dependency contract; a value getter
does not become one.

### Unassigned semantics

SIMD full products, future opt-in IEEE handling/helpers, a dedicated slice
operation, mixed-input full products, the ExceptionRegion/handler carrier
and unselected higher-math/atomic/vector members have no assigned usable
decoding. The `B6`/`EA` RegionExit shapes have no assigned exit-kind or
region inventory and reject; `Throw` active-handler rethrow likewise
requires the missing region carrier. The `CD..CF`, `DF`, `E9` and
`EC..FF` long-primary cells are invalid, not future meanings or automatic
escapes. Reserved space does not approve an operation, source API, runtime
profile or native intrinsic. Missing required semantics reject; there is no
quiet fallback or invented marshalling.

## Selected contracts and bounds

| Area | Rule |
|---|---|
| Arithmetic recipes | Encode all four operations even when overflow is possible; reconstruct their ordered nodes and preserve checks/source sites. The control byte assigns inner bits 0..1, outer 2..3, order 4 and A/B/C negation 5..7; operation values are neg/add/sub/mul = 0/1/2/3. |
| Recipe absence | Integer absence is the modified R14 token; floating absence is the modified R15 token. Recognize combined tokens before reading or negating; absent inputs create no operation step. |
| Integer profiles | Development/Debug defaults to checked and Release defaults to wrapping for integer add/subtract/multiply/negate and updates; explicit IL policy is fixed and independent of consumer configuration. |
| Special registers | R0..R13 are general, R14 is context-typed zero, and R15 is all-bits-set or a role-specific discard/unused field. Decimal BID R15 is the raw negative signaling-NaN pattern. |
| Local limits | Body-local indexes are `u16`; corresponding inventory counts are `u32` and at most 65,536. Global references, code offsets, byte lengths and instruction ordinals use independent widths. |
| Comparisons | Numeric `cmp` produces uint8 L=1/E=2/G=4/U=8; TypeIdentity `cmp` produces only E=2/U=8. Consumers test any uint8 mask by nonzero intersection. Scalar `cmp3.and/or` uses low A/B and high B/C predicates and returns bool; TypeIdentity is not admitted by `CB/CC`. |
| Register movement | `mov` aliases the same value identity. It does not copy, transfer, clean up or create another owner; aliases share consumption state. |
| Storage and places | Typed semantic stores encode initialization/replacement under explicit state, authority and source-use rules. A place is an internal typed location, not a reference or address; reference formation is explicit. |
| Calls | Use compact windows when their invocation phases fit; otherwise use a CallSite record with captured target, formal-order completed inputs, result and restricted ordered preparation. |
| Compact bodies | Exact field read/write/reference and identity-forwarder descriptors retain MethodDef identity/contracts. Other adaptations use ordinary instruction streams. |
| Full product | Scalar `mul.full` publishes low/high halves for all twelve primitive integer identities; low is unsigned, high retains the input type. SIMD and mixed-input forms are unassigned. |
| Unreachable | The instruction raises a fresh ordinary failure with exact Core `Terrum.UnreachableException`, normal handler dispatch and cleanup; it is not failfast or undefined behavior. Handler/cleanup execution remains capability-gated by the unassigned ExceptionRegion carrier. |
| IEEE and other extensions | IEEE status handling remains disabled by default. SIMD full products, mixed-input full products, slices and unselected higher-math/atomic/vector operations are unassigned. |

## Conformance and acceptance cases

Conformance exercises the complete assigned schema and explicitly unassigned
forms:

- Exercise the first-byte size boundaries at `0F/10` and `BF/C0`, every
  assigned tiny tuple and all short-form register selectors.
  Operand bytes that resemble opcodes must not be decoded recursively.
- Verify exactly sixteen primary values are budgeted for one-byte forms,
  with no implicit second-window allocation for every operation.
- Reject unknown header codec selectors, unassigned primary values and
  unsupported family controls. Do not infer another codec from the stream.
- Check explicit source/destination overlap and multi-result publication;
  no source reads a newly rebound destination accidentally.
- Verify Boolean `not` produces a valid Boolean without treating it as a
  raw complement of all storage bits.
- Cover every floating comparison outcome, both NaN placements, signed zeros
  and decimal cohorts.
  Exercise all sixteen predicate masks and all 256 cmp3 predicate bytes,
  including low/high-nibble order and the two family combining rules.
- Cover quiet/signaling policies with both qNaN and sNaN, and distinguish
  invalid status from trap/language-failure handling. Classification must
  not acquire unwanted invalid effects through a self-compare lowering.
- Confirm the default profile produces no language-visible IEEE signals.
  Future opt-in software handling must not silently become enabled by
  a predicate or codec bit.
- Verify one compare condition can feed `setcc`, `jcc` and conditional
  movement without repeating the comparison or its signaling event.
  Check one-hot uint8 cmp outputs and arbitrary consumer masks, including
  zero, high-bits-only, multi-bit and all-one values. Predicate F is true
  for every cmp output, not for every possible uint8 value.
- Check three-input comparison chains with eager and short-circuit behavior;
  an unexecuted second comparison must not signal or evaluate its producer.
  Scalar predicate chains produce bool usable by ordinary branch/selection,
  not a raw relation pair or mandatory additional setcc.
- Exercise all 256 ternary-logic controls, operand permutations, constant
  controls, bit-width preservation, majority/minority and bit-mask versus
  whole-value selection.
- For arithmetic recipes, preserve association, ordered intermediate checks,
  source/failure sites and strict/fused rounding. Do not move an inner
  operation past an effectful producer of the third input.
  Check the 182 unary-canonical controls, both domain-specific absent tokens,
  rejection of absent tokens in required positions, and no dummy evaluation.
  Preserve real integer all-one and floating negative-zero source forms.
- Check full multiplication's exact low/high reconstruction and result types
  for signed/unsigned/native widths, zero, signed minima, both-negative inputs
  and unsigned maxima. Cover 128-bit inputs without a wider primitive, one
  or both discarded halves, and overlapping input/output selectors with
  distinct published result bindings.
- Check development/debug checked arithmetic and release wrapping, including
  relevant constant evaluation. Explicit policies remain fixed, including
  when a consumer imports an artifact under a different build profile.
  Checked and saturating forms must use full four-byte representations.
- Compare stream bodies with field/forwarding implementation forms before
  choosing the one-byte set. Preserve method identity, contracts and access/
  initialization obligations in the compact forms.
- Test same-type joins, incompatible live-in joins, dead incoming slots,
  transiting values and loop backedges.
- Preserve value identity through register aliases; reject double transfer
  or retirement through aliases of one affine responsibility and loss of its
  last available name without another valid holder or explicit retirement.
- Distinguish vacant, live, maybe-live, partial and pending-reservation
  storage; forbid replacement through initialization-only authority.
- Preserve complete-receiver confinement during replacement; no callback
  observes a deliberately exposed invalid interval.
- Preserve element target/check timing around calls and rebinding; do not
  create a reference through a value-returning accessor. Check internal
  place aliases/state changes without duplicated authority, implicit pinning
  or treating their representation as a public native address.
- Keep range index domains, overflow-safe checks, empty ranges and exact
  view/backing results explicit; no slice operation is assigned here.
- Cover direct/value/dispatch calls, nonconsecutive inputs, the 14/15-input
  register-window boundary and larger signatures, unit parameters/results,
  aggregates, staged-owner failure, hidden destination preparation and
  abnormal output/capture state.
- Cover typed ConstantMap duplicates, distinct zero/NaN/decimal representations,
  the selected 65,536-entry boundary and independent wider global references.
  Index `0xFFFF` is valid; absence must not steal it. Reject 65,537-entry
  inventories without treating count zero as a wrapped/full inventory.
- Validate special constant domains and type inference, disallowed writes,
  legitimate discard and unused-field roles without creating invalid
  references, implicit owners or abandoned cleanup responsibilities.
- Check R14's valid Boolean false, positive floating zero, decimal zero
  exponent 0 and per-lane vector zeros. Reject missing type witnesses and
  implicit reference/default-aggregate construction; preserve negative zero
  and alternate decimal cohorts in ConstantMap.
- Check R15's all-one patterns at every admitted width, valid Boolean true,
  binary negative quiet NaNs, decimal BID negative signaling NaNs and
  corresponding vector lanes. Preserve every raw decimal bit despite its
  decoded numeric payload zero; do not quiet/canonicalize the constant read
  or substitute a numeric maximum.
- Check require's true path, false-path construction, zero-supplied-argument
  binding, constructor failure, omitted outputs and source-owner defaults.
- Check all throw payload routes and preservation of event identity/causes,
  with no double propagation through aliases or after handler capture.
- Check executed unreachable's ordinary event origin, handler dispatch and
  required cleanup and exact Core UnreachableException identity; do not lower
  it to failfast or undefined behavior, inherit a RequireException default or
  fabricate the event after preparation failure. Preserve the independent
  terminal behavior of explicit failfast.
- Keep native callable typing, caller-unsafety and target ABI checks
  independent of register packing.
- Bound method-local record/map/block indices; reject overflow rather than
  treating an index as a code address or selecting a wider codec implicitly.
- Rebuild code indexes, block/terminator locations, operand references and
  SourceMap references on re-encoding.

The assigned register codec and Body layouts are the current `(1, 0)` draft
schema under exact out-of-band agreement. This specification does not claim
compiler/runtime support, released-schema stability or native qualification.
Implementation and independent-consumer conformance remain tracked in the
[conformance-gap inventory](../implementation/conformance-gaps.md#cg-001).
