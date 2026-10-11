# Contract clause ownership and applicability

## 1. Scope and row admission

This chapter owns the applicability of the assigned Contract clauses. The
[registry](registry.md) owns their numbers, field sequences and exact extents;
[metadata](../il-metadata.md#semantic-contracts-and-stable-binding) owns their
container and references. Source contracts, body truth and feature support are
separate obligations. A well-framed row is not evidence that its declaration
implements the promise or that its feature can execute.

Every Contract begins with `u32 ownerRow, u16 clauseKind, u16 flags=0`.
Its offset-index extent includes this eight-byte header. The clause consumes
exactly its assigned payload: trailing bytes are not padding, alternate
versions or optional attributes. Unknown kinds, nonzero header flags, invalid
reserved fields and out-of-range references reject their applicable admission.
An accessed unknown required clause cannot be ignored even when its owner is uncalled
or another clause would make its outcomes unreachable.

Only TypeDef and MethodDef have Contract ranges in this schema. The assigned
clause kind fixes its owner table: MethodDef for Effects/OutputState,
TypeDef for Cleanup. `ownerRow` is a nonzero typed `u32` row index with
reserved-zero high byte, not a DefId or table selector; its row must exist.
A FieldDef, PropertyDef, MethodSig, GenericParam or Body cannot own one of
these ranges. Reserved Transfer/Dependency shapes have no admitted owner
application until their complete variants/owner rules are assigned. Each owner's
`contractFirst/count` is `(0, 0)` for an empty range, or a nonempty contiguous
in-bounds range. Mathematical range bounds must be established before
indexing; wrapped addition cannot validate a range.

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
| `0002` OutputState | 16 | MethodDef with exact Boolean result; selected initialization-destination parameter | `(owner, OutputState, parameterOrdinal)`; at most one per parameter | No row supplies an additional Boolean state correlation |
| `0003` Transfer | 16 | No semantically admitted application; transfer/cleanup mode values are unassigned | No semantically admitted occurrence or assigned variant key | Does not erase required transfer or cleanup obligations |
| `0004` Dependency | 20 | No semantically admitted application; result/source kinds and flags are unassigned | No semantically admitted occurrence or assigned variant key | Does not establish fresh backing or erase required dependencies |
| `0005` Cleanup | 12 | TypeDef with admitted Unique and selected complete-value cleanup contracts | `(owner, Cleanup)`; at most one | No designated authored cleanup operation; transitive retirement is not thereby suppressed |

Effects is usable in the bounded scalar executable profile. OutputState and
Cleanup have inspectable assigned shapes and the applicability rules below,
but remain non-executable until their complete publication, exceptional,
ownership and feature obligations are admitted. Transfer and Dependency
payloads can be inspected as reserved shapes in the common TypeDef/MethodDef
container; no payload value, including all-zero bytes, activates a variant.
No semantic consumer may use or discard such a required reserved clause.

Absence never substitutes for a required source or retained contract. In
particular, a producer must preserve an adopted source default's normalized
meaning under its governing rules. Missing OutputState is not permission to
guess that a Boolean method initializes on true, on false or unconditionally.
The bounded row does not encode arbitrary conditional predicates or later
completion phases; a required relation outside its shape needs its admitted
representation or before-use rejection.

## 3. Effects

An Effects payload is one little-endian `u32` mask. The only assigned bits are
`01` NoThrow, `02` DoesNotReturn and `04` caller-unsafe. All eight combinations
are structurally permitted; bits `08..80000000` reject rather than becoming
private extensions. Valid combination does not prove implementation truth.

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

NoThrow bounds escaping failures over the declared extent. It is not Pure,
ConstEval, termination, no-allocation, no-blocking or no-safepoint. In
particular, an async declaration's established
[execution extent](../language/async-execution.md#effects-and-independent-contracts)
is not shortened by this mask, and the bounded scalar codec does not thereby
encode async completion. Caller-unsafe is an obligation on the call site; it
does not authorize unsafe operations inside the callee or grant receiver
write, alias separation or lifetime authority.

The [directional binding relation](binding-and-replacement.md#3-bounds-predicates-and-retained-observations)
preserves expected promise bits and does not add caller-unsafe to a safe
expectation. Applicability is checked before that comparison; numeric mask
ordering, a private body or a stronger effect cannot cure an invalid owner.

## 4. DoesNotReturn and actual completion

The checked source [no-return contract](../language/expressions-and-statements.md#no-return-contracts)
is applicable to any declared logical result type, not only `void`.
It promises that the actual invocation has no normal completion. An escaping
failure or divergent execution can satisfy it; returning a value, returning
unit or normally falling through cannot. NoThrow is independent: their
combination cannot satisfy both by an escaping failure.

The declaration and every call keep their ordinary complete signature, result
type, argument arity, modes and source validation. DoesNotReturn does not introduce
a public bottom type, an implicit conversion from `void`, a result-discard
adaptation or permission to ignore ill-typed unreachable source.
No result is published on the absent normal edge, but its declared type is
not erased from metadata or the typed Body inventory.

For the current `Call`, zero result ID is still legal only when the selected
result is `void`; a non-unit no-return target requires the ordinary typed
result definition operand. That result cannot be observed through a normal
continuation that does not exist. The bounded terminal-call shape retains its
following Fail fallback; only a no-return target admitted under the
producer-trust/audit policy makes
that fallback unreachable. It does not excuse earlier failures or required
framing, type and supported-form validation of retained records.

A task/cursor factory that returns normally does not satisfy DoesNotReturn
because the returned operation later fails, diverges or never produces an
item. An async call that establishes and returns a responsible Task likewise
has a normal invocation edge. A generated operation's own promise must refer
to that operation's actual identity and extent; it cannot be substituted for
its creator's contract.

Applicability alone neither proves no-return truth nor enables unsupported
result representations. The scalar profile retains its existing type/call
matrix; signatures outside it are rejected before affected execution.
Removing a retained no-return promise breaks callers that relied on its
absent normal edge. Adding it cannot erase independently promised normal
completion, output states, cleanup or phase obligations.

## 5. OutputState

OutputState selects a zero-based `parameterOrdinal` in the owner's actual
MethodSig. Its Param row must belong to that signature and ordinal, with
mode `01` and the complete unpinned readonly outer `ref<T>` required for an
initialization destination. Mode `00`, `implicit`, `write ref<T>` and
`pin ref<T>` are not substitutes. Inner `T` retains its full qualification;
it can be unit or an optional value without confusing vacancy with presence.
The result must be the normalized atomic `bool`, not an optional Boolean,
integer, same-sized nominal type or later `Task<bool>` completion.

`onTrue` and `onFalse` independently select vacant `0` or fully initialized
`1`; all four pairs can describe explicit source contracts. `onExceptional`
must be `0`, and the reserved byte must be zero. Fully initialized means a
valid complete value and its actual ownership/dependencies, not zeroed
storage. A false initialized-null optional is distinct from false vacancy.
Partial initialization and pending publication cannot be encoded as state `1`.

Different initialization parameters can have different state pairs. They
occupy different keys even within one method; two rows for the same ordinal
reject instead of refining or combining that output. The ordinal is not the
receiver, result, a Param table row number or a field offset. Sharing a
MethodSig does not share a MethodDef's clauses.

The relation applies to the specified invocation's normal Boolean observation,
with its existing checks and cleanup. Async completion, producer advancement
and lending require their own identity/phase/reservation contracts; this row
does not retarget itself because a signature looks familiar. NoThrow or
DoesNotReturn neither changes its bytes nor deletes a retained state promise.
The absence of a possible outcome is not proof that a different exact state
contract satisfies binding.

## 6. Cleanup

The Cleanup payload is a nonzero typed `u32` MethodDef row index, not a DefId,
Body index, String or interface-name convention. It designates the exact
operation required by the source
[complete-value cleanup contract](../language/ownership-and-cleanup.md#complete-value-cleanup-and-transitive-field-retirement):
a synchronous parameterless readonly instance method of the selected unique
type, returning void, with checked NoThrow and nonsuspending cleanup.
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

NoThrow does not prove termination. A cleanup that never completes cannot be
treated as having completed retirement, discharged remaining fields or
released responsibility. Conversely, applicability does not add a general
termination promise or forbid divergence merely because it is undesirable.
Complete retirement keeps authored cleanup before reverse-declaration fields,
and partial owners keep their distinct structural retirement rules.

## 7. Binding, rewriting and evidence boundaries

Required owner, range, extent, ordinal, mask and reserved-value checks on
accessed records survive producer trust. A consumer checks the complete range
it uses, without scanning unrelated ranges solely for global orphan/overlap
proof. Producers and broader validation claims still establish those global
invariants. Known ownership contradictions reject under the
[reader-trust partition](reader-trust.md). A complete binding retains every
applicable clause and any additional
provider fact actually used by optimization, with its selected subjects and
phases. The mask, row existence or body absence is not a trust credential.

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
