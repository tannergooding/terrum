# Field provenance for fresh inline unique construction

**Status: unapproved; provisionally actionable solely under the user's
authorized missing-contract instruction.** This is a bounded amendment to
[field-projected value provenance](reference-field-value-provenance.md),
including its private candidate 95. It changes no normative specification,
approves no adjacent proposal and establishes no implementation support.

## Decision and scope

Extend **`FreshInlineConstruction = 2`** to an ordinary synchronous Terrum
instance constructor's fresh inline **unique struct** receiver. Keep every
individual copy-origin fact restricted to an already admitted **Copy
subtree**. The containing unique value is neither copied nor made Copy.
Reuse the existing summary, paths, source views and normal-completion phase;
add no summary kind, source role, source syntax or runtime operation.

Amend the unpublished candidate 95 **in place**, rather than allocate a new
private revision or a unique-transfer summary. Its record bytes are unchanged;
its exact selected semantics include this amendment. Ordinary unique normal
results remain ineligible. A constructor result can subsequently move through
ordinary local ownership flow without losing its actual child facts; that
does not publish an origin guarantee for an independently called unique
factory.

Prerequisites are the existing exact-field/value ledger, ordinary stored
checked-reference contracts, structural unique construction/cleanup and the
complete declaration/type/field identity needed by the selected use.
Persistence and execution additionally require their independently supported
type, ownership, reference-operation and target representations.

### Why this is the missing contract

An inline unique aggregate can contain both an owning field and a Copy
reference descriptor. Copying that descriptor into fresh receiver storage
does not duplicate the owner. Its exact selected referent should not disappear
merely because its containing aggregate is unique.

The motivating construction has `ReferenceOwner.Buffer` of
`OwnedInt32Buffer` and `ReferenceOwner.Target` of `write ref<int32>`, with
constructor formals `(buffer, target)` and
`[this: Captures(nameof(target))]`. After the independently authorized move of
`Buffer`, the still-live `Target` needs its own reference facts. Neither
Captures alone nor unioning all constructor arguments establishes that map.
The source capture relationship remains necessary and is not narrowed.

| Inspected source evidence | Consequence for this recommendation |
|---|---|
| [`BoundProgram.cs`](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs), `BoundCopyOriginSummary`, `BoundCopyFact` and `BoundCopyOriginFieldPath` | Kind 2 already means fresh inline construction; facts have exact destination paths and entry-source alternatives. Uniqueness belongs to the complete type, not a missing origin tag. |
| [`SemanticVerifier.TokenSpans.cs`](../../sources/Terrum.CodeAnalysis/Semantics/SemanticVerifier.TokenSpans.cs), `CopyOriginSummaryBuilder.Create` and `ValidateCopyOriginSummary` | Both currently require a Copy constructor root. The per-fact validator separately requires an admitted Copy destination subtree and exact source-type agreement. The proposed change separates those two tests rather than weakening Copy. |
| The same file, `CopyOriginFieldLeaves`, `CopyOriginFactsForFreshReceiver`, `CreateCopyOriginCallResult` and `CreateReferenceFieldCallResult` | Existing mechanisms enumerate inline paths, derive fresh-receiver facts and instantiate selected source facts. These are the integration points; the result must not inherit the unique root's storage identity. |
| The same file, `CheckReferenceReturn` and `AggregateReferenceReturnDependsOnFormal` | Aggregate reference publication already checks incoming targets/descriptor obligations and maps constructor sources through receiver Captures. Bypassing that check is not the fix. |
| [`SemanticVerifier.cs`](../../sources/Terrum.CodeAnalysis/Semantics/SemanticVerifier.cs), `BoundReturn` and `BoundCall` verification | Fresh-receiver observation follows successful normal-return cleanup. Calls stage inputs before applying call effects and retain separate reference-field results. The extension must preserve those phase distinctions. |
| [`TokenCursorTests.cs`](../../tests/Terrum.CodeAnalysis.Tests/Lexing/TokenCursorTests.cs) | `CopyAggregateReferenceFieldsRetainTheirTargetsAsync` and `GenericAggregateReferenceFieldsRetainTheirTargetsAsync` provide nearby Copy cases; `PartialOwnerMovesPreserveSiblingReferenceFieldsAsync` is the unique-constructor regression target, not evidence that this amendment already passes. |

These are source observations, not new execution results. The reported failing
unique-constructor experiment identifies the gap; this documentation task
performs no compiler or native qualification.

## Exact semantic amendment

The semantic shape remains:

