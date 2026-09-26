# Native module sets

A native module set persists **one** Windows x64 consumer EXE and its checked
semantic contract. Two to sixteen independently compiled leaf DLL packages
provide distinct, unambiguous public exports. Identity selects the DLL, not
the order of `--provider` arguments. The consumer imports only those named
exports, and validation checks the selected packages, contracts, policy,
profiles, and PE import ownership before any process starts. A compatible
replacement changes the provider image without changing the consumer bytes.

From the repository root, after building `terrumc`:

```powershell
$c = "artifacts\bin\sources\terrumc\Debug\net11.0\terrumc.exe"
& $c compile-native producer samples\native\module-sets\first.terrum First --export Alpha -o first.trmn --runtime fail-fast
& $c compile-native producer samples\native\module-sets\second.terrum Second --export Beta -o second.trmn --runtime fail-fast
& $c compile-native-set samples\native\module-sets\consumer.terrum --provider second.trmn --provider first.trmn -o consumer.tnset --runtime fail-fast
& $c validate-native-set consumer.tnset --provider first.trmn --provider second.trmn
& $c run-native-set consumer.tnset --provider second.trmn --provider first.trmn --runtime fail-fast
```

The last process exits with status 42. Compile replacement producers with
the same identities and exports using `first-replacement.terrum` or
`second-replacement.terrum`, then rerun validation and execution with the
unchanged `consumer.tnset`: each replacement gives status 43. Use `$LASTEXITCODE`
to inspect the child status in PowerShell. Compilation never runs the consumer.
`--json` is supported as the final argument on all three set commands.
Output artifacts are atomically replaced only after successful compilation.

The `TNSP` version 2 container records retained declaration ownership separately
from actual machine imports; version 1 remains readable. Actual imports come
from the same emission's fixups, including implicit recursive cleanup calls.
They must belong to the selected provider's retained declarations, cover
semantic calls, and exactly match the persisted PE's import table. Keeping a
declaration for type or cleanup contracts does not itself create an IAT entry.

The set is intentionally leaf-only: it does not model transitive graphs,
cycles, arbitrary version roll-forward, hot servicing, resilient layouts,
or full LMOD. Two providers cannot both claim the same public type or
qualified export name; case-insensitive DLL identities also cannot collide.
The selected dependencies must match exactly and each must supply a retained
declaration; only emitted calls require machine imports.
Implementation hashes are not pinned. These packages remain trusted native
artifacts rather than an authenticated sandbox; validate before executing,
and supervise the generated process with a deadline. Existing semantic JIT
linking handles one producer; it does not replace an already emitted native
consumer or claim multi-provider JIT execution.
