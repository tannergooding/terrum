# Bounded native dependency graphs

## Definitions and scope

A **root** is the persisted consumer EXE and its direct imports. An
**intermediate** is a `.trng` DLL package that both provides exports and may
import other providers; a **leaf** is a `.trmn` producer package with no
provider dependency. A **selected closure** is the complete set of packages
needed to satisfy the root's transitive imports. A **candidate catalog** is an
explicit local list from which the resolver must select one unique compatible
closure.

This document specifies implemented bounded, acyclic native graphs and their
CLI behavior. It does not define a general package manager or native-code
sandbox. See [implementation](implementation.md) for the broader compiler
support boundary and [roadmap](roadmap.md) for future format work.

A `.trng` library package stores one verified DLL, its symbolic semantic
library, public exports and exact direct DLL-import ownership. A `.trng` root
stores one already emitted consumer EXE and its direct imports. Leaf `.trmn`
producers remain unchanged. Each graph may contain at most 16 distinct
producers. Validation walks the *entire selected closure*, rejecting missing
or extra packages, cycles, case-ambiguous identities, incompatible public
signatures and layouts, changed ownership/NoThrow contracts, mismatched
policies, malformed images and import maps before the root process starts.
Implementation hashes are not pinned: a compatible leaf implementation can
replace an independently compiled package without changing the intermediate
DLL or root EXE.
For managed roots and intermediate producers in persisted acyclic native DLL
graphs, each participant uses current semantic-module revision 68, the sole
accepted revision. This serialization revision is distinct from the graph
container and native package formats. Participants
communicate only through the established scalar and handle-free public `Copy`
value contracts. Each persisted image has its own heap and root chain; an
imported call preserves the caller's roots, and `ManagedHeap.Collect()` remains
local to the image where it runs. The invocation-scoped `Host.Source`
requirement is a semantic-module JIT capability, not a native graph capability.
The same current semantic-module revision
supports private managed consumers through ordinary single-provider semantic
linking and native single-provider or module-set entry points. Semantic linking places producer
and consumer code in one image/heap domain and remaps private class identities;
the persisted native graph continues to keep each image's heap separate.
Managed handles never cross image boundaries, and graph cycles remain
unsupported.

`resolve-native-graph` accepts an explicitly listed local candidate catalog
rather than an already selected closure. It validates every package, ignores
unrelated valid candidates, and reports the one complete compatible closure
with selected paths and SHA-256 hashes. Byte-identical catalog entries count
once; distinct compatible implementations of the same identity are ambiguous,
not ranked by profile, version, input order or hash. Missing dependencies,
incompatible contracts, cycles, malformed candidates and unproven uniqueness
fail without executing or writing an artifact. The bounds are 32 candidates,
64 MiB of aggregate root and candidate bytes, 16 selected producers and 4096
candidate assignments. Exhausting the search budget fails even if a compatible
closure was found earlier. Output paths are informational; use the selected
packages with an existing `run-native-graph` or `run-native-graph-jit` command,
which revalidates their bytes before execution. No directory scanning or
remote discovery occurs.

Build `terrumc`, then from the repository root run:

```powershell
$c = "artifacts\bin\sources\terrumc\Debug\net11.0\terrumc.exe"
& $c compile-native producer samples\native\graph\leaf.terrum NodeB --export Leaf -o leaf.trmn --runtime fail-fast
& $c compile-native-graph producer samples\native\graph\middle.terrum NodeA --provider leaf.trmn --export Middle -o middle.trng --runtime fail-fast
& $c compile-native-graph consumer samples\native\graph\consumer.terrum --provider middle.trng -o root.trng --runtime fail-fast
& $c validate-native-graph root.trng --provider middle.trng --provider leaf.trmn
& $c run-native-graph root.trng --provider leaf.trmn --provider middle.trng --runtime fail-fast
$LASTEXITCODE # 42
& $c compile-native producer samples\native\graph\leaf-replacement.terrum NodeB --export Leaf -o leaf-new.trmn --runtime fail-fast
& $c validate-native-graph root.trng --provider middle.trng --provider leaf-new.trmn
& $c run-native-graph root.trng --provider middle.trng --provider leaf-new.trmn --runtime fail-fast
$LASTEXITCODE # 43
& $c run-native-graph-jit root.trng --provider middle.trng --provider leaf-new.trmn --runtime fail-fast
$LASTEXITCODE # 43
& $c compile-native producer samples\native\graph\leaf.terrum Extra --export Leaf -o extra.trmn --runtime fail-fast
& $c resolve-native-graph root.trng --candidate extra.trmn --candidate leaf-new.trmn --candidate middle.trng --json
& $c resolve-native-graph root.trng --candidate leaf.trmn --candidate leaf-new.trmn --candidate middle.trng
# Ambiguous: both NodeB implementations satisfy the persisted contracts.
```

