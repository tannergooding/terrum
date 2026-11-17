# Standard TRRM generic TypeDef source closure

> [!IMPORTANT]
> **Unapproved proposal; provisional implementation direction only under the
> user-authorized missing-contract workflow.** This selects a checked
> declaration handoff and bounded producer applicability, not normative
> adoption, implementation support or runtime qualification.

## Finding and decision

Recommend a **compiler-owned, immutable open-TypeDef declaration projection**
associated with the existing nominal definition identity. Retain its complete
formal, field, declared-layout and dependency graph before executable instance
layout. Use that projection, or an already checked artifact declaration graph,
to emit exact constructed TypeSpecs and their required definition/import
closure. Do not reconstruct an open definition from observed specializations.

There are two distinct gaps:

* **Missing producer decision:** the source handoff for ordinary generic
  struct/class definitions, and the applicability of explicit TypeDef inputs
  to G1 generic structs. The existing source maps select generic methods,
  interfaces, or CG1 enum/Transparent-wrapper domains; the C1 class source
  proposal selects ordinary nongeneric classes. None selects this handoff.
* **Missing code, not another decision:** complete type identity, formal
  ownership, TypeSpec `05`, declared Layout rows, expected/provider comparison
  and contextual import/remapping obligations already have owners. G1
  emission of a source signature using a checked imported generic definition
  does not first require source publication of that provider's definition.

This proposal completes only the first boundary and applies the existing
rules to its output. It does not redefine nominal equality, approve general
generic export/execution, or make generic class source publication a G1
operation.

### Authority and source evidence

No accepted text is superseded. The
[specification index](../specs/README.md) leads to these preserved owners:

