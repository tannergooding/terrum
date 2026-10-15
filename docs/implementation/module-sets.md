# Native module sets

> **Informative development reference.** `.tnset` bytes, reader versions,
> support limits and command examples are private implementation contracts.
> Canonical [native-set boundaries](../specs/module-sets.md) and the
> [persistence taxonomy](../specs/representation/persistence.md) remain separate.

## Definitions and scope

A **module set** is one persisted consumer EXE plus two to sixteen selected
provider DLL packages. A **provider** supplies public exports; the consumer
imports only the selected exports. A **retained declaration** preserves a
contract needed for validation or type/cleanup checking, while an **actual
machine import** is an emitted call represented in the PE import table.
Declaration retention does not by itself create a machine import.

A module set is a flat, leaf-only native deployment contract. The broader
compiler support boundary is in [implementation](../implementation.md); future
formats and servicing work are tracked in [roadmap](../roadmap.md).
The accepted [TRRM binding relation](../specs/representation/binding-and-replacement.md)
does not relax these development package, exact-layout or native ABI gates.

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
from actual machine imports; the reader accepts only version 2. Actual imports
come from the same emission's fixups, including implicit recursive cleanup calls.
They must belong to the selected provider's retained declarations, cover
semantic calls, and exactly match the persisted PE's import table. Keeping a
declaration for type or cleanup contracts does not itself create an IAT entry.
Source-authored direct native imports are retained in the consumer's semantic
module and checked against the same PE import table, separately from provider
exports. Their arbitrary DLLs are not set dependencies or staged by set
execution; the Windows loader must resolve them under its normal search policy.

The set is intentionally leaf-only: it does not model transitive graphs,
cycles, arbitrary version roll-forward, hot servicing, resilient layouts,
or the future general `TRRM` semantic-IL format. Current modules permit a
native-set consumer to retain private managed state, but image boundaries
remain limited to scalar and
public handle-free `Copy` value contracts, including existing conditional
`bool`/`out` results. Handles, unique values, views, reference modes and
mutating-receiver ABI do not cross the boundary; each persisted image has its
own heap/root domain, while collection stays local. This uses the existing
native profile and package versions and adds no physical ABI change. It does
not add a dedicated native-set JIT API. Two providers cannot both claim the same public
type or qualified export name; DLL basenames also cannot collide under the
implementation's case-insensitive destination check. The
[selected native identity contract](../specs/native.md#native-package-identity-and-filename-collisions)
keeps that filename check separate from case-sensitive ASCII package equality.
This private inventory does not establish complete identity/path conformance.
The selected dependencies must match exactly and each must supply a retained
declaration; only emitted calls require machine imports.
Implementation hashes are not pinned. These packages remain trusted native
artifacts rather than an authenticated sandbox; validate before executing,
and supervise the generated process with a deadline. Existing semantic JIT
linking handles one producer; it does not replace an already emitted native
consumer or claim multi-provider JIT execution.
