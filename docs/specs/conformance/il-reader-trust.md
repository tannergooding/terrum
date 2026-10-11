# Reader-trust conformance

These cases apply [reader trust](../representation/reader-trust.md),
[metadata](../il-metadata.md), [Body encoding](../il-instruction-set.md) and
the exact `trrm/1-draft-unified-3` registry. Producer validity, required
access checks, whole-artifact structural validation, semantic audit and
canonicality are distinct claims. A false assertion may remain undetected
by an otherwise conforming trusted selective reader; it remains false.

The compact indexed Body below contains two independent int32-returning
blocks. A trusted seek to block 1's terminator does not inspect block 2's
operations or even block 1's preceding ConstBits. A structural audit instead
decodes both counted streams and compares their indexes:

```text
08 02 02 00 01  01 00 00 00  01 01 01 01
00 01 02  02 02  00 08 10  06 06
00 01 01 01 01 2A 83 01  00 01 01 02 01 2B 83 02
```

Its header/index storage is 23 bytes; each block occupies eight bytes.
Definition offsets are 2, terminator offsets 6, and only the type-map words
are artifact-wide table references. Equivalent wider/general forms retain
the same access and trust boundary; compact linear derives omitted facts
by a real one-block scan.

## 1. Trust and complete requirements

| Case | Input or observation | Required outcome |
|---|---|---|
| ITRU-01 | Explicit environment policy admits a producer's valid indexed Body | Permit selective seeks without a preliminary instruction-stream scan |
| ITRU-02 | Producer is untrusted and the consumer has no adequate audit | Decline safe execution; bounded indexes are not semantic proof |
| ITRU-03 | An adequate audit establishes the needed finite supplied facts | Permit that checked scope without treating it as arbitrary-program certification |
| ITRU-04 | Artifact identity, signature, checksum or self-declared trust bit is offered | None independently supplies the required trust/audit basis |
| ITRU-05 | Unknown required section kind appears in the directory | Reject while establishing the section map, even for trusted input |
| ITRU-06 | Unknown opcode exists only in an unvisited block or uncalled Body | Producer is nonconforming; a trusted selective read need not detect it, while whole-scope structural validation rejects |
| ITRU-07 | The unknown opcode is actually decoded or used | Reject the unknown form; no guessing, skipping or execution |
| ITRU-08 | Unsupported semantics are declared anywhere in the retained Module requirement closure | Reject before affected behavior, including requirements of uncalled retained declarations |
| ITRU-09 | An unrelated recognized Module is not retained | Its execution support is not claimed; actual directory/access checks remain |
| ITRU-10 | Complete declared requirements are supplied by a trusted producer | Rely on completeness without scanning bodies or executing user code to infer features |
| ITRU-11 | A general semantic auditor is claimed to prove arbitrary bodies | Needs independent supported proof/coverage evidence; no total-certification promise |
| ITRU-12 | A reader examines only selected records and claims every emitted Body valid | Invalid scope claim; complete producer validity is a separate obligation |

## 2. Bounded natural row access

| Case | Input or observation | Required outcome |
|---|---|---|
| ITAX-01 | A fixed row is looked up directly near the end of a table | Check its table/count/stride and selected extent without decoding preceding rows |
| ITAX-02 | Fixed-row ID is null, exceeds the table count or carries a DefId kind | Reject the typed reference before accessing the row |
| ITAX-03 | Selected variable row has a valid adjacent offset pair | Read only that bounded row; no all-offset scan |
| ITAX-04 | An unrelated offset pair or row payload is malformed | It need not be inspected by trusted lookup; a whole-table validator rejects |
| ITAX-05 | Selected adjacent offsets decrease, exceed data or expose an empty required row | Reject the accessed extent |
| ITAX-06 | Count/stride/index/end arithmetic cannot fit the containing slice | Reject before slicing or allocating |
| ITAX-07 | Directory entries duplicate a kind, overlap or disagree with a fixed stride | Reject those naturally exposed section-map facts |
| ITAX-08 | A selected row has trailing bytes or nonzero reserved fields | Reject its exact schema shape |
| ITAX-09 | An accessed owner/range exposes a local membership or duplicate-key contradiction | Reject; producer trust does not hide known invalidity |
| ITAX-10 | Unrelated owner ranges contain an undiscovered orphan/overlap | Producer is invalid; a trusted selected lookup need not perform a global search |
| ITAX-11 | A name/key String is used while another String is unreferenced | Validate the used row's scalar text, zero and extent rules without decoding the other row |
| ITAX-12 | A required access exhausts its declared fixture resource limit | Explicitly decline; no skipped check or success-shaped default |

