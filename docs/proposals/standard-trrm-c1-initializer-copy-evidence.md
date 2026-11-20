# Standard TRRM class-initializer Copy evidence

**Status: unapproved; provisional direction under the user-authorized
missing-contract workflow, pending later review.** This proposes one bounded
composition of existing declaration evidence and existing Body operations.
It establishes neither implementation support nor native execution evidence.

## Finding and decision

**The missing decision is Copy-evidence composition, not a new value-transfer
operation.** Reuse `SemanticStore.sourceUse=1=CopyRead` for eligible complete
values installed into vacant class fields. Carry the declaration facts needed
to justify that use through source-free publication and binding.

The [accepted value rules](../specs/language.md#values-copying-and-aliases),
[storage rules](../specs/il-instruction-set.md#storage-reads-stores-takes-and-retirement)
and [construction rules](../specs/language/ownership-and-cleanup.md#construction-and-publication)
already distinguish copying, transfer, borrowing and field completion.
[EN1](standard-trrm-c1-enum-closure-composition.md#enum-records-catalog-and-values)
already preserves nominal enum values and their typed constants. Enum
initializer changes implement that contract; they need no new transfer
semantics. Already-admitted raw data/function pointers likewise keep their
existing CopyRead semantics.

Inspected source and test sources, **not executed validation**:

| Evidence | Established boundary |
|---|---|
| [`GetC1M1Construction2FieldAssignments` and initializer writer](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.StandardClassTypeReferenceWriter.cs) | The shared source collector admits scalar, nominal-enum, raw data/function-pointer and bounded source-checked Copy-struct parameter values; constants are scalar/enum. The writer emits record `04` followed by record `05`, Initialize/CopyRead. [`EN1`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.C1EnumClosureWriter.cs) reuses both helpers. |
| [`SupportsDeclaredCopyEvidence`](../../sources/Terrum.CodeAnalysis/Modules/TrRmArtifactIndex.cs), [`TypeDef reader`](../../sources/Terrum.CodeAnalysis/Modules/TrRmTypeDefTableReader.cs) | Unamended Construction/EN1 parents do not carry `DeclaredCopy`; checking local source fields is not a serialized promise for local or imported structs. Adding evidence support to separate EN1 successors does not enable the non-EN1 C2 parents. |
| [`ValidateConstructionClassFieldStore` and `IsCopyableRegisterValueType`](../../sources/Terrum.CodeAnalysis/Modules/TrRmRegisterBodyMapValidator.cs) | Initializer stores require CopyRead. In unamended parents, admitted nominal enums, pointer-family values and native function-pointer form `0D` are already copyable; nominal structs need the separate LocalAggregate selection and positive Copy evidence, and class kind 2 is excluded. Broadening the kind check alone is unsound. |
| [`Public source-class entry and C2 dispatch`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.cs), [`non-EN1 class writer`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.StandardClassTypeReferenceWriter.cs) | The seven-argument class-mapping route admits base C2, CM1 and CM1AR1 and forwards their captured classes through `EmitC1M1Construction2` to the same field-assignment collector and I/P writer. Thus non-EN1 constructor Copy stores need the same declaration facts. |
| [`Class-layout schema routing`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.StandardConsumption.cs), [`artifact index`](../../sources/Terrum.CodeAnalysis/Modules/TrRmArtifactIndex.cs), [`layout fixtures`](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmC1EnumClosureSourceTests.cs) | Existing class-layout literals map to the corresponding EN1 decoding schema while the index separately retains `standardSchemaIdentity`. Layout selection supplies no Copy evidence. The fixtures now retain explicit constructors storing an inline Payload or a normal Leaf handle, so their provider Bodies need the same evidence even when the eventual query is expected to refuse. |
| [`Scalar initializer tests`](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmStandardClassSourceEmissionTests.cs), [`Body tests`](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmObjectConstructionBodyTests.cs), [`EN1 tests`](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmC1EnumClosureSourceTests.cs) | Existing mixed scalar stores and initialization-authority cases do not establish enum/struct/handle constructor support. EN1's imported class-handle struct test explicitly preserves unknown ownership and rejects an ownership-sensitive use; type retention is not Copy evidence. |

[A2](standard-trrm-rootless-copy-type-evidence.md) already proposes positive
struct Copy evidence; [C1M1U1](standard-trrm-c1-class-declaration-ownership.md)
already proposes total class uniqueness. Neither selects their composition
with Construction/EN1. Recommend importing **only those declaration deltas**
below, not their array, rootless-native or generic-publication profiles.
No accepted source/ownership rule is replaced.

## Exact schema scope and reused declaration facts

Define exactly seven **new, unapproved whole-schema agreements**: the two
non-PI1 EN1 compositions, their two directly required class-layout
compositions, and three admitted non-EN1 C2 source-class compositions.
The latter preserve [Construction-2](standard-trrm-c1-construction-runtime-boundary.md),
[CM1](standard-trrm-c1-class-parameter-defaults.md#one-exact-candidate-agreement)
or [CM1AR1](standard-trrm-cm1-associated-relation-constraints.md#provisional-choice-cm1ar1)
respectively, adding only this document's evidence.

| Exact parent ([EN1](standard-trrm-c1-enum-closure-composition.md#exact-whole-schema-selection), [class layout](standard-trrm-en1-class-field-layout.md#exact-successor-identities), or non-EN1 owner above) | Exact selection literal |
|---|---|
| Construction-1L1R1EN1 | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-1-parameter-labels-1-receiver-access-1-enum-closure-1-initializer-copy-evidence-1` |
| Construction-2L1R1EN1 | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-enum-closure-1-initializer-copy-evidence-1` |
| Construction-1L1R1EN1 class-instance-layout-1 successor | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-1-parameter-labels-1-receiver-access-1-enum-closure-1-class-instance-layout-1-initializer-copy-evidence-1` |
| Construction-2L1R1EN1 class-instance-layout-1 successor | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-enum-closure-1-class-instance-layout-1-initializer-copy-evidence-1` |
| Non-EN1 Construction-2 | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-initializer-copy-evidence-1` |
| Non-EN1 CM1 | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-class-parameter-defaults-1-initializer-copy-evidence-1` |
| Non-EN1 CM1AR1 | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-class-parameter-defaults-1-associated-relation-constraints-1-initializer-copy-evidence-1` |

These are **three distinct non-EN1 parents**, not every C2-related constant.
The two CM1 constant names in the emitter resolve to the same full literal,
so need one candidate, not another alias. Standalone C2L1R1 is rejected by
the public class-mapping overload and its switch arm does not forward
`sourceClassExports`; its classless generic route is not this producer.
Non-EN1 Construction-1 rejects class closure. Neither receives another
composition here; D1/GD1-only or other unlisted siblings are not inferred.

CM1 and CM1AR1 are necessary because their admitted source-class route
retains the same real I/P Bodies while also retaining class parameter
surfaces and, for CM1AR1, associated-relation contracts. Preserve those
inherited facts, not a downgrade to base C2 or EN1. This amendment does not
enable aggregate/handle/pointer/enum defaults, generic stored fields or new
constraint forms.

The layout-bearing rows inherit that owner's exact numeric-layout rule,
target and dependencies, adding only this document's Copy evidence. They are
needed for complete constructor publication under the selected layout
agreement, not a second store encoding. A non-layout Copy-evidence selection
cannot select numeric layout; an older layout-only selection cannot carry
these new declaration facts. Neither suffix order nor parent selection is an
alias for another literal; only the full strings listed above are selected.

**No PI1 composition is selected.** The required Body path is role-1 I plus
direct FieldDef stores; it needs neither PropertyDef backing nor Construction
role-3 property-initializer guarantees. Both source class writers reject
property-backed field substitutions; EN1 source-free class re-emission also
explicitly refuses PI1. There is no concrete required PI1 path here.
[PI1](standard-trrm-c1-property-initialized-state.md) remains separate;
property-required closure must refuse, not omit those facts or infer a
composition.

Each is exactly its named parent plus this document's deltas. Select the full
literal ordinally and case-sensitively for root, every supplied provider
including unused inputs, and retained origins before schema-dependent
decoding. This is not suffix parsing or an optional overlay. EN1-bearing
agreements can represent enum-free programs without requiring an enum
Feature absent a retained enum use. Non-EN1 agreements acquire neither enums
nor numeric class layout from a Feature row or this Copy amendment.

Preserve `(1,0)`, edition 1, all row widths, Body kind/encoding `01` and parent
catalog edges. Allocate **no opcode, record, source-use value, Feature,
section, DefId or numeric target ID**. In the existing 44-byte TypeDef:

| Item | Exact candidate rule |
|---|---|
| Kind/visibility | Preserve each parent's visibility rules. EN1-bearing agreements keep `(flags >> 2) & 7`, kinds 1/2/3/4. The three non-EN1 agreements retain kinds 1/2/3 in bits 2..3, with bit 4 reserved zero. |
| Kind 1 struct | Reuse A2's `0x20=DeclaredCopy`: positive universal inline Copy promise, including expected proxies; clear is unavailable, not known Copy or unique. Reject this bit with that struct's owning Cleanup clause. |
| Kind 2 class | Reuse C1M1U1's `0x40=DeclaredClassUnique`: total known Boolean on every retained class and expected proxy; clear is known aliasable, set is known unique. Preserve independent class-only mutability `0x80`. |
| Applicability/mask | EN1-bearing combined mask `0xFF`; non-EN1 combined mask `0xEF`. Bits 8..31 remain zero. `0x20` is struct-only; `0x40/0x80` are class-only. Interface/enum rows clear all three. No A3 struct-Unique meaning is imported. |

These are mandatory declaration semantics of these exact agreements, not
Feature 2 rootless-native admission. A Copy struct can contain supported
aliasable handles; its positive bit does not assert rootlessness or
Blittable representation. Parent Feature requirements remain conditional:
in particular EN1's 18/11, typed frozen values' 11, fields/imports' 13 and
Construction-2's 25 keep their existing meanings. CM1's parameter surfaces
and CM1AR1's associated-relation requirements likewise remain inherited,
including their conditional 26/23 requirements. No new general Copy,
generic-domain, array or ownership-transfer execution capability is selected.

All older agreements, including Construction, CM1/CM1AR1, EN1, A2 and
C1M1U1, retain their exact meanings. Their reserved clear bits cannot supply
these facts.
No relabeling, automatic conversion, alias, fallback or API/CLI default is
defined. Reuse existing typed source inputs; CLI transport expansion is
outside this Body-specific decision.

Producers obtain these facts from checked declarations or supported retained
evidence, never from field shape, a constructor role or an unknown ownership
value coerced to false.

## Exact value cases and effects

Apply this amendment only to **class-initializer field stores**, not ordinary
member replacement, struct constructors, properties or arbitrary calls.

| Source value, after checked complete-type normalization | CopyRead rule |
|---|---|
| Existing supported scalar parameter/constant | Unchanged. No new contextual default, null or zero-initialization rule. |
| Nongeneric nominal enum E, in an EN1-bearing agreement only | Admit a live by-value parameter or already computed E, including an EN1 typed constant. Preserve exact E, base, mathematical value and target binding. Equal-width integers or another enum are not E. No backing hold or source-place loan is created. |
| Already-admitted nonoptional raw data pointer | Unchanged descriptor CopyRead. Preserve the complete pointer TypeSpec: assigned `ptr` family in form `01`/`0C`, or form `08`, including pointee nominal identity, pointer depth, every access qualification and target binding. Pointer-node GC/init qualifiers remain invalid; `write` controls only immediate referent access and cannot upgrade the source's authority. Copy the pointer cell, not T; pointee Copy/uniqueness evidence is not required merely to copy that cell. |
| Already-admitted nonoptional native function pointer | Unchanged descriptor CopyRead. Preserve form `0D`, zero qualifiers, exact calling convention and complete signature/contracts through its bodyless, noncallable MethodDef anchor. Identity is the resolved contract, not the anchor row/name or an equal-width data pointer. |
| Nongeneric inline struct S | Require positive `DeclaredCopy` on the exact local declaration or imported expectation **and** bound actual. Copy into independent destination field storage; preserve complete child types and dependencies. Nested inline structs require their own positive promises. Complete supported enum, pointer and handle children follow these same rules. Unknown, unique or Cleanup-owning inline values do not qualify. |
| Ordinary nonoptional normal class handle C, including legal `write C` | Require known nonunique declaration evidence, exact complete type/access and a complete live object. Copy aliases that same object, not its payload. The field obtains its own normal-backing residence obligation; the source remains live. Payload fields need not themselves be Copy to make the handle aliasable. |

This adds no aggregate/handle constant codec, optional/GC/box/ref/own/view
admission, generic stored value or unique move. Any independently required
representation, capture or retirement contract must already be supported.
Source-collector recognition alone cannot admit a type absent the selected
parent's codecs.
An unpublished construction receiver is not a complete C source.

For layout-bearing selections, Copy eligibility still assigns **no new field
representation**. In particular, storing a Leaf handle in a Box constructor
can have a valid Body while numeric layout of Box still refuses: the inherited
layout agreement does not represent that handle field. Complete closure must
remain publishable for this retained symbolic-query fixture before numeric
use refuses, not be bypassed by omitting the constructor, unqueried field or
required evidence. Required early numeric evaluation keeps its existing
refusal boundary.

Pointer cases preserve the accepted [descriptor](../specs/language/references.md#descriptors-referents-and-bindings)
and [native code-lifetime](../specs/native.md#runtime-and-code-lifetime)
rules. Retain actual provenance, backing/pin/owner endpoints and code/provider
origins required by later uses; no owner, normal hold, root, pin, environment
or unload lease is acquired by a pointer copy. Independently validate pointee
type formation/binding and function-pointer ABI/contracts. Copy does not
initialize, dereference or prove validity of the pointee, nor invoke code.
Descriptor copying adds no blanket unsafe requirement or authorization:
formation, dereference and invocation retain their existing explicit unsafe
obligations and caller-unsafety metadata. An `unsafe` declaration still
does not authorize its body. A known dangling raw address retains only the
existing [unsafe inert-data policy](../specs/lifetimes.md#definitions-and-notation),
never permission to dereference it.

Copying a value does not borrow its parameter slot or extend an explicit
owner/guard/loan endpoint. Preserve all contained backing, root, code,
access and whole-source bounds; a positive Copy fact cannot erase them.
`ref<C>` still designates a handle slot, not a by-value C. BorrowRead alone
cannot install an independently retained field.

For native execution inherit the separately selected
[normal-backing agreement](reference-normal-backing-support.md#hold-acquisition-copy-publication-and-retirement)
through [Construction-2](standard-trrm-c1-construction-runtime-boundary.md):
each new logical handle residence acquires one hold on the same actual
control, with typed traversal for Copy aggregates. Register aliases add none.
This is evidence-backed reuse of that candidate's nonallocating, nonfailing
retain/drop, **not** an assumption about pointer bits, tracing or arbitrary
class handles. Keep its explicit host/runtime-instance selection and
runtime-only-retirement gate; metadata success selects no native support.

A successful store changes only that vacant field to live, with its actual
acquisition and dependencies. It neither consumes the source nor completes
the parent. Preserve I's exact member guarantees and phase order;
Construction-2 Publish still requires completed I, final validity and all
surviving obligations. Before publication, failure retains actual acquired
children for the existing reverse-acquisition settlement, never incomplete
parent Cleanup. Policy-zero termination gains no unwind promise.

## Existing Body representation and checking

Use existing [typed records](../specs/il-instruction-set.md#typed-operand-records):

* `04 PlaceSelection`: Field target `2`, Initialize access `3`, destination
  Register `1`, base Home `2` selecting I's role-1 receiver, exact FieldDef
  **row** with `targetRefKind=1`, exact referent TypeMap and zero flags.
* `05 SemanticStore`: `placeKind=1`, that place register,
  `sourceKind=1|2` for a live value Register/Home, `transition=1=Initialize`,
  **`sourceUse=1=CopyRead`**. Preserve its existing 20-byte total extent.
  Registers are R0..R13 Value carriers, not places or TypeIdentity. Homes are
  ordinary by-value role-2 formals or eligible live role-3 locals, never
  receiver/context Homes or initialization/borrowed-slot inputs.
  Formal/dependency fields are zero only when their required relationship is
  absent; otherwise retain their existing exact contract or refuse support.
* Existing D8/D9 or equivalent admitted compact forms express the operation.
  A typed enum constant uses existing ConstantMap/D4: both map type and frozen
  leaf name E, never its underlying integer. No enum member token is added.

Source production may map direct checked by-value formal reads and supported
scalar/enum constants through this path, including the collector's existing
raw-pointer formal reads. This selects no pointer constant/conversion,
NativeAddress, dereference or invocation recipe, new expression evaluation,
aggregate construction or hidden copying helper.

Source/Body checking must establish live complete sources, actual vacancy,
unpublished authority and field-state/dependency truth. Structural consumers
validate record framing/indexes, source carriers, exact normalized types,
I/class/member ownership, initialization access and selected required
evidence; they check every original expected/actual binding before affected
use. No class-kind-to-Copy shortcut, field-layout inference or absent-Cleanup
heuristic is valid. Struct Copy requirements must survive provider replacement;
class uniqueness compares exactly in both directions. Unknown remains
unavailable, including after packing. Check a finite inline child closure,
reject inline cycles and missing evidence, and stop Copy recursion at a
qualified eligible handle or raw pointer cell rather than requiring its
payload/pointee to be Copy.

Preserve source availability and value/dependency identity through register
aliases, CFG joins and P-to-I staging. Genuine checked transfer/elision may
reuse existing contracts only when semantically equivalent; `sourceUse=2`
must not replace a source Copy merely because Copy evidence is missing.
Borrowing or a bare `mov` is likewise no substitute. Required unsupported
facts refuse complete publication, not just eventual execution.

On integration, amend [TypeDef metadata](../specs/il-metadata.md#section-kinds-and-row-layouts)
and [registry](../specs/representation/registry.md) applicability, declaration
capture and bound expected/actual views, selected readers/writers and the
initializer-specific eligibility path together. Rebuild TypeDef/Import,
TypeSpec, FieldDef, I/P, Home/Type/Constant maps and source locations under
their owning contexts; retain original expectations and typed constant
roots. Visit nested pointee types and remap function-pointer anchors,
signatures and contracts without losing actual code origins. Do not widen
unrelated uses of the shared copyability helper.
Propagate each complete selection through source/provider snapshots,
indexing, Body validation, loading, re-emission and native binding plans.
Shared codecs may remain shared, but mapping a new literal to old EN1
decoding alone cannot supply Copy-evidence support or discard the separate
layout selection. The two layout-bearing agreements must additionally retain
the inherited Layout/profile facts and existing `0045`/`004A` query
dependencies through remapping and folding; none of the other agreements
gains numeric-evaluation/lowering authority.
The [consumer-trust split](../specs/representation/reader-trust.md) remains:
producers owe truthful Copy/body facts; ordinary consumers need not
semantically re-prove arbitrary producer Bodies.

## Alternatives and acceptance

**Selected:** existing CopyRead plus the two existing declaration carriers.
Merely broadening scalar/kind admission loses source-free ownership evidence;
transferring a named source instead changes its contract. A new copy opcode,
field-specific Copy clause or per-store ownership bit duplicates type-level
facts without solving backing support. A unified ownership/capability schema
is a possible larger redesign, but unnecessary for these initializer stores.
This proposal intentionally defines a new exact composition rather than
silently changing the parents' unknown-evidence meanings.
Explicit replacement of unpublished parents is also permissible; distinct
literals make the changed guarantees selectable, not a compatibility duty.

Required future tests, **not results**:

| Family | Required cases/outcomes |
|---|---|
| Positive values | Preserve existing scalar stores; initialize enum fields from parameters and typed constants, including unnamed values, duplicate-valued members, wide signed/unsigned endpoints and target-bound native bases. Retain nominal E after source-free reload. |
| Existing pointer copies | Direct and Copy-struct-contained data/function-pointer parameters retain source liveness and exact TypeSpecs through D9, reload and remapping. Cover nested nominal/enum and independently admitted no-Copy-evidence pointees without demanding pointee Copy; preserve function-pointer conventions/signatures/contracts. Unsupported pointee formation, access/signature mismatches, lost required backing/code origins and missing authorization for actual unsafe operations refuse; descriptor storage alone grants none. |
| Struct evidence | Local and imported positive-Copy structs, nested private scalar/enum fields, repeated stores from one formal and independent inline storage succeed. Trivial unique/no-promise structs and expected-Copy/actual-no-promise replacements refuse, even with identical layouts. |
| Handles/backing | Under admitted N/Construction-2, copy one normal handle into two fields, preserve the source, then retire source/parameter and field residences: keep the same child alive until its last hold, release once, and add no hold for register aliases. Exercise supported handle-containing Copy structs without borrowing the old inline slot. |
| Negative values/state | Reject wrong nominal enum/struct/class despite equal bits, access upgrades, unknown/unique handles, borrowed-slot substitution, partial/vacant sources, repeated/live field initialization, premature receiver escape/Publish and CopyRead without required dependencies/support. Missing evidence never triggers Transfer/BorrowRead fallback. |
| Cross-feature/rewrite | Exercise all seven exact selections, retaining L1/R1 and CM1/CM1AR1 facts only where inherited. Non-first providers, aliases/diamonds, expected stubs, row/map permutation and Import-to-direct packing preserve Copy/uniqueness, exact E constants where admitted, I/P and original obligations. Old/mixed supplied schemas, including layout-only and PI1+EN1 parents and unused providers, refuse on this route. Required property/backing closure cannot be dropped. |
| Non-EN1 C2 producer closure | Through the seven-argument class-mapping API, base C2, CM1 and CM1AR1 successors retain real Copy-struct/normal-handle field-store I/P Bodies and their evidence through source-free loading/re-emission. CM1 variants preserve scalar/unit defaults, full label vectors and I/P correspondence; CM1AR1 also retains existing associated-relation declarations/formal obligations. Non-scalar Copy inputs acquire no new default codec. Standalone C2L1R1 and non-EN1 Construction-1 do not gain a source-class route, and non-EN1 selections still reject enum closure. |
| Direct layout composition | Under both new layout-bearing agreements, emit complete providers with Copy-struct and normal-handle constructor field stores and retain their real I/P Bodies as applicable. Eligible complete geometry keeps its numeric query behavior. The trailing-handle fixture must pass constructor/provider validation before refusing numeric layout; no known-prefix shortcut is added. Re-emission retains both agreements' facts. Non-layout Copy evidence alone cannot authorize folding/lowering, and layout selection alone cannot authorize Copy. |
| Completion/support | Keep source liveness, field guarantees and final validation separate. Unsupported capture, authored retirement, GC, runtime-instance or failure routes refuse before use. Headless native qualification is separate from semantic round-trip evidence and must honor N's existing gates. |

Formal approval, implementation and qualification remain outstanding. This
document changes no normative owner, support inventory, compiler or test.