| Owner | Existing decision |
|---|---|
| [Declaration grammar](../specs/grammar-spec.md#31-source-file-structure-and-declarations), [generic arguments/defaults](../specs/grammar-spec.md#32-types-parameters-and-arguments) | Class/struct generic declarations, mixed formal order, complete arity, earlier-only defaults and source checking already exist as target contracts. |
| [Complete types](../specs/language/types-and-normalization.md), [constant generics](../specs/language/constant-generics.md) | Definition provenance, owner/kind/ordinal formal identity, exact complete arguments, forward substitution and canonical typed constant identity; no equality by carrier or spelling. |
| [Metadata](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics), [binding/replacement](../specs/representation/binding-and-replacement.md) | Existing declaration/TypeSpec rows, explicit keys, full retained expectations, contextual comparison and meaning-preserving remapping. |
| [Consumer verification](../specs/representation/reader-trust.md), [admission](../specs/representation/requirements-and-admission.md), [format versioning](../specs/representation/format-versioning.md) | Producer truth, mandatory safe-consumption/support/binding checks, retained per-Module requirements and exact out-of-band schema agreement. |

The unapproved [G1 owner](standard-trrm-generic-method-call-body-encoding.md)
already specifies complete generic declarations and supported argument/default
forms. Its [source-input owner](standard-trrm-source-generic-method-export-inputs.md)
and [interface extension](standard-trrm-g1-interface-declaration-exports.md)
do not select ordinary generic struct/class publication.
[A3](standard-trrm-closed-constructed-array-elements.md) selects bounded
generic **storage**, not this G1 source handoff or a G1 Copy flag.

The [C1 declaration carrier](standard-trrm-c1-class-declaration-carrier.md)
already chooses declaration data separate from executable `BoundClass`;
reuse that separation and existing nominal identities.
[C1M1](standard-trrm-c1-class-declaration-mutability.md) owns class declaration
mutability. [C1 source publication](standard-trrm-c1-class-source-publication.md)
requires real constructor/member closure under its constructor successor,
not a field-only complete class publication. Those decisions are unchanged.

The current G1 reader can consume checked local/imported TypeDefs, but source
emission does not retain a complete open generic declaration, and the class
binder does not yet support generic class declarations. These are
implementation gaps, not decisions to remove the already accepted generic
declaration grammar; current support limits are indexed in
[`modules.md`](../implementation/modules.md#definitions-and-scope).

## Exact scope and schema gates

Use the existing explicit **`trrm/1-draft-generic-method-calls-1`** agreement,
including its separately proposed [Write amendment](standard-trrm-generic-write-requirements.md).
Add no schema identity, section, flag, Feature, token, opcode, Body record,
version pair or compatibility alias. The source applicability extension below
is a typed library-producer decision, not a new CLI mapping grammar.
G1L1, CG1, A3, C1/C1M1 and private formats remain independently selected;
links or equal `(1,0)` bytes do not compose them.

In particular, G1 has no C1M1 `DeclaredClassMutable` fact. An opaque imported
G1 class identity retains **unknown** declaration mutability, not `false`.
This proposal can preserve such a checked identity in an already admitted
signature; it cannot publish or bind a complete fresh generic class declaration
under G1, or authorize a use requiring the missing fact. Capture may retain
generic class facts for supported consumers, but does not make them G1 output.

Complete fresh generic class publication needs a mutability-capable exact
agreement and the required member/constructor closure. C1M1 supplies the
Boolean carrier, not generic-class publication by itself; its existing
type-only route and the constructor successor's nongeneric scope are not
extended here. No new class schema or constructor decision is necessary for
G1 imported-signature emission or the generic struct route below.

Retain requirements from the selected catalog and each using Module's actual
closure. A generic TypeDef alone does not require G1's method-call Feature
23/1. SF1 values, Write constraints and layout-dependent uses retain their
independent requirements; the provider's manifest never substitutes for the
using Module's. Unsupported witness, associated, constant-execution, ownership
or representation facts refuse affected output rather than disappear.

## Checked declaration capture

Capture a declaration graph, not caller-authored metadata or a new nominal
identity namespace. A source declaration uses a compiler-owned local token
until output Module/TypeDef assignment; all uses and any executable instance
remain associated with that one definition.

1. Attach capture to the reference-identical checked `BoundProgram`, its ordered
   immutable source snapshots, exact primary declaration/input ordinal/span
   and actual partial parts in accepted composition order. Retain each part's
   actual source ownership. Only successful checking over the declared formal
   domain supplies authority; parsing or one successful closed instance does
   not. Unsupported source checking remains a failure, not an empty capture.
2. Resolve and freeze capture before emission rewrites. A hand-built program,
   `with` copy, rebind or rewrite cannot acquire authority from matching
   names/spans. A changed program requires compiler-owned recapture; supported
   internal transformations carry explicit declaration correspondences.
   Loaded/imported declarations use their checked artifact origins instead,
   never fabricated source anchors.
3. Retain namespace/name, nominal kind, complete access and declared
   mutability/ownership/static roles, including facts unavailable in the
   selected output schema. Unavailable is distinct from absent or false;
   required unrepresentable facts block that output.
4. Retain the following complete, typed data, including unused/phantom formals,
   private fields and dependencies. A presence Boolean or unscoped row number
   is insufficient.

| Data | Required capture and G1 representation |
|---|---|
| Ordered formals | One declaration-order vector of all `K` formals, with owner TypeDef and contiguous total ordinals `0..K-1`. Type-only and constant-only helper indexes do not replace these ordinals. |
| Type formal | `formalKind=1`, ownership `1=default Copy-capable`, `2=unique-only`, `3=default plus unique`; nullability `1=non-null`, `2=nullable allowed`; zero constant-domain field. Preserve the full supported Constraint range. These domains do not establish native storage or positive A2/A3 ownership evidence. |
| Constant formal | `formalKind=2`, both domain tags zero, exact nonzero constant-domain TypeSpec and no type constraints. G1 retains its ten fixed-integer/Boolean domain subset; enum/wrapper domains and open constant terms do not join from CG1. |
| Defaults | Zero means genuinely no default. Otherwise retain the complete type term or typed constant source, its original declaration context and earlier-formal dependencies. Type defaults may refer only to earlier owner formals. G1 constant defaults use admitted tag-1 SF1 values; symbolic defaults, ConstantSource tags 2/3 and unencodable defaults refuse, never become zero. Required/defaulted formals may interleave. |
| Constraints | Retain exact owner and kind-directed payload: `1=interface instance`, `2=intrinsic property`, `3=associated relation`, with flags zero. Property codes `1=Unique`, `2=Write`, `3=Blittable` are inline codes, not row references. Preserve complete typed interface/relation dependencies where their codecs are supported; otherwise refuse. Do not synthesize Copy/Sized/Pin constraints or erase unsupported constraints. |
| Fields and layouts | Retain every declared FieldDef identity/name/access/static/mutable role and complete open type. Retain exactly one default Layout and each complete exact-profile variant, kind, Blittable commitment, declared alignment/pack and ordered instance-field membership. Statics are excluded from LayoutField lists, not from retained declarations. No sparse variant or omitted private field stands for the full declaration. |

Capture nominal validity, public-operation, ownership, member/witness and
other required contract edges under their existing owners as well. This is
not permission to advertise a complete class through fields alone. If a
retained independent-use promise requires an unavailable member/Body/codec,
decline that publication; do not strip it, fabricate a Body or substitute a
closed specialization.

Declared Layout inputs are not computed size, alignment, field offsets,
object headers or collector descriptors. Do not read those facts back from
`BoundStruct`/`BoundClass` as an open declaration recipe. Native layout and
generic instance lowering remain separate use gates.

## Source TypeDef inputs

Extend only the typed G1 producer's existing `TrRmSourceTypeExport` list to
public, namespace-level, fresh source **generic struct definitions** whose
complete retained contracts fit G1. Use its explicit
`(Namespace, Name, GenericArity)` selector
against the checked declaration capture: exact decoded names, nonnegative
arity counting all formals, and exactly one local declaration. This
intentionally completes the earlier G1 ordinary-TypeDef input exclusion;
it does not alter CG1's enum/wrapper selection or the interface map.
It adds no nested-owner or file-local selector.

Capture lists and resolve selectors against the original checked program
before rewriting. Reject imported/loaded expectations, aliases,
specializations, wrong kinds and stale or missing source authority. The
enum/member-specific mapping arrays must be default for this type-only
generic struct route; it supplies no independent FieldDef/member exports.

Each key is explicit nonempty, NUL-free scalar text under Standard String
capacity and ordinal decoded equality. Require Module-wide uniqueness across
all export kinds. Distinct keys may alias one captured definition without
duplicating it; repeated keys reject. Public visibility alone generates no
type key. Unmapped required local definitions remain unexported closure, not
discarded declarations; mappings are not a trim list.

The existing class source selector remains owned by C1; this name/arity map
does not bypass it or add generic class publication to G1. Existing CLI
versions 1/2/3 and their exact schema pairings remain unchanged. No CLI
transport or implicit key-generation policy is selected.

## Emission, imports and comparison

Emit local source tokens as definitions in the explicitly supplied output
Module. Never export one TypeDef per observed closed instance or restamp an
imported origin as local. Checked artifact inputs retain immutable bytes,
exact schema, logical Module, TypeDef, publication keys, original expected
views, actual provider bindings and independently relied-on facts.

Compute the full retained declaration/type closure before returning output:

* Follow formal domains/defaults/constraints, fields, all Layout variants and
  every required contract/member edge, including uncalled retained content.
  Bind provider-owned Imports in their own using Modules, not the root's
  context. Source/debug data is unnecessary for an artifact declaration.
* An external edge uses its exact checked ModuleRef/Import/key and expected
  declaration. A newly external edge needs an explicit supported public key
  selection, or retained direct definitions in a checked packed provider
  closure; otherwise refuse. Do not invent private exports, choose an alias
  by row order, re-export proxies or write an external row as a local row.
* Resolve Module/revision/key/kind and owner before contract comparison.
  Compare declaration namespace/name/access and every retained schema-defined
  declaration fact in that selected nominal correspondence.
  Preserve every original expectation and alias equality independently,
  including after a prior successful resolution. Actual facts never replace
  the expected view; several expectations do not merge to the weakest one.
* Map formals by corresponding owner, kind and total ordinal, not name or
  raw row. Compare retained formation domains, default presence and normalized
  default terms/typed values under that mapping. Preserve nominal formation
  constraints under the existing binding relation; a callable's supported
  constraint relaxation is not a blanket rule for nominal TypeDefs.
* Compare fields by declaration correspondence, exact names/access/roles and
  normalized complete types. Compare the entire retained Layout variant set
  by default versus exact profile identity/revision, kind/flags/alignment/pack
  and ordered corresponding fields. Formal names or physical row placement
  may differ; equal widths, field offsets or carrier types do not suffice.

Complete types retain every nested argument and inner/outer qualification.
Comparison uses each side's artifact, using Module and formal/substitution
context; even a pointer child remains a type-identity dependency. Reuse the
finite contextual comparison worklist, closing finite nominal recursion only
after all outgoing obligations succeed. Malformed structural/storage cycles,
growing substitutions and explicit resource refusal never count as equality.
Metadata comparison requires no class object sizing or native instance ABI.
Consumers check schema support, framing, bounds, owners/ranges, references and
bindings before affected use; they do not semantically re-prove the producer's
authored Body facts or infer declaration truth from physical layout.

## Row and index reconstruction

Decode and rebuild graphs, not byte-substitute references or pass through an
old snapshot as the new declaration. In particular:

| Edge | Required reconstruction |
|---|---|
| Module/TypeDef/Import/Export | Rebuild partitions and owned ranges, map local tokens and checked origins, preserve keys/aliases and each original expectation after Import-to-direct resolution. Packing preserves provider Module ownership. |
| GenericParam/Constraint | Rebuild exclusive formal/constraint ranges, owner DefIds and total ordinals. Remap type domains/defaults and reference-bearing constraint variants in their own namespaces; leave inline property codes unchanged. |
| TypeSpec `02/03/04/05` | Map definition/Import/formal references separately. Form `05` uses an earlier nominal `02/03` anchor row, exact extent `6 + 4*K` and exactly one argument per formal: complete TypeSpecRef for kind 1, ConstantSource row for kind 2. Preserve qualification placement and lexical formal scope; no omitted/default/inference sentinel or Body ConstantFormal tag fits these slots. |
| Nested types and constants | Rebuild complete wrapper/constructed graphs in dependency order, preserving eligible inline F2/F3 forms and checked constant-domain/value identity. Remap ConstantSource/frozen-value/Blob relationships; scalar payload bytes are not reference slots. |
| Fields/Layout/contracts | Rebuild owners, complete contiguous ranges, variant/profile selections and LayoutField membership, plus every supported contract/member dependency and its context. Retain declared inputs, not computed geometry. |
| Signature/Body/section indexes | Remap each retained MethodSig and TypeMap use, formal reference and supported Body map/record edge in its own namespace; rebuild offsets/counts/extents and applicable SourceMap slots or omit optional debug data legally. This grants no additional Body operation. |

Clone structural rows shared by contexts whose resolved meanings would differ.
Do not treat TypeSpec rows, DefIds, Import tokens, GenericParam rows or
Body-local indexes as interchangeable integers. Verify the rebuilt complete
artifact and bindings before returning bytes or replacing output.
Malformed serialized structure/binding, invalid caller mappings, unsupported
complete use and scoped resource refusal retain their existing distinct
failure paths; none returns a partial successful artifact.

## Focused acceptance and remaining boundaries

These are future public-API cases, not test results:

| Case | Required outcome |
|---|---|
| Checked source capture | Publish unrelated supported generic structs with unused formals, mixed formal order and interleaved defaults; retain complete open fields/Layout variants without instantiating each type. Wrong arity, stale/copied programs, imported targets and missing default terms fail. Source errors remain source errors. |
| Formal/type identity | Distinguish TypeDef ordinal zero from MethodDef ordinal zero and same-named unrelated owners. Renamed corresponding formals and equivalent indexed/inline encodings agree; changed kind/domain/default, nested nominal origin or qualification fails. Unencodable symbolic constants/constraints never disappear. |
| Import closure | Emit a source signature using exactly selected imported constructed types with source/debug absent and the provider non-first. Exercise several using Modules, aliases, defaults/private-field dependencies and provider-owned Imports; wrong revision/key/kind, incomplete closure and incompatible replacement fail. |
| Remapping | Permute definition/formal/constraint/TypeSpec/field/Layout/import rows with valid ranges and dependency order, pack Imports to direct definitions, re-emit and reconsume. Preserve all original views and same-definition alias equalities, not just actual-provider snapshots. |
| Scope and refusal | Genuine empty declarations remain distinct from unavailable facts. G1 class mutability stays unknown; fresh complete generic class publication, missing member closure, unsupported storage/runtime/Body uses and sibling-schema inputs refuse without schema inference or fabricated layout. |

The capture authority and typed G1 generic struct input extension are the only
new recommendations. There is no unresolved choice inside that boundary.
Fresh generic class source publication still needs an explicitly selected
generic-capable class publication agreement under the existing class owners;
this document selects none. Class runtime/object ABI, constructor/member
execution, generic-class instance lowering, richer constraint/value codecs,
CLI transport and normative adoption remain separate. No support inventory,
conformance status, test or implementation change follows from this proposal.
