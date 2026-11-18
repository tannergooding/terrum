# Source-free G1 generic-method export selection

> [!IMPORTANT]
> **Status: unapproved; provisional direction under the user-authorized
> missing-contract workflow.** This document recommends a public selection
> contract. It does not approve or implement an API, change a specification
> or support inventory, or report qualification results.

## Decision and scope

Recommend a distinct **`EmitStandardReemittingGenericMethods`** entry point,
with an immutable, exact-program `TrRmRetainedGenericMethodExport` record.
Each record explicitly selects one already checked, source-free, exported
generic MethodDef and one output ExportKey. Multiple records can assign
aliases to that same definition. There is no implicit selection of imports.

The name above is an explicit new recommendation, **not an existing API or
authority derived from the removed speculative implementation**. Implement
the operation through the shared Standard emission machinery, not a second
generic compiler, resolver or binary copier.

This proposal owns only that selection and publication boundary. Reuse
[G1](standard-trrm-generic-method-call-body-encoding.md), its exact
[label-bearing](standard-trrm-g1-interface-parameter-labels.md) and
[receiver-access](standard-trrm-method-receiver-access.md) agreements where
explicitly selected, and the existing
[result-dependency direction](standard-trrm-g1-generic-result-storage-dependencies.md).
It adds no schema identity, Feature, section, opcode, Body record, source
syntax, CLI option or selection-file grammar. It does not choose another
default/effect/dependency encoding, generic execution strategy, target,
runtime policy or released-schema compatibility rule.

Successful emission requires the selected schema's complete supported
declaration, Body and dependency closure. A selector supplies none of a
missing codec, provider contract or execution facility. In particular, this
is not permission to erase a fact that the current bounded G1 reader or
writer cannot retain.

## Established boundary

The [specification index](../specs/README.md) leads to the controlling
[metadata](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics),
[binding](../specs/representation/binding-and-replacement.md),
[reader-trust](../specs/representation/reader-trust.md) and
[exact-schema](../specs/representation/format-versioning.md) rules.
The missing decision is a host API contract, not missing G1 wire semantics.

