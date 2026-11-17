# Standard TRRM source type and generic-method export inputs

> [!IMPORTANT]
> **Unapproved proposal; provisional direction only under the user's
> authorized missing-contract workflow.** This document selects producer
> inputs, not formal approval, implementation evidence or a support claim.

## Decision, scope and owners

Recommend a **typed, exact-program source mapping API plus one strict
mapping file** on `compile-trrm`:

```text
--trrm-source-generic-method-exports <path>
```

The **G1 method-only version-1** grammar carries generic-method mappings. The
[CG1-only version-2 extension](#cg1-combined-source-producer-inputs) carries
`typeExports` and `genericMethodExports` against **one** ordered source
snapshot set. Method assignments use source-input ordinal and complete
UTF-16 declaration span; TypeDef assignments use exact namespace/name/arity
and complete enum-member associations. Every key is explicit producer input.
Neither names, source positions nor snapshots generate keys.

For the label-preserving route, the unapproved
[G1/C1 label proposal](standard-trrm-g1-interface-parameter-labels.md)
supersedes only the original G1 artifact-schema pairing: version 1 selects
G1L1, while the companion interface map uses version 3/G1L1. The grammar,
source anchors and snapshot rules remain owned here and by the companion
interface-input proposal. Original G1 is not an alias or fallback; CG1
version 2 remains unchanged.

This closes the producer-input gaps in
[G1's public generic-definition publication](standard-trrm-generic-method-call-body-encoding.md#providers-source-free-remapping-and-lowering)
and [CG1's nominal constant domains](standard-trrm-constant-generic-methods.md).
Do not change CG1's exact wire selection, records, Feature catalog, actuals,
evidence, binding or source-free lowering. CG1 remains exactly G1 plus its
Standard enum delta and CG1 wire amendments; it does not transitively select
G1L1. The distinct G1L1 table and Feature are owned by the linked label
proposal; no source syntax, overload rule, provider-selection API, private
persistence or native execution route is added here.

No accepted text is superseded. The CG1 section explicitly extends the
applicability of existing source TypeDef/member inputs to the six-argument
CG1 route; their older schema-specific behavior remains unchanged. Preserve
[exact-schema agreement](../specs/representation/format-versioning.md),
[metadata identity/key rules](../specs/il-metadata.md#identity-and-contract-invariants),
[selected identity and alias binding](../specs/representation/binding-and-replacement.md#1-scope-and-selected-identities),
[producer truth and consumer checking](../specs/representation/reader-trust.md)
and [source snapshots/coordinates](../specs/language/source-input.md).
The [source enum CLI transport](standard-trrm-source-enum-cli-transport.md)
remains NominalEnumsDraft-only. Its mapping machinery is reusable, not
authority to combine that option with G1 or CG1.

### Source evidence

These are source observations, not newly executed qualification:

| Evidence | Relevant boundary |
|---|---|
| [`BoundProgram` / `BoundGenericMethodDefinition`](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs) | `GenericMethodDefinitions` retains `DefinitionOrdinal`, `Name`, `IsPublic`, caller-unsafety, `FunctionIndex`, `SourceInputOrdinal`, `Span`, formals and a checked `ProofProgram`. The collection and definition type are internal, not an export configuration API. |
| [`ValidateGenericTemplates`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.StaticProtocols.cs) | Assigns definition ordinals after sorting by input ordinal and declaration start; captures the defining tree's ordinal and complete `template.Declaration.Span`, including unused templates. A proof `FunctionIndex` indexes that proof program, not a durable public declaration. The current name-keyed template table does not establish general overload support. |
| [`EmitStandard` / `TrRmSourceTypeExport`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.cs) | Four- and five-argument entry points select exact schemas and accept explicit source TypeDef/member keys; neither accepts generic-method assignments or the proposed GenericArity selector. The TypeDef overload snapshots submitted records. |
| [`enum mapping reader`](../../sources/terrumc/TrRmSourceEnumExportsReader.cs), [`TRRM CLI handler`](../../sources/terrumc/Program.TrRmExecution.cs) | Strict versioned JSON, exact option/schema gating, configuration admission before source acquisition, source-order capture, typed emitter handoff and existing experimental diagnostics provide reusable mechanisms. |
| [`PathsAlias`](../../sources/terrumc/Program.PathIdentity.cs), [`WriteArtifact`](../../sources/terrumc/Program.Compilation.cs), [`enum CLI cases`](../../tests/Terrum.CodeAnalysis.Tests/Cli/CliTests.TrRmExecution.cs) | Existing path checks account for normalized paths, parent links and Windows file identities; publication stages a complete artifact beside the destination before replacement. Existing enum cases establish test patterns, not generic-method coverage. |
| [Bounded-generic inventory](../implementation.md#bounded-generic-functions) | Checked source proof programs do not establish public/open Standard interchange or source-free generic execution. |

## Alternatives

| Option | Assessment |
|---|---|
| API only, with the typed mapping below | Smallest library-only amendment. It supplies explicit keys without a parser, but leaves standalone `compile-trrm` unable to produce the requested publications. A host-specific wrapper would still invent the persistent selection/failure contract. Not recommended for this objective. |
| **Typed API plus one exact-version CLI file** | **Selected.** One semantic mapping model, one argv operand and explicit snapshot checks cover library and CLI producers. More configuration than enum name selection is justified by same-name/overload disambiguation and stale positional selectors. |
| Method names/signature strings, definition ordinals or repeated inline flags | Method names alone are ambiguous; signature text adds a second type/owner/qualification grammar. Definition ordinals change with input ordering. Inline groups still need snapshot and alias framing. None is needed when exact checked method spans already exist. TypeDef selectors use the separately defined exact namespace/name/arity tuple. |

## Candidate API

Add these proposed public declarations in `Terrum.CodeAnalysis.Modules`:

```csharp
public sealed record TrRmSourceGenericMethodExport(
    BoundProgram Program,
    int SourceInputOrdinal,
    TextSpan DeclarationSpan,
    string ExportKey);

public static byte[] EmitStandard(
    BoundProgram program,
    string moduleIdentity,
    string schemaIdentity,
    uint moduleTargetRequirement,
    IReadOnlyList<TrRmSourceTypeExport> typeExports,
    IReadOnlyList<TrRmSourceGenericMethodExport> genericMethodExports);
```

`TextSpan` is the existing `Terrum.CodeAnalysis.Text.TextSpan`. The
`EmitStandard` declaration is an overload on `TrRmExecutableCompiler`.
Retain the existing four- and five-argument overloads unchanged; using a sixth
argument avoids introducing a second five-argument list overload and ambiguous
existing null calls. Pass an explicit empty `typeExports` list when none is
required. Under G1, nonempty TypeDef assignments still require independently
supported G1 publication; otherwise reject them without importing another
schema's type support. The CG1 section below supplies that selection's
explicit nominal-source applicability and GenericArity completion.

The new overload requires exact ordinal equality with either the G1 or CG1
schema identity, even for empty lists. These are separate exact selections,
not aliases or an inferred composition. It accepts library and executable
source programs subject to the selected carrier's own gates. It does not
infer a schema, target, runtime policy, Module identity or export key.

### Exact declaration and snapshot association

These rules apply to `TrRmSourceGenericMethodExport` records; CG1 TypeDef
selectors and their compiler-owned capture are specified below.

* Each record's `Program` must be reference-identical to the supplied checked
  `program`, normally the final `Compilation.Program`. Structural record
  equality is insufficient. A rebind, `with` copy, reordering or rewritten
  program requires explicit recapture; never rebase by name or array position.
* `SourceInputOrdinal` is zero-based in the ordered input trees used to bind
  that program, not a path sort, DefinitionOrdinal, Function index or TRRM row.
  Require it in `[0, program.SourceInputCount)`.
* `DeclarationSpan` is the exact full declaration node's `Span` retained by
  `BoundGenericMethodDefinition`, not its name, body or trivia-inclusive extent.
  Require nonnegative start, positive length, checked nonoverflowing end and
  exactly one matching generic definition with that input ordinal and span.
  A containing/overlapping span or a span in the separate proof program fails.
* Resolve the match to that checked definition before assigning its key.
  Same simple/qualified names, generic arity or overload shape never select
  another definition. Distinct overloads, where source binding supports them,
  have distinct declaration spans; identical offsets in different inputs have
  distinct input ordinals. This transport does not legalize rejected overloads.
* Only a public, locally source-authored open generic definition is eligible.
  Reject private/internal, nongeneric, synthetic closed-specialization,
  imported-expectation and loaded-artifact targets. Mapping cannot promote
  access, create a definition, retarget a call or rekey a retained provider.
  Preserve binder provenance for this check; a loader must not manufacture
  source eligibility from diagnostic spans or a coincident source ordinal.

Callers can obtain the ordinal and declaration `Span` from their existing
ordered syntax trees; no public exposure of internal proof IR or new
name/signature discovery API is required. The exact-program anchor scopes the
API record to one checked `BoundProgram` snapshot. It is not a durable nominal
identity or permission to trust a malformed bound program.

Snapshot both submitted lists before emission. Reject null lists, entries,
program anchors or keys; reject invalid spans/selectors and key text. Do not
mutate the program, proof programs or caller collections. Resolve assignments
against the original checked program before any internal rewrite; carry the
resolved declaration association through that rewrite, not a new positional
lookup against rewritten arrays.

## Coverage, aliases and keys

Retain every fresh public generic method definition in the checked source program
and require **at least one assignment for each**, including uncalled methods
and unused formals/defaults/constraints. Do not publish only observed closed
instances, or silently trim a public declaration to avoid a missing key or
unsupported selected-carrier contract. Private implementation helpers need
no export key but retain their ordinary dependency/admission obligations.

One record assigns one key. Multiple records may intentionally select the
same definition with **distinct keys**: these are ordinary Export aliases,
not duplicate MethodDefs, proof programs or specializations. Repeating a key
is invalid even for the same selector. Require Module-wide ordinal key
uniqueness across generic aliases, ordinary callable exports, TypeDef exports
and every retained publication; never resolve a collision by input order.
The map cannot append aliases to imported expectations or existing provider
publications.

Keys are nonempty, NUL-free, well-formed scalar text. Preserve decoded text
ordinally without trimming, Unicode normalization, case folding or identifier
interpretation; whitespace and differently normalized Unicode remain distinct.
Apply Standard String capacity and the selected UTF-8 writer's
`0xFF_FFFF`-byte per-key limit, not a private 256-byte cap. Checked aggregate
capacity/resource failure never truncates text or partially publishes a map.

Validate every supplied assignment, not only reachable declarations. With
G1, omission of the new list, or an explicit empty list, supplies **no keys**;
if fresh public generic definitions exist, fail emission for incomplete
publication. With none, an empty list adds no declaration or Feature by
itself. G1 still derives requirements from retained contents, not option
presence. CG1 applies the same method-coverage rule plus the nominal coverage
below; TypeDef-only CG1 content does not acquire Feature 23 from this option
or an empty generic-method list. Existing source-free aliases and original
expectations remain owned by the selected carrier's re-emission/binding rules.

## Exact CLI selection and configuration

Accept `--trrm-source-generic-method-exports <path>` only on `compile-trrm`,
once, after its required source operand and before a final optional `--json`.
Consume exactly one following argv element. Reject missing/empty operands,
`-`, dash-leading operands, repetition, altered casing, equals/short aliases
and other commands before configuration or source content is read.
Dash-leading filenames require an explicit relative or absolute path.

Presence requires explicit `--trrm-schema` selection of exactly one of:

| Exact artifact schema | Required mapping-file grammar |
|---|---|
| `trrm/1-draft-generic-method-calls-parameter-labels-1` | Version 1 below; root `sources` and method-only `exports` under the unapproved G1L1 pairing. |
| `trrm/1-draft-constant-generic-methods-1` | Version 2 below; root `sources`, `typeExports` and `genericMethodExports`. |

Reject baseline, Development, NominalEnumsDraft and every other exact schema,
even for empty maps. The file cannot select a schema. A grammar-version/
artifact-schema mismatch is a configuration error after file decoding, not
permission to retry the other grammar. The enum-export option remains
NominalEnumsDraft-only: reject combining it with this option under either
G1 or CG1 before configuration/source reads. No pair of old options implies
a composite schema.

Keep required `--identity`, output extension, fail-fast runtime, Windows x64
Module target requirement **1**, arithmetic selection and `--library` behavior
unchanged. Core manifests remain separate source-binding configuration; they
cannot fill in keys or grant selected-carrier representation/execution
support. Omission preserves existing command defaults and overload selection
for existing schemas; the explicitly selected new CG1 route uses empty lists
as specified below.

Resolve the mapping path once against the invocation working directory; read
one regular UTF-8 file, not stdin, a directory, URL or include tree. Admit all
selected configuration files in argv option order, before acquiring any
source, including stdin. Preserve existing Core-composition rules.

### Version-1 G1 method-only file

The complete shape is shown below. The digest/span illustrate fields, not a
validated source program or a generated key:

```json
{
  "schemaVersion": 1,
  "sources": [
    {
      "path": "provider.terrum",
      "snapshotSha256": "0000000000000000000000000000000000000000000000000000000000000000"
    }
  ],
  "exports": [
    {
      "sourceInputOrdinal": 0,
      "declarationStart": 0,
      "declarationLength": 64,
      "exportKey": "method.identity/v1"
    }
  ]
}
```

Every object has exactly the shown required properties; no nulls, unknown
properties, defaults or alternate forms are admitted. Object property order
is insignificant. Arrays retain order. `schemaVersion` is the number token
exactly `1`, not `1.0`, `1e0` or text; it versions this configuration grammar
only. This shape remains method-only even when its arrays are empty; on the
label-preserving route it pairs only with exact G1L1. Version 1 cannot carry
TypeDef assignments. Source
ordinal/start/length use unsigned decimal integer tokens without
fraction, exponent or sign, bounded by `Int32.MaxValue`; length must be
positive and start-plus-length must not overflow.

Use strict UTF-8 with at most one leading UTF-8 signature. Require one root
object followed only by JSON whitespace. Reject malformed UTF-8, unpaired
decoded surrogates, duplicate decoded property names at every depth (including
escape-equivalent names), comments, trailing commas/content and wrong JSON
types. Paths are nonempty scalar strings without NUL; export keys follow the
API rules. No file-extension requirement, environment expansion, discovery,
include, provider reference or inline/response-file alternative is selected.

### Source order and stale-map checks

`sources` asserts the **entire** source list, not just files containing exports.
Its count and order must match the required positional source followed by each
`--source` in argv occurrence order, under the existing source-count/stdin
gates. Do not sort, deduplicate or zip export entries to declarations.

For each `sources[i]`:

1. `path` asserts the already selected source operand at ordinal `i`; it never
   adds or reads an input. Resolve relative paths against the invocation working
   directory, not the mapping file's directory. Require the existing path-
   identity comparison to match that source file. `"-"` is permitted here
   only when that same ordinal is the command's stdin source.
2. `snapshotSha256` is exactly 64 lowercase ASCII hexadecimal digits encoding
   SHA-256 of that source's complete decoded immutable UTF-16 snapshot, with
   each code unit encoded little-endian, **no BOM, terminator or normalization**.
   Hash the captured syntax-tree text, not a reread file, path, source bytes,
   semantic tree or concatenation of files. A consumed UTF-8 signature is not
   part of the snapshot; CRLF, supplementary characters and interior U+FEFF are.
3. Validate count/path correspondence before source acquisition, then validate
   digests after strict source decoding and before binding. A stale digest
   rejects even if name, span or inferred signature happens to match.

Path assertions distinguish equal-content files; the ordered digest vector
detects edits in any input, including a file without mapped declarations. A BOM
change alone can leave a snapshot digest equal, but its actual current origin
map is still captured afresh. These checks do not authorize reuse of source
origins, binding, options, provider state or verification results.

After successful source binding, construct every method assignment with that
exact `Compilation.Program` and the supplied ordinal/span/key. Preserve entry
order without making it declaration order. With G1's version-1 file, pass an
empty TypeDef list and the captured generic list to the new overload; that
grammar carries no TypeDef assignments. With CG1's version-2 file, capture
both lists against that same program as specified below. Missing required
public-method assignments remain emitter failures.
The producer must update paths, snapshot assertions and spans when changing
inputs while deliberately retaining its chosen keys. No hash, ordinal, name
or span becomes an ExportKey automatically.

## Aliases, failures and output preservation

Before reading configuration/source content or acquiring output, preflight
the mapping file against every source file, selected Core/configuration file
and destination using existing path-identity checks, not string equality
alone. Reject an alias, including case/relative-path, link and Windows hard-
link aliases recognized by those checks. For this option also reject source
file operands aliasing one another; stdin has no filesystem path. Preserve
existing source/output and Core/configuration alias guards. Path-inspection
I/O failure is an input failure, not permission to assume distinct files.

Use existing experimental diagnostics, without assigning new codes:

| Failure | Required projection |
|---|---|
| Option spelling/arity/repetition/operand, wrong command or missing/wrong exact schema | `TRM2000`, exit 2, before configuration/source content reads. |
| Mapping I/O, strict decoding/JSON/version/schema-pair/types/text/capacity errors, source-list/path assertion or digest mismatch, invalid or duplicate assignments/keys | `TRM2001`, exit 2. Source-dependent mapping `ArgumentException` identifies `genericMethodExports`, or CG1's `typeExports`, according to the affected input; do not relabel unrelated program exceptions. |
| Mapping/configuration alias, or duplicate source-file alias introduced above | `TRM2002`, exit 2. Existing source/output argument failures retain their existing `TRM2000` projection. |
| Source input decoding/I/O failure | Existing source-input diagnostic and exit behavior; there is no snapshot or invented span for malformed byte input. |
| Syntax/binding error after snapshot admission | Existing source diagnostics, exit 1, before declaration-selector checks. |
| Missing assignments for public generic definitions or CG1's retained fresh nominal definitions; valid but unsupported selected-carrier declaration/body/dependency/target facts; invalid checked program | Existing `TRM5001`, exit 1. Do not classify these as malformed configuration or retry another schema. |
| Output publication failure | Existing output-error behavior; no success response/artifact claim. |

For the API, use `ArgumentNullException` for null top-level arguments and
`ArgumentException` naming `genericMethodExports` for malformed method mappings,
including null entries/anchors/keys, stale program anchors, invalid selectors,
ineligible targets and method-key collisions; CG1 type mappings and cross-list
collision attribution follow the rules below. Wrong exact schema identifies
`schemaIdentity`. Preserve existing target/TypeDef argument validation.
Missing required public assignments and unsupported selected-carrier use are
`NotSupportedException`, not an empty artifact. Validate the complete
submitted map before serialization; an independently invalid checked program
still fails its own verification.

All prepublication failures leave an existing destination byte-identical and
create no absent destination. Build and validate the complete artifact before
calling the existing same-directory staged `WriteArtifact` publication path.
Never open/truncate/delete the destination to parse configuration, bind source
or diagnose a missing key. Recheck input/configuration-versus-output aliases
immediately before publication; failure still precedes replacement.
Retain existing staged-file cleanup and output-failure handling. These are
ordinary CLI file-safety guarantees, not a new adversarial filesystem-
transaction or crash-durability protocol.

Configuration diagnostics identify the file and established entry/field,
without fabricating a source span. Preserve human and experimental `--json`
response shapes; this does not amend [`terrum.cli/1`](../specs/cli-protocol.md).
Consumers never need the mapping file, source paths, spans, hashes or API
program anchor: the selected carrier's emitted declarations, explicit keys
and complete retained contracts remain sufficient. Configuring keys cannot
repair missing proof, constraint, owner, witness, provider or lowering support.

## CG1 combined source producer inputs

CG1 needs TypeDef keys for source-authored enum/wrapper constant domains,
complete enum-member keys, and generic-method keys. The version-1 G1 file
has no type list; the enum-only option cannot select CG1 or coexist with
G1's option. Accepting two API lists without a transport for both is not a
complete standalone source producer path.

### Combined transport alternatives

| Route | Assessment |
|---|---|
| **Extend this mapping file with an exact CG1-only version 2** | **Recommended.** One option, one ordered snapshot vector, both typed lists and one atomic emitter handoff. No joins between independently admitted map files or new option are needed. Version 1 and its G1 method-only grammar remain unchanged; its label-preserving pairing is G1L1. |
| Compose two explicit files/options under CG1 | Coherent if a type file and a method file each assert the identical complete ordered snapshot vector, both select the same CG1 invocation/program, and a joint admission pass checks snapshots, all file aliases and Module-wide key collisions before emission. It would need a CG1-capable type option/grammar rather than silently accepting the old enum file, plus cross-file equality/ordering/diagnostic rules. The two files still feed one complete API invocation, not separately published artifacts. This extra framing duplicates snapshot inputs without a present requirement for independent file ownership; not selected. |

Keep the existing option name `--trrm-source-generic-method-exports` for both
exact grammar/schema pairs. Its legacy name adds no alias or second route.
Do not broaden `--trrm-source-enum-exports`, accept either old file shape
under CG1, or infer CG1 from populated arrays.

### CG1 TypeDef API selection

Reuse `TrRmSourceTypeExport` and the six-argument overload above. Add one
CG1 selector property while preserving the record's three positional
parameters and deconstruction, and its existing member-key property:

```csharp
public int GenericArity { get; init; }
```

Default zero preserves existing nongeneric mappings. Under CG1 it is the
exact number of **all** declared generic formals, type and constant, including
unused/defaulted ones. It is not the number supplied at a constructed use.
Require a nonnegative value and exact equality with the selected definition.
The existing four-/five-argument routes and the six-argument G1 route retain
their previous applicability; they reject nonzero GenericArity rather than
gaining CG1 generic TypeDef publication.

For CG1, a type selector is the exact tuple
`(Namespace, Name, GenericArity)`, following the accepted
[type-name declaration arity](../specs/language/names-and-binding.md#type-name-arity-selection):

* Namespace is the complete decoded source namespace, possibly empty; Name
  is the decoded simple declaration name, without constructed arguments,
  source escapes, qualification or an inferred mangled arity suffix.
  Compare ordinally without normalization, trimming, alias/import lookup or
  provider search.
* Select exactly one fresh, positively locally authored public, namespace-level
  TypeDef definition in the original checked program: an enum in CG1's admitted
  fixed-integer catalog, or an inline Transparent wrapper representable under
  CG1's published carrier/formation contracts. Generic wrapper selectors
  identify the **open definition**, not one observed closed specialization.
  Enums retain CG1's nongeneric primary/module-level boundary.
* Retain complete composed declaration/part/member order, generic formals,
  source ownership and checked snapshot facts. Equal names, a loaded
  Source-like handle, absent import metadata or a generated specialization
  name do not establish local authorship. Imported/Core/private/already
  published definitions cannot be rekeyed through this map.
* Enum assignments require a nondefault complete
  `EnumMemberDeclarationKeys` array, explicitly empty for an empty enum.
  Associate positions with actual checked members in complete composed
  compiler-file/source order, not value, member name, key or primary-first
  order. Keys are nonempty and unique within that enum; duplicate-valued
  members remain distinct. Wrappers require the property to be default:
  no member-key array, including a nondefault empty array, is ignored.

This explicitly extends the **CG1 six-argument route only** beyond
[the older enum input's](standard-trrm-source-enum-identity-inputs.md)
NominalEnumsDraft/int32-only applicability and the older struct-map schema
set. It supplies selectors/keys, not a way to make an ineligible wrapper,
change enum bases/values, grant visibility, add Blittable or waive a missing
codec/runtime contract. Other retained nominal kinds or unsupported
publication facts still refuse CG1 output, not disappear from the closure.

Capture type selectors, arity, actual definition/member handles and all
supplied arrays against the same reference-identical `BoundProgram` used by
method assignments. A generic-definition inventory/provenance adapter is
required; the current struct-specific or closed-specialization inventory is
not sufficient. Snapshot both input lists before rewriting and rebase their
resolved associations together through supported internal helper programs.
Neither source program nor submitted records/arrays is mutated.

### Exact CG1-only version-2 file

The following illustrates the complete root and both type-entry variants,
not a validated program or generated keys:

```json
{
  "schemaVersion": 2,
  "sources": [
    {
      "path": "provider.terrum",
      "snapshotSha256": "0000000000000000000000000000000000000000000000000000000000000000"
    }
  ],
  "typeExports": [
    {
      "namespace": "Example",
      "name": "Mode",
      "genericArity": 0,
      "exportKey": "type.mode/v1",
      "enumMemberDeclarationKeys": ["member.first", "member.second"]
    },
    {
      "namespace": "Example",
      "name": "TaggedCount",
      "genericArity": 1,
      "exportKey": "type.tagged-count/v1"
    }
  ],
  "genericMethodExports": [
    {
      "sourceInputOrdinal": 0,
      "declarationStart": 160,
      "declarationLength": 64,
      "exportKey": "method.select/v1"
    }
  ]
}
```

All four root properties are required and nonnull, including explicit empty
arrays. Version is the number token exactly `2`. `exports` is unknown in
version 2; `typeExports`/`genericMethodExports` are unknown in version 1.
Do not accept a missing list as empty or support both shapes under one
version. Apply all version-1 strict UTF-8/JSON, duplicate decoded-property,
text/capacity and source-snapshot rules to version 2 as well.

Every type entry requires exactly namespace, name, genericArity and exportKey,
plus **enumMemberDeclarationKeys iff the selected declaration is an enum**.
The optional syntactic presence of that field is checked against the
resolved source kind after binding; null is never accepted. GenericArity is
an unsigned decimal integer token without fraction/exponent/sign, bounded by
Int32.MaxValue; the actual formal count must also fit the selected output
schema. Method entry properties and bounds are exactly version 1's
ordinal/full-declaration-span/key fields. No name-based method selector,
signature string, inferred span or specialization ordinal is added.

There is **one** `sources` array for both lists. Its count/path identities,
argv order and UTF-16LE snapshot digests assert the entire invocation's
captured immutable source set, including files with no exported declaration.
No list carries a second digest vector or source-order interpretation.
Perform common count/path and file-alias preflight before source acquisition,
then verify every captured snapshot digest before binding. Bind once;
resolve type definitions and exact generic-method declaration spans against
that same program only after successful source diagnostics. Do not reread or
rebind source independently for the second list.

### Coverage, aliases and joint handoff

Retain the complete method-coverage rule above. Every retained fresh local
source enum/wrapper definition needed by a CG1 domain, value, signature,
default or implementation closure needs its own TypeDef mapping; every enum
mapping supplies its complete member list. An explicit type export is itself
a retained root, even if otherwise unused. Omitting nominal mappings cannot
erase their required facts or synthesize keys. This producer-input completion
does not add an unexported/nonpublic-source nominal identity mode.

For **this CG1 route**, distinct keys selecting the same TypeDef are
intentional Export aliases to one definition, not separate nominal types.
Enum alias entries must repeat the **same complete ordered member-key
association**, comparing decoded keys ordinally rather than array-object
identity; conflicting member assignments reject. This does not add member
aliases or broaden the older NominalEnumsDraft input's one-key rule.
Generic-method aliases retain the existing full-span rules.

Require ordinal **Module-wide ExportKey uniqueness** across both lists,
ordinary callable exports and all already retained publications of the
output Module, regardless of definition kind. Repeating a key rejects even
for an identical selector. Different provider Modules keep their own key
spaces. Enum member keys are unique per owning enum, not Module exports:
they may repeat across different enums or equal a Module ExportKey.
Names, arities, ordinals and member values select no durable key.

Validate all type entries and their member associations, then all method
entries, against one shared key set seeded with existing output-Module
exports. Attribute an invalid type assignment/type-list collision to API
`typeExports`; a method-list collision, including one with a supplied type
key, to `genericMethodExports`. This order only locates a diagnostic: neither
first-wins publication nor partial success is allowed. Wrong schema still
identifies `schemaIdentity`. CLI mapping ArgumentExceptions project to
TRM2001; missing required mappings and unsupported valid retained contracts
remain TRM5001. Preserve all common preflight/source/error ordering.

After capture, pass **both** immutable lists with the same program, explicit
Module identity, exact CG1 schema and target requirement to the six-argument
EmitStandard invocation. Do not produce enum and method fragments separately
and merge them by guessed names/rows. Option omission under an explicitly
selected CG1 supplies empty lists to that route, never generated mappings or
a fallback to G1/the four-argument emitter. Empty genericMethodExports is
valid for a TypeDef-only producer when no public generic method needs keys.
The option/list presence does not itself require Feature 23; CG1's independent
Feature-24 and G1 retained-use triggers still decide each Module's manifest.

Every CG1 map participates in the common source/Core/configuration/output
alias preflight and final alias recheck. Snapshot, selector, arity, member,
collision, missing-key and unsupported-carrier failures all precede output
publication. Preserve an existing destination byte-identically and leave an
absent one absent; validate one complete artifact before the existing
same-directory staged replacement. No list may publish successfully while
the other fails. Consumers need neither versioned map nor original source.

## Integration gates and remaining decisions

Integrate only the typed inputs/overload, CG1 arity/nominal capture, selected
writer handoff, CLI option/strict versioned reader, shared configuration
admission and related API/CLI documentation. Keep G1/CG1 responsible for their
publication, manifests, complete binding and source-free remapping. This
proposal assigns no new wire policy or schema composition.

Required cases below are acceptance gates, **not executed results**:

| Family | Required cases |
|---|---|
| Exact association | Multiple inputs and export-entry permutations; equal offsets in different files; same simple names and supported overloads; full declaration versus name/body/overlapping span; wrong ordinal, negative/zero/overflowing extent, no/multiple matches. |
| Snapshot scope | API assignments reused with a rebind or `with` copy reject despite equal fields; original program is unchanged. CLI rejects reordered/omitted/extra paths and changed snapshots, including equal-length edits and edits to nonexporting inputs. Exercise CRLF, supplementary scalars, consumed signature and stdin without byte/UTF-16 confusion. |
| Coverage and aliases | Unused public method definitions/formals/defaults require keys; missing one fails. Distinct keys for one definition produce aliases to one MethodDef. Repeated keys and collisions with ordinary/TypeDef/retained exports reject. Private, imported, nongeneric and closed-specialization method selectors reject; no inference for an empty map. |
| Strict transport | Missing/null/unknown/duplicate escape-equivalent properties, malformed UTF-8/surrogates, double signature, comments/trailing content, wrong exact integer tokens, invalid hash text, minimum/max/over-limit keys and aggregate/resource limits. |
| Admission and ordering | Wrong schema/command/repeated option fails before missing files or stdin reads; version 1 remains G1 method-only grammar and pairs with G1L1 on the label-preserving route; version 2 remains CG1-only, including empty maps. Reject mixed/unknown root shapes and old enum-option composition. Configuration errors precede source acquisition; digest errors precede binding; binding errors precede selector checks. Preserve Core argv admission order. |
| Files and publication | Map/source/Core/output aliases, source aliases, relative/case/link/hard-link forms supported by existing checks, late alias recheck, missing files and write failures. Assert sentinel output unchanged and absent output absent on each prepublication failure, in human and JSON modes. |
| G1 handoff | API and CLI assign identical explicit keys and one open definition per declaration; unrelated key spellings have no semantic special cases. Independent source-free consumer binding observes aliases and complete original expectations, without the configuration file or source. These cases depend on G1 implementation, not just transport parsing. |
| CG1 joint handoff | One source set supplies enums, nongeneric/generic Transparent wrapper definitions and generic methods with type/constant formals. Test exact namespace/name/arity selection, full method spans, empty/duplicate-valued enums, complete member order, unused retained nominal/default dependencies, type/method aliases and conflicting alias member keys. One artifact retains all assigned identities without requiring source or the configuration map at consumption. |
| Cross-list failures | Type/method/ordinary-export key collisions, duplicate keys for identical selectors, missing type/member/method assignments, changed or reordered snapshots and unsupported retained facts fail atomically. Check sentinel/absent outputs and human/JSON errors; cross-owner enum member-key reuse remains legal. Preserve version-1 G1 and enum-only option behavior. |
| CG1 capability separation | A TypeDef-only version-2 map with an empty method list requires no Feature 23 unless its retained closure independently has a generic MethodDef/GenericCallTarget, including a generated template helper. Mapping/configuration presence does not supply or activate a missing semantic capability. |

**No transport decision remains unresolved in this recommendation.** Exact
producer keys remain caller inputs, not design omissions. Formal adoption and
implementation/qualification remain separate; this document is provisional
direction under the authorized workflow and does not complete the larger
G1, CG1 or compiler milestone.
