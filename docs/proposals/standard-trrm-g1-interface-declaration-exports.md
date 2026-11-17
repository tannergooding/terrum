# G1 interface declaration export inputs

> [!IMPORTANT]
> **Unapproved proposal; provisional implementation direction only under the
> user-authorized missing-contract workflow.** This proposal changes no
> normative specification, does not assign a new TRRM identity, and reports
> no implementation or qualification result.

## Decision and scope

The G1 GenericCall proposal already defines role-2 interface-requirement
targets, exact declaring-interface/MethodDef identity, open Constraint
evidence, closed InterfaceWitness evidence, and complete obligation
reconstruction. The source producer inputs do not currently select or publish
the InterfaceDef/InterfaceMethod declarations those targets must retain:
G1's source map is method-only, and the six-argument G1 emitter rejects
TypeDef export mappings. This is a source-publication gap, not a second
GenericCall wire design.

**Recommendation:** extend the existing source-input contract with an exact
G1-only version 3 map. Reuse explicit TypeDef ExportKeys for source interface
owners and add source-anchored ExportKeys for their directly declared
callable requirements. Keep G1 version 1 (method-only) and CG1 version 2
(its existing type/member plus method mappings) grammatically unchanged. The
version number selects the CLI mapping grammar. For the label-preserving
route, the unapproved
[G1/C1 label proposal](standard-trrm-g1-interface-parameter-labels.md)
supersedes this proposal's original G1 artifact pairing: versions 1 and 3
select the distinct exact G1L1 identity. Original G1 is not an alias or
fallback; CG1 version 2 does not transitively select G1L1.

The initial source-publication subset is public, module-level interfaces with
type-only owner formals, no bases or associated members, and directly
declared, bodyless, instance `Callable` requirements with value-only scalar
or owner-formal signatures. It emits role-2 calls only with exact open
Constraint evidence. Unsupported defaults, member kinds, constraints,
contracts, provider dependencies, or closed-witness graphs reject; they are
not omitted. This does not select a dynamic interface ABI, interface values,
WitnessProjection, associated-member encodings, or a new runtime lookup.

This proposal supersedes only the G1 TypeDef-export exclusion and
method-only-input boundary in
[source generic-method export inputs](standard-trrm-source-generic-method-export-inputs.md).
The G1 Body roles/evidence remain owned by
[GenericCall Body encoding](standard-trrm-generic-method-call-body-encoding.md).
Both proposals remain unapproved.

## Version-3 source inputs

Reuse `EmitStandard`'s existing explicit source TypeDef and generic-method
mapping inputs. Extend `TrRmSourceTypeExport` with the companion proposal's
required nonnegative `GenericArity` selector for this G1 map, and:

```csharp
public ImmutableArray<TrRmSourceInterfaceMethodExport>
    InterfaceMethodExports { get; init; }

public sealed record TrRmSourceInterfaceMethodExport(
    int SourceInputOrdinal, TextSpan DeclarationSpan, string ExportKey);
```

The selected program is the `EmitStandard` input. Each nested record anchors
to one checked source declaration in that program by source ordinal and full
span. A default `InterfaceMethodExports` means the selected TypeDef is not an
interface; a source interface requires a nondefault list, which may be empty
only when its directly declared callable requirement range is empty. A
version-3 configuration has exactly:

```json
{
  "schemaVersion": 3,
  "sources": [],
  "typeExports": [
    {
      "namespace": "Example",
      "name": "I",
      "genericArity": 1,
      "exportKey": "example.interface/I",
      "interfaceMethodExports": [
        {
          "sourceInputOrdinal": 0,
          "declarationStart": 42,
          "declarationLength": 19,
          "exportKey": "example.interface/I.Read"
        }
      ]
    }
  ],
  "genericMethodExports": []
}
```

`sources` retains the existing complete ordered UTF-16 snapshot assertions.
`typeExports` selects only the interface TypeDefs admitted by this proposal.
`genericArity` counts all owner formals; it is not a type-argument count.
Each directly declared requirement mapping identifies its exact checked
source declaration by input ordinal and full span. The interface TypeDef
ExportKey selects the complete InterfaceDef and InterfaceMethod declaration
closure; a method key never substitutes for the owner identity. Duplicate,
missing, stale, cross-owner, or ambiguous mappings reject.

Map every directly declared public callable requirement on a selected
interface, including an explicit empty list when there are none. Do not emit
an InterfaceMethod whose MethodDef lacks a corresponding checked declaration
anchor. Inherited requirements are represented by their actual declaring
interface and are not copied into a derived interface's direct range. The
writer preserves source order for InterfaceMethod ordinals; ordinals are not
export keys or dispatch slots.

The label-preserving CLI route pairs versions 1 and 3 only with exact G1L1;
version 1 remains method-only and version 3 carries the bounded interface
mappings. Version 2 remains CG1-only. A version/schema mismatch never retries
another mapping grammar or falls back to original G1. Strict decoding,
duplicate-property rejection, source path/order/hash checks, alias checks,
one binding operation, immutable mapping capture and atomic output
preservation remain unchanged.

