# Standard TRRM Core exception identity binding

> [!IMPORTANT]
> **Unapproved proposal; provisional direction only under the user's explicit
> missing-contract authorization.** This document does not amend normative
> specifications, approve E1, establish compiler conformance or supply runtime
> support. [Proposal index](README.md).

The authorization is: “At a genuine missing contract, prepare the smallest
concrete decision needed as a new proposal doc using gpt-6.1-sol at max
reasoning, then continue with it being treated as a provisional
approval/direction.”

## Decision and scope

Recommend **ordinary exact Module/Import/Export binding, with the selected
Core authority supplying two explicit nominal anchors per using Module**:
the self-typed exception interface and the Unreachable payload. Derive the
common existential view from that same interface declaration. Add no Core
identity table, registration opcode, declaration-kind assignment or extension
to `NativeCoreRegistrationSet`.

The three selected roles are `IException<TSelf>`, `IException<_>` and exact
Core `Terrum.UnreachableException`. They are **two nominal declarations and
one constructed view**, not three independently registered TypeDefs. A class's
closed `IException<Class>` witness is ordinary nominal conformance to the
first anchor; it is not another Core declaration.

Apply this recommendation only to the unapproved
[E1 candidate](standard-trrm-register-body-exception-regions.md):
**`trrm/1-draft-register-exception-regions-1`**, exactly **B + E1**, with
Feature **21/1**, Body record **0F** and amended RegionExit **0A** unchanged.
For the initial Core-exception consumption operation, recommend **explicit
E1 selection for the root and every supplied provider artifact**. The
[provider-schema rule](#explicit-provider-schema-boundary) below deliberately
chooses this conservative restriction; it does not infer a composite schema.

This fills E1's exact Core selection/binding deferral. It does not redesign
events, handlers, dispatch, exception creation, cleanup or the common member
API. It selects no production Module identity or export-key spelling. It
proposes the explicit nonserialized host/CLI selection record below, but no
source annotation, ABI or private persistence contract.
Standard `runtimePolicy=0` remains fail-fast for unhandled failures; Core
identity selection adds no runtime-policy opt-in or native-unwind admission.

## Accepted owners and implementation evidence

The [specification index](../specs/README.md) leads to these controlling
contracts:

| Accepted owner | Preserved baseline and relevant gap |
|---|---|
| [Local failures](../specs/language/local-failures.md#1-the-common-exception-contract) | Classes implement exact self-typed Core `IException<TSelf>`; its common view hides self without changing object identity. Exact Core UnreachableException alone is the selected zero-data Copy payload and does not implement that interface. The concrete cross-artifact Core designation is not specified here. |
| [Library identity](../specs/libraries/adopted-inventory.md#1-status-and-identity) | Core is a designated authority, not a fixed package binary identity. The selected profile supplies missing exact identities before source-independent use; an ordinary referencing package cannot grant Core roles. |
| [Complete types](../specs/language/types-and-normalization.md#complete-type-terms), [witnesses](../specs/language/interface-witnesses.md#nominal-conformance-and-member-identity) | Preserve selected constructors, complete arguments, qualifiers, self/formal identities, requirement declarations and checked implementations/defaults. Names, shape and an implementing type alone do not identify a witness. |
| [Metadata](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics), [registry](../specs/representation/trrm-1.registry.json) | ModuleRef, TypeDef Import/Export, InterfaceDef, complete TypeSpecRef and InterfaceWitness already carry the needed declaration edges. They do not designate arbitrary exports as Core. |
| [Binding and replacement](../specs/representation/binding-and-replacement.md) | Resolve one exact Module/key/kind first, then compare every original expected contract in its context. Preserve alias equality, selected owners and stronger provider facts actually used. |
| [Format identity](../specs/representation/format-versioning.md), [admission](../specs/representation/requirements-and-admission.md) | One exact schema per artifact; separately supplied artifacts have independent selections. Their supported directed interoperability is not decoder inference or profile composition. |
| [Consumer verification](../specs/representation/reader-trust.md) | Structural, support and binding checks are mandatory before use. Producers establish semantic truth; consumers need not re-prove arbitrary provider Bodies. |
| [Logical records](../specs/representation/logical-records.md#local-failure-eligibility-and-catch-inputs-encoding-pending), [Modules](../specs/modules.md), [JIT](../specs/jit.md#replacement-and-code-lifetime) | Complete exception evidence, original expectations and actual code/type/witness origins survive separate compilation and lowering. Identity alone supplies no missing runtime or generic codec. |

E1's
[handler rows](standard-trrm-register-body-exception-regions.md#exceptionhandler-rows)
already require an ExactObject witness, exact CommonObject type and exact
UnreachableValue type. Its
[binding rules](standard-trrm-register-body-exception-regions.md#binding-replacement-and-retained-origins)
and [candidate cases](standard-trrm-register-body-exception-regions.md#candidate-qualification-and-acceptance-gates)
require rejecting a wrong Core/witness identity, without choosing how to
supply the Core anchors.

Direct source inspection establishes the following implementation context,
not new validation or support claims:

| Inspected source | Relevant fact |
|---|---|
| [`NativeCoreRegistration.cs`](../../sources/Terrum.CodeAnalysis/Semantics/NativeCoreRegistration.cs) | `CoreDeclarationKind` admits only EnumType, EnumMember and AttributeSchema. `NativeCoreRegistrationSet` selects native enums/schemas. Its provider-origin descriptors are useful precedent, but neither interface nor exception-payload registration is present. |
| [`BoundProgram.cs`](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs) | `BoundInterfaceDefinition` carries `BoundModuleImport`; that import retains ProducerIdentity, ExportKey, TargetRequirement and RuntimePolicy. This is an existing ordinary interface-origin carrier, not Core designation. |
| [`TrRmInterfaceDefTable.cs`](../../sources/Terrum.CodeAnalysis/Modules/TrRmInterfaceDefTable.cs) | The reader checks interface TypeDef ownership, a declared type self formal and owned ranges. Those checks cannot identify the selected Core interface. |
| [`TrRmInterfaceWitnessTables.cs`](../../sources/Terrum.CodeAnalysis/Modules/TrRmInterfaceWitnessTables.cs) | `TrRmInterfaceWitnessTableReader` exposes both `GetImplementingTypeSpecRef` and `GetInterfaceTypeSpecRef`, and checks nonzero complete references and exclusive binding ranges. Selected Core/provider comparison belongs above this structural layer. |
| [`ModuleRef`](../../sources/Terrum.CodeAnalysis/Modules/TrRmModuleRefTableReader.cs), [`Import`](../../sources/Terrum.CodeAnalysis/Modules/TrRmImportTableReader.cs), [`Export`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExportTableReader.cs) readers | Existing rows retain exact provider/key/expectation edges and public local exports; structural reading is not complete provider satisfaction or authentication. The current E1 Import path does not enable the A2-specific multi-view alias bookkeeping. Legal alias preservation needs implementation, not a new E1 wire meaning. |
| [`TrRmArtifactIndex.cs`](../../sources/Terrum.CodeAnalysis/Modules/TrRmArtifactIndex.cs), [`TrRmExceptionDirectoryReader.cs`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExceptionDirectoryReader.cs) | An explicit RegisterExceptionRegionsDraft selector and directory/handler carriers exist. The directory reader takes bytes and counts, not an authorized Core binding context. A selector or directory pass does not establish exact Core identity or execution support. |

The unapproved
[text-profile identity proposal](text-construction-profile-identities.md)
likewise separates profile-supplied role selection from ordinary binding.
The [A2 source identity proposal](standard-trrm-source-copy-type-identity.md)
supplies explicit source export maps for its distinct scope. Reuse checked
origin/expectation machinery where applicable; neither proposal grants E1
support, exports, authority or schema composition.

## Alternatives

All directions in this table are unapproved.

| Direction | Assessment |
|---|---|
| **Ordinary exact bindings plus profile-supplied anchors — recommended** | Existing TypeDef exports/imports express both nominal identities; existing construction and witness rows express their uses. Only the profile-to-binding handoff is missing. No second declaration identity namespace is needed. |
| Extend `NativeCoreRegistrationSet` and `CoreDeclarationKind` | Possible as a future implementation API design, but the current native enum/schema descriptor is not this contract. Extending it would require interface/payload descriptors and all checks below anyway, while coupling ordinary exception binding to native registration. Do not assume that extension or allocate new kinds here. |
| Serialize a Core-role registry or fix new well-known type codes | Adds ownership, authority and reference rules even though ordinary imports already preserve exact identity. A self-declared role still cannot authorize an arbitrary provider. No need for this carrier is established. |
| Recognize names, signatures, flags, zero-data shape or witness implementers | Rejected. These may participate in checking an already selected contract; none chooses Core or substitutes for the complete interface/self/witness identity. |

Profile selection is still necessary: ordinary import resolution identifies
*a declaration*, but cannot decide which declaration the authorized profile
designates as Core. Supplying that existing authority's exact selection is
not a new identity registry or a permission for packages to self-register.

### Host and CLI selection shape

The smallest candidate transport is one immutable
`CoreExceptionIdentitySelection` per using Module, with exactly these fields:

| Field | Meaning |
|---|---|
| `usingModuleIdentity` | Exact logical Module whose retained E1 uses are checked. |
| `exceptionInterfaceProducerIdentity`, `exceptionInterfaceExportKey` | Exact provider Module and public local TypeDef ExportKey for `IException<TSelf>`. |
| `unreachableProducerIdentity`, `unreachableExportKey` | Exact provider Module and public local TypeDef ExportKey for `UnreachableException`. |

Carry the records as explicit inputs to source verification/emission and to
source-independent Standard loading/reference/provider-binding operations.
The exact C# namespace/property names may follow the owning API, but the
record fields and identity comparisons above are the contract. The provider
artifact bytes and their exact case-sensitive schema identities remain the
separately supplied `TrRmStandardArtifactInput` values; the role record does
not infer schema, revision, Module row or actual provider. Require exactly one
selection per using Module whose retained records/types use any E1 exception
role, view, witness or implicit `UnreachableException`; that record selects
both anchors so either role has one stable Module-level meaning. Modules with
no retained E1 exception/Core use need no record. Missing, duplicate,
conflicting or malformed selections decline before affected use. No
name-derived/default key or first-provider fallback is permitted.

For CLI artifact commands, propose the repeatable option
`--trrm-core-exception-anchor <using-module> <role> <producer-module> <export-key>`,
where `<role>` is exactly `exception-interface` or `unreachable-payload`.
Require exactly one option for each of the two roles for each selected Module
with any retained E1 exception/Core use; retain the
existing explicit `--trrm-schema`, `--trrm-module` and `--trrm-provider`
selections. Source compilation transports the same role records through its
explicit Core/reference inputs; an ordinary source package cannot authorize
or override them. The option and API shape remain unapproved and are not
implemented by this proposal.

## Required selection context

For each using logical Module with retained E1 exception handling, transport
or Core-role use, the embedding source compiler, Standard reference loader,
linker or JIT/AOT consumer supplies an immutable context with:

| Input | Concrete meaning |
|---|---|
| Using Module | Exact Module identity and checked ownership context of the authored or reconstructed use, not the importing caller or first Module row. |
| Exception-interface anchor | Exact provider Module identity and export key selecting a public primary TypeDef of interface kind with one type formal, its self formal at ordinal zero. Resolve and check the accepted `IException<this TException>` contract. |
| Unreachable-payload anchor | Exact provider Module identity and export key selecting the public primary, nongeneric Core UnreachableException TypeDef and its accepted zero-data Copy/constructor contract. |
| Selected providers | One unambiguous actual Module snapshot per selected identity, its containing artifact's explicit case-sensitive schema identity, exact revision selection, governing edition, complete requirements, target and runtime. Revision/snapshot selection is an environment input, not a new TRRM version field. |
| Binding evidence | Each use's original expected declaration graph, its ModuleRef/key/type/witness obligations, the selected actual origins and any stronger relied-on facts. A strings-only label or cached acceptance Boolean is insufficient. |

The two nominal anchors may come from different explicitly selected Modules;
no single physical Core package is required. Core authority/profile input
must authorize the selections. Artifacts and packages that merely reference
Core cannot supply or override that authorization. Provider authentication
remains an environment obligation; this context does not authenticate bytes.

Module identities and keys use metadata's nonempty, well-formed decoded
scalar-text domain, without embedded zero, and ordinal equality without
normalization. Apply no trimming, case folding, namespace-derived keys or
dependency-order preference. A checked resolved declaration handle may
replace a selector only while retaining the same original selection,
expectations and immutable provider context.

This context is a **required accompanying binding input for source-independent
exchange**, not optional debug data or a serialized trust assertion. Producer
and consumer agree on the originating Module's selections. Loading a Module
does not substitute the host's current Core default for its retained
selection. Missing, partial, conflicting or unauthorized context declines
affected use; it need not mean the artifact bytes are structurally malformed.
Structural-only inspection remains a narrower operation.

An event-free stream Body with only Root and no retained exception-role or
delivery dependency need not manufacture Core imports. Required selections
and imports cannot be omitted merely because a typed catch is absent or a
retained failure path is anticipated not to run.

### Identity consistency across a propagation boundary

Each Module keeps its own context. Before admitting an E1 inter-Module
failure-delivery boundary, establish that its participants resolve the
exception-interface and Unreachable anchors to the same nominal identities
under the selected binding correspondence. Distinct keys resolving to one
public TypeDef may satisfy this equality, while retaining every key
expectation. Same names or compatible contracts in different Core Modules
do not.

This is a provisional rule for one admitted E1 propagation closure, not a
global singleton Core package. Independent execution closures may make
different explicit selections. Incompatible families cannot share this
boundary by converting events, synthesizing adapters or relabeling them;
this proposal selects no such bridge.

## Assigned carriers and binding procedure

The [registry](../specs/representation/registry.md) and machine registry
already assign the following carriers. **No allocation is proposed.**

| Carrier | Use without changing its payload |
|---|---|
| ModuleRef `0010` | Exact producer identity plus target/runtime requirements. |
| Import `001C`, Export `001D` | Bodyless consumer TypeDef expectation bound to a public provider-local TypeDef by exact Module/key; both Core anchors use the already permitted TypeDef DefId kind `13`. InterfaceDef is reached through its TypeDef owner, not exported using a new DefId kind. |
| InterfaceDef `0025`, GenericParam `0019` | Exact interface owner and declared self-formal identity, domains, constraints and complete member/base closure. |
| TypeSpec `0011`, full TypeSpecRef | Nominal form `02`, imported form `03`, constructed form `05` and existential argument form `0F` express the types below. TypeSpec form `0F` and E1 Body record `0F` are distinct namespaces. |
| InterfaceWitness `0028` and its ordinary binding rows | Exact implementing/interface TypeSpecRefs, declaring requirements, selected implementations/defaults and complete owned bindings. |

Use ordinary checked global row bounds `1..0xFF_FFFF` and required
reserved-zero high bytes; use DefIds only in their polymorphic fields.
Every complete type field retains the full TypeSpecRef grammar. Body
TypeMaps retain their distinct token grammar and zero-based `u16` indexes;
an E1 handler's witness is a required global InterfaceWitness row in `u32`,
not a DefId or Body-local index. No reserved token, TypeDef Copy bit from A2,
Feature, section, RuntimePolicy, target/profile ID or alternate schema is
added. E1's 21/1, 0F and 0A remain unapproved assignments owned by E1.

Apply the following candidate procedure before affected use:

1. Select every artifact schema and actual provider snapshot explicitly.
   Decode under that selection, validate the referenced structures and
   admit the complete retained requirement/target/runtime closure.
2. Resolve each Core anchor's exact Module, then key, then required TypeDef
   kind. Require a public provider-local declaration, not an imported
   expectation re-exported as local. The interface's InterfaceDef and self
   GenericParam must belong to that exact TypeDef.
3. For an external use, retain the using Module's TypeDef expectation and
   ordinary Import through its own ModuleRef. ModuleRef target/runtime
   fields must match the selected actual producer. A source-selected local
   Core declaration instead resolves through its own authorized public
   Export and may use a direct TypeDef reference.
4. Resolve expected and actual complete types, owners, formals and witness
   requirements in their original contexts. Map declaration formals by
   kind and ordinal; apply owner substitution before member/method
   substitution. Check every original directed contract expectation.
5. Compare the resulting selected declaration with the Core anchor too.
   Compatible structure cannot establish Core identity, and correct Core
   identity cannot waive an incompatible expected contract.

Every legal retained Import view is checked independently, including
several views or keys resolving to one declaration. Preserve ordinary
duplicate-triple and conflicting-binding rejection; do not merge to the
weakest expectation. A matching Module/key with the wrong declaration kind,
owner, interface contract or payload contract fails without alternative
lookup.

### ExactObject, CommonObject and UnreachableValue

The following are candidate selected-use checks, not new source examples
or new record payloads. Let `I` and `U` be the two checked nominal anchors
and `C` the normalized complete catch class:

Equality below compares resolved, normalized complete types after binding
and substitution, not raw TypeSpecRef words or row numbers. Imported proxies
and checked direct references may denote the same identity without having
the same encoding.

| E1 use | Required identity and evidence |
|---|---|
| ExactObject | Resolve the catch TypeMap to `C`. Its required witness must have implementing TypeSpecRef exactly `C` and interface TypeSpecRef exactly the construction of `I` with self argument `C`. Check the declared class authority, self formal, all substitutions, declaring requirements, selected targets/defaults and retained contracts. A witness for `C` to another interface, or for another class to `I`, fails. |
| CommonObject | Resolve the TypeMap to the construction of the same `I` with its one self slot explicitly hidden by the assigned existential argument. This is exact `IException<_>`, not `IException<void>`, an inference hole, another interface or a separately exported common TypeDef. The handler witness field stays zero. Each actual object supplies its own closed `C` and checked `I<C>` witness, correlated with that same object and Core anchor. |
| UnreachableValue | Resolve the TypeMap to exact `U`; the handler witness field stays zero. Confirm the selected payload's retained nongeneric zero-data Copy contract. It neither supplies an object witness nor matches CommonObject. A same-shaped user struct never acquires the role. |
| Fresh Throw and implicit `unreachable` | An object operand retains its actual complete class and exact `I<C>` evidence; a value operand must be exact `U`. `unreachable` has an implicit dependency on the using Module's `U` anchor even when no typed catch or TypeMap names it. Retain the ordinary expected TypeDef/Import for that external dependency; no new well-known type code supplies it. |

Constructed form `05` references the earlier nominal/imported generic
TypeSpec row and supplies one complete argument for its one type formal.
The common view's hidden argument resolves to existing existential form
`0F` with zero qualifiers, or its equivalent assigned well-known TypeSpecRef,
not a zero/null reference. Honor the existing earlier-row, exact extent,
formal-domain and qualification rules.
Normalization expands transparent aliases; it does not erase access,
backing, representation or inner argument qualifications.

No arbitrary witness projection or adapter is manufactured to make these
equalities pass. Independently required projection, associated-member,
generic execution or common-member codecs must be supported in full or the
affected use declines. The exception-specific E1 evidence scope is not
general existential storage, GenericCall support or a new runtime lookup.

After exact `U` designation, Copy and zero-data behavior come from its
selected Core contract, not a new generic Copy flag or inference from
layout/Cleanup absence. Source producers check its public zero-parameter
constructor and absence of authored checks/effects. Consumers check the
available declared/structural contract and binding; they do not prove
arbitrary constructor or interface member Bodies. Missing required
publication/execution evidence remains an admission gap, not permission to
fabricate a stub or replace the common API's unresolved member integration.

## Explicit provider-schema boundary

The [format contract](../specs/representation/format-versioning.md#1-format-and-schema)
permits independently selected supported dependency schemas. E1 likewise
says providers *may* use other explicitly supported schemas for unchanged
contracts. That permission is not a concrete interoperability catalog.

**Provisional initial rule: this Core-exception binding/consumption operation
supports E1 root input and E1 provider input only.** This is narrower than
the general permission, not a reinterpretation of it.

Compare schema tokens ordinally and case-sensitively without trimming,
normalization, component parsing or aliases. Magic/version bytes, Features
and plausible rows never supply a missing root or provider selection.

| Explicit selection | Initial candidate disposition |
|---|---|
| Root `trrm/1-draft-register-exception-regions-1` | Required; its complete B+E1 schema/catalog is selected before indexing. |
| Every supplied provider with that same exact E1 identity | Eligible for structural/support/binding checks under its own immutable selection. Equal selectors alone do not establish contract or runtime compatibility. |
| Provider `trrm/1-draft-layout-queries-1` or another known schema | Unsupported by this initial operation, including apparently unchanged metadata-only bytes. Structural support in another operation is not this directed interoperability case. |
| Unknown, omitted, differently cased or composed selector | Reject before indexing, including an otherwise unused supplied provider. No default or retry decoder. |

Supporting B-to-E1 declaration-only providers could be a later bounded
interoperability decision. It would need a concrete unchanged contract
closure and checking rules; exception-role declarations, witnesses or
Bodies cannot silently cross without their required 21/1 meaning. This
proposal does not select that alternative or a profile powerset.

A provider containing only unchanged ordinary contracts may deliberately
produce an E1-schema artifact. It does not require 21/1 merely because
E1 was selected if it retains no E1-governed uses. Conversely every Module
retaining an exception role/view/witness or stream directory retains E1's
own 21/1 requirements, even when unused. Neither changing a selector nor
equal `(1,0)` bytes is a typed conversion or proof of that obligation.

Every provider Module selection remains explicit: exact Module identity,
actual revision/snapshot and containing artifact/schema. There is no first
Module, provider search, filename-derived identity or two-revision winner.
Co-located Modules share their artifact's one schema; separate artifacts
do not splice their directories or rows into the root decoder. Mixed
schemas within one artifact, unselected F/S/A2EU/RootPin/general-predicate/
enum/private uses and unsupported required contracts reject before use.

Packing selects one complete destination schema. It may proceed only after
typed decoding, support and binding, while preserving every retained
obligation. No provider-schema rule adds a converter, compatibility alias,
fallback, E1 inference from Feature 21 or native/private escape route.

## Replacement, rewriting and runtime origins

A compatible replacement selects another explicit actual revision under
the original Module identity and preserved keys. Resolve identity first
and recheck the full directed relation against each **original** expectation,
the originating Core context and every stronger fact actually used.
Changing to another Module identity is a different selection, not a
compatible replacement discovered by structural comparison.

Preserve exact nominal owner/formal/type identity, common-view construction,
self and declaring-member identities, callable contracts, representation,
backing/rooting, failure categories/phases and cleanup/publication obligations.
Export aliases that once selected one definition must not split retained
equality into different definitions. Private Body, region, row and code
indexes may change; matching them is neither required nor sufficient.
Different actual implementations/defaults are permitted only by the
supported directed witness/contract comparison and retained target facts.

Catch elimination or specialization using positive **or negative**
type/witness facts retains those dependencies. If the exact selected
schema cannot represent a surviving stronger obligation, decline the
optimization/re-encoding rather than publish weaker metadata. Cache reuse
requires unchanged owned inputs, schemas, using-Module/Core contexts,
selected revisions, substitutions, contracts and target/runtime facts.

Shared traversal and remapping must visit protected code, catch types,
filters, handler bindings/bodies and finally programs, including dispatch-only
entries. Also visit implicit `U` dependencies, each original expected
interface/class/payload graph and the actual witness/member/provider graph.
Normal CFG successor traversal alone is insufficient.

A meaning-preserving Standard rewrite remaps Module partitions/cursors,
ModuleRef/Import/Export/DefId references, TypeSpecs and their complete
arguments, GenericParam/self ownership, interface and witness binding rows,
Body TypeMap/Home/catch indexes and all ordinary E1 directory references.
Rebuild affected row/record offsets and enclosing extents using existing
canonical rules, not byte substitution or a Core-first row convention.
Clone contextual structural rows when their imports/formals resolve
differently in different using Modules.

A checked pack may replace an Import use with a direct provider TypeDef.
Elide physical expectation/import rows only when no independently
replaceable obligation survives, or its complete original meaning remains
in an already admitted required representation. Otherwise retain them as
semantic dependencies even if code uses the direct definition. Clearing
`ModuleImport`, re-exporting a proxy or keeping only the replacement's
contract loses the expectation; this proposal supplies no hidden registry
to recover it.

JIT and AOT dispatch use checked complete nominal/type/witness
correspondence and actual object correlation, never display names, layout
or raw row equality across artifacts/revisions. Logical compatibility does
not itself supply an independently replaceable native failure/object ABI.
Require the actual admitted call, backing, event and target runtime
contracts before crossing or publishing code.

Live objects/views/events and pending causes retain their actual class,
Core, witness/default/implementation, cleanup, artifact and code/load
providers through their last use. Keep the original operation address and
instruction mapping with that actual code instance. New bindings cannot
restamp an old event as originating in the replacement or release its old
provider. Semantic satisfaction alone does not authorize live migration,
hot unload, stale native code or outward native exception propagation.

## Proposed owner deltas and implementation dependencies

These are integration instructions **if this proposal is adopted**, not
edits made by this document:

| Exact document owner | Proposed delta |
|---|---|
| [Local failures §1 and §4](../specs/language/local-failures.md) | Define the profile-supplied exact exception-interface/payload anchors and derived common view; replace the unspecified registration mechanism with ordinary checked binding. Preserve every source/member/event/catch behavior and full source identity. |
| [Library identity §1](../specs/libraries/adopted-inventory.md#1-status-and-identity), [complete types](../specs/language/types-and-normalization.md#complete-type-terms), [witnesses](../specs/language/interface-witnesses.md#nominal-conformance-and-member-identity) | Cross-link the exception anchor mechanism and originating context. Clarify that the common view is not a third declaration; preserve existing authority, substitution and projection rules. |
| [IL metadata: logical tables and identity invariants](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics) | Add this exact Core selected-use binding application and implicit dependency retention using existing rows/forms. Change no payload, namespace, bounds or target set. |
| [Logical failure records](../specs/representation/logical-records.md#local-failure-eligibility-and-catch-inputs-encoding-pending), [instruction owner](../specs/il-instruction-set.md) | Complete only the Core identity/evidence-selection deferral and `unreachable`'s implicit exact-type dependency. Leave region/event/runtime and unrelated generic/publication gates with their existing owners. |
| [Binding and replacement](../specs/representation/binding-and-replacement.md), [admission](../specs/representation/requirements-and-admission.md), [reader trust](../specs/representation/reader-trust.md) | Apply original Core selections, complete witness comparisons, boundary-family equality and retained stronger facts. Keep mandatory binding/support checks separate from producer Body truth. Preserve the general independent-schema permission. |
| [E1 proposal](standard-trrm-register-body-exception-regions.md#exact-schema-composition-and-prerequisites) | Supersede only its unchosen exact-Core mechanism with this context/procedure. Incorporate the explicit initial E1-only provider restriction into its prerequisites/remapping boundary; retain all current allocations and noncomposition exclusions. |
| [Standard artifact/compilation APIs](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.StandardConsumption.cs), [TRRM CLI](../../sources/terrumc/Program.TrRmExecution.cs), [CLI protocol](../specs/cli-protocol.md) | Add the proposed nonserialized per-Module Core identity selection input and repeatable `--trrm-core-exception-anchor` transport. Validate roles, uniqueness, using Module and selected provider/key before affected use. Provider artifact schema remains the separate exact `TrRmStandardArtifactInput` selection; no identity is inferred from it. |
| [Modules](../specs/modules.md), [JIT](../specs/jit.md#replacement-and-code-lifetime), [native lifetime](../specs/native.md#runtime-and-code-lifetime) | Apply original/actual Core/class/witness/code lifetime retention without choosing a dispatch ABI, deployment format or servicing protocol. |
| [Binding cases](../specs/conformance/il-binding-and-replacement.md), [local-failure cases](../specs/conformance/source-local-failures.md) | Add the identity, schema, substitution, replacement and lifetime cases below after adoption; distinguish logical requirements from implementation/runtime evidence. |

Implementation needs the following coherent handoff, without changing the
accepted consumer-trust boundary:

1. **Source binding and reference projection:** explicitly publish/select
   the public Core TypeDefs by real export keys; attach checked originating
   Module identities, contexts, original expectations and actual provider
   graphs to interfaces, exception classes and payload uses. Extend ordinary
   reference/origin plumbing, not name recognition or native enum admission.
2. **Shared bound-tree traversal/remapping:** preserve all handler/implicit
   dependencies and hidden-self/witness correlations through adapters,
   copies, normalization and transformations.
3. **Mandatory verification:** compose structural directory/type/witness
   reading with the exact context/provider checks before selected use.
   Reconstructed metadata does not get optional-only Core identity checks.
4. **Standard persistence/provider binding:** carry ordinary expected
   TypeDefs, Imports/Exports, complete TypeSpecs/witnesses and 21/1 closure;
   retain selections as required binding inputs, including bodyless and
   source-free paths. Implement legal alias/multi-view bookkeeping for E1
   without importing an A2 profile. Reject a missing supported codec.
5. **JIT/AOT dispatch and lifetime:** consume the checked binding plan and
   exact actual evidence, retain all provider/code dependencies, and qualify
   supported source-free and replacement paths independently.

No A2EU, F, S, RootPin, general predicates, enum, scalar-frozen or private
profile composition is a prerequisite supplied by this proposal. General
exception factories/member publication, owning payloads, generic witness
projections and runtime support remain their independent gates; identity
completion is not completion of the E1 milestone.

## Candidate cases and remaining decision

These are proposed acceptance cases, **not tests executed here**. Use public
compiler/Standard APIs and independently supplied artifacts; do not require
a new internal-access grant or a semantic Body audit to check identity.

| Case | Required candidate outcome |
|---|---|
| Exact selected Core exports with non-name-derived keys; unrelated synthetic exception classes | Accept complete supported declarations and exact self witnesses. Neither motivating sample names nor literal Core package names select identity. |
| Missing/unauthorized/partial using-Module context | Decline affected Core/event use, including untyped `unreachable`; structural-only inspection may still succeed. |
| Same Core display names/signatures in another Module or at the wrong key | Reject anchor mismatch, even with the correct witness implementing class or identical payload shape. |
| Wrong self/interface/witness | Reject wrong InterfaceDef owner/self formal, nonclass implementer, different complete class argument, wrong declaring requirement, incomplete bindings or incompatible selected implementation contract. |
| Common view | Accept exact same-anchor `IException<_>` with per-object closed evidence; reject `IException<void>`, null/missing argument, another nominal common type, uncorrelated witness or hidden-type inference from another value. |
| Unreachable | Accept exact selected zero-data Copy payload for typed catch/Throw/implicit operation; reject unrelated zero-data Copy structs, enum/class substitutions and a nonzero handler witness. CommonObject does not match it. |
| Import/Export boundaries | Reject missing providers/keys, wrong DefId kind, nonpublic exports, authored implementations in expectations, proxy re-export and ModuleRef target/runtime mismatch. |
| Module/row/String permutations | Preserve exact identity across non-first selected Modules, independent row layouts and equivalent decoded String encodings; reject casing/normalization changes and duplicate actual Module revisions. |
| Aliases and several retained expectations | Check every view; accept keys resolving to one actual TypeDef only while preserving retained equality and all contracts. Reject conflicting mappings, duplicate triples and weakest-view substitution. |
| Cross-provider class and Core dependencies | Accept an exception class in a separately selected E1 provider bound to the same exact Core family. Reject a different Core family across an admitted propagation boundary; no event conversion. |
| Explicit schemas | Accept eligible explicitly selected E1 providers after checks. Reject omitted/unknown/case-changed selectors and all initial non-E1 providers, including B metadata-only and unused supplied artifacts. Reject mixed-schema packing and Feature/row sniffing. |
| Core selection transport | Accept one record with both exact anchor identities per using Module with retained E1 exception/Core use; reject missing, duplicate, malformed or conflicting selections, unknown roles, absent provider Modules and ExportKey mismatches before use. Omit the record only when the Module retains no Core exception role, view, witness or implicit Unreachable dependency. |
| Compatible replacement | Accept an explicitly selected revision preserving original Module/keys, owners, complete Core/class/witness contracts, stronger facts and runtime obligations despite changed private Bodies/rows. This is binding evidence, not automatic live replacement. |
| Incompatible replacement | Reject removed/rebound keys, split aliases, changed arity/self/type/representation, added caller requirements, changed failure/cleanup guarantees or loss of a relied-on positive/negative witness fact. |
| Packing/remapping | Preserve all contexts and nested/implicit dependencies through import-to-direct rewriting and contextual row cloning. Reject a rewrite that discards surviving original expectations or changes an E1 meaning to fit another schema. |
| Old event during later provider selection | Preserve its actual code/type/witness/cause providers and original instruction mapping through final use; do not relabel it or unload its dependencies. Native qualification must exercise the lifetime claim, not merely emit an image. |
| Support and truth boundary | Missing required publication, generic, event/root, dispatch or ABI support declines use. A structurally valid producer lie is not required to become a consumer semantic-reproof test. |

The outstanding approval is **ordinary bindings with profile-supplied
anchors, a derived common view, the nonserialized role-record/CLI transport,
and the initial E1-only provider rule**. Concrete production Core Modules/
keys must be supplied by the authorized profile. The API record and CLI option
are proposed here, not adopted. No unresolved numeric allocation or new
identity codec is required for this recommendation. Implementation and runtime
qualification remain separate work, and this proposal remains unapproved and
provisional.
