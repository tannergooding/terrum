# Standard TRRM multiple root MethodDef Imports

**Status: unapproved admission amendment.** The complete provider-admission
contract is integrated in
[Standard TRRM selected-provider consumption](standard-trrm-selected-provider-consumption.md).
This document records the distinct root-cardinality decision; it does not
approve F, add a schema/API or claim implementation support.

## Decision and boundary

For exact `trrm/1-draft-foreign-invocation-contracts-1` (F), admit multiple
retained root-owned bodyless MethodDef Imports under one supported caller and
one NativeAddress anchor. Each expected MethodDef keeps one original Import,
ModuleRef, NativeImport contract and provider binding, whether or not its
target appears in the caller's MethodMap/CallSites. The caller still has one
E9 target, which resolves through that target's exact retained expectation;
the other Imports are not discarded.

For N root-imported expectations, the bounded root has N Import-owned
MethodDefs, one anchor and one caller Body (`N+2` relevant MethodDefs).
ModuleRefs may be reused across Imports; if N is nonzero, every retained
ModuleRef is used and `1 <= M <= N`. A distinct provider/key may be shared
only while every original expectation is independently satisfied. The
existing owned-direct and single-import routes are unchanged.

The current lowering locator bounds Modules with more than one callable
declaration to ordinally distinct method names among those declarations and
the caller; the anchor is not a callable name. This is an implementation
admission limit, not a metadata identity rule. Names may repeat across
Modules. Do not rename or merge declarations to pass the bound.

The full exact F binding, Module shapes, closure traversal, cycles and
acceptance cases are owned by the
[selected-provider proposal](standard-trrm-selected-provider-consumption.md#bounded-f-module-closure).
Provider chains/mixed provider Modules are the separate
[provider-owned closure decision](standard-trrm-provider-dependency-closure.md).
Native realization and precompiled deployment remain owned by
[provider-native lowering](standard-trrm-provider-native-lowering.md); this
cardinality amendment does not redefine either.

## Approval boundary

This is a candidate amendment to provider-consumer admission, not a new
Import/Export format, provider resolver, general linker or runtime policy.
The exact F schema, Windows x64/fail-fast restrictions, direct
provider-owned NativeImport requirement, no forwarding/re-export rule and
complete original-expectation checks remain. Checked implementation/test
evidence belongs in the implementation inventories; this proposal records no
current test result.