## Required serialized closure

For each selected interface, emit its TypeDef, explicit Export, InterfaceDef,
owner GenericParams, and complete directly declared InterfaceMethod range.
Emit the TypeDef's required default Layout row under the existing layout
contract; it is not a dispatch slot. Each requirement uses its MethodDef,
MethodSig, Params and InterfaceMethod mapping. Assign it an explicit
MethodDef ExportKey because the MethodMap Import branch expects a MethodDef
Import when the requirement is used by another Module; the interface
TypeDef ExportKey is not a synonym for that method key. Preserve exact source
ownership, public visibility, receiver, ordinary parameter/result types,
constraints and failure/effect facts admitted by this subset. The MethodDef
is the exact requirement target; do not emit a generated forwarding stub or
infer it from a name, signature, declaration ordinal, vtable slot, or
proof-stub name.

For an open role-2 GenericCall, emit the exact requirement MethodDef token,
actual declaring-interface TypeSpec, and the caller-owned kind-1 Constraint
that supplies the open dispatch requirement. The GenericCallTarget's
singular evidence is not the plural generic-argument obligation environment:
reconstruct every owner/method obligation from the complete selected
declaration and caller constraints. No closed InterfaceWitness is required
for this open form.

This proposal does not select source or source-free publication of closed
InterfaceWitness/WitnessMember graphs. Closed evidence kind 2 remains usable
only where another exact selected path provides and validates the complete
witness graph; otherwise the affected call rejects. Evidence kind 3 remains
unsupported under the existing GenericCall proposal.

The source-free reader resolves the interface TypeDef export and requirement
MethodDef exports against explicit provider artifacts. Preserve exact
provider identity, expected definitions, original contracts, and selected
schema for every interface, requirement, Import, GenericParam, Constraint,
and Body edge. Reject missing/duplicate providers, incompatible replacement,
unresolved imported requirements, or dependencies whose source/provider
facts cannot be retained. Do not derive method keys from InterfaceMethod
ordinals or use one requirement's evidence for another.

## Alternatives

| Direction | Assessment |
|---|---|
| **G1 version-3 interface TypeDef and requirement mappings** | **Selected.** Supplies the declaration identity and explicit requirement keys required by role 2 while preserving existing G1 bytes, records, Feature 23, and version-1/CG1-v2 map grammars. |
| Infer interface exports/method identities from names, signatures, ordinals, slots, or generated proof stubs | Rejected. These are not producer-selected identities and cannot support exact replacement checks. |
| New GenericCall record, opcode, Feature, or successor schema | Not needed for this source-input gap. Use a separate exact schema only if the required interface, witness, projection, or provider graph needs records outside existing G1 and assigned Interface* tables. |
| Publish arbitrary TypeDefs, associated members, interface defaults, or closed witnesses through this mapping | Out of scope. Each brings independent value, Body, provider, or authority obligations and must not be hidden in this interface-only export route. |

## Acceptance cases

These are proposal requirements, not claimed passes:

- Publish a generic `I<T>` and bodyless `Read(T)` requirement with unique
  explicit keys; emit `Constrained<T> where T : I<T>` and a role-2 GenericCall
  using the exact InterfaceMethod target and Constraint evidence.
- Independently load the provider artifact without source/debug mapping files;
  bind a consumer's `S : I<S>` and specialize the imported generic method using
  the consumer's exact witness.
- Reject absent/duplicate owner and requirement mappings, wrong owner/input
  span, same-name overload ambiguity, private or inherited member selection,
  and stale source snapshots.
- Reject unsupported owner formals, bases, default bodies, member kinds,
  non-value modes, added contracts, associated-member ranges, and role-2
  evidence that names a property/associated Constraint or the wrong formal.
- Reject missing/duplicate provider, wrong ExportKey/DefId, changed interface
  owner or requirement signature, incompatible replacement, missing witness,
  and any unrepresented provider/dependency obligation.
- Preserve exact interface/requirement identities through source-free
  re-encoding and remapping; row equality, a method name, and a successful
  closed specialization are not evidence.
- Validate exact schema/configuration version pairs and preserve existing or
  absent destinations on every mapping, binding, consumer, and publication
  failure.

## Owners and implementation boundary

This proposal amends the producer-input boundary in
`standard-trrm-source-generic-method-export-inputs.md`; it does not amend
accepted syntax or the logical GenericCall operation. Implement its records
through the existing InterfaceDef/InterfaceMethod/GenericParam/Constraint
schemas, explicit Import/Export expectations, mandatory consumer validation,
and GenericCall role-2 lowering. Keep implementation support in the
implementation inventory and per-run evidence in `artifacts\log` or
`artifacts\tst`. Adoption of this proposal would not complete closed witness
persistence, projection, all interface members, or the wider G1 milestone.
