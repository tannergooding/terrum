# Standard TRRM EN1 class instance-field layout

**Status: unapproved; provisional direction under the user-authorized
missing-contract workflow.** This selects a candidate physical field-storage
rule, not normative adoption, implemented support or native qualification.
The [class field selection proposal](standard-trrm-en1-class-field-query-selection.md)
retains its status and owns query subjects, access, explicit keys and symbolic
Body retention.

## Decision and agreement

**Derive offsets from the existing complete TypeDef/FieldDef/Layout/LayoutField
metadata.** The candidate rule is named
**`en1-windows-x64-class-field-layout/1`**. For this implementation
continuation, recommend **candidate A: exact whole-schema successors** as the
smallest existing end-to-end selection and retention mechanism. Formal
approval remains outstanding; this is the user-authorized provisional
direction, not normative adoption. Its scope is the four
[EN1 parents](standard-trrm-c1-enum-closure-composition.md#exact-whole-schema-selection),
observing Module target requirement **1**, concrete TargetProfile **(1,1)**
(`windows-x64-native/1`) and the existing fail-fast execution boundary.
All supplied providers and retained origins must use the same exact successor;
no LayoutQueries-profile composition is implied.
Preserve actual provider/ModuleRef target and runtime requirements; the
observation's target cannot override an incompatible provider requirement.

The existing schema-identity input and provider/native-plan schema snapshots
select and retain the rule. Base EN1 identities keep their current meaning;
they permit symbolic query retention, not numeric class-layout use. Numeric
evaluation, folding and lowering require the matching exact successor for the
root, every supplied provider and retained origin. This does not amend the
accepted Windows profile or imply that Profile (1,1) alone assigns class
storage.

### Exact successor identities

Use these exact candidate literals, each an extension of one existing EN1
parent:

| Existing parent | Candidate class-layout successor |
|---|---|
| `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-1-parameter-labels-1-receiver-access-1-enum-closure-1` | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-1-parameter-labels-1-receiver-access-1-enum-closure-1-class-instance-layout-1` |
| `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-enum-closure-1` | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-enum-closure-1-class-instance-layout-1` |
| `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-1-parameter-labels-1-receiver-access-1-property-initialized-state-1-enum-closure-1` | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-1-parameter-labels-1-receiver-access-1-property-initialized-state-1-enum-closure-1-class-instance-layout-1` |
| `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-property-initialized-state-1-enum-closure-1` | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-property-initialized-state-1-enum-closure-1-class-instance-layout-1` |

These are complete identities, not aliases. Existing `schemaIdentity`,
`TrRmStandardArtifactInput.SchemaIdentity`,
`BoundSourceTypeProviderSnapshot.SchemaIdentity` and
`TrRmStandardNativeBindingPlan` snapshots provide the selection/provenance
path. Producer, root, every supplied provider (including unused inputs),
re-emission and source-free loading must use the identical successor. A mixed
base/successor graph rejects; conversion must preserve/rebind retained
obligations, not relabel an artifact. `CompilationOptions.NativeLoweringAgreement`
does not select this rule. Native code must remain associated with the validated
schema/provider binding plan and complete query dependencies; detached code or
a cache entry without that context must refuse reuse. There is no fallback to
a build name, process state, documentation label, target tuple or base EN1
identity.

The alternative is a separate host option/manifest plus a new binding-plan
field. It would preserve the four EN1 identities but duplicate selection and
provenance mechanisms across compilation, source-free loading and native
execution, so it is not recommended. The exact successor identities are
provisionally selected for implementation under the user's authorized
workflow; changing them remains a future approval decision.

| Alternative | Disposition |
|---|---|
| **Existing declared inputs plus a deterministic target recipe** | **Recommended.** Ordered field membership and complete field representations suffice for the admitted forms below. Reuse existing query/dependency records and remapping. |
| Producer-authored per-field offsets and block extent/alignment | Needed for arbitrary gaps, interleaved runtime data or another non-derivable layout, not this slice. Would require an explicit schema, extent/alignment/overlap rules and replacement obligations. Existing rows contain no such offsets; do not reinterpret an ordinal or reserved word as one. |
| Reuse private executable `BoundClass` geometry | Not selected. It supplies neither a Standard declaration-derived agreement nor portable object/header facts. |

The exact successor identity is a new schema selector, but no table/row
encoding, Feature or TargetProfile assignment changes. It does not broaden the
selection proposal's class/path applicability.

## Admitted variants and field representations

Select the exact (1,1) Layout variant when present, otherwise the single
default variant. Selection replaces the **whole** layout/ordered-field payload;
never merge variants or fall back because the selected variant is unsupported.

| Selected class Layout | Numeric field-storage support in this agreement |
|---|---|
| Auto (`kind=0`), flags/alignment/pack all zero | Supported by the natural-placement recipe below. Its ordered LayoutField list is authoritative, not physical FieldDef row order. |
| Sequential (`kind=1`), flags zero | Supported using Windows x64 packing/alignment rules and the complete ordered list. Pack is normalized to 1/2/4/8/16; alignment is omitted (zero) or a permitted power of two through 8192 which does not weaken derived alignment. This is a metadata mapping, not permission to annotate a source class. |
| Class Blittable bit, Union, Transparent or BlittableInterface | No numeric mapping selected here, even if structurally retainable. Union's commitment/active-state issues and Transparent's carrier semantics cannot be imported as a class-handle or payload ABI. Refuse affected evaluation/lowering. |

Those unsupported kinds need a separate class-specific semantic decision, not
automatically an offset table; a genuinely non-derivable physical layout needs
the explicit representation described above.

Validate complete membership: every instance FieldDef, including private and
represented backing storage, occurs exactly once; static fields do not occur.
Resolve **all** instance-field representations, including unqueried fields
after the selected field, before producing any offset. A known prefix or a
first-field offset of zero is not a partial-success route.

Eligible complete field representations are the Windows x64 core scalar
storage, covered native data/plain-code pointers, EN1 integer-backed enums
with their exact underlying target binding, and ordinary nongeneric inline
structs whose complete selected layout is independently supported. Preserve
each inline type's internal offsets, extent, natural alignment and transitive
required explicit alignment. This does not invent an Auto-struct rule where
none is selected, nor infer Copy/Unique from geometry.

Stored class handles, references, own/box/view/task descriptors, optional/tag/
tuple forms, generic or otherwise unknown representations receive no
pointer-sized substitute. Their presence refuses the complete numeric layout
unless a separate compatible representation agreement is explicitly composed;
this proposal selects no such composition. Pointer pointees are not expanded.
By-value cycles reject. Logical void uses the existing zero-storage rule only
where its declaration is independently legal; a zero-extent nominal needs its
own admitted placement rule and is not treated as void.

## Physical recipe and bounds

The origin is an abstract **instance field-storage base**, not the allocation
base, class handle, header or first user-obtainable address. There is no hidden
header/vtable/collector word in this block. Assigning its offsets defines no
way to locate that base from a handle, allocate an object or access a value.
Any future object representation claiming this agreement must place the
represented fields consistently relative to its field-storage base.

1. Start `end=0` and derived block alignment `A=1`. For each nonzero field,
   obtain complete extent `s`, natural alignment `a` and required explicit
   alignment `r`. Positive alignments are powers of two.
2. Auto uses placement alignment `p=max(a,r)`, with no aggregate packing or
   alignment control. Sequential uses `p=max(min(a,Pack),r)`, exactly as
   [Windows x64 §5](../specs/targets/windows-x64-data-layout.md#5-aggregate-placement-empty-storage-and-stride).
   Set `offset=roundUp(end,p)`, then `end=offset+s`; accumulate `A=max(A,p)`.
   A legal logical void field has offset `end` without padding or advancement.
3. Apply a valid Sequential aggregate Alignment, if supplied, to obtain final
   `A`. Internal block extent is `roundUp(max(end,1),A)`, using the existing
   ordinary-empty-storage rule. It is not an object-size query, allocation
   requirement, class array stride or `sizeof(C)`. Do not force eight-byte tail
   rounding or import hosted payload limits.

Compute additions, rounding, extents and cumulative path sums in the checked
Windows x64 **64-bit nuint domain**, not host `int`, pointer width or metadata
u32. Check each field range against its completed containing block. Only the
observed query result must additionally fit `0..nint.MaxValue`; an internally
representable larger block is not itself a queried `nint` size. Overflow or an
unrepresentable result refuses use, without wrapping, saturation, truncation,
reordering or a zero/default sentinel. Existing checked row/range limits and
explicit resource/capacity refusal remain independent of mathematical layout.

## Source, symbolic Bodies and numeric use

* `offsetof(C, F.Inner)` sums the agreed class-field displacement and subsequent
  inline-struct displacements. `offsetof(Place)` returns only the terminal
  field displacement in its immediate containing layout. Neither traverses
  a class handle, reference, pointer or other indirection. A terminal pointer
  can have known storage; a terminal class handle remains unsupported here
  because this agreement does not assign that field's representation.
* Source classes keep their existing unannotated Auto declaration, private/
  unfrozen field rules and constructor/member closure obligations. Existing
  struct Layout/Blittable schemas do **not** apply to classes. A legally
  accessible class observation requires explicit unsafe authorization; no
  safe/fixed class-field commitment is selected. Public Auto metadata fixtures
  do not establish legal source publication/access. No new class Place,
  receiver, member Body, construction or value operation is authorized.
* Metadata-only declarations need no executable `BoundClass`, source/debug
  text or live receiver. Keep the existing declaration and query carriers
  separate from derived layout facts. Source producers check access/unsafe
  facts; consumers perform structural, support and binding checks, then rely
  on the producer's operation-local published facts without re-proving
  authored Bodies or manufacturing ambient unsafe authorization.
* Symbolic `DE` kinds 3/4 with FieldMap/FieldPath may survive without this
  physical agreement. Required constants, optional folds and JIT/AOT/native
  lowering instead require the selected agreement and complete eligible
  geometry. AOT/JIT timing changes neither the rule nor dependencies.
  Supported offsets are fixed at that target specialization and lower to
  ordinary `nint` constants, without object creation, dereference or a runtime
  layout helper. This agreement supplies no execution-dependent class layout.
  A published AOT image is closed over its selected build inputs; changing a
  provider or rule requires rebuilding the image. JIT/native plans must keep
  the exact successor identity and full provider snapshots through lowering;
  code detached from that plan is not reusable under changed inputs.
* Unavailable required evaluation diagnoses; unavailable lowering refuses.
  An optional fold may leave the original symbolic query, but cannot repair
  an unsupported final executable use. Preserve existing malformed-input,
  unsupported-contract and resource-refusal distinctions. `sizeof(C)` and
  `alignof(C)` still concern a separately supported **handle** representation;
  this proposal supplies neither result.

## Provenance, persistence and invalidation

Under candidate A, retain the exact successor schema identity together with
exact root/path identities,
original expected owner/field views and aliases, actual provider snapshots,
selected Layouts, every field's complete
representation closure, target and companion rule context through binding,
shared rewriting, verification and lowering. A derived offset is not a
declaration identity or a new serialized field-offset fact.

These class layouts are unfrozen. Retained values, derived decisions and
embedded machine-code constants require the complete explicit layout-coupled
rebuild/rebind/invalidation basis; a scalar result or exported returning method
does not create an independently replaceable fixed-layout promise. Changing
any relied-on field membership/order/type, nested layout, binding/provider
revision, target or rule context invalidates cached geometry and affected
observations/code before reuse. Rebinding only metadata does not patch native
constants. Preserve every original expectation and stronger relied-on fact,
even when equal offsets survive or Imports become direct references.

Reuse `0045` TargetLayoutDependency and each observation's exclusive ordered
`004A` FieldReference range, including after folding, with concrete (1,1)
profile and the complete subject/path. Remap their owners, TypeSpecs and field
references alongside FieldMap/FieldPath and Module/provider contexts.
These records do not by themselves encode an invalidation mechanism or admit
general ConstantExecution in base EN1, and they do not replace the exact
successor-schema selection. If the selected owning records/admission context
cannot retain the full observation and coupling obligations, preserve the
symbolic Body for interchange or refuse persistence of the evaluated form;
never emit an orphan scalar constant and discard its dependencies.

Ordinary Body linkage is separately specified by the unapproved
[Body layout-query dependency proposal](standard-trrm-en1-body-layout-dependencies.md):
ordered exact matching of rows outside request/value ranges, with genuine
value-owner linkage or symbolic retention after folding. The existing
reader and producer changes are partial; the proposal's full acceptance
requirements remain open.

## Integration owners and inspected evidence

On adoption, integrate the recipe into
[Windows x64 data layout](../specs/targets/windows-x64-data-layout.md), with
its selected-target scope in [the target profile](../specs/targets/windows-x64.md).
[Data layout/ABI](../specs/data-layout-and-abi.md#value-layout-observations) and
[registered queries](../specs/language/registered-queries.md#layout-observation-and-authorization)
own the proposed class-field-storage subject exception and unchanged
authorization/coupling rules. [Metadata](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics)
retains declared inputs, not serialized physical offsets;
[reader trust](../specs/representation/reader-trust.md),
[binding/replacement](../specs/representation/binding-and-replacement.md#fielddef-importexport-and-layout-path-obligations),
[native admission](../specs/native.md#binding-and-image-admission) and
[JIT](../specs/jit.md#replacement-and-code-lifetime) retain their existing
checking, dependency and code-lifetime responsibilities.
[Exact draft agreement](../specs/representation/format-versioning.md) remains
necessary. The exact successor literals are the proposed schema selectors;
no Feature, TargetProfile, row-ID or schema sniffing mechanism is proposed.

Inspected source, **not executed qualification**:

| Evidence | Consequence |
|---|---|
| [`BoundStandardClassLayoutVariant`](../../sources/Terrum.CodeAnalysis/Semantics/BoundStandardClassDeclarations.cs), [`CreateStandardClassTypeView`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.StandardTypeReference.cs) | Retain target/kind/flags/alignment/pack and ordered FieldDef identities, not physical offsets. Sufficient inputs only after selecting this recipe and resolving complete field layouts. |
| [`TrRmLayoutTableReader`](../../sources/Terrum.CodeAnalysis/Modules/TrRmLayoutTableReader.cs) | Already selects exact-profile-or-default complete variants and validates declared controls; it does not establish a class placement algorithm. |
| [`DeclareClass`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.ManagedClasses.cs), [`ValueLayout`](../../sources/Terrum.CodeAnalysis/Semantics/ValueLayout.cs) | Hosted class geometry includes bounded payload/eight-byte rounding and managed-handle assumptions. Those private facts are not this Standard agreement. |
| [`NativeEmitter` query lowering](../../sources/Terrum.CodeAnalysis/Native/NativeEmitter.cs) | The bounded EN1 path now derives Auto/Sequential class-field offsets from ordered layout fields and complete field types. Full representation/replacement coverage and native qualification remain open; this proposal stays unapproved. |

## Required acceptance, not current results

Numeric cases below are required outcomes after implementing candidate A, not
permission to bypass the current gate.

| Case | Required outcome |
|---|---|
| Selection and retention | Base EN1/profile (1,1), an arbitrary `NativeLoweringAgreement` string or an undocumented ambient flag cannot enable numeric use. The exact class-layout successor must match through producer, artifact/provider capture, source-free reload/re-emission and lowering. A retained native plan must retain the selected identity and full provider snapshots; detached code or mismatched context refuses reuse, and changed context invalidates prior acceptance. |
| Natural and packed placement | Auto fields uint8/uint32 have offsets 0/4; uint8/int128 has second offset 16. Metadata Sequential Packing=1 gives uint8/uint32 offsets 0/1. No forced header displacement or pointer-alignment cap. |
| Nested explicit alignment | Auto class fields uint8 then an independently admitted 32-aligned inline struct place that struct at 32; an inner field at 4 yields type-path offset 36 and immediate Place offset 4. Packing never removes its required explicit alignment. |
| Variant selection | Exact-profile reordered membership/controls replace the default completely. Unsupported exact variants refuse rather than use the otherwise supported default. An unknown required target/rule or absent companion refuses numeric use. |
| Source versus metadata | A legal owner-accessible unsafe source query and a truthful source-free query with identical declarations resolve identically when their Body/closure routes are supported. Missing unsafe, inaccessible fields and public Auto source declarations diagnose; metadata retention alone does not legalize them. |
| Symbolic versus executable | Source-free retain/load/re-emit preserves supported symbolic paths without geometry. Numeric/native query-only methods return the agreed constant without allocating/reading a class; unsupported field representations, including unqueried trailing handles, refuse even for a first-field query. Emission and supervised execution are separate acceptance evidence. |
| Bounds and malformed input | Exercise checked nuint addition/rounding, results at nint.MaxValue and beyond, resource refusal, empty classes without an invented field, and non-void zero-extent fields lacking placement rules. Reject wrong owners, incomplete/duplicate/static membership, bad ranges and inline cycles; never truncate indexes or geometry. |
| Binding and transformations | Exercise all four class-layout successor identities, non-first providers, aliases, changed later expectations, nested layout changes and physical row permutations. Source-free reload/remapping and Import-to-direct packing preserve every original view and `0045/004A` relationship. Changed relied-on inputs invalidate folded values/native constants, even if their numeric offset happens to remain equal. |
| Scope isolation | Pointer traversal, class-handle traversal, unsupported class kinds/qualifications, class Blittable/Union/Transparent mappings and invented source layout attributes refuse. No successful offset establishes class value access, construction, marshalling, ownership, header shape or a native class ABI. |

Formal approval remains outstanding. Candidate A's exact successor identities
are the provisional implementation direction. Bounded exact-successor
dependency validation, source-free class-query load/re-emission and
Auto/Sequential lowering for supported complete layouts now exist; direct and
nested regressions are added but remain unverified under the active hold.
Nontrivial remapping/replacement, folding and native qualification remain
unverified or open. Public fixed class-field publication/safe observation,
additional stored field representations, arbitrary explicit offsets and
object/header/handle/runtime ABIs remain separate **unselected decisions**, not
implementation discretion under this agreement. Existing source/member/Body
closure and native qualification gates remain; this documentation supplies no
evidence that those gates have passed.
