# Source nominal enum identity inputs

**Status: unapproved candidate; provisional direction only under the user's
explicit missing-contract workflow. All recommendations below remain
unapproved. This document changes no accepted source rule, public API,
schema allocation or compiler support claim.**

## Missing decision and owners

The missing contract is how an actual source enum declaration and its members
are identified **before** private Module identities and producer member keys
are supplied. Accepted enum syntax alone supplies neither those inputs nor
Core tokens. Recommend a **source-local bound identity**, followed by an
explicit, snapshot-checked producer projection through
`PrivateModuleIdentityInputs` into the existing private occurrence carrier.

| Evidence | Boundary |
|---|---|
| [Grammar §3.1](../specs/grammar-spec.md#31-source-file-structure-and-declarations) and its enum semantic constraints | Namespace/file-level `EnumDecl` is accepted. The primary explicitly supplies its eligible integral base; a secondary inherits it and must not repeat it. Members inherit visibility, have exact-underlying-type constants, permit duplicate values and preserve unnamed values; implicit successor overflow rejects. |
| [`SyntaxKind`](../../sources/Terrum.CodeAnalysis/Syntax/SyntaxKind.cs), [`Parser`](../../sources/Terrum.CodeAnalysis/Syntax/Parser.cs), [implementation boundary](../implementation.md) | There is no `EnumDeclaration` node; the parser rejects that declaration outside syntax-47. Source enum collection and a production provider adapter remain unsupported. |
| [`BoundEnumTypes`](../../sources/Terrum.CodeAnalysis/Semantics/BoundEnumTypes.cs), [`Binder`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.cs), [`identity inputs`](../../sources/Terrum.CodeAnalysis/Modules/PrivateModuleIdentityInputs.cs) | Core/private identities, private occurrence snapshots, `CreatePrivate` and ordinary member lookup exist. Type-origin inputs and actual-Core `PrivateEnumMemberAssignment` do not provide an unmapped source declaration/member carrier or source-member-key input. |

Preserve [names and binding](../specs/language/names-and-binding.md),
[complete types](../specs/language/types-and-normalization.md),
[source-unit identity](../specs/language/source-input.md) and
[constant value identity](../specs/language/constant-generics.md#canonical-value-identity).
No accepted text is replaced.

This fills the source-collection identity boundary left separately gated by
[nominal enum persistence](nominal-enum-persistence.md#logical-identities-and-binding),
[private bound identities](private-105-enum-bound-identity.md) and
[nominal-origin inputs](private-105-nominal-origin-inputs.md).
Those owners retain the logical `(T, producer member key)` identity, original
expectations and independently selected Standard/private carriers.
The persistence candidate's complete, public, module-level, nongeneric,
primary int32 domain is **a publication bound**, not accepted source semantics.
Production [Core registration](native-calling-convention-core-registration.md)
and [provider configuration](native-core-provider-configuration.md) remain
separate decisions and implementation gates.

## Alternatives and recommendation

| Unapproved alternative | Consequence |
|---|---|
| Minimal explicit post-bind source mapping directly to private identities | Collect a source declaration/member inventory, then require Module/type/member assignments before constructing usable Core/private bound enum identities. This avoids a third identity alternative, but either makes ordinary checked binding depend on private publication inputs or leaves a second, incomplete binding-result stage. Fabricating a private origin to bypass that stage is not valid. |
| **Source-local identity, later explicitly mapped — recommended** | Ordinary source binding has actual nominal declarations without publication configuration. Add one disjoint internal alternative and preserve it through normal bound routes; project only at the identity-input boundary. The extra propagation work avoids making a private carrier's scope or key availability a source-language restriction. |

This is not a universal nominal-IR redesign, a source key-generation scheme
or a new cross-Module binding relation.

## Recommended source carrier — unapproved

The following are internal shape candidates, not Terrum syntax, serialized
records or approved public host types:

```text
SourceEnumDeclarationHandle = opaque checked composed-source declaration
SourceEnumMemberHandle      = opaque checked member with its declaration owner

BoundEnumDeclarationIdentity += Source(SourceEnumDeclarationHandle)
BoundEnumMemberIdentity      += Source(SourceEnumMemberHandle)
```

* Only checked source binding creates these handles. A declaration handle
  retains its defining compilation/source snapshot and positive source
  ownership. Checked partial parts in one Module share one composed declaration
  and exactly one primary authority; a secondary is not another nominal enum.
  Referenced declarations retain their defining owner.
* A member handle belongs to exactly that declaration and actual authored
  member. Snapshot association and owner checks establish handle validity;
  equality is declaration-object identity within that snapshot, not structural
  equality. An index may locate it but is not durable identity. Recompilation
  does not make a stale handle valid merely because names or indices agree.
* The checked source snapshot retains effective namespace/name, access,
  primary/composition facts, exact eligible underlying integer type, complete
  ordered members and their exact integer constants. Member order follows
  the accepted compiler-file-order/source-order composition rule, not key
  order or primary-first ordering. Do not narrow source constants through
  today's int32 member field or apply today's public/int32-only table
  predicate as the source grammar.
* A source member has **no producer declaration key until projection**.
  Represent that absence explicitly for the Source alternative, not as a
  valid empty key. Private and existing Core-backed key requirements remain.
  Preserve actual checked Flags/Blittable facts when present; annotation
  spelling or integer width supplies neither their schema nor Core authority.
* Lookup selects the enum under ordinary namespace, lexical import and access
  rules, then its declared member by name. Duplicate names reject; duplicate
  values do not merge member handles. Ordinary values remain
  `(complete T, exact underlying integer value)`: equal-valued members yield
  equal values of T, and copies retain no member-declaration provenance.

Source, Core and Private alternatives are disjoint. A source declaration
cannot become Core-backed through a default token, private key, lookup name or
equal payload. Source/private correspondence requires the explicit projection
map below; any actual Core bridge still follows the existing checked-bridge
owner and separately selected registration.

Source units remain semantic access/identity inputs where accepted `file`
rules require them. Equal filenames, paths or text do not merge units, including
generated inputs. Public/module-level enum identity does not depend on a
debug path, source span or optional SourceFile row. No Module/type/member key
is inferred from a path, source-unit discriminator, name, value, ordinal or
hash. This candidate adds no source-unit wire field: file-local publication
needs an independently admitted carrier and must not hide required identity
in optional debug data.

## Recommended private input and projection — unapproved

Reuse `PrivateNominalDefinition(Type, LocalDeclarationKey, IsExported)` for
locally owned source enums and `PrivateNominalImport` for already mapped
provider expectations. Add a separate source-member input alongside, **not
in place of**, the existing actual-Core member collection:

```text
PrivateSourceEnumMemberAssignment(
    PrimitiveType EnumType,
    SourceEnumMemberHandle Member,
    string MemberDeclarationKey)

PrivateModuleIdentityInputs.SourceEnumMembers
PrivateModuleIdentityInputs.Create(
    program, usingModuleIdentity, definitions, imports,
    enumMembers, sourceEnumMembers)
```

These are candidate public input shapes; they do not amend an existing
signature or compilation configuration:

```text
PrivateSourceEnumDefinitionAssignment(
    string QualifiedName,
    string LocalDeclarationKey,
    bool IsExported,
    ImmutableArray<string> MemberDeclarationKeys)

PrivateModuleIdentityInputs.Create(
    program, usingModuleIdentity, definitions, imports,
    enumMembers, sourceEnumDefinitions)
```

`QualifiedName` is only a selector within the **exact supplied BoundProgram**:
it must match exactly one public source EnumDecl. It is not the declaration's
identity or a persisted key. Aliases, unqualified names, provider names,
source paths and file-local declarations do not select a definition. Ambiguous
matches reject. Requiring a public namespace-level enum matches the bounded
private persistence candidate; internal/file enum source semantics remain
valid independently but this candidate does not publish them.

The supplied member-key array is complete and ordered like the composed source
members. Each key is explicit caller input; the array position selects a member
in this one checked snapshot but never generates or defines its key. The source
carrier retains its own member handles, and projection associates each handle
with the corresponding explicit key. Reject null/default arrays,
non-enum selectors, wrong identity alternatives, mismatched counts, and
duplicate type/member keys. Core assignments cannot substitute for Source
assignments or conversely. No key is auto-generated; the producer's host
persists and resupplies the keys it selected.

1. **Ownership:** an owned assignment requires positively local authored
   source provenance in the defining compilation represented by the program.
   The explicit using Module/key/export input establishes its publication
   Binding; namespace lookup and absence of `ModuleImport` do not establish
   ownership. An external source handle cannot be restamped local.
2. **Producer keys:** the declaration's actual producer explicitly supplies
   the complete member map. Reuse the nominal-input owner's Module/type-key
   domains and collision rules. Member keys are nonempty strict UTF-8 text,
   at most 256 bytes, without zero scalars, compared ordinally without
   normalization or case folding. They are unique **within T**, not across
   enums, and are not independent Module exports. Text equal to a type key,
   another enum's key or a real Core token creates no correspondence.
3. **Totality:** every source-member group joins exactly one supplied source
   type assignment or matching retained source projection; orphan groups reject.
   Require one mapping for every member, including members unused by Bodies.
   An empty enum needs its type assignment and complete snapshot but no member
   rows. Reject missing/extra members, foreign owners, duplicate handles/ordinals,
   duplicate per-owner keys and repeated type assignments, even identical
   repetitions. Input order does not change declaration order. No sparse
   overlay or first/last-wins rule is selected.
4. **Missing input:** ordinary source analysis needs no publication map.
   A newly retained enum export/type/value closure cannot be privately
   published without its complete origin and member map. Existing overloads
   may forward a complete checked retained map, but an empty collection
   never requests inference. An explicit group asserting retained data must
   be complete and agree exactly; it cannot rekey an existing occurrence.
5. **Imports:** retain producer keys and the entire original expectation.
   New consumer inputs supply only the consumer Module/proxy selection.
   An unmapped external Source declaration needs its producer's checked
   projection before private publication; the consumer cannot mint its keys.
   Existing `PrivateEnumMemberAssignment` remains actual-Core-only, while
   decoded private occurrences reuse their checked retained maps.
6. **Capture:** validate all submitted assignments, including later-pruned
   ones; copy collections and complete source associations immutably.
   Preserve the input's exact program association. Transformations must
   explicitly rebase type/member maps; host-modified programs or stale
   handles reject rather than mutating the original snapshot.

Malformed explicit arguments use existing argument failures. Incomplete
explicit member groups are invalid input; missing retained closure,
unavailable provider or incompatible/unsupported publication uses
`ModuleContractException`. Malformed selected bytes use
`ModuleFormatException`. Source formation failures remain source diagnostics.
Publish no partially admitted projection, reference or artifact as success.

After validating carrier eligibility and the complete map, construct a
separate projected private occurrence using `BoundEnumDefinition.CreatePrivate`
and the existing nominal-origin Binding. Preserve the source-to-projected
type/member correspondence without altering the source program.
An owned exported enum publishes the same supplied local declaration key
under the existing private candidate; public visibility alone does not export
it. Project every ordered member key/name/value and all retained declaration
facts, not only observed members.

Use the enum owner's private bytes under the
[exact 105 composition](checked-reference-private-persistence.md#exact-schema-composition);
this adds no field, revision, type code, member token or schema selection.
Non-int32 bases, unsupported scopes/facts and missing independent routes
decline publication without redefining valid source formation.
A future Standard producer may consume the same checked logical producer map
through its own adapter and exact schema; this decision neither implements
that route nor makes private records Standard TRRM.

## Providers, consumers and replacement — unapproved application

Provider-scoped reference projection retains the owned export, complete
original enum snapshot and inherited external requirements before Function
erasure. It invents no consumer Module/proxy or member keys.
Explicit consumer inputs then capture the new using context without replacing
any original Module/export expectation.

Apply [binding and replacement](../specs/representation/binding-and-replacement.md):
resolve the exact selected Module/export/kind and one unambiguous actual owned
provider snapshot before comparing contracts. Resolve members only within
that T by their producer keys, then check the complete original
key/name/ordinal/value list, base, namespace/name, access/primary/Flags and
retained representation facts under the enum owner. Preserve every independent
expectation through sets/diamonds and every stronger fact relied on.
Never replace a consumer's originals with the replacement's own expectations.

The bounded complete-snapshot rule rejects changed, added, removed, reordered
or rekeyed members even if old integer values remain valid. Changing the
Module/type key publishes a different identity, not an automatic migration.
Physical table movement or relocating a public declaration between source
files can succeed when the selected identity and retained contract remain
unchanged; source handles themselves are not replacement keys.
Frozen values are not recomputed. Source-free decoding/re-emission needs
retained logical identities, not original source files or Core tokens.

[Reader trust](../specs/representation/reader-trust.md) still separates truthful
producer facts from mandatory structural, bounds, reference, support and
binding checks. Consumers need not rerun source member initializers or
semantically re-prove producer Bodies. Authentication, source compilation,
structural projection and successful execution remain distinct.
[Persistence separation](../specs/representation/persistence.md) and
[exact-schema agreement](../specs/representation/format-versioning.md) remain
unchanged.

## Required integration and acceptance

These are future gates, not implemented or newly run tests.

| Route | Required integration |
|---|---|
| Syntax and source binding | Lossless EnumDecl/member nodes and recovery; namespace/file/partial collection; sole-primary base and inherited visibility checks; exact constant formation; ordinary lookup records actual selected source identities. |
| Shared semantics | Source-valid enum/member snapshots and mandatory verification; exact types and constants through values, aliases, calls, joins, generic substitution and supported evaluation. No enum-to-integer identity erasure or Core role inferred from names. |
| Retention and rewriting | `BoundProgram`, `CompilationReference`, clone/select helpers, shared traversal/rewriting and all nested type positions retain source provenance, checked projection maps and original expectations. Rebase owners and types together. |
| Private publication/consumption | Identity inputs, producer/consumer/reference/set/graph and native wrappers share the same checked projection and complete closure. Remap binary enum codes and grammatical `enum#i` atoms, not ordinary strings; re-emission preserves original contexts and rebuilds affected framing. |
| Independent adapters and lowering | Standard production, attribute schemas and production Core registration have their own admission/adapter gates. Lower only after exact typing, binding and target/call checks; preserve reference, initialization, unsafe, provenance, ownership and evaluation obligations. Normalized-only native facts need no fabricated raw enum dependency. |

Use public compiler APIs and independently authored carrier fixtures, not
internal enum-row injection as source support evidence. Existing
[identity tests](../../tests/Terrum.CodeAnalysis.Tests/Semantics/BoundEnumIdentityTests.cs)
and [private enum tests](../../tests/Terrum.CodeAnalysis.Tests/Modules/PrivateNominalEnumTests.cs)
identify regression areas, not acceptance of this new source route.

| Future cases | Required observation |
|---|---|
| Positive source and projection | Two unrelated enums reuse member keys without sharing identity; arbitrary supplied keys unrelated to names/values/order, empty and duplicate-valued members, unnamed values, aliases and eligible constant forwarding retain exact T without Core. Complete composed parts yield one owner and producer map. |
| Source negatives | Duplicate names/cross-kind collisions, ineligible bases, missing primary base, repeated base on a secondary, multiple/missing primaries, illegal secondary contracts or member visibility prefixes, out-of-domain constants and implicit successor overflow diagnose. Same-named file-local enums in distinct units retain distinct identity/access; equal paths do not merge them. |
| Mapping negatives | Missing/extra/duplicate type or member maps, wrong owner/program, stale handles, imported-as-owned declarations, conflicting retained origins/keys and attempted Core-token fabrication reject. Equal integer values never repair a mapping. |
| Boundaries | Exact selected underlying-type minima/maxima and successor overflow; primary not first in compiler order; inherited visibility and generated-unit boundaries. Otherwise valid internal/file or wider-base enums cannot be published through this bounded private carrier. Private keys at 256/257 UTF-8 bytes, empty/NUL/malformed text, ordinally distinct Unicode and inherited enum-count/shared-budget limits retain their separate source/publication outcomes. |
| Binding and replacement | Provider-scoped projection and explicit new consumer proxy preserve all original keys/expectations. Wrong Module/export/kind/revision, unexported/imported targets and changed unused members fail; physical reindexing with unchanged contracts succeeds. Independent diamond expectations are all checked. |
| Cross-feature and consumers | Enums inside fields, optional/reference/pointer/fnptr/array types, typed places and uncalled/bodyless declarations survive cloning, unrelated Core selection, private remapping and source-free re-emission. Another enum or int32 with equal bits is not T. Further ConstEval, Standard serialization and native execution require their independently admitted routes and separate evidence. |

## Adoption boundary

The recommended source identity, exact-program selector, explicit per-member
key map, caller storage responsibility and projection failure rules are
specified above; implementation must not invent substitutes. Applying this
candidate provisionally requires extending `PrivateModuleIdentityInputs`,
the private bound carrier and their exact-snapshot validation while preserving
the existing Core-only assignment path. It leaves the nominal-enum persistence
proposal's Standard/private wire owners intact. Accepted grammar and semantic
owners need no amendment for this identity-input bridge. Production provider
authentication, attribute schemas and actual Core registration remain
independent gates; wider persistence carriers retain their own bounds.
This document alone establishes neither approval, compiler support nor a
conformance milestone.
