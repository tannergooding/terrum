# A3 positive uniqueness evidence for nominal struct actuals

**Status: unapproved; provisionally actionable only under the user's explicit
missing-contract workflow.** This document selects no accepted schema or
compiler conformance. [Proposal index](README.md).

## Decision and scope

Recommend **one positive nominal uniqueness declaration**, `TypeDef.flags`
**bit 6, `0x0000_0040 = DeclaredUnique`**, solely in the uniqueness-amended A3
composition below. It supplies the missing evidence for a unique nominal
struct actual with no authored Cleanup. Preserve A2's positive, universal
`DeclaredCopy=0x20`, A3's complete formal domains and its pointer-cell/phantom
admission; do not infer either ownership category from an absent flag.

Select the complete amended candidate as
**`trrm/1-draft-foreign-invocation-storage-fixed-arrays-3-unique-1`**:
the [existing A3 composition](standard-trrm-closed-constructed-array-elements.md),
except that this proposal replaces its no-new-flag/no-numerical-assignment
claims, A3's inherited TypeDef mask, and its nominal-actual ownership checks
with the rules below.
The flag-free `trrm/1-draft-foreign-invocation-storage-fixed-arrays-3` does
not select this amendment. This is one exact A3 successor, not A3 plus an
optional document, Feature switch or inferred decoder.

No source syntax, per-instantiation fact, Constraint kind/payload, Layout
field, section, Body record, generic callable capability or ownership-runtime
codec is added. Unique actuals are still not rootless Copy array elements.

## Confirmed gap and alternatives

