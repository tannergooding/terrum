# Semantic IL and metadata extensions

> [!IMPORTANT]
> The complete-type `TypeSpecRef` union, `0xF2`/`0xF3` forms, complete-field
> scope and whole-artifact canonical writer rule are integrated in the
> specification and registry. The approved register instruction encoding,
> fixed Body framing, record schemas, SourceMap recipe roles and canonical
> instruction forms are also integrated. This proposal now covers only
> additional Body/runtime carriers that remain unapproved.

## Remaining Body and runtime questions

The current Body schema assigns instruction-stream kind `01` and exact
field-read/write/reference and identity-forwarder kinds `02..05`. It has no
general/compact-linear/compact-indexed profile variants. These assignments
do not complete every semantic/runtime carrier or authorize additional Body
forms.

| Area | Remaining boundary |
|---|---|
| Additional implementation kinds | No additional Body implementation kind is assigned. Any new descriptor must be an exact implementation of a MethodDef and preserve its full signature, authority, failure, ownership, cleanup and output contract. |
| Wider local indexes | The current Body uses fixed `u16` indexes and counts through 65,536. A wider index requires a separately selected codec/schema; no width is inferred from an out-of-range value. |
| Logical roots and effects | Complete method/block effects, roots, owners, interior dependencies, cleanup and exceptional continuations for executable features, with producer obligations and consumer validation. |
| Exception regions and handlers | Assign the ExceptionRegion inventory, ordered handler/filter/finally rows, exception-object transport and runtime event storage before claiming handler/region-exit support; see CG-003. |
| Cross-method sharing | Defer Body sharing until the ownership model validates every referencing MethodDef's complete signature and contracts. |
| Inlining contracts | Specify the exact Body/declaration contract and remapping model before admitting shared or inline bodies; opcode identity alone is insufficient. |

The [current instruction-set schema](../specs/il-instruction-set.md#body-row-layout-and-compact-implementations)
and [registry](../specs/representation/registry.md) own current Body forms
and instruction assignments. Producer, reader, remapping and qualification
work remains tracked in [CG-001](../implementation/conformance-gaps.md#cg-001).
