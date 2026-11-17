# Standard TRRM source enum identity inputs

**Status: unapproved candidate; provisional direction only under the user's
explicit missing-contract workflow.** This selects a producer input contract,
not normative approval, compiler support or execution evidence.

## Decision and scope

Extend the existing `TrRmSourceTypeExport` record with one explicitly supplied,
complete ordered enum-member-key array. Reuse its existing five-argument
`EmitStandard` overload. A separate public enum assignment collection,
identity-input factory or enum emitter is unnecessary.

Apply this completion only to the already selected
**`trrm/1-draft-nominal-enums-1`** carrier. Preserve
[nominal enum persistence](nominal-enum-persistence.md)'s complete, public,
namespace-level, nongeneric, primary **int32** declaration boundary, including
checked composed parts with one primary. Wider, internal and file-local enums
remain valid source where otherwise admitted, but unsupported by this carrier.
Retain the complete member snapshot, not only members used by a Body.

This narrowly extends the source-map applicability restriction in
[A2 source type identity](standard-trrm-source-copy-type-identity.md#proposed-api-and-source-selection)
to this exact enum schema. Existing struct mappings, their supported schemas
and restrictions remain unchanged. The
[source identity proposal](source-nominal-enum-identity-inputs.md) still owns
source-local handles and private projection; the
[source schema selection proposal](standard-trrm-source-schema-selection.md)
still owns exact emitter selection and defaults.

There is no new wire field, schema identity/composition, ID, alias, decoder
fallback, source syntax, Flags annotation schema, enum operation, target rule,
Core designation or private-to-Standard conversion.

## Evidence and owners

The [specification index](../specs/README.md) leads to
[metadata identity and keys](../specs/il-metadata.md#identity-and-contract-invariants),
[binding/replacement](../specs/representation/binding-and-replacement.md),
[admission](../specs/representation/requirements-and-admission.md),
[reader trust](../specs/representation/reader-trust.md),
[canonicality](../specs/representation/encoding-canonicality.md),
[format versioning](../specs/representation/format-versioning.md) and
[persistence separation](../specs/representation/persistence.md).
These preserve producer-chosen keys, exact nominal ownership, original
expectations and safe consumption. They do not assign keys to source members.
No accepted text is replaced by this bridge.

Current source observations, not newly run tests:

| Inspected owner | Missing input or preserved boundary |
|---|---|
| [`TrRmSourceTypeExport` and `EmitStandard`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.cs), [`source export writer`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.FixedArrayCopyWriter.cs) | The generic-named record supplies Namespace/Name/ExportKey only. Its nonempty-map route selects supported struct schemas, not NominalEnumsDraft. `BoundSourceTypeDeclaration` and its checked declaration snapshot are currently struct-specific; their names do not establish an enum inventory. |
| [`BoundEnumTypes`](../../sources/Terrum.CodeAnalysis/Semantics/BoundEnumTypes.cs), [`source binding`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.cs) | Source enums and members have actual source-local handles, complete ordered declarations and exact integer values. A Source member's DeclarationKey is absent, not a producer key. Source identity alone does not establish local ownership in a program containing references. |
| [`PrivateModuleIdentityInputs`](../../sources/Terrum.CodeAnalysis/Modules/PrivateModuleIdentityInputs.cs) | `PrivateSourceEnumDefinitionAssignment` explicitly captures declaration/member keys for one checked program, but its local-key/export rules and private projection do not authorize Standard TypeDef exports. |
| [`RegisterWriter`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.RegisterWriter.cs), [`NominalEnumsWriter`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.NominalEnumsWriter.cs) | The declaration writer requires `GetCoreIdentity` and explicit member DeclarationKeys. Source identities reject. Body helpers construct a declaration-only BoundProgram, so an input bridge must preserve or explicitly rebase its original program association rather than lose it at that helper boundary. |
| [`CLI source emission`](../../sources/terrumc/Program.TrRmExecution.cs) | `compile-trrm` passes the exact selection to the four-argument emitter. It supplies no source declaration/member export map; schema selection and Core manifests cannot fill that absence. |
| [`active enum tests`](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmEnumTablesTests.cs), [`test project`](../../tests/Terrum.CodeAnalysis.Tests/Terrum.CodeAnalysis.Tests.csproj) | Existing Core-identity and independent-artifact cases cover bounded declarations, constant returns, identity methods, provider snapshots and re-emission. They do not supply a Source-to-Standard member-key assignment. The project does not exclude this test file. |

Thus the gap is a Standard producer-input completion, not a missing enum
carrier or permission to infer a Core identity.

## Alternatives

| Unapproved option | Assessment |
|---|---|
| **Extend the generic export map — recommended** | One existing declaration selector and ExportKey, plus the enum's required ordered member keys. Reuses list capture and Module-wide export collision checking without a second source-emitter overload. Kind-specific validation remains explicit; the struct-only internal inventory still needs an enum adapter. |
| Separate complete `TrRmSourceEnumExport(Namespace, Name, ExportKey, MemberDeclarationKeys)` input | Coherent, but needs another overload or parallel collection and shared cross-collection collision/retention rules. Splitting type and member assignments into two collections adds an unnecessary join and orphan-group policy. Neither is needed for this bounded completion. |
| Consume the private assignment unchanged | Its snapshot checks are reusable machinery, not a Standard publication contract. It has a private LocalDeclarationKey/IsExported policy and private key bounds; treating it as Standard authority conflates carriers. A host may deliberately resupply the same selected strings through the Standard input, never implicitly convert private origins. |
| Generate keys from names, values, ordinals or handles | Not selected. It violates the existing producer-assigned-key direction and cannot preserve arbitrary selected provider keys. |

## Exact candidate API

In `Terrum.CodeAnalysis.Modules`, retain the record's three positional
parameters and add this init-only property:

```csharp
public sealed record TrRmSourceTypeExport(
    string Namespace, string Name, string ExportKey)
{
    public ImmutableArray<string> EnumMemberDeclarationKeys { get; init; }
}

public static byte[] EmitStandard(
    BoundProgram program,
    string moduleIdentity,
    string schemaIdentity,
    uint moduleTargetRequirement,
    IReadOnlyList<TrRmSourceTypeExport> typeExports);
```

The method signature already exists; this is its scoped extension, not a new
overload. Existing three-argument record construction and deconstruction
remain unchanged. A default `EnumMemberDeclarationKeys` means **no enum
assignment**. A Source enum requires a nondefault array, including an explicit
empty array for an empty enum. A non-enum mapping requires the property to be
default; even a nondefault empty array is invalid there, not silently ignored.
The map has no access, underlying-type, Flags or Blittable override.

For a nonempty `typeExports` list, the five-argument overload accepts
NominalEnumsDraft only when every entry selects a Source enum and supplies a
nondefault complete member-key array. The list cannot mix enums with
structures: this exact candidate has no struct TypeDef route. Existing
fixed-array/local-aggregate selections retain their current struct-only
behavior and require the member-key property to be default. No other schema
accepts enum assignments. The property does not select a schema, add enum
support to A2 or select an enum composite. Empty lists retain the four-argument
operation, including its failures for missing required inputs. The existing
Development default and four-argument Standard behavior are unchanged.

## Validation and exact-program capture

1. **Select and capture.** Apply the existing exact schema/target argument
   rules and copy the outer list, entries and every supplied member-key array
   before production. Validate every submitted mapping, including one whose
   declaration would later be pruned. No first/last-wins or sparse overlay is
   selected.
2. **Select a declaration, not an identity string.** Namespace is the exact
   full decoded source namespace, possibly empty; Name is the exact simple
   decoded identifier. Compare ordinally without normalization, trimming,
   using/alias lookup or dependency search. Resolve exactly one positively
   locally authored Source enum in the supplied BoundProgram's checked local
   declaration inventory. Require the actual handle, composed parts and
   declaration snapshot to agree with that program. A reference's Source
   handle, absence of ModuleImport, equal names or an equal declaration shape
   does not establish local ownership. Core, Private, imported and already
   published declarations are not new Source mapping targets.
3. **Assign the TypeDef key.** ExportKey is explicit producer input: nonempty
   well-formed scalar text without zero scalars, under the Standard String/key
   rules. Preserve it exactly; it need not be an identifier or resemble a
   source name. It is unique across every Export of the output Module,
   including existing callable and Core-backed exports. Repeated declaration
   selections or export keys reject even when identical. This bounded input
   assigns one key per new declaration and introduces no export aliases.
4. **Assign every actual member.** The array length must equal the complete
   checked member count, in composed compiler-file/source declaration order,
   not primary-first or key order. Each position associates the actual member
   handle in that snapshot with the explicit key supplied at that position.
   The position selects an association; it never generates the key or becomes
   durable identity. Require correct owner handles, unique actual members and
   one nonempty, well-formed, zero-free scalar-text key per member. Keys are
   unique **within that enum**, not across enums or Module exports. Equal-valued
   members remain distinct; equal keys in different enum owners remain legal.
   Member keys are not independent exports. Apply Standard String/resource
   bounds, not the private carrier's 256-byte rule.
5. **Preserve eligibility and facts.** Require the complete public,
   namespace-level, nongeneric int32 declaration with one checked primary.
   Validate member values before any checked int32 projection; never truncate
   a wider source base. Preserve namespace/name, source access, all member
   names/order/values, actual IsFlags and actual target-specific Blittable
   commitment. Public visibility and int32 storage do not manufacture Flags,
   Blittable or a source Layout annotation. Unrepresentable retained facts
   decline; the map cannot clear them to obtain output.
6. **Require total retained closure.** Every retained fresh local Source enum
   needs its own mapping and full member array, even when only one member,
   an unnamed value or an uncalled signature uses it. Its explicit Export is
   itself a retained root. This carrier does not add an unexported-source-enum
   identity mode. Missing maps do not request inference; omitting an entry
   cannot silently erase an otherwise retained declaration. Unsupported
   declarations/operations cannot be hidden by choosing a smaller list.

Construct a compiler-owned immutable capture, associated with the **exact
BoundProgram object and checked binding snapshot**, before assigning rows.
Its required contents are the local enum type/handle and positive ownership,
the complete original declaration/member snapshot, output Module and exact
schema, TypeDef ExportKey, and every ordered `(actual member handle, key)`
association. The source program remains unchanged; Source members do not gain
Core tokens or mutable DeclarationKeys.

The current struct-specific declaration inventory cannot be used unchanged.
Retain corresponding positive local enum provenance from source binding and
adapt it to the shared export-map validation. Share key/collision machinery,
not a fabricated struct or Core declaration.

Helper-created programs and supported transformations explicitly rebase this
capture and all type/member associations together. A cached capture cannot
apply to a different program, including a `with` clone, merely because names,
indices or values match. Changed source snapshots require fresh validation;
unchanged handles alone do not excuse changed member/access/representation
facts. Resubmitting the public string selectors to another compilation is a
new checked invocation, not reuse of an old captured identity. The producer
host stores and resupplies its chosen keys across builds; this proposal
defines no key-generation or key-storage service.

## Publication, imports and re-emission

Emit each admitted local source enum through the existing TypeDef/EnumDef/
EnumMemberDef/Export path, using its supplied type key and complete ordered
member keys. Retain the owner's existing Layout, Feature 18/1 over scalar
11/1, conditional native requirements, typed constants and exact nominal
types. This bridge applies to the selected emitter's already supported
declaration, constant-return and identity-method shapes; it adds no Body
operation. Unsupported retained content still rejects the whole operation.

Imported declarations retain their selected provider Module/export/kind,
unambiguous owned provider snapshot and its independently selected schema,
original using-Module context and ModuleRef target/runtime expectations,
complete enum/member snapshot and stronger relied-on facts. A new local map
cannot restamp them or replace originals with the selected provider's current
view. An external unmapped Source declaration first needs its producer's
checked Standard publication projection; the consumer cannot mint its keys.
Existing actual-Core paths retain their explicit keys/origins and independent
registration rules.

On decoding or reference projection, retain `(selected T, member key)` and
all original expectations before source handles are discarded. Any internal
adapter must preserve that owner: collapsing members to `(Module, key)`,
requiring Module-wide member-key uniqueness or fabricating Core designation
is not an implementation of this contract. Ordinary source lookup still
uses the original namespace/type/member names, not the export/member keys.
Consumers perform structural/support/binding checks; they need not re-evaluate
source initializers or semantically re-prove producer Bodies.

Already retained local Standard publication maps, including admitted aliases,
are authoritative on source-free re-emission. An empty/omitted new map
preserves them without inference or original source files. A nonempty entry
cannot target an existing publication, even if it repeats the same keys.
Assignments to fresh local Source declarations do not overwrite separately
retained imported/Core facts. Relabeling an identified Module or replacing its
retained map needs a separate checked transformation, not a different emission
argument.

Use the [enum owner's transformation rules](nominal-enum-persistence.md#consumption-materialization-and-transformation):
rebuild affected Module partitions, Strings, TypeDefs, enum/member ranges,
Layouts/TargetProfiles, Imports/Exports, TypeSpecs/signatures, frozen
source/node/Blob references and Body/source-reference maps. Physical movement
changes none of the keys, owner/member associations or original expectations.
Keep every independent diamond expectation; replacement compares the complete
original member key/name/order/value and declaration facts after resolving the
selected owner. No byte substitution, integer retagging or source-name rebinding
is a preservation algorithm. Validate the final artifact under the same exact
schema before returning its complete byte array.

## Failures and CLI boundary

Use the existing Standard API failure families; no diagnostic or runtime
failure identity is added.

| Condition | Required result |
|---|---|
| Null top-level argument/list | `ArgumentNullException` naming that argument. |
| Null entry/key, malformed selector/key text, missing/ambiguous/wrong-owner selector, duplicate declaration/export/member key, default/wrong-length enum array, or member array on a non-enum | `ArgumentException` naming `typeExports`. Selecting a nonpublic/file-local or non-Source declaration as a new public local export is also invalid mapping input. |
| Invalid program/declaration snapshot, stale capture or inconsistent owner/member association | `ArgumentException` naming `program`; no previously checked capture repairs it. |
| Attempt to replace a retained publication via a new mapping | `ArgumentException` naming `typeExports`. |
| Missing map for retained Source closure; valid wider-base or otherwise unsupported retained contract, schema operation, target/runtime or unavailable required provider-origin support | `NotSupportedException`, identifying the missing/unsupported obligation. |
| Malformed selected artifact or incompatible selected provider binding at a consumer boundary | Existing invalid-data failure, `InvalidDataException`; do not synthesize a replacement expectation. |

Existing invalid schema-token and target-value failures remain unchanged.
Resource failures remain failures. Return no partial artifact/reference and
never retry Core, private persistence or another Standard schema.

No CLI transport is selected here. `compile-trrm` continues to use its existing
four-argument route and cannot claim new Source enum publication without a
separate explicit mapping transport. A selected enum schema or Core manifest
must not generate these missing keys. Production failure preserves any output
destination under the existing command's publication rules.

## Integration and acceptance

On adoption, the metadata writer-policy discussion is the accepted owner for
the scoped authoring bridge; the generic source export/API proposals own the
record extension and exact-schema applicability. The nominal enum proposal
continues to own all wire/member/binding semantics. No registry or source/
runtime/library/target contract needs amendment for this input completion.

Implementation must carry local provenance and the checked projection through
binding, shared verification/rewriting, helper selection, references and
source-free lowering wherever this route is supported. Preserve imported
provider origins and original expectations independently of the new map.
This document records no support-inventory completion.

Required future tests use public compiler APIs and independent selected-schema
artifacts, extending the existing enum and source-export areas. They are not
results of this documentation change.

| Family | Required cases and outcome |
|---|---|
| Producer/source | Compile unrelated public int32 enums without Core registration; explicitly publish keys unrelated to names/values/order. Include empty and duplicate-valued enums, int32 extrema, composed parts with primary not first, and unused members. Check complete emitted snapshots and supported constant-return/identity-method typing, not just declaration counts. |
| Assignment validation | Default versus explicit empty arrays, missing/extra member keys, null/empty/NUL/malformed text, duplicate declarations/keys and wrong selectors reject as specified. Same member keys in different enums, or text also used as an export key, remain legal; cross-kind ExportKey collisions do not. Include ordinally distinct Unicode and supported nonidentifier keys without importing private key caps. |
| Ownership/snapshots | Imported Source/Core/Private declarations cannot be mapped as local. Missing positive local provenance, changed member/access/value/representation snapshots, stale helper captures and invalid program rewrites reject; an explicit checked remap of unchanged declarations preserves every association. |
| Source/carrier boundary | Otherwise valid wider/internal/file-local enums retain their source-analysis result but cannot be smuggled onto this carrier. No width truncation, public-access promotion, manufactured Flags or inferred Blittable. Preserve actual Flags/Blittable facts through existing checked adapters/independent fixtures without introducing annotation syntax. |
| Schema/defaults | Only the exact nominal-enum selection admits the new enum assignment. Baseline/A2/private/altered tokens do not select it; struct mappings with default member property preserve existing behavior. Empty maps never repair missing source keys or erase retained publication. CLI has no name-derived mapping and preserves a sentinel output on failure. |
| Consumer/providers | Independently decode and bind source-produced declarations without source files or Core authorization. Check exact provider Module/key/kind/revision, original target/runtime expectations and every member snapshot. Wrong/missing providers, changed unused members and independent conflicting diamond expectations fail; a consumer cannot provide new producer keys. |
| Re-emission/remapping | Source-free load/re-emit preserves type/member keys, owner identity, Flags/access/representation and frozen nominal values, including two enums reusing member keys. Canonical fixtures retain exact re-emission where already promised; independently permuted rows/String encodings preserve meaning while rebuilding ranges/references/maps, not by requiring original bytes or substituting tokens. |
| Replacement/retention | Physical relocation with unchanged contracts succeeds; changed names/base/member keys/order/values/Flags/access or retained representation fails every affected original expectation. Omitted maps retain loaded exports/aliases; nonempty maps cannot rekey them. Unsupported closure declines rather than returning declarations-only success. |

No assignment-shape or key-inference decision remains open within this
recommendation. Actual keys are explicit producer inputs. Formal approval,
implementation and qualification remain outstanding; broader enum operations/
carriers, CLI transport, provider authentication and Core/schema designation
remain separate gates.
