# Standard TRRM EN1 class field query selection

**Status: unapproved; provisional direction under the user-authorized
missing-contract workflow.** This is a recommended selection/consumption
contract, not normative adoption, implementation evidence or qualification.

## Decision and integration boundary

Extend the existing explicit **type+field ExportKey selection** operation to
the four exact [EN1 agreements](standard-trrm-c1-enum-closure-composition.md#exact-whole-schema-selection).
Retain declaration-aware class field paths without manufacturing executable
class layouts. Recommend the class-offset applicability below as the smallest
source decision needed alongside that host extension.

Reuse `DE` kinds 3/4, FieldMap, FieldPath and Feature `13/1`. Select **one
complete EN1 literal**, identically for root, every supplied provider
(including unused inputs), retained origins and re-emission. This does not
compose EN1 with LayoutQueries or the nested-initialization profile. This
selection contract adds no syntax, API, wire field, Feature, row ID, flag, CLI
transport or compatibility reader. The separate
[class instance-field layout proposal](standard-trrm-en1-class-field-layout.md)
recommends exact whole-schema successors for numeric class-layout use; base
EN1 identities retain symbolic queries only.

The [class publication proposal](standard-trrm-c1-class-source-publication.md)
continues to own class/member export input and complete retained declarations.
This proposal adds its missing **consumer field selection**, not another
publication map or permission to omit constructors/members. The
[LayoutQueries boundary](standard-trrm-layout-queries-source-provider-boundary.md#recommended-consumer-input-and-api)
continues to own its struct-only profile; its API shape is reused, not its
safe-Blittable eligibility rule.

On adoption, add the explicit class-offset subject rule below to
[registered queries](../specs/language/registered-queries.md#layout-observation-and-authorization)
and [value-layout observations](../specs/data-layout-and-abi.md#value-layout-observations).
The physical class field-storage recipe and numeric support boundary are
separately proposed in the
[EN1 class instance-field layout proposal](standard-trrm-en1-class-field-layout.md).
It is a narrow exception to their complete-value/handle subject wording,
**not a claim that that wording already selects class payloads**. Preserve
their other query rules. The existing
[FieldDef/path binding](../specs/representation/binding-and-replacement.md#fielddef-importexport-and-layout-path-obligations),
[metadata](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics),
[query encoding](../specs/il-instruction-set.md#optional-values-aggregates-and-queries),
[manifest](../specs/representation/semantic-manifests.md) and
[consumer-verification](../specs/representation/reader-trust.md) contracts
remain unchanged.

The inspected gap is concrete:
[`StandardTypeReference`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.StandardTypeReference.cs)
accepts EN1 type keys but routes type+field keys only to LayoutQueries/nested
initialization; [`Binder`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.cs)
and the [query codec](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.RegisteredQueryBody.cs)
use struct path owners, and the codec rejects unsafe-required queries.
The existing public-API
[`ReemitsEN1ClassTypeDefAndFieldImportsAndTypeSpecs` regression](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmC1EnumClosureSourceTests.cs)
retains FieldDef bindings, not offset Bodies. None establishes this proposal's
source/Body acceptance.

## Exact host selection

Use the existing required-five-argument C# API, with no new overload:

```csharp
public static CompilationReference CreateStandardReference(
    TrRmStandardArtifactInput input,
    string moduleIdentity,
    IReadOnlyList<TrRmStandardArtifactInput> providers,
    IReadOnlyList<string> typeExportKeys,
    IReadOnlyList<string> fieldExportKeys);
```

For EN1, the existing four-argument type-key operation is equivalent to an
empty field-key list; it acquires no implicit field selections. Existing
class/inline-struct/enum root eligibility remains unchanged. A separate
class-specific API would have the same identities, inputs and permissions,
but duplicate capture and validation; it is not recommended.

1. Capture owned immutable artifacts, both lists and their original order
   before processing. Preserve existing scalar-text, nonempty/NUL-free,
   ordinal key equality, duplicate-key and Module-wide publication rules.
   Require at least one type key. Later caller mutation cannot change the
   completed reference.
2. Resolve keys **only in `moduleIdentity`**. Each type key selects an eligible
   public provider-local TypeDef. Each field key selects a public, nonstatic,
   provider-local FieldDef whose exact owner is an explicitly selected class
   or already-admitted inline-struct root. Reject proxy exports, wrong kinds,
   missing keys, wrong/unselected owners and enum-member selections.
3. Keep all required declaration/layout closure, including private fields,
   independently of selection. Expose original names, not keys. Hidden closure,
   public metadata, a type-key selection and provider presence do not select
   field keys or grant source access.
4. Every external path step needs its exact explicitly selected field binding,
   or an already-retained exact FieldDef Import translated from its original
   using context. The latter preserves that binding, not permission to discover
   other provider fields. A key from another Module is not selected by this
   list. Separate selections require independently admitted composition;
   this proposal does not extend the LayoutQueries-only reference-set API.
5. Distinct selected aliases to one actual definition share identity while
   retaining every key/view and equality obligation. After equality checking,
   the first explicit alias may represent **new uses only**. Existing Imports
   keep their keys. Names, offsets, field ordinals and equal layouts are never
   cross-artifact identity.

Preflight all exact selections and retained per-Module requirements, then bind
the complete provider closure, including unqueried/provider-owned Imports.
Retain the selected Module directory, target/runtime tuples and every original
expected owner/field view separately from actual provider declarations.
Resolve a FieldDef Import only after its owner TypeDef binds to that exact
actual owner; correspond its complete type, access/static/mutable roles and
every retained complete Layout variant/ordered membership. Direct references
to expected stubs cannot bypass those checks.

## Proposed class-offset subjects and limits

These are **proposed applicability rules for existing syntax**, not runnable
examples or a new class representation:

| Subject | Candidate meaning and boundary |
|---|---|
| `offsetof(C, F)` and `offsetof(C, F.Inner)` | An exact ordinary nongeneric class declaration `C` explicitly selects its **instance field-storage layout**, not its handle slot. Return cumulative inline displacement from that layout's field-storage base. This does not construct an object, project `val<C>` or define a header-to-payload displacement. |
| Subsequent path steps | Derive the exact next owner from the previous complete field type. Only inline transitions continue: class-to-inline-struct is eligible; a class-handle, reference, pointer, optional or other indirection may terminate a path but cannot be traversed. Preserve enum and class-handle terminal types without inferring Copy/Unique. |
| `offsetof(Place)` | Only an independently legal, statically bound instance-field Place. Return its terminal displacement in its immediate containing field-storage layout, not a cumulative offset across receiver indirections. It evaluates neither receiver nor value. No new class-valued parameter, receiver, local or Place operation is admitted merely to obtain this subject. |
| Other class forms | This extension admits no generic, constructed, GC, optional or qualified class query root. Existing independently supported forms retain their own rules; no qualifier is erased to fit this route. |

Ordinary visibility, source declaration legality, public-field/frozen-layout
restrictions and field-specific obligations still apply. No private field,
property, indexer, static field or inferred layout name becomes a selected
source path. In particular, persistence fixtures with public Auto class fields
do not establish legal source publication/access; this proposal adds no public
Auto declaration exception or class applicability for struct Layout/Blittable
attributes.

For a non-Blittable **queried instance layout**, source checking requires an
explicit unsafe block/expression covering the observation. A fixed handle
representation, scalar result, exported field or caller-unsafe declaration
does not discharge that requirement. Safe observation requires an independently
valid fixed representation commitment for that exact class layout and target.
Preserve any distinct per-field unsafe obligation; an unrepresented declaration
contract refuses affected use rather than being erased.

The new minimum Body case is a nongeneric, parameterless module-level method
returning `nint` from one such type-path query. Existing scalar/query Body
forms keep their own admission; this adds no arbitrary class method,
constructor, construction, lifecycle, field-value or address operation.
Bindings and source-free semantic Bodies may retain the symbolic query before
physical layout is available. Required constants, folds and native lowering
must instead have a supported selected target/layout contract.

Keep the existing Windows x64/fail-fast boundary and exact target/rule
requirements. Metadata-only class declarations supply **declared** Layout
inputs, not object geometry. Derive a numeric result only under an independently
selected supported class-layout agreement, with complete field representations
and checked `0..nint.MaxValue` results. Do not use field ordinals, host/private
`BoundClass` offsets, pointer-width guesses or zero defaults. Missing geometry
refuses evaluation/lowering; it need not prevent declaration/path retention.
Every retained/folded observation needs its fixed commitment or complete
rebuild/rebind/invalidation dependency basis. `sizeof(C)`/`alignof(C)` still
observe the complete handle; no object-size API or native class ABI is chosen.

## Verification, reconstruction and remapping

* Carry compiler-owned selected-field associations, complete roots/ordered
  steps, snapshot-local owner/field identities, original expected views,
  aliases and target/observation dependencies through `CompilationReference`,
  binding, `BoundProgram`, shared traversal/rewriting and mandatory verification.
  Use the separate class declaration/nominal-use carriers; do not fabricate
  `BoundClass`, layout-shaped structs or executable field-access nodes.
  Query permission neither proves nor consumes class ownership.
* Source producers establish access and operation-local unsafe facts before
  erasing scopes. A valid decoded `DE` publishes that operation's checked fact;
  after structural/support/binding checks, reconstruct it only for that query,
  without an ambient unsafe scope or new serialized trust/authorization bit.
  Reuse the [scope-erasure trust distinction](standard-trrm-l1-field-unsafe-authorization.md#exact-operation-local-fact),
  not L1's profile or value-access admission. Consumers need not re-prove
  authored Bodies. Hand-built/changed queries need the producer's justification;
  mandatory compiler verification is not bypassed.
* Emit consumer-local owner expectations and FieldDef stubs in their checked
  ranges, with original TypeDef/FieldDef Imports and ModuleRefs. A canonical
  one-field exact unqualified nongeneric owner uses the direct FieldDef/Import
  FieldMap form; nested paths use `0x1D`/`004B`, a complete root TypeSpecRef and
  ordered FieldDef/Import references. No offset TypeMap operand is added.
  Keep Feature `13/1` for FieldDef publication/imports or retained query/path
  content, independently of Feature 18 and other retained requirements.
* `LoadStandard(input, moduleIdentity, providers)` must validate that same
  selected closure before rebuilding class-aware queries, without source/debug,
  a live receiver, native symbol resolution or executing providers. Validate
  map/token namespaces, extents, bounds, nonempty paths and every immediate
  owner. Metadata retention does not imply numeric evaluation support.
* Re-encoding rebuilds Module partitions, Import/Export/ModuleRef,
  TypeDef/FieldDef ranges, complete TypeSpecs/Layout/Profile references,
  FieldPath roots/steps, Body maps/indexes/counts/extents and affected
  source/reference slots. Rebuild each `0045` observation and its exclusive
  ordered `004A` FieldReference range, including after folding. Clone
  context-sensitive rows. Preserve all original views and stronger relied-on
  facts after Import-to-direct resolution or replacement; no byte substitution,
  representative-only expectation or unchanged-snapshot replay substitutes
  for remapping changed inputs.

Use existing argument failures for invalid selections, `InvalidDataException`
for malformed/incompatible bindings and `NotSupportedException` for unsupported
complete contracts/use/target. Preserve explicit checked resource refusal.
Return no partial successful reference/program/artifact and never retry another
schema, key or provider.

## Required acceptance, not current results

| Case | Required result |
|---|---|
| Source and exact selection | Through public APIs, select eligible class/type/field keys unlike source names; bind the minimal offset Body, emit, source-free load/re-emit/reload. A provider may be source-free metadata; public Auto source fixtures are not a substitute for an eligible publication. Empty field selection grants no new field use; a path lacking both selected and exact retained bindings rejects. Exercise all four exact EN1 selections; unsupported retained PI1/other facts still refuse. |
| Paths and representation | Direct class field and class-to-inline-struct paths preserve exact root/steps and cumulative versus terminal semantics. A terminal enum/class handle preserves nominal identity and unknown ownership. Class-to-class-handle traversal, unsupported root qualifications and invented `val<C>` refuse. Symbolic retention passes without invented geometry; unsupported numeric evaluation refuses. |
| Selection and source negatives | Missing/wrong-kind/duplicate/invalid-text keys, unselected or wrong owner, proxy/private/static fields and enum members reject. Public metadata/closure alone supplies no key. Missing source unsafe, inaccessible fields or invalid public Auto declarations diagnose; an unsafe declaration is not an unsafe body. |
| Provider and original views | Non-first/co-located providers, provider-owned FieldDef Imports and a chain/diamond retain every original Module/key/owner expectation. Compatible physical row relocation passes binding; wrong actual owner/type/role, alias splitting, a changed later expectation or incompatible Layout/profile/target/observation fails even after an earlier edge passed. |
| Transformations and bounds | Nontrivial type/field/Import/FieldPath/map permutations and Import-to-direct packing preserve all expected views, class/struct/enum TypeSpecs and `0045`/`004A` ownership. Test wrong token kinds, discontinuous paths, truncated ranges, capacity/resource refusal and post-capture caller mutation. |
| Cross-feature and scope | Preserve recursive unknown-ownership rejection for Copy-sensitive uses. No class-field selection enables value access, construction, generic classes, PI1 getter/init access, native ABI or LQ/nested-profile mixing. Reject an unused wrong-schema provider and unsupported retained contracts rather than pruning them. |

No in-scope selection or source-subject choice is left to implementation.
Formal approval remains outstanding. An end-to-end source-produced public
class-field case still needs a legal class-field publication/representation
contract; struct layout attributes do not supply one. Physical class-layout
observation support, complete EN1/PI1 member/Body closure, cross-Module selection
composition and runtime qualification remain independent gates, not decisions
silently settled here.
