# ForeignNativeInterface selection in the native Core manifest

**Status: unapproved; provisionally actionable under the user's authorized
missing-contract workflow.** The user will review this candidate later.
Provisional implementation direction is not normative approval or evidence
of compiler conformance.

## Decision and owners

Recommend **`terrumc.native-core-manifest/2`**, retaining the complete
NativeImport/NativeCallable/NativeCallConv closure and adding one required,
nonnull `foreignNativeInterface` schema definition. Use the existing
`--native-core-manifest <path>` option and its already eligible source routes;
`compile-trrm`, including `--library`, is the required source-admission route.
Change no option spelling, command eligibility, path/publication rules or
machine protocol.

The missing contract is host-input delivery:
[`NativeCoreManifestReader`](../../sources/terrumc/NativeCoreManifestReader.cs)
selects an exact `/1` root and I/C/P schema grammar, whereas the hosted
[`NativeCoreRegistrationSet`](../../sources/Terrum.CodeAnalysis/Semantics/NativeCoreRegistration.cs)
already admits an optional ForeignNativeInterface schema and its
`UInt32Constant` argument. The separate
[`NativeOutputCoreManifestReader`](../../sources/terrumc/NativeOutputCoreManifestReader.cs)
selects only NativeOutputCondition/NativeOutput; it cannot supply FNI.

