# Standard TRRM provider-owned dependency closure

**Status: unapproved provider-admission amendment.** The complete bounded
Module shape, exact binding rules, every-edge traversal and acceptance cases
are integrated in
[Standard TRRM selected-provider consumption](standard-trrm-selected-provider-consumption.md).
This document records the distinct closure decision; it does not approve F,
add a schema/API or claim implementation support.

## Decision and boundary

For exact `trrm/1-draft-foreign-invocation-contracts-1` (F), admit a finite
ModuleRef/MethodDef Import closure across the explicitly supplied artifact
set. Every edge—including uncalled/declaration-only edges—retains its
original expected declaration and binds to an exact provider-local public
NativeImport through the selected Module identity and Export key. A provider
Module may itself retain imports; resolving a root edge to it does not
redirect that edge to a downstream module.

The integrated F candidate permits owned-direct, imported-consumer and
mixed provider/consumer Module shapes, with bounded anchors/callers and
bodyless imports. It supports finite chains, diamonds and cycles only after
every Module and every incoming/outgoing expectation passes. A self-edge
requires a distinct expected stub selecting a local owned NativeImport.
Imported proxies are never re-exported or used as actual provider targets.
No forwarding thunk, general linker, Module discovery, authored-method
provider or initialization/cleanup schedule is added.

This amendment supersedes the former provider-leaf-only and
extra-dependency-edge exclusions solely for the exact F semantic consumer.
The
[multiple-root Import record](standard-trrm-multiple-root-method-imports.md)
retains its separate root-cardinality/name-bound decision. Exact F selection,
Module identities, original expectation comparison and per-Module requirements
remain in the selected-provider owner.

## Native realization remains separate

This is semantic admission and binding only. The
[provider-native-lowering proposal](standard-trrm-provider-native-lowering.md)
owns the checked native plan, actual code/load origins, JIT/PE realization,
lifetime and process behavior. Semantic relowering is not unchanged
precompiled-provider deployment: existing package/set/graph routes keep their
selected images and entry/lifetime contracts, and this decision does not
regenerate them or select a conversion path.

Implementation support and test evidence belong in the implementation
inventories, not this decision record. No current source/test snapshot here
establishes complete closure, native execution or formal approval.
