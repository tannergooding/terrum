# Standard native fixed-array source contract

**Status: unapproved; provisionally actionable only under the user's explicit
missing-contract workflow.** This document recommends source-layer choices;
it approves no syntax, schema, API or runtime policy and establishes no
compiler conformance. [Proposal index](README.md).

The governing authorization is: “At a genuine missing contract, prepare the
smallest concrete decision needed as a new proposal doc using gpt-6.1-sol at
max reasoning, then continue with it being treated as a provisional
approval/direction.” Its boundary remains: “Implement accepted contracts as
written. This approval does not approve unassigned syntax, schemas, APIs or
runtime policy.” Subsequent work may use this recommendation provisionally
under that instruction; writing it is not normative adoption.

## Decision and preserved baseline

Recommend **structural type syntax `[E; N]`, a required `uint64` extent,
target-typed exact-cardinality construction, and intrinsic element places**.
The minimum executable scope is initialized local inline storage, nested
arrays and real array fields of independently admitted local Copy aggregates.
This supplies the missing source-to-A2 mapping, not completion of the wider
source/runtime, provider, source-free, CLI or JIT/AOT adoption task.

The [specification index](../specs/README.md) reaches the accepted owners.
Keep the accepted baseline, A1/A2 provisional direction and new source
candidates distinct:

