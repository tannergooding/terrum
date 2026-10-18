# Persisted scanner invocation profile

**Status: deferred until an integrated compiler consumer and whole-input
resource policy justify persisted scanner execution.** Provisional
`scanner-10` support does not accept its source ceiling or binding model.

## Scope and alternatives

This candidate is a named private semantic-module JIT profile for the
Terrum-authored scanner, not a general multi-buffer registry. It extends the
binding/publication surface beyond the
[source-only grant](invocation-scoped-source-grant.md), while leaving the
[scanner contract](../scanner.md), [source input](../specs/language/source-input.md)
and [private-persistence distinction](../specs/representation/persistence.md)
unchanged.

Keep ordinary compilation-local scanner use unless persisted execution serves
a real integrated path. A general host-buffer capability or explicit view ABI
is a substantive alternative with wider ownership and compatibility work;
do not introduce either implicitly through this fixed component profile.
The [self-host inventory](../implementation/self-host.md) owns current
provisional support.

## Candidate identity and binding

Bind exactly the canonical `ScannerHost` descriptors in ordinal order:

| Ordinal | Buffer | Access and meaning |
|---|---|---|
| 0 | `Source` | Readonly `char16`, immutable UTF-16 snapshot |
| 1 | `XidStart` | Readonly `int32`, inclusive Unicode range pairs |
| 2 | `XidContinue` | Readonly `int32`, inclusive Unicode range pairs |
| 3 | `Tokens` | Writable `int32`, three words per token including real EOF |
| 4 | `Diagnostics` | Writable `int32`, count plus three words per record |
| 5 | `Trivia` | Writable `int32`, count plus four words per record |
| 6 | `Keywords` | Readonly `int32`, packed catalog |
| 7 | `Operators` | Readonly `int32`, packed catalog |
| 8 | `IntegerValues` | Writable `int32`, count plus five words per record |
| 9 | `EscapeKeywords` | Readonly `int32`, packed catalog |

`IntegerValues` is an output, not a readonly catalog. The current
16,384-code-unit source bound and source-derived capacities belong to the
candidate envelope, not the language or an accepted full-file policy.

Fingerprint the component/schema, source/resource envelope, descriptor order,
names/types/access and capacity formulas, readonly table contents, token and
record meanings. Names alone cannot establish compatibility. Preserve exact
identity through producer references, reconstruction, linking and replacement.
Persist the requirement and symbolic buffer operations, not seed values,
concrete source capacities, addresses or trusted analysis.

For each invocation, validate before entry and bind fresh correctly typed,
capacity-checked backing. Outputs must be mutually disjoint and cannot alias
readonly inputs; the canonical tables cannot be replaced by caller-selected
contents. Use checked capacity/aggregate arithmetic. Hold all backing through
synchronous calls and cleanup; publish no escaped native view.

## Publication and failure

After normal completion, copy and validate tokens, diagnostics, trivia and
integer values with the existing host readers before releasing native storage.
Return owned immutable results that cannot change during another invocation.
Lexical diagnostics are a **completed scan result**, not a runtime failure.
A runtime failure or malformed publication yields no successful partial
record set.

This profile must execute the persisted scanner: a hosted lexer fallback,
captured publication or source-only integer result is not scanner-profile
success. Scanner completion does not establish parsing, binding, emission,
frontend invocation or compiler bootstrap.

## Integration and exclusions

The private implementation currently retains structured buffer assignments
as private IR. Neither those tags nor a development revision number are TRRM
assignments. Promotion needs an exact requirement/IR schema and mandatory
reader/link verification, not a rule that every additive experiment preserves
every earlier private decoder.

The [frontend profile](scalar-frontend-invocation-profile.md) owns its distinct
twelve-buffer identity and complete program audit; it cannot silently reuse
`scanner-10`. Keep arbitrary profile composition, captured/returned views,
native DLLs/packages, graphs/module sets, persisted AOT and source-free TRRM
outside this candidate. If the consumer requires those, reopen the capability
design rather than treating them as incidental scanner support.

## Acceptance gates

1. Identify the integrated consumer and settle whole-input size, capacity and
   failure policy. Preserve strict file-byte decoding versus decoded snapshot
   behavior; do not normalize malformed UTF-16 grants.
2. Reuse identical artifacts with empty, varied, maximum and one-too-large
   sources under both runtime policies. Cover all four output families,
   malformed UTF-16, diagnostics, EOF, trivia, integer magnitudes and exact
   capacities against independent scanner oracles.
3. Reject missing/extra/reordered/wrong-access descriptors, altered tables,
   fingerprints, record tags, counts, spans, capacities and replacement
   requirements before unsafe execution or successful publication.
4. Exercise fresh-storage, retained-result and successive-invocation
   isolation, failure cleanup and absence of embedded seed contents.
5. Demonstrate the consumer continues through the appropriate hosted audits,
   verifier and emitter without a hosted-lexer fallback.

The [`ScannerHost`](../../sources/Terrum.CodeAnalysis/Lexing/ScannerHost.cs),
[`Scanner`](../../sources/self-host/Scanner.terrum) and
[`NativeJit`](../../sources/Terrum.CodeAnalysis/Native/NativeJit.cs) implementations
are evidence entry points. Their existence does not discharge these gates.
