# Language and library improvement review

**Proposal, not accepted specification.** This C#/.NET, Rust, and systems-
language synthesis adds no syntax/API promise or compiler authorization.
[Language](language.md), [lifetimes](lifetimes.md), [type families](type-families.md),
[conversions](conversions.md), and [libraries](libraries.md) remain authoritative;
status follows [implementation](implementation.md) and [roadmap](roadmap.md).

## Thesis

Prefer **explicit, composable contracts** over importing the CLR's universal
object conventions or reproducing each language's feature catalog. A small
number of checked mechanisms can cover many library gaps:

1. **Nominal capabilities and checked conditional witnesses** for equality,
   formatting, generic operators, collections, and user-defined participation.
2. **One representation/access/lifetime model** for arrays, views, shapes,
   opaque layout, and explicit ABI contracts.
3. **Declared compile-time and runtime expansion contracts** for literals,
   handlers/builders, and specialization.
4. **A deliberate effects/failure model** separating expected outcomes,
   exceptional failures, programmer defects, cancellation, and async ownership.

The target is predictable AOT/JIT and separately compiled semantics without
invisible boxing, copying, allocation, or lifetime extension. C#/.NET supplies
ergonomics, Rust foregrounds representation/borrowing/coherence/`Drop`, and
systems ABIs require explicit foreign contracts; none is copied wholesale.

## Current baseline and status vocabulary

| Label | Meaning in this review |
|---|---|
| **Accepted direction** | Already established by Terrum design documents; this review argues for preserving it, not reopening it. |
| **Refinement proposal** | A mechanism to make an existing direction more usable or precise; requires language/library design and contract rules. |
| **Open proposal** | A genuinely unsettled capability or policy; requires a decision before it can be treated as language behavior. |
| **Implemented subset** | Verified in the bounded compiler/runtime profile described by the implementation documents. It is not a claim that the general feature is complete. |

The reviewed implementation baseline, commit `57fa61d`, is bounded
syntax-11/native-19: compact-owner moves,
conditional `bool`/`out`, cleanup, managed classes, and compilation-local static
protocols. It does not establish general
interfaces/witnesses, arrays/views, async, proposed libraries, or specialization;
examples and `.api.terrum` files remain design material.

## 1. Make capabilities explicit and composable

### 1.1 Propose invariant mutable storage; consider read projections

**Proposal derived from accepted access rules, not a settled array contract:**
make mutable generic storage invariant. Treating `Array<Derived>` as
`Array<Base>` would permit storing a `Base` that is not a `Derived`. A read-only
view might support covariance only through a checked per-element subtype
projection. It must prove compatible physical representation or advertise
explicit per-read conversion cost, preserve backing/lifetime, and attenuate
writable access. This is read access, not graph immutability. Interface
conversion may box/allocate and must advertise cost. No reinterpretation,
materialization, or copy is implicit. The view may alias backing storage that
remains mutable through other aliases; it grants no write authority and does
not make the object graph immutable. Terrum has not committed to covariant views.

As contrast, .NET's reference-array covariance is not inherited by mutable
`Span<T>`: its array constructor rejects covariant arrays unless the runtime
array type is exactly `T[]` ([CoreLib `Span<T>`](https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Span.cs)).

Do not imply one all-purpose `Array<T>`: separate variable-length storage,
fixed-extent [`InlineArray<T, const nuint N>`](../library/application.api.terrum),
and rank-dynamic tensor views. An erased/root array must not bypass element
validity, storage, ownership, or lifetime. Shape metadata may be inspected, but
writable stores need checked element type/validity/ownership; do not invent
`default(T)` or scan uninitialized slots. Keep logical shape separate from
physical layout: strided views need not be contiguous, and SoA field access is
not `Span<Record>` without that layout. Check bounds, access, aliasing, and
metadata lifetime before mutation; zero-sized dimensions differ from rank-zero.
The CLR `Array` base is runtime-special, not an ordinary generic capability
([CoreLib `Array`](https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Array.cs)).
Managed graphs, unique buffers, and borrowed spans retain distinct ownership.

