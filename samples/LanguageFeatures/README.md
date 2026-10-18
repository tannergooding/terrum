# Language feature samples

These Terrum files isolate language concepts rather than package a complete
application. Some demonstrate implemented compiler behavior; others describe
target-language contracts that still require unimplemented facilities. The
[sample catalog](../../docs/samples.md) identifies the evidence and support
boundary for each runnable or design-only example.

| Directory | Topic |
|---|---|
| `Collections` | Collection construction and lookup design |
| `ConstructionAndFormatting` | Value construction, text formatting, and ownership design |
| `Contracts` | Requirements, effect contracts, and declaration-contract design |
| `ControlFlow` | Loops, recursion, and structured control flow |
| `EffectsAndCleanup` | Cleanup and checked-recovery behavior |
| `Functions` | Calling convention and argument-placement examples |
| `Generics` | Bounded generic behavior and broader generic design |
| `ManagedStorage` | Managed classes, optional links, and cycle behavior |
| `Numerics` | Generic numerics and tensor design |
| `NumericTypes` | Integer-width and conversion examples |
| `Ownership` | Unique owners, transfers, buffers, and ownership design |
| `StaticProtocols` | Source-defined arithmetic and comparison protocols |
| `TextAndSpans` | Text, encoding, and span design |
| `Views` | Scoped views and writable receiver paths |

Do not infer implementation support from a folder name or a successful parse.
Design syntax is reviewed against the specifications; runnable sources are
qualified only by their linked semantic, emission, or execution evidence.
