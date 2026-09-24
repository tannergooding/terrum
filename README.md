# Terrum

Terrum (TAIR-um), a programming language from TerraFX.

This repository hosts the language design and the C# bootstrap compiler/tooling.
It is experimental; source syntax and public APIs are not stable.

## Build

Install the exact .NET SDK specified in `global.json`. The initial toolchain uses
.NET 11 RC, already available on the development host; no system SDK installation
is performed by the repository.

```powershell
dotnet build Terrum.slnx
```

Outputs go to `artifacts`. Builds enable nullable analysis, checked host integer
arithmetic, deterministic compilation and warnings as errors. Compiler arithmetic
is not the implementation of Terrum numeric semantics.

## Layout

`sources` contains compilable host code; `tests` will exercise its public surface.
`docs`, `library`, and `samples` hold maintained design and staged source, separate
from the host implementation. No compiler backend or runtime is implemented yet.

The bootstrap path targets .NET CLI IL, not LLVM. Terrum semantic IL will remain
distinct from its eventual CLI lowering.