| Direct evidence | Consequence |
|---|---|
| [TypeDef flags](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics), [Cleanup](../specs/representation/contract-clauses.md#7-cleanup) | Accepted TypeDef flags encode visibility/kind, not Unique. Cleanup remains shape-only and requires independent uniqueness, callable-role and ownership facts. |
| [Ownership and cleanup](../specs/language/ownership-and-cleanup.md#complete-value-cleanup-and-transitive-field-retirement), [ordinary/unique structs](../specs/language.md#types-and-operations) | A unique declaration remains noncopyable with trivial fields. Without an authored cleanup body, retirement derives from its fields; an authored Cleanup method is not mandatory uniqueness evidence. |
| [A2's flag meaning](standard-trrm-rootless-copy-type-evidence.md#exact-unapproved-field-and-capability-amendments), [A3 domains](standard-trrm-closed-constructed-array-elements.md#universal-copy-across-formal-domains) | Clear Copy explicitly does not prove Unique. A3 requires actual-domain checking, including unique actuals behind pointer cells or phantom formals, but allocates no nominal uniqueness carrier. |
| [Pre-amendment `TrRmTypeDefTableReader`](../../sources/Terrum.CodeAnalysis/Modules/TrRmTypeDefTableReader.cs), [`TrRmFixedArrayLayoutValidator`](../../sources/Terrum.CodeAnalysis/Modules/TrRmFixedArrayLayoutValidator.cs) | The flag-free A3 candidate accepted `0x20` only for kind-1 structs and reserved `0x40`; its nominal actual classifier used Cleanup as the only Unique signal. The recommended successor replaces those rules; this implementation snapshot is evidence of the original gap, not a normative rule. |
| [`BoundGenericNominalTypes`](../../sources/Terrum.CodeAnalysis/Semantics/BoundGenericNominalTypes.cs), [array-table fixtures](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmFixedArrayLayoutTableTests.cs) | Semantic domain classification uses the actual `BoundStruct.IsUnique` fact independently of Cleanup. The reviewed A3 unique-actual fixtures instead encode family `0x8F`, `own<T>`; they do not cover the missing nominal carrier. |

These are inspected contracts and sources, not newly run tests.

| Alternative | Assessment |
|---|---|
| Reuse Cleanup presence | Does not represent the requested no-Cleanup declaration. Requiring a dummy operation adds callable/retirement obligations and confuses authored Cleanup with derived field retirement, including their different partial-move rules. The existing clause also explicitly requires other facts; its presence alone is not a completed ownership codec. |
| Infer Unique from missing Copy | Contradicts A2's explicit unknown/no-guarantee meaning and turns omitted evidence into domain authorization. Trivial bytes, Blittable, zero size/count and a formal-domain tag do not repair it. |
| Restrict A3 unique actuals to positive existing carriers such as `own<T>` | Coherent as an explicitly narrower profile, requiring no nominal bit. It does not preserve the requested nominal-actual admission; replacing U by `own<U>` changes the complete type and indirection rather than supplying U's evidence. |
| Reuse intrinsic `Unique` Constraint | [Property code 1](../specs/representation/logical-records.md#generic-domains-and-specialization-branches-encoding-pending) is a formal requirement derived from canonical closed-type facts, not a declaration of the actual's uniqueness. A3 admits only Blittable property code 3; widening that subset still would not supply U's missing fact. |
| **Positive A3 TypeDef flag — recommended** | Reuses the declaration-owned word, preserves nominal identity and both positive categories, and covers non-generic and closed constructed unique actuals without synthesizing cleanup. A separate nominal capability clause/table could do so, but needs additional ownership, extent, reference and binding rules without benefiting this bounded question. |

Thus a new positive nominal carrier is necessary **to retain this admission**;
the proposed flag is the smallest such carrier, not a prerequisite for an
intentionally restricted `own<T>`-only alternative.

## Exact unapproved A3 encoding and validity

All multibyte words remain little-endian.

| Item | Amended A3 rule |
|---|---|
| TypeDef | Section `0x0013`, unchanged 44-byte stride; existing `u32 flags` at byte offset 8. |
| Visibility/kind | Unchanged `flags & 0x03` visibility, with 3 invalid; unchanged `(flags >> 2) & 0x03` kinds 1 struct, 2 class, 3 interface. Kind zero is invalid. |
| `0x20` | Unchanged A2 universal positive Copy promise, permitted only for kind 1. |
| `0x40` | Positive `DeclaredUnique`, permitted only for kind 1, including retained imported expectations. |
| Reserved/mask | Bit 4 and bits 7..31 remain zero; permitted mask exactly `0x0000_006F`. Class/interface rows additionally clear both declaration bits. The separate enum kind-band candidate is not admitted. |
| Exclusivity | Both declaration bits set is structurally invalid. Both clear means no positive category in this carrier, not Unique and not permission to reconstruct a Copy value. |
| Public struct examples | `0x26` Copy; `0x46` Unique; `0x06` unknown; `0x66` invalid. These are proposed metadata values, not executable source examples. |
| Cleanup cross-row rule | A kind-1 TypeDef retaining Cleanup requires `DeclaredUnique=1`, `DeclaredCopy=0`, plus all unchanged Cleanup owner/range/singleton/target checks and independently supported role/phase/ownership facts. Unique without Cleanup is valid. |
| Requirements | Under this exact schema, a Module declaring, expecting or relying on the new type-only fact needs effective `2/1 -> 1/1`, including declaration-only/unused retained facts. `20/1` is additionally required only for retained arrays, with its unchanged closure. No new Feature ID/revision is assigned; this does not admit owning-value or Cleanup execution. |

`DeclaredUnique=1` asserts the accepted unique ownership contract of the
declaration's ordinary inline value form, **for every well-formed
instantiation admitted by its complete published formation contract**.
It is not an observation of one value, specialization or physical layout.
It grants no Copy, Blittable, rootless, initialization, backing, cleanup-role
or execution guarantee. It assigns no uniqueness evidence for classes,
interfaces, enums, tagged unions or unsupported representation constructors.

## Formation, production and consumption

1. Resolve the complete normalized actual and its exact selected declaration,
   including ordered generic substitutions and independent qualification/
   constraint support, before using either bit. For a nominal struct actual,
   domain 1 requires positive Copy, domain 2 requires positive Unique, and
   domain 3 requires one of those positive categories. When neither flag is
   set, reject affected admission even for a phantom formal, pointer field
   or N=0.
   Nullable admission remains independent; an independently admitted optional
   preserves its complete payload ownership rather than making Unique Copy.
   Already-positive constructors retain their own rules: `own<T>` is Unique;
   a raw `ptr<T>` cell is Copy and does not inherit pointee uniqueness.
2. The containing array element G still requires inherited universal Copy,
   Blittable and recursive closed rootless storage. A Unique actual U may
   remain behind a pointer cell or unused phantom formal; it cannot become
   an inline stored Copy child, even with no Cleanup or zero extent. Do not
   expand pointer pointee storage or omit its required formation facts.
   The flag does not enable materializing, moving or retiring U where the
   independent value/ownership codec is unsupported.
3. Producers publish the correct positive declaration from canonical source/
   semantic ownership facts, not Cleanup presence, names or field bytes.
   Retained ordinary Copy declarations set `0x20`; retained unique declarations
   set `0x40` even without Cleanup. Check universal declaration truth across
   the full formal domains; a favorable closed actual cannot invent either
   promise. Rewriters own the truth of changed facts. Consumers check schema,
   flag applicability/exclusivity, references/ranges, required support, actual
   domains, storage and binding before affected use, but need not re-prove
   source/Bodies. Structurally consistent false declarations remain producer
   violations under [reader trust](../specs/representation/reader-trust.md).
4. Expected Unique requires actual positive Unique from the separately
   selected supported provider schema. Preserve original expectations,
   selected actual origin and every relied-on stronger guarantee through
   substitution, reconstruction, verification, rewriting and persistence.
   Apply [A2's multiple-expectation rule](standard-trrm-rootless-copy-type-evidence.md#nominal-binding-replacement-and-re-encoding)
   symmetrically: a newly relied-on provider Unique promise requires an
   additional positive expected TypeDef/Import when the original stub lacks
   it, not overwriting that stub or keeping the fact only in a host cache.
   Copy or unknown actuals do not satisfy retained Unique; equal layout alone
   supplies neither guarantee. Unique does not satisfy retained Copy.
   No bit changes nominal identity.

## Schema boundary and acceptance gates

Keep `(major,minor)=(1,0)`, all inherited row extents and the array layout
reserved word unchanged. A2 remains exactly
`trrm/1-draft-foreign-invocation-storage-fixed-arrays-2`, with mask `0x2F`;
Standard and other selected schemas do not gain `0x40`. A nongeneric A2
provider cannot satisfy an A3 nominal Unique reliance without an independently
specified positive carrier; a consumer expectation is not provider evidence.

[Draft versioning](../specs/representation/format-versioning.md) permits
retiring or deliberately replacing unpublished A3 without compatibility
readers. The distinct successor here avoids two meanings under the current
`-3` selection; it is not a stability claim about that draft name or number.
Any deliberate in-place replacement would instead have to supersede all
selection/composition owners together, never optionally reinterpret `-3`.
No fallback, byte/Feature sniffing or selector-only upgrade is selected.
Conversions select both complete schemas, obtain genuine positive facts,
remap affected declaration/type/import/layout/Module records and revalidate;
missing Copy or Cleanup presence cannot be converted into `0x40`.

Required acceptance gates; this proposal is not evidence that they pass.
Current support and qualification evidence belong in the implementation
inventory.

| Gate | Acceptance |
|---|---|
| Contract integration | Reconcile A3's composition/no-flag paragraphs with the new exact selection; integrate TypeDef flag/mask and Cleanup cross-row rules in IL metadata, canonical Unique evidence in logical records, and the schema-specific native-value manifest scope. Leave A2 unchanged. |
| Codec and isolation | Writer/reader/public/internal selectors agree on the complete successor. Check all four declaration-bit states, visibility/kinds/reserved bits and Cleanup compatibility; unchanged A2/Standard/flag-free A3 reject `0x40`. Missing effective Module requirements fail, including bodyless providers. |
| Actuals and storage | Unrelated synthetic no-Cleanup unique structs, including closed constructed actuals, satisfy domains 2/3 behind Copy pointer cells and phantom formals; fail domain 1 and directly stored array-element closure. Exercise both nullability domains, independently admitted nullable forms, Blittable requirements, unknown actuals and N=0. Existing `own<T>` cases remain valid under their own admission. |
| Producer truth | Source unique scalar-only declarations publish Unique without Cleanup. Ordinary declarations retain Copy and full-domain universal checks; false source promises fail production, while structurally consistent false binary promises are separate optional-audit cases. No dummy Cleanup is generated. |
| End-to-end retention | Standard production/loading and bound reconstruction/mandatory verification preserve positive Copy/Unique/unknown evidence rather than defaulting `IsUnique=false`. Reorder/re-emit/reload without source; preserve substitutions and original/actual provider identities through every existing persistence/lowering handoff that retains these types. |
| Independent binding | Positive expected Unique succeeds only against positive actual Unique; missing/Copy/different-origin replacements fail. Preserve additional stronger expectations and each Module's closure across packing/re-encoding. Reject unsupported ownership/cleanup facts instead of treating the new bit as their codec. |

These gates complete only this evidence amendment; owning-value execution,
general generic export and whole array/compiler-adoption acceptance remain
their independent work.