```text
CopyOrigin = (SourceKey, EntryValue | EntryReferentValue, sourceFieldPath)
CopyFact   = (destinationFieldPath, nonempty set<CopyOrigin>)
Summary    = Unavailable | (NormalResult=1 | FreshInlineConstruction=2,
                           nonempty set<CopyFact>)
```

Kind 1 retains the original Copy-result contract. For kind 2, replace the
Copy-only root restriction with:

* The independently established callable role is a synchronous Terrum
  instance constructor initializing its own fresh, unpublished inline struct
  receiver. Its complete constructed type may be Copy **or unique**.
  Class/box/heap construction, existing-holder calls, static constructors,
  deferred completion, raw foreign declarations and function-pointer anchors
  are not admitted by this amendment.
* The root is the exact constructed owner instance under its complete
  substitution, not an ordinary factory result or an ABI result buffer.
  Constructor role, initialization authority and uniqueness must already be
  represented; a name, summary tag or body absence supplies none of them.
* For a unique root, every destination path is **nonempty** and selects a
  completely initialized, valid, provenance-bearing Copy subtree at the
  constructor's normal-completion boundary. An empty destination would select
  the whole unique value and rejects. A unique field/subtree cannot be the
  copied subject, even when it contains Copy descendants.
* Paths can traverse structurally identified inline unique or Copy ancestors.
  Traversal copies no ancestor and grants no partial-move permission. Every
  step names an exact immediate instance field under the current complete
  owner/substitution, including admitted implicit backing in structural
  declaration order. Names, offsets and equal field ordinals in unrelated
  types do not establish identity.
* Source and destination selections have exactly the same complete admitted
  Copy type, including nominal origin, generic arguments and all reference
  qualifications. A copied descriptor preserves its selected referent and
  supporting facts; it is not a reference formed to the source field slot.
  No qualifier conversion, ownership transfer or fresh reference formation is
  encoded by a CopyFact.
* Destinations remain prefix-free and free of unsupported physical overlap.
  Following an indirection, raw Union alternatives, optional payloads,
  arbitrary indices and opaque/erased field paths remain outside the schema.
  Different field paths alone prove neither disjointness nor alignment.

An origin guarantees that the selected destination value has the complete
value provenance of copying one listed entry selection, not merely equal
address bits. Alternatives are an unconditional set with no positional
cross-field correlation. Keep every applicable enclosing source bound and
the constructed receiver's complete capture relationship independently.

Only the destination container is generalized. Source eligibility remains:

| Source/view | Selected value and restrictions |
|---|---|
| Explicit formal, `EntryValue = 1` | The already staged by-value Copy input of its complete formal type, or a Copy subtree selected beneath that admitted Copy root. An ordinary checked-reference descriptor is itself a value here; copying it does not snapshot its referent. |
| Explicit formal, `EntryReferentValue = 2` | The completely initialized Copy `T` contents selected by an already admitted checked input-reference view at Invoke, then its source path. The root is `T`, not the reference slot. This does not newly admit unique referent roots. |
| Receiver | **Ineligible for this constructor summary.** The fresh receiver is a destination, not an entry input. Existing ordinary Copy-member receiver sources remain unchanged outside this amendment. |

By-value unique inputs, initialization/output formals, NativeOutput cells,
raw pointees and callee-local storage are not sources. A Copy descriptor may
designate a non-Copy referent only where its independently admitted reference
contract already permits that descriptor; this does not admit copying the
referent. A desired mapping from a moved unique input's fields requires a
different transfer-origin contract, not relaxed source checks here.

For the motivating declaration, structural field ordinals are Buffer=0,
Target=1 and explicit formal ordinals are buffer=0, target=1. The proposed
summary is exactly:

```text
kind = 2, root = complete ReferenceOwner
destination [Target@ReferenceOwner]
    <- { (Formal(1), EntryValue, sourcePath=[]) }
```

In private encoding the destination path is `[1]`. There is no fact for
Buffer, no whole-owner CopyFact and no receiver-source alternative.
The formal ordinal is not the verifier slot: an internal constructor receiver
in slot zero shifts explicit parameter slots, not declaration ordinals.

### Captures is still the source permission

Every listed source must be an explicit formal authorized by the effective
**constructed receiver's Captures** relationship, with a borrowing/forwarding
permission that permits the actual copied provenance. An ordinary result's
DependsOn is not a substitute. Retain the full source set, including sources
not mentioned by any exact field fact. `Captures()` cannot justify an
input-origin reference field.

ForwardsBacking may refine only its already declared sources and modes.
Copying the original checked-reference descriptor still depends on its
selected referent storage; it cannot be relabeled backing-only. Selecting a
stored descriptor value through an admitted source path does not borrow its
slot merely because the source was a reference. Preserve whichever actual
storage/backing obligations that value carries.

