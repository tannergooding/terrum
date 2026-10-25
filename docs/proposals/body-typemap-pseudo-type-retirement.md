# Body TypeMap complete types without pseudo tokens

**Status: unapproved; provisional direction under the authorized
missing-contract workflow.** This proposal changes no normative specification,
registry, compiler code or support claim.

## Finding and decision

**Recommend retiring Body TypeMap token kinds `0xF0` and `0xF1`, using the
existing complete TypeSpec representation whenever a direct, `0xF2` or `0xF3`
token cannot express the type.** This removes a second construction/qualification
codec rather than inventing its missing elaboration rules. It allocates no
ID, replacement token, compatibility alias or additional type identity.

The numerical assignments are **not missing**. The normative
[registry](../specs/representation/trrm-1.registry.json#L245-L265) already gives:

| Field | Existing assignment |
|---|---|
| Payload, least-significant byte first | `baseTypeMapIndex:u8`, `genericFamily:u8`, `wrapperModifier:u8` |
| Kind | `F0=wrapper<generic<T>>`, `F1=generic<wrapper<T>>` |
| Generic family | `00=none`, `01..05=simd64/128/256/512/scalable`, `06/07=reserved simd1024/2048`, `08=Span`, `09=Task`, `0A=TextSpan` |
| `wrapperModifier & 07` | `0=none`, `1=ref`, `2=ptr`, `3=box`, `4=val`, `5=own`; `6/7` unassigned |
| Remaining modifier masks | `08=outer-write`, `10=inner-write`, `20=outer-gc`, `40=inner-gc`, `80=inner-init` |

[Registry authority](../specs/representation/registry.md#1-authority-and-schema-identity)
makes those assignments normative even though the metadata prose does not
repeat them. The actual missing contract is a complete, deterministic
elaboration of every admitted pseudo payload into the existing complete type
terms:

* The labels `outer` and `inner` do not assign qualifier attachment across the
  three possible nodes, or define their attachment when either constructor is
  absent. The complete-type rules prohibit moving qualification into or out of
  a stored argument; a reader cannot select an attachment convention merely
  because the resulting type is legal. With `F0`, family `08`, wrapper `1`,
  and `inner-write`, does the bit qualify the immediate child Span or base T?
  For an eligible class C, the accepted type-term fragments
  `ref<write Span<C>>` and `ref<Span<write C>>` are distinct legal candidates;
  the field labels alone do not select between them.
* `inner-init` has no stated translation to the distinct initialization-reference
  form `0x0E` versus the position-limited Span-argument qualifier. Those are
  different contracts, not a general `init T` node. For example, family `00`,
  wrapper `1` and modifier bit `80` need an explicit decision between constructing
  `ref<init T>` and rejecting an impermissible inner qualifier.
* `09=Task` and `0A=TextSpan` name declared families, but the pseudo codec does
  not specify how they select the exact nominal generic declaration and its
  using-Module binding/profile context. They have no corresponding assigned
  TypeSpec built-in-family code. The accepted Core-identity rules expressly
  prohibit resolving this by name matching or inventing a missing selector.

The governing evidence and its limits are:

| Evidence | Established contract |
|---|---|
| [Body token forms](../specs/il-metadata.md#L325-L341) | Constructor order, Body-local base and fixed fields, not a full elaboration algorithm |
| [TypeSpec forms and qualification](../specs/il-metadata.md#L989-L1070) | Complete indexed/nominal forms, per-node qualification, distinct `ref-init`, and exact resolved identity |
| [Built-in families](../specs/il-metadata.md#L1094-L1141) | SIMD/Span/wrapper codes; no Task/TextSpan built-in code or permission to use reserved family values |
| [Core identity](../specs/libraries/adopted-inventory.md#1-status-and-identity), [Task identity](../specs/language/task-responsibility.md#core-identity-and-value-contract), [TextSpan family](../specs/libraries.md#views-shapes-and-tensor-primitives) | Exact registered declaration/profile identity, not spelling or a fixed package binary |
| [Body maps](../specs/il-instruction-set.md#L286-L309) | Every TypeMap entry resolves a complete type; formal Homes use exact substituted signature types |
| [Canonical tokens](../specs/representation/encoding-canonicality.md#4-body-dependency-maps-and-typespecref-values) | Pseudo dependencies precede their entries; exact pseudo spellings are preferred when representable, without defining that representability |

Thus the gap is not an absent family-number table, nor does the implementation's
missing Home-to-signature comparison itself establish an undecided contract.
The accepted equality requirement already exists. The missing pseudo
elaboration prevents a general consumer from deriving its complete operand.

## Minimal completion versus retirement

| Candidate | Consequence |
|---|---|
| Retain `F0/F1` and complete them | Preserve four-byte compositions and the existing canonical preference. Define total node attachment/absent-layer rules, qualification composition with an already qualified base, `ref-init` versus Span-init translation, and exact Task/TextSpan role-to-declaration selection. Apply ordinary formation/normalization after elaboration. Families `01..05` can map to TypeSpec families `80..84`, `08` to `8A`, and wrapper codes `1..5` to forms `07..0B`; those correspondences do not settle the missing cases. Keep the eight-bit base bound and acyclic dependency graph, with TypeSpec fallback after remapping when a base no longer fits. |
| **Retire `F0/F1`** | Reuse TypeSpec's complete constructors, arguments and per-node qualifiers. Remove the eight-bit Body-local dependency graph and the pseudo-only Core role selection. Existing row forms carry nominal declaration and formal identities explicitly. Extra global TypeSpec rows may increase artifact size; no size or performance improvement is claimed without measurement. |

Retirement is the simpler complete semantic contract. Both choices intentionally
change or complete the current draft; neither is forced by stability. Retaining
the pseudo codec solely to preserve unpublished bytes is not required by
[format versioning](../specs/representation/format-versioning.md#2-beta-draft-and-preview-artifacts).
No normative change is made here; subsequent provisional work must explicitly
select this proposed schema rather than claiming it is the accepted one.

## Proposed selected-schema contract

### Token forms and complete representation

The proposed Body TypeMap kind set is exactly `11`, `13`, `1C`, `F2`, `F3`.
All entries remain four-byte little-endian words:

| Kind | Proposed interpretation |
|---|---|
| `11` | Low 24 bits are a required one-based TypeSpec row |
| `13` | Existing exact unqualified TypeDef identity, under its ordinary binding/context rules |
| `1C` | Existing TypeDef Import expectation and retained complete binding contract |
| `F2` | Unchanged assigned well-known complete type; upper 16 payload bits zero |
| `F3` | Unchanged TypeSpec qualifier/family/atomic-argument payload and formation rules |

`F0/F1` are invalid in this selected schema, including no-op, otherwise
well-formed, acyclic and unused entries. They are not noncanonical aliases.
Consumers reject an examined token; this does not add a preliminary scan of
unrelated input or change the existing before-affected-use verification scope.
This retirement concerns only the Body **type-token** namespace; no instruction
primary byte, field token, definition kind, TypeSpec form or Feature ID changes.
The existing restrictions on other unassigned/reserved kinds remain.

Use the existing TypeSpec forms without new abbreviations:

* Indexed wrapper forms `07..0B`, indexed SIMD/Span form `0C`, optional form
  `06`, and distinct initialization-reference form `0E` carry their existing
  complete argument references. `write/gc` stay on their actual nodes.
* `Span<init T>` uses `0C` family `8A` and its immediate init-qualified argument;
  `ref<init T>` uses `0E`, never an ordinary `07` with a generally init-qualified
  child. These are accepted type terms, not runnable examples or support claims.
* Task/TextSpan and other declared generics use nominal form `05`, with an
  explicit nominal generic-definition row (`02` or `03`) and one argument word
  per declared formal. Do not assign a reserved built-in family code or add a
  name recognizer. Missing independently required profile/runtime support still
  blocks affected use; an explicit nominal reference does not confer a Core role.

A Body row token for TypeSpec row 1 is `0x1100_0001`; a non-Body TypeSpecRef
to that row is `0x0000_0001`. These namespaces remain distinct. Nested complete
TypeSpecRef fields retain their existing row/F2/F3 union and never accept
Body-map tokens. This proposal does not rename `11` to the TypeSpecRef row form.

### Identity, validity and bounds

Resolve terms using the Body's owning Module and declared generic environment,
then substitute and normalize using the existing
[complete-type rules](../specs/language/types-and-normalization.md) and
[binding relation](../specs/representation/binding-and-replacement.md).
Preserve the selected nominal declaration, Import expectation, formal owner and
ordinal, ordered type/constant arguments, constant domain/value, representation,
optionality and every qualifier position. Equivalent encodings are not distinct
types; equal names, rows in different artifacts or equal layouts are not equality.
Do not infer a closed actual type for an open formal.

The decoded complete type supplies the existing HomeMap/MethodSig,
ConstantMap, place/value and instruction-type relationships. A TypeOf operation
and its internal TypeIdentity carrier observe that same normalized identity;
removing a spelling neither erases qualification nor creates reflection/storage
capabilities. Preserve loan, backing, owner and cleanup facts separately from
type identity. This adds no source, native ABI, runtime, library or lowering
permission and no mandatory semantic re-proof of producer Body facts.

Reject malformed selected references, unsupported required forms and incompatible
bindings before affected use under
[consumer verification](../specs/representation/reader-trust.md).
Ordinary formation rules continue to invalidate reserved qualifier bits,
`write` on an ineligible inline value, `gc` on a pointer/owner/initialization
reference, the `init` qualifier bit outside its Span-argument position, wrong
generic arity/kind, or a constructor argument outside its declared domain.
A supported byte shape does not prove producer truth or establish every
family/runtime capability.

All existing bounds remain: TypeMap count `0..65,536`, local index
`0..65,535` strictly below that count, TypeSpec/table row
`1..0xFF_FFFF` not exceeding its actual table's count, and checked artifact/row
extents. Zero is not a TypeMap absence sentinel or a valid required row.
There is no 256-entry TypeMap limit or new limit on row references.
TypeSpec structural references retain their existing earlier-row/acyclicity
rules; self/forward/cyclic structural edges reject where those rules require.
Do not confuse that structural graph with permitted nominal declaration
recursion, which remains governed by its own storage-cycle rules.

### Canonical writing and remapping

Replace only the pseudo branches of the existing canonical token policy:
use `F2` for an eligible well-known type, `F3` for an eligible family over an
atomic argument, `13/1C` for eligible exact unqualified nominal identities,
and `11` for every remaining complete type. An equivalent TypeSpec-row
spelling remains valid ordinary input when a direct/inline form is canonical.
There is no preferred `F0/F1` spelling or pseudo dependency ordering.
Non-Body TypeSpecRef size/tie rules are unchanged.

Canonical TypeMaps retain first logical use from Homes, BlockEntryState,
instructions and their records, reuse eligible identical entries and omit
unused entries. A nested type used only as a TypeSpec argument does not require
an additional TypeMap entry. Home storage identities remain distinct.
Carrier-2 BlockEntryState's zero type field remains unused, not TypeMap[0].

A producer or rewriter must:

1. Start from checked complete type terms. **Do not migrate ambiguous old
   pseudo bytes by guessing their meaning.** Migration requires the producer's
   complete terms or an explicitly selected source schema with a complete
   elaboration contract. No historical decoder or compatibility reader is required.
2. Build/remap the required TypeSpec graph, preserving its ordering, declaration,
   formal, constant-source and using-Module relationships. Shared rows may be
   reused only when all relevant contexts preserve meaning; retain distinct
   Import expectations and required contracts even if their types resolve equally.
3. Rebuild TypeMap entries and every affected Home, BlockEntryState, typed-record,
   compact descriptor and instruction reference in its actual namespace. Drop
   an old pseudo-only base entry only when it has no surviving logical use.
   Remap non-Body TypeSpecRefs, including ConstantMap types, if their rows move.
4. Recompute map counts/mask, row/section offsets and checked extents. If new map
   indexes change short-form eligibility, reselect instruction forms and rebuild
   Block offsets/lengths, record offsets, code length and SourceMap slots/step
   roles under the existing transformation contract. Byte substitution is not
   a type conversion or a valid remap.

Exact draft-schema agreement is required before exchange even when version
bytes remain `(1,0)`. Do not retry `F0/F1` with another decoder after rejection,
or reinterpret an old artifact simply by rewriting its version.

## Integration owners and current implementation boundary

Normative adoption would make the following coordinated edits; this unapproved
proposal changes none of them:

| Owner | Proposed change |
|---|---|
| [Registry](../specs/representation/trrm-1.registry.json) | Remove `F0/F1` from type-token acceptance and the pseudo-only payload/family/modifier/canonical-policy entries; no replacement assignments or aliases |
| [Metadata token forms](../specs/il-metadata.md#table-stream-and-token-encoding) and [Body maps](../specs/il-instruction-set.md#body-row-layout-and-compact-implementations) | State the five-kind set and existing complete TypeSpec fallback; preserve all map/row widths and type relationships |
| [Canonicality §4](../specs/representation/encoding-canonicality.md#4-body-dependency-maps-and-typespecref-values), [registry overview](../specs/representation/registry.md), [conformance cases](../specs/conformance/il-encoding-canonicality.md#4-complete-types-and-constants) | Remove pseudo preference/base ordering; replace pseudo acceptance/cycle cases with the selected-schema cases below |

The provisional selected-Body [map validator](../../sources/Terrum.CodeAnalysis/Modules/TrRmRegisterBodyMapValidator.cs#L91-L121)
now rejects examined `F0/F1` entries and no longer interprets their
pseudo-only fields or scans their base graph. The [HomeMap check](../../sources/Terrum.CodeAnalysis/Modules/TrRmRegisterBodyMapValidator.cs#L34-L79)
still checks roles/ordinals, not complete substituted type equality. The
[Body reader](../../sources/Terrum.CodeAnalysis/Modules/TrRmRegisterBodyReader.cs#L26-L112)
continues to frame/slice maps without an artifact-wide Body-content scan. This
is a provisional selected-schema divergence: the normative registry remains
unchanged, and the implementation does not claim current-schema conformance.

Active [selected-map tests](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmRegisterBodyReaderTests.cs#L225-L250)
now reject no-op `F0` and `F1` tokens, and
[additional selected-map cases](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmRegisterBodyReaderTests.cs#L345-L380)
reject formerly accepted wrapper and generic pseudo forms. The focused
`TrRmRegisterBodyReaderTests` selection passed 47 tests with zero skipped; the
combined active `Modules.TrRm|Modules.Metadata|CliTests.TrRm` selection passed
1,745 tests with zero skipped. The normal Release analyzer build still reports
1,514 repository-wide diagnostics, with none on the changed validator or test
lines after the local style correction. These results establish only the
implemented rejection behavior, not a nontrivial pseudo's complete type,
modifier placement or signature equality. The
[TypeSpec argument cases](../../tests/Terrum.CodeAnalysis.Tests/Modules/Metadata/TypeSpecTableValidationComponentTests.cs#L162-L180)
already reject Body-only `F0/F1` in non-Body complete-type fields.

General complete-type substitution, contextual binding, canonical writing,
rewriting and typed Body support remain independent implementation gates in the
[existing conformance inventory](../implementation/conformance-gaps.md#cg-001).
Retiring the pseudo codec would remove one contract gap, not complete that work.

## Required conformance cases

These cases apply to the **proposed** schema. The current provisional
implementation has selected and tested only the `F0/F1` rejection behavior;
the remaining cases are not established by that slice, and no normative
approval is implied.

| Case | Expected result |
|---|---|
| Equivalent atomic/wrapper TypeSpec and eligible F2/F3 spellings | Same complete identity; ordinary acceptance, existing direct/inline canonical preference |
| Indexed wrapper/Span/SIMD over a nominal, formal or qualified argument | Preserve the complete argument and per-node qualification; no additional base TypeMap entry |
| Writable outer reference versus writable stored class-handle argument | Distinct exact types; neither can satisfy the other's signature merely by layout |
| Distinct `0E` ref-init and indexed Span with immediate init-qualified argument | Preserve their separate initialization contracts; invalid init placement rejects |
| Explicit nominal Task/TextSpan with exact type arguments, including Task's default unit and TextSpan's erased unit specialization | Retain selected declaration/profile identity and ordinary family constraints; no reserved built-in codes or payload erasure |
| Unrelated generic with the same spelling, layout or one equal instantiation | Not the selected nominal/Core identity; no special-case recognition |
| Open formal and two unrelated formal owners with equal ordinal/name | Preserve owner/ordinal identities and forward substitution; do not merge by one actual instantiation |
| `F0/F1` with every former constructor/modifier pattern, including no-op, unused, acyclic, cyclic and out-of-range-base examples | Invalid producer output; reject the examined token kind under this selected schema, not a canonicality failure or fallback decode |
| Wrong row/token namespace, zero/out-of-range row, invalid arity/domain or reserved qualifier/family | Reject before affected structural/binding use |
| TypeSpec row `0xFF_FFFF` with that actual row present; table count `0x100_0000` | Accept the inclusive row bound; reject the oversized table without truncating its count |
| TypeMap count 65,536 and final index 65,535; count 65,537 or index equal to count | Accept the in-range boundary; reject overflow/out-of-range; indexes 255 and 256 have no special pseudo boundary |
| Structural TypeSpec self/forward/cycle edge versus an otherwise legal recursive nominal declaration | Reject the former under existing row rules, without banning the latter |
| Reordered/duplicate TypeMaps and shared TypeSpec rows across resolved Imports/Modules | Ordinary acceptance only with preserved references and meaning; canonical reuse must not erase retained binding obligations |
| Home/MethodSig or ConstantMap type differs only by inner qualification/nominal argument | Existing exact type relationship is not satisfied; structural token acceptance alone is not evidence of equality |
| TypeOf/TypeIdentity before and after map/row rewriting, including equal-layout unequal nominal types | Preserve normalized identity, comparisons, carrier/refinement evidence and unused carrier-2 zero fields |
| Re-encoding removes pseudo-only bases or changes short-instruction eligibility | Remap all uses and affected source/offset data; no dangling indexes or byte substitution |
| Equal `(1,0)` versions but different pseudo acceptance/elaboration schemas | Require exact out-of-band schema selection; never silently interoperate |

The bounded decision is retirement versus a separately completed pseudo codec.
This recommendation selects retirement provisionally; normative adoption and
implementation/qualification remain separate work.