Compile commands never launch a consumer. The CLI takes `--source` for
additional source snapshots, reports original source locations, accepts
`--json` last, bounds package reads and preserves existing output on failure.
`validate-native-graph` and `run-native-graph` require the exact complete
closure, in any argument order. Packages are trusted native artifacts, not
an authenticated sandbox. Execution stages validated byte snapshots into a
private directory and supervises a single subprocess with a deadline.
`run-native-graph-jit` accepts the same explicit closure and runtime policy.
It first validates all packages and graph edges, then starts a supervised
`terrumc.exe` apphost worker. The worker revalidates the staged snapshots and DLL
images before loading any provider; it reconstructs only the root's verified
semantic program and emits root machine code through the ordinary encoder.
Every actual JIT fixup must match the root package's recorded machine imports.
Selected leaf and intermediate DLL images remain byte-for-byte unchanged.
The JIT worker uses its application directory as its current directory and
absolute paths for staged inputs, so a retained worker-directory handle cannot
prevent removal of the transient staging directory after execution.
The graph reader accepts only version 2, and both JIT and AOT execution require
its explicit machine-import metadata.
The worker loads DLLs by full staged path in dependency order, registers
unwind information for root code, and unloads DLLs after root code and unwind
memory are released. This requires Windows x64. On CET-enforcing Windows, the
supervisor registers the worker's validated exception continuations through
private inherited pipes, bound to the child it launched. The bounded target
set must lie within one RX code region; unwind metadata is removed before
continuation deregistration, code release and DLL unload. Failed deregistration
retains the code instead of freeing a registered target. The supervisor never
executes native artifacts; target registration needs Windows build 20348 or
later and the dynamic registration API. Direct apphost `jit` uses a separate
framework-dependent .NET registrar bound to its creator process, which likewise
never executes generated code. It is process-start code generation, not
in-process replacement of live code or a native-code sandbox.

This graph is acyclic and explicitly bounded. It does not claim transitive
version roll-forward, hot servicing, resilient layouts, the future general
`TRRM` semantic-IL format, or replacement of already-emitted machine code by
the semantic JIT. Single-provider, leaf-set and graph package readers accept
only version 2; version 2 metadata records emitted imports separately from
retained declarations.

An implementation may use lexical readonly views over an owned native buffer
without changing its exported declaration. Such a producer carries the
`native-16` semantic profile, while an unchanged root and intermediate retain
their original profiles and machine images when the implementation is replaced.
Views cannot escape through public parameters, returns or fields. The checked
package reader rejects unknown or inconsistent profiles before loading a DLL;
the native image validator permits owned-buffer heap imports only for the
`native-15`, `native-16` and `native-17` profiles.

To replace a leaf without changing the root or intermediate, use the scalar
`Leaf()` package first, then compile the implementation with an internal view:

