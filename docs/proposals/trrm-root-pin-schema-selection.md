# Exact RootPin draft schema and Body record namespace

**Status: unapproved; provisionally actionable under the authorized
missing-contract workflow.** This selects a candidate schema boundary, not
normative adoption, implementation support or execution evidence. It changes
no accepted assignment and approves no additional source or runtime contract.

## Decision and evidence

Recommend one separately selected **B + RootPin** whole-artifact schema.
Keep the four [foreign-invocation profiles](trrm-draft-schema-profile-selection.md#exact-compositions-and-identities)
unchanged, including their **DSP-10 no-composite decision**. Among these
selections, Body record `0D` means ReferenceSupport under the new RootPin
schema and NativeAddress under the existing F-containing schemas. It never
has two meanings within one artifact.

The relevant owners establish a missing composition/selection decision,
not a conflicting normative allocation:

* The accepted [registry](../specs/representation/trrm-1.registry.json#L125-L137)
  assigns Body record kinds `01..0B`, not `0D`. Its current exact schema is
  `trrm/1-draft-layout-queries-1`; `E9` and `F0` are unassigned primaries.
* [NativeAddress](standard-native-address-formation.md#exact-candidate-encoding)
  proposes `E9` and a 12-byte kind-`0D` record.
  [RootPin](reference-root-pin-runtime.md#candidate-typed-body-program)
  independently proposes `F0` and a 16-byte kind-`0D` ReferenceSupport record,
  Feature `14/1` and TargetProfile `(2,1)`. These are intentional provisional
  candidates. TypeSpec `0D`, Contract `000D` and Body TypeMap token `F0`
  occupy different namespaces and do not resolve this Body-kind collision.
* The [F owner](standard-native-import-function-pointer-contracts.md#L409-L414)
  already excludes RootPin and requires a separate noncolliding allocation
  for a combined schema. The [selection owner](trrm-draft-schema-profile-selection.md#L140-L147)
  and [DSP-10](trrm-draft-schema-profile-selection.md#L268) preserve that gate.
* The current low-level
  [Body reader constructor](../../sources/Terrum.CodeAnalysis/Modules/TrRmRegisterBodyReader.cs#L26-L33)
  receives no schema. It [requires kind `0D` for E9](../../sources/Terrum.CodeAnalysis/Modules/TrRmRegisterBodyReader.cs#L567-L573)
  and [dispatches `0D` to NativeAddress validation](../../sources/Terrum.CodeAnalysis/Modules/TrRmRegisterBodyReader.cs#L946-L969).
  This source observation is neither normative ownership nor RootPin support;
  that dispatch cannot be reused unchanged for the new selection.

The accepted [format-versioning](../specs/representation/format-versioning.md),
[registry](../specs/representation/registry.md),
[manifest](../specs/representation/semantic-manifests.md) and
[admission](../specs/representation/requirements-and-admission.md) owners remain
unchanged. Unpublished IDs may be reassigned, but equal `(1,0)` bytes do not
permit guessing which candidate was intended.

## Exact proposed selection

The new case-sensitive out-of-band identity is
**`trrm/1-draft-root-pin-runtime-1`**, with proposed distinct
**`TrRmArtifactSchema.RootPinRuntimeDraft`** selector. It is not a flag,
Feature combination, TargetProfile identity, wire field or CLI spelling.

Here **B** is the complete accepted `trrm/1-draft-layout-queries-1` schema,
including its governing chapters and catalog. **RootPin** is precisely the
Standard-facing delta in [the root/pin proposal](reference-root-pin-runtime.md):
its complete typed program, runtime/target contract, Feature `14/1` with no
unconditional Feature prerequisites, and TargetProfile `(2,1)`. The remaining
B catalog stays unchanged, including incomplete Features `9..12`; this does
not borrow F/G's predicate or frozen-value catalog amendments.

The composition includes the prerequisite operation-derived provenance
semantics, **not** CopyOrigin's `000D`/`15/1` codec or any private ledger
format. It includes no aggregate-output `DF`/record-`0C`/`8/1` delta, F's
`000E`/`16/1`, general graphs, enum codecs or unrelated candidate. RootPin's
aggregate-output cases and instructions to preserve that neighboring
candidate remain conditional on a separately selected combination. For this
profile, this decision replaces any implicit combined-schema reading of
RootPin's allocation/integration sections with exactly B + RootPin.

| Exact selected schema | Body `0D` | Operation/namespace boundary |
|---|---|---|
| B (`Standard`), and historical `trrm/1-draft-copy-origin-contracts-1` (`CopyOriginDraft`) | Unassigned | Neither selection gains E9, F0 or either candidate record. |
| Each of the four existing F-containing schemas below | 12-byte NativeAddress | E9 selects NativeAddress; F retains DF/`0C` OutputReservation. F0, RootPin `14/1` and TargetProfile `(2,1)` are not added. |
| `trrm/1-draft-root-pin-runtime-1` (`RootPinRuntimeDraft`) | 16-byte ReferenceSupport | F0 actions `01..07` select ReferenceSupport. E9, DF, Body `0C` and `0E..FF` remain unassigned. |

Preserve these existing identity/selector pairs exactly:

* `trrm/1-draft-foreign-invocation-contracts-1` / `ForeignInvocationDraft`.
* `trrm/1-draft-copy-origin-foreign-invocation-contracts-1` / `CopyOriginForeignInvocationDraft`.
* `trrm/1-draft-foreign-invocation-general-predicate-graphs-1` / `ForeignInvocationGeneralPredicateDraft`.
* `trrm/1-draft-copy-origin-foreign-invocation-general-predicate-graphs-1` / `CopyOriginForeignInvocationGeneralPredicateDraft`.

Default Standard, historical CopyOrigin, Development and other independently
selected candidates are not widened. No F+RootPin, C+RootPin, G+RootPin or
aggregate-output+RootPin profile is defined here. This is an interchange
selection gate, not a new source-language prohibition.

Revision 1 denotes this complete composition, not future moving contents of
linked proposals. Exchange agrees on the exact schema and catalog, not just
its name or digest. A changed composition needs a distinct explicit selection;
retaining old readers or converters is not required for unpublished drafts.
All candidates retain `(1,0)`, codec `01`, existing Body framing and fixed
one/two/four-byte instruction lengths. No prefix, escape, second kind
namespace, wider record index or extra section is introduced.

## Alternatives and rationale

These are coherent alternatives, **not additional selected encodings**:

| Direction | Consequences |
|---|---|
| **Separate B + RootPin; recommended** | One new exact selector makes RootPin consumable without redefining F or requiring its output/predicate machinery. The shared numeric tag is harmless only because the whole schema is fixed before decoding. Combining retained F and RootPin remains deliberately unsupported. |
| Reassign ReferenceSupport to Body `0E` | Keeps F's `0D` NativeAddress and `0C` OutputReservation; F0 would require `0E`. This is a straightforward allocation for a future composed schema, but still needs a new exact whole-schema identity, reconciled catalog, target/runtime and binding rules. It requires updating the RootPin owner, vectors and all kind checks; `0D` is not retained as a RootPin alias. Moving NativeAddress to `0E` instead is also possible, but cannot silently redefine the four existing F identities. |
| Substantive family repack in a fresh schema | For example, place OutputReservation/NativeAddress at `20`/`21` and ReferenceSupport at `30`, retiring candidate `0C`/`0D` there and changing DF/E9/F0's required-kind mappings. This groups native and managed records without treating present draft numbers as immutable. It rewrites all three candidate owners and their consumers, gives no byte-size or index-capacity benefit with fixed `u8` kinds/`u16` indexes, and still requires explicit schema selection and semantic composition. It cannot inherit an existing F identity. |

The recommendation preserves the already intended no-composite boundary,
not compatibility with unpublished numbers. Neither `0E` nor a repack alone
completes a composite contract. A future requirement to pack both capabilities
should decide that whole composition explicitly; it must not emerge from a
decoder accepting whichever `0D` payload happens to fit.

## Coordinated integration and consumer gates

1. **Select before decoding or writing.** Resolve one exact supported
   selection before schema-dependent indexing, Body record dispatch, writer
   row acceptance or loading. Thread it immutably through indexes, metadata
   and Body readers/writers, manifest closure, binding, caches and lowering.
   The Body-reader boundary above needs schema-aware dispatch/admission,
   including rejection of candidate `0D` under B. Unknown/unsupported selectors
   and mismatched component contexts fail without partial output or load
   side effects. Do not select by record length, E9/F0 occurrence, Feature
   rows, TargetProfile, unsafe authorization, filename or a failed decoder.
   Common-subset bytes may be valid under several explicitly selected
   schemas; their originating profile cannot be recovered from those bytes.
2. **Admit requirements within that schema.** Keep RootPin's per-Module
   `14/1` scope, including retained types, declarations, expected imports and
   unused records, not just executed F0. Check complete conditional native/
   layout requirements independently. Target `(2,1)`, its actual runtime
   domain/provider and all required representations must be supported before
   affected use; rootless `(1,1)` is not a substitute. Neither another
   Module's manifest nor recognizable row shapes completes a missing
   capability. Unsupported handler, ownership, erasure or other independent
   codecs remain gates.
3. **Preserve source/runtime facts.** No new syntax, implicit pinning,
   marshalling or library policy follows from this namespace decision.
   Binding, common bound traversal/rewriting, producer verification and
   AOT/JIT lowering preserve RootPin's typed acquisitions, backing, holders,
   failure/cleanup and code-origin obligations under its linked runtime ABI.
   Lowering must not replace F0 with ordinary reference formation. Interchange
   persists operations, never live cells or runtime addresses. Private saved
   IR, Development and native containers gain no codec by recognizing this
   selector; unsupported retained declarations/operations still reject.
4. **Bind artifacts, not decoder modes.** Each separately supplied dependency
   gets its own explicit selection. Compatible independently supported
   artifacts may bind under the [directed binding rules](../specs/representation/binding-and-replacement.md);
   a root's schema does not select a dependency's schema. Preserve original
   expected contracts and relied-on stronger facts, complete types, runtime
   domain/layout, cleanup and helper/provider code lifetimes. Equal record
   tags or carrier widths cannot bind a GC/root-bearing declaration to a raw
   pointer or rootless contract. Structural support does not claim execution
   support or semantic re-proof of producer body facts
   ([consumer checking](../specs/representation/reader-trust.md)).
5. **Re-encode meaning, never tags.** Remap each selected owner's actual
   record, TypeMap, MethodMap, Home, field and global-reference namespaces;
   preserve fields explicitly designated unused. Rebuild record offsets,
   maps, CFG/code extents and affected SourceMap coordinates under existing
   [canonicality rules](../specs/representation/encoding-canonicality.md).
   Packing retained F and RootPin into one artifact fails before destination
   writing: none of these profiles represents both. Separate Modules do not
   create separate Body-kind namespaces. Only a genuine preservation
   transformation that discharges all uses of one capability could remove
   that obstacle; relabeling, dropping Features or patching `0D` cannot.

On adoption, reconcile the linked registry, instruction/metadata, manifest/
admission and target owners together with source/runtime, interchange,
binding/replacement and lowering support. This proposal supplies only the
profile/namespace decision; it does not duplicate either operation's payload
semantics or runtime ABI. Approval, those coordinated amendments,
implementation and qualification remain separate gates.

## Focused acceptance cases

These are required future cases, not executed results. Positive consumption
assumes the selected operation's complete independent support.

| Case | Required result |
|---|---|
| RPS-01 RootPin positive | Explicit RootPinRuntimeDraft; valid F0/01 with a 16-byte ReferenceSupport record, `14/1`, `(2,1)` and admitted runtime. Decode as root acquisition, never NativeAddress; inspection itself acquires no runtime root. |
| RPS-02 Preserved F/defaults | Exercise E9/12-byte NativeAddress under each supported F identity. Preserve its meaning and catalog; the same candidate form under B/historical C rejects. Existing default selection never auto-upgrades. |
| RPS-03 Wrong-profile content | E9 or 12-byte NativeAddress under RootPin; F0 or 16-byte ReferenceSupport under F. Reject in the selected schema without length-, opcode- or failure-based retry. |
| RPS-04 Preflight/context | Unsupported RootPin selector with otherwise common-subset bytes, unknown selector, or RootPin index paired with F Body/catalog context. Reject before schema-dependent use or output publication. Explicitly selecting each supported schema for common-subset bytes is valid, not provenance detection. |
| RPS-05 Module/capability separation | One artifact attempts NativeAddress `0D` in one Module and ReferenceSupport `0D` in another, even with both Features. Reject; manifests do not choose per-Module decoders. Reject missing RootPin `14/1` and unsupported `14/2`/target `(2,2)`; these checks also cover retained unused records. |
| RPS-06 Closed domains | Truncated/trailing `0D`, nonzero reserved fields, RootPin action `00`/`08`, Body kind `0E`, and F0 used as a prefix reject under the applicable selected schema. TypeSpec `0D` or TypeMap token `F0` remains its separate namespace. |
| RPS-07 Index boundaries | Exercise record/type/Home indexes `0` and `FFFF` with counts through 65,536, including remapping. Reject 65,537, one-past-end and extent overflow. Do not turn zero into absence or remap an owner-designated unused zero as TypeMap[0]. |
| RPS-08 Separate input/packing | Consume separately selected supported F and RootPin artifacts with compatible retained bindings; then attempt to pack retained uses of both into RootPin or any F destination. Reject packing before writing, including when the uses belong to different Modules. |
| RPS-09 Binding/runtime gates | Reject a root-bearing expected signature against a raw-pointer/rootless provider or incompatible runtime domain. A private/Development declaration-only RootPin dependency remains unsupported; no empty Body or native wrapper bypasses it. |
| RPS-10 Preservation | Permute/deduplicate valid records and remap typed references, maps, blocks and source slots within the selected schema. Preserve operation identity, acquisition/retirement order, original bindings and requirements. Reject header/tag-only “conversion”; no compatibility converter is required. |
