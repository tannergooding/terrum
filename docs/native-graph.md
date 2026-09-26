# Bounded native dependency graphs

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
JIT requires a v2 root with explicit machine-import metadata; older graph
packages remain readable and executable by the AOT runner.
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
version roll-forward, hot servicing, resilient layouts, full LMOD or
replacement of already-emitted machine code by the semantic JIT. Existing
single-provider and independent leaf-set APIs still accept their v1 packages;
new consumer packages use v2 metadata to record emitted imports separately
from retained declarations. Existing leaf producer packages remain v1.

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

Graph v2 packages carry the declared origin and cleanup owner of every
retained type, separately from the actual PE imports reported by the same
native emission. A graph DLL may return or bool/out-publish a dependency-owned
Copy or unique value without exporting its cleanup from the wrong DLL. Select
the origin provider explicitly alongside the intermediate when compiling a
caller that uses the shared type; duplicate declarations merge only with the
same origin, compatible fixed layout and cleanup. Unrelated equal-named
types remain ambiguous. A dependency with no PE calls still needs a retained
declaration or imported type to justify its selection. Legacy v1 graphs remain
readable, but their imported Copy type origins cannot be inferred from names;
they cannot participate in a same-origin merge without v2 provenance.

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