```powershell
& $c compile-native producer samples\native\graph\scoped-view-leaf.terrum NodeB --export Leaf -o scoped-leaf.trmn --runtime fail-fast
& $c compile-native-graph producer samples\native\graph\middle.terrum NodeA --provider scoped-leaf.trmn --export Middle -o scoped-middle.trng --runtime fail-fast
& $c compile-native-graph consumer samples\native\graph\consumer.terrum --provider scoped-middle.trng -o scoped-root.trng --runtime fail-fast
& $c run-native-graph-jit scoped-root.trng --provider scoped-middle.trng --provider scoped-leaf.trmn --runtime fail-fast
$LASTEXITCODE # 42
& $c compile-native producer samples\native\graph\scoped-view-leaf-replacement.terrum NodeB --export Leaf -o scoped-leaf-new.trmn --runtime fail-fast
& $c validate-native-graph scoped-root.trng --provider scoped-middle.trng --provider scoped-leaf-new.trmn
& $c run-native-graph scoped-root.trng --provider scoped-middle.trng --provider scoped-leaf-new.trmn --runtime fail-fast
$LASTEXITCODE # 44
& $c run-native-graph-jit scoped-root.trng --provider scoped-middle.trng --provider scoped-leaf-new.trmn --runtime fail-fast
$LASTEXITCODE # 44
```

An internal exclusive writable view and `OwnedInt32Buffer.Clear()` may likewise
replace a leaf implementation without changing its exported `Leaf()` contract.
The new leaf uses `native-17`; previously persisted `native-15` and `native-16`
packages remain readable. Writable views cannot appear in public value
contracts or escape their lexical scopes. The package reader checks profiles
and contracts before loading any native image.

```powershell
& $c compile-native producer samples\native\graph\writable-view-leaf-original.terrum NodeB --export Leaf -o writable-old.trmn --runtime fail-fast
& $c compile-native-graph producer samples\native\graph\middle.terrum NodeA --provider writable-old.trmn --export Middle -o writable-middle.trng --runtime fail-fast
& $c compile-native-graph consumer samples\native\graph\consumer.terrum --provider writable-middle.trng -o writable-root.trng --runtime fail-fast
& $c run-native-graph writable-root.trng --provider writable-middle.trng --provider writable-old.trmn --runtime fail-fast
$LASTEXITCODE # 42
& $c compile-native producer samples\native\graph\scoped-view-leaf-replacement.terrum NodeB --export Leaf -o writable-readonly.trmn --runtime fail-fast
& $c run-native-graph writable-root.trng --provider writable-middle.trng --provider writable-readonly.trmn --runtime fail-fast
$LASTEXITCODE # 44
& $c compile-native producer samples\native\graph\writable-view-leaf.terrum NodeB --export Leaf -o writable-new.trmn --runtime fail-fast
& $c validate-native-graph writable-root.trng --provider writable-middle.trng --provider writable-new.trmn
& $c run-native-graph writable-root.trng --provider writable-middle.trng --provider writable-new.trmn --runtime fail-fast
$LASTEXITCODE # 46
& $c run-native-graph-jit writable-root.trng --provider writable-middle.trng --provider writable-new.trmn --runtime fail-fast
$LASTEXITCODE # 46
& $c compile-native-graph producer samples\native\graph\writable-view-middle.terrum NodeA --provider writable-old.trmn --export Middle -o writable-middle-new.trng --runtime fail-fast
& $c run-native-graph-jit writable-root.trng --provider writable-middle-new.trng --provider writable-new.trmn --runtime fail-fast
$LASTEXITCODE # 46
```

A leaf may use image-private `ManagedNode` allocation and explicit collection
behind the same scalar `Leaf()` declaration. The persisted root and intermediate
still import only `Leaf`; replacing the leaf does not re-emit either machine
image. The replacement selects `native-18` and carries checked lexical root
metadata. Its native heap imports are accepted only for the matching verified
profile. The example calls `ManagedHeap.Collect()` inside the leaf after its
unreachable cycles retire, so repeated calls do not depend on roots in another
image. It does not export managed handles, transfer roots across DLLs, or
promise cleanup of live heap objects on DLL unload.

