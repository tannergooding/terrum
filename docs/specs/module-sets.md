# Native deployment sets

A native deployment set selects one consumer image and multiple provider
images. A flat leaf-only set is distinct from a
[transitive native graph](native-graph.md). The selected deployment profile
determines which topology, value modes, runtime domains and failure/cleanup
transport it admits.

The selected [Windows x64 profile](targets/windows-x64-deployment.md#4-selected-closure-origin-and-replacement)
preserves complete fixed rootless edge and allocator/cleanup-origin contracts;
its scope is independent of the private development set's narrower examples.

Selection preserves each producer's identity, complete retained contract and
the [binding relation](representation/binding-and-replacement.md). Provider
argument order is not semantic identity or a compatibility preference.
The [native identity contract](native.md#native-package-identity-and-filename-collisions)
keeps case-sensitive ASCII package selection separate from target filename
collision rejection before staging or launch.
Retained declarations and type/cleanup dependencies are distinct from emitted
machine imports: an obligation remains even when no call/IAT entry names it.
Conversely, every actual import must have its required admitted provider.

Before affected execution, establish the complete selected dependency,
binding, target/runtime and image-admission obligations. Matching exports or
successful platform loading alone is insufficient. Compatible provider
replacement preserves the unchanged consumer's required contracts and
[code/cleanup lifetime](native.md#runtime-and-code-lifetime); it does not
automatically rewrite or recompile that consumer.

Private packaging does not generalize the development leaf-set value surface
to unique owners, managed handles, references or views. Each crossing still
needs a complete admitted profile. The topology itself grants no shared heap,
root transfer or safe unloading.

The `.tnset` container is [private persistence](representation/persistence.md).
Its version, magic, numerical implementation limits, stored semantic data and
CLI workflow are in the informative
[native-set implementation guide](../implementation/module-sets.md).
Those bytes are not a Terrum-standard independently produced deployment set.
