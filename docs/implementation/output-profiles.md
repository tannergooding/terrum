# Hosted output implementation profiles (informative)

These profiles describe the existing development implementation, including
historical `out` modes. They do not add parameter spellings, narrow the
language's generic/output contracts, or grant TRRM execution support.
The canonical source model uses
[reference descriptors and initialization destinations](../specs/language/references.md);
[output publication](../specs/language-contracts.md#2-outcome-initialization-and-nullability-are-separate)
defines its semantics.

The inventory below retains the implementation's profile names and exclusions.
Its use of "accepted" means accepted in that development implementation,
not another normative language profile.

The accepted non-Boolean/void `out` profile also admits ordinary non-unique,
handle-free `Copy`-struct outputs. These outputs are unconditional: every
normal return fully initializes them, and the return value is not treated as a
success discriminator. Exceptional departure publishes no output, and the
caller cannot read the destination on that exceptional path. `out var` infers
the declared parameter type. Compatible public semantic-module and native
module exports and native graphs preserve this contract. This profile does
not include unique values, managed-handle-bearing values, buffers, views,
cursors, generic outputs, or executable TRRM; it does not change Boolean
conditional `out` or `init ref<T>`. Custom conditional postcondition-driven
`Copy` outputs are outside this profile; explicit postconditions remain
governed by the general outcome rules above.

Compilation-local default-domain generic templates also admit ordinary `out T` with a fixed
`int32`, `uint32`, `int64`, `uint64`, `char16`, or `void` result, provided the
concrete `T` satisfies the existing non-Boolean `out` shape. The result is not
a success discriminator; every normal return fully initializes the output, and
exceptional departure publishes none. This standalone profile covers closed
private specializations only; compatible semantic-module and native-module
replacements preserve those closed bodies without exposing the generic
template or changing the output ABI. Formal-valued results, generic
`init ref<T>` outputs, fixed-valued output side parameters, public generic
exports or witnesses, source-free TRRM, and custom conditional
postcondition-driven generic `Copy` outputs remain outside this profile;
Boolean publication rules are unchanged. Mixed generic `out`/`init` calls are
governed separately below.

Compilation-local default-domain generic templates admit `float32` and `float64` as concrete
generic type arguments. Their substituted `T` values may be passed, returned,
assigned, and published through the existing specialization and output-mode
rules, including forwarding through nested generic helpers. This does not
grant symbolic `T` arithmetic, comparison, conversion, or other primitive
operator rights, or widen shared `Copy` predicates. Floating fields in
concrete dependencies reached while proving a generic body are governed by
the separate proof-hydration profile below; admitting floating generic
arguments does not itself widen that profile. Public generic exports or
witnesses, source-free TRRM, and scalar-frontend verification or emission also
remain outside this hosted profile.

While proving a compilation-local generic body over default-domain formals, the compiler may
hydrate reached `float32` and `float64` leaves only in non-unique `Copy`-struct
descriptors and reached top-level or struct-owned concrete callable signatures.
Hydration preserves descriptor identity, fields, layout, visibility,
mutability, effects, caller-safety, output mode (`out` versus `init`),
specialization limits, and existing persistence rules. These are concrete
dependencies; this does not confer symbolic `T` capabilities. Floating fields
or signatures in managed-class dependency closures remain outside this profile.
It does not alter shared `Copy` predicates, primitive operator-witness
eligibility or results, generic scalar parameter tables, `ConstEval`, scalar
floating getters, public generic ABI, source-free TRRM, or scalar-frontend
verification or emission.

Compilation-local default-domain generic templates also admit `init ref<T>` outputs with a
fixed `int32`, `uint32`, `int64`, `uint64`, `char16`, or `void` result. Every
normal return, including implicit `void` fallthrough, must fully initialize
the destination; exceptional departure publishes none, leaving it vacant and
unreadable. Boolean results keep their conditional rule: `true` publishes and
`false` leaves the destination vacant. Calls must use `init` with vacant
storage, and type inference remains unchanged: explicit type arguments, typed
inputs, or an already typed vacant destination may establish `T`; `init var`
alone cannot. the default-domain Copy eligibility includes
compilation-private managed-handle specializations and their rooting and
cleanup. Closed private specializations survive compatible semantic-module and
native-graph producer replacements without changing output-mode metadata or
wire formats. Unique or owning values and fixed-valued output side parameters
remain outside this standalone profile, as do public generic exports or
witnesses, source-free TRRM, and scalar-frontend verification or emission.
Mixed modes are governed separately below.

A separate fixed-floating result profile admits `float32` and `float64` as
results for compilation-local default-domain generic templates with at least one
generic-typed output, provided all generic outputs use one mode and the
template has no fixed-valued output side parameters. A floating result is not
a success discriminator: every output must be fully initialized on every
normal return. Exceptional departure follows the existing mode rules. Closed
private specializations preserve their result width across compatible
semantic-module and native-graph producer replacements without changing
output-mode metadata or wire formats. This result profile does not admit fixed
floating by-value parameters, formal-valued results, output-free templates,
floating protocol results, symbolic floating operations, or source-free TRRM.
It defines no new arithmetic or NaN-payload contract. Generic mixed-output and
fixed-side-output combinations with floating results remain outside this
single-mode, no-side profile.

A compilation-local default-domain generic template may combine generic `out T` and
`init ref<U>` output formals when its result is fixed `int32`, `uint32`,
`int64`, `uint64`, `char16`, or `void`. Every output must be fully initialized
on every normal return, including implicit `void` fallthrough. Each formal
retains its own mode and eligibility: `out` uses its accepted writable
destination and non-unique, handle-free `Copy` shape, while `init` requires
vacant storage and retains compilation-private managed-handle support. When a
type parameter is used by both modes, its concrete type must satisfy both
eligibility rules. On checked exceptional departure, an `init` destination
remains vacant; an existing `out` destination has no rollback guarantee and
cannot be read in a catch path. Closed private specializations preserve the
`IsOut`/`IsInit` metadata and survive compatible semantic-module and
native-graph producer replacements without changing the module ABI or wire
formats. Boolean-result mixed-mode behavior is unchanged. Fixed-valued output
side parameters, unique or owning values, formal-valued results, public generic
exports or witnesses, source-free TRRM, scalar-frontend verification or
emission remain outside this profile.

A compilation-local default-domain generic template may also add fixed scalar output
parameters alongside at least one generic-typed output when its result is a
fixed `int32`, `uint32`, `int64`, `uint64`, `char16`, or `void`. Fixed output
types are limited to `bool`, `char16`, `int32`, `uint32`, `int64`, `uint64`,
`float32`, and `float64`; they do not infer generic type arguments, provide a
generic output anchor, or add managed-handle or unique-value support. Every
output must be fully initialized on every normal return, including implicit
`void` fallthrough, under its mode-specific proof. On exceptional departure,
`init` destinations remain vacant, while `out` destinations have no rollback
guarantee and cannot be read in catch paths.
Compatible semantic-module and native-graph replacements preserve closed
private specializations and output-mode metadata without ABI or wire-format
changes. The generic-typed outputs in this profile must all use one mode:
fixed side outputs do not combine with a template that mixes generic `out` and
generic `init`. Boolean-result behavior is unchanged; floating results,
formal-valued results, and templates without a generic-typed output remain
outside this profile.

A compilation-local default-domain generic template may combine a fixed `float32` or
`float64` result with generic outputs using mixed `out` and `init ref<T>` modes
and fixed scalar output parameters, provided at least one generic-typed output
is present. For example, a template may combine `out T` with `init ref<U>`.
Fixed side output types use the existing scalar set:
`bool`, `char16`, `int32`, `uint32`, `int64`, `uint64`, `float32`, and
`float64`. Fixed results and side outputs do not infer generic type arguments.
Each output retains its mode-specific eligibility; a type parameter used in
both generic modes must satisfy both. Every output must be fully initialized
on every normal return, and the floating result is not a success discriminator.
On checked exceptional departure, `init` destinations remain vacant and
unreadable, while `out` destinations have no rollback guarantee and cannot be
read in a catch path. Closed private specializations preserve their output-mode
metadata across compatible replacements without changing ABI or wire formats.
This profile does not add floating arithmetic or NaN-payload guarantees, and
does not admit public generic exports or witnesses, output-free templates,
fixed floating by-value parameters, formal-valued results, floating protocol
results, symbolic floating operations, source-free TRRM, or scalar-frontend
verification or emission.

## Provisional TRRM output Bodies

The current `ScalarInitOutput` writer/reader is a bounded experiment, not
implementation of the accepted general output/place contract. It uses
unregistered Feature `1` and a historical minimum-rule-revision interpretation;
the writer requires revision `8`, while the reader also accepts revisions
`1..7` with their narrower implementation rules. These are current reader
behaviors, not compatibility promises or candidate standard allocations.
Old Param mode `0x02` and retired operation numbers are not the current
[registry](../specs/representation/registry.md). This prototype-specific
behavior is part of the migration tracked by
[CG-001](conformance-gaps.md#cg-001), not a legacy decoder or alternate
current-schema profile.

| Surface | Current bounded behavior |
|---|---|
| Types | `bool`, `char16`, `int32`, `uint32`, `int64`, `uint64`, `float32`, `float64` values and scalar places; `void` or supported scalar results |
| Output calls | Module-local initialization outputs; Boolean results conditionally publish, non-Boolean/void results require normal-return initialization |
| General Bodies | One straight-line block or one three-block split on a scalar Boolean expression; a direct Boolean output call correlates destinations with its result, and a terminal true arm may have a false-path continuation; no general join or arbitrary CFG |
| Places | Output formals then local places, exact types, vacant activation and initialization; entry/inherited state counts as use ordinal zero |
| Operations | Existing admitted scalar values, comparisons, checked/wrapping integer add/subtract/multiply/negate, checked divide/remainder/shifts/conversions, loads, initialization and local calls; no general place escape |
| Failure | Existing Fail reason 1; no checked handling, ownership cleanup, exceptional output publication or general output forwarding |
| Native scalar addresses | Revision 7 readonly `NoEscape` inputs; revision 8 writable result-correlated outputs and the bounded three-block unsafe scope |
| Unsafe scope | Experimental opcode `0x2E` marks the one-block suffix or conditional entry and its terminal successors; not the accepted indexed region `0x06` |
| Native correlation | Exact producing call, result and destination versions; direct zero comparisons or verified one-parameter predicates for `NonZero`/signed `NonNegative` |
| Invalidation | Status/destination overwrite, another output call, or an unmodeled pointer write invalidates the bounded proof |

The separate unapproved Standard profile `trrm/1-draft-init-reference-bodies-1`
currently round-trips one-block scalar `ref<init T>` stores (including
multiple destinations), direct-Home DD and source-use-5 calls, plus a bounded
three-block Boolean provider/caller branch with true/live and false/vacant
state. Same-profile source MethodDef binding and re-emission are supported,
and the same-module path has FailFast native library execution coverage.
The distinct provisional
`trrm/1-draft-init-reference-bodies-general-predicate-graphs-1` composition
also emits, loads and re-emits these bounded Body forms against the
GeneralPredicateGraphsDraft schema while preserving Feature 10/1 default
OutputState rows. A general phase-3 `SubjectValue` Boolean-input selector
tree (including the supported Boolean operations and Conditional) also
survives same-profile provider binding and re-emission; native lowering does
not evaluate it. Both exact profiles also emit, load and source-free re-emit a
typed `ref<init ptr<int32>>` cell store with exact pointee identity and
Feature 1/2 closure. Imported pointer-cell providers bind through the
source-use-5 Home input, load and source-free re-emit in the same profile, and
reject a provider with a different pointer referent. Loaded same-module Bodies
execute in FailFast PE libraries under both identities, preserving the pointer
value without changing the pointee. Provider-linked native execution and AOT
remain open. The profile does not select the LayoutQueries
source/provider/query-Body profile. Open gates are phase-1/2 clause
persistence (including explicit/default phase-1 plans and phase-2 identity/
construction maps), source-derived phase-3 projection, other graph nodes,
complete remapping/provider dependencies, provider-linked execution/AOT,
Span, hidden preparation, general CFG joins, CheckedExceptions and failure
cleanup. Neither identity is a Development fallback.

The distinct provisional C1 and C1M1 identities select that same bounded
init-Body subset with exact schema routing; the C1 Feature catalog recognizes
GeneralPredicateGraphs and GenericMethodCalls together. GenericCall
producer/consumer support remains open. C1M1 accepts TypeDef flag `0x80` only
on class rows; the explicit type-root reference route now binds non-generic
class TypeDef Imports through exact providers and compares the complete field
TypeSpecs, declared Layouts and mutability facts without lowering class
objects. Type-only re-emission rebuilds the supported class/Import rows and
revalidates their provider closure. C1 remains unchanged. The exact C1M1
construction successor now parses and writes Construction `0003` I/P roles,
checks class-handle qualification and FieldDef guarantees, and routes
class-free emission; source capture validates checked class/member anchors.
Class TypeDef/member/MethodDef/Body publication, provider binding, independent
carrier verification, private local type dependencies, broader remapping/
replacement, interface/witness closure and runtime support remain open.

The native path requires candidate NativeBoundaryContracts and
RootlessNativeValues requirements. It admits initialized scalar backing and
matching typed pointer formals, including direct-import and role-4 vtable
calls. It does not admit fixed arrays, aggregate addresses, pointer memory
operations, general pointer locals, arbitrary CFGs or a general reference ABI.

Earlier supported revisions restrict referents to `int32` (1), add the other
scalar referents (2), non-Boolean results (3), Boolean publication (4),
output-call terminal splitting (5), no-output Boolean-call splitting (6),
and readonly native scalar addresses (7). Existing support for these drafts
does not require retaining them when the reviewed contract is implemented.

`TrRmScalarInitOutputBodyReaderTests` and related executable reader/compiler
cases exercise this Development experiment, not current normative bytes.
The accepted [output-state and dependency contracts](../specs/representation/contract-clauses.md)
own the design; the [gap inventory](conformance-gaps.md#cg-002)
owns remaining physical and compiler gates.
