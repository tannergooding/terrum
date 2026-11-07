# Private revision-105 enum bound identities

**Status: unapproved candidate under the authorized provisional
missing-contract workflow.** This is an investigation and host-internal
carrier recommendation, not authorization to implement it in this change,
normative approval or a stabilized host API.

## Finding, scope and owners

The existing proposals specify the logical reconstruction target, but **not
a sound reconstruction into the current Core-only bound identity fields**.
The [enum owner](nominal-enum-persistence.md#logical-identities-and-binding)
defines a member as `(selected nominal T, producer member key)`. The
[host-input amendment](private-105-nominal-origin-inputs.md#adapter-tokens-versus-persisted-member-identity)
explicitly distinguishes those keys from Core adapter tokens and prohibits
fabricating tokens. Its requirement for a capable bound carrier leaves that
carrier's representation and source-binding bridge unspecified.

For example, changing only a real member's in-process
`CoreDeclarationIdentity.ExportKey`, while retaining its private owner
Binding, member key/name/ordinal/value, changes no enum directory field.
Those bytes cannot determine which actual adapter token was used. A selected
provider snapshot can supply its real tokens; requiring that snapshot merely
to decode every enum would defeat the specified provider-scoped,
source-free reference projection, and ordinary private enums need not have
a Core registration at all.

Recommend dedicated **disjoint Core-backed/private declaration and member
carriers**, with a separately checked bridge where real Core identities
exist. This completes the host representation requirement; it does not
replace the recently amended key-input design.

| Owner | Preserved contract |
|---|---|
| [Nominal enum persistence](nominal-enum-persistence.md#private-candidate) | Complete public, module-level, nongeneric primary int32 enum snapshots, per-owner member keys, exact replacement and typed values. |
| [Private 105 B+C+R](checked-reference-private-persistence.md#exact-schema-composition) | Exact `terrum/private-semantic-105-checked-references-1`, reconciled directory order, type closure and rewriting. |
| [Nominal-origin inputs](private-105-nominal-origin-inputs.md) | Explicit Module/type/proxy/export assignments, separate complete member assignments for Core-backed authoring, retained original expectations and provider-scoped reference timing. |
| [Binding/replacement](../specs/representation/binding-and-replacement.md), [reader trust](../specs/representation/reader-trust.md), [persistence](../specs/representation/persistence.md) | Resolve selected declarations before comparison; retain every original expectation; distinguish structural consumption, producer truth, provider authentication and execution admission. |

No accepted text is replaced. Amend only the proposal-level bound carrier
and its projection rules. Preserve all bytes: Binding/origin/enum records,
`356+i`, `enum#i`, four-byte enum values, R's eight-byte reference rows and
outer 3 / inner 105 / B+C+R. There is no new schema selection, revision,
member export, source grammar, Core identity meaning or class runtime
support. Standard enum codecs, source EnumDecl collection, attribute-schema
publication and additional evaluator/native capabilities retain their
independent gates.

## Inspected host boundary

The carrier recommendation below remains unapproved. The compiler now has a
provisional private-identity implementation; current support evidence and
remaining route gaps are tracked in the implementation inventory.

| Source | Consequence |
|---|---|
| [`BoundEnumTypes.cs`](../../sources/Terrum.CodeAnalysis/Semantics/BoundEnumTypes.cs) | The internal carrier now distinguishes Core tokens from private occurrence/member identities. Private occurrence records retain the complete expected snapshot and owner-scoped member keys; table validation preserves the distinct Core token rules. |
| [`BoundProgram`](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs), [`CompilationReference`](../../sources/Terrum.CodeAnalysis/Semantics/CompilationReference.cs) | Both retain enum/origin collections. `SelectCoreTaskType` now forwards private enum origins and related reference snapshots; a focused regression covers this selection path. |
| [`Binder`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.cs) | Ordinary enum lookup and typed values support private occurrences. `TryBindCoreEnumMember` still requires an actual Core identity; the optional checked Core/private bridge remains unimplemented and cannot be inferred from equal-looking tokens or keys. |
| [`ModuleCompiler`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.cs), [`reader/writer state`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.TokenSpans.cs), [`105 reader`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.CheckedReferences.cs) | The selected private schema now reads/writes private enum rows, projects exact provider expectations and remaps enum types through references, sets and graphs. An uncalled DirectNativeImport `NativeParameter` pointee round-trips and rejects a changed selected provider snapshot. Broader source-free execution, CLI and full cross-feature closure remain incomplete. |
| [`Nominal origins`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.PrivateNominalOrigins.cs), [`identity inputs`](../../sources/Terrum.CodeAnalysis/Modules/PrivateModuleIdentityInputs.cs) | Explicit imports may reuse a retained private enum occurrence without Core member assignments; selection must match the original Module/export, and producer projection records an imported occurrence. `PrivateEnumMemberAssignment` remains the Core-backed source adapter and does not fabricate Core tokens for decoded private enums. |

Removing those refusals or adding an enum collection alone would not resolve
the identity mismatch.

## Concrete internal carrier recommendation

Use the following closed alternatives; these are proposed internal record
shapes, not public C# API signatures, Terrum syntax or serialized records:

```text
BoundEnumDeclarationIdentity =
    Core(actual CoreDeclarationIdentity of kind EnumType)
  | Private(PrivateEnumOccurrence)

BoundEnumMemberIdentity =
    Core(actual CoreDeclarationIdentity of kind EnumMember)
  | Private(PrivateEnumOccurrence owner, memberDeclarationKey)
```

Use these types in the internal bound enum identity positions:
`BoundEnumDefinition.Identity`, `BoundEnumMemberDefinition.Identity`, and
the latter's `DeclaringEnum` (a `BoundEnumDeclarationIdentity`).
Keep names, values, declaration order, flags, access/primary facts,
Blittable commitment and `DeclarationKey` as their existing separate facts.

A `PrivateEnumOccurrence` is an immutable reference to **one original enum
occurrence in one retained declaration snapshot**. It contains:

* the original enum Binding, including owned/imported kind, using Module,
  local declaration key, export status and selected Module/type export;
* the complete original enum contract and ordered member key/name/value
  snapshot, with ordinals given by list position;
* association with that originating snapshot and its checked local type map.

Any retained `ModuleImport` must agree with its selected provider/export and
requirements; it is not another origin authority. A missing import marker
does not establish positive local ownership.

Reuse the retained nominal-origin graph, not another host-assigned key or
second binding relation. The existing retained enum directory owns the
contract payload once; the identity references it rather than introducing a
second mutable declaration authority. A snapshot handle distinguishes original
expectations during merging; it is provenance, **not nominal identity**,
authentication, an extra wire field or a requirement for byte-identical
replacement. A second read does not create another nominal T. Physical enum
indices and `PrimitiveType` numbers belong to remap tables, not identity.

For a private row, `DeclaringEnum` must name exactly its enclosing occurrence,
and its member identity must name that same occurrence and exactly its
`DeclarationKey`. Enforce uniqueness per owner, not globally across private
member keys. The ordinal, lookup name and value are checked contract facts,
never identity substitutes. Equal-valued members remain distinct; different
enums may reuse the same member key. Use the existing Binding/key domains,
including ordinal text comparison and the member-key NUL prohibition;
do not apply Core token validity rules or impose a flattened key capacity.

For the Core alternative, `FromCore` wraps only actual checked tokens and
preserves the current Core owner/provider/kind and uniqueness requirements.
`CoreDeclarationIdentity`, `CoreEnumDefinition`, their public consumers and
Core registration semantics remain unchanged. No sentinel/default Core ID,
nullable-ID fallback, concatenation, hash, name, ordinal or private row number
can stand in for the private alternative.

### Equality and the optional Core bridge

Core carrier equality compares actual Core tokens; private carrier equality
compares retained occurrence handles, not recursive contract-graph values.
Neither replaces contract comparison or the cross-artifact type-equality
algorithm. Within an occurrence, all its typed uses share its owner. Across
occurrences, use the existing checked nominal binding map:

* An owned occurrence anchors T at its actual Module/local declaration key
  and enum kind. An import's local pair identifies its expected proxy only;
  its selected Module/type export must resolve to an actual owned enum.
* Resolve the one unambiguous selected provider snapshot, then compare every
  original complete expectation before semantic interning. Same target text,
  equal directory indices or equal payloads cannot discharge those checks.
* After successful binding, private member semantic identity is
  `(resolved T, memberDeclarationKey)`. Keep original occurrences even when
  their value-type uses share a resolved T.
* A Core/private pair can correspond only through an explicit checked
  adapter map. Direct equality of their token strings is neither that map
  nor permission to register a Core role.

Construct a bridge only from an independently host-selected, checked provider
snapshot containing the real Core enum/member declarations and agreeing
Module/schema/revision/export provenance. Match the exact enum owner first,
then its actual members by explicit declaration key, validating the complete
name/ordinal/value and declaration facts. Attach **the provider's real Core
tokens**; never derive them from those keys. Keep the original expectation
and selected actual snapshot separate. A replacement's bridge cannot replace
the original snapshot's adapter association.

Two different Core tokens do not become equal because they map to the same
private owner/key pair. Any retained Core identity/registration expectation
still requires its own exact Core checks. A compatible private replacement
may therefore satisfy a private-only use but fail a Core-specific one; the
bridge is not a Core alias or replacement rule.

Core role authorization still requires the separate explicit native/Layout
registration. Byte decoding and ordinary private enum source lookup neither
create such registration nor reacquire Core merely to recover identities.

## Reader, reference, source and consumer rules

1. **Decode without Core.** Under the already selected exact 105 schema,
   validate the complete enum/origin universe and construct private
   occurrences/member identities from its checked records before typed values
   or affected declarations use them. Retain all original member facts.
   An external provider may remain a requirement in a structural projection;
   this is not successful provider resolution, native admission or execution.
   The reader does not need the producer's original Core adapter.
2. **Project provider-scoped references.** `CreateReference(byte[])` and
   `CreateReference(Module)` retain owned exported enum roots, their complete
   contract closure and inherited external expectations. They do not turn
   definitions into imports for an invented consumer Module, generate proxy
   or member keys, or acquire a new Core selection. Existing artifact/code
   cardinality rules remain; this adds no type-only package shape.
3. **Bind ordinary source using the carrier.** `Binder` imports and remaps
   the complete enum/origin/member graph, not just its integer type selectors.
   Ordinary lookup selects a name within its selected enum; typed constants,
   arguments, results and supported evaluation preserve that owner. Use
   checked resolved identities when unifying declarations, not direct
   cross-alternative equality or lookup spelling. Keep same-named unrelated
   owners distinct; unsupported/ambiguous lookup must not collapse them.
   `TryBindCoreEnumMember` may construct a Core descriptor only from the
   actual Core alternative or its checked original-snapshot bridge and the
   selected registration. A private-only enum remains valid for ordinary
   supported use but cannot impersonate a registered Core enum.
4. **Form a new consumer occurrence at the explicit-input boundary.**
   Preserve provider provenance during source binding. The supplied using
   Module/proxy assignment establishes the new import occurrence and captures
   its original complete expectation; inherited using contexts remain intact.
   Select the real public type export, never a facade's invented re-export.
   Member keys remain the producer's per-owner keys.
5. **Resolve before claiming satisfied binding.** Select exactly the
   requested Module/export/kind and one actual owned exported declaration.
   Check its complete enum snapshot exactly under the enum owner, including
   all members whether called or not. Map each expected member key to exactly
   one actual member of that T; compare count, name, ordinal, value, base,
   public/primary/Flags, lookup name and retained Blittable commitment.
   Do not search another enum/provider or replace an expectation on mismatch.
   Sets/diamonds preserve and check every independent original expectation;
   an intermediate imported proxy is not an owned provider definition.

No selected-provider parameter is added to the context-free byte reader.
Existing single-provider, set and graph operations use their explicitly
supplied selected snapshots and retain their topology. An operation that
needs a provider closure it cannot supply must refuse, not discover a
provider, erase a requirement or silently switch topology.

The public `PrivateEnumMemberAssignment` remains the **Core-backed source
adapter input** from the nominal-input owner. Its token comparisons unwrap
the actual Core alternative or consult a checked bridge for that exact
original source snapshot. It is not a universal bound identity type.
Decoded private occurrences already have a complete compiler-retained
logical member map; consumer/re-emission inputs may reuse that map without
fresh member assignments, as the existing input contract permits.
An explicit Core-only assertion without a real bridge rejects rather than
minting one. This selects no new public authoring API for private-only enums.

Malformed records use `ModuleFormatException`; unavailable required
bindings, unsupported closure or contract mismatch use
`ModuleContractException`; malformed explicit host inputs retain argument
failures. Structural checks do not semantically re-prove initializer
programs or arbitrary producer Bodies. Publish no partially admitted
reference as successfully bound.

## Propagation, lowering and re-emission

Retain the carriers, complete original occurrence snapshots, optional checked
adapter maps and separate selected-actual maps together in `BoundProgram`,
`CompilationReference`, decoded `Module`/reader state and consumer/provider/
set/graph snapshots. Clone/select helpers, including unrelated Core-role
selection, must forward them. A compiler transformation explicitly rebases
their type/owner/member associations; host table replacement or a stale
program association is not a checked rebase.

Re-emission needs the retained logical graph, **not the original Core
registration or token spelling**. Emit an owned enum's Binding and complete
snapshot, or an import's original Binding and expected snapshot; do not emit
replacement facts over that expectation. Rebuild context-sensitive enum,
origin, pointer/signature/array/reference and member-owner maps, every binary
enum type use and parsed `enum#i` atom, and affected typed-place/annotation/
bodyless roots. Clone divergent physical closures instead of merging by
name, token text or index. Retain all original contexts even after physical
imports disappear.

Preserve member keys and declaration order; reordering physical enum/origin
tables is not reordering members. Four-byte enum values contain no references;
ordinary strings/source data are not type atoms. Re-encode counts, Strings,
references and affected outer lengths/hashes in the original importing
Contract's type universe. Apply the existing shared budgets without creating
a fresh budget per occurrence, bridge or member list.

Lower an admitted ordinary enum to its int32 carrier only after exact typing,
binding and the applicable representation/call checks. No Core token is
needed for a copy. Preserve reference/initialization/unsafe/ownership and
native dependency obligations independently. Normalized native facts do not
retain an otherwise unused raw enum/Core dependency. Further ConstEval use
still needs its actual eligible program/forms/dependencies, not a reference
stub. No new class, native ABI, AOT/JIT or runtime support follows.

## Required acceptance cases

These are future public-API tests with independently authored 105 byte
fixtures and real checked host adapters, **not tests run by this change**.
Do not inject internal enum rows through a friend assembly to bypass the
reader or provider-selection boundary.

| Case | Required observation |
|---|---|
| Source-free owned enum | Read/project/bind supported ordinary uses and re-emit with no original Core registration or selected Core provider. Preserve arbitrary type/member keys unrelated to names, ordinal and value, zero-member enums, duplicate-valued members, unnamed values, Flags and Blittable 0/1. No Core token is synthesized. |
| Private identity domains | Two unrelated enums reuse a member key, including text equal to an ordinary/type export or a real Core token, without becoming one declaration or a Core role. Exercise legal 256-byte independent keys, ordinally distinct Unicode, and nonempty whitespace keys where the key grammar permits them; no concatenation or Core-string validator narrows the domain. |
| Core bridge | Round-trip a genuine Core-backed enum whose actual member token differs from its member key. Recover an optional bridge only from the explicitly selected checked original/provider snapshots. Equal-looking keys/names/values from another provider, wrong enum/member kind, wrong owner, missing member or mere token-text agreement cannot authorize a bridge or registration. A replacement with the same private snapshot but changed Core tokens cannot satisfy a retained exact Core expectation. Existing Core token uniqueness remains checked. |
| Missing or incompatible provider | Structural projection retains unresolved external requirements. Binding/native admission fails without the selected closure, with two conflicting selected revisions, wrong kind, an imported/unexported target, or any changed/added/removed/reordered/rekeyed member. Correct replacement with physical row movement succeeds; an uncalled mismatch and the stronger independent diamond expectation still fail. |
| Projection and new consumer | A owns T; provider-scoped projection invents no B identity. Explicit B/proxy inputs capture the full original snapshot, preserve producer member keys and need no fake Core assignment for a decoded private enum. Reference sets/diamonds retain intermediate/leaf contexts regardless of order. An ambiguous same-name source lookup cannot unify distinct owners. |
| Association/retention negatives | Reject wrong private member owner/key, duplicate per-owner keys, stale snapshot/type maps and conflicting explicit Core assignments. Preserve the map through Function erasure, cloning, unrelated Core selection and re-emission without adapter input. Re-reading identical bytes is not a new nominal type; divergent original expectations are not silently deduplicated. |
| Cross-feature | Raw enums inside nested checked references/optionals, pointer/fnptr contracts, arrays, fields, typed places and bodyless/uncalled native expectations retain exact T and every original occurrence. Equal int32 bits or another enum fail exact-type checks. Missing independently required C/R/evaluator support still refuses. Normalized-only native contracts need no fabricated raw enum/Core dependency. |
| Re-encoding and bounds | Reorder enum/origin and complete-type tables; rebuild codes/atoms/owner maps and inner/outer lengths/hashes while leaving same-looking ordinary text and value bits untouched. Exercise inherited legal/one-over key, enum-count and shared byte/node/depth limits, including cumulative occurrences and bridges. No extra wire field, changed reference-row extent or schema/header inference appears. |

## Alternatives and remaining host decisions

Requiring a selected Core provider for every read would block ordinary
private-only declarations and context-free projection. Forged Core tokens
contradict the existing identity contract. Adding only nullable Core fields
would remove a constructor requirement without supplying private identity or
comparison rules. A separate raw Module enum table alone would move the
problem to its projection into Binder. A universal nominal declaration IR
could generalize more families, but is unnecessary for this bounded gap.
The recommended disjoint internal carriers reuse the current enum table and
nominal occurrence graph without redefining public Core identities.

Remaining unapproved host decisions:

1. Adopt this concrete internal carrier/bridge and propagation direction,
   including its distinction between projected requirements and resolved
   binding. This document approves no API or implementation.
2. Supply actual selected provider snapshots, exact schema/revision/export
   choices and authentication for each embedding operation that needs
   resolution; independently select any desired Core role and its real
   adapter. No production provider/default or key-generation convention is
   selected here.
3. If an embedding needs provider closure or private-only authoring/CLI
   inputs not deliverable through existing host surfaces, approve that
   explicit public delivery contract separately. Do not make it an implicit
   reader dependency or extend the recently completed key-input API by guess.

The carrier, owner/key comparison, projection timing and re-emission meaning
are specified within this candidate, not left as implementation policy.
Actual implementation and qualification remain outstanding. This document
changes no compiler code, tests, accepted specification or wire bytes and
establishes no broader conformance milestone.
