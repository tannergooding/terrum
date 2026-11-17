# Standard TRRM source type and field CLI transport

**Status: unapproved candidate; provisional direction authorized for
implementation under the missing-contract workflow.** This does not formally
approve a CLI contract or change the accepted language, Standard TRRM schema,
or compiler API.

## Problem and recommendation

The provisional
[nested FieldPath successor](standard-trrm-init-reference-nested-field-destinations.md)
requires explicit TypeDef and FieldDef ExportKeys when a source provider is
published for a nested path. The existing
[`TrRmSourceTypeExport.FieldExports` API](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.cs)
and five-argument
[`CreateStandardReference` overload](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.StandardTypeReference.cs)
carry those mappings. `terrumc` currently exposes only the strict,
enum-specific `--trrm-source-enum-exports` file; its parser accepts enum member
keys and rejects unknown properties. The existing enum transport proposal
explicitly keeps that input enum-specific.

Add a distinct, nonrepeatable `compile-trrm` option:

```text
--trrm-source-type-exports <path>
```

The file transports the existing `TrRmSourceTypeExport` mapping under only
`trrm/1-draft-init-reference-bodies-local-field-layout-2`. It introduces no
new compiler authoring API, source syntax, TypeDef/FieldDef encoding, Feature,
or compatibility alias. Do not extend or reinterpret the enum-specific file.

This is provider-publication transport only. `compile-trrm` currently rejects
provider inputs; this candidate does not add source dependency loading or
consumer-side provider selection. Source consumers continue to require the
existing checked `CreateStandardReference` handoff with explicit keys. A CLI
route without that handoff must decline rather than derive an Import.

## Exact manifest

The complete version-1 JSON shape is:

```json
{
  "schemaVersion": 1,
  "exports": [
    {
      "namespace": "Example",
      "name": "Cell",
      "exportKey": "type.cell",
      "fieldExports": [
        {
          "name": "Value",
          "exportKey": "field.cell.value"
        }
      ]
    }
  ]
}
```

The root has exactly the required `schemaVersion` and `exports` properties.
Each export has exactly `namespace`, `name`, `exportKey`, and `fieldExports`;
each field entry has exactly `name` and `exportKey`. `fieldExports` may be
empty. Type selectors use an exact namespace and simple declaration name;
field selectors name direct fields only. Keys are opaque, explicit,
nonempty text, unique across TypeDef and FieldDef exports in the manifest.
Reject missing, null, duplicate, unknown, incorrectly typed, malformed UTF-8,
or noncanonical version values. Do not accept enum member keys, generic arity,
interface-method exports, dotted field paths, includes, or inferred keys.

The reader creates the existing `TrRmSourceTypeExport` records, populates
`FieldExports`, leaves enum/interface properties at their default state, and
passes the checked mappings to the existing `EmitStandard` overload. Existing
producer checks remain authoritative for source declaration identity,
visibility, layout, and field eligibility.

## CLI and failure boundary

- Accept the option only on `compile-trrm`, only with the exact nested
  FieldPath schema identity and the command's existing module identity.
- Reject repetition, missing or dash-leading operands, `-`, schema mismatch,
  and combination with `--trrm-source-enum-exports` or
  `--trrm-source-generic-method-exports`.
- Resolve one regular file relative to the invocation working directory.
  Preserve existing path-alias checks against source and output. Preflight
  option/schema/path identity before reading source; on any failure, preserve
  existing outputs.
- Keep artifact-command root/provider schema selection exact. This option
  does not add provider inputs to `compile-trrm`, alter `terrum.cli/1`, or
  change `run-trrm`/`analyze-trrm` behavior.
- Report selection/option errors as the existing `TRM2000`, file/field-mapping
  errors as `TRM2001`, and path aliases as `TRM2002`; unsupported source
  declarations retain the compiler's existing diagnostic boundary.

Keep the enum version-1 manifest and
[`--trrm-source-enum-exports` contract](standard-trrm-source-enum-cli-transport.md)
unchanged. This is a host-side transport for an existing compiler mapping,
not a new source or metadata authority. If “no new authoring API” in the
nested FieldPath proposal is interpreted to exclude CLI transport as well as
compiler APIs, this candidate conflicts and requires later review rather than
silently broadening that proposal.

## Owners and acceptance

On adoption, update the `terrumc` option contract and
[CLI implementation inventory](../implementation/cli-protocol.md), the
[`Program.cs` usage](../../sources/terrumc/Program.cs), the
[TRRM execution option handler](../../sources/terrumc/Program.TrRmExecution.cs),
and a separate strict type-export reader. Keep enum parsing and its tests
unchanged. No `docs/specs` grammar, ABI, registry, or TRRM record amendment is
selected.

Required cases:

| Family | Cases |
|---|---|
| Positive | Explicit type-only and type-plus-field maps compile under the exact successor; inspect the emitted keys and Feature 13/1. |
| Negative | Wrong schema/command, duplicate or colliding keys, missing/unknown properties, malformed JSON/UTF-8, invalid source selectors, option repetition/combination, and output-alias failures reject without replacing output. |
| Cross-feature | Existing enum and generic-method manifests retain their exact behavior; CLI-produced FieldDef exports bind through the existing five-argument source-reference API; nested imported FieldPath emission, exact root/provider `run-trrm`/`build-trrm` selection, source-free re-emission and supervised AOT preserve explicit keys. |

These are future acceptance criteria, not evidence that the CLI route is
implemented or validated.
