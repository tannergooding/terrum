# Complete function-pointer contracts in private persistence

**Status: unapproved; provisional direction under the authorized
missing-contract workflow.** The applicable authorization is:

> At a genuine missing contract, prepare the smallest concrete decision needed as a new proposal doc using gpt-6.1-sol at max reasoning, then continue with it being treated as a provisional approval/direction.

This document recommends a private carrier, not normative adoption,
implementation support or released stability. It changes no code or other
document and allocates no Standard Feature, section, clause or operation.
The source, anchor, selector and convention directions referenced below
remain unapproved/provisional independently of this recommendation.

The [checked-reference private-persistence companion](checked-reference-private-persistence.md)
supersedes its earlier reference-97 candidate with one unapproved,
unimplemented **private-105 composition over implemented 104**. It selects
recursive reference/optional carriers, declaration publication/implicit
facts and typed Body operations, including reusable materialization.
Its explicit composition retains this file's pointer-row/type-reference
semantics plus the later foreign-invocation vector/raw-output amendment,
direct-import expectation and enum deltas; it does not inherit a 97 byte
layout or decoder. This file continues to own pointer invocation rows and
`fnptr#i`/address-contract references, not a competing checked-reference codec
or the 105 composition. The standalone historical 97 recommendation below
is not the implemented 104 schema or an alias for 105.

## Decision and precise supersession

Amend the **still-unimplemented candidate private structured-module revision
97** with a mandatory inline invocation-contract payload on every
`NativeFunctionPointerSignature` row. Carry normalized declared Effects,
bounded phase-3 selector identities, ordinary checked OutputState and shared
Dependency payloads. A row denotes a complete structural type, never an actual
callable or a reference to a provider's current declaration.

Use the signature table as the authority for private function-pointer type
uses, including textual type uses in bodyless declaration expectations.
Retain an actual addressable declaration's exact contract through one
reference to that table, even when its Function payload is removed.
Extend only its private convention domain with **candidate `4=Terrum`**;
retain `0..3=Unmanaged/Cdecl/Stdcall/Vectorcall`. Do not copy the separately
proposed Standard `0=Terrum` assignment into this private field.

**Do not admit the raw NativeOutput protocol on a function-pointer contract.**
Preserve it on its existing actual-declaration carrier and reject address
formation/projection requiring its erasure. Ordinary checked initialization
outputs and raw output cells remain different formation contracts.

