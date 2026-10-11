# Native dependency graphs

A native dependency graph has a root consumer, intermediate providers that
may themselves import definitions, and leaf providers. Its selected closure
contains the dependencies required by those participants. A candidate catalog
is a set of possible providers, not an already admitted closure.

[Package identity](native.md#native-package-identity-and-filename-collisions)
is case-sensitive ASCII. Check the entire selected closure separately for
target filename collisions; forwarding and graph order cannot fold or merge
distinct identities.

The selected complete deployment profile determines topology and supported
value, receiver, output, heap/root and failure modes. The private development
implementation's acyclic graphs and examples do not admit arbitrary cycles,
shared managed heaps or cross-image handles. An owner-forwarding example and
an image-private managed-state example do not jointly prove that their
crossing modes can be combined.

Each edge retains the selected producer/definition, complete expected
contract and [binding/replacement obligations](representation/binding-and-replacement.md).
Intermediate forwarding preserves the original nominal type and cleanup
provider; physical co-location or an equal-named declaration does not replace
that identity. A cleanup/type dependency can remain required without an
emitted machine call.

Admission establishes the complete retained closure and actual target/runtime
compatibility before affected execution. A missing provider, unsupported
required mode or incompatible expectation is not repaired by an empty import,
newest-version choice, guessed runtime fallback or source rebinding.
An explicitly selected catalog/resolver policy must distinguish a unique
selection from ambiguity; this chapter does not standardize the private CLI's
candidate enumeration, search algorithm or numerical limits.

Compatible replacement can leave root and intermediate machine images
unchanged only while every retained expectation and native ABI remains
satisfied. Owned values, roots, calls and cleanup retain the responsible
origin image and [code lifetime](native.md#runtime-and-code-lifetime).
The chosen heap/collection domains remain separate from graph topology;
no serialization tag grants cross-image root or handle authority.

The [Windows x64 deployment contract](targets/windows-x64-deployment.md#4-selected-closure-origin-and-replacement)
defines the fixed rootless closure, allocator/cleanup origin and retained-code
scope. It does not merge separately admitted managed/owner example modes,
choose a resolver algorithm or make a dependency cycle's lifetime schedule
valid merely because the platform loader accepts it.

The `.trng` graph and its leaf `.trmn` packages are
[private persistence](representation/persistence.md). The informative
[native-graph implementation guide](../implementation/native-graph.md)
preserves current containers, reader/profile ranges, managed/owner examples,
commands and limits. They are not a standard graph byte grammar or a complete
independent native deployment-profile specification.
