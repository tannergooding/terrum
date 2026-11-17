# Type-only Span terms in private generic persistence

> [!IMPORTANT]
> **Unapproved candidate; provisional direction under the authorized
> missing-contract workflow.** This selects an implementation-private
> agreement, not normative approval, implemented support, format stability
> or independent private-format conformance.

## Decision and scope

Select **`terrum/private-semantic-108-generic-methods-span-1`**, abbreviated
**G108S**: the exact
[G107 schema](private-generic-method-persistence.md#exact-base-and-header),
including its `terrum/private-semantic-106-raw-union-return-coverage-2` base,
plus the Span directory and formal Write fact below. Use `TRRM` followed by
little-endian **i32 108**, bytes **`6C 00 00 00`** at offset 4.

G108S **supersedes the earlier G107 recommendation for this private
open-generic exchange**, including artifacts with no Span rows. Revision 107
is not reinterpreted or retained as an alias. An explicitly supported G107
decoder can remain separate; conversion decodes its exact schema and rebuilds
the destination records, with no invented Write requirement. Rewriting its
header is not conversion. Amending unpublished 107 would be permissible, but
a distinct revision makes the changed mandatory records unambiguous.

The new terms are admitted **only in generic type actuals and type defaults**,
including forwarded actuals and retained closed-instance origin attachments.
They can nest through the already represented G107 type constructors.
The one necessary constraint delta retains the accepted Write requirement
that justifies an inner qualified formal; it adds no general property codec.
All other G107 formal-domain, constructor, evidence and body restrictions
remain unchanged.

No source syntax, Standard TypeSpec/Feature/IL allocation, runtime Span value,
Span MethodSig/Body value position, foreign/GC execution, ABI or independent
value crossing is selected. In particular, this does **not** extend the
[stable-backing Span value candidate](span-stable-backing-value-profile.md)
or select its descriptor/Core binding. The G1 use of family `0x8A` in type
actuals/defaults is a semantic counterpart, not an embedded private encoding
or evidence of Span value support.

## Preserved owners

The [specification index](../specs/README.md) identifies the governing contracts:

| Owner | Obligation preserved |
|---|---|
| [Complete types](../specs/language/types-and-normalization.md), [grammar §3.2](../specs/grammar-spec.md#32-types-parameters-and-arguments), [whole-span initialization](../specs/type-families.md#whole-span-initialization) | Exact constructor, complete element, declaration-owned formal/ordinal, independent inner/outer Write and whole-range initialization; accepted normalization and formation, not layout equality. |
| [Dependencies](../specs/language/dependencies-and-views.md), [lifetimes](../specs/lifetimes.md) | Actual backing, source generations, roots/guards, retirement and broad versus backing-only relationships are not type arguments or acquired by serialization. |
| [Binding/replacement](../specs/representation/binding-and-replacement.md), [reader trust](../specs/representation/reader-trust.md) | Original expectations and directed satisfaction; producer semantic truth distinct from consumer safe decoding. |
| [Persistence](../specs/representation/persistence.md), [versioning](../specs/representation/format-versioning.md), [Modules](../specs/modules.md), [sets](../specs/module-sets.md), [graphs](../specs/native-graph.md) | Exact schema agreement, selected closure and source-free lowering distinct from unchanged native execution. Only `.trrm` is normative semantic interchange. |

This proposal replaces only G107's type-term exclusion of Span and its
intrinsic-property exclusion **for Write**. It changes no normative chapter
or existing proposal. The in-memory
[`BoundSpanTypeDefinition`](../../sources/Terrum.CodeAnalysis/Semantics/BoundSpanTypes.cs)
and [`CompilationReference`](../../sources/Terrum.CodeAnalysis/Semantics/CompilationReference.cs)
are reconstruction carriers, not identities or byte-schema authority.

## Exact private deltas

### Formal Write fact

In **every** G107 `GenericFormal` record, immediately after
`nullabilityDomain` and before `defaultKind`, insert:

```text
u8 requiresWritable                // exactly 0 or 1
```

`1` publishes the accepted `where T : write` requirement for this exact type
formal. `0` publishes no such requirement; do not infer it from a default,
Span use, placeholder or successful specialization. Keep `formalKind=1` and
ownership/nullability tags `1/1`. Write is independent of those domains and
conjoins with every retained operator requirement. Other property/domain
forms still refuse transport; `[Copy]` remains invalid.

Check explicit, inferred and defaulted actuals using their normalized
**immediate** access. A writable inner element, `mut` binding or writable
supplying place does not satisfy a readonly actual on its behalf.

### Span table and reference namespace

Immediately after G107's `operatorEvidence` table and before
`ordinaryFunctionCount`, insert one mandatory directory:

```text
i32 spanTypeCount
SpanTerm spanTypes[spanTypeCount]

SpanTerm:                          // exactly eight bytes
    i32 elementType
    u8 elementWrite                // exactly 0 or 1
    u8 spanWrite                   // exactly 0 or 1
    u8 wholeRangeInit              // exactly 0 or 1
    u8 reserved = 0
```

All remaining G107 fields, directory ordering, length-delimited `P` parsing,
expression 55, eight-byte `TypeActual` records and attachments are unchanged.
The directory exists even when its count is zero; it is not an optional
trailer or a native-profile discriminator.

For zero-based row `s`, assign private i32 type code **`-1_000_000 - s`**,
with `0 <= s < spanTypeCount <= 100_000`. This band is disjoint from G107's
checked/reference codes `-1-i` and formal codes `int.MinValue+i`; other
negative codes retain G107's rejection rule. Compute classifications and
indexes with checked wide arithmetic, never `Abs(int.MinValue)`.
The matching grammatical atom is **`span#s`**, using unsigned minimal decimal.
It is a type atom, not a substring to replace in arbitrary text.

`elementType` uses the same complete-type resolver: an inherited admitted
closed term, G107 formal leaf/reference/optional term, or another Span row.
A direct formal leaf resolves through `GenericFormal.ownerMethod` and
`ordinal`; neither its name nor a proof placeholder is serialized identity.
Traverse indirect children too. All reached formals must belong to the
enclosing declaration; defaults may reach only its earlier formals, while
open call actuals use the **caller** owner. Closed attachments contain none.
Nominal leaves retain their existing provider/definition identity.

The flags have exactly these meanings:

* `spanWrite` qualifies the Span's element-slot access, not descriptor
  rebinding or the stored element's independent authority.
* `elementWrite=1` applies symbolic `write T` only when `elementType` is a
  direct formal leaf with `requiresWritable=1`. Otherwise it is invalid.
  Closed or nested writable elements carry their complete qualification in
  `elementType`; their `elementWrite` is zero.
* `wholeRangeInit` qualifies this Span's whole element range. It is not an
  outer descriptor-init qualifier, initializedness assertion, per-element
  bitmap, output promise or standalone `init T`.

Thus `write Span<Span<int32>>` has outer `spanWrite=1` and a readonly child;
`Span<write Span<int32>>` has outer `spanWrite=0` and a writable child.
Nested initialization belongs to the corresponding row, not every ancestor.
There is no GC, pin, lifetime-owner or mutable-binding flag.

Substitution preserves the **complete** actual element, then applies the
recorded constructors and accepted normalization. Redundant `write T` under
that formal's Write guarantee has the same complete T: canonical writers
use `elementWrite=0` while retaining `requiresWritable=1`; readers accept
`1` as a valid noncanonical application and normalize it. This never strips
the substituted element's Write or moves it to `spanWrite`.
`Span<void>`, unjustified qualification and other invalid formation refuse;
an encodable term does not by itself satisfy a formal's Copy/non-null domain.

### Writers and safe consumption

Canonical writers normalize and intern equal Span terms in their exact
formal/nominal context, emit Span descendants before parents and use the
inherited G107 canonical declaration order. They retain every published
default/Write fact and every selected actual, including unused formals,
uncalled definitions and bodyless expectations. Convert proof placeholders
through explicit formal mappings, not synthetic names or fake Structure rows.

Consumers safely decode the complete typed closure before affected use:
validate exact selection/header, table and `P` extents, nonnegative counts,
checked `8*count`, Boolean/reserved bytes, all type-reference bands/ranges,
exclusive formal ownership, default ordering, lexical scope and supported
formation/binding. Reject cycles through Span/reference/optional edges without
expanding nominal definitions into fields. Accept valid acyclic row permutations,
redundant qualifications and repeated equal Span rows, normalizing them into
the host's canonical table; they are not alternate decoders.

Charge every encoded row, edge and repeated occurrence **before** deduplication
to G107's shared 4 MiB, 100,000-visit, depth-128 budget. Its other capacities
remain in force; nested terms, templates and embedded Contracts receive no
fresh budget. Preflight extents before allocating. Refusal returns no partial
reference/catalog or success-shaped unknown type.

Producers check authored generics under the accepted domain/formation rules
and publish truthful facts. A consumer checks newly selected actuals,
substitutions, bindings and supported representations, but need not re-prove
the producer's Body semantics or reconstruct source. A merger is responsible
for the correctness of its rewritten facts. Optional truth audits are separate;
checksums, profile labels and successful decoding are not semantic proof.

## Reconstruction, merging and provider binding

All producer/reference/consumer, reference-set and graph-library routes must
restore the same type graph and G107 implementation closure:

1. Resolve owned declaration identities and **separate expected-view
   identities** before interning formals. Correspond provider/expectation
   formals by kind and ordinal; names and physical row indexes are irrelevant.
2. Allocate destination owner mappings keyed by the input's exact declaration
   or expected view, not its local owner integer. For the current host carrier,
   remap nonnegative definition ordinals separately from imported owner
   ordinals; an imported owner `o` uses `-2-o`, while `(-1,-1)` denotes a
   nonformal immediate element. Validate/check arithmetic and capacities.
   Rebuild `FormalTypeOwnerOrdinal`, each direct element's owner/ordinal and
   its explicit placeholder mapping together.
3. Recursively remap complete element types and intern the resulting Span
   rows; record an input-row-to-destination-row map and rewrite **every**
   Span code/atom through it. A row whose immediate element is another Span
   still needs its descendant formal-owner remapping. Remap defaults, formal
   dependencies, call actuals, attachments and all inherited recursive types,
   contracts and bound-tree edges, not just the root Functions array.
4. Preserve separate expected and provider Span/formal contexts and their
   explicit correspondence. Installing a provider body must not mutate an
   expectation's owner and reinterpret its original defaults. A closed
   instance replaces formal leaves with complete actuals and interns new
   rows; it does not globally replace matching negative integers.

Two independently decoded references can both have Span row zero, imported
owner zero and formal ordinal zero. Those integers must not collide after
merge. Equal closed terms may share rows after exact nominal binding;
open terms from different views cannot share merely because they select the
same provider or have equal spelling. Same-owned-origin copies can share only
after G107's complete agreement checks. Preserve every expectation and alias
equality; a diamond does not choose whichever provider is visited first.

`CompilationReference.SpanTypes`, imported signatures/formals/defaults and
immutable implementation handles must describe one consistent destination
context. Apply these mappings at every reference-set entry point, including
independently admitted G1 reference objects using the shared in-memory carrier.
Each input still selects its own decoder; this grants no automatic G1/private
conversion, standard payload embedding or packing beyond G108S's subset.

Bind every retained provider/key/kind and original view, including metadata-only
edges. Compare complete nested types after formal correspondence and accepted
normalization. A provider cannot add Write to an unconstrained expected formal.
Removing that caller requirement can satisfy a Write-constrained expectation
only while all old-domain types, defaults, other requirements and guarantees
remain satisfied. No default replay, overload search, layout/name match or
weakest-view merging is allowed. Retain any stronger provider fact actually
used; replacement invalidates affected specialization caches under G107's
complete provider/schema/rule/target/runtime keys.

## Independent route boundaries and lowering

| Route | Selected obligation / exclusion |
|---|---|
| Private `.trm` producer, consumer and references | Publish open definitions/defaults and bodyless original views with the new terms. A signature-only reference cannot supply an independently instantiable implementation; a closed cache cannot replace the open provider. |
| Private `.trmn`, `.tnset`, `.trng` | Keep TNPK/TNSP/TNGP outer-version-3 layouts under exact **outer-3/G108S** agreement for embedded semantic Contracts. Rebuild lengths, offsets, hashes and semantic provider closure. Outer version 3 alone does not select G108S; generic/type-only edges need no fabricated machine import. |
| Semantic link / source-free root lowering | Bind the retained provider closure, substitute recursively and close supported instances without source, syntax, inference/default replay or a specialized export-name protocol. Preserve origin attachments, expectations, access principals, ordinary dependencies and cleanup obligations. |
| Unchanged images / graph JIT | Existing complete native admission remains required. Graph root regeneration does not regenerate provider DLLs; incompatible retained bindings cannot trigger implicit JIT, patching or source recompilation. |
| Standard `.trrm` | Its independently selected G1 type-only terms and standard schema remain separate. This proposal assigns no Standard row, family, Feature, opcode or Body form. |

Before emitting or consuming a selected closed instance, reject any new Span
term in a MethodSig value parameter/result, Body value/place/map or required
value layout, including a Span hidden by substitution of bare T or inside a
reference, optional or aggregate. Type-only actual/default traversal is not
value support. Scalar-only code may retain unused Span actuals and forward
them exclusively to further generic type slots; it needs no Span descriptor.
Inherited TokenSpan or lexical buffer support is not a fallback identity.

A term's declaration owner is not a backing owner. No type row manufactures
source storage, stability, liveness, a root/guard, support retain or whole-range
publication. Existing admitted reference results still preserve declaration
Dependency source ordinals, once-only argument mapping and every backing,
access and cleanup bound under G107; no Span value relationship is inferred.
Independent precompiled Span crossings and the stable-backing candidate's
foreign/GC/init-execution exclusions remain untouched.

## Required conformance cases and remaining decisions

These are future implementation cases, **not executed validation**. Use public
compiler APIs and independently encoded fixtures, with provider source
unavailable to consumers; writer/reader round trips alone are insufficient.

| Group | Required cases and outcomes |
|---|---|
| Positive type-only use | Independent scalar-only consumer selects nested readonly/writable Span actuals absent from the producer; unused formals and fully explicit defaulted actuals survive save/load and source-free lowering. No Span value is created. |
| Defaults and Write | Earlier-formal nested Span defaults, complete qualified actuals, Write-constrained element formals and normalization of redundant inner Write retain exact owner/ordinal and immediate access. Explicit actuals override defaults without rewriting the retained default. |
| Qualifier axes | Distinguish outer Write from a writable nested child; retain whole-range-init at each exact row without claiming runtime availability. Exercise valid type-only rows separately from formal-domain or value-use refusal. |
| Cross-provider collisions | Merge two independently decoded providers with row/owner/formal zero, different defaults/qualifiers and same labels. Reverse provider order; combine separately selected private and G1 references only through their admitted carriers. No capture, wrong default or lost provider identity. |
| Graphs and copies | Three-provider forwarding chain and same-origin diamond, nested caller-formal actuals, unused metadata-only edges and bodyless consumer expectations. Preserve each original view and implementation closure; conflicting selections reject. |
| Re-encoding | Independently permute Span rows, method/formal ranges and inherited nested type rows; include repeated equal terms and strip diagnostic source data. Rebuild all binary/textual references, host owner ordinals, embedded extents and hashes. Reopen producer, consumer, set and graph references source-free. |
| Replacement | Renamed/reordered compatible formals and changed private body pass complete binding; inner/outer/init mismatch, swapped formal ordinals, changed default, added Write, missing provider or lost alias/stronger fact reject. Revalidate caches; do not replay new defaults. |
| Negative semantics/support | Reject unjustified `write T`, readonly actual for a Write formal, `Span<void>`, self/forward/out-of-scope defaults, foreign-owner leaves and unsupported G107 domains/constructors. Rejected declarations cannot be transported after dropping their unused requirements. |
| Value exclusion | Reject direct and substituted Span value signatures, locals/results/maps and nested value-bearing wrappers, even in unsafe code or with the stable-backing candidate selected. Do not alias them to TokenSpan/buffer intrinsics. |
| Malformed input | Truncated directory/row/Contract, negative/oversized count, bad flags/reserved bytes, unknown code bands, dangling/wrong-table references, cyclic mixed type graphs, overlapping formal ranges and invalid owner/ordinal mapping reject safely without decoder retry. |
| Boundaries | Zero Span count; first/last actual row; adjacent/outside code-band endpoints and int.MinValue; formal ordinal 0/7 and refusal beyond eight; checked count/extent and imported-owner arithmetic; shared node/depth/artifact exhaustion including duplicate-heavy input. Do not promise that independently maximal counts fit the aggregate budget. |
| Route identity | G107 is not G108S; missing new fields, header-only conversion, an outer-3 package without exact inner agreement, or an unsupported Standard input rejects. Source-free scalar emission and supervised execution are separate future evidence, not Span value/ABI conformance. |

**No wire or remapping decision remains open within G108S.** Adoption and
implementation remain separate. Other G107 carriers and all Span value,
MethodSig/Body, backing/Core, GC/foreign and independent ABI/crossing decisions
remain outside this selection and require their own complete agreements.