```powershell
& $c compile-native producer samples\native\graph\managed-node-leaf-original.terrum NodeB --export Leaf -o node-original.trmn --runtime fail-fast
& $c compile-native-graph producer samples\native\graph\middle.terrum NodeA --provider node-original.trmn --export Middle -o node-middle.trng --runtime fail-fast
& $c compile-native-graph consumer samples\native\graph\consumer.terrum --provider node-middle.trng -o node-root.trng --runtime fail-fast
& $c compile-native producer samples\native\graph\managed-node-leaf.terrum NodeB --export Leaf -o node-new.trmn --runtime fail-fast
& $c run-native-graph node-root.trng --provider node-middle.trng --provider node-new.trmn --runtime fail-fast
$LASTEXITCODE # 44
& $c run-native-graph-jit node-root.trng --provider node-middle.trng --provider node-new.trmn --runtime fail-fast
$LASTEXITCODE # 44
```

The same unchanged root and intermediate can instead load a class-backed leaf.
Its private `Cycle` objects have optional edges; the leaf snapshots across edge
clearing, restores the cycle, then collects it after its roots retire. The replacement carries a
current semantic-module class table and verified `native-20` profile. A class leaf
without optional handles selects `native-19`. Neither class layout nor handle
is part of the imported `Leaf()` declaration, and no other DLL can share the
leaf's managed roots.

The [Copy-field variant](../samples/native/graph/managed-copy-class-leaf.terrum)
can replace that same leaf with nested inline Copy values alongside its optional
edges. Use it in place of `managed-class-leaf.terrum` in the producer command
below. It writes current semantic-module revision 68 while retaining the
`native-20` profile, returns the same result and collects its private cycle.
The unchanged root and intermediate do not import its nested struct layouts;
retired semantic-module revisions are rejected and must be rebuilt from source.

```powershell
& $c compile-native producer samples\native\graph\managed-class-leaf-original.terrum NodeB --export Leaf -o class-original.trmn --runtime fail-fast
& $c compile-native-graph producer samples\native\graph\middle.terrum NodeA --provider class-original.trmn --export Middle -o class-middle.trng --runtime fail-fast
& $c compile-native-graph consumer samples\native\graph\consumer.terrum --provider class-middle.trng -o class-root.trng --runtime fail-fast
& $c compile-native producer samples\native\graph\managed-class-leaf.terrum NodeB --export Leaf -o class-new.trmn --runtime fail-fast
& $c run-native-graph class-root.trng --provider class-middle.trng --provider class-new.trmn --runtime fail-fast
$LASTEXITCODE # 44
& $c run-native-graph-jit class-root.trng --provider class-middle.trng --provider class-new.trmn --runtime fail-fast
$LASTEXITCODE # 44
```

Semantic v31 also lets a managed-backed leaf export a public handle-free
compact `Copy` value without exporting its private class or managed handles.
The original and replacement below expose the same `Snapshot` type and
`ReadSnapshot`, `TryReadSnapshot` and `ReadTotal` signatures. The unchanged
consumer returns 40 with the original and 42 with the replacement. The managed
implementation builds a private two-node cycle, copies out its `Snapshot`,
collects both private records, then returns the independent value.

```powershell
$policies = 'fail-fast', 'checked-exceptions'
foreach ($policy in $policies) {
    $original = "copy-original-$policy.trmn"
    $replacement = "copy-managed-$policy.trmn"
    $root = "copy-root-$policy.trng"
    & $c compile-native producer samples\native\graph\copy-contract-original.terrum NodeB --export ReadSnapshot --export TryReadSnapshot --export ReadTotal -o $original --runtime $policy
    & $c compile-native producer samples\native\graph\copy-contract-managed.terrum NodeB --export ReadSnapshot --export TryReadSnapshot --export ReadTotal -o $replacement --runtime $policy
    & $c compile-native-graph consumer samples\native\graph\copy-contract-consumer.terrum --provider $original -o $root --runtime $policy
    & $c run-native-graph $root --provider $original --runtime $policy
    $LASTEXITCODE # 40
    & $c run-native-graph $root --provider $replacement --runtime $policy
    $LASTEXITCODE # 42
    & $c run-native-graph-jit $root --provider $replacement --runtime $policy
    $LASTEXITCODE # 42
}
```