The summary adds no ownership destination, access authority or cleanup
permission. A sibling move remains subject to structural state, derived
versus authored Cleanup, surviving dependents and actual storage generations.
Knowing Target's referent cannot authorize a whole use of a partial owner.

## Invocation, publication and failure

1. Select the constructor and complete type first. Evaluate/stage supplied
   arguments once in ordinary source order, including named arguments; bind
   sources by explicit formal identity, not evaluation position. Unique
   arguments retain their ordinary staged ownership but are not origin inputs.
2. Save EntryValue facts at by-value staging. For EntryReferentValue, save the
   selected reference/place identity, then obtain its current initialized Copy
   contents at **Invoke**, after later argument effects. An admitted implicit
   reference temporary uses its own staged value. These compiler snapshots
   introduce no runtime read, copy, root, allocation or lifetime extension.
3. Keep the saved entry facts separate from caller places that the invocation
   may mutate. Apply ordinary call effects to those places; the result
   guarantee does not restore their old contents. Unknown effects invalidate
   affected precision, not ownership or outstanding dependency obligations.
4. Instantiate kind 2 **only on ordinary successful constructor completion**,
   after its required postconditions and normal-return cleanup succeed.
   The producer must establish the mapping at that boundary, including any
   changes made by cleanup. A body-return snapshot alone is insufficient.
5. Install the mapped child facts in the fresh receiver without copying the
   unique parent or inventing a borrow of its former/ABI storage. Caller
   initializer entries and completion validation remain later unpublished
   phases in their existing order. Later field writes update/invalidate the
   relevant facts normally; a constructor guarantee is not reasserted after
   those writes. Publish the complete parent only at its existing final gate.

Each covered subtree must be live and valid at the constructor boundary.
Other initialization obligations can remain only where the existing declared
construction contract permits caller completion; the summary adds no such
permission. It never makes a partial parent complete.

No normal summary applies on later-argument failure, constructor failure,
failed normal epilogue or an absent normal edge. A no-return constructor has
no present summary. Failed caller initialization/final validation publishes
no complete parent even if constructor completion supplied child facts.
Keep actual acquired/staged child obligations until their real retirement:
failed unpublished construction unwinds remaining acquisitions in reverse
actual acquisition order, with complete children using their complete cleanup
and no invocation of incomplete-parent Cleanup. Consumed inputs do not revive.
Existing-holder mutations/captures surviving failure are not rolled back.
Published complete/partial retirement keeps its separately specified order.

Unavailable origins remain **Unavailable**, not known empty or independent.
Missing coverage, an unavailable source dimension, an unsupported effect or
failed/exhausted inference supplies no selected target. A present mapping
cannot turn an unavailable actual source into a known one. Uncovered Copy
fields retain their complete types, established state and whole-source bounds;
owning fields retain their independently established transfer/cleanup facts.
Absence of an origin summary does not erase any of those obligations.
Uses supported by the remaining contracts can proceed; an exact-target use
without sufficient facts must refuse, not guess from sibling owners or types.

## Exact private candidate-95 amendment

Keep the complete candidate-95 composition on candidates 93/94 and its single
ModuleFunction declaration attachment, after general dependencies and before
the expected-vtable Boolean. The field remains:

```text
u8 summaryKind                 // 0 unavailable, 1 NormalResult, 2 FreshInlineConstruction
if summaryKind != 0:
    i32 bindingCount           // >= 1
    repeat bindingCount:
        Path destination
        i32 alternativeCount   // >= 1
        repeat alternativeCount:
            u8 sourceKind      // 1 explicit formal, 2 receiver
            u8 sourceView      // 1 EntryValue, 2 EntryReferentValue
            u16 reserved = 0
            u32 sourceOrdinal
            Path source
Path:
    i32 depth                  // >= 0; destination >= 1 for a unique root
    repeat depth:
        u32 instanceFieldOrdinal
```

All integers remain little-endian. A path is `4 + 4*depth` bytes; an alternative
is `12 + 4*sourceDepth`; a binding is
`8 + 4*destinationDepth + sum(alternative extents)`. Tag 0 is one byte;
a present summary is five bytes plus its bindings. For a unique constructor
each binding has at least 24 bytes. Counts/extents require bounded,
nonwrapping preflight before allocation. Preserve candidate 95's canonical
destination and alternative ordering, duplicate/prefix/overlap rejection,
reserved-zero checks and exact type/field/source validation.

