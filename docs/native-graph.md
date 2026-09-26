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
```

Compile commands never launch a consumer. The CLI takes `--source` for
additional source snapshots, reports original source locations, accepts
`--json` last, bounds package reads and preserves existing output on failure.
`validate-native-graph` and `run-native-graph` require the exact complete
closure, in any argument order. Packages are trusted native artifacts, not
an authenticated sandbox. Execution stages validated byte snapshots into a
private directory and supervises a single subprocess with a deadline.

This graph is acyclic and explicitly bounded. It does not claim transitive
version roll-forward, hot servicing, resilient layouts, full LMOD or
replacement of already-emitted machine code by the semantic JIT. Existing
single-provider and independent leaf-set package formats/APIs are unchanged.
Graph DLLs cannot publicly re-expose a dependency-owned unique type: doing
so would require exporting that type's cleanup implementation from the
wrong DLL, and graph exports must be locally implemented. They may consume
unique values through checked direct imports without republishing the type.
