# Bootstrap and scalar-frontend composition

This is the current documented support inventory, not an implementation audit
or adoption of a private format. The [self-host reference](../../self-host/README.md)
owns component-level coverage. Only the
[remaining profile decisions](../proposals/bootstrap-frontend-profiles.md)
are proposals; the superseded per-increment worklists are removed.
Language grammar, lookup, arithmetic and dependency contracts remain
independent of these narrower paths.

## Supported paths and prerequisites

| Path | Prerequisites and supported combination | Boundary |
|---|---|---|
| Standalone scalar projection | Scanner/source validation; current private v87 projection | Not a complete-program or summary-profile success |
| Complete scalar frontend | Original source, scanner tables and runtime policy; current private v88 program | Hosted source audit, binding, verification and emission are still required |
| v88 summary decoding | Completed v88 publication, zero publication scratch and unchanged complete-program buffer | Structural summary only; not semantic/source verification |
| Persisted frontend invocation | Exact summary profile, fresh source grant and twelve invocation-owned buffers; revision-77 private-span IR | Host audits original source/program/policy and compares every summary word before returning owned results |
| Projected bootstrap Main | Host-audited v88 FrontendProgram; supported global parameterless int32 Main leaf | Separate emitter, not general frontend-to-native lowering |
| Direct-source bootstrap | Terrum scanner/integer parser, bounded checked expression parser and supplied source/workspace/code/image capacities | Does not require or consume the audited v88 projection |
| Import-free bootstrap PE | Successful source emission, admitted exact code/frame shapes, checked PE sizing and unwind records | Not a reusable linker, arbitrary machine-code input or general ABI implementation |

Persisted invocation does not silently compose with standalone v87, scanner-only
success, returned spans, general view persistence or arbitrary buffer profiles.
The host must preserve original source, runtime policy, successful publication
and program independently before decoding overwrites the Frontend scratch.
`ScalarFrontendHost.ReadProgram` audits those originals; a summary alone is
not authoritative.

## Current bounded source support

| Route | Documented provisional support | Still excluded |
|---|---|---|
| Direct Main leaf | Expression body or explicit return; original single-operation encodings plus checked precedence/grouping/unary folding; immutable locals with earlier-local visibility | General local storage, statements and runtime expression lowering |
| Parameterless helper | Existing leaf or folded expression/local block; callee-first linear forwarders; Main calls the outer helper | Arbitrary call graphs, recursion, arithmetic around call results |
| Parameterized helper | int32 identity or admitted sum/subtraction/product leaf; same-arity declaration-order forwarding; folded Main call arguments and source-sized locals | Reordered/modified forwarding arguments, other signatures and general helper bodies |
| Direct-source namespace | One optional leading dotted file-scoped namespace; unqualified same-source helper calls | Aliases, linking, general namespace lookup or metadata |
| Complete scalar namespace | File-scoped namespace, one block, repeated flat blocks with the same qualified identity, or a bounded nested-block chain; supported same-leaf aliases | Different namespaces, sibling nested blocks, repeated-flat-block aliases and parent-alias implementation support |
| Complete scalar calls | Current v88 four-parameter projection and its checked argument/type/mode mapping | Adoption of the discarded v86 encoding or a general public call ABI |
| Cleanup switch | Audited private arm dispatcher with acyclic finite-exit proof; bounded strict unit-step counted-for recognition | Cycles or unproved data-dependent iteration; cleanup escapes |

Grouping, precedence, unary operands and group unary are cumulative current
support, not simultaneously forbidden by a pre-grouping increment.
Older exact-one-forwarder, fixed two-local, 32-argument/fixed-section and
per-function-token restrictions describe superseded stages, not current
aggregate support. Preserve existing fully parsed single-operation leaf
encodings when later expressions fall back to folding.

Same-leaf alias support is narrower than selected enclosing-scope/namespace-prefix
[lookup](../specs/language/names-and-binding.md); Terrum does not prohibit
parent aliases. Similarly a bounded folder can decline an unsupported input,
but signed minimum remainder by -1 is canonically zero, not language remainder
overflow. Division and combined DivRem retain their quotient-overflow rule.

## Exact private identities and resources

Current private readers use standalone **v87** and complete-program **v88**;
v1 through v86 are not current accepted reader inputs. These tags are not
language versions or public metadata assignments.

| Dimension | Current documented private requirement |
|---|---|
| Source | At most 16,384 UTF-16 code units; not a language/source ABI limit |
| Frontend per-function workspace | 8 named/hidden locals, 12 statements, 64 expression nodes, 16 expression recursion levels, 6 structural levels |
| Complete frame | 4 header + 712 body + 64 call-target words = 780 int32 words |
| Complete program | 4 + 32 * 780 + 61 reserved words = 25,025 int32 words |
| Frontend scratch/summary | 2 + 32 * 7 + 32 * 64 = 2,274 int32 words |
| Summary | v88 tag/status; 32 seven-word declaration descriptors at words 2–225; 2,048 node/call-target slots at words 226–2,273; unused slots zero |
| `scalar-frontend-v88-summary-1` | Ten scanner descriptors followed by writable Frontend and FrontendProgram; 64 total scanner tokens including real EOF; exclusive frame token-end limit 63 |
| `scalar-frontend-v88-summary-2` | Same twelve-buffer shape; source-derived maximum 16,385 tokens including EOF; source-bounded exclusive frame token-end limit; no separate current per-function token-count gate |
| Private span persistence | Exact semantic-module revision 77, compiler-known Token/SourceRange/TokenSnapshot roles and mandatory backing/lifetime revalidation after read/link |
| Legacy scalar module | Revision 76 still rejects span IR; a compatible scalar consumer can link an admitted revision-77 private provider without acquiring a public span ABI |
| Direct-source counts | Source/workspace/output capacities and checked planning/format ranges, not an enumerated arity, local-count or forwarding-depth ceiling |
| Windows x64 helper calls | Four argument registers, subsequent eight-byte stack slots, shadow space/alignment; large-frame page probing and matching unwind allocation record |

Function capacity and tokens are independent axes. Earlier ten-function
recommendations are not silently adopted as the current 32-frame capacity,
and profile 2 does not widen profile 1. Profile fingerprints must bind scanner
identity, both additional descriptors/capacities, layout/summary, resource
ceilings and entry/result convention; names alone do not establish compatibility.
Source-free execution, exported/imported span signatures, returned spans,
cursor captures/advance, persisted AOT and general native source ABI remain
outside these private profiles.

## Selected source-code and image publication direction

Code emission and PE image creation publish independently. This table states
the accepted documentation contract, not a claim that every WIP entry already
implements it.

| Outcome | Published code | Published PE image | Composed success |
|---|---|---|---|
| Parse, validation, planning or code-capacity refusal before emission succeeds | None; code length zero | None; image length zero | No |
| Successful source emission, before image creation | Valid complete code and its length | None yet | Not yet |
| Later PE validation, planning or image-capacity refusal | Previously published valid code remains valid | None; image length zero | No |
| Both phases succeed | Valid complete code | Valid complete image | Yes |

No failure publishes a partial code/image length. Each phase retains its own
input validity and capacity checks; composed false does not retroactively
invalidate a successful emission. Consumers must not execute an image after
image failure or mistake valid intermediate code for overall source-to-PE
success. Required validation covers exact and one-short capacities for each
phase, semantic/emission rejection, late PE rejection, retained code validity,
stack/unwind/PE ranges and unsupported combinations.
