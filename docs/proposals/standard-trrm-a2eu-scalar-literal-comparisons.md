# Standard TRRM A2EU numeric-literal comparisons

**Status: unapproved; provisionally actionable only under the user's
authorized missing-contract workflow.** This document proposes one exact
schema composition. Creating it does not approve normative changes, register
a capability, implement the feature or establish compiler/runtime support.
[Proposal index](README.md).

## Decision and alternatives

Recommend composing [A2EU](standard-trrm-unit-fixed-array-initialization.md#alternatives-and-exact-composition)
with the **whole 24-scalar frozen-value codec** in
[scalar frozen-value registration](scalar-frozen-value-registration.md),
including its proposed `11/1` registration with no unconditional
prerequisites. Add only completed numeric-literal operands to the existing
bounded scalar comparisons used by unit-conditional capture. Do not change
source syntax, literal typing or exact-unit conditional semantics.

The [conditional proposal](unit-valued-conditional-expressions.md#fixed-array-capture-and-body-consequences)
correctly excludes numeric literals from the present A2EU Body profile.
Assigned [ConstantLoad and ConstantMap](../specs/il-instruction-set.md#explicit-typed-constantmap)
mean an exact typed value, not an immediate integer. A2EU selects neither
the scalar codec nor its Feature 11 catalog amendment. This is a genuine
composition decision, not just a missing operand whitelist.

| Candidate | Assessment |
|---|---|
| A2EU-only literal extension in a new selector, with a ten-type codec or new typed-immediate compare form | Could be specified, but needs its own complete value/Feature scope, encoding, identity, validation and rewrite rules. Reusing the scalar proposal's bytes while redefining `11/1` as ten types creates a second capability meaning; a new immediate carrier duplicates an already assigned load. Neither is the smallest coherent integration. |
| **A2EU plus the complete scalar frozen-value codec — recommended** | One new whole-schema selection; reuse the existing codec owner and assigned loads. The persistence contract covers all 24 types, while this bounded comparison extension covers only the ten numeric types below. A smaller executable subset is not a different codec. |
| Enable `0x16`/`D4` under the existing A2EU identity | Reject: silently selects unsupported value decoding and a different Feature catalog. Recognizable instructions or version bytes do not complete that contract. |
| Compose general predicates or richer frozen values too | Unnecessary for ordinary scalar comparisons; adds independent contracts without resolving another part of this blocker. |

No arithmetic synthesis, fake initialized Homes, helper calls, typeless
immediates, alternate opcode meanings, compatibility aliases or byte-level
patching are selected.

## Exact combined selection and manifest

Propose the exact, case-sensitive out-of-band selector:

**`trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-scalar-frozen-values-1`**

Call it **A2EU+SF1** here; that shorthand is not an API/CLI alias or a flag
combination. Its complete schema is A2EU
`trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-1`,
amended by the scalar codec/registration from
`trrm/1-draft-scalar-frozen-values-1`, with the catalog reconciliation below.
It is not an alias for either component or a rule for composing future text.
A changed composition needs a distinct exact selection.

Preserve **A2, A2E and A2EU unchanged**, including their selectors,
catalogs and admission boundaries. A2EU+SF1 retains A2 Copy/type/layout
contracts, A2E's N=0-only None/zero-use initialization, A2EU's typed R14 unit
source and ordinary unit stores, and all F/S contracts. No existing default
or other profile gains scalar constants. In particular, A3, RootPin,
general predicates, enums and exception-region compositions are not selected.

| Candidate catalog rule | Exact disposition in A2EU+SF1 |
|---|---|
| `11/1=TypedFrozenValues` | Adopt the scalar proposal's complete closed 24-scalar registration and prerequisites `[]`, intentionally replacing inherited incomplete `11 -> 9` **only in this new schema**. Require 11 in each using Module's effective manifest for every retained use of that value path, including non-Body uses. An empty-present ConstantMap or unused physical value tables alone is not a trigger. |
| `10/1`, `8/1`, `16/1` | Preserve [F's bounded catalog](trrm-draft-schema-profile-selection.md#composition-rules-and-catalog-prerequisites): selector-only `10 -> {}`, `8 -> {1,2,10}`, `16 -> {1}`. The scalar proposal's preservation of baseline `10 -> 11` does not replace F's already selected selector-only scope. Scalar values do not require 10. |
| `9/1`, `12/1` | Remain registration-pending; 12 retains inherited `{10,11}`, not G's `{11,17}`. ConstantSource tags 2/3 and retained execution/target-binding forms remain unsupported without their complete registration. Neither prior compile-time formation nor an ordinary scalar literal requires 9 or 12. |
| Array, native and target closure | Retain `20/1 -> 2/1 -> 1/1`, Windows x64 TargetProfile `(1,1)` and Module target requirement 1 for the array execution route. Calls, fields, queries, outputs and dependencies keep their own inherited triggers and closures. Feature 11 itself does not imply arrays, native calls or a new runtime/helper. |

Use governing edition 1 and the existing per-Module least-closure rules;
another Module's 11 cannot supply a missing using-Module requirement.
Selecting the schema does not require every Module to use constants or
arrays. All other inherited catalog entries remain unchanged. Feature lists
never select a decoder.

Keep format `(1,0)`, Body implementation/codec `01`, existing sections,
row strides, record kinds, prefixes, controls and 1/2/4-byte instruction
bands. Allocate no numeric ID or width. Select the complete schema before
indexing, production, binding, reconstruction, rewriting or lowering, through
the existing exact-selection surfaces. Equal versions, feature sniffing,
name-fragment parsing, decoder retry and private fallback are forbidden.
[Exact draft agreement](../specs/representation/format-versioning.md) remains
the governing policy.

## Operand and value contract

The newly admitted literal comparison domain is exactly **ten types**:
`int8`, `int16`, `int32`, `int64`, `uint8`, `uint16`, `uint32`, `uint64`,
`float32` and `float64`. This matches the numeric subset of the current
[`DirectNativeImportContract.IsScalar`](../../sources/Terrum.CodeAnalysis/Semantics/DirectNativeImportContract.cs)
admission; it makes no additional scalar-operation or native-call ABI promise.

For each of the existing six source comparisons, each operand may be an
already admitted Home read, an already admitted direct NativeImport call,
or a **completed exact typed numeric constant** (`BoundConstant`) in that
domain. Admit literals on either or both sides, with the same normalized
complete operand type and an ordinary Boolean result. Preserve
[ordinary literal context and evaluation rules](../specs/language/primitive-operators.md#operand-contexts-and-evaluation):
Boolean result context does not select a numeric width; there is no
promotion, width-based coercion or new constant-expression evaluator.
Literal construction or independently authorized earlier folding must
already be complete and valid. At this boundary admission depends on the
typed value, not its source spelling. Unevaluated arithmetic, conversions,
calls and retained execution requests gain no constant-operand admission.

The full scalar codec still owns **24** types and all its validity rules.
The other fourteen gain no new Body-use admission here: existing Boolean
literal branching and character/rune read/call comparisons remain unchanged;
unit still uses A2EU's R14 route. Wide integers, other binary formats and
decimals retain independent operation/ABI support gates. A ten-type
comparison implementation must not claim it implements a ten-type variant
of `11/1`. `nint/nuint` remain outside the frozen codec entirely; no
int64/uint64 substitution follows from the target.

Reuse, without a second field or payload definition, the scalar proposal's
[rows and Blob codec](scalar-frozen-value-registration.md#rows-and-blob-payloads):
`ConstantMap -> ConstantSource kind 1 -> FrozenValueNode kind 1 -> Blob`.
The map entry and root have the same normalized complete type. Identity for
interning is complete type, codec and **exact payload bits**, not numerical
equality, lexical spelling, row number or host floating equality. Preserve
signed zero, NaN sign/state/payload and decimal cohorts even when a later
comparison considers values equal or unordered. Loads do not quiet or
canonicalize them. No pointer, nominal wrapper/enum or aggregate is a scalar
merely because its width matches.

Materialize a literal into an ordinary R0..R13 typed binding using assigned
ConstantLoad: canonical tiny `09` only for R0/map index 0, short `0x16`
when the index fits 0..15, otherwise `D4` with the full u16 ConstantMap
index and zero high control nibble. A longer valid assigned form remains
ordinary noncanonical input. These indexes are not TypeMap indexes,
ConstantSource rows, register numbers or inline literal bits.

Continue with assigned `0x40` compare (same-typed inputs to uint8 relation)
and `0x41` predicate-to-bool. Preserve masks `2`, `D`, `1`, `3`, `4`, `6`
for equality, ordinary inequality, less, less/equal, greater, greater/equal.
In particular, ordinary floating inequality includes unordered; do not
replace `D` by ordered-not-equal `5`. No new predicate mask, signaling
policy, unit comparison or Boolean ordering is selected. Existing
[comparison semantics](../specs/il-instruction-set.md#comparison-conditions-and-consumers)
and [canonical writing](../specs/representation/encoding-canonicality.md)
remain authoritative.

## Sequencing, consumption and transformations

Evaluate/capture the left operand, then the right, once each, before comparing.
A left Home read must retain its captured value across a right call, not be
reread afterward; calls and their checked arguments retain their actual
failure/effect/provenance obligations. Constant payloads are complete
effect-free values, not calls to reconstruct at runtime. Register allocation
must preserve live values across calls without manufacturing storage state.
Observation-equivalent folding/scheduling remains an optimization, not an
alternate constant codec.

Evaluate only the selected unit arm and continue only after normal
completion. Preserve existing short-circuit/nested conditions, cleanup and
no-return edges. Each completed unit conditional is one initializer; all N
captures precede final stores/publication. Operand/selected-arm failure
prevents later initializers and publication, preserves reached external
effects and leaves a replacement's old destination unretired until ordinary
unwind. This selects no new failure policy or exception/cleanup transport.

**Consumer checks before affected use:** apply the scalar owner's complete
framing, typed-reference, field, width and scalar-validity checks, plus exact
schema/manifest/target support, ConstantMap and instruction bounds, writable
load destinations, register/entry-state types and supported comparison forms.
Check actual provider bindings. Missing 11, wrong-table references, mismatched
complete types, invalid values and unsupported required forms cannot fall
back to immediates or zero. Preserve the
[producer-truth boundary](../specs/representation/reader-trust.md): producers
owe truthful source evaluation and Body facts; consumers perform required
safe-consumption/support/binding checks, not arbitrary semantic re-proof.

**Source-free reconstruction:** resolve typed values into exact bound
constants and preserve their complete bits through shared traversal,
rewriting, mandatory verification and lowering. Combine them with reads and
staged call results without replaying calls, reparsing literals, invoking
producer programs or requiring source/private persistence. Keep operand
snapshots, CFG, selected-arm effects and complete-array publication. Use
decoded typed operations/value flow, not sample names or opcode-byte matching.

**Re-encoding:** apply the scalar owner's
[transformation contract](scalar-frozen-value-registration.md#production-consumption-and-transformation)
to global ConstantSource/node/Blob and complete-type references, independently
of Body-local ConstantMap indexes. Rebuild first-use/deduplicated maps and
remap **every** ConstantMap use in instructions and typed records. Rebuild
mapMask bit 4, conditional count words, map payloads and prefix extent,
record offsets, short/tiny/long forms, code lengths, Block offsets/extents,
Body/section offsets and affected SourceMap ordinals/step roles; regenerate
or legitimately drop optional source data. Preserve other maps, CFG/state,
layout/provider references and per-Module requirements. Payload bits are
data, never remappable references. Retain the 65,536-entry Body-map and
24-bit global-row limits with checked accounting; no truncation or new budget.

**Providers and replacement:** each supplied artifact retains its independent
explicit schema and every Module its manifest. Providers carrying only
unchanged declarations may remain on supported A2/A2E/A2EU/F schemas; their
selection does not supply the root's constant decoder. Check original
Module/key/import expectations against actual providers, retaining actual
origins and any stronger relied-on facts. Compatible reordered providers
must preserve literal identity/order after remapping; incompatible signature,
native contract, Copy/layout or retained target changes reject affected use.
Built-in scalar payloads introduce no provider-rebound nominal constant
identity. Packing requires one destination schema representing all retained
obligations, never per-Module decoders. Export to an older schema must decline
if a retained scalar-value use has no admitted representation; changing the
label or deleting requirements is not conversion.

## Evidence and acceptance gates

Current [producer](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.NativeAddress.cs)
admits non-Boolean comparison operands as reads/calls and builds no
ConstantMap source chain for this route. The
[reader](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.NativeAddressReader.cs)
tracks `0x40` inputs through Home reads/staged call results, not frozen
numeric operands; [schema predicates](../../sources/Terrum.CodeAnalysis/Modules/TrRmArtifactIndex.cs)
exclude A2EU from scalar-frozen decoding. These are implementation gaps
after the proposed composition, not missing compare semantics.

The conditional proposal records **76/76, zero skips** for existing source-free
A2EU JIT/AOT coverage at
`artifacts\tst\Debug\a2eu-boolean-literal-comparisons-final3\a2eu-boolean-literal-comparisons-final3.trx`.
The active regression includes
[`StandardA2EUConditionalJitExecutesOnlyTheSelectedNativeArm`](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmForeignInvocationAddressEmitterTests.cs).
It covers no numeric-literal comparison and proves nothing about A2EU+SF1.
No build, test or native execution is performed by this documentation task.

| Required future cases | Acceptance |
|---|---|
| Positive operands | All ten types and six comparisons; read/literal, literal/read, call/literal, literal/call, literal/literal, plus existing read/call combinations. Both Boolean outcomes, nested/short-circuit conditions and N=1/N=2 unit captures retain exact results and selected-arm traces. |
| Values and type boundaries | Signed minima/maxima, unsigned maxima and high-bit patterns; float32/64 signed zeros, infinities, subnormals and both NaN states/payloads. Use independent typed frozen fixtures for representations without a source literal spelling. Compare outcomes follow the existing policy; exact constants remain distinct where their bits/types differ. Full 24-type codec write/read/rewrite fixtures remain a separate prerequisite, including decimal cohorts and unit/bool/rune validity. |
| Negative support/structure | Existing A2/A2E/A2EU must not gain the retained scalar path; reject missing/conflicting Feature requirements, unsupported selections, malformed/missing/truncated source/node/Blob chains, wrong widths/reserved fields/types, bad registers/indexes and unsupported operand forms. Native-width literals, nominal lookalikes and unadmitted comparison types do not acquire a codec/operation by matching width. Preserve rejection of untyped or heterogeneous source comparisons. |
| Index/rewrite boundaries | Exercise map indexes 0, 15/16, 255/256, 65535, count 65536 and rejection above it; global-row endpoints and reserved high bytes; reordered/duplicate/unused map/value rows, equivalent complete-type spellings and opcode-looking payloads. Independently re-encode canonical and longer assigned loads with changed map/record/Block/section and SourceMap locations. |
| Effects/failures/providers | Mutating right calls observe the left snapshot; each call executes once. Operand/selected-arm and nested argument-check failure prevent later work/publication. Compatible reordered replacement and packing preserve constants and original/actual origins; incompatible bindings reject before use. Source-free reload/re-emission needs no source or producer execution. |
| JIT and AOT | Supervised, headless execution of the **source-free rewritten** artifacts under the admitted Windows x64 FailFast route checks actual result/trace, both arms, both literal sides and failure termination; emission alone is insufficient. Retain final compiler/configuration, exact root/provider schemas and input identities, deadlines and actual exits. Broader CheckedExceptions/cleanup support remains separately gated, not silently reported as passed. |

## Approval-time owners, prerequisites and remaining decisions

Only this proposal and its index entry are changed now. After approval,
integrate the following exact owners together; do not copy the scalar codec
into a second normative definition.

| Owner | Intended amendment |
|---|---|
| [Scalar frozen-value proposal](scalar-frozen-value-registration.md#exact-integration-edits-proposed), [logical records](../specs/representation/logical-records.md#published-predicates-and-evaluation-forms), [metadata](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics), [registry prose](../specs/representation/registry.md) and [machine registry](../specs/representation/trrm-1.registry.json) | Apply that owner's full scalar registration/codec edits for the selected adopted schema; identify this exact composite without changing any older selection. Preserve its 24-type scope and excluded richer forms. |
| [Manifests](../specs/representation/semantic-manifests.md), [admission](../specs/representation/requirements-and-admission.md), [F schema selection](trrm-draft-schema-profile-selection.md#composition-rules-and-catalog-prerequisites) | Add the new exact profile and the catalog reconciliation above, particularly independent 11, unchanged selector-only 10 and still-incomplete 12. Apply existing format-versioning policy, not a new inference rule. |
| [Instruction set](../specs/il-instruction-set.md#explicit-typed-constantmap), [canonicality](../specs/representation/encoding-canonicality.md), [reader checking](../specs/representation/reader-trust.md), [binding/replacement](../specs/representation/binding-and-replacement.md) | Cross-reference the selected scalar codec and comparison profile; retain assigned loads/compare meanings, reference namespaces, producer truth and rewrite rules. Add no instruction, field or ID. |
| [Unit conditional proposal](unit-valued-conditional-expressions.md#fixed-array-capture-and-body-consequences), [A2EU proposal](standard-trrm-unit-fixed-array-initialization.md#alternatives-and-exact-composition) | Cross-link this separately selected numeric extension. Their existing exclusion/no-scalar-composition statements remain true for original A2EU; do not replace its identity or source semantics. |
| [Source selection](standard-trrm-source-schema-selection.md), [selected consumption](standard-trrm-selected-provider-consumption.md), [native handoff](standard-trrm-provider-native-lowering.md) | Add deliberate exact-profile support through producer/consumer/provider/re-encoding/native paths only when implemented; preserve API/CLI defaults and independent provider selections. |
| [Registry cases](../specs/conformance/il-registry.md), [constant canonicality cases](../specs/conformance/il-encoding-canonicality.md#4-complete-types-and-constants), [manifest cases](../specs/conformance/il-semantic-manifests.md), [implementation inventory](../implementation.md) and [CG-001](../implementation/conformance-gaps.md#cg-001) | Add independent complete artifacts and the acceptance cases above; record actual producer/reader/rewrite/native support and remaining gates only when evidenced. This proposal closes no broader conformance milestone. |

Prerequisites are the unchanged A2EU/conditional directions, the scalar
proposal's complete codec and registration, explicit composite selection
through every layer, and complete retained target/call/binding support.
Implementation must add constant production, consumption and remapping to
the general NativeAddress path and carry exact values through verification
and JIT/AOT; no compiler or test edit is authorized here.

**Remaining approval:** the combined selector, catalog reconciliation and
ten-type literal-operand admission are all unapproved recommendations,
provisional only under the quoted workflow. The scalar proposal's own
adoption remains pending. Wider executable numeric domains, native-width
frozen values, retained Feature 12 forms, richer value trees, additional
targets and A3/G/RootPin/exception-region combinations require their own
explicit decisions/support gates. None is a prerequisite to this bounded
FailFast comparison direction, and none may be inferred while implementing it.