For kind 2 with a unique root, only `sourceKind=1` is valid, in one of the
two admitted views above. Kind 1 still requires a Copy result. Unknown kinds,
empty present collections and a root-wide unique binding reject.
Structural ordinals resolve against each immediate complete containing
type's ordered instance-field collection; in-memory owner identities are
derived and checked, not additional path bytes. Include this declaration
attachment on definitions and bodyless expectations; no second Body-owned
summary or serialized verifier cache is added.

The exact amended standalone schema selection is
**`private-95-fresh-inline-unique-1`**: all of candidate 95, with only the
constructor-root eligibility/unique-path rules specified here changed.
This is an out-of-band whole-schema identity, **not an on-wire string,
Feature or extra revision field**. It is not the original Copy-only 95
selection. A later private composition must explicitly agree its complete
schema with this amendment before consuming it; revision ordering or a
reference to “95 or later” is insufficient.

A separate summary kind 3 or private revision would be possible, but repeats
the already required root uniqueness/constructor-role distinction and solves
no additional semantic gap. Unpublished drafts need no compatibility reader.
Select the amended schema explicitly, reject unsupported affected input and
regenerate artifacts as needed; do not infer a decoder from the type, matching
revision bytes or plausible fields. Outer native-container schemas remain
independently selected; rebuild affected framing/digests and generated images,
not just headers.

This amendment does **not** widen
[Standard TRRM copy-origin summaries](reference-copy-origin-trrm.md).
That proposal's selected standard kind-2 role remains Copy-only. Neither
`CopyOriginDraft`, its Feature 15 nor another existing standard schema
silently gains unique-constructor support. A standard path requiring this
retained guarantee remains unsupported until separately selected and
represented; adding standard bytes is not necessary for this local/private
decision.

## Producer, consumer and replacement obligations

Source checking derives/checks facts once per constructor from typed
operations and already established callee summaries. Intersect covered paths
across reachable successful completions and union their possible origins;
unavailable on one successful edge prevents an unconditional known fact there.
Recursion or analysis limits cannot bootstrap an origin. Preserve
`CheckReferenceReturn`'s lifetime/source checks independently: deriving a
copy relationship does not prove that the copied reference remains valid
through cleanup or final publication.

Publishing a summary makes it a producer truth obligation. A bodyless
expectation for a selected ordinary Terrum constructor can retain it without
access to the provider Body. Such a proxy is not a raw foreign declaration
or source-level permission to write an otherwise illegal bodyless constructor.
Consumers check exact schema/framing, counts, tags, constructor role, complete
unique/Copy roots, field identities/paths, source ordinals/views, Copy type
agreement, capture permissions and required support before affected use.
They need not semantically re-prove the producer's body. A structurally valid
but false origin mapping violates producer truth; consumer detection is not
promised. Optional semantic audits remain separate.

Binding preserves the original whole-source and ownership expectations.
After exact identity/substitution mapping, a provider satisfies an expected
known field fact only if it covers that destination at the same constructor
phase with origins contained in the expected alternatives. Expand a copied
Copy subtree to required descendants before comparison. Missing/Unavailable
cannot satisfy known. Extra precision can be ignored, or retained explicitly
if used; it cannot silently rewrite the original expectation. A lost/swapped
mapping is incompatible even if Captures still matches.

Projection, substitution, cloning, packing and replacement must retain the
constructed owner, complete referenced types, each ordered field declaration,
formal correspondence and relied-on summary. Rebuild structural ordinals
after field/type remapping; retaining nonpublic field identity grants no
source access. Unsupported declaration/ownership/reference transport refuses
the precision-dependent use rather than dropping the requirement or peeking
at a provider body. Retain the same expectation in generated native artifacts
when relied on, including after machine imports are erased.

## Qualification cases

These are required outcomes, **not passing-test or implementation claims**.
Use public compiler APIs and unrelated synthetic types as well as the
motivating buffer case.