The following graph places private managed records in both the root and the
intermediate DLL. The middle exports `ForwardSnapshot` and `MiddleValue`;
the root also retains a root-local managed cycle while calling into the middle.
Both communicate with the managed leaf through the leaf's existing
`Snapshot`/scalar contracts. The persisted root and middle are unchanged when
the compatible managed leaf replaces the original. The root, middle and leaf
have independent managed heaps and collection state; the samples deliberately
do not compare collection counts across images.

```powershell
$policies = 'fail-fast', 'checked-exceptions'
foreach ($policy in $policies) {
    $original = "copy-original-$policy.trmn"
    $replacement = "copy-managed-$policy.trmn"
    $middle = "managed-copy-middle-$policy.trng"
    $root = "managed-copy-root-$policy.trng"
    & $c compile-native producer samples\native\graph\copy-contract-original.terrum NodeB --export ReadSnapshot --export TryReadSnapshot --export ReadTotal -o $original --runtime $policy
    & $c compile-native producer samples\native\graph\copy-contract-managed.terrum NodeB --export ReadSnapshot --export TryReadSnapshot --export ReadTotal -o $replacement --runtime $policy
    & $c compile-native-graph producer samples\native\graph\managed-copy-middle.terrum NodeA --provider $original --export ForwardSnapshot --export MiddleValue -o $middle --runtime $policy
    & $c compile-native-graph consumer samples\native\graph\managed-copy-root.terrum --provider $middle --provider $original -o $root --runtime $policy
    & $c run-native-graph $root --provider $middle --provider $original --runtime $policy
    $LASTEXITCODE # 42
    & $c run-native-graph-jit $root --provider $middle --provider $original --runtime $policy
    $LASTEXITCODE # 42
    & $c run-native-graph $root --provider $middle --provider $replacement --runtime $policy
    $LASTEXITCODE # 44
    & $c run-native-graph-jit $root --provider $middle --provider $replacement --runtime $policy
    $LASTEXITCODE # 44
}
```

Graph version 2 packages carry the declared origin and cleanup owner of every
retained type, separately from the actual PE imports reported by the same
native emission. A graph DLL may return or bool/out-publish a dependency-owned
Copy or unique value without exporting its cleanup from the wrong DLL. Select
the origin provider explicitly alongside the intermediate when compiling a
caller that uses the shared type; duplicate declarations merge only with the
same origin, compatible fixed layout and cleanup. Unrelated equal-named
types remain ambiguous. A dependency with no PE calls still needs a retained
declaration or imported type to justify its selection.

The following source-to-DLL-to-EXE example owns two leaf-created values in
the root and drops them using the leaf's cleanup. Its middle DLL and root EXE
are unchanged when the leaf is replaced:

```powershell
& $c compile-native producer samples\native\graph\owned-leaf.terrum Leaf --export Make --export Ticket.Close -o owned-leaf.trmn --runtime fail-fast
& $c compile-native-graph producer samples\native\graph\owned-middle.terrum Middle --provider owned-leaf.trmn --export Forward --export TryForward -o owned-middle.trng --runtime fail-fast
& $c compile-native-graph consumer samples\native\graph\owned-consumer.terrum --provider owned-middle.trng --provider owned-leaf.trmn -o owned-root.trng --runtime fail-fast
& $c run-native-graph owned-root.trng --provider owned-middle.trng --provider owned-leaf.trmn --runtime fail-fast
$LASTEXITCODE # 42
& $c compile-native producer samples\native\graph\owned-leaf-replacement.terrum Leaf --export Make --export Ticket.Close -o owned-leaf-new.trmn --runtime fail-fast
& $c run-native-graph owned-root.trng --provider owned-middle.trng --provider owned-leaf-new.trmn --runtime fail-fast
$LASTEXITCODE # 44
& $c run-native-graph-jit owned-root.trng --provider owned-middle.trng --provider owned-leaf-new.trmn --runtime fail-fast
$LASTEXITCODE # 44
```