### 1.2 Propose against universal object defaults

**Proposal derived from opt-in capability and cost constraints, not an accepted
root-type decision:** avoid universal inheritance of `Equals`, `GetHashCode`,
`ToString`, boxing, or allocation by every value. Likewise, make behavioral
`IEquatable<T>` and `IFormattable` conformance explicit, not implicit for every
struct. This does not prohibit primitive-specific built-ins or compiler-
verified capabilities.

Keep compiler diagnostic/type metadata distinct from user-visible operations:
the compiler may need type identity for checking, diagnostics, or tools without
requiring each runtime value to inherit a root object or expose identity,
equality, hashing, or formatting.

Keep distinct operations distinct:

- value equality (and its operand/result types);
- key equality paired with a compatible hash;
- reference/identity equality where identity exists;
- invariant machine-readable formatting;
- human-facing display/debug formatting.

`IEqualityComparer<T>`/`IKeyComparer<T>`-style policies are preferable where a
collection needs a choice of equality semantics. Floating `==`, NaN-aware
numeric-key equality, bitwise identity, decimal cohort equality, and display
text must not collapse into one universal override. A useful formatting
protocol can be shared without promising that every value allocates a string.
There should be a debugger/tool representation, but it need not be a language
level `ToString` method.

This separation also exists in .NET: [`Object`](https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Object.cs)
supplies object defaults; [`IFormattable`](https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/IFormattable.cs)
is separate. Terrum may choose other defaults; this review recommends making
their costs and semantics explicit.

### 1.3 Keep generic and nongeneric APIs honest

**Refinement proposal.** Keep generic collections primary; do not duplicate
their API graph in an erased hierarchy. Where heterogeneity is necessary, use
a narrow dynamic adapter with explicit type identity, ownership, lifetime, and
dispatch cost.