| Family | Required outcome |
|---|---|
| Basic positive | Construct a fresh inline unique owner from a moved owning input plus an external writable reference input. Publish only the Copy reference field's formal origin. An allowed sibling-owner move preserves use of that live reference field; repair restores completeness under existing rules. |
| Nested/generic positive | Traverse nested inline unique ancestors and Copy descriptor-containing children; compose fresh constructor calls from eligible Copy inputs. Preserve exact `ref<T>` referent identity/qualifications after supported substitution. Moving an unrelated sibling does not invalidate the selected target. |
| Copy boundary | Existing Copy constructor and ordinary Copy-result summaries retain their meaning. Reject an empty-path unique destination, a unique destination subtree, a by-value unique source or unique referent-root source. No origin summary is manufactured for an ordinary unique-returning factory. |
| Capture negatives | Reject a producer reference escaping a local, an origin absent from receiver Captures, substitution of result DependsOn for constructor Captures, or false backing-only forwarding of the original reference. An unused extra capture still constrains the whole retained value. |
| Storage/access negatives | Copy a stored descriptor versus borrow its field slot; only the latter depends on that slot's storage. Preserve readonly access, actual aliasing, alignment and generation. Moving/replacing a referenced ancestor or invalidating its actual owner/guard must still reject. Authored complete-value Cleanup still prevents otherwise illegal partial moves. |
| Staging boundary | Use swapped named arguments and a unique first argument. A later argument mutates a referenced Copy field before Invoke: EntryValue retains its staged value, EntryReferentValue sees the current contents. Failure before Invoke publishes no receiver and cleans only actual staged holders, without source revival. |
| Normal completion | Different successful branches produce origin alternatives; an unknown successful branch makes that path unavailable. A normal `finally`/postcondition changes or invalidates a field: publish only its final truthful origin or none. Throwing/divergent paths and no-return constructors produce no normal guarantee. |
| Caller completion/failure | A caller initializer replaces a summarized field; the new value wins. Constructor, caller-initializer and final-validation failures preserve actual acquisitions and reverse-acquisition cleanup, never incomplete-parent Cleanup. Whole-parent use stays gated until final publication. |
| Owners and mutation | Whole local owner moves transfer actual child facts without Copying the owner. Unknown/aliased writes invalidate only supported affected precision and retain obligations. Repair/replacement cannot narrow the broad capture bound or invent field disjointness. No fresh-receiver/self-reference is encoded as an entry input. |
| Unavailable/opaque | Remove the summary or required source facts; retain whole bounds and ordinary ownership but refuse exact-target uses. Bodyless, recursive, unsupported witness/fnptr and unsupported serialized paths never become known-empty or trigger call-site body inspection. |
| Malformed private input | Independently encode truncation, negative/oversized counts, bad tags/reserved values, wrong constructor/root/source roles, shifted formal ordinals, wrong generic/field owners, non-inline steps, unique or mismatched Copy subjects, empty/duplicate/prefix/overlap paths and absent capture permissions. Reject structurally unsafe/unsupported input before affected use; separately test a truthful-source checker rejecting a false mapping. |
| Separate compilation/replacement | Strip provider bodies from expectations; retain the exact constructor field fact through supported private restore/projection/remapping and native embeddings. Accept origin-set narrowing, reject missing/swapped required facts and an ordinary factory masquerading as a constructor. Reject an unselected draft despite matching revision bytes. |

JIT and AOT use the same semantic guarantee, not different source rules.
The motivating public regression should execute on each already admitted
runtime-policy/backend combination being claimed; a JIT result alone is not
AOT evidence, and successful emission is not execution. Private round trips,
bodyless binding and native replacement need separate evidence from local
execution. No new runtime/ABI representation is chosen to obtain those results.

## Integration boundary and exclusions

On adoption, add this bounded fresh-unique-container guarantee alongside the
field-provenance proposal's callable semantics and private candidate 95.
Integrate its distinction between whole captures and exact child facts with
[dependencies](../specs/language/dependencies-and-views.md#stored-values-and-flow),
[ownership/construction](../specs/language/ownership-and-cleanup.md#construction-and-publication),
logical declaration records and
[binding/replacement](../specs/representation/binding-and-replacement.md#5-packing-replacement-and-relied-on-facts).
Preserve the accepted
[construction phases](../specs/grammar-spec.md#510-properties-and-accessor-local-effects),
[by-value/ABI distinction](../specs/lifetimes.md#by-value-results-and-borrowed-results),
[reader-trust boundary](../specs/representation/reader-trust.md) and
[exact draft selection](../specs/representation/format-versioning.md).
No normative owner is amended by this document.

The explicit supersession is only the original proposal's exclusion of unique
**fresh inline construction destinations**, its corresponding kind-2
Copy-root restriction and the matching candidate-95 validity rule.
All other exclusions remain: ordinary unique normal results, unique input
transfer summaries, general mutation/heap/effect summaries, fresh acquisition
or self-relative-reference origins, field-granular lifetime permissions,
GC/root/pin runtime, new descriptor layouts/call ABIs, NativeOutput origins,
new source annotations and standard TRRM transport.

There is **no unresolved design choice within this bounded recommendation**.
Formal approval and compiler/codec/qualification work remain outstanding;
the user's provisional direction is not an adoption or support claim.