| Inspected owner | Relevant boundary |
|---|---|
| [`TrRmSourceGenericMethodExport`](../../sources/Terrum.CodeAnalysis/Modules/TrRmSourceGenericMethodExport.cs) and its [input proposal](standard-trrm-source-generic-method-export-inputs.md#exact-declaration-and-snapshot-association) | Select a public local source declaration by exact `BoundProgram`, source-input ordinal and full declaration span. Loaded artifact definitions are deliberately ineligible; fabricated source coordinates cannot select them. |
| [`EmitStandard`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.cs) | The generic source overload accepts explicit source mappings. It has no retained generic MethodDef export-selection input. Existing class-export overloads also occupy the seven-argument list-shaped surface. |
| [`GenericMethodSourceReader`](../../sources/Terrum.CodeAnalysis/Modules/TrRmGenericMethodSourceReader.cs) | Projects root-owned exported MethodDefs separately from bodyless expected MethodDefs reached through Imports. It refuses exporting an imported expectation and retains selected provider Bodies for source-free use. |
| [`GenericMethodProviderBinding`](../../sources/Terrum.CodeAnalysis/Modules/TrRmGenericMethodProviderBinding.cs), [`bound origins`](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs) | Snapshot selected artifact bytes, calculate full-artifact SHA-256, and retain Module identity, exact schema, MethodDef DefId and export key. An Import binding separately retains its Import/ModuleRef and expected/provider views. These internal facts are not a public export configuration. |
| [`GenericMethodSourceWriter`](../../sources/Terrum.CodeAnalysis/Modules/TrRmGenericMethodSourceWriter.cs), [`nominal closure writer`](../../sources/Terrum.CodeAnalysis/Modules/TrRmGenericMethodNominalTypeWriter.cs) | Emit source-selected definitions and bounded MethodDef Import expectations, with checked TypeDef snapshots and original nominal expectations. The source writer still refuses inherited direct MethodDef targets; this proposal does not claim that closure path exists. |
| [Module inventory](../implementation/modules.md#definitions-and-scope), [CG-006](../implementation/conformance-gaps.md) | Source-free generic MethodDef re-emission remains open. Existing source-written forwarding/import regressions do not establish direct source-free re-publication. |

These are source observations, not newly executed validation. The active
no-overlap validation hold and disabled CA1851 setting remain unchanged.

## Alternatives

| Alternative | Assessment |
|---|---|
| Add a minimal `EmitStandard` overload extending the existing six-argument source route with a seventh `IReadOnlyList<TrRmRetainedGenericMethodExport>` | Coherent: use the same record and rules below, with explicit empty source lists for source-free use. It also exposes joint source/retained publication and collides with existing class-export overload resolution for untyped empty/null final arguments. Named arguments or typed lists can resolve that, but it couples this narrow decision to mixed publication. Not selected. |
| **Distinct explicit re-emission entry point below** | **Recommended.** One new record and one method distinguish copying a checked retained definition from assigning a source declaration's key. Existing overload resolution, source coverage and omitted-selection behavior stay unchanged. Share verification, graph remapping and writing with `EmitStandard`; the distinct name does not select a different wire format. |
| Automatically export every retained generic import, or preserve every originating method alias | Rejected. Reference availability and implementation dependency retention do not authorize a new output ExportKey or output definition. |
| Reuse source spans, method names, proof-function indexes or closed specializations | Rejected. None identifies the exact checked open artifact definition, and no selector can create missing source provenance or an open Body. |

Joint fresh-source/open-generic and retained-method publication is not selected
by the new entry point. The existing source API remains available unchanged;
this proposal does not add another combined options object or CLI transport.

## Candidate public API

Add these **proposed**, not currently available, declarations in
`Terrum.CodeAnalysis.Modules`; the method belongs to
`TrRmExecutableCompiler`. All arguments are required.

```csharp
public sealed record TrRmRetainedGenericMethodExport(
    BoundProgram Program,
    string ProviderIdentity,
    string SchemaIdentity,
    ImmutableArray<byte> ProviderArtifactDigest,
    uint MethodDefId,
    string OriginExportKey,
    string ExportKey);

public static byte[] EmitStandardReemittingGenericMethods(
    BoundProgram program,
    string moduleIdentity,
    string schemaIdentity,
    uint moduleTargetRequirement,
    IReadOnlyList<TrRmRetainedGenericMethodExport> genericMethodExports);
```

`BoundProgram` and the collection types are the existing C# host types.
This is a semantic emission operation, returning one complete owned byte
array; it does not write files, execute code or load native providers.

For this operation, destination selection is limited to these existing whole
agreements, each requiring its own complete operation support:

| Agreement | Exact `schemaIdentity` |
|---|---|
| G1 | `trrm/1-draft-generic-method-calls-1` |
| G1L1 | `trrm/1-draft-generic-method-calls-parameter-labels-1` |
| G1L1R1 | `trrm/1-draft-generic-method-calls-parameter-labels-1-receiver-access-1` |

Each selected root's `SchemaIdentity` must equal the destination selection.
This API does not convert G1 into G1L1, invent missing labels, compose C1/CG1
or derive a schema from a token fragment. Independently selected dependency
schemas remain subject to the existing reader/binder's support gates; this
proposal does not widen those gates or infer a dependency's schema from the
destination. Equal `(1,0)`, Features or digest bytes select no decoder.

### Exact checked origin

| Field | Required interpretation |
|---|---|
| `Program` | Reference-identical to the supplied final checked `program`, not merely record-equal. A newly bound, rewritten or `with`-copied public program needs a new association. |
| `ProviderIdentity` | Exact owning Module identity of the originating actual MethodDef. This is the provider's Module text, not an assembly name, reference-list ordinal, path or separate vendor namespace. |
| `SchemaIdentity` | Exact whole-artifact schema used when that origin was checked. Compare ordinally without normalization or fallback. |
| `ProviderArtifactDigest` | Nondefault immutable array of exactly 32 raw SHA-256 bytes over the complete owned artifact snapshot that supplied the MethodDef. Compare byte sequences, not `ImmutableArray` storage identity or record equality. |
| `MethodDefId` | Full artifact-local DefId, high byte `0x16`, nonzero low-24-bit MethodDef row. The row must belong to `ProviderIdentity` in the pinned snapshot. It is not a MethodMap index, Import token, MethodSig row or durable cross-revision identity. |
| `OriginExportKey` | Explicit alias in that checked origin's Export table selecting exactly `MethodDefId`. It is not the declaration's source/name string. |
| `ExportKey` | One explicit output alias. Supplying the same text as `OriginExportKey` explicitly retains that alias; any other valid text explicitly renames this publication. No key is inferred. |

The whole tuple must match a **directly retained reference root's public,
producer-owned open MethodDef**, with its actual supported open implementation
and complete checked closure. A reference root is the Module whose exported
declarations were explicitly projected into this checked program, not every
provider visited while reconstructing one of its Bodies. This operation
selects only roots positively recorded by the checked Standard reference;
it does not compose additional roots or dependencies.

The reader/binder must retain positive root/publication provenance for this
check. Absence of an import marker alone is insufficient. Internally exposing
a provider Body through a `BoundGenericMethodImport` does not make its
consumer-local expected stub an exportable definition.

Reject selections of a private/internal declaration, nongeneric method,
closed specialization, signature anchor, bodyless imported expectation,
unretained root or transitive-only target. An external provider method may
become separately eligible only through an explicitly admitted root reference
and an explicit selection of its actual exported definition. Do not follow
an Import to promote its provider automatically.

Callers supply the origin fields from the selected input artifact/export
association and its bytes, not internal proof IR or source coordinates.
The existing public artifact, Module and Export readers expose these fields;
the selector requires no new enumeration or resolver API.
Those fields are assertions to cross-check against the program's retained
checked origin, never authority to manufacture one. No discovery, resolver
callback or source parsing API is introduced here.

Capture the list and immutable records before emission, validate every entry,
and resolve the origins against the original checked program before internal
rewriting. Carry those resolved associations through remapping. Neither a
later positional lookup nor structural record equality re-establishes the
association. The operation must not mutate caller collections, input bytes,
the program, reference objects or proof programs. Callers must not mutate
borrowed inputs concurrently with capture; later caller mutation cannot
change captured inputs or output.

### Staleness and replacement

The digest and DefId pin this request to a checked snapshot; they are not
nominal identity, authentication, semantic truth or a permanent ban on
compatible replacement. Selecting a changed artifact snapshot, including
compatible row relocation, requires a newly checked origin and a newly
associated record. Mutating an original caller buffer after snapshotting does
not change the retained origin. An old record does not retry lookup by key,
name, signature or former row.

For an external dependency, preserve the original expectation and apply the
existing directed binding rules to any explicitly selected replacement.
A compatible replacement can have another digest and different physical
rows. It must still satisfy every retained view and relied-on provider fact.
Rebinding invalidates affected cached associations/specializations; changing
only the public selector cannot authorize an incompatible provider.

The selection record and its digest are host inputs, not new serialized
records. Reloaded Imports use existing G1 identity and compatibility rules,
not an invented wire-level hash lock.

## Output identity, aliases and retained contents

`moduleIdentity` explicitly names the output Module and may differ from every
origin identity. A selected method becomes a local definition of that output;
this is an explicit declaration copy, not a provider-identity equivalence, and
does not retarget any existing Import. Preserve nominal identities and their
exact provider expectations through the existing TypeDef Import route; never
rekey a TypeDef or synthesize a type/field ExportKey to make the copy fit.
Reject ambiguous or incompatible closure rather than masking another
provider revision with the output identity.

| Situation | Required selection behavior |
|---|---|
| Several records select one origin DefId through checked aliases | Emit one MethodDef/Body and only the explicitly requested output aliases. |
| Duplicate output key or collision with another Module ExportKey | Reject; no last-wins behavior or input-order resolution. |
| Unselected root method or original alias | Do not publish it implicitly. Root-local methods required by a selected Body may remain implementation closure without gaining export keys. |
| External MethodDef Import used by a selected Body | Preserve its bodyless expectation, exact ModuleRef/key and binding obligations; do not copy the provider Body or redirect the Import. |
| Empty selector list or omission through an existing overload | Add no retained-method exports or Feature; preserve existing `EmitStandard` behavior and failures. |
| Fresh source open definition on this entry point | Refuse as unsupported; this API does not replace source mapping or completeness checks. |

Use the existing nonempty, NUL-free, well-formed scalar-text and UTF-8
capacity rules for identities and keys. Other program contents follow current
Standard retention/publication rules; this operation changes only the
explicitly selected retained-method publication set.

## Preservation obligations

Reuse [G1's source-free fixups](standard-trrm-generic-method-call-body-encoding.md#providers-source-free-remapping-and-lowering)
for complete signatures, defaults, constraints, effects, dependencies,
nominal closure, expected/provider views, Body maps and cross-table indexes.
Re-own copied MethodDef formals by owner and ordinal, preserve original
expectations and alias equality, and rebuild every schema-owned reference;
never patch row numbers or matching bytes. The selector adds no encoding or
permission to drop unsupported facts. Mandatory consumption, binding and
output validation remain required; unavailable complete closure refuses
emission rather than returning a lossy artifact.

## Failure and output preservation

| Condition | Required public result |
|---|---|
| Null top-level argument | `ArgumentNullException` naming that argument. |
| Null entry/field, invalid key/identity/digest/DefId, wrong program anchor, missing root membership or origin-tuple mismatch | `ArgumentException` identifying the selection argument. Includes stale/mismatched origins and duplicate/colliding output keys. |
| Empty/unknown/altered schema selection | `ArgumentException` for `schemaIdentity`; no default or alternate decoder. |
| Unassigned target requirement | Existing `ArgumentOutOfRangeException` behavior. |
| Known selection outside this operation, unsupported required closure, unsupported joint source publication or unavailable complete reader/writer support | `NotSupportedException`; identify the unsupported obligation rather than returning a lossy artifact. |
| Malformed retained artifact graph, missing/ambiguous required provider or incompatible selected binding | Existing `InvalidDataException` path; a malformed caller-supplied selector itself remains an argument error. |
| Verification, capacity or resource failure | Preserve the applicable existing failure category; no truncation, empty-success array or partial publication. |

Validate the entire requested publication set, including unused selections.
Return only after all selection, closure, binding, encoding and output
validation succeeds. Failure returns no array and mutates no input.
Existing `moduleTargetRequirement` and the program's runtime-policy gates
are unchanged; a selector neither weakens nor supplies them.

Because this API writes no files, it cannot create, truncate or replace a
destination. A host that publishes its returned array must retain the
existing stage-then-replace boundary: every preceding failure leaves an
existing destination byte-identical and an absent destination absent.
No new CLI behavior is selected here.

## Required public-API cases

These are proposed acceptance cases, **not executed results**. Use
`CreateStandardReference`, public compilation APIs/final `Compilation.Program`,
the proposed emitter and fresh public reference loading. Selected MethodDefs
must originate in `.trrm` input, not a newly authored forwarding method or
fabricated source mapping. Include independently constructed complete
artifacts, not only one writer's round trips. No test needs internal IR
access or a new friend assembly to make the public selection.

| Case | Required outcome |
|---|---|
| Positive selection and reload | Select an open root definition under an explicit key, including an uncalled method; emit, reload without producer source and bind multiple admitted closed instantiations. No native execution is implied. |
| Projection and aliases | Select only one of several root methods, request multiple output aliases for one DefId, and retain required local helper closure plus external bodyless Imports without exporting helpers, unselected methods or provider targets. |
| Invalid/stale selectors | Reject wrong program anchors, malformed/default digests, changed schema/artifact, wrong Module/DefId/ExportKey, private/nongeneric/closed/bodyless/transitive targets, duplicate keys and collisions with other Module exports. |
| Relocation and replacement | Fresh selectors from relocated artifacts re-emit with rebuilt indexes; stale tuples reject. Compatible dependency replacement preserves original expectations, while incompatible contracts and split aliases reject. |
| Cross-feature closure | Exercise the G1-supported signature/default/constraint/dependency and constructed nominal/ref/Span cases, including formal-owner remapping; explicitly refuse unsupported variants. |
| Identity and independent reload | Re-emit under the same and a new output identity, preserve exact nominal/provider identities, then load emitted bytes in a fresh program with only real retained dependencies. |
| Snapshot and failure atomicity | Caller mutation after capture cannot change output; invalid selection, unsupported closure, incompatible binding or capacity failure returns no artifact and causes no host destination replacement. |

Native JIT/AOT, target ABI and runtime evidence remain independently gated
by their existing owners. No build, test, restore or execution is performed
or authorized by this document.

## Integration after approval

Keep all following owners unchanged in this proposal-only change.

| Owner | Exact later integration |
|---|---|
| [Source schema selection — exact public library contract](standard-trrm-source-schema-selection.md#exact-public-library-contract) | Add the selected distinct API and link this record/selection contract. Preserve all existing emitter signatures, defaults and no-implicit-import-publication behavior. |
| [Source generic-method inputs — exact association and coverage](standard-trrm-source-generic-method-export-inputs.md#exact-declaration-and-snapshot-association) | Cross-link the separate retained-definition selector; keep source ordinal/span eligibility and fresh public-source coverage unchanged. No source map/CLI grammar is superseded. |
| [Selected-provider consumption — public API/admission](standard-trrm-selected-provider-consumption.md#exact-proposed-public-api) | Require retention of exact checked root origins and their root-versus-transitive role for this handoff. Preserve original expected/provider graphs and independent schema selections. Do not add a resolver or execution behavior. |
| [G1 — providers, source-free remapping and lowering](standard-trrm-generic-method-call-body-encoding.md#providers-source-free-remapping-and-lowering) | Link explicit public root/alias selection and its empty-selection rule. Preserve the existing wire, semantic and remapping obligations verbatim; do not treat this selector as completing unsupported closure. |
| [IL metadata — row semantics and identity invariants](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics), [binding/replacement — selected identities](../specs/representation/binding-and-replacement.md#1-scope-and-selected-identities) | Clarify that an explicitly copied actual definition is output-local, whereas an expected Import is never an Export target; retain alias equality, exact nominal origins and every original dependency expectation. No row, key domain or compatibility relation changes. |
| [Reader trust — transformations](../specs/representation/reader-trust.md#5-transformations-verification-reuse-and-audit-claims), [binding conformance](../specs/conformance/il-binding-and-replacement.md) | Add the explicit-selection handoff/failure boundary and independent selection, relocation/replacement and source-free reload cases. Do not require blanket body re-proof. |

The label, receiver-access, dependency, default, logical-record, registry,
format-versioning, target and runtime owners need no new assignment or
semantic amendment for this decision. Its only supersession is the
producer-surface deferral for explicit source-free generic-method selection,
not the source-authorship restrictions of the existing mapping record.

After implementation, update
[module support](../implementation/modules.md#definitions-and-scope) and
[CG-006](../implementation/conformance-gaps.md) only to the actual supported
boundary and qualified evidence; this document does not close either gate.

**Remaining decision:** formal approval of the recommended distinct API and
record. No selection-policy alternative is left to implementation.
Implementation, complete retained-graph support and qualification remain
open obligations, not approval claims.
