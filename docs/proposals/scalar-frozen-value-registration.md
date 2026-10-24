# Scalar frozen-value registration

> [!IMPORTANT]
> **Unapproved proposal.** The recommendation is provisional implementation
> direction under the current task authorization, not normative approval.
> This document does not itself approve IDs, a schema, implementation support
> or conformance. The accepted registry and specifications remain unchanged.

## Decision and current gap

Register a closed fixed-width scalar path through the existing
`ConstantMap -> ConstantSource -> FrozenValueNode -> Blob` representation.
Keep the assigned eight-byte source and 24-byte node; add no instruction,
section, private codec, compatibility alias or `ConstBits` interpretation.

The [logical owner](../specs/representation/logical-records.md#published-predicates-and-evaluation-forms)
already selects typed values distinct from execution requests.
The [registry](../specs/representation/trrm-1.registry.json) assigns
`9/1=NominalUnionMetadata` and `11/1=TypedFrozenValues`, but both registrations
are incomplete. Its unconditional `11 -> 9` edge currently prevents a
complete scalar-only closure. Recognizable row shapes are not registration.
The metadata `Answer` [excerpt](../specs/il-metadata.md#row-assignment-example)
omits its value rows; the complete integer
[vector](../specs/conformance/il-registry-vectors.json) uses argument loads,
not ConstantMap. Neither completes this missing contract.

**Recommend intentionally narrowing the initial registration of `11/1` to
the scalar domain below and removing its unconditional prerequisite on
`9/1`.** Feature 9 remains assigned and registration-pending. Aggregate,
enum, union and immutable-declaration records remain separately gated, not
implicitly enabled by scalar support. This amends the draft capability
boundary; it is not a claim that the existing broad assignment was already
fully registered.

The source language's 26-scalar and admitted composite constant-execution
floor is unchanged. This proposal completes one interchange capability,
not that floor or full TRRM 1.0 conformance.

## Exact integration edits proposed

These are future owner edits, not changes made by this document.

| Owner | Addition or replacement |
|---|---|
| [Logical records: generic sources](../specs/representation/logical-records.md#generic-domains-and-specialization-branches-encoding-pending) and [frozen values](../specs/representation/logical-records.md#published-predicates-and-evaluation-forms) | Add the scalar field/Blob rules below. Replace “Feature 11 requires 9” with “Feature 11 revision 1 has no unconditional Feature prerequisites and registers only fixed-width built-in scalar frozen values.” Preserve `10 -> 11` and `12 -> 10,11`. Replace the `ImmutableData` ownership sentence with “Its section is assigned; capability registration remains pending and Feature 11 revision 1 does not activate this family.” Preserve the broader logical value/tree requirements as pending physical completion. |
| [Machine registry](../specs/representation/trrm-1.registry.json) | Add `11/1` to `semantic_manifest.features`, using the existing object fields, governing editions `[1]`, prerequisites `[]` and the exact scope below. Mirror that registration in `metadata.feature_ids`, replacing only Feature 11's prerequisite/status. Add `metadata.value_tables` as described below; retain existing section strides, tags and atomic allocations. Feature 9 remains pending, not a registered empty capability. |
| [Registry prose §4](../specs/representation/registry.md#4-admission-status-and-namespace-boundaries), [manifest §§2–4,6](../specs/representation/semantic-manifests.md), [admission §§3–4](../specs/representation/requirements-and-admission.md) | Replace blanket “Features 9–12 pending” statements with `9,10,12` pending and `11/1` registered for this closed domain; document its trigger and complete closure. Do not change edition 1's empty baseline Feature set or imply reduced GA conformance. |
| [Metadata: rows and references](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics), [Body boundary](../specs/il-metadata.md#body-table-and-coded-instruction-boundary) | Add the per-kind source/node/Blob rules and using-Module context below. Complete the `Answer` excerpt with value rows and a required `11/1` manifest entry; its existing zero Feature range is insufficient for this proposed path. |
| [ConstantMap](../specs/il-instruction-set.md#explicit-typed-constantmap), [canonicality §5](../specs/representation/encoding-canonicality.md#5-constant-values-and-exact-bits), [reference verification §4](../specs/representation/reader-trust.md#4-schema-owned-reference-namespaces) | Add exact value identity, table-selected references, unit and fixed-container rules below. Preserve ordinary duplicate/noncanonical acceptance and producer-truth boundaries. |
| [Format identity](../specs/representation/format-versioning.md), registry/metadata/manifest schema labels and [vectors](../specs/conformance/il-registry.md) | Identify the adopted amended draft consistently; candidate label `trrm/1-draft-scalar-frozen-values-1`, still `(1,0)`. This is an out-of-band public draft designation, not a private schema, on-wire selector or alternate decoder. Add independent complete constant artifacts and the cases below. |

All numbers in the proposed registration text retain existing assignments;
their proposed scope/status changes remain candidates. A registry collision
check found unique section, Feature and DefId allocations. In particular,
`0003`, `0030`, `003F`, `0040`, `0041`, `0034/0035`, Feature IDs `9/11`,
and UnionCaseDef DefId kind `21` are occupied by the stated families.
There is no new numeric allocation. Section `0012`, TypeSpec escapes, flags,
opcodes and optional-extension space are not used.

## Candidate registration and value tables

**Feature `11`, exact revision `1`, governing edition `1`:**
typed persistence, loading, comparison for interning, remapping and target
materialization of the fixed-width built-in scalar values listed below.
Its representations are ConstantMap, ConstantSource tag 1, FrozenValueNode
kind 1 and scalar Blob payloads. It has no unconditional Feature, evaluator,
runtime-helper, union or witness prerequisite. Materialization additionally
requires the selected target's compatible scalar representation and core
Blittable commitment. Scalar operation, call and failure support are not
granted by this value registration.

Require `11/1` in each using Module's effective manifest whenever retained
content references this value path, including non-Body uses. Merely having
the common Body framing, an empty-present ConstantMap or unused physical
value tables does not activate it. Tables have no new Module cursor:
references must resolve consistently in every using Module context.
All emitted rows still obey the selected schema. Surrounding generic,
default, declaration or contract uses retain their own registration gates;
a scalar source does not complete those capabilities.

Add the following three entries to candidate `metadata.value_tables`.
This is a machine-readable registry description, **not another wire table**;
`metadata.sections` remains the row-shape authority, and projections must agree.

| Entry | Exact registration content |
|---|---|
| `constant_source` | Section `0030`; eight-byte fixed row; scalar registration requires `[11,1]`; tag `1` selects a nonzero typed row in `003F`. Assigned tags `2 -> 0042` and `3 -> 0049` retain their separate Feature 12 gate; zero/unknown tags are invalid. |
| `frozen_node` | Section `003F`; 24-byte fixed row; scalar registration accepts kind `1` only with a complete type in the closed scalar table and the field constraints below. Assigned kinds `2..6` and enum interpretations of kind `1` remain pending richer registrations. |
| `scalar_blob` | Section `0003`; existing offset-indexed variable Blob framing; referring-node-selected codec `fixed-scalar-frozen/1`; exact little-endian fixed containers and validity rules below. No payload-local tag, count, reference, padding or codec selector. |

### Rows and Blob payloads

All multi-byte fields are little-endian. Table-specific references are
one-based `u32` words in `1..0xFF_FFFF` with a zero high byte, not tokens.
Complete types use the accepted four-byte TypeSpecRef grammar.

| Record/field | Candidate scalar rule |
|---|---|
| ConstantMap, offsets `0,4` | `TypeSpecRef type, u32 constantSourceRow`; both required. The complete normalized type equals the selected root's type, not merely its storage width. |
| ConstantSource, offsets `0,4` | `u32 kind=1, u32 payloadRef`; `payloadRef` selects FrozenValueNode, not Blob, TypeSpec or a Body-local index. |
| FrozenValueNode, offset `0` | Required `TypeSpecRef typeSpec`; normalize to one unqualified built-in scalar in the table below. Equivalent row and F2 references are permitted; a family/F3, formal, nominal or qualified type is not erased to a scalar. |
| Node, offsets `4,5,6` | `u8 kind=1, u8 flags=0, u16 reserved=0`. |
| Node, offset `8` | `u32 scalarBlobRef`: exactly zero for unit; otherwise a required typed Blob reference. |
| Node, offsets `12,16,20` | `u32 childFirst=0, u32 childCount=0, u32 activeUnionCaseDef=0`. No FrozenValueChild or union metadata is required for a scalar root. |

| Existing atomic code/type | Blob bytes | Interpretation and validity |
|---|---:|---|
| `00` void | 0 | Singleton unit; no Blob reference or payload. A nonzero reference, even to an empty Blob, is invalid, not an alias. |
| `01` bool | 1 | Exactly `00` or `01`. |
| `02/03/04` char8/char16/char32 | 1/2/4 | Unsigned raw code units; all containers valid, including char16 surrogates and char32 values outside Unicode's scalar range. |
| `05` rune | 4 | Unsigned Unicode scalar `0..10FFFF`, excluding `D800..DFFF`. |
| `06..0A` int8/16/32/64/128 | 1/2/4/8/16 | Exact two's-complement container. |
| `0B..0F` uint8/16/32/64/128 | 1/2/4/8/16 | Exact unsigned container. |
| `12/13/14/15/16` float16/bfloat16/float32/float64/float128 | 2/2/4/8/16 | Exact sign/exponent/fraction containers specified by the binary-format field table in [Windows x64 layout §2](../specs/targets/windows-x64-data-layout.md#2-binary-floating-representation). All patterns valid; no floating arithmetic while loading. |
| `17/18/19` decimal32/64/128 | 4/8/16 | Candidate wire choice: the precise BID containers/classification in [layout §3](../specs/targets/windows-x64-data-layout.md#3-ieee-decimal-bid), including noncanonical finite/NaN containers. Not DPD, CLR decimal or host serialization. |

This proposes an explicit scalar wire codec, not an inference that native
storage is already a serialized-value contract. The binary/BID choices match
Windows x64. Other targets must preserve the declared representation
observations through an explicitly admitted mapping or reject affected
materialization; numerical conversion is not such a mapping.

The closed domain contains **24** of the 26 source scalars. Native-width
`nint/nuint` (`10/11`) are deliberately excluded: the root has no target
profile field, and neither Blob length nor the producer's host selects one.
Their target-bound registration, including whether a fixed-target Module
alone suffices or a TargetValueBinding is mandatory, remains a decision.
Do not recode them as int64/uint64. This does not restrict their source
semantics or authorize replacing a required `nint` Count/query result.

Blob row extent must equal the stated width exactly; there is no zero
extension, truncation, compressed integer or target-endian alternative.
Unit is not an empty/nonexistent ConstantMap entry. Integers retain their
complete signed/unsigned type; floating and decimal values retain every bit,
including zero sign, NaN sign/state/payload and decimal cohort/noncanonical
container. Serialization, loading and interning do not quiet or canonicalize
them. Constant-execution NaN selection occurs at the admitted operation,
not here.

## Production, consumption and transformation

- **Producer:** establish the actual typed value and source validity, emit
  the exact rows/payloads and truthful complete requirements/dependencies.
  Already completed source evaluation may leave only a frozen value when
  no other required contract remains. Do not retain its Body solely to
  replay it, but do not discard a retained evaluation-form/public contract
  merely to avoid its Feature requirement.
- **Consumer:** before affected use, validate selected directory/offset
  framing, strides, checked extents/counts, reserved fields, typed references,
  complete type agreement, scalar validity and actual schema/Feature/target/
  binding support. A recognized shape with an incomplete registration is
  unsupported, not a successfully decoded executable value. Do not re-run
  constructors, parsers or producer programs to discover or prove the value.
  Semantic producer truth remains distinct from these mandatory checks.
- **Canonical writer:** use first logical use for ConstantMap, reuse identical
  typed values and omit unused entries under the existing map rules. Identity
  is normalized complete type plus codec and exact payload, never numerical
  equality, row number or host object equality. Global source/node/Blob
  interning is optional; multiple references may share an immutable scalar
  root. No unique whole-artifact byte image is required.
- **Ordinary reader/audit:** accept valid reordered/duplicate map and immutable
  value rows with consistent references, longer assigned load forms and
  empty-present maps. Canonicality audits may reject their scoped deviations.
  Wrong widths, tags, types or reserved fields are malformed/unsupported,
  not alternative canonical forms. Sharing scalar roots does not authorize
  sharing future non-root children; the accepted strict-tree rule survives.
- **Rewriter:** resolve and remap ConstantSource and FrozenValueNode rows,
  complete TypeSpecRefs and Blob references independently; never rewrite
  bytes inside scalar payloads as references. Rebuild Blob/Body/section
  offsets, ConstantMap indexes in instructions and typed records, mask/count
  words, instruction sizes, Block extents and affected SourceMap slots.
  Preserve using-Module requirements and target/binding contexts; clone
  context-sensitive records if sharing would change meaning.
- **Resources:** retain the existing 24-bit global row limits, checked
  variable indexes and 65,536-entry Body-map limit. Account for selected
  input and constructed state under the existing structural resource
  contract. Loading a frozen scalar is not a ConstantExecution request
  and does not invent execution-resource charges or new portable budgets.

Feature 12 is **not** a prerequisite merely because a value was formed at
compile time. Required retained evaluation uses ConstantSource tag 2;
completed target-dependent bindings use tag 3. Both keep the accepted
Feature 12 obligation and its complete `12 -> 10,11` closure. Their missing
program/target registration is not supplied here. Retained `DE`/layout facts
require Feature 13 only within its registered query/path/binding scope;
ordinary Layout rows alone do not. A scalar result does not erase retained
query dependencies.

### Candidate value-chain fixture

For an int32 value 42, using existing F2 type spelling:

```text
Feature row:       0B 00 00 00 01 00 00 00
ConstantMap[0]:    08 00 00 F2 01 00 00 00
ConstantSource 1:  01 00 00 00 01 00 00 00
FrozenValueNode 1: 08 00 00 F2 01 00 00 00 01 00 00 00
                   00 00 00 00 00 00 00 00 00 00 00 00
Blob table:       00 00 00 00 04 00 00 00 2A 00 00 00
```

The Blob bytes include its two offset words. The Module explicitly names
the Feature row, and the Body's `09 00` loads map index zero and returns R0.
These are candidate fragments, not a complete accepted-schema artifact.
Independent qualification must supply full header, directory, Module,
String, MethodDef/MethodSig and Body framing, not treat omitted rows as proof.

## Required cases and acceptance dependency

Add cases alongside [unit values](../specs/conformance/il-unit-values.md),
[exact constants](../specs/conformance/il-encoding-canonicality.md#4-complete-types-and-constants)
and [manifest closure](../specs/conformance/il-semantic-manifests.md):

| Cases | Required result under the candidate |
|---|---|
| Each of the 24 types; signed minima/unsigned maxima; unit; char16 surrogate; maximal char32 versus maximal rune | Independent full-artifact write/read/rewrite retains exact type/value; rune excludes surrogates/out-of-range values, unlike code units. |
| Binary signed zeros, infinities, both NaN states/payloads; BID cohorts and noncanonical containers | Exact bit preservation through load, interning and repacking; no host floating evaluation. |
| Missing/zero/high-byte/out-of-range required source, node or Blob reference; missing table; truncated Blob/index; wrong width; nonzero node reserved/child/case fields; mismatched complete types | Reject before affected use, including wrong-table token-shaped words. |
| Unit with any Blob reference; bool byte 2; bad rune; scalar-shaped nominal enum/wrapper; native-width type; pointer/address, TypeIdentity or aggregate | Reject invalid values or unsupported required forms; do not fabricate a scalar substitute. |
| Module requires only `11/1`; another requires `9/1`, `10/1` or `12/1`; retained evaluation contract survives beside a frozen result | Scalar closure has no invented union/evaluation prerequisite; incomplete richer closures still reject. |
| Reordered/duplicate maps/rows, F2 versus equivalent TypeSpec, all ConstantMap index endpoints, opcode-looking payload bytes, packed Modules and changed row assignments | Ordinary valid forms retain meaning; canonical writes deduplicate only identical typed values and remap every actual reference. |
| Required native-width Count/layout result or target-dependent value | Remains gated on its real codec/dependencies; no int64 substitution, host inference, replay or runtime fallback. |

[CG-001](../implementation/conformance-gaps.md#cg-001) remains the producer/
reader/rewriter migration and independent qualification gate; its scalar
ConstantMap slice can proceed provisionally using this decision.
[CG-007](../implementation/conformance-gaps.md#cg-007) and
[CG-010](../implementation/conformance-gaps.md#cg-010) still require their
broader value/program/target closures and source-floor work. The handoff
inventory is not changed here. A registry update, sample emission or focused
round trip alone closes none of those items.

## Remaining decisions and alternatives

These are genuine richer-contract gates, not details for an implementer to
guess while completing the scalar slice:

| Surface | Decision needed before its registration |
|---|---|
| Native-width scalars | Exact profile ownership/selection, width/value identity, fixed-target versus per-value binding and remapping/replacement obligations; settle the Feature 12 boundary without inferring it from the type name. |
| Nominal enum/wrapper | Enum declaration/base/value-validity encoding; exact nominal/carrier identity, substitution and Transparent/Copy/Blittable validity facts. Scalar shape and kind 1 alone cannot encode these. |
| Aggregates/tuples/optionals | Canonical complete field/slot sets and order; exact field/import references and substitutions; tuple TypeSpec form ID; absent/present child rules; exclusive child ranges and strict-tree/root sharing; target tag/niche/layout and accounting contracts. Reuse existing FieldReference where sufficient, but explicitly register its slot use. |
| Nominal unions and Feature 9 | TypeDef currently has only struct/class/interface kinds: select nominal-union discrimination, case membership/order, exact active-case reference encoding, imported/exported case identity and owner substitution, payload slots/validity, target tag/layout and complete metadata registration. DefId kind `21` is assigned but is not currently an allowed Import/Export target. Do not guess a kind bit or truncate a DefId into a typed row. |
| ImmutableData and richer Feature 11 scope | Complete resolved-constant/static-region declaration reference sets, publication/value/witness dependencies and ownership. Choose an explicit capability revision or separate coherent registration for richer frozen values; no implicit widening of this closed `11/1` scope. Coordinate Feature 10/12 prerequisites if their exact needs change. |
| Required source/target evaluation | Complete Feature 10/12 registration, predicate node/operand schemas, form/read/effect authorization, selected checked Body or exact intrinsic registration, resource recipe, substitutions/arguments/views, executed and layout dependencies, target profiles/bindings and cycle/ownership checks. Preserve the separate frozen result and no-runtime-fallback rule. |

**Alternatives:** retaining `11 -> 9` and registering the entire assigned
value family avoids a scope amendment, but requires the union, nominal,
immutable-data and target decisions above before any scalar value can be
admitted. It is not the smallest independent completion. Adding a separate
scalar Feature would preserve the broad Feature 11 direction, but adds a
capability/ownership split solely to preserve an unpublished draft boundary.
The recommended narrow initial registration is simpler; later extension
must still make an explicit identity/schema decision.

A substantive row redesign could inline small scalar containers in a
variable ConstantSource, remove a node/Blob indirection and reduce per-value
overhead. It also changes every constant consumer, reference/remapping rule
and composite extension point. The existing typed family is preferred for
this blocker, not because its draft numbers are immutable. No alternate
decoder or numerical compatibility mapping is proposed.