This proposal extends only the delivery grammar owned by
[NativeCore CLI registration](native-core-cli-registration.md#exact-version-1-json-grammar).
Its `/1` contract remains unchanged. The
[foreign-interface owner](native-output-foreign-interface-method-identity.md#candidate-source-contract)
continues to own the provisional nominal interface, offset, method, receiver,
dispatch and unsafe contracts.
[NativeCallable](native-calling-convention-source-schema.md) and
[NativeOutput registration](native-output-cli-registration.md) remain
independent prerequisites where used. No normative specification is amended.

Adding an optional field to `/1` would be a permissible intentional draft
replacement, not a forbidden compatibility break. Prefer `/2` because `/1`
currently rejects extra properties: an optional extension would change that
exact agreement while old readers still reject the new field. A separate
FNI-only manifest would instead add an unnecessary delivery family for a role
already carried by NativeCore. Keeping `/1` support is an explicit choice
here, not a claim of pre-release immutability.

## Exact version-2 grammar

Select the grammar from `schema` before validating its exact root; do not
infer a version from fields or retry another grammar. The root has exactly
these required properties:

```text
schema
coreAuthorityIdentity
usingModuleIdentity
providers
callingConventions
resolutionModes
resolutionPolicies
nativeImport
nativeCallable
nativeCallConv
foreignNativeInterface
```

`schema` is exactly `"terrumc.native-core-manifest/2"`. All other existing
properties retain their complete `/1` definitions, except that `providers`
admits **one through seven** Origins: exactly the distinct providers of the
three enums and four schemas, without duplicate or unused entries.
Providers may be shared or split; FNI need not have NativeCallable's provider.
No provider may equal the still-unbound `usingModuleIdentity`.

Origin retains exactly `moduleIdentity: Text`, `revisionIdentity: Text`,
`schema: { identity: Text, revision: u32 }`,
`governingSemanticEdition: u32`, `targetRequirement: u32` and
`runtimePolicy: u32`. Require nonzero schema revision, edition 1, and target
requirement/runtime policy each 0 or 1. Identity retains exactly
`providerIdentity: Text`, `exportKey: Text` and `kind`; FNI requires
`kind="attribute-schema"` and exactly one matching Origin. Keys and real
provider origins are host-supplied, never derived from source names.

`foreignNativeInterface` is a nonnull SchemaDefinition with exactly:

| Property | Required value |
|---|---|
| `identity` | The explicit attribute-schema Identity above. |
| `qualifiedName` | `"Terrum.Interop.ForeignNativeInterface"` |
| `role` | `"foreign-native-interface"` |
| `placement` | `"primary-declaration"` |
| `targets` | Exactly `["interface"]` |
| `arguments` | Exactly one Argument, defined below. |
| `genericArity` | Unsigned integer token 0 |
| `isPublic`, `isPrimary`, `isRepeatable` | Required Booleans true, true, false, respectively. |

The Argument has exactly these properties and values:

```json
{
  "name": "VTableByteOffset",
  "kind": "uint32-constant",
  "enumType": null,
  "isRequired": true,
  "allowsPositional": false,
  "allowsNamed": true,
  "contexts": ["interface"],
  "default": null
}
```

The new kind tag maps to `CoreNativeAttributeArgumentKind.UInt32Constant`
only in this FNI record; I/C/P retain their exact argument grammars.
The schema's internal `CallingConventionEnum`, `ResolutionModeEnum` and
`ResolutionPolicyEnum` are all null. NativeCallable still supplies its own
independently checked convention contract. `primary-declaration` is the
existing carrier spelling for FNI's primary-interface-only applicability.
The file describes the schema, **not an interface's offset value, members,
slots or IID**. Explicit offset supply, including zero, and target alignment
remain source obligations, not defaults in this file.

Retain `/1`'s strict UTF-8/JSON/Text/integer rules: at most one leading UTF-8
signature; one root followed only by JSON whitespace; no comments, trailing
commas, unpaired surrogates, unknown/missing properties or duplicate decoded
property names at any depth. Object order is insignificant; `targets` and
`contexts` are exact duplicate-free sets. Preserve ordinal text without
trimming, case folding or normalization; reject empty/whitespace-only text
and NUL. `u32` requires an unsigned integer token in `0..4294967295`, without
sign, fraction, exponent or string conversion. Validate all records, even
when source uses none of the selected roles; omission is never repair.

## Admission and composition

Preserve explicit host authorization of the closed declaration producer.
`coreAuthorityIdentity` records that designation; the file does not
authenticate itself, load provider code or establish artifact-backed exports.
The provider-origin schema selection remains a separate explicit declaration
contract, not this JSON version or a TRRM decoder selection.

Require every selected Core manifest's using identity to equal
`compile-trrm --identity` and preserve existing authority/using checks with
other Core manifests. Merge NativeCore, NativeOutput and any selected Layout
providers only when **every Origin field** agrees. Reject conflicting
revisions, schema selections, editions, requirements or policies; neither
file nor declaration order grants precedence.

Combine the complete unbound declarations, including FNI, in the existing
[`CoreManifestRegistrationComposer`](../../sources/terrumc/CoreManifestRegistrationComposer.cs).
Top-level provider/export keys and declaration identities must be distinct
across families, even for equal duplicate definitions. Enum member identities
remain unambiguous; enum-owned declaration keys retain their owner scope.
Create one actual `CoreProviderModuleBindings` object and complete original
expectations, then create those selected families' registrations against that
same object. Other families retain their existing admission; do not put local
evaluation catalogs into this external-provider carrier or transplant handles
from separate equal snapshots.

For `/2`, pass FNI through the existing optional
`NativeCoreRegistrationSet.Create` parameter and
`CompilationOptions.NativeCoreRegistration`; include its complete declaration
in the shared snapshot. NativeExport remains null. NativeOutput's unchanged
NativeOutputCondition/NativeOutput (E/O) file creates its own set against the
same bindings and selects no FNI or NativeCallable. Conversely, `/2` selects
no NativeOutput role. Publish no registration if any selected record or family
fails admission; retain the complete original FNI expectations through
compilation and checked replacement.

Source lookup, aliases, typed constants, effective conventions, unsafe
authorization and output/backing checks remain with their existing owners.
Neither FNI's presence nor a same-shaped or same-named declaration supplies
NativeCallable or NativeOutput.

## Backward behavior and refusal cases

Both versions are selected explicitly through the same NativeCore option.
`/1` retains its exact ten-property root, one-through-six provider bound and
absent FNI registration. It rejects `foreignNativeInterface` and
`uint32-constant` extensions. `/2` requires FNI; missing or null FNI does not
mean `/1`. Existing `/1` invocations keep their prior behavior.

These are required cases, not qualification evidence:

| Case | Required result |
|---|---|
| Valid `/2`, one shared provider or seven distinct used providers; optional agreeing E/O selection | Exact independent roles in one shared binding snapshot; no inferred provider or output registration. |
| Existing `/1`; no native manifest; NativeOutput-only; FNI-capable `--trrm-schema` alone | No FNI source registration; unaffected source remains usable and affected source retains ordinary diagnostics. |
| Unknown/wrong-case version; `/1` with FNI; `/2` missing/null FNI; wrong kind/name/role/placement/targets/argument/default/flags | `TRM2001`, exit 2, including unused malformed records. |
| Invalid UTF-8/JSON, escaped duplicate properties, numeric coercions/overflow, duplicate sets, `/1` with seven providers or `/2` with eight | Whole configuration refusal: `TRM2001`, exit 2. |
| Duplicate/unused/missing provider, wrong key/kind, conflicting shared origins, authority disagreement or colliding declarations | Combined admission refusal: `TRM2001`, exit 2; no first/last winner. |
| Using identity differs from `--identity`; manifest aliases another input/output | Preserve `TRM2000` or `TRM2002`, respectively, exit 2 and existing prepublication output preservation. |
| Invalid source FNI, missing effective NativeCallable, required NativeOutput selection or unsafe/backing facts | Existing located source failure, exit 1; configuration does not repair source. |
| Valid source registration with an unsupported destination schema, target or emitter | Preserve the independent later refusal and staged-publication boundary; do not discard retained FNI facts or select a fallback. |

## Non-scope and remaining decision

This chooses no new source syntax, NativeOutput/NativeExport/NativeParameter
schema, vtable prototype admission, layout/value representation, IID
acquisition, ownership, marshalling, ABI, TRRM allocation/profile/codec,
runtime behavior, provider default/discovery or `terrum.cli/1` extension.
FNI artifact selections and `--trrm-schema` remain independent: a manifest
does not select an output schema, and an artifact schema does not register
source attributes. Source-free normalized consumption needs no manifest
merely because source binding used one.

The single new review decision is the exact `/2` delivery contract above.
The host must still supply legitimate authority, actual provider
origins/revisions/declaration-schema selections and export keys. Existing
FNI/NativeCallable/NativeOutput proposals retain their separate provisional
status; this recommendation neither approves them nor establishes support.