This supersedes only the signature-row layout, signature-specific convention
domain, shape-only textual fnptr identity and associated interning/bounds
instructions in
[candidate 97](native-calling-convention-private-persistence.md#function-pointer-signatures-and-ordinary-nativecallable).
In particular, its `9 + 4*n` row is replaced, not extended optionally.
Keep its NativeCallable/NativeImport token spellings and relative order,
stored Function fields, foreign-member variant and other record placements;
add only the address-contract reference token specified below.
Keep the
[resolution amendment](native-import-resolution-private-persistence.md)
and every prerequisite in the
[97 composition](native-calling-convention-private-persistence.md#exact-composition-and-selection).
These instructions do not edit those documents or supersede their other
recommendations.

| Unapproved alternative | Assessment |
|---|---|
| **Amend unpublished 97 in place — recommended** | One complete selection before its implementation; the existing signature table already owns structural pointer types. Requires exact agreement on this amended draft everywhere, not simultaneous support for two meanings of 97. |
| Select a new candidate 98 with the same payload | Gives numeric rejection of a hypothetical earlier 97, but does not reduce implementation or exact-schema obligations. No implemented/exchanged 97 is established here; a number change supplies no necessary distinction for this bounded decision. No 98 is selected. |
| Add a private MethodDef-like anchor or shared invocation-contract directory | Could unify callable and pointer contracts, but requires new owners/references, declaration migration and remapping across unrelated contracts. The Standard anchor is not an existing private row to reuse. Not justified for this gap. |
| Store only Effects, add a shape fingerprint, or look up a target after loading | Still loses selectors, source sets or projected static views, especially for independent pointer parameters and bodyless imports. Rejected. |

Retaining 97 is not a compatibility concession. There is exactly one selected
amended schema, no old-row alias, optional trailer, shape-only zero-contract
variant or fallback reader.

## Historical source evidence and semantic owners

These source observations describe the snapshot used when this proposal was
drafted; they are not newly executed compiler/native evidence or current
support claims. Current private-module support is tracked in the
[implementation inventory](../implementation/modules.md).

| Inspected owner | Relevant boundary |
|---|---|
| [`NativeFunctionPointerContract.cs`](../../sources/Terrum.CodeAnalysis/Semantics/NativeFunctionPointerContract.cs) | In this snapshot, the in-memory `NativeFunctionPointerSignatureDefinition` carried declared Effects, bounded selector identities, checked-output slots and normalized Dependency rows; its target matcher checked declared effects against the target, and projection supported DoesNotReturn weakening plus source subsets/backing-only refinements under exact destination/phase correspondence. Checked OutputState remained unsupported for the then-current ABI parameter subset. These in-memory additions did not change private bytes. |
| [`BoundProgram.cs`](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs) | `BoundFunction` separately carried DoesNotReturn, effective RequiresUnsafeCaller, DeclaredRequiresUnsafeCaller, NativeOutputContracts and logical dependency contracts. Host dependency enum values/source sets were not the shared wire payload. Effective native-role unsafety was not sufficient evidence of a declared Effects `04`. |
| [`ModuleCompiler.cs`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.cs) | The inspected header reader selected 92; signature writing/reading was shape-only. The writer refused signatures with nonempty invocation facts rather than silently dropping them. `Contract`/`AppendTypeName` embedded shape identities, while imported ModuleFunctions could have no Function payload. A larger table without references from those expectations would still be lossy. |
| [`ModuleSets`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.ModuleSets.cs), [`Graph`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.Graph.cs) | Projection, type-table merging and restoration were separate paths. `RecordedImportPromises` restored selected outer callable flags, not a complete nested pointer contract. |
| [Source-contract direction](standard-function-pointer-source-contracts.md) | Owns fixed bare-type defaults, complete inferred NativeAddress types, declared/effective unsafety separation and checked directed projection. It explicitly rejects shape-only private persistence. |
| [Anchor-contract direction](standard-function-pointer-anchor-contracts.md#proposed-normalization-and-equality) | Owns applicable normalized invocation facts and logical-subject rebasing. Target identity, NativeCallable/NativeImport roles and evaluation-form authorization are not structural pointer identity. |
| [Shared selector candidate](aggregate-native-outputs.md#exact-shared-predicate-codec), [composition](predicate-feature-composition.md) | Own the six closed normal-result tests and their exact graph/footprint meaning. No general predicate program is selected here. |
| [Clauses](../specs/representation/contract-clauses.md), [general dependency carrier](reference-provenance-carriers.md#private-exchange-candidate-revision-93) | Own Effects masks, checked OutputState and shared dependency domains, occurrence keys, defaults and phases. Reuse those meanings, not compiler enum ordinals. |

The accepted [persistence taxonomy](../specs/representation/persistence.md),
[binding relation](../specs/representation/binding-and-replacement.md),
[reader obligations](../specs/representation/reader-trust.md) and
[exact draft agreement](../specs/representation/format-versioning.md)
remain unchanged. This private carrier does not replace Standard TypeSpec
`0x0D`/MethodDef anchors, the Development signature schema, or the separately
proposed [NativeAddress E9/Body-record 0D](standard-native-address-formation.md).

## Exact candidate row and reference layout

All assignments below are **unapproved private candidates**. Multi-byte
integers are little-endian. Keep the table after imported structures and
before NativePointerTypes and candidate 96's nominal directories. There is
one mandatory table count, including when zero:

```text
i32 nativeFunctionPointerSignatureCount
repeat nativeFunctionPointerSignatureCount:
    u8 callingConvention
    i32 resultType
    i32 explicitParameterCount
    i32 parameterType[explicitParameterCount]
    u32 effectsMask
    i32 selectorCount
    SelectorIdentity[selectorCount]
    i32 outputStateCount
    CheckedOutputState[outputStateCount]
    i32 dependencyRowCount
    Dependency[dependencyRowCount]

SelectorIdentity:                         // 12 bytes
    u8 phaseTag = 3
    u8 flags = 0
    u16 reserved = 0
    u32 phaseLocalOrdinal
    u32 nodeKind

CheckedOutputState:                       // 12 bytes
    u32 formalOrdinal
    u32 selectorOrdinal
    u8 state
    u8 reserved[3] = 0

Dependency:                               // 12 bytes; shared payload
    u8 destinationKind
    u8 sourceKind
    u8 relationship
    u8 phase
    u32 destinationOrdinal
    u32 sourceOrdinal
```

For `n` parameters, `s` selectors, `o` output states and `d` dependencies:

| Row-relative offset | Field/extent |
|---|---|
| `0` | Convention, 1 byte |
| `1` | Result type, 4 bytes |
| `5` | Parameter count, 4 bytes |
| `9` | Parameter types, `4*n` bytes |
| `A = 9 + 4*n` | Effects mask, 4 bytes |
| `A + 4` | Selector count, 4 bytes |
| `A + 8` | Selectors, `12*s` bytes |
| `B = A + 8 + 12*s` | Output-state count, 4 bytes |
| `B + 4` | Output states, `12*o` bytes |
| `C = B + 4 + 12*o` | Dependency count, 4 bytes |
| `C + 4` | Dependencies, `12*d` bytes |

The row is exactly **`25 + 4*n + 12*(s + o + d)` bytes**; the table prefix
is four bytes. There is no row version, length word, owner index, optional
payload, extension flag or padding. Counts delimit successive rows and the
following table; trailing artifact bytes remain invalid.

Types remain complete **private i32 type identities**, not Standard
TypeSpecRefs or DefIds. The containing signature is the implicit logical
contract owner. It has no receiver, method-generic range or captured
environment. Every explicit parameter has ordinary logical mode `00`;
checked initialization/access qualifications belong to its complete type.
Do not encode legacy `IsOut`/`IsInit` flags as substitute types.
Keep zero parameters distinct from one unit parameter and retain unit
positions even when the target needs no data carrier.

| Logical convention | Private signature byte | Separate candidate Standard byte |
|---|---:|---:|
| Unmanaged | `0` | `1` |
| Cdecl | `1` | `2` |
| Stdcall | `2` | `3` |
| Vectorcall | `3` | `4` |
| Terrum | **`4`** | **`0`** |

Private bytes `5..255` reject. Encode/decode explicitly, never by host enum
cast. Each byte denotes a present identity; zero is not absence. The
[Standard default-convention proposal](standard-function-pointer-default-convention.md)
does not determine this map. Terrum does not become a source native
CallConv value or a NativeCallable/NativeImport token. Those roles retain
their four foreign conventions and existing omission rules. Known identity
does not establish target support or a stable Terrum-to-Terrum ABI.

### Textual type uses, including bodyless expectations

Replace the old private textual `fnptr:K(P...)->R` type production with
exactly **`fnptr#i`**, where `i` is the zero-based signature-table index in
the **containing embedded Module**. This is a private wire type atom, not
Terrum source syntax. It occupies `6 + decimalDigitCount(i)` ASCII bytes
inside the existing i32-length UTF-8 String. Its decimal spelling has no
sign, whitespace or leading zero except the single digit `0`; require an
in-range, representable index. Old shape spellings are not aliases.

Use this atom at every textual fnptr occurrence, including nested pointer
types, fields and declaration result/formal types. Keep all other type
productions and declaration-role tokens unchanged. Binary type fields,
including the optional stored `NativeFunctionPointerType`, retain their
existing i32 private type-code encoding; `i` itself is not that type code.
Parse/remap only grammatical type positions; matching text in names, entry
symbols or ordinary strings is not a type reference.

Parse these atoms to typed references before comparing declaration
contracts. Raw String equality or equal `i` values across Modules is not
type equality. Conversely, different indices may resolve to equal complete
types. The referenced row is the sole invocation-contract authority; no
duplicate textual Effects/output/dependency grammar is introduced.
For a retained Function, cross-check its complete typed declaration and every
textual type use. A bodyless expectation retains the same references and
row closure without obtaining types from the replacement. This limited
type-use amendment is necessary to make the row useful on independent
references; it adds no new binary ModuleFunction field.

An actual addressable target also needs a root reference: its declaration
role/convention and effective caller-unsafety cannot reconstruct its exact
declared mask or a complete pointer type. In the existing ordinary
ModuleFunction `Contract` String, add exactly one token:

```text
|NativeAddressContract:fnptr#i
```

Its extent is `29 + decimalDigitCount(i)` ASCII bytes, with no additional
String prefix. Place it after the existing base signature/ordinary member
tokens and before any NativeCallable/NativeCallableEntry tokens or the
direct-import resolution/convention pair. The existing relative token orders,
especially resolution immediately before convention immediately before
DoesNotReturn/MayReturn, remain unchanged.

Require this token for every NativeCallable or callable NativeImport
declaration whose complete contract is admitted for address formation,
whether used or uncalled. A stored Function must have its existing optional
`NativeFunctionPointerType` present and resolving to the same complete row;
it is a second reference, not a second contract payload. An imported
expectation retains the token before Function erasure. Cross-check its
complete expected signature, convention, DoesNotReturn, checked outputs and
dependencies against that row. The row preserves declared `04` separately
from the declaration's role-imposed effective caller-unsafety; restoration
must preserve that distinction rather than infer `04` from `CallerUnsafe`.

The token names the original exact address contract, not a projected use.
It grants neither NativeCallable role nor addressability by itself. Reject
duplicates, wrong position/case, inconsistent references and an inapplicable
owner. Receiver-bearing/foreign-interface methods retain their original
role-only grammar and no token; no receiver adapter is inferred. A required
unrepresented invocation protocol cannot become addressable by supplying a
row without it. Missing carrier/support declines affected address use,
never reconstructs the contract from a replacement. Definitions/expectations
with no admitted address contract have no token or stored pointer-type fact.

## Contract interpretation

### Effects and defaults

`effectsMask` is mandatory and exactly one of `0`, `02`, `04`, `06`.
Reuse the assigned DoesNotReturn and declared caller-unsafe meanings.
Reject retired NoThrow bit `01` and every other bit. Standard absent Effects
and one explicit zero normalize to private mask zero; a private zero is
still physically present. Standard re-emission omits its zero-mask clause.

Do not derive this field from a shape, result type, target name or merely
effective `BoundFunction.RequiresUnsafeCaller`. The semantic model must
separate authored/published `04` from native-role/family unsafety before
writing. Native pointer formation/invocation remains unsafe independently;
neither inject `04` solely for that reason nor delete a declared `04`.

The source proposal owns default normalization in the defining rules/context:
bare types have fixed defaults, inferred addresses retain target contracts,
and Boolean checked-output defaults publish true/complete and false/vacant.
Non-Boolean unconditional completion needs no OutputState row. Required
dependency defaults and explicit empty sets must also be normalized before
writing. Source-created parameter, result and field types allocate these
complete rows without needing any address expression, selected target or
stored Function. A reader never fills an incomplete row from a nearby target,
source annotation, current language default or provider body. DoesNotReturn
keeps the declared result and every other required fact, even without a
normal edge.

### Exact bounded selector identity

`SelectorIdentity` is a private transport of the **already proposed six
closed shared selector templates**, not a new predicate expression codec.
`nodeKind` has exactly these shared meanings:

| Value | Test of this signature's saved ordinary result | Exact complete result |
|---:|---|---|
| `1` | True | Built-in bool |
| `2` | False | Built-in bool |
| `3` | Nonzero | Built-in int32 or uint32 |
| `4` | Zero | Built-in int32 or uint32 |
| `5` | Nonnegative | Built-in int32 |
| `6` | Negative | Built-in int32 |

The descriptor has precisely the shared codec's implicit normalized
structure: one graph root/node; a bool node result; one `ReadRoot` operand;
one exclusive footprint root `(rootKind=5, rootIdentity=0)` observing the
owning signature's ordinary result; no dependencies, edges or captures.
There is no literal, helper call, output read, user operator or general
predicate evaluator. The observed complete result type comes from this row,
not an extra type index or a name-based status classification. All fixed
flags/reserved fields of a reconstructed shared graph are zero.

This projection is available only after validating that exact complete
shared shape. A graph with additional information cannot be reduced to a
test number; unsupported phase-1/2 predicates or broader nodes require their
own admitted carrier and decline affected transport/use. Selecting this
private projection does not complete or register Standard Feature 10.

Require phase `3`, unique contiguous ordinals `0..s-1` and valid result/test
pairs. An OutputState `selectorOrdinal` selects that local attachment, not
a Standard graph row, global signature index or callable ordinal. Different
outputs may share an attachment. Keep even unreferenced retained attachments.
For exact type identity, follow the anchor proposal's **phase, phase-local
ordinal and normalized graph/footprint** rule: remapping physical graph rows
is harmless; merging distinct logical attachments or commuting their
ordinals is not. Reusing an existing attachment is not merging separately
published occurrences. The private writer orders attachments by ordinal,
not by silently reassigning ordinals to sort tests.

### Checked OutputState and Dependency

For each checked output row require an in-range explicit `formalOrdinal`
whose complete type is an eligible checked initialization reference/range,
ordinary mode `00`, a local selector, state `0=vacant` or `1=complete`, and
zero reserved bytes. State means the complete whole value/range, including
ownership/backing, not initialized address bits or a prefix.

OutputState observes normal publication. **Selector attachment phase `3`
is not Dependency phase `3` deferred publication.** Neither introduces an
exceptional-output promise. Missing rows do not invent a status condition or
repair missing normalized source defaults.
Duplicate `(formalOrdinal, selectorOrdinal)` keys reject even when equal.
Contradictory applicable states reject: the closed tests permit checking
overlap using bool's two outcomes, int32's negative/zero/positive partition,
or uint32's zero/positive partition. This is not arbitrary predicate proving.

Dependency rows reuse candidate 93's exact twelve-byte shared payload:

| Field | Candidate admitted domain/constraint |
|---|---|
| Destination | `1=result` with ordinal zero; `2=checked initialization output` with its formal ordinal; `3=retained formal holder` with its formal ordinal. Shared destination `4=receiver` is invalid here because there is no receiver. |
| Source | `1=explicit formal` with its ordinal. Shared source `2=receiver` is invalid here. The selected whole source must independently be eligible; a raw pointer does not expose its pointee as checked storage. |
| Relationship | `1=borrow source storage`, `2=forward backing`, `3=explicit declared union`; nonzero sources require an assigned nonzero relationship. |
| Phase | `1=normal publication`, `2=every-applicable-exit capture`, `3=deferred publication`, with the existing destination/type/phase applicability, not blanket permission for every combination. |
| Empty source set | One row with source kind, relationship and source ordinal all zero; distinct from absence and exclusive of nonempty rows at that destination/phase. |

Preserve result/output publication, returned/output holder capture and
existing-formal retention according to the
[normalization table](reference-provenance-carriers.md#source-schema-normalization).
Deferred publication still requires its complete admitted operation/output
contract; phase `3` does not manufacture one.
The occurrence key is
`(destinationKind, destinationOrdinal, phase, sourceKind, sourceOrdinal)`;
duplicates reject, not merge to relationship `3`.

Translate `BoundDependencyContract` logically: DependsOn selects its
publication destination/phase, Captures selects its capture destination/phase,
broad sources normalize to relationship `3`, and ForwardsBacking refines
only its selected sources to `2`. Independently admitted borrow-only facts
remain `1`. Do not cast host destination/relation/source enum values, guess
a base phase, or collapse incompatible authored relationships to one key.
An operation destination without an assigned projection remains unsupported.
No runtime source instances, loans, roots, pin tokens, verifier caches or
copy-origin summaries are serialized in this type payload.

### Raw-native obligations remain separate

`NativeOutputContracts` are **not CheckedOutputState**. Keep candidate 92's
declaration-owned output collection, role, formation, success/complement,
dependencies and tag-41 reservations exactly as composed into 97. Its
phase-1 dependencies remain disjoint from candidate 93's general declaration
collection. No such collection, role byte or reservation is added to a
signature row.

A target with a required raw NativeOutput protocol cannot yield an admitted
pointer by dropping its role/formation, copying only its state pair, or
pretending a raw pointer formal is a checked initialization reference.
Reject that address/projection/indirect route, including for no-return
targets and apparently unused outputs. This does not forbid ordinary direct
use of the declaration, or an eligible raw output cell whose value type is
itself an otherwise admitted complete function pointer.

NativeParameter access/retention/extent and other actual-target obligations
likewise cannot disappear. Form a pointer only when all required invocation
obligations are represented and satisfied under the admitted contracts;
otherwise decline the route. No addressability, foreign-interface receiver
adapter, native marshalling or raw-output callback protocol is selected here.

## Context, projection and transformation

Resolve each row in its owning private Module/type universe and retained
defining rule context. Candidate 97 fixes the composed interpretation,
including candidate 93's dependency-rule versions; no per-row default-rule
word is added. Neither a consumer edition nor a same-spelled type supplies
the original meaning.

Substitute admitted enclosing type/constant arguments through all complete
signature types and contract subjects together, then normalize and validate.
Rebase target-local result/formal subjects to this receiver-free logical
signature by role and declaration ordinal. There is no method-generic
environment recipe. An open/context-sensitive type whose complete private
type/context carrier is unavailable must decline transport, not become a
primitive code or a provider-chosen specialization. Concrete native use
requires the admitted closed signature and target/runtime representation.
This row does not supply missing checked-reference, generic, task, owner,
cleanup or runtime codecs.

Exact equality/interning includes original convention, all ordered complete
types/modes, mask, selector attachments, checked output states and dependency
records under the anchor normalization rules. Rebase the implicit local
owner; retain external nominal identities and substitutions. Absence and
an explicit empty dependency remain distinct. Layout, a target MethodDef,
names and directed satisfaction are not equality.

Keep actual address targets distinct from type rows. NativeCallable and
callable NativeImport targets first supply their complete applicable
contract; imports need no NativeCallable marker. Check any requested static
view by the source proposal's directed projection. An inferred value retains
the exact target type; an explicit bare type retains its fixed default view.
The stored target's optional `NativeFunctionPointerType`, when present,
describes that declaration's complete address contract, not every projected
use. Validate its convention/signature/applicable facts against the actual
declaration, independently of declaration-role tokens.

Persist the selected view on existing typed address/value/indirect-call
uses and validate target-to-view projection where required. A weaker view
does not recover stronger facts because lowering recognizes the address.
If an existing private Body/type form cannot express the selected view and
its correspondence, its complete codec is a prerequisite, not a reason to
erase facts or invent a projection tag here. Writable pointer cells stay
invariant. Terrum type byte `4` does not make an ordinary Terrum method
native-addressable or cast a foreign target to the default convention.

Remapping must visit binary private type codes, every `fnptr#i` atom,
nested signatures/pointers, stored addressable types, fields, variables,
Body address/indirect-call types, and native input/output/reservation
pointees. A private `selectorOrdinal` is unchanged when physical selector
records move; rebuild decoded attachment/graph maps without renumbering its
logical identity. Rebase formal subjects through any admitted declaration
mapping. A parameter reorder is not structural equality. Rebuild
corresponding Standard anchors/ranges/graphs and Body maps when cross-encoding,
rather than copying private ordinals as Standard row IDs.

Use context-sensitive traversal/comparison keys. Clone rows when a shared
physical row cannot preserve every using context; never rewrite one use's
imports into another's universe. Check the full structural type/contract
closure, including pointer-mediated cycles. Structural cycles reject;
valid nominal recursion remains nominal. An in-progress pair is not equality
success. Rebuild actual provenance/loan ledgers after rewriting rather than
persisting caches. Preserve actual target/provider/code-origin dependencies
and optional source/debug references under their own maps; never replace
matching octets indiscriminately.

## Reading, writing and closure admission

### Bounds, malformed input and canonical writing

Keep candidate 97's private 4 MiB artifact, 1,024-function, 256-slot,
64-call-argument, 100,000-node and depth-128 budgets and existing String
bounds. These are private limits, not new source/Standard capacities.
Signature count is nonnegative, at most 100,000 and within the private
function-pointer type-code range; parameter count is `0..256`.
Each collection count is nonnegative and subject to the same shared work
budget. Bound `o <= n*s`; a checked loose dependency bound is
`d <= 3*(1 + 2*n)*max(1,n)` for this receiver-free signature, further
restricted by actual destination/source/phase eligibility.

Before allocation/consumption, use checked wide arithmetic for the table's
`25*signatureCount` minimum, `4*n`, each `12*count`, cumulative extents,
count products and remaining bytes. Charge nested types and contract visits
to one bounded traversal, not a fresh budget per signature. Forward type
references require complete closure validation before use. Validate unknown
tags, masks, reserved bytes, type ranges, selector ordinal closure, output
keys/states/overlap, dependency keys/empty-set exclusion and applicability
even on uncalled or no-return signatures. Required missing/unsupported
carriers decline affected use; malformed selected structures reject.

Canonical writers normalize before encoding, write all mandatory fields and
counts, order selectors by retained phase-local ordinal, output states by
unsigned `(formalOrdinal, selectorOrdinal)`, and dependencies by the shared
occurrence key. Readers may accept consistently reordered physical records,
then normalize; duplicate keys are malformed, not noncanonical. Equal
complete signature rows may remain distinct or be interned with all type
references remapped; global byte uniqueness is not required. Never intern
only by shape, drop unused retained contracts, commute logical selector
ordinals or truncate a type/Contract String to fit.

### Exact revision and outer-container joins

Select `TRRM` plus the private little-endian **i32 97**, with the complete
92–96 composition, original 97 declaration convention changes, resolution
amendment and this mandatory signature delta. Every current writer/reader,
producer validator, graph-library reader, reference projection and parsed
snapshot must select that **same exact draft**, even with zero signatures.
Reject other revisions; do not add `>=97` dispatch, retry a previous layout,
promote headers or decode a missing payload as defaults.

Equal 97 bytes do not distinguish this draft from a hypothetical old
shape-only 97. Exact out-of-band agreement selects the whole schema; the
reader does not infer a draft from empty tables or plausible counts.
Regenerate artifacts from complete checked inputs. No consumer can promise
to detect every dishonest relabelling that happens to satisfy its new
structural rules.

Retain **TNPK/TNSP/TNGP outer version 3 and their byte layouts unchanged**,
including the direct-import expectation table. Select the corresponding
amended outer-3/inner-97 agreement throughout the retained closure. Outer 2,
unknown versions, earlier inner revisions and old outer-3 drafts are not
aliases. An outer header, native profile or valid checksum does not select
or repair the inner schema.

Join outer direct expectations to the **original importing** ModuleFunction
by their existing local-name/provider/key rules and bijection. Its original
resolution/convention tokens, complete nested pointer types and separate
output/dependency expectations remain authoritative. Interpret outer native
parameter pointees in that importing Contract's final type universe before
comparing the selected provider. Do not reconstruct expectations from the
provider or add an outer invocation-contract byte/table.
After remapping, recompute inner String/Contract lengths, outer offsets and
SHA-256; regenerate affected code/images from final checked inputs, not by
patching semantic bytes.

### Independent references, replacement, sets, graphs and JIT

Retain signature closure on every independent definition/expectation,
including unused parameters/results/fields, address-only references and
imports with `hasFunction=false`. Removing a body is not removing its type.
Provider binding resolves the selected identity first, checks every original
expected declaration and then separately compares structural pointer types.
Directed outer-callable effects/source-bound satisfaction never changes
equality of a pointer occurring inside its signature. An explicit value
projection is not permission to change an imported parameter or aliased cell.

Preserve each using Module's original expectations in reference artifacts,
Consumer/Provider/GraphContractSnapshot, packing, catalogs, diamonds and
replacement. Equal pointer types do not merge distinct provider obligations.
Snapshot/cache keys retain artifact identity, the exact amended draft,
target/rules, binding and substitution context; old shape-only comparison
results are not reusable merely because their revision number is 97.
Restoration must use the recorded complete view and collections, not a
provider copy plus `RecordedImportPromises` flags. A replacement losing a
relied-on no-return/output/backing promise or changing a nested complete type
fails its applicable comparison, even if code carriers are identical.
Retain any stronger provider fact actually exploited as an additional
dependency rather than silently strengthening the imported view.

Apply the same checks to semantic modules/sets/graphs and every native
package/set/graph edge, including declaration-only dependencies. Validate
stored-provider declaration, export, image and ABI consistency under the
existing owners before affected native use; this is not semantic re-proof
of machine code. Root-only graph JIT restores the root's original views,
validates the selected closure, and re-lowers only that root; providers stay
precompiled. No provider regeneration, implicit fallback, source rebinding
or live replacement is added. Existing module-selected function-address/
provider-promotion exclusions remain gates until their own complete
implementation is supplied.

Producers must publish schema-valid, truthful normalized contracts and
matching code. Source compilation checks authored source; transformations
are responsible for changed facts. Consumers must check structural bounds,
references, exact-schema/support/role consistency, projection and retained
binding before use. They are **not required to re-prove provider bodies**
or foreign truth. A lying producer can supply structurally valid but false
metadata; successful decoding, verification of structure, hashing or loading
does not establish that truth. Optional audits remain separate. No unsafe
authorization, marshalling, root, pin, retain, ownership or code-lifetime
extension follows from these records.

## Proposed acceptance cases and remaining gates

These are implementation requirements, **not executed results**. Use public
compiler APIs and the existing module/native test areas, with headless,
supervised native execution where runtime evidence is needed.

| Case | Required evidence |
|---|---|
| Identity/defaults | Round-trip all five conventions and four Effects masks; distinguish Terrum/Unmanaged, zero parameters/one unit parameter, equal shape/different contracts and absent/explicit-empty dependencies. Bare defaults do not depend on an initializer or consumer edition. Native unsafety does not inject `04`. |
| Selectors/outputs | Preserve Boolean true/false defaults, non-Boolean unconditional completion, exact int32/uint32 tests, shared output selectors and distinct logical attachment ordinals. Test negative/zero/positive statuses, invalid result/test pairs, cross-owner/out-of-range selector references, duplicate keys and contradictory states. |
| Dependencies | Round-trip broad, borrow-only, backing-only, empty and multiple-source records; preserve destinations/phases. Reject receiver/operation misuse, empty/nonempty mixing, accidental host-enum casts and unsupported deferred carriers. Test directed source refinement separately from exact type equality. |
| Targets/projections | Infer a non-unit no-return NativeCallable address, persist it and invoke under its exact view; separately project to an admitted weaker view and lose only its no-return proof. Exercise callable NativeImport without NativeCallable, reverse projection failure, unsafe-scope failure and mutable-cell invariance. No target-body rediscovery supplies a fact. |
| Address-contract roots | Preserve the exact target row on unused and bodyless NativeCallable/NativeImport expectations, including declared mask zero versus `04` despite equal effective unsafety. Reject duplicate/misplaced tokens, missing required references, signature/row disagreement and role-less or receiver-bearing owners; a projected use does not rewrite the target token. |
| Raw protocol boundary | Raw NativeOutput address formation declines without converting its cells to checked outputs. Direct declarations still retain their original role, states, dependencies and reservations; native-input retention/extent cannot disappear. Include an output cell whose value is an admitted complete pointer type. |
| References/replacement | Produce a reference, erase Function payloads, persist/reload a source-free consumer and bind an independently encoded provider. Change only a pointer mask, selector, dependency, convention or nested nominal origin and reject the incompatible replacement. Preserve all independent/diamond expectations. |
| Remapping/substitution | Reorder/deduplicate type rows, rewrite textual atoms, nest pointers in fields/signatures and apply admitted substitutions. Preserve selector logical ordinals, complete types, external origins, target indexes, Body/source maps and provenance. Reject structural cycles and unavailable open contexts. |
| Framing/bounds | Truncate each field/vector boundary; test negative/overflowing counts/products, invalid private bytes/masks/reserved fields, malformed atom spellings, highest in-range indexes and resource exhaustion. No-return/uncalled records receive the same checks. |
| Cross-layer/native | Cover private producer/reader/reference/set/graph/snapshot/restoration and root-only JIT together, plus stored-image AOT. Include reordered private type universes in outer joins, unused/address-only dependencies and a valid checksum with incompatible expectations. Observe actual supervised execution where claimed; emission alone is not runtime evidence. |
| Schema isolation | Reject old private revisions and wrong outer/inner selections. Standard anchors/default-convention/address operations and Development signature rows never become private aliases; a header rewrite or empty table never selects a different decoder. |

Remaining approvals and implementation gates are distinct:

1. **Private draft selection:** agree on the complete amended 97, including
   the row, private Terrum mapping, textual type-reference/address-contract
   token productions and all composed prerequisite deltas.
   **All remain unapproved/provisional**; exact agreement is not normative
   approval.
2. **Normative/Standard adoption:** the source/anchor directions, separate
   Standard default-convention/address carriers, and bounded selectors'
   owner/Feature-registration discrepancies retain their own approval gates.
   Provisional private implementation does not discharge those gates or
   establish Standard conformance.
3. **Semantic implementation:** add a complete-contract model with declared/effective
   unsafety separation, default normalization, checked projection and shared
   traversal/verification/lowering. Complete every required underlying
   type/context/Body/target carrier rather than restricting identity to the
   current scalar whitelist.
4. **Wire integration and qualification:** migrate all private writers/readers,
   textual type parsing/comparison, independent reference projection,
   replacement, set/graph/snapshot/JIT and native outer joins together;
   qualify the final relevant compiler inputs with the cases above.
5. **Separate future decisions/support:** keep raw-output pointer contracts,
   unrepresented native-input obligations, ordinary Terrum-method address
   eligibility and independently replaceable Terrum ABI choices closed.
   Their absence is not an implementer-selected erasure policy.

Completing this carrier is one persistence slice, not acceptance of the
broader source-to-Standard or native-function-pointer milestone. Normative
approval, actual implementation, execution qualification and released-schema
stability remain separate.
