# Standard TRRM copy-origin summaries

**Status: unapproved; provisionally actionable solely under the user's
authorized missing-contract instruction.** This selects a candidate standard
encoding for the guarantee in
[field-projected value provenance](reference-field-value-provenance.md#the-bounded-callable-guarantee).
It does not approve that guarantee, amend normative specifications, change
source syntax or establish compiler support. The existing proposal remains
the semantic owner; this document owns only the proposed standard transport,
its necessary declaration-identity closure and its integration rules.
The authorized implementation may explicitly select this candidate now as
provisional direction. Formal adoption and a current-specification
conformance claim remain separate; the old absence of a standard-summary
decision is not an implementation fallback or a reason to infer private bytes.

## Decision and scope

Add one MethodDef-owned singleton **CopyOrigin Contract, kind `0x000D`**,
registered under candidate **Feature `15/1=CopyOriginContracts`**. Reuse
standard global **FieldPath, section `0x004B`**, for nonempty paths; an
explicit nullable path reference selects the whole root value. Preserve each
source as a receiver/formal plus its entry-value view, not a Body operand or
runtime instance.

There is a second concrete representation gap: existing FieldDef
Import/Export requires public fields with public owners. That cannot retain
the exact identity of a private field, implicit backing field or nonpublic
nominal type needed by an independently projected summary. Select the
smallest associated amendment: permit **contract-only TypeDef and FieldDef
exports/imports without changing their declared visibility**, using the
existing ModuleRef/key resolver and unchanged Import/Export rows. This is
metadata identity binding, not public source access.

The recommendation preserves all of the semantic owner's boundaries:

* Only ordinary synchronous Terrum Copy results and fresh inline Copy
  constructor completion have summaries. Raw foreign imports and function
  pointer contract anchors do not.
* A fact guarantees copying one of its listed entry values, including that
  value's applicable facts and independent backing/code obligations. It is
  not equal bits, a field-granular lifetime permission or a mutation summary.
* DependsOn/Captures/ForwardsBacking remain independent whole-source bounds.
  Exact fields cannot subtract permitted sources, release a holder early,
  authorize access/mutation, acquire a root/pin or extend a lifetime.
* Declaration-only consumption never requires callee Body inspection.
  Missing precision is Unavailable, not known empty. Alternatives carry no
  cross-field correlation.

No source annotation, foreign summary, Body instruction/record, runtime tag,
private revision or native container version is added. In particular,
[private candidate 95](reference-field-value-provenance.md#necessary-private-transport-candidate-95)
still composes exactly candidates 93/94 as specified there.

## Allocation check and alternatives

The allocation baseline is
[`trrm/1-draft-layout-queries-1`](../specs/representation/trrm-1.registry.json),
not implementation enums or stale projections:

| Namespace | Baseline and selected candidate |
|---|---|
| Contract kind, `u16` | Assigned `0001..0005`, `000A..000C`; Transfer `0003` is inactive. Select **`000D=CopyOrigin`**. `0006..0009` remain unassigned, including the still-unnumbered static-initialization clauses. |
| Feature identity/revision, two full `u32` words | Native `1..7` and layout `13` are registered; `9..12` have assigned/incomplete registrations. Aggregate outputs provisionally select `8/1` and a bounded `10/1` registration; root/pin provisionally selects `14/1`. Select independent **`15/1`**, not an alias or prerequisite inferred from numeric order. |
| Sections and definition references | Core sections run through `004B`; `7F00` is optional SourceMap and `8000..FFFF` is nonsemantic. Reuse `001B` Contract and `004B` FieldPath. Keep DefId kinds `13/14/16/19/1E/21` and existing Import references; add no section or definition kind. |
| Field paths | FieldPath is `u32 rootTypeSpecRef, u32 fieldCount>0, FieldReference[fieldCount]`. FieldReference is FieldDef DefId `14` or Import token `1C`, not FieldPath token `1D`. Preserve these shapes. CopyOrigin's path fields are typed FieldPath row references, not FieldMap tokens. |
| Body families and operands | Keep codec `01`, tiny/short/long bands, fixed instruction lengths, typed records `01..0B`, maps and source-use domains unchanged. Aggregate `DF`/record `0C` and root/pin `F0`/record `0D` are independent candidates. **Contract `000D` is not Body record `0D`.** |
| Widths | Metadata references remain one-based `u32` with 24-bit rows and reserved-zero high bytes; complete types use TypeSpecRef. Body-local `u16` indexes, including usable `FFFF`, are unrelated. No prefix, escape, widened reference or reserved flag is consumed. |

Registry clauses `000A` EvaluationForm and `000B` PredicateGraph are already
assigned, although portions of the clause/metadata prose still call them
unassigned. Adoption must reconcile those projections, not reuse their
numbers or interpret CopyOrigin as a predicate. TypeSpec forms `01..0F`,
qualifiers and TargetProfile assignments are unchanged.

| Candidate owner | Assessment |
|---|---|
| **MethodDef-owned Contract; selected** | One declaration authority, including bodyless expectations; existing owner ranges, required-section policy and replacement graphs apply. A variable clause holds all prefix-free facts and their alternatives without a second attachment or owner range. |
| Dedicated summary/fact/origin sections | Coherent for a shared general summary program, but requires new directory kinds, owner/child ranges, references and canonical traversal. These small declaration-local sets have no shared program or runtime identity requiring those tables. |
| Standalone Feature-gated declaration record | A Feature advertises support; it does not attach a record to a method. A new section plus owner reference, or a new MethodDef column/range, duplicates Contract attachment and changes more framing. Feature 15 instead gates the selected clause and its closure. |
| Extend Dependency, Transfer or a predicate | Dependency has a fixed 20-byte row and expresses source permissions, not destination-to-entry-value alternatives. Transfer is ownership, and predicates do not encode copying complete ValueFacts. Reassigning an unpublished kind is permissible, but conflating these meanings is unnecessary. |
| Inline relative field ordinals or a new path table | Can be specified, but needs another path/identity/remapping contract, particularly for nonpublic imported members. Reusing exact FieldDef/Import identities and FieldPath avoids a parallel standard path language. Private 95's structural ordinals are not standard references. |
| Body attachment, Attr or optional section | A Body cannot serve a bodyless expectation or survive Body removal as the sole authority. Attr, optional payloads and reserved bits cannot carry required facts. |

Putting CopyOrigin at `0006` beside Dependency, or reallocating inactive
`0003`, would not save bytes or remove a record. Select `000D` beside the
existing declaration-contract family without moving unrelated assignments.
This is a design choice, not a requirement to preserve unpublished IDs.

## Exact candidate clause

All integers are little-endian. The following is proposed metadata notation,
not source syntax or a private-95 payload:

```text
CopyOrigin Contract:
    u32 ownerRow                         // typed MethodDef row, nonzero
    u16 clauseKind = 0x000D
    u16 flags = 0
    u8  summaryKind                      // 1 NormalResult, 2 FreshInlineConstruction
    u8  reserved0 = 0
    u16 reserved1 = 0
    u32 valueTypeSpecRef                 // complete destination root type, nonzero
    u32 factCount                        // >= 1
    repeat factCount:
        u32 destinationPathRef           // typed FieldPath row; 0 selects whole root
        u32 originCount                  // >= 1
        repeat originCount:
            u8  sourceKind               // 1 explicit formal, 2 receiver
            u8  sourceView               // 1 EntryValue, 2 EntryReferentValue
            u16 reserved = 0
            u32 sourceOrdinal
            u32 sourcePathRef            // typed FieldPath row; 0 selects whole root
```

The fixed prefix is **20 bytes**, each fact header **8 bytes**, and each
origin **12 bytes**. Total row extent is exactly
`20 + sum(8 + 12*originCount)`, with no padding or trailing data. One
whole-to-whole, single-origin fact is 40 bytes; two single-origin facts are
60 bytes, excluding separately indexed FieldPath rows. A nonempty FieldPath
row remains exactly `8 + 4*fieldCount` bytes.

An origin has exactly the semantic owner's/private-95 meaning
`(sourceKind, sourceView, sourceOrdinal, sourcePath)`. The standard path
reference transports that path; the reserved word cannot encode another
receiver kind or projection. No additional origin fact or private schema
change is selected.

The clause lives in the existing offset-indexed Contract section `001B`,
directory policy `0000`. Its owner range includes it exactly once.
Occurrence key is `(owner MethodDef, CopyOrigin)`; duplicate clauses reject
even if identical. No TypeDef, PropertyDef, MethodSig, Param or Body owns
one. A property uses its referenced getter's MethodDef clause.
Existing exclusive, contiguous owner-range and orphan/overlap rules apply.
Interpret all references in the MethodDef's owning Module. FieldPath remains
a global structural signature without a Module cursor; sharing requires the
same complete meaning in every using Module/context.

Absence means **Unavailable**. There is no `summaryKind=0`, empty present
summary, empty origin set, explicit independent-origin sentinel or second
body-owned copy. Uncovered dependency-bearing subtrees remain Unavailable.
Publish facts only for provenance-bearing subtrees; dependency-free scalar
children need no fact.

Counts are full `u32` inventories, not row references or private signed
counts. References obey their table's `1..0xFF_FFFF` bound; only the two
path-reference fields admit zero, with the whole-root meaning above.
`valueTypeSpecRef` admits the existing row/F2/F3 forms where valid, never a
Body TypeMap index or pseudo token. `sourceOrdinal` is zero-based in the
MethodSig's explicit Param order, including zero-data parameters; receiver
ordinal is exactly zero and does not denote formal zero.

Preflight the 20-byte prefix, `20*factCount` minimum remaining extent, every
`12*originCount`, offset index `4*(recordCount+1)`, and all complete sums
against the actual containing extents before allocation. Validate FieldPath
indexes, `8 + 4*fieldCount`, references and interpretation depth separately.
Use nonwrapping arithmetic and the selected
[resource scope](../specs/representation/resource-accounting.md); exhausting
capacity declines the operation, not a required check. No private 256-slot
or 64-argument limit becomes a standard limit. Executable Bodies retain
their separate flattened-input and `u16` inventory bounds.

## Roles, roots and entry views

MethodDef flags currently encode visibility, not synchronous/constructor
roles. `summaryKind` selects this guarantee's publication extent and must
match the independently admitted callable role. `valueTypeSpecRef` retains
the complete destination root, including
the constructed owner instance where source has no result declaration.
Neither field chooses a receiver interpretation or supplies missing
receiver authority. Names, `bodyRow=0`, unit results and native ABI do not
establish those contracts.

| Kind | Applicability and root |
|---|---|
| `1 NormalResult` | An ordinary synchronous Terrum callable, not a constructor or deferred completion. `valueTypeSpecRef` equals its complete normalized MethodSig result type, which must be admitted Copy. Facts apply only to normal result publication. |
| `2 FreshInlineConstruction` | A synchronous Terrum instance constructor of the exact complete inline Copy owner instance named by `valueTypeSpecRef`. MethodDef has that nominal owner and its constructor receiver; retain the exact owner substitution and admitted constructor signature. Facts apply to the fresh receiver at ordinary successful constructor completion, before any subsequent caller initializers/final validation. |

Check the kind against the complete selected declaration, including on an
expectation. Conflicting callable roles reject.
It does not recast a constructor as an ordinary result-returning factory,
change its MethodSig/ABI, admit an existing-holder constructor call, or supply
an otherwise missing initialization/receiver-authority/cleanup program.
Those independent execution contracts must also be represented and supported.
An async/foreign/otherwise ineligible producer falsely claiming this role is
invalid; a Body is not a consumer-side repair or mandatory proof.
An authored Terrum provider does not become a raw foreign declaration merely
because it calls native code or has an independently admitted native-callable
role. Its unsafe-caller and ABI obligations remain unchanged.

For each origin, derive its complete source root as follows:

| Source/view | Root and obligation |
|---|---|
| Explicit formal, `EntryValue` | Its already staged by-value Copy value. Root is that formal's complete type under substitution. No caller/callee parameter storage is borrowed. |
| Explicit formal, `EntryReferentValue` | The completely initialized `T` of its admitted ordinary checked `ref<T>` view at Invoke. Root is that exact `T`, not the reference descriptor type. Initialization formals, raw pointees and output cells are ineligible. |
| Receiver, `EntryReferentValue` | Its completely initialized borrowed inline `T` value at Invoke. Derive `T` from the exact selected `MethodSig.receiverTypeSpec`, complete owner substitution and admitted callable/receiver role; the origin does not select a different view. |

Formal sources use ordinary Param mode `00` and an eligible input role.
Initialization/output formals, including NativeOutput, are ineligible in
**both** views even if their physical descriptor/address carrier is Copy.
Receiver plus `EntryValue` rejects. A fresh constructor receiver is never an
entry source. The receiver source/view pair requires the already admitted
borrowed-inline receiver contract; it does not declare or reinterpret that
contract.

The accepted [MethodSig contract](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics)
retains the complete receiver TypeSpec and receiver presence.
[Binding's exact dimensions](../specs/representation/binding-and-replacement.md#2-directed-satisfaction-and-exact-dimensions)
also preserve callable kind, owner, receiver authority and complete
normalized types. Together these determine the receiver value view before
summary instantiation:

* For an inline `T` receiver, root is that exact complete `T`, including
  when the inline value is itself a descriptor.
* For an admitted initialized checked-`ref<T>` receiver view, root is its
  exact `T` referent. Unwrap only that declared receiver reference view,
  not reference-valued contents inside `T`, and preserve all qualifications.
* Validate the selected owner and actual borrowed-inline role. Do not
  follow a handle/object, reinterpret a raw/init reference, or choose a
  different view because a source path would then type-check.

[Descriptor versus referent member lookup](../specs/language/references.md#dereference-and-member-access)
already selects different declarations for descriptor-member access and
access through dereference. The summary consumes that selected target; it
does not repeat lookup or add an alternate interpretation of one exact
signature. A [borrowed receiver's physical pointer](../specs/targets/windows-x64-call-interface.md#3-physical-argument-positions-and-stack)
is subsequent ABI transport, not another origin discriminator.

This derivation requires the complete admitted receiver/type/role contract,
not the receiver TypeSpec alone as a substitute for missing authority
records. The metadata chapter still gates unrepresented receiver-access,
generic and ownership contracts. If those independent facts cannot be
retained and supported, reject affected use; no origin selector, owner-name
guess or callee Body can supply them. A source view grants no borrowing,
access or initialization authority. These are the same prerequisites as
the semantic owner/private 95, not another standard-only guarantee.

The source and destination selected subtrees must have the same complete
admitted Copy type, including every relevant qualification and substitution.
A borrowed referent/receiver root need not itself be Copy: an eligible Copy
field can be selected from a complete unique inline owner under its
independently admitted ownership contract. This never permits a whole-owner
Copy or partial/invalid receiver.
A nonzero path reference names a FieldPath whose complete root agrees with
the derived root above. Zero selects the whole root directly; it is not an
empty FieldPath row, null value, no dependency or independent backing.

Every step resolves an exact instance FieldDef/Import under the immediate
complete containing type. Retain intermediate types, substitutions and
field/layout facts needed for that resolution. Only supported inline
transitions are permitted. Static/computed members, indirection into a
handle's object, arbitrary indices, optional payloads, active Union-case
selection and erased payloads are not added. Implicit property backing must
be an actual retained FieldDef in the established structural order; a
PropertyDef/getter/name cannot substitute for its identity.

Destinations are prefix-free and unique after identity resolution, even if
different physical path rows resolve to the same field sequence. Unsupported
physical overlap rejects; distinct spellings or FieldDefs do not establish
disjointness, alignment or an active Union alternative. Whole-subtree copies
can preserve an existing nested fact tree without inventing field selectors
for otherwise unrepresentable payloads.
Origins are unique by mapped `(sourceKind, sourceOrdinal, sourceView,
resolved source path)` within each fact; identical alternatives do not form
separate cases.

## Contract-only identity closure

The existing public-only Import/Export rules cannot encode all those paths.
Do not silently restrict the semantic guarantee to public fields, equate
private fields by ordinal/name/offset, or hide field references in a Blob.
Adopt this exact eligibility amendment under Feature 15:

1. Keep Export's eight-byte `u32 localDefId, str exportKey` and Import's
   twelve-byte `u32 moduleRef, str exportKey, u32 expectedLocalDefId`.
   Their TypeDef/FieldDef target kinds and selected-identity resolver do not
   change. Export keys remain nonempty, unique per Module and exact decoded
   text identities.
2. Permit a provider-local **TypeDef** or **instance FieldDef** to have a
   contract-only export even when it or its owner is nonpublic. Preserve the
   actual visibility, owner, complete type, mutability, representation and
   other retained contracts. Do not apply this exception to static fields,
   MethodDef, PropertyDef, imported expectations or a new re-export role.
3. An expected TypeDef/FieldDef uses the existing local stub and separate
   owner-TypeDef binding. Field keys must select the exact provider FieldDef
   of that selected owner. Compare its original access and complete
   substituted field/owner/layout contracts, not merely its bits or name.
   A direct FieldDef word naming an expected stub still resolves that
   stub's Import; it cannot bypass this binding.
   A nonpublic nominal field type similarly needs its exact TypeDef binding;
   retaining only a same-shaped consumer-local type is insufficient.
4. A nonpublic contract export/import requires 15/1 in its owning Module,
   even when no local CopyOrigin clause or machine call survives. FieldDef
   Import/Export additionally retains 13/1. Dependency providers may publish
   these identity keys for independently authored summaries without
   themselves defining a summary. No new Export flag or key-name convention
   is needed: original visibility/owner plus the manifest determines this
   newly admitted metadata role.
5. These keys grant no source lookup, access, reflection, storage, field
   operation or marshalling authority. Other features still check their own
   authorization and support. This amendment does not encode file/friend
   principals or expose a private method as callable.

An externally consumable summary must retain/export the local type/field
identities needed to project it into an independent bodyless expectation.
Imported identities retain their actual selected provider/key; a wrapper
does not export its local imported stub. Type/field records needed for
Copy, path, overlap and layout validation cannot be stripped to create an
opaque facade while advertising that same exact summary.

Stable-key assignment remains writer policy, as for existing exports; once
a key is selected by a consumer, preserving its exact selected identity and
contract is a replacement obligation. Merely having a private FieldDef row
does not create such a key. If a dependency supplies no required contract
export/closure, the producer cannot publish or retain that independent
exact-path use until the dependency supplies it. There is no name fallback.

## Feature, schema selection and canonical writing

Register **CopyOriginContracts, Feature `15`, exact revision `1`**, governing
edition `1`, with **no unconditional Feature prerequisites**. Its coherent
scope is the bounded entry-copy guarantee, declaration attachment,
contract-only identity closure, call instantiation, directed binding,
retained replacement obligations, structural checking and transformations
defined here. It adds no value/runtime representation or execution substrate.

Require 15/1 for every retained CopyOrigin clause, expected summary or new
nonpublic contract binding, including uncalled/declaration-only uses.
Conditional closure is explicit:

* Nonempty FieldPath use or FieldDef Import/Export requires existing 13/1,
  including declaration-only fields. A whole-to-whole summary with no such
  retained content does not require 13 merely because it uses CopyOrigin.
* Actual roots/pins, native calls/exports/interfaces, predicates/frozen
  values, generics/witnesses, ownership and target/runtime representations
  retain their own complete requirements. Feature 14/1, native 1..7,
  aggregate 8/1 and predicate 10/1 are not inferred from copying a value.
* Feature support covers declared semantics even if another fact makes a
  normal outcome unreachable. DoesNotReturn, readonly, NoAlias and Pure
  do not justify skipping a required clause or narrowing its broad bounds.

Call the candidate exact draft **`trrm/1-draft-copy-origin-contracts-1`**:
the allocation baseline above plus this entire delta and the semantic
owner's bounded guarantee. Keep format `(1,0)`, Module edition `1`,
the 32-byte header, 48-byte Module row and existing Feature codec.
This is an out-of-band candidate name, not a new on-wire selector or a
claim that the baseline registry has changed.

Per [versioning](../specs/representation/format-versioning.md), exchange
selects one complete artifact schema before decoding. Equal `(1,0)`,
Feature 15, private 95, an outer container or a registry digest cannot
select this decoder or justify trying an older one. Composition with
root/pin, aggregate-output or other provisional standard deltas requires
explicit agreement on their complete combined schema; their records are
not automatically admitted by this proposal. Released schemas remain a
separate unassigned boundary.

Canonical writers:

1. Put the singleton CopyOrigin after the owner's currently assigned
   lower-kind clauses; do not reorder execution-sensitive clauses by
   treating physical Contract order as execution order.
2. Sort facts lexicographically by their **decoded destination path's
   unsigned FieldReference words**, with the whole-root empty sequence
   first. Sort origins by
   `(sourceKind, sourceOrdinal, sourceView, decoded source path words)`.
   Sort keys do not use allocated FieldPath row numbers. This local rule
   does not choose a unique global declaration/import numbering.
3. Intern identical complete FieldPath roots and ordered FieldDef/Import
   identities under the existing rules, keeping distinct Import
   expectations distinct. Emit no empty or unused FieldPath row.
   Even a one-field CopyOrigin path uses a FieldPath row: its typed slot
   has no direct FieldMap-token alternative. FieldMap's own compact
   one-field writer rule is unchanged.
4. Extend FieldPath first-use traversal to visit each Module's MethodDefs
   in row order, visiting a method's canonical CopyOrigin facts before its
   Body: destination path first, then each origin path. Existing ordered
   Body/Block/instruction/FieldMap traversal follows for that method.
   Earlier first use retains precedence when paths are shared.
5. Preserve existing per-Module unsigned Feature-ID order without repeated
   IDs. Do not turn different required revisions into duplicates.

Ordinary readers accept equivalent physical fact/origin/path-row orderings
and equivalent duplicate FieldPath rows with consistently remapped uses.
Duplicate facts/origins, prefix-overlapping destinations, invalid roots,
reserved values and wrong references are malformed, not noncanonical.
Audits report only their actual scope; no unique artifact byte image or
mandatory unrelated-Body scan is introduced.

## Producer truth, call instantiation and broad bounds

The producer checks/derives a summary once from typed authored operations,
then publishing it makes it a declaration promise. Calls compose published
summaries; they do not rescan a callee, infer facts from parameter names, or
use an available implementation as a stronger interface contract.

Stage receiver/arguments once in source order and map their checked logical
formals. EntryValue selects staged value facts. EntryReferentValue selects
the actual initialized referent facts **at Invoke**, after later argument
effects; its reference still identifies current storage rather than frozen
contents. These compiler snapshots insert no runtime read, copy or root.
The producer must truthfully return a copy of that selected entry value
despite any body mutations.

On the specified normal completion, instantiate alternatives against that
invocation's saved entry facts. Preserve unavailable dimensions, independent
capability/code origins, enclosing source bounds and the complete original
destination bounds. Alternatives are unconditional sets, not zipped
cross-field branch cases. This adds no mutation-effect guarantee:
possibly affected current places still update/invalidate normally, including
aliased receivers and ambient state. Returned facts cannot restore an old
caller place after mutation.

Use the existing normalized Dependency clause `0004` independently.
Result DependsOn uses destination `1`, phase `1`; returned-holder Captures
keeps its phase `2`. Constructor Captures uses destination `4`, ordinal
zero, phase `2`, as fixed by the
[carrier proposal's normalization](reference-provenance-carriers.md#source-schema-normalization).
On adoption clarify phase-2 applicability to the fresh constructed holder
as well as existing holders; retain its actual acquired-child obligations
on failed unpublished construction. No new destination/phase or
normal-success-only reinterpretation of Captures is selected.

Every summary source and its actual borrowing/forwarding mode must be
permitted by every applicable normalized whole-source relationship.
Copying an original reference still requires its source-storage permission;
source-field selection cannot turn it into backing-only forwarding.
Explicit empty bounds, source defaults and inherited constraints keep their
meaning. The summary cannot subtract siblings or independent enclosing
bounds when a field is read or replaced.

No normal summary applies on argument failure, exceptional call completion,
constructor failure or a DoesNotReturn edge. Preserve staged/current holders,
mutations surviving failure, acquired-child cleanup and later publication
phases; no rollback, vacancy, revived move or complete-parent cleanup is
inferred. Copying an independently supported root/pin descriptor preserves
its existing capability rather than acquiring another one.

Consumers must check exact schema/framing, clause owner/ranges, counts and
reserved fields, typed references and roots, supported roles/views/Copy types,
field ownership/paths/overlap, source permissions, requirement closure and
selected binding before affected use. This includes retained bodyless and
uncalled requirements. Under
[reader trust](../specs/representation/reader-trust.md), they may then rely
on published facts **without semantic re-proof**. A structurally consumable
but false source-to-field claim violates producer truth; a consumer is not
promised to detect it. Optional body audits remain separate.

## Bodyless binding, replacement and remapping

Resolve the exact Module/export key and complete selected declaration
before comparing summaries. Bind every TypeSpec, field Import and owner
substitution in each declaration's using context. Keep each expected local
MethodDef's own clause and every independent expectation; equal row numbers,
names, layouts or current Bodies do not identify their subjects.

An expected fact is satisfied only when a provider fact covers its
destination at the same or an ancestor path and, after projection, the
provider origin set is a subset of the expected set. Kind, complete types,
mapped source identities, entry views and publication phase must agree.
Project an ancestor copy by appending the selected inline destination suffix
to each copied source subtree through its exact type/field correspondence,
not by concatenating raw row numbers across owners.

Missing/Unavailable cannot satisfy an expected known fact. Finer provider
child facts do not alone prove an expected whole-value copy: independent
whole-value facts/bounds and unrepresented correlations cannot be invented.
Additional precision may be ignored or retained when used; it does not
rewrite a broad interface/generic requirement or any Dependency clause.
Unsupported witness transport remains an independent gate.

For this candidate the existing **Import plus bodyless expected declaration
graph** is the concrete retained replacement edge: a MethodDef expectation,
or a PropertyDef expectation selecting its exact getter MethodDef. Getter
precision uses the existing property/accessor correspondence, not a new
export key for a private method. If extra provider precision needs another
expectation, retain an additional Import/expected graph without rewriting
the original interface/generic promise; all expectations must bind.

Keep that edge whenever its summary was relied upon, including when
optimization removes the call or packing rewrites its MethodMap to a direct
provider. Do not replace it with a provider-only clause, prior acceptance
bit, Body digest or new implicit non-call record. Retain its complete
type/field identity closure and all original whole bounds. A provider with
swapped/lost field origins can satisfy the old broad bound yet fail this
retained expectation.

Transformations must:

* Remap clause-implied MethodDef owners/ranges, complete root TypeSpecs,
  MethodSig/Param and receiver contexts, every typed FieldPath reference,
  each path root and each FieldDef DefId/Import step. Rebind selected keys,
  owner substitutions and nonpublic contracts; clone context-sensitive
  structural rows instead of changing another Module's meaning.
* Preserve logical formal order and source views even when physical Param
  rows move. Resolve private-95 field ordinals to exact declarations before
  constructing standard paths/Imports, and reverse that resolution for an
  explicitly supported private writer. There is no bytewise payload copy,
  header relabeling or guarantee that either codec supports all the other's
  complete types.
  Derive the same receiver root from the remapped complete signature and
  callable role in either codec. Preserve that existing contract rather
  than persisting an extra receiver choice; a conversion unable to retain
  it declines.
* Rebuild standard variable-row offset indexes, Contract and Feature ranges,
  table counts, directory extents and total length. If Bodies also change,
  remap their maps/Home/record/CFG references and code offsets, regenerate
  local ledger identities and preserve/regenerate/drop optional SourceMap
  data under its actual rewrite rules.
* Preserve retained expectations in derived native semantic contracts even
  if no machine import remains. Revalidate/regenerate affected images and
  enclosing private extents/checksums when required by their selected
  contracts; standard Feature 15 changes neither private 95 nor outer
  version 3. This adds no live replacement, hot patch or unload protocol.

## Unavailable and failure boundaries

| Condition | Required behavior |
|---|---|
| Summary absent, a subtree uncovered, or an input fact/correlation unavailable | Keep all established complete types, value states and broad/independent bounds. A use needing only those can proceed; an exact-origin-dependent use cannot. No known-empty substitution. |
| Raw foreign import, fnptr contract anchor or opaque foreign output | No CopyOrigin attachment or inferred summary. NativeOutput retains its separately specified successful inner bound, complement, maybe-live and exceptional state; it is neither a summary destination nor an entry source. |
| Unknown clause/schema, missing 15/1 or conditional requirements, unsupported required type/receiver/runtime | Reject affected use before binding/code generation/execution. An inspector may report known shapes without claiming semantic support; it may not skip required meaning as optional data. |
| Malformed owner/count/reference/type/path/domain or incompatible selected provider | Reject before affected use, including declaration-only requirements. Do not obtain a callee Body to repair it or search a different provider/key. |
| Structurally valid false guarantee | Producer violation; consumers may rely on the declaration without re-proving its Body. Do not promise a mandatory semantic diagnostic. |
| Resource exhaustion or conversion to a schema unable to express retained facts | Explicitly decline that operation/claim. A producer may omit genuinely unrelied-on optional precision, but cannot erase a retained obligation to make conversion or replacement succeed. |

## Required cases

These are acceptance requirements, **not executed tests**. Use unrelated
synthetic types/providers, independent byte producers and public compiler
APIs rather than a motivating sample recognizer.

| Case | Required outcome |
|---|---|
| Whole copy and field selection | Round-trip a 40-byte whole-copy clause and a 60-byte two-fact clause with separately indexed paths. An ancestor copy supplies a required nested child fact through exact type correspondence; copying two independent descriptor fields preserves their separate facts. |
| Borrowed non-Copy root | Select a Copy field from a complete unique inline referent/receiver under its admitted ownership contract. Do not require the borrowed root to be Copy, and do not admit whole-owner copying, a partial receiver or new cleanup/access authority. |
| Private identity closure | Project a source-free public provider summary referring to a private field, implicit backing FieldDef and nonpublic nominal field type. Bind exact contract keys without public lookup/access. Reject a missing key, wrong owner/provider, changed visibility/type or same-layout substitute. |
| Bodyless expectations and aliases | Bind without source or callee Body, with reordered metadata rows, distinct export aliases and multiple consumer expectations. Check all expectations separately; one broad expectation cannot weaken another exact one. |
| Strengthening and replacement | Accept an origin-alternative subset and whole-provider-copy projection to expected children. Reject added origins, lost coverage, swapped fields, changed view/phase or a child-only claim offered as whole-value copying, even when DependsOn still matches. |
| Retained stronger facts | Optimize/inline/remove the machine call, pack a direct reference, then replace the provider. The retained bodyless Import/MethodDef and private field/type closure still govern compatibility; the caller's broad bound remains intact. |
| Staging, names and aliases | Reorder named arguments at the callsite without changing source evaluation. A later argument's mutation precedes EntryReferentValue selection; staged by-value copies remain independent. Two formals can select the same actual region; summary spelling proves no separation. |
| Construction and failure | Distinguish normal result from fresh inline constructor completion. Reject constructor receiver as source and existing-holder reuse. Caller initializers/final validation remain later phases; failing construction/staging/mutation preserves actual holders and cleanup order without publishing a parent early. |
| Broad-bound/access negative | Exact Left=A/Right=B cannot narrow a broad A+B bound, justify early disposal, grant mutation/alignment, treat readonly/NoAlias as Pure, or restore caller contents after a potentially mutating call. |
| Unsupported provenance | Fresh acquisitions, pointer arithmetic, arbitrary indices, active Union member/optional payload paths, erased values and cross-field branch correlation are not encoded as CopyFacts. Whole copies preserve only independently established facts. |
| Malformed framing | Reject truncated prefix/fact/origin/offset indexes, zero counts, products overflowing host arithmetic, extra trailing bytes, unknown kind/view/source tags and nonzero flags/reserved fields before allocation/use. |
| Malformed references and structure | Reject zero/oversized owner/type references, FieldMap tokens in typed path fields, wrong path roots, empty FieldPath rows, wrong-field owners, static/non-inline paths, mismatched Copy types and missing source permissions. Duplicate clauses/facts/origins and resolved prefix/physical overlap reject. |
| Namespace boundaries | Distinguish receiver ordinal zero from formal zero and explicit unit formals. Path rows `1` and `00FF_FFFF` are valid when present; `0100_0000` is not a typed row. Formal `paramCount-1` is in range and `paramCount` is not; with no formals, formal zero is invalid. Zero path means whole, while Body index FFFF remains an ordinary unrelated index. Exercise declared capacity boundaries without private slot limits or confusing metadata validity with Body execution capacity. |
| Exact receiver contract | Derive the inline-value versus checked-referent root from the selected complete MethodSig and callable/receiver role. A reference-valued inline payload is not recursively dereferenced. Reject a missing/incompatible receiver, owner or role; a nonzero reserved word cannot choose another view. Standard and private-95 origins decode to the same four-part semantic tuple. |
| Canonicality and rewrite | Accept valid reordered facts/origins/path rows and duplicate structural path rows ordinarily; canonical writing interns/first-orders paths and sorts lists. Rebuild root/field/import references and extents after module/type/field/method reordering. An audit certifies only its checked scope. |
| Precision absence and foreign roles | An absent/partial summary leaves uncovered origins Unavailable while retaining whole bounds. Reject a CopyOrigin clause on a raw NativeImport or fnptr anchor, and reject output formals in either source view. An opaque successful NativeOutput packet gets no invented field map; complement, maybe-live, pre-Invoke cancellation and exceptional indeterminacy retain their independent rules. |
| Cross-feature combinations | Preserve root/pin capabilities and function-pointer code origins carried by otherwise admitted Copy fields without attaching a summary to fnptr anchors. Require actual 13/14/native/output/predicate prerequisites only for retained uses, and refuse unsupported witness/generic/owner/receiver crossings. |
| Independent admission and truth | Reject absent/conflicting Feature requirements and wrong same-version draft selection, including uncalled declarations. Separately test a well-framed false guarantee as producer/audit evidence, not a mandatory consumer Body proof. Private-95 success and a valid outer digest do not establish standard support. |

## Integration owners, gaps and acceptance gates

On approval, integrate the following together; do not partially allocate the
clause and silently reject the facts the authorized standard route needs.
The source guarantee remains owned by the existing proposal until separately
adopted. This document changes no normative owner or support inventory.

| Owner | Required integration |
|---|---|
| [Registry](../specs/representation/registry.md) and [machine catalog](../specs/representation/trrm-1.registry.json) | Add Contract `000D`, Feature 15/1, exact fields/domains/absence and nonpublic type/instance-field binding eligibility. Preserve all unrelated assignments; reconcile stale A/B projections. |
| [Metadata](../specs/il-metadata.md#semantic-contracts-and-stable-binding), [clauses](../specs/representation/contract-clauses.md) and [logical dependencies](../specs/representation/logical-records.md#general-dependency-contracts-carrier-and-capability-integration-pending) | Add singleton ownership, roles/roots, path-reference zero semantics, contract-only identity closure and independent broad-bound/constructor-Captures applicability. No separate Body copy. |
| [Field identity binding](../specs/representation/binding-and-replacement.md#fielddef-importexport-and-layout-path-obligations) and [binding cases](../specs/conformance/il-binding-and-replacement.md#7-fielddef-imports-and-layout-paths) | Replace public-only TypeDef/instance-FieldDef metadata eligibility with the explicit Feature-15 exception. Revise IBID-07/IBFP-01 accordingly; preserve IBFP-05 visibility/type equality, original source access checks and the ban on private MethodDef/PropertyDef exports. |
| [Binding/replacement](../specs/representation/binding-and-replacement.md), [trust](../specs/representation/reader-trust.md), [canonicality](../specs/representation/encoding-canonicality.md), [manifests](../specs/representation/semantic-manifests.md), [admission](../specs/representation/requirements-and-admission.md) and [versioning](../specs/representation/format-versioning.md) | Integrate directed coverage/subset comparison, retained non-call expectations, typed references, support closure, first-use writer order and exact draft selection. Preserve producer truth and ordinary noncanonical acceptance. |
| [Semantic owner](reference-field-value-provenance.md#binding-transformations-and-consumer-obligations) | After adoption, supersede only its “no standard summary encoding selected” gate with this standard route. Keep its semantics and exact private-95 composition; private ordinals/bytes remain private. |

Current [module support](../implementation/modules.md) and
[CG-001/CG-002](../implementation/conformance-gaps.md#cg-001) distinguish
standard structural/scalar slices from complete standard references/calls.
This proposal supplies a missing contract, not implementation evidence.
Acceptance of the standard slice requires all of these gates:

1. **One agreed candidate schema and catalog.** Select this complete delta,
   the semantic guarantee and any actually required provisional standard
   compositions explicitly. Register coherent capability closure, not just
   a number or an implementation-only clause enum.
2. **Real standard producer/consumer.** Emit/read `001B`/`004B`, complete
   MethodDef/import/type/field contracts, contract-only keys and manifests
   through the explicitly selected standard path. Independently authored
   malformed/boundary vectors must exercise the actual decoder, not a
   development `.trrm` or private-95 round trip.
3. **Shared bound facts and checking.** Derive truthful summaries once,
   restore them on bodyless callables, carry them through shared
   traversal/rewriting and mandatory structural verification, and
   instantiate the same semantic ledger without callsite body-peeking.
   Broad bounds and mutation/failure/cleanup facts remain independent.
4. **Binding, dependency and replacement closure.** Exercise private field/
   type identities, owner/formal substitution, property getters, independent
   provider artifacts, retained stronger expectations, packing and graph/
   native contracts after calls disappear. Unsupported witness/generic
   routes do not become supported by matching a clause.
5. **Source-free execution and lowering.** Consume the final standard
   provider/consumer inputs through admitted ordinary-call and fresh-inline
   construction routes, with correct ABI residences and all required
   type/receiver, initialization, failure/cleanup and runtime carriers.
   General constructor/ownership/root representations missing elsewhere
   remain explicit prerequisites, not fields invented by this codec.
   Binding or successful image emission alone does not pass execution.
6. **Final-input evidence.** Cover positive, negative, malformed, boundary,
   replacement/remapping and cross-feature cases through public APIs and
   supervised headless execution where relevant. Identify final compiler,
   patch/configuration and selected input schemas; private/native successes
   do not substitute for standard-path evidence.

There is no undecided field, numeric assignment or strengthening rule inside
this bounded recommendation. Formal approval, coordinated normative
integration and implementation/qualification remain outstanding. Broader
mutation summaries, field-granular lifetime bounds, foreign/NativeOutput
summaries, general constructor/runtime codecs and historical compatibility
are not selected by it.