## 3. Indexed Bodies and exposed contradictions

| Case | Input or observation | Required outcome |
|---|---|---|
| ITBI-01 | Seek the recorded terminator of the supplied block | Bound the block/index and decode that terminator to the exact block end, without scanning preceding operations |
| ITBI-02 | Seek a definition through its interval and offset | Check the selected operation's exposed result ID/kind/type against the requested descriptor |
| ITBI-03 | Terminator/definition offset is outside its selected block | Reject before reading |
| ITBI-04 | Indexed terminator decodes as an operation or leaves trailing bytes | Reject the exposed role/extent mismatch |
| ITBI-05 | Selected definition decodes a different result ID or type | Reject the exposed contradiction |
| ITBI-06 | In-range false index points into an operand that locally resembles a valid record | Trusted selective access need not scan preceding operations; the producer is false and full index/stream audit rejects |
| ITBI-07 | The entire counted block is already decoded | Check operation count, complete extent and indexed locations exposed by that traversal |
| ITBI-08 | Complete traversal exposes a terminator or definition index disagreement | Reject even for a trusted producer |
| ITBI-09 | Untrusted deadness/definition indexes are used to omit their own contrary evidence | Not adequate checking; audit the necessary closure or decline |
| ITBI-10 | Compact indexed lists or general indexes have truncated storage/reserved bits | Reject the accessed shape before using offsets or allocating lists |
| ITBI-11 | Compact linear has no direct definition/terminator indexes | Derive its omitted facts by its one-block traversal; do not invent a direct seek |
| ITBI-12 | Body-local ID or type-map reference is zero/out of range | Reject its actual namespace; no artifact-row/local-ID substitution |
| ITBI-13 | Equivalent one/two/four-byte local-width and general/compact-indexed fixtures are read | Same selected logical records; fixed metadata row words remain four bytes |
| ITBI-14 | Fast-import throughput or general compiler/native safety is claimed | Requires independent measurements/implementation evidence, not these fixture reads |

## 4. Calls, effects and unsafe scopes

| Case | Input or observation | Required outcome |
|---|---|---|
| ITCA-01 | Call selects a trusted declared method | Access/check its actual signature and required caller effects without auditing its body |
| ITCA-02 | Argument/result type, mode or logical arity conflicts with the selected signature | Reject the exposed call/ABI mismatch |
| ITCA-03 | A non-unit call uses zero result ID, including a no-return target | Reject; no-return does not erase its declared result |
| ITCA-04 | Trusted unit call uses zero or its ordinary nonzero unit definition | Preserve the distinct result-definition choice and ordered call effect |
| ITCA-05 | A NoThrow caller selects a checked trusted NoThrow target | Preserve the promise without re-proving the target implementation |
| ITCA-06 | NoThrow caller selects a target lacking the required promise | Reject the exposed effect mismatch |
| ITCA-07 | Caller-unsafe target is called from a safe root context | Reject; producer trust does not grant call-site authorization |
| ITCA-08 | Call has a valid recorded enclosing unsafe-region chain | Check accessed chain entries and callee obligation; trust complete CFG membership/edge truth without an all-Body audit |
| ITCA-09 | Accessed region parent/role is invalid or a traversed edge visibly bypasses its entry/exit | Reject that exposed scope violation |
| ITCA-10 | Checked trusted DoesNotReturn contract justifies the terminal-call fallback being unreachable | Permit the inference while preserving earlier failure effects and ordinary result typing |
| ITCA-11 | Arbitrary effect/purity/termination/ownership truth is claimed from masks or trusted fixture calls | No such independent proof; preserve each separate contract |
| ITCA-12 | A caller-unsafe declaration is offered as callee-body or reference/lifetime authority | Does not supply that authority; source/owner rules remain independent |

## 5. Typed references and true polymorphism

