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