| Authority | Preserved rule or missing decision |
|---|---|
| Accepted [type/constant grammar](../specs/grammar-spec.md#32-types-parameters-and-arguments), [collection grammar](../specs/grammar-spec.md#34-expressions-and-precedence) and [construction handlers](../specs/language/construction-handlers.md#target-owned-builder-schemas) | `InlineArray<T, N>` demonstrates constant-argument syntax, not a built-in registration. Collections are target-directed; no default array, numeric element width or similarly named builder is selected. The structural family and its intrinsic construction exception below are new candidates. |
| Accepted [complete types](../specs/language/types-and-normalization.md#complete-type-terms), [constant domains](../specs/language/constant-generics.md), [initialization](../specs/language/initialization-and-flow.md#states-reads-and-initialization) and [evaluation](../specs/language/expressions-and-statements.md#evaluation-and-expression-boundaries) | Preserve nominal origins, complete arguments and qualifier positions; vacancy is not a default value. Capture operands once in source order and validate an assignment place before its RHS. |
| Unapproved [A1 structural design](standard-trrm-fixed-array-identity-shape.md#complete-identity-and-exact-layout-row), composed only through unapproved [A2 Copy evidence](standard-trrm-rootless-copy-type-evidence.md) | A2 selects `(FixedInlineArray, complete normalized E, uint64 N)`, not a nominal Core type. Retain exact schema `trrm/1-draft-foreign-invocation-storage-fixed-arrays-2`, TypeSpec `0x11` `(E,u64 N)`, section `0x0012`, Feature `20/1`, positive TypeDef `0x20` Copy evidence with `2/1`, and PlaceSelection `targetRefKind=3`. No A1 alias/fallback or wire amendment. |
| Accepted [layout](../specs/data-layout-and-abi.md), [Windows placement/access](../specs/targets/windows-x64-data-layout.md#5-aggregate-placement-empty-storage-and-stride), [native obligations](../specs/native.md#native-declaration-roles), plus provisional [S address formation](standard-native-storage-address-formation.md#source-region-and-failure-semantics) | Reuse completed layout, checked target-nuint arithmetic, actual alignment, initialized stable backing and caller-unsafety. No array decay, marshalling, rooting, pinning, implicit heap allocation or new whole-array foreign mapping. |

### Spelling alternatives

These alternatives and the recommendation are **unapproved**.

| Alternative | Assessment |
|---|---|
| Minimal amendment retaining `InlineArray<E, N>` | Coherent only if explicitly selected as a **nonnominal language constructor** or an exact checked transparent alias, with type/`uint64` argument roles and lookup/reservation/formation rules. Merely recognizing the identifier is insufficient. Keeping it as a nominal Core struct instead requires its actual provider identity, generic arguments/domains, containing storage and API contracts; that nominal type cannot become A2's structural recipe. Neither registration is currently selected. |
| **Type-position `[E; N]` — recommended** | One TypeCore alternative using existing delimiters, with explicit complete-element and extent slots. It needs no identifier/Core registration, nominal erasure, generic export codec or user indexer. The semicolon distinguishes a type recipe from `[e0, e1]` construction, including in type/query/generic-argument contexts. |

The unapproved [nominal Core sketch](core-library-reference.md#general-span-and-inline-storage)
remains separate. An authored nominal wrapper can contain this structural
storage through an independently admitted real field; its identity is not
the field's identity. A genuine transparent source alias may normalize to
the structural type under ordinary alias rules. No automatic conversion
between a nominal `InlineArray` and `[E; N]` is selected.

## Proposed type formation

**Unapproved grammar amendment:** add `FixedArrayType` to `TypeCore` in
[§3.2](../specs/grammar-spec.md#32-types-parameters-and-arguments):

```ebnf
FixedArrayType = "[", Type, ";", ConstantExpression, "]" ;
```

`Type`, `ConstantExpression`, prefix qualification and existing `*`/`?`
suffix rules otherwise remain unchanged. Interior trivia is ordinary trivia;
the type's brackets are not a receiver-attached index operation. Empty
extent slots, `[E]`, `E[]` and inferred `_` extents are not this type form.
This adds no allocating `new`-array syntax or intrinsic constructor call.
An outer `write`, `gc` or type-use `init` does not qualify the inline family;
declaration `init` and `mut` retain their distinct storage meanings.
`[write ptr<int32>; 2]` preserves the element pointer's access, whereas
`write [ptr<int32>; 2]` rejects. An outer pointer/optional needs its own
independently admitted representation.

### Extent and identity

The following source choices are **unapproved**:

1. Establish **exact `uint64` context** before checking the extent. Accept
   an integer literal, an accessible constant name or an eligible required
   constant expression of that complete domain. The brackets already delimit
   the expression; generic arguments retain their separate grouping rules.
   A typed signed/native integer, Boolean, character, enum, nominal wrapper,
   floating value or runtime value is not implicitly converted/extracted.
   An explicitly authored conversion to `uint64` must satisfy its ordinary
   conversion and constant-execution contract.
2. Reuse [integer literal construction](../specs/language/literal-construction.md#integers-and-based-bit-patterns):
   radix/separators/sign are meaningful parser input; negative nonzero or
   values above `0xFFFF_FFFF_FFFF_FFFF` reject, while integer `-0` is zero.
   Required expression evaluation retains its resolved
   [checked/wrapping arithmetic and staging](../specs/language/proof-and-constant-evaluation.md#staged-constant-evaluation);
   there is no extent-specific arithmetic policy, implicit wrapping parser,
   saturation or runtime fallback. A checked overflow or failed/exhausted
   required evaluation diagnoses; an explicitly selected wrapping operation
   retains its ordinary result.
3. Resolve and [normalize E](../specs/language/types-and-normalization.md#forward-substitution-and-normalization)
   under the actual using Module, then check the complete closed E/N recipe.
   Alias/expression spelling adds no identity; nominal origins, ordered
   arguments, pointer depth, function-pointer contracts and every nested
   qualifier remain. Equal layout or equal counts do not erase a constructor
   or a nominal constant domain. N is A2's prescribed uint64 quantity, not
   an arbitrary constant-generic domain stripped to convenient bits.
4. A2 publication requires the completed `u64 N`, not an unevaluated recipe
   or target-width approximation. Open/formal/existential E, symbolic N and
   general exported array-generic recipes are outside this minimum profile.
   A closed constructed E still needs its independently admitted formation,
   substitution/witness and physical records; default generic Copy guarantees
   do not prove rootlessness. No rootless constraint or generic codec is added.
   The separate unapproved
   [A3 constructed-element proposal](standard-trrm-closed-constructed-array-elements.md)
   recommends only bounded type-only generic storage under its exact schema
   identity; it leaves A2 unchanged and does not make an A2 artifact generic
   by naming an additional proposal.

The [A2 complete-element check](standard-trrm-rootless-copy-type-evidence.md#unapproved-complete-element-checking-rule)
already fixes Copy/rootless admission: scalars/unit, admitted raw data/code
pointer **cells**, exact positive-Copy nominal storage children and nested
arrays can participate with complete target-valid Blittable layout.
Pointee storage is not an inline child. Copy with a class/box/ref/owner/view
child, unique/Cleanup elements, missing evidence or inline cycles fail this
bounded route, including at N=0. Do not infer Copy from layout or Cleanup
absence, or use the source prototype's element whitelist as the contract.

Keep A1's derived `A=max(1,a)`, `S=checkedRoundUp(s,A)`,
`B=checked(N*S)` and inherited explicit alignment R unchanged. All layout,
nested-offset and access arithmetic fits the selected target's `nuint`;
no `int`, u32 or 4096-byte source cap follows. Representability, codec support
and resource exhaustion remain distinct. Unit and a distinct admitted
Copy Transparent-over-void E retain their own identities/layouts, with array
`A=1,S=B=0`. N=0 still validates E and geometry. No payload/anchor byte or
padding initialization is introduced. Nested arrays preserve each dimension;
`[[int32; 2]; 3]` has three elements of type `[int32; 2]`, not a flattened
six-element identity.

Existing [queries](../specs/language/registered-queries.md#selected-query-forms)
apply only under their own admission: `countof(Type)` uses N, `sizeof` uses B
and `alignof` uses A. Their nonnegative `nint` result bound is unchanged,
even for valid zero-extent types with a larger N. No live `Length`/Count
witness or `AsSpan` member is synthesized.

## Proposed initialization and publication

**Unapproved construction amendment:** a collection expression with an
established exact `[E; N]` target uses this family-owned intrinsic instead
of a nominal CollectionBuilder. Amend the builder-only collection paragraph
in [§3.4](../specs/grammar-spec.md#34-expressions-and-precedence) and cross-link
the [construction owner](../specs/language/construction-handlers.md).
Other targets retain their existing registration, capture, refusal and
completion contracts; no builder is discovered by name or generated for a
nominal array.

* Require **exactly N ordinary element expressions**, checked before runtime
  evaluation. Preserve the existing optional trailing comma; missing elements,
  underfill/overfill and every spread reject this intrinsic. A missing target
  does not infer E/N from the list (`var a = []` has no array type).
* Contextually check each expression as exact complete E under ordinary
  conversion, Copy, validity and dependency rules. Evaluate and acquire typed
  captures once, left-to-right, before writing the destination. A nested
  initializer completes its own capture/construction when reached; do not
  reorder it breadth-first or replay expressions.
* After all captures/checks succeed, install elements in increasing index
  order under the whole destination's actual initialization/replacement
  authority, then publish a complete array. Private construction progress is
  not an escaping partial array, per-element bitmap protocol or readable
  prefix. If capture/construction fails, publish no array and settle actual
  private acquisitions under ordinary unwinding; preserve prior external
  effects. Failed RHS construction does not retire or install over the old
  destination. This is not rollback or a whole-array atomic snapshot.
* `[]` completes **only N=0**. `init [E; 0] a;` is still vacant until an
  explicit permitted initialization such as `a = [];`. N>0 requires N
  expressions even when E has zero payload. Zero size/count never makes
  vacant storage live or makes an omitted initializer/`default` zero storage.
* Ordinary whole-array Copy/assignment uses an already complete **exact**
  array value and the existing transition/retirement rules. Copies preserve
  per-element pointer/backing/code-origin facts; they neither copy pointees
  nor initialize padding. This runtime construction rule selects no frozen
  aggregate/default/static-data value codec.

Proposed function-body fragments, **not currently runnable or A2-qualified**:

```terrum
const uint64 N = 2;
mut [int32; N] values = [int32(4), int32(5)];
values[int32(1)] = int32(6);
unsafe {
    write ptr<int32> selected = &values[int32(1)];
    *selected = int32(7);
}

init [void; 0] empty;
empty = [];
```

The mutable binding authorizes element replacement; pointer write authority
does not come from E or the type spelling. The empty binding's transition
is logical initialization, not a memory clear.

## Declaration contexts and indexed use

The source candidate is an inline Copy family, not a new declaration kind.
Complete type use always requires the enclosing context's existing contracts.
The minimum selected executable slice and its **separate** boundaries are:

| Context | Source/selected-A2 boundary |
|---|---|
| Locals | Required: ordinary initialized and `init`/`init mut` declarations, whole initialization/copy, intrinsic reads/replacements and eligible references/addresses. A role-3 Home begins vacant; actual Body operations establish completeness. |
| Fields | Required inside independently admitted local Copy fixed-layout aggregates, retaining the real FieldDef, path, offset, mutability and containing authority. Nested array fields/elements use local-rooted captured subplaces. Do not fabricate N FieldDefs or give an array a nominal TypeDef. Other containing ownership/managed representations need their own admitted records and execution. |
| Globals/static fields | Existing [load-scoped initialization](../specs/grammar-spec.md#static-initialization-and-load-scoped-storage) and [failure/publication](../specs/runtime/standard-failures.md#5-static-and-lazy-initialization-failure) remain controlling. No global/static array construction transport or nonlocal A2 element/address base is selected here; these routes remain independent adoption prerequisites when required. No prologue/RVA zeroing or new retry policy supplies them. |
| Parameters/results | Preserve exact array identity and complete-value Copy/initialization through any independently admitted logical signature/call crossing. No decay or caller-storage alias from by-value passing. A2's intrinsic base is not a parameter Home, checked-reference value or ABI temporary. An addressable local copy requires an actual lawful materialization with its own alignment/state, not relabeling that incoming carrier. Whole-array call/result and ref-parameter operation support are separate gates, not a new foreign ABI. |
| Constants/defaults, views and generic APIs | Ordinary eligibility alone supplies no fixed-array frozen codec, Span conversion, iteration/indexer API, symbolic type export or generic recipe support. Do not treat required missing carriers as a completed route. |

### Places, addresses and failures

**Unapproved source selection:** `a[i]` on a structural array **place** is
an intrinsic element place, not an authored getter/setter or a value-to-place
conversion. Select one direct integer index; an otherwise contextual numeric
index receives `int32` context, while a typed signed/unsigned fixed integer
or `nint`/`nuint` retains its original width/sign, including int128/uint128.
Boolean/character/enum/optional/pointer indices and implicit user conversion
reject. This minimum does not register multi-index, from-end, slice, optional
indexing or iteration consumers; their accepted grammar/semantics remain
conditional on independently admitted family contracts.

Reuse [A1's captured element semantics](standard-trrm-fixed-array-identity-shape.md#bound-local-element-selection-and-fs-consumption)
and the accepted [place rules](../specs/il-instruction-set.md#fields-elements-and-ranges):
evaluate the base, then i once; check original numeric `0 <= i < N` before
narrowing or multiplying. Retain the immediate parent, original index,
`N-i` suffix, exact E, actual alignment, storage generation and all authority/
backing dependencies. Rebinding does not retarget the selection; nested
suffixes do not concatenate outer arrays or adjacent fields.

Reads, ordinary references, replacement and S addresses require the whole
array complete in this profile. Element stores do not initialize a vacant
source-visible array one cell at a time; intrinsic private construction uses its
actual whole-initialization authority. Fixed declarations cannot replace live
elements; `mut`/`init mut` and every containing path must authorize a write.
Capture/check a replacement or compound-update target before its RHS,
then use that same place; preserve ordinary RHS failure and alias invalidation.

`ref a[i]` follows accepted [reference formation](../specs/language/references.md#formation):
eligible addressable live storage, exact E, existing authority and bounded
backing, with no pin or conversion to a raw pointer. A unit element is not
borrowable payload. A zero-sized nominal element is not unit; its actual
reference/target support must be established, not invented by an anchor.

`&a[i]` and `&a` require the existing explicit
[unsafe scope](../specs/grammar-spec.md#53-caller-unsafe-declarations-and-explicit-unsafe-scopes)
and [S](standard-native-storage-address-formation.md#source-region-and-failure-semantics):
generic formation requests Q=1; an element pointer retains the full known
suffix, while `&a` points to one exact **array object**, not E. A pointer-cell
element yields a pointer to that cell at the correct depth. S rejects vacant/
partial backing, underalignment, unauthorized write and zero extent,
including N=0 or positive N with zero-size E. Containing padding is not backing.
Direct NativeParameter address adaptation requests `max(1, declaredMinimum)`
at that argument's Stage position, checking Q against the immediate suffix;
separately formed pointer values do not acquire a retroactive larger request.
Retain [known pointer-backing obligations](native-parameter-pointer-backing.md)
and complete F invocation/provider facts. Raw calls remain caller-unsafe;
an unsafe declaration does not make its body an unsafe scope.

Static type/extent/cardinality/layout errors diagnose before affected use.
S's statically impossible address requests retain their diagnostic rule.
Dynamic index/suffix failure uses the existing exact
[Core IndexOutOfRangeException](../specs/runtime/standard-failures.md#3-checked-index-access)
under the independently admitted checked-failure runtime or its admitted
terminal policy. Required constant/static-proof handling remains separate.
Failed selection publishes no element place/value/reference; failed address
formation publishes no pointer and leaves its result Home vacant. Neither
failed operation touches an element, evaluates a later RHS/argument or makes
a native call. Preserve already completed staging, including a previously
successful selection, and propagate actual earlier failures unchanged.
No new exception, handler transport, fallback or runtime policy is selected.

## Integration obligations and acceptance

On approval, add the TypeCore production in grammar §3.2 and the structural
family term in complete types; amend grammar §3.4/construction handlers'
builder-only collection rule; add the intrinsic source-place and whole-array
state rules to expressions/initialization, with reference cross-references.
A1/A2 already own the physical identity/layout/place/Copy composition.
Provisional implementation changes neither their wire nor normative owners.

Carry complete E/N and the using Module through binding, shared traversal/
rewriting, initialization/loan/provenance analysis, mandatory verification,
selected Standard production/consumption and native lowering. The
[binding relation](../specs/representation/binding-and-replacement.md#1-scope-and-selected-identities)
resolves nominal provider identity before E/N comparison. A2 requires original
Copy-positive expectations **and actual selected provider evidence** for every
stored nominal child, transitively; an imported stub is a proxy, not a new E.
Retain all replacement/layout/code-lifetime obligations and invalidate affected
derived data. Remap Module/TypeSpec/TypeDef/Import/profile/layout rows, field
paths, Homes and Body maps/records/offsets/source data, cloning shared recipes
when using contexts differ. Counts/geometry/Copy bits are not references.

Persist and reconstruct the actual ordered captures, typed initialization/
stores, complete-state transitions, checks and original-storage accesses.
Neither a layout row nor S's scalar R14 store initializes an array.
Reuse genuinely admitted typed operations; richer scalar/aggregate constants,
call/field/reference/flow carriers still need their own exact support.
Do not change A2's value catalog, infer an initializer from a name/shape or
recognize one application's statement sequence to repair a missing carrier.
Apply [producer truth and consumer checks](../specs/representation/reader-trust.md):
structural safe consumption and binding are mandatory, source/Body truth
re-proof is not. Failure publishes no partial program/artifact/image and
does not replace an existing requested output.

Current [implementation support](../implementation.md) and
[CG-004](../implementation/conformance-gaps.md#cg-004-native-declaration-and-body-integration)
remain inventories, not authority. The recommended local source form is now
provisionally implemented: `[E; N]` is parsed structurally, `InlineArray` is
not compiler-recognized, and the bound type table retains normalized element
identity, `uint64` count, stride, extent, alignment and required alignment.
Current local formation accepts closed `uint64` literals, earlier scalar
`const` locals, scalar-folded required expressions and eligible scalar
`ConstEval` calls after prebinding their bodies independently of declaration
order. Struct field type formation also resolves those calls in a temporary
type context and derives field geometry from the resulting structural row.
These paths use the existing bounded required-constant evaluator and do not
persist call identities. Alias and function-signature extents remain open;
whole-array signature support is a separate ABI gate. Local initializers
enforce exact ordered cardinality; only `N = 0`
accepts `[]`. No source-level 4096-byte limit remains. JIT/AOT local lowering
includes page probing and large-frame
unwind coverage; zero-payload unit elements use `A=1`, `S=B=0`, require N
initializers, and retain indexed bounds checks. Local slot offsets still have
a signed-32-bit resource limit; zero-extent arrays do not provide borrowable
element addresses. Source JIT coverage now includes all signed and
unsigned 8/16/32/64/128-bit element and pointer indices, negative/N/too-wide
rejection, one-time evaluation and right-hand-side suppression on checked
failure.
This prototype remains conditional on approval of this proposal and does not
establish accepted grammar or full proposal conformance. Nested scalar/unit
local initialization, row replacement/copy, and multi-dimensional
read/write/address paths preserve dimensions and check each index in order;
addresses require positive-payload elements. Pointer-bearing nested storage
provenance, distinct Copy Transparent-over-void nominal elements, field/
initialization/access, other declaration contexts, complete
loan/provenance transformation, and private/standard persistence remain
unfinished. Local, real struct-field and transparent-alias extents now
evaluate eligible scalar `ConstEval` calls through the existing bounded
required-constant evaluator, with field geometry derived from the structural
row. Whole-array function signatures remain rejected because no call ABI is
selected. Active
[native storage tests](../../tests/Terrum.CodeAnalysis.Tests/Native/Storage/NativeFixedArrayStorageTests.cs)
and [frame/unwind tests](../../tests/Terrum.CodeAnalysis.Tests/Native/Unwind/NativeFrameTests.cs)
exercise bounded source and runtime paths; active
[A2 structural/provider tests](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmFixedArrayLayoutTableTests.cs)
exercise metadata and direct exact nongeneric Copy-provider binding. Neither
establishes complete A2 source/runtime support. Provider dependency chains,
complete replacement/remapping, initialization/address/store persistence and
source-free/CLI arrays remain incomplete; the selected source-free loader
still rejects `0x0012`/nonempty TypeDefs.

The following are **required future gates**, not achieved evidence. Combine
them with A1/A2's malformed/schema/geometry gates; use unrelated synthetic
types/APIs, active public-API tests and supervised headless execution.

| Gate | Required positive, negative and boundary cases |
|---|---|
| Source/identity | Parse contextual `[E; N]` and nested/pointer/qualified-element forms; reject missing slots/illegal outer qualifiers/runtime extents. Equal normalized aliases and uint64 expressions share identity; different E origins/depth/qualification/N and nominal wrappers do not. Same-named user `InlineArray` stays nominal. |
| Extent/layout | N=0/1/max-u64, decimal/based/separated literals and -0; negative/too-wide/wrong-domain constants, checked overflow, selected wrapping results and required-evaluation failure/exhaustion. Representable extents above int/u32/4096 limits, product/round-up/nested-offset overflow, packed/overaligned occurrences and huge zero-stride types. Check compact graphs without expanding N. |
| Construction/state | Exact N, trailing comma, underfill/overfill/spreads/missing target; side-effecting/throwing captures and nested evaluation order. N=0 remains vacant before explicit `[]`; N>0 zero-data elements still need N expressions and preserve unit reads/checks. Failure commits no replacement and preserves prior RHS effects without publishing a prefix. Whole Copy retains pointer facts and unspecified padding. |
| Places/native | First/nonzero/middle/last, negative/N/too-wide int128/uint128 indices; base/index once, rebinding and skipped RHS/arguments on failure. Readonly versus writable paths, vacant/partial storage, unit versus nominal zero size, underalignment, nested immediate suffix and Q below/equal/above it. Actual pointer/native writes change original cells and not neighbors; no one-past or padding address. |
| Providers/transformation | Same key wrong provider, Copy loss/unique replacement, support-bearing child and fixed-layout mismatch reject. External/co-located and transitively required providers retain original/actual identities and per-Module `20 -> 2 -> 1` closure. Non-byte-identical remap/re-emission/source-free reload retains every array/place/state/provenance fact; wrong schema never falls back to A1/private paths. |
| End-to-end/boundaries | Explicit public A2 production, provider binding, source-free load, semantic rewrite/re-emission, CLI, JIT and AOT exercise initialization, dynamic nonzero selection, failure and fresh readback after mutation. Qualify final compiler/configuration/input identity. Test global/parameter/reference/constant routes only against their independently admitted profiles; a deliberate unsupported diagnostic records an open gate, not end-to-end completion. |

**Remaining decisions:** the spelling, uint64 source context, exact-list
construction/publication and direct-index source contract recommended here
still require approval, as do A1/A2 themselves. No in-scope choice is delegated
to a name recognizer or implementation limit. General symbolic/generic and
frozen array values, nominal Core APIs, nonlocal/reference/call transport,
managed/non-Copy arrays and extra targets/compositions remain external
prerequisites when demanded by adoption; this document selects none of their
missing schemas, APIs or runtime policies. Required implementation and
qualification remain work even where the contract is already fixed.