Generic constraints should compose granular capabilities: request an operator
with explicit operand/result types, not one “number” interface that excludes
vectors, complex types, or different results. Operators alone confer neither
scalar/SIMD membership nor copy/storage permission. C# static-abstract interface
members are generic-math prior art, but current Terrum protocols prove only a
bounded slice ([C# interface members](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/interface#static-abstract-and-virtual-members)).

Bridge constraints through checked implications/witnesses, using the
declaration/version discipline of the conditional member-contract refinement
already recommended in [lifetimes](lifetimes.md), not a second `ISpecialSpan`
or unsafe downcast. That refinement alone does **not** confer behavioral
witnesses or family membership. Member refinement and conformance/witness
registration are distinct contracts, though both need declared proofs and
published versioning. Expose narrower facts only when declared and proven;
separate consumers retain the broad contract. Bodies and extension names do
not prove public representation. Rust's [coherence rules](https://doc.rust-lang.org/reference/items/implementations.html#trait-implementation-coherence)
illustrate the need for a conflict policy. Across loaded modules, a defining
producer or authorized registration owns each witness/family; reject competing
blanket/specific overlaps and publish versioned resolution. A constraint
establishes admissibility, not inference: `Parse<T>(text)` cannot infer `T`
from `IParsable<T>` alone ([C# constraints](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/where-generic-type-constraint)).
Whether a resolved expected-result target may infer `T` for such a call is a
separate Terrum rule that needs design; otherwise require explicit `T` or a
typed argument. A known typed argument can infer `T` before constructing a
contextual literal; `F(1)` still has no fallback type.

Separate enum underlying-storage capability from numeric semantics: ordinary
enums may hold unnamed values, closed unions can promise exhaustive cases, and
flags are explicit. .NET generic enum APIs use enum constraints and
underlying-type dispatch, not general numeric conformance
([CoreLib `Enum`](https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Enum.cs)).
Retain **sealed-by-default** concrete types; use interfaces/composition or
narrow explicit extension points, not implicit virtual overrides.

### 1.4 Separate homogeneous parameters from type packs

**Open proposal:** compiler-recognized type packs could avoid a hand-maintained
`Func` arity ladder, but must distinguish homogeneous from ordered
heterogeneous parameters and specify per-element binding/ownership/effects,
constraints, overload ambiguity, metadata, code size, and ABI. .NET has finite
`Func` arities (16 inputs) and recursive `Tuple<..., TRest>` shapes -- library
conventions, not a language-level pack ([`Func`](https://learn.microsoft.com/en-us/dotnet/api/system.func-17),
[`Tuple`](https://learn.microsoft.com/en-us/dotnet/api/system.tuple-8)). A
tuple's structural fields are not automatically a nominal record or transparent
wrapper. C# `params` accepts a homogeneous collection shape, not a heterogeneous
pack ([`params`](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/method-parameters#params-modifier)).
A finite function-type baseline can precede packs; avoid implicit argument
tuples or overload/ABI changes. Packs must not conflate bare
`Task<void>` (no result) with erased `TextSpan<void>`, nor discard a unique
`Task<T>` result and cleanup. A typed text projection checks recorded encoding
and preserves backing/lifetime dependencies; typed `AsSpan()` drops text
validity only, including for empty views.

## 2. Make representation and cost visible

### 2.1 Opaque layout and explicit ABI proposals

**Accepted direction:** layout/ABI promises need explicit checked contracts.
**Proposed default:** ordinary layout is opaque. **Open proposal:** a
transparent single-field wrapper could promise a validated representation, not
permission to erase validity, ownership, mutability, cleanup, niches, or ABI.
It must preserve carrier ABI and narrower payload validity/ownership; equal
bytes never permit arbitrary reinterpretation or automatically propagate every
niche optimization. Construction and imports validate the wrapper's narrower
invariant; layout equality never waives that check.
Sequential/explicit/union layouts are opt-in per ABI and constrain servicing;
interop separately declares calling convention, alignment, ownership, encoding,
callbacks, and exception containment. .NET distinguishes unmanaged from
blittable (`bool`/`char` differ) and native marshalling from managed layout
([interop guidance](https://learn.microsoft.com/en-us/dotnet/standard/native-interop/best-practices#blittable-types)).
Rust [`repr(C)`/`repr(transparent)`](https://doc.rust-lang.org/reference/type-layout.html)
specify representation, not Terrum validity/ownership: `repr(C)` does not
recursively change nested layout or give a wrapper its field's call ABI;
transparent requires one nontrivial
field. Neither bypasses construction/import validation or allows blind
reinterpretation.

Request required storage capabilities (`[Sized]`, managed-storable, copyable)
separately from behavioral interfaces; sizeless values cannot silently become
fields or array elements.

### 2.2 Generic operations should not imply generic representation

**Refinement proposal.** Generic algorithms use declared capabilities without
boxing; constraints do not license representation erasure or value duplication.
Constraints, static witnesses, and representation families solve distinct
problems; avoid one universal mechanism.

Keep vector/tensor concepts separate:

- scalar arithmetic protocols;
- fixed-width SIMD family membership and its lane-mask association;
- hardware-scalable, potentially sizeless vectors and their target ABI;
- logical multidimensional shapes and their backing strides;
- domain vectors such as geometric points, whose operations need not be
  arithmetic on a scalar number.

SIMD comparisons return a declared mask; whole-vector equality may return
`bool`. Tails, inactive lanes, alignment, overlap, reduction order, CPU/OS
selection, scalable ABI, and suspension restrictions are correctness contracts.
Capabilities apply to the complete qualified form: `[Copy]` on a payload does
not make `own T` or a borrowed `ref<T>` copyable. Access only attenuates:
`mut Span<T>` may yield a read view, never a writable upgrade from one.

### 2.3 Discards need one meaning

**Refinement proposal.** Choose a single rule for discards (`_`) across
deconstruction, assignment, pattern matching, and lambda parameters. It should
mean “intentionally do not bind/use this value,” not inconsistently mean
“fresh local” in one position and “wildcard” in another. Define binding versus
wildcard, RHS evaluation/effects, temporary and unique cleanup, and the vacancy
of failed conditional `out`; failure cleans partial owners and never creates
`default(T)`. Discarding a task result is not detaching or
supervising work. Tooling/diagnostics must report these meanings consistently.
Keep `_` distinct from an uninitialized destination or omitted optional field.
Audit prior examples before freezing a rule; conventions differ by context.

## 3. Make compile-time participation and code generation declared

### 3.1 One checked participation model, multiple capabilities

**Refinement proposal.** User-defined types should be able to participate in
const-evaluation, typed literals, checked operators, compound assignment, and
generic constraints through declarations that can be verified and published.
Avoid compiler magic keyed to names or a handful of intrinsic types, but do not
allow arbitrary user callbacks to execute inside the compiler without
determinism and resource bounds. `[Pure]` alone does not imply const-evaluability.
Registered literals/const-eval must be
target-format and culture-independent, exclude ambient path/I/O, enforce
resource budgets, preserve source error ranges, and validate through the normal
constructor or witness contract. A baked consumer cannot change retroactively
when its producer is replaced; require rebuild or reject mismatched
compiler/rule/module/value-ABI dependencies, not assume the same body remains
([.NET `const`](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/const)).

The selected literal target may be written or established by an unambiguous
context; literal construction still has no fallback type. The model should
distinguish:

- **literal parsing/construction**: validate a token against its resolved
  contextual target or explicit type tag, with no fallback/intermediate type;
- **const evaluation**: a restricted interpreter evaluates approved pure,
  deterministic operations over validated constant values;
- **checked runtime operators**: declared behavior, effects, and failure policy
  apply in ordinary code;
- **compound assignment**: a single-place operation with defined evaluation
  count, aliasing, overflow, and whether it mutates in place or returns a value.

These can share capability declarations without sharing one execution model.
Const-eval must not bypass validity, execute I/O, or assume target behavior
([Rust const evaluation](https://doc.rust-lang.org/reference/const_eval.html)).
Compound assignment should not lower to an
unintended read/copy/write sequence for affine or large values. A declaration
that participates in checked `+` does not automatically claim support for
`+=`, saturating arithmetic, wrapping arithmetic, or constant evaluation.

### 3.2 First-class handlers for target-directed construction

**Open proposal.** Interpolation, collection expressions, and future typed
literal builders need a principled handler/build protocol. Treat them as
explicit target-directed expansions that specify evaluation order, captured
values, ownership transfer, partial failure, cleanup, and the result's
representation. A handler can consume typed segments directly without
allocating a temporary string; a builder can accept elements once without
materializing an intermediate collection.

The language should require a registered handler contract, not select any
method with a convenient name. Preserve the existing interpolation direction:
evaluate every hole exactly once, left-to-right, into typed captures **before
draining any segment**. C#'s [handler protocol](https://learn.microsoft.com/en-us/dotnet/csharp/advanced-topics/performance/interpolated-string-handler)
can skip later holes after refusal; do not silently adopt that behavior. If
lazy/conditional evaluation is useful, make it a separate construct with its
own effects and ordering contract. Clean partially accepted unique values on
refusal/failure, including remaining captured owners never accepted by the
handler, exactly once; borrowed captures stay valid through drain. A scalar
writer preflights room for the full encoding. Refusal stops later writes, not
pre-evaluated hole effects or prior stream output. Builders publish only after
success; refusal is not an implicit async continuation.

### 3.3 Make specialization a declaration, not body-divination

**Open proposal.** First-class specialization can improve generic hot paths and
enable target-specific implementations, but specialization must remain an
implementation choice with declared applicability and semantic obligations.
An explicit specialization may optimize a verified generic definition; it
must not silently redefine its source contract or make AOT depend on a JIT.
The generic implementation remains a valid fallback unless the declaration
explicitly requires a target capability. This is a machine-code selection, not
a semantic overload or a way to select a different algorithm contract.

Validate that the specialized implementation preserves observable behavior,
including checked failure/cleanup, effects, ownership, aliasing, access,
published contracts, and floating-point guarantees (including FMA/reordering
policy). Rust still treats specialization as unstable
([Rust Unstable Book](https://doc.rust-lang.org/unstable-book/language-features/specialization.html)),
which reinforces the coherence and contract burden rather than supplying a
design to copy. A selected type or current body cannot legalize an invalid
source escape or strengthen a public guarantee. Define code-size, versioning,
and fallback rules before syntax; start with explicit target/library
registrations. This is not a ban on semantic algorithm variants: e.g. strict
and fast reductions may be distinct named APIs with different rounding,
reordering, and effect contracts, not same-contract specialization.

## 4. Make failures, functions, and async lifetimes explicit

### 4.1 Distinguish expected outcomes from exceptional failures

**Accepted direction.** Expected alternatives (invalid user input, absence,
domain status) should normally use a conditional output, optional/response
value, or named status. Exceptions represent exceptional failure and propagate;
catch-based recovery remains unsafe and cannot prove invariants survived.
`require` is a checked requirement, not an assertion that disappears in release.

**Refinement proposal:** document a taxonomy that does not conflate *cause*,
*recoverability*, and *cleanup*. At minimum distinguish:

1. **Source/compiler diagnostics**: invalid source, stable codes/ranges, and unknown-versus-proven facts.
2. **Expected domain result**: normal status/optional branch, not an exception or compiler bug.
3. **Exceptional operational failure**: declared I/O/allocation/runtime failure propagated through cleanup.
4. **Programmer/invariant defect**: invalid state/precondition, not misclassified
   as bad external input.
5. **Host/process failure**: ABI escape, fail-fast, or missing runtime service; not a source-level result.

This taxonomy is a proposal for APIs and diagnostics, not a promise that all
such failures are catchable or that all causes have stable exception types.
.NET's `ArgumentException` derives from `SystemException`, another reason not
to treat exception inheritance as a developer-versus-environment taxonomy
([API reference](https://learn.microsoft.com/en-us/dotnet/api/system.systemexception?view=net-10.0)).
No-throw, no-return, cleanup, and expected-result contracts remain independent.
An invariant defect may surface as a checked `require` failure; classification
does not make it irrecoverable. The accepted unsafe-catch rule applies to
environmental exceptions too: external cause does not prove object/resource
integrity. Recovery needs separate unsafe authorization and proof. Cancellation
is a separate control/lifetime signal, not automatically an expected-result
branch or a synonym for either an environmental error or developer defect; it
does not prove that a callee, device, or foreign operation has stopped using
borrowed storage. Validate external input at a checked boundary; reject static
invariant violations rather than relying on catch-and-continue.

### 4.2 Prefer single-target callables; make event aggregation explicit

**Open proposal.** A general function value should have an explicit signature,
capture/lifetime contract, and single-target invocation. Repeat calls are valid
only while captures remain valid; consuming a capture needs explicit one-shot
semantics or reconstruction. Multicast is
useful for events but should not be an accidental universal delegate semantic:
define a separate event/subscription abstraction with order, exception
aggregation, reentrancy, mutation-during-dispatch, and unsubscribe/disposal
rules. A closure's captures must preserve ownership and borrowing rules; copying
a function value must not silently duplicate a unique capture.

C# multicast delegates provide one defined policy: ordered invocation, stop
when a handler throws, and return the last handler's result. That behavior is
appropriate only when explicitly chosen for an event-like abstraction
([Using Delegates](https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/delegates/using-delegates)).

Until pack ABI exists, keep function arity explicit; reserve multicast for a
declared event adapter, not ordinary calls.

### 4.3 Never let async work disappear

**Accepted direction plus refinement.** Async APIs return a task-like completion
value, including no-result `Task<void>`; `async void` has no safe default place
in ordinary library code because callers cannot await completion, observe
failure, or structure cleanup. For intentional detached work, use a deliberate
supervision/detach adapter with an explicit error sink, cancellation policy,
lifetime/affinity, and shutdown/join story -- not an unobserved `void` task.

In .NET, `async void` posts exceptions to the captured context/thread pool
rather than faulting a `Task`; callback compatibility needs an explicit adapter
([`AsyncVoidMethodBuilder`](https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Runtime/CompilerServices/AsyncVoidMethodBuilder.cs);
[`AsyncTaskMethodBuilder`](https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Runtime/CompilerServices/AsyncTaskMethodBuilder.cs)).

Tasks do not extend borrows: declare captures through actual completion, since
cancellation is not proof that external work has stopped using a buffer. Specify
copy/borrow/ownership and cleanup; keep `Task<void>` distinct from a result-
erasing `Task<T>`. Scheduler, affinity, start, and cancellation policies remain
open in the language design.

## Coverage and priority

| Concern raised | Shared response in this review | Status |
|---|---|---|
| Array variance | Propose invariant mutable storage; possible read-only covariance only through a checked projection | Proposed refinement from accepted access rules; open |
| Universal object equality/hash/format defaults | Propose explicit equality/hash/format capabilities; keep compiler diagnostic metadata separate | Proposed refinement from opt-in capability and cost constraints |
| Generic/nongeneric inconsistency | Generic primary APIs; explicit narrow dynamic adapter, not a duplicate erased hierarchy | Proposal |
| Multicast delegates | Single-target callables; explicit event/subscription aggregate | Open |
| `async void` | Return completion; detached work needs a supervisor/error contract | Accepted direction / refinement |
| Developer errors vs external failures | Separate diagnostics, expected outcomes, operational exceptions, invariant defects, host failures | Proposal |
| Cancellation | Separate control/lifetime signal; not evidence of completion or invariant safety | Refinement |
| Unspecified layout / transparent wrappers | Propose opaque defaults and validated transparent wrappers, preserving explicit ABI contracts | Refinement / open representation proposal |
| Arrays/enums lacking generic capabilities | Request checked nominal capabilities; don't infer behavior from representation or special-case types | Refinement |
| Sealed-by-default | Preserve; interfaces/composition, explicitly justified extension points | Accepted direction |
| Variadic `Func`/`Tuple` | Type-pack design distinct from homogeneous generics, with ABI/code-size limits | Open |
| Multidimensional arrays, SoA/AoS | Shape and layout separated; checked strided views and honest projections | Proposal; detailed sketch exists |
| Discards | One evaluation/binding/cleanup rule across syntax contexts | Refinement |
| User participation in consteval/literals/operators/compound ops | Shared checked capability declarations; separate runtime and constant evaluators | Open |
| First-class handlers/builders | Registered expansions; interpolation holes captured once, left-to-right, before draining; cleanup/publication rules | Open |
| First-class specialization | Declared optimization with contract-preserving generic fallback | Open |
| Generic constraint bridging | Composable checked witnesses and declaration-published refinements | Refinement |

Suggested sequencing: first verify witnesses, views, async lifetimes, and
producer metadata across AOT/JIT/module replacement; next enable bounded,
versioned opt-in literal/handler evaluation. Defer packs and explicit
specialization until workload/ABI/code-size evidence. This is not a demand to
add every proposal to the current roadmap.

These choices trade implicit convenience for stronger contracts. Keep the cost
manageable with checked derivation of ordinary equality/formatting witnesses,
contextual inference where unambiguous, and useful library defaults -- not
universal inherited behavior. Static witnesses favor optimization but can grow
code; explicit dynamic adapters favor code sharing but add dispatch and storage
contracts. Handler registration is narrower than arbitrary macros, while packs
and overlapping specialization add inference and coherence complexity that
ordinary typed builders do not need.

## Illustrative contract sketches (not syntax)

These contract expansions are **pseudocode, not grammar or promised APIs**:

**Constraint bridging.** A caller needs evidence for the particular operation,
not a cast from a weak interface to a stronger one.

```text
Forward<T, E>(source: read loan T)
    requires witness T : IContiguousSequence<E>
    returns source.AsSpan()

Default published result dependency: source receiver.
Declared conditional rule: if T's AsSpan witness has backing-only dependency,
Forward's result depends on that backing instead.
Caller proves the condition -> use the published refinement.
Caller cannot prove it -> retain the broad receiver dependency.
```

Family membership and behavioral witnesses remain separate evidence. Neither
is created merely by this member-lifetime refinement.

**Target-directed construction.** The target chooses a registered builder;
the builder does not choose the meaning or evaluation order of source code.

```text
value: int32 = 42
own Text result = $"item={value}"

resolve own Text's builder
evaluate/capture holes once, left-to-right
drain typed literal/value segments into private builder state
validate/freeze -> publish result
failure -> clean private state and all still-owned captures exactly once
```

Never-accepted captures still need cleanup; loans stay valid through draining.
A user-defined numeric literal similarly receives its resolved target or tag,
not an already rounded `float64`. Registering a literal constructor does not
make every operation on the type eligible for compile-time evaluation.

**Shape versus storage.** An indexing contract can describe either layout
without promising the wrong contiguous view.

```text
shape = (m, n); elementStrides = (1, m)
element(i, j) = backing[base + i + m*j]
SoA field projection: points.x[i] = xBuffer[i]
AoS record projection: points[i] = recordBuffer[i]
```

Construction validates dimensions, signed-stride representability, the checked
element count, and reachable minimum/maximum backing offsets. Empty shapes skip
`length - 1` address arithmetic and dereference, not dimension validation or
backing/metadata dependencies. Indexing checks each coordinate and arithmetic.
A contiguous `xBuffer` may lend an `x` span; it cannot lend `Span<Point>`.

**Heterogeneous forwarding.** Pack expansion preserves each argument's mode;
it is not conversion to an untyped argument array.

```text
signature pack = [(read loan A), (consume B), (value C)]
call arguments = [ref a, move b, c]
requires complete-form C : [Copy]
expand in source order, evaluating each argument once
invoke the checked signature without boxing or a universal tuple layout
```

On partial failure each side cleans only owners it still owns. Forwarding a
consuming argument is itself a transfer, not another copy; loans cannot outlive
the permitted call/suspension. Per-element effects, inference, and ABI remain
part of the proposed pack contract.

**Supervised start.** Establish ownership before an operation can run, without
assuming that an ordinary async call is cold.

```text
factory = operation factory with explicit transferred/borrowed captures
completion: Task<void> = scope.Start(factory)
await completion
scope exit: join any outstanding operation before releasing its captures
```

`Start` is hypothetical: the scope accepts responsibility before invoking the
factory, and rejection cleans transferred captures. Exceptional scope exit
also needs the join/cleanup contract. Cancellation requests do not discharge
that obligation. Longer-lived detached work must instead transfer eligible
captures to a supervisor with explicit error and shutdown handling.

Spelling, proof transport, handler registration, and scope/start policy remain
open. Executable claims require producer/witness, lifetime, and AOT/JIT/module
checks.

## Evidence and limits

See [language](language.md), [lifetimes](lifetimes.md),
[type families](type-families.md), [conversions](conversions.md),
[libraries](libraries.md), [implementation](implementation.md),
[roadmap](roadmap.md), [`library`](../library), and [references](references.md).
External features are comparisons; performance claims require measurement.
Array/function syntax, async supervision, const-eval, handlers, type-pack ABI,
specialization, and production APIs remain open.
