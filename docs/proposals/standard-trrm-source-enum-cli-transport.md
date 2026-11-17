# Standard TRRM source enum CLI transport

**Status: unapproved candidate. The user explicitly authorized provisional
implementation after this proposal is created under the missing-contract
workflow; that is provisional direction, not formal approval. This document
records the contract and does not approve the candidate.

## Decision, scope and owners

Add one optional, nonrepeatable `compile-trrm` option:

```text
--trrm-source-enum-exports <path>
```

Its strict version-1 JSON file supplies only the existing
`TrRmSourceTypeExport` selectors, declaration ExportKeys and complete ordered
`EnumMemberDeclarationKeys`. Forward these through the existing five-argument
`EmitStandard` overload. Do not add a compiler API, source syntax, key
generator, Standard schema/composition, wire field or private carrier.

This closes only the CLI deferral in
[Standard source enum identity inputs](standard-trrm-source-enum-identity-inputs.md#failures-and-cli-boundary).
That proposal still owns mapping, exact-program capture and failure semantics;
[source nominal enum identity](source-nominal-enum-identity-inputs.md)
owns source handles/composition, and
[nominal enum persistence](nominal-enum-persistence.md)
owns the complete bounded public, namespace-level, nongeneric, primary
int32 declaration, member identity, provider snapshots and replacement rules.
No accepted text is replaced. Preserve
[metadata keys](../specs/il-metadata.md#identity-and-contract-invariants),
[exact-schema agreement](../specs/representation/format-versioning.md),
and [binding/replacement](../specs/representation/binding-and-replacement.md).

Current source observations, not newly executed validation:

| Evidence | Current boundary |
|---|---|
| [`TrRmSourceTypeExport` / `EmitStandard`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.cs), [`NominalEnumsDraftSourceEnumExports`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.NominalEnumsWriter.cs) | The API accepts complete explicit source enum export/member assignments and captures them against the checked program. |
| [`CLI handler`](../../sources/terrumc/Program.TrRmExecution.cs), [`mapping reader`](../../sources/terrumc/TrRmSourceEnumExportsReader.cs), [`usage`](../../sources/terrumc/Program.cs) | A provisional `compile-trrm --trrm-source-enum-exports <path>` route parses the strict version-1 file and calls the existing five-argument emitter only after exact NominalEnumsDraft schema selection. |
| [`enum API tests`](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmEnumTablesTests.cs), [`CLI tests`](../../tests/Terrum.CodeAnalysis.Tests/Cli/CliTests.TrRmExecution.cs), [CLI inventory](../implementation/cli-protocol.md#provisional-trrm-source-schema-selection) | The API and CLI paths exercise explicit source keys, strict manifest handling, schema/option preflight, and path/order validation. This is implementation evidence under provisional authorization, not formal adoption. |

Use the existing
[single-file configuration pattern](source-layout-cli-registration.md#exact-command-line-contract),
not its Core-authority or declaration-producer semantics. A repeatable
counted/variadic argv group would introduce member-count framing and argument
boundary policy; separate declaration/member flags need joins and orphan
rules. One file mirrors the existing complete API records, handles empty
enums and arbitrary key text, and avoids a second mapping model. No alternate
inline, per-member or response-file form is selected.

## Exact arguments and selection

Accept the option only on `compile-trrm`, among its existing options after
the required positional source and before a final optional `--json`.
Consume exactly one following argv element as the path. Reject repetition,
including the same path; missing/empty operands; `-`; and operands beginning
with `-`. Dash-leading filenames require an explicit relative or absolute
path. No short/equals-form alias, altered casing, environment default,
automatic discovery or filename-extension requirement is provided.

Presence requires the existing explicit pair:

```text
--trrm-schema trrm/1-draft-nominal-enums-1
```

Require exact ordinal equality, including when the file's export array is
empty. No omitted schema, baseline, enum composite, A2/L1 selection or private/
Development profile accepts the option. All other commands, including
source-free TRRM commands, reject it before input reading. The file cannot
select a schema or repair an unsupported selection.

Keep the command's required `--identity`, output, fail-fast runtime,
Windows x64 Module target requirement **1**, arithmetic selection and
`--library` behavior unchanged. The option adds no executable admission:
the existing enum producer's supported library/body shapes remain its gates.
Core manifests remain independent source-binding configuration; none can
supply omitted export/member keys, and this file grants no Core authority.

Resolve the path once against the invocation working directory. Read one
regular file, not stdin, a directory, URL, include tree or provider artifact.
Use the existing path-identity checks to reject aliases with any source
file, selected Core manifest or output destination before acquisition.
Perform argument/schema and alias preflight before file I/O. Read and admit
configuration files in their argv option order, preserving the existing
Core-only composition rules, before reading any source, including stdin.
Then bind source before performing source-dependent mapping checks.

## Exact version-1 file

The complete shape is:

```json
{
  "schemaVersion": 1,
  "exports": [
    {
      "namespace": "Example",
      "name": "Color",
      "exportKey": "type.palette/v1",
      "enumMemberDeclarationKeys": ["member.warm", "member.cool"]
    }
  ]
}
```

This illustrates transport fields, not a source declaration or generated
keys. Every object has exactly the shown required properties; no property
admits null. Property order is insignificant; array order is preserved.
`schemaVersion` is the integer token exactly `1`, not `1.0`, `1e0`, `+1`
or a string. It versions this input grammar only, never TRRM.

Require strict UTF-8, permitting one leading UTF-8 signature, and one root
object followed only by JSON whitespace. Reject malformed UTF-8, unpaired
surrogates in decoded strings, comments, trailing commas/content, unknown or
missing properties, and duplicate decoded property names at every depth,
including differently escaped spellings. There are no includes or nested
file references.

| Field | Exact meaning |
|---|---|
| `exports` | Array of zero or more complete source enum assignments. Preserve entry order when constructing the API list; never zip entries to declaration ordinals. |
| `namespace` | String containing the exact full decoded source namespace; `""` selects the global namespace. No alias/import lookup, trimming or dependency search. |
| `name` | Nonempty string containing the exact decoded simple source declaration name, not a qualified name or source escape spelling. Together with `namespace`, it selects exactly one eligible locally authored Source enum in this checked program. |
| `exportKey` | Explicit producer-selected TypeDef ExportKey, preserved exactly. |
| `enumMemberDeclarationKeys` | Required string array; construct a nondefault `ImmutableArray<string>`, including explicit `[]` for an empty enum. Its length must equal the complete checked member count. |

All selector/key strings are well-formed scalar text without NUL. Keys are
nonempty ordinal text, not identifiers; whitespace and Unicode spelling are
preserved without trimming, normalization or case folding. Apply the
existing API's Standard String/key capacity checks, including its selected
UTF-8 writer's `0xFF_FFFF`-byte per-key limit, not the private 256-byte cap
or a new transport-specific key cap. Retain independent aggregate/resource
limits; exceeding capacity is failure, never truncation or partial capture.

## Association, totality and omission

* **Source order:** the required positional source is first; each accepted
  `--source` operand appends a tree in argv occurrence order. Preserve existing
  input count, duplicate-path and stdin rules. Within each tree, use source
  declaration order. Composed enum parts keep this compiler-file/source order,
  with no priority for the primary part and no path/name sorting.
* **Declaration selection:** entries select by exact namespace/simple name,
  independently of their array position. Reject repeated selectors, even
  identical ones, and ExportKey collisions across all Module exports,
  including callables and separately retained declarations. Do not target
  imported/Core/Private declarations or rekey retained publications.
* **Member selection:** associate each supplied key with the corresponding
  actual member in the complete composed order, including unused and
  duplicate-valued members. Reject missing/extra keys and duplicate keys
  within one enum. Reusing a member key in another enum or as a Module export
  key is legal: member identity is `(selected T, producer member key)`.
* **No inference:** names, paths, spans, source handles, values, ordinals and
  array positions never generate durable keys. Positions only select members
  within this checked snapshot. The producer stores/resupplies its own keys
  and adjusts associations when source-input/member order changes.
* **Total capture:** validate every submitted entry, not only Body-used
  declarations. Forward immutable records to the existing API; it owns
  eligibility, complete retained closure, snapshot validation/rebasing and
  preservation of original provider expectations. Do not mutate source
  handles or replace those checks with CLI name-based identities.
* **Omission:** with no option, retain today's Development default and
  four-argument Standard route unchanged. With the option, call the
  five-argument overload using the exact schema, `--identity`, target 1 and
  captured list. An empty `exports` array retains the API's empty-list behavior:
  no assignments, not inference. Neither omission nor an empty list supplies
  keys for retained fresh Source enums; those still fail publication.

## Failures and publication

Use the existing experimental CLI diagnostics, not new code assignments:

| Failure | Required projection |
|---|---|
| Option spelling/arity/repetition/operand, wrong command or missing/ineligible exact schema | `TRM2000`, exit 2, before configuration/source reads. |
| File I/O/access/strict decoding/JSON/version/field/type/key errors; duplicate assignments/keys; source-dependent invalid mapping reported by API `ArgumentException` naming `typeExports` | `TRM2001`, exit 2, as invalid submitted mapping input. |
| File aliases an input/configuration/output | `TRM2002`, exit 2, before output acquisition. |
| Source syntax/binding failure after configuration admission | Existing source diagnostics, exit 1; do not fabricate mapping results. |
| Missing mappings for retained closure, unsupported valid enum/base/body/target facts (`NotSupportedException`), or invalid checked program/snapshot | Existing `TRM5001`, exit 1; do not misclassify program failures as configuration errors. |
| Output publication failure | Existing output-error behavior; no success artifact. |

Configuration diagnostics identify the file and established field/entry when
available, without inventing a source span. Bind errors precede
source-dependent selector/count checks. Every prepublication failure leaves
an existing destination byte-identical and creates no absent destination.
Publish only the complete validated artifact through the existing writer;
never retry Core/private/another schema or return declarations-only success.

Preserve human and experimental `--json` response shapes. This option does
not implement or amend [`terrum.cli/1`](../specs/cli-protocol.md), extend its
command catalog, or alter source coordinates/status semantics. It introduces
no source/runtime/library/target/lowering contract or consumer input route.

## Integration and remaining acceptance

The provisional implementation is limited to CLI usage, the strict mapping
file, the existing compile handler's overload handoff and active CLI tests.
The API, schema, enum/member rows and provider/re-emission owners are unchanged.
Formal approval remains separate from the user's provisional implementation
authorization.

| Family | Required cases |
|---|---|
| Positive association | Covered: secondary-before-primary partial composition across source files, reversed export-entry order, global/namespaced selectors, empty/duplicate-valued enums, an unused public declaration/member, cross-owner member-key reuse, 257-byte/max keys, complete rows and exact member association. API producer/consumer tests separately cover provider binding and source-free re-emission. |
| Validation boundaries | Covered: duplicate decoded properties, unpaired surrogates, missing/null/unknown fields, malformed UTF-8/double signature, exact version tokens, wrong counts/selectors, duplicate declaration/export/member keys, and maximum/over-limit key lengths. |
| Selection/defaults | Covered: exact NominalEnumsDraft gate, missing/wrong schema preflight, repeated/wrong-command/equals-form/bad operands, selected-schema empty export lists, no-key inference failure, and unchanged Development option omission. |
| Ordering/I/O | Covered: source/config and source/Core/output aliases, both source-enum/Core option orders, malformed configuration before invalid source/stdin, missing mapping files, output sentinel preservation and absent-output preservation. |
| Cross-feature retention | Existing API/Standard producer tests cover declaration, constant-return and identity-method library paths, exact provider binding, source-free re-emission and Windows x64 PE execution; the CLI positive test independently validates complete declaration/member tables from its artifact. Core manifests do not infer keys; wider/file-local/internal enums and unsupported operations still decline through their existing gates. |

No transport choice remains unresolved in this recommendation. Actual
producer keys remain explicit inputs; formal approval, implementation and
qualification remain outstanding. Provisional implementation authorization
does not imply formal adoption or a completed compiler milestone.