| Case | Input or observation | Required outcome |
|---|---|---|
| ITRF-01 | Typed fields name MethodSig, Body, TypeSpec, TypeDef, Import, GenericParam or FieldDef | Each field/form fixes its table; no table discriminator is decoded |
| ITRF-02 | A typed `u32` carries a nonzero high byte, even the apparent intended table kind | Reject, rather than masking, selecting or accepting an alias |
| ITRF-03 | Packed Call uses `m=0` followed by a four-byte row index | Select that artifact-wide MethodDef row; preserve the cross-module and current profile gates |
| ITRF-04 | Packed Call carries the old explicit MethodDef DefId or another table's kind | Reject the typed word, without a retired-schema heuristic |
| ITRF-05 | Effects/OutputState/Cleanup encode the same numeric owner row | Clause kind fixes MethodDef/MethodDef/TypeDef respectively; identities remain distinct |
| ITRF-06 | A Contract owner has a DefId discriminator or appears in the wrong table's owner range | Reject the typed word or clause/range context |
| ITRF-07 | Reserved Transfer/Dependency shapes are offered as semantically resolved owners | Decline the unassigned application; no invented owner table or variant |
| ITRF-08 | Export/Import/GenericParam genuinely permit several definition kinds | Retain their explicit permitted DefId discriminator and selected identity |
| ITRF-09 | A polymorphic definition field uses kind zero, an unpermitted kind or a missing row | Reject; true polymorphism is not an arbitrary-table escape |
| ITRF-10 | Fixed typed row references cross 255/256 or 65535/65536 | Preserve the four-byte word independently of Body-local instruction width |
| ITRF-11 | Same numeric row or equal bytes exist in another table | No alternate target or semantic identity; decode the field's actual namespace |
| ITRF-12 | Complete generic/witness/nominal context is claimed correct from typed row lookup | Requires the separate complete binding/formation evidence |

## 6. Optional slots and untouched payloads

| Case | Input or observation | Required outcome |
|---|---|---|
| ITSL-01 | Optional nonsemantic payload is ignored | Bound its directory entry and policy; no slot/payload scan merely to skip |
| ITSL-02 | A reference-slotted section is interpreted or preserved | Bound its prelude and every slot used by the operation |
| ITSL-03 | Selector 1 has zero section kind and a valid Body/block/ordinal | Body is schema-implied; decode that coordinate without a table selector |
| ITSL-04 | Selector 1 supplies nonzero section kind, including Body `0020` | Reject the reserved field |
| ITSL-05 | Selector 0 targets one of its permitted core tables | Keep its genuine target-kind discriminator; block/ordinal are zero |
| ITSL-06 | Selector/row/block/ordinal or prelude extent is invalid | Reject the accessed coordinate; no hidden pointer or guessed fallback |
| ITSL-07 | Rewriter cannot preserve a slot's referent after transformation | Drop/regenerate the optional section, not stale success |
| ITSL-08 | Arbitrary opaque payload is claimed to preserve all meaning merely because slots remap | No such proof; section contract and transformation evidence remain necessary |

## 7. Rewriting and admission reuse

| Case | Input or observation | Required outcome |
|---|---|---|
| ITRE-01 | Typed MethodDef/TypeDef/FieldDef/TypeSpec rows are remapped | Rewrite each decoded field in its schema-owned namespace, not coincident data octets |
| ITRE-02 | Same numeric row is moved differently in two target tables | Use the field/clause-implied map; no global numeric replacement |
| ITRE-03 | Unchanged input indexes are trusted while a rewriter emits new indexes | Rewriter owns the output facts it changes/emits and must preserve meaning |
| ITRE-04 | A cached decision's bytes/schema/context/target/trust basis is unchanged | Reuse only its previously admitted scope |
| ITRE-05 | Any relied-on byte, binding, rule, target or trust basis changes | Revalidate the affected decision; same Module name is insufficient |
| ITRE-06 | Equal `(1, 0)` or registry labels accompany different selected schemas | Reject unsupported schema agreement; old fixed-kind encodings are not aliases |
| ITRE-07 | An arbitrary linker/optimizer changes control flow, types or ownership | Needs complete preservation evidence beyond finite remapping |
| ITRE-08 | Calls disappear while cleanup/load/view/constant dependencies remain | Retain the complete remaining requirements; no dependency discharge from instruction absence |

## 8. Honest evidence scope

| Case | Input or observation | Required outcome |
|---|---|---|
| ITCL-01 | Tool performed selected bounded reads only | Report that scope, not whole-artifact structural/semantic validation |
| ITCL-02 | Tool structurally decoded all supplied fixture Bodies and compared indexes | May claim that structural scope, not dominance/effect/ownership truth |
| ITCL-03 | Semantic audit is incomplete, unsupported or resource-exhausted | Decline the requested safe-execution claim unless an independent admitted trust basis suffices |
| ITCL-04 | Producer emits noncanonical data not examined by a trusted reader | Producer obligations remain; reader's access claim is not a canonicality certificate |
| ITCL-05 | General performance, resource ceilings or native behavior are claimed | Independently measure/specify them; these finite cases assign no universal envelope |
| ITCL-06 | All-behavior certification is inferred from passed fixture counts | No such conformance claim |
