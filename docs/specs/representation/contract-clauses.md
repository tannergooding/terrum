# Contract clause ownership and applicability

## 1. Scope and row admission

This chapter owns the applicability of the assigned Contract clauses. The
[registry](registry.md) owns their numbers, field sequences and exact extents;
[metadata](../il-metadata.md#semantic-contracts-and-stable-binding) owns their
container and references. Source contracts, body truth and feature support are
separate obligations. A well-framed row is not evidence that its declaration
implements the promise or that its feature can execute.
The [shared terminology](../language.md#definitions-and-notation) and
[logical dependency requirements](logical-records.md#general-dependency-contracts-carrier-and-capability-integration-pending)
explain reference/backing relationships. The approved Dependency row
preserves those relationships; row presence alone does not establish body
truth or capability support.

Every Contract begins with `u32 ownerRow, u16 clauseKind, u16 flags=0`.
Its offset-index extent includes this eight-byte header. The clause consumes
exactly its assigned payload: trailing bytes are not padding, alternate
versions or optional attributes. Producers emit only assigned clause kinds
with valid flags, reserved fields and in-range references. A support claim
for a required clause includes its meaning even when its owner is uncalled
or another clause would make its outcomes unreachable. Consumers validate
required clause shapes, owners, references and support, rejecting malformed
or unsupported clauses before affected use; they are not required to prove
the semantic truth of a structurally consumable clause. Diagnostic wording
is not prescribed.

Only TypeDef and MethodDef have Contract ranges in this schema. The assigned
clause kind fixes its owner table: MethodDef for Effects, OutputState,
Dependency and NativeCallable; TypeDef for Cleanup. Transfer remains
semantically inactive.
`ownerRow` is a nonzero typed `u32` row index with reserved-zero high byte,
not a DefId or table selector; a valid artifact names an existing row. A FieldDef, PropertyDef,
MethodSig, GenericParam or Body cannot own one of these ranges. Each owner's
`contractFirst/count` is `(0, 0)` for an empty range, or a nonempty contiguous
in-bounds range. Producers encode ranges whose mathematical bounds fit the
owning table.

The approved static-initialization direction places `InitializationRole` and
`InitializationDemand` as additional MethodDef-owned clause kinds in these
ranges. The logical occurrence rules are settled: at most one role per
method, required where the method has an initialization role; and zero or
more demands, one per exact closed eager group, with duplicate group demands
rejected. Role variants use one `InitializationRole` clause kind with a
closed tag, not separate clause kinds. The approved candidate row shapes are
20 bytes for `InitializationRole` (the 8-byte header followed by
`u8 roleTag, u8 flags=0, u16 reserved=0, u32 eagerGroupRow,
u32 ownerTypeSpec`) and 16 bytes for `InitializationDemand` (the header
followed by `u32 eagerGroupRow, u32 ownerTypeSpec`). Role tags are
`1=eager initializer`, `2=lazy initializer`,
`3=authored static constructor`, `4=eager-phase helper`,
`5=lazy-phase helper`; zero and unknown tags are invalid. Clause IDs and
typed-reference encodings remain unassigned; these candidate shapes do not
admit new physical kinds or change the currently assigned clause registry.

Every Contract row belongs to exactly one range and names that range's owner
under its clause-implied table.
Orphaned rows, overlapping ranges, shared rows and mismatched owners reject.
Row order within a range does not create execution order or combine promises.
An imported expected declaration has its own complete range; binding does not
replace it with a provider's physical rows.

## 2. Clause matrix and uniqueness

All byte lengths below include the header. Occurrence keys are local to each
containing TypeDef or MethodDef declaration and its range. Several independent
expected declarations resolving to one provider identity do not merge their
ranges or clause occurrences; binding checks every expectation. A parameter
ordinal is local to its selected MethodSig.
Duplicate keys reject even when their payloads agree or one is a zero mask.
There is no merge, last-row-wins or implicit disjunction rule.

| Clause | Bytes | Admitted owner and subject | Occurrence key | Absence |
|---|---:|---|---|---|
| `0001` Effects | 12 | MethodDef; its declared callable extent | `(owner, Effects)`; at most one | Mask zero: no assigned promise bits and no caller-unsafe requirement |
| `0002` OutputState | 20 | MethodDef; selected `ref<init T>` destination or `Span<init T>` range parameter and exact published normal-outcome predicate | `(owner, OutputState, parameterOrdinal, predicateRef)`; at most one per predicate/output pair | No row supplies an additional conditional state relation; unconditional complete output needs no row |
| `0003` Transfer | 16 | No semantically admitted application; transfer/cleanup mode values are unassigned | No semantically admitted occurrence or assigned variant key | Does not erase required transfer or cleanup obligations |
| `0004` Dependency | 20 | MethodDef; result, checked init-output or retained holder/receiver relation | `(owner, destinationKind, destinationOrdinal, phase, sourceKind, sourceOrdinal)` | No row supplies an additional dependency relation |
| `0005` Cleanup | 12 | TypeDef with admitted Unique and selected complete-value cleanup contracts | `(owner, Cleanup)`; at most one | No designated authored cleanup operation; transitive retirement is not thereby suppressed |
| `000C` NativeCallable | 12 | MethodDef; effective native calling convention | `(owner, NativeCallable)`; at most one | Method has no native-callable role |

The NativeCallable payload is `u8 callingConvention, u8 flags=0,
u16 reserved=0`. Convention values are `1=Unmanaged`, `2=Cdecl`,
`3=Stdcall` and `4=Vectorcall`; zero, unknown values and nonzero reserved
fields reject. The clause marks the MethodDef as native-callable and makes
calls through that role caller-unsafe. Interface-level source defaults are
published as the effective convention on each method declared by that
interface; inherited methods retain their declaring method's convention.
A method-level source contract selects the effective convention for that
method. Method-form NativeExport references this exact unique clause rather
than copying the convention; an import's own NativeImport record carries its
effective convention and does not require a NativeCallable clause merely to
form a native address.

Published predicates use a logical attachment: each
`PredicateGraph` is referenced by a MethodDef-owned typed `Contract` clause
using one clause kind for all predicate phases. Its payload retains the phase
and an explicit zero-based, contiguous `u32` phase-local declaration ordinal
independent of physical clause-row order. The `u8` phase tags are
`1=Precondition` and `2=NormalPostcondition`; zero and unknown tags are
invalid. Gaps and duplicate ordinals are invalid. Body-level
`static require`/`static ensure` obligations
are not these declaration attachments. The graph owns its root, subjects and
occurrence tree
rather than duplicating callable/phase/order
identity, and references its own separate actual read-footprint record. The
callable-level allowed-root contract remains distinct. This clause is not yet
in the assigned matrix. Its approved candidate extent is 20 bytes total: the
8-byte Contract header followed by
`u8 phaseTag, u8 flags=0, u16 reserved=0, u32 phaseLocalOrdinal,
u32 predicateGraphRef`. The field uses the standard typed row reference: a
little-endian `u32` row index in `1..0xFF_FFFF` with reserved-zero high byte.
Its target table is fixed by the assigned clause kind, and binding remaps it
through the typed-reference map. It is not a polymorphic `DefId`.
The clause kind and PredicateGraph section/Feature assignment remain open.

Effects retains its assigned bounded use. OutputState and Dependency have
approved row semantics, but executable admission requires their complete
predicate, place, publication, dependency and capability-closure obligations.
The OutputState predicate target is owned by the shared published-predicate
codec. Dependency destinations do not use a NativeOutput row. Transfer
remains reserved and semantically inactive;
no payload value, including all-zero bytes, activates an ownership-transfer
variant. No semantic consumer may use or discard an unsupported required
clause.

Absence never substitutes for a required source or retained contract. In
particular, a producer must preserve an adopted source default's normalized
meaning under its governing rules. Missing OutputState is not permission to
guess a conditional output relation. A relation outside the assigned
predicate, destination or phase representation needs its complete admitted
carrier or before-use rejection.

## 3. Effects

An Effects payload is one little-endian `u32` mask. The only assigned bits are
`02` DoesNotReturn and `04` caller-unsafe. The four combinations of those
bits are structurally permitted; the retired `01` NoThrow bit and bits
`08..80000000` reject rather than becoming private extensions. A valid
combination does not prove implementation truth.

Each callable owns its own mask. A PropertyDef uses its selected getter's
complete MethodDef contract; there is no property-owned Effects row or
name-derived `get_` lookup. Type construction, initialization and cleanup use
their actual selected callable contracts, not a TypeDef blanket mask.
Enclosing types do not supply inherited Effects defaults. A future callable
type or phase-specific effects carrier requires its own admitted schema.

An absent Effects row and one well-formed zero-mask row have the same assigned
meaning. A canonical producer omits the zero-mask row; a semantic reader
accepts the explicit zero without acquiring another promise or rejecting the
owner solely for that representation. This equivalence does not permit two
Effects rows, even when both are zero. Canonicality auditing is separate from
this semantic equivalence.

Caller-unsafe is an obligation on the call site; it does not authorize unsafe
operations inside the callee or grant receiver write, alias separation or
lifetime authority. The current draft row set does not thereby encode async
completion.

The [directional binding relation](binding-and-replacement.md#3-bounds-predicates-and-retained-observations)
preserves expected DoesNotReturn and caller-unsafe facts without treating
mask values as an ordering. Applicability is checked before that comparison;
a private body or stronger unrelated contract cannot cure an invalid owner.

## 4. DoesNotReturn and actual completion

The checked source [no-return contract](../language/expressions-and-statements.md#no-return-contracts)
is applicable to any declared logical result type, not only `void`.
It promises that the actual invocation has no normal completion. An escaping
failure or divergent execution can satisfy it; returning a value, returning
unit or normally falling through cannot. DoesNotReturn states only the
absence of a normal completion; it does not bound which failures may escape.

The declaration and every call keep their ordinary complete signature, result
type, argument arity, modes and source validation. DoesNotReturn does not introduce
a public bottom type, an implicit conversion from `void`, a result-discard
adaptation or permission to ignore ill-typed unreachable source.
No result is published on the absent normal edge, but its declared type is
not erased from metadata or the typed Body inventory.

The register-codec `CallSite` has no serialized result-definition ID. A
normal unit-returning call uses result selector `None` and continues; a
normal non-unit call uses a register or Home result selector. A call whose
bound target has verified `DoesNotReturn` uses result selector `None` for any
declared result type and is the terminal instruction of its Block, with no
normal successor or published result. The target keeps its complete
signature, and its selected Effect contract—not a unit result or sentinel—
distinguishes this terminal call from a normal unit call. No following `Fail`
fallback or legacy result-ID escape is part of this schema. Required framing,
type and supported-form validation still precedes affected use.

A task/cursor factory that returns normally does not satisfy DoesNotReturn
because the returned operation later fails, diverges or never produces an
item. An async call that establishes and returns a responsible Task likewise
has a normal invocation edge. A generated operation's own promise must refer
to that operation's actual identity and extent; it cannot be substituted for
its creator's contract.

Applicability alone neither proves no-return truth nor enables unsupported
result representations. A consumer must support the exact representation and
all required capability prerequisites before affected execution; a partial
implementation profile does not define a reduced TRRM 1.0 conformance floor.
Removing a retained no-return promise breaks callers that relied on its
absent normal edge. Adding it cannot erase independently promised normal
completion, output states, cleanup or phase obligations.

## 5. OutputState

Each OutputState payload is `u32 parameterOrdinal, u32 predicateRef, u8
state, u8 reserved[3]=0`. It selects a zero-based formal in the owner's
MethodSig whose complete type is `ref<init T>` or `Span<init T>`; the
parameter uses ordinary Param mode `00`. The complete TypeSpec preserves the
initialization capability/view. This does not use a special initialization
parameter mode, `init ref<T>`, or an ordinary `ref<T>` with a mode-only
reinterpretation.
The predicate is an exact published normal-outcome predicate identity, not a
second expression language, numeric status heuristic or name-based test.
Canonical Boolean-success defaults use their normalized true/false
predicates; authored non-Boolean statuses use their checked predicates.

`state` is `0` for vacant or `1` for fully complete and publishable.
Unconditional outputs are complete on normal return and need no row.
For `Span<init T>`, complete means every element in the whole range is a valid
live `T`; no mixed or prefix-initialized state is represented. Fully complete
includes actual ownership and dependencies, not zeroed storage; a live
optional containing null is distinct from vacancy. Partial initialization is
never state `1`. Exceptional state is indeterminate and is not encoded by
this clause.

The occurrence key includes both the parameter ordinal and predicate
identity. Duplicate pairs reject. For one output and one normal outcome,
applicable rows must agree on state; a declaration whose predicates make the
published state ambiguous is invalid. Distinct outputs and distinct
predicates remain independent. A predicate reference must resolve in the
shared published-predicate codec. Deferred completion uses phase-specific
dependencies and does not retarget this normal-invocation relation.

## 6. Dependency

Each Dependency payload is `u8 destinationKind, u8 sourceKind, u8
relationship, u8 phase, u32 destinationOrdinal, u32 sourceOrdinal`. Values
are:

| Field | Value | Meaning |
|---|---:|---|
| `destinationKind` | `1` | Normal result |
|  | `2` | Checked initialization output |
|  | `3` | Retained formal holder |
|  | `4` | Retained receiver |
| `sourceKind` | `1` | Formal parameter |
|  | `2` | Receiver |
| `relationship` | `1` | Borrow source storage |
|  | `2` | Forward existing backing |
|  | `3` | Declared union of both relationships |
| `phase` | `1` | Normal publication |
|  | `2` | Retention/capture into an existing holder on every applicable exit |
|  | `3` | Deferred-completion publication |

Ordinals are zero for the result and receiver; formal ordinals follow the
MethodSig declaration order. Destination ordinals identify their selected
result, formal output or retained holder. Source ordinals identify formals or
the receiver in the same declaration namespace.

An explicit empty-source row has `sourceKind`, `relationship` and
`sourceOrdinal` all zero. It cannot coexist with source rows for the same
destination kind/ordinal and phase. Nonempty rows use only assigned nonzero
source and relationship values. Rows are keyed by destination kind/ordinal,
phase and source kind/ordinal; relationship `3` is an explicit declared
union, not an inferred merge. Repeated rows express multiple sources and
destinations without a fixed count cap.

`Dependency` records backing and retention relations, not ownership
transfer. Ownership handoff is derived from the complete type, explicit
call/Body transfer, responsible holder and selected Cleanup identity.
Reserved Transfer remains inactive. The row has no general field or
subregion path; an implementation cannot claim support for a required shape
that the selected shared contracts cannot identify. The shared row does not
extend source lifetimes, authorize unrelated storage, or prove body truth.

## 7. Cleanup

The Cleanup payload is a nonzero typed `u32` MethodDef row index, not a DefId,
Body index, String or interface-name convention. It designates the exact
operation required by the source
[complete-value cleanup contract](../language/ownership-and-cleanup.md#complete-value-cleanup-and-transitive-field-retirement):
a synchronous parameterless readonly instance method of the selected unique
type, returning void, with nonsuspending cleanup.
Static methods, constructors, accessors, async/extern or caller-unsafe
declarations cannot supply that automatic operation.
The current MethodDef target selector supplies no method-generic argument
recipe; unresolved method formals reject its use. Enclosing type substitution
still retains the exact selected operation under its admitted generic contract.

Resolve its owner, receiver and signature in the complete binding context.
The operation may have ordinary non-public declaration visibility; retaining
it as cleanup is not a user-callable destruction alias. A borrowed receiver
with different access, a method of an unrelated nominal type or a guessed
same-layout helper does not satisfy the selected contract.

The current TypeDef flags do not encode Unique, and MethodDef flags do not
encode every callable role or execution extent. Neither a struct kind nor
`bodyRow=0` establishes those missing facts. Inspection can check the row
target and available signature/effects facts, but independent semantic use
requires all admitted uniqueness, role, phase and ownership records. An
unsupported or missing fact rejects use; it is not filled in from a name.

Cleanup does not prove termination. A cleanup that never completes cannot be
treated as having completed retirement, discharged remaining fields or
released responsibility. Conversely, applicability does not add a general
termination promise or forbid divergence merely because it is undesirable.
Complete retirement keeps authored cleanup before reverse-declaration fields,
and partial owners keep their distinct structural retirement rules.

## 8. Binding, rewriting and evidence boundaries

Producers establish valid owner, range, extent, ordinal, mask and
reserved-value relationships, including global orphan/overlap invariants.
Consumers check the encoded ranges and relationships needed for safe
consumption before relying on them; any broader structural validation claim
states its additional scope. A complete binding retains every applicable
clause and any additional
provider fact actually used by optimization, with its selected subjects and
phases. The mask, row existence or body absence is not proof of semantic correctness.

Reordering or packing remaps every clause-implied typed owner row, Contract
range, referenced
MethodDef and selected signature/parameter context. Clone context-sensitive
structural rows when required; physical row equality does not identify nominal
subjects. Ordinals keep logical declaration order even when Param rows move.
Transform decoded reference fields, not coincident payload octets. Equivalent
zero-mask omission does not authorize dropping a nonzero guarantee, output
state, cleanup selection or reserved required record.

[Conformance cases](../conformance/il-contract-clauses.md) distinguish
independently decoded row/range/signature facts, finite supplied truth/flow
models and document-only source, runtime and transformation obligations.
They do not certify a compiler, general linker, ownership executor or
arbitrary-body proof engine.
