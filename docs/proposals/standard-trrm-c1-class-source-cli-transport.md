# Standard TRRM C1 class source CLI transport

**Status: unapproved; provisionally actionable under the user-authorized
missing-contract workflow, pending later review.** This selects a development
CLI input contract, not normative adoption, compiler implementation or
validation evidence.

## Decision, scope and owners

Add one distinct, nonrepeatable `compile-trrm` option:

```text
--trrm-source-class-exports <path>
```

Its strict version-1 JSON file transports **all three mapping lists** of the
existing seven-argument `EmitStandard` API against one checked program:

| Required root array | Existing API input |
|---|---|
| `typeExports` | [`TrRmSourceTypeExport`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.cs), for C1-selected struct/field and interface/requirement mappings only |
| `genericMethodExports` | [`TrRmSourceGenericMethodExport`](../../sources/Terrum.CodeAnalysis/Modules/TrRmSourceGenericMethodExport.cs) |
| `classExports` | [`TrRmSourceClassExport` / `TrRmSourceClassMemberExport`](../../sources/Terrum.CodeAnalysis/Modules/TrRmSourceClassExport.cs), including optional constructor projection keys |

It supplies explicit keys and checked-source selectors, never declaration
facts. These are one class-owned C1 input and one atomic publication, not
three separately emitted fragments.

This fills the class-input gap left by the class-publication proposal and
selects one class-owned C1 CLI mapping candidate. It does not change the
label proposal's G1-only version-1/version-3 generic/interface map pairing;
those maps remain unavailable for C1.

Preserve these unapproved dependencies and their complete obligations:

| Owner | Preserved contract |
|---|---|
| [Class source publication](standard-trrm-c1-class-source-publication.md) | Exact-program primary-declaration capture, partial composition, explicit aliases, complete class/member/Body closure and original expected/provider views. Its deliberate absence of CLI/JSON grammar is the missing decision filled here. |
| [Constructor declarations and projections](standard-trrm-c1-constructor-declarations.md) | Distinct initializer I and complete-result projection P, real implementations, minimum completion, explicit P key and exact constructor-successor selection. |
| [Construction-2](standard-trrm-c1-construction-runtime-boundary.md) | Its independent fresh-backing lifecycle, Feature and host/runtime gates; Construction-1 does not acquire them from this option. |
| [Labels](standard-trrm-g1-interface-parameter-labels.md#c1-provider-inputs-and-phase-maps), [receiver access](standard-trrm-method-receiver-access.md#exact-profile-consequence) and [PI1](standard-trrm-c1-property-initialized-state.md) | Exact whole-schema successors, original declaration views, phase contexts and property/backing/initialization obligations where selected. |

This version-1 transport selects no source grammar, API signature, TRRM record,
Feature, numeric assignment, native agreement or
[`terrum.cli/1`](../specs/cli-protocol.md) amendment. It adds transport to the
class-publication proposal; it does not supersede its closure rules. Accepted
[source coordinates](../specs/language/source-input.md),
[key/identity rules](../specs/il-metadata.md#identity-and-contract-invariants)
and [exact draft agreement](../specs/representation/format-versioning.md)
remain authoritative.

### Why the sibling lists are required

These are inspected code/contract facts, not test results:

| Evidence | Consequence |
|---|---|
| The [seven-argument overload](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.cs) snapshots all three lists, calls `SourceGenericMethodExports.Capture`, then `SourceClassExports.Capture` with the type and generic lists, and passes all captures to emission. | Its C1M1 type-only branch rejects nonempty siblings; the constructor branch does not impose that restriction. The type-only restriction is not a complete-construction policy. |
| [`SourceGenericMethodExports.Capture`](../../sources/Terrum.CodeAnalysis/Modules/TrRmSourceGenericMethodExport.cs) requires an explicit key for every retained public generic definition, including uncalled definitions. | An empty generic list cannot cover a class provider containing an independent public generic function. A class-member selector cannot select that function. |
| [`SourceClassExports.Capture`](../../sources/Terrum.CodeAnalysis/Modules/TrRmSourceClassExport.cs) checks class keys against supplied type/member and generic-method keys. Its member selectors require the selected class owner. | The API anticipates joint nonempty mappings. Class/member records cannot substitute for sibling interface or generic inputs. Its generic iteration over legacy enum arrays does **not** admit an enum schema or redefine enum-member key identity. |
| The [enum API guard](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.cs) and [enum identity-input contract](standard-trrm-source-enum-identity-inputs.md#exact-candidate-api) admit nondefault `EnumMemberDeclarationKeys` only for exact NominalEnumsDraft. [Class source publication](standard-trrm-c1-class-source-publication.md#candidate-source-input-and-capture) does not extend that name/arity-oriented enum input. | Version 1 accepts only struct/interface type entries, with the legacy enum property always default. Enum composition requires a distinct exact agreement, not removal of a producer guard. |
| [C1 publication](standard-trrm-exception-class-witness-binding.md#binding-inputs-and-publication) and [C1 label inputs](standard-trrm-g1-interface-parameter-labels.md#c1-provider-inputs-and-phase-maps) require exact checked declaration associations and complete interface/generic/member closure. | C1 includes method-generic common requirements such as `IFormattable.TryFormat<TWriter>` and `ITextWriter.TryWriteFormatted<TValue>`. Class keys neither select these requirements nor invent their keys or labels. |

Therefore version 1 carries the siblings now. Empty arrays are valid only
when the selected program actually needs no assignments from that list;
they are not a smaller C1 publication profile or a deferred version-2 choice.
The current seven-argument guard admits C1M1/base Construction-1, not the
four R1/PI1 selections below. Their exact admission and complete C1 mapping
implementation remain integration work; the API shape is not support evidence.

### Transport alternatives

| Route | Disposition |
|---|---|
| **One class-owned file carrying all three lists** | **Selected.** Mirrors the seven-argument API, shares one ordered snapshot vector and key check, supports coupled declarations and leaves other manifest contracts unchanged. |
| Class-only file with empty sibling lists | Insufficient for the selected complete C1 scope: public generic definitions and independently published interfaces/requirements cannot be expressed. Not selected, even as a first version. |
| Extend `--trrm-source-generic-method-exports` with class arrays or reuse its version 1/3 under C1 | Not selected. The label proposal explicitly keeps those pairings G1-only and selects **no C1 CLI/JSON grammar**; receiver selection does not change that ownership. An empty G1 list is not a C1 transport. |
| Extend the enum/type manifest or use class/member names | Not selected. Those files have different schema/selector contracts; names cannot replace the existing exact-program/span association. |

The current
[CLI implementation reference](../implementation/cli-protocol.md#provisional-trrm-source-schema-selection)
and [module inventory](../implementation/modules.md) describe implementation,
not authority to broaden those pairings. The current
[`Program.TrRmExecution`](../../sources/terrumc/Program.TrRmExecution.cs)
gate pairs the generic-method mapping option only with G1L1. Its
[generic/interface CLI tests](../../tests/Terrum.CodeAnalysis.Tests/Cli/CliTests.TrRmGenericMethodExports.cs)
cover positive G1L1 version-1/version-3 inputs and negative C1-family
selectors; those tests do not provide a class-source CLI transport.

## Exact CLI and schema admission

Accept the option only after `compile-trrm`'s required source operand and
before its optional final `--json`. Consume exactly one following argv
element. Reject repetition, missing/empty operands, `-`, dash-leading
operands, alternate casing, equals/short aliases and other commands before
configuration/source content reads. Dash-leading filenames require an
explicit relative or absolute path.

Version 1 requires explicit `--trrm-schema` selection of exactly one literal
below.
Use the current receiver-preserving construction agreements; this new CLI
route creates no additional label-free or L1-only pairing.

| Explanatory shorthand | Exact artifact-schema identity admitted with this version-1 file |
|---|---|
| Construction-1L1R1 | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-1-parameter-labels-1-receiver-access-1` |
| Construction-2L1R1 | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1` |
| Construction-1L1R1PI1 | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-1-parameter-labels-1-receiver-access-1-property-initialized-state-1` |
| Construction-2L1R1PI1 | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-property-initialized-state-1` |

These are four separate complete agreements, compared ordinally and
case-sensitively, not components to parse or compatibility aliases. Reject
Development, C1/C1M1, their declaration-only label/receiver successors, G1,
label-free construction parents and every unlisted selection, **even for
empty maps**. The file selects no schema; its `schemaVersion` versions only
this configuration grammar. PI1 is not inferred from a property selector.
No retry, subset decoder, flag/row/Feature sniffing, relabeling or automatic
promotion is defined.

The separately specified
[C1 enum-closure composition](standard-trrm-c1-enum-closure-composition.md)
additionally pairs this option's **complete version-2 grammar** with four
distinct enum-bearing successor literals. That decision is supplied now, not
deferred. It does not admit enums in this version-1 grammar or any parent
schema. Version/schema pairs are exact; a map never promotes its selection.

Keep required `--identity`, `.trrm` output, fail-fast runtime, Windows x64
Module target requirement **1**, arithmetic selection and `--library`
behavior unchanged. Omission does not opt into this transport or waive the
selected producer's required closure; existing defaults remain unchanged.

This class-publication option adds **no source-provider fields** to its
manifest. A separate [source-provider CLI candidate](standard-trrm-source-provider-cli-transport.md)
proposes a consumer-input route for the exact local-field-layout-2 successor;
it does not compose this class manifest or admit its C1 successors. The
artifact-only `--trrm-provider`, `--trrm-module` and anchor options remain
unavailable on `compile-trrm`. For a subsequent admitted constructor-family
API operation, root, **every supplied provider including unused inputs**, and
captured Clause/Origin/Plan artifacts must select that same exact literal
before schema-dependent indexing or decoding. A declaration-only provider is
not exempt. Retained provider origins must also satisfy that agreement;
source mapping cannot restamp them. This proposal neither broadens
artifact-command schema whitelists nor claims that these outputs can already
be run or built by those commands.

## Version-1 file

The following illustrates the complete root, an interface type mapping,
a public generic definition and a class with an optional projection key.
Digest/spans are placeholders, not a validated source fixture:

```json
{
  "schemaVersion": 1,
  "sources": [
    {
      "path": "provider.terrum",
      "snapshotSha256": "0000000000000000000000000000000000000000000000000000000000000000"
    }
  ],
  "typeExports": [
    {
      "definitionKind": "interface",
      "sourceInputOrdinal": 0,
      "declarationStart": 0,
      "declarationLength": 128,
      "exportKey": "type.reader",
      "interfaceMethodExports": [
        {
          "sourceInputOrdinal": 0,
          "declarationStart": 40,
          "declarationLength": 32,
          "exportKey": "reader.read"
        }
      ]
    }
  ],
  "genericMethodExports": [
    {
      "sourceInputOrdinal": 0,
      "declarationStart": 160,
      "declarationLength": 64,
      "exportKey": "method.identity"
    }
  ],
  "classExports": [
    {
      "sourceInputOrdinal": 0,
      "declarationStart": 240,
      "declarationLength": 160,
      "exportKey": "type.box",
      "memberExports": [
        {
          "sourceInputOrdinal": 0,
          "declarationStart": 280,
          "declarationLength": 36,
          "exportKey": "box.initialize",
          "constructionProjectionExportKey": "box.construct"
        },
        {
          "sourceInputOrdinal": 0,
          "declarationStart": 336,
          "declarationLength": 32,
          "exportKey": "box.read"
        }
      ]
    }
  ]
}
```

The root has exactly **five required properties**: `schemaVersion`, `sources`,
`typeExports`, `genericMethodExports`, `classExports`. All three mapping
arrays are required and nonnull, including when empty. `exports` is unknown:
no class-only shape, G1 version-1/3 shape or compatibility alias is selected.
The enum successor's separately specified version 2 is not another shape
accepted under these version-1/parent-schema pairs.

Use the following exact object shapes. An **anchor/key** means precisely
`sourceInputOrdinal`, `declarationStart`, `declarationLength`, `exportKey`.

| Object | Exact properties |
|---|---|
| Source assertion | `path`, `snapshotSha256` |
| Struct type entry | Anchor/key, `definitionKind` exactly `"struct"`, required `fieldExports` array |
| Interface type entry | Anchor/key, `definitionKind` exactly `"interface"`, required `interfaceMethodExports` array |
| Field entry in `fieldExports` | Anchor/key selecting a direct field of that struct |
| Requirement entry in `interfaceMethodExports` | Anchor/key selecting a direct MethodDef-backed interface requirement |
| Generic-method entry | Anchor/key selecting a checked public open generic definition |
| Class entry | Anchor/key and required `memberExports` array |
| Class-member entry | Anchor/key and optional `constructionProjectionExportKey` |

All mapping/member arrays may be explicitly empty subject to checked coverage.
A type entry cannot contain another kind's member-array property, even an
empty one.
The complete `typeExports.definitionKind` domain is exactly `"struct"` and
`"interface"`. Reject `"enum"` and `enumMemberDeclarationKeys`, including an
empty vector. Classes belong only in `classExports`. Absence of the projection
field maps to the API's null property; explicit JSON null is invalid everywhere.
Every constructed `TrRmSourceTypeExport.EnumMemberDeclarationKeys` remains
default; an empty nondefault array is not interchangeable with default.

No caller-supplied name/signature, namespace, generic arity, getter key,
checked fact, provider, schema or parameter-label override is accepted.
Names and formal counts required by the existing API are derived from the
exact checked declarations, as specified below. Object order is insignificant;
array order is preserved, not substituted for source declaration order.

Resolve the file once against the invocation working directory. Read one
regular UTF-8 file, not stdin, a directory, URL, include tree or response
file. No extension restriction, environment expansion or discovery is added.
Use strict scalar UTF-8 with at most one leading signature; a second signature
is invalid JSON input. Require one root object followed only by JSON
whitespace. Reject malformed UTF-8, unpaired decoded surrogates, duplicate
decoded property names at every depth, unknown/missing properties, wrong
types, comments, trailing commas/content and nesting beyond **128**.
Escape-equivalent property names are duplicates.

`schemaVersion` is exactly the JSON number token `1`. Ordinal/start/length
tokens are unsigned decimal integers, without sign, fraction or exponent,
bounded by `Int32.MaxValue`; length is positive. Reject overflowing
start-plus-length using widened/checked arithmetic. Require **1..256** source
assertions and every selector ordinal below that count. Every mapping/member
array and the combined number of supplied type, field,
requirement, generic-method, class, member and projection keys must fit
**`0xFF_FFFF`**; all final table/String capacities still apply. Explicit
resource refusal is not truncation or partial admission.

Paths and keys are nonempty, NUL-free scalar strings. Keys preserve decoded
ordinal text without trimming, normalization, case folding or identifier
interpretation; whitespace-only keys are valid. Each key's strict UTF-8
encoding is **1..`0xFF_FFFF` bytes**, not a private short-name limit.
Every supplied key participates in one ordinal set across **all three lists
and their nested assignments**, then the output Module's other publications,
including ordinary callable and retained aliases. Repeating a key rejects
even for the same declaration. Capture complete associations once per alias
group rather than repeating their keys. This grammar carries no enum-member
keys and defines no enum-member key domain.

## Ordered snapshots and exact selectors

`sources` asserts the **entire** selected input list: required positional
source followed by each `--source` in argv occurrence order, including inputs
without mapped declarations. Do not sort, deduplicate, concatenate or add
inputs from the file.

1. Before source acquisition, require equal counts and path identity at every
   ordinal using the existing path-identity mechanism. Relative assertions
   resolve against the invocation working directory, not the manifest
   directory. `"-"` is permitted only at the actual stdin ordinal under the
   existing source/stdin gates; it is not the display name `"<stdin>"`.
2. Acquire each input once under `source-input/1`. `snapshotSha256` is exactly
   64 lowercase ASCII hexadecimal digits encoding SHA-256 of that input's
   complete captured immutable UTF-16 snapshot: each code unit little-endian,
   with **no BOM, terminator or normalization**. Hash the captured syntax-tree
   text, never reread files or hash original UTF-8 bytes.
3. Check every digest before binding. A consumed leading source signature
   contributes no code units; CRLF, supplementary surrogate pairs and interior
   U+FEFF do. Source decoding consumes only one leading signature, unlike the
   manifest's JSON rule. Equal snapshot hashes do not authorize reuse of a
   different path/origin map, checked program, options or provider context.
4. After successful source binding, require each span's checked end within its
   asserted snapshot. Resolve each anchor to exactly one full primary
   declaration of its required kind, in that program's compiler-owned source
   associations. Type entries select open definitions, not observed closed
   specializations. Coordinates are zero-based UTF-16 units, not bytes,
   scalars, name/body spans or trivia-inclusive extents. A containing/
   overlapping span, secondary-only implementation, inherited member,
   imported proxy or synthetic factory is not a match.

### Type, requirement and generic-method associations

Resolve type entries by **ordinal, full span and `definitionKind`**, not by
a G1 namespace/name/arity lookup. Only fresh public source-local definitions
are eligible. Capture partial composition and actual declaration/member order.
Derive `TrRmSourceTypeExport.Namespace`, `Name` and `GenericArity` from the
selected checked open definition: decoded namespace/simple name and exact
count of all declared type/constant formals, including unused/defaulted
formals and zero. These are API transport values, not another selector:
before handoff, verify that the derived tuple corresponds uniquely to the
same checked local definition in the original program. Capture that
correspondence before rewriting; never reselect a same-named definition in
a changed program. Where today's bound inventory lacks that association,
its capture is required implementation work, not authority for a name-based
fallback. Supply every type/field/requirement mapping required by the retained
publication and cross-artifact edges; omitted mappings cannot trim that closure.

For struct `fieldExports`, select exact public direct field primaries within
that struct's partial composition. Derive each `TrRmSourceFieldExport.Name`
from the selected field; the supplied key remains unchanged. Empty assignments
do not remove fields/Layout or their complete types. For interfaces, convert
each exact requirement anchor/key to `TrRmSourceInterfaceMethodExport`.
Require keys for every directly published MethodDef-backed requirement,
including uncalled and method-generic requirements; pool aliases and check
coverage once per declaring interface. Inherited requirements retain their
actual declaring interface and its mappings, not synthetic derived copies.

Use **C1's complete requirement contract**, not G1's bodyless/non-generic-
member/ordinary-default exclusions. Preserve all required defaults, generic
formals, associated/base/witness relationships, properties and real
implementations through their selected C1 declaration closure. Noncallable
property/associated records do not become ordinary MethodDefs merely to fit
`interfaceMethodExports`, nor does an interface key invent getter keys.
Required record/Body support remains an implementation acceptance gate; a
G1-only helper's rejection is not a narrower C1 contract. Capture parameter
labels from each original declaring source view, including member-generic
requirements, without a JSON label override.

Generic-method entries select the exact public local open definition retained
in `program.GenericMethodDefinitions`, with its checked proof/declaration
association, not a closed instance, function index, name or generated stub.
Construct `TrRmSourceGenericMethodExport` with the same checked program.
Require at least one key for **every** retained public generic definition,
even uncalled ones, under the existing capture rule. A generic interface
requirement uses its declaring interface's requirement entry; method formals
alone do not turn a bodyless requirement into an independent proof definition.
If the checked source exposes one concrete member/default generic declaration
through both an owner-member and the generic-definition inventory, both
lists resolve to the same owned MethodDef. This includes a class member or
interface default with its own checked generic proof definition. Supply
distinct explicit alias keys for the two assignments, not a repeated key,
duplicate method or specialization. A shared source span alone does not merge
distinct requirement and implementation declarations; preserve their checked
owner/role/formal correspondence and separate identities where required.

### Class/member associations and aliases

Member ordinals may differ from the class ordinal for partial compositions;
do not require textual containment in the selected primary class part.
Syntax fixes constructor/method/property/field kind. Partial constructors
select their primary and retain the actual implementing secondary's Body
and ownership. A property selects its property declaration, not an accessor
or implicit backing field; PI1 supplies no new source-nameable backing key.
Visibility and declaration eligibility remain checked producer obligations.

Class anchors select the exact full primary `ClassDeclaration.Span`. Distinct
keys may alias one type, class, generic definition or direct member. Pool
field/requirement/class-member assignments across their owner's aliases and
check required coverage once per composed declaration, not once per alias.
Different member keys may alias the same directly selected definition;
every supplied entry is still validated. Aliases do not duplicate TypeDefs,
MethodDefs, Bodies or properties. Require the existing
class capture's public direct-member keys, including constructor/method/
property and public field publications; private/internal closure gains
neither keys nor access.
Empty member arrays supply no keys, not evidence that the class has no members.

Only a constructor member may carry `constructionProjectionExportKey`.
Its ordinary `exportKey` names **I**, and the optional key independently names
that selected I's genuine **P**. It is not a class alias, getter/factory name,
derived suffix or Boolean/out `TryCreate` key. Aliases may assign distinct
keys to the same I/P. An absent P key supplies no public P export and does
not discard a P or provider-owned private implementation required by closure.
A valid request for a P that cannot establish complete construction refuses;
it never publishes an incomplete/default result or a success stub.

## Composition, capture and closure

Reject combination with `--trrm-source-enum-exports`,
`--trrm-source-type-exports` or `--trrm-source-generic-method-exports` before
reading configuration/source content, even when either map would be empty.
Do not add C1 pairings to any of those options. Core manifests remain separate
eligible source-binding configuration; admit selected files in argv option
order, validate their existing composition and exact using-Module agreement
with `--identity`, and acquire sources only after configuration admission.
They supply no missing publication keys or representation authority.

After binding **once**, resolve all three lists against the **same
reference-identical `Compilation.Program`** and source vector. Construct class
and generic records with that program; retain the type/field/requirement
checked associations while constructing their existing API records.
For struct entries, populate `FieldExports` and leave enum/interface arrays
default; for interfaces, populate `InterfaceMethodExports` and leave enum/
field arrays default. No entry populates `EnumMemberDeclarationKeys`.
Every class member array is
explicitly immutable. Snapshot all lists/arrays before emission and carry
resolved associations through rewrites; do not independently reread/rebind
for another list, copy a program or reselect changed names/spans.

Seed joint key checks with existing output-Module publications, then admit
`typeExports` and their nested assignments, `genericMethodExports`, and
`classExports` and their nested assignments in that fixed order. This order
locates the offending list/JSON entry; it never implements first-wins
publication. Group and validate aliases/coverage before serialization.

Make **one** seven-argument `EmitStandard(program, identity, schema, 1,
typeExports, genericMethodExports, classExports)` handoff with **all three
captured lists as supplied**, not hard-coded empty siblings. This is C1
applicability of existing typed mappings under each listed exact successor,
not a new overload or reuse of the G1 CLI decoder. A type/generic-only provider
may use `classExports: []`; preserve its exact C1 schema and checked empty
class association. Even three empty arrays use this route and retain all
required closure. Never retry a four-/five-/six-argument emitter, G1 decoder
or C1M1 type-only writer after refusal.

The file is **not a trim list**. Preserve all retained declarations and
required private/internal dependencies, real constructor/method/accessor
Bodies, complete signatures/types, mutability, unsafe/effect/dependency/
ownership facts, contracts, completion guarantees, witness edges, I/P
correspondence and selected PI1 backing/init facts. Unmapped classes remain
unpublished, not stripped or incomplete. Missing required type/requirement/
generic/member keys, Body/contract/context codecs or provider closure fail
complete publication.
Construction-2 lifecycle Bodies and the separate phase-1/phase-2 maps remain
required where applicable; metadata-only never means field-only success.
No runtime execution or native qualification follows from emission.

All sibling mapping, source-selector, alias and joint-handoff decisions for
this transport are selected above **in version 1**. Implementation must carry
nonempty lists where required, not decline the planned C1 scope because a
class-only transport or bounded G1 helper was easier to implement. Existing
provider/role/phase contexts remain governed by their linked owners; this
file neither serializes authorization nor replaces those required contexts.

### Enum-bearing closure

Removing the invalid enum transport is **not** a decision to exclude enum
fields, signatures, properties or values from planned complete class closure.
Checked classes can retain them, and erasing the enum to its integer base
would lose nominal identity. The
[separate enum-closure proposal](standard-trrm-c1-enum-closure-composition.md)
supplies the missing exact C1 composition, source-anchored enum input and
version-2 joint handoff now. That route, not a broader version-1 type-kind
list or the NominalEnumsDraft name-based API, is required for enum-bearing
publication. Existing parent selectors retain their non-enum meaning;
refusal under those selectors is not acceptance of the complete enum slice.

## Failures and output preservation

Before any configuration/source content reads or output acquisition, use
[`PathsAlias`](../../sources/terrumc/Program.PathIdentity.cs) to distinguish the
class file from every source, selected configuration file and output. Also
reject source file operands aliasing one another. Preserve existing
source/output and Core-file guards. Include recognized case/relative-path,
link and Windows hard-link aliases; identity-inspection failure is an input
failure, not assumed distinctness.

| Failure | Existing experimental projection |
|---|---|
| Option/operand/command errors, forbidden mapping combinations, missing or unlisted exact schema | `TRM2000`, exit 2, before configuration/source content reads. |
| Manifest I/O/UTF-8/JSON/version/types/text/count errors; source count/path/hash mismatch; wrong definition kind/owner, enum kind or legacy enum vector, invalid spans/targets; duplicate/cross-list keys or projection on a nonconstructor | `TRM2001`, exit 2. Identify the configuration file and JSON entry/field where available, using the fixed joint-admission order, without fabricating a source span. Translate only errors attributable to that mapping, not unrelated `ArgumentException`s. |
| Class/configuration/input aliases or duplicate source-file aliases | `TRM2002`, exit 2. Existing source/output argument failures retain `TRM2000`. |
| Source I/O/decoding or syntax/binding failures | Existing source-input behavior; syntax/binding diagnostics retain exit 1 and precede exact declaration-selector checking. |
| Missing required type, interface-requirement, public-generic or class/member assignments; unsupported or incomplete declaration/Body/provider/context closure, including enum-bearing content under a parent schema; valid but unrepresentable P; invalid checked program | Existing `TRM5001`, exit 1. No partial artifact, weaker profile or alternate schema retry. |
| Output publication failure | Existing output-error behavior; no success response. |

Validate the complete map and emitted artifact before output publication.
All prepublication failures leave an existing destination byte-identical and
create no absent destination. Recheck the class/configuration/source versus
output alias guards immediately before the existing same-directory staged
[`WriteArtifact`](../../sources/terrumc/Program.Compilation.cs) replacement.
Never truncate/delete output to parse a map or discover unsupported closure.
Retain staged-file cleanup and current human/experimental `--json` shapes;
this is not a new crash-durability or hostile-filesystem transaction protocol.
No worker is launched to discover support.

## Integration and focused acceptance

Implementation owners are
[`Program` usage](../../sources/terrumc/Program.cs), its TRRM option/configuration
handler, a separate strict combined class-export reader, compiler-owned
type/field/requirement source association capture, and the existing
seven-argument emitter's exact-successor admission and joint alias/key/coverage
handling. The source-facing API signatures stay unchanged; the C1 producer
must preserve resolved associations and admit all three lists under the
listed exact schemas, rather than invoke G1's narrower type/member adapter.
The linked CLI implementation reference will describe that support when
implemented. No other manifest grammar or accepted specification needs an
edit for this transport. Required producer/consumer work stays with the
class, constructor, receiver and PI1 owners; changing an option whitelist
alone cannot complete it.

Existing [class capture tests](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmSourceClassExportTests.cs)
cover record capture, partial spans, projection keys and some invalid inputs.
[C1M1 source-emission tests](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmStandardClassSourceEmissionTests.cs)
exercise bounded type-only metadata, **not** complete constructor-successor
publication. The current [NUnit project](../../tests/Terrum.CodeAnalysis.Tests/Terrum.CodeAnalysis.Tests.csproj)
contains these fixtures without per-file exclusions. These are inspected
test-source boundaries, not claims that tests ran or passed.

Add focused regressions through the CLI/public compiler APIs, using existing
supervision and cleanup helpers:

| Family | Required cases and outcome |
|---|---|
| Positive transport/closure | Under each admitted exact schema, publish an eligible ordinary class with a private initialized field, a real bodyful constructor and uncalled public method/getter. Inspect explicit keys, exact ownership/signatures, nonzero actual Bodies and required facts; a field-only artifact or unsupported result is not a passing producer test. Exercise mutable and immutable declarations and PI1-backed property guarantees under PI1. |
| Coupled siblings | One input must require **nonempty type, generic and class lists together**: an independently exported interface with method-generic requirements, a retained public generic function and a class/member implementation using them. Check all explicit keys, generic/formal/requirement ownership, labels, receiver access and real Bodies through the single seven-argument handoff. Include the C1 common formatter requirements and an unrelated synthetic interface; a G1-only requirement subset is not acceptance. |
| Type-only C1 provider | Use `classExports: []` with nonempty interface/requirement and generic mappings under the same exact constructor-family schema. Retain the checked empty class association and complete declaration closure; do not fall back to G1 or infer a smaller schema. |
| Type/member capture | Anchor struct fields and interface requirements in partial compositions. Exercise wrong-kind properties, wrong owner/primary/span and a same-named or same-arity impostor. Preserve base/default/associated/property/witness closure rather than treating every member as a bodyless ordinary Callable. |
| Aliases/partials | Type and class aliases pool disjoint field/requirement/member assignments; generic/member/I/P aliases share definitions. Use reordered partial inputs, members in another source, a partial constructor primary with a real secondary Body and identical numeric spans in different inputs. Resolve by checked ordinal/span/owner, not path or name order. |
| Coverage/collisions/projection | Remove a required type/requirement/public-generic/class-member assignment from an otherwise valid combined map: fail without dropping the declaration. Exercise every pair of cross-list/nested key kinds, including projection and ordinary callable collisions. A valid genuine P retains exact P→I/Body closure; nonconstructor P keys and unrepresentable complete P requests fail in their respective categories. Empty lists never waive closure. |
| Strict input | Missing/null root mapping arrays, the old `exports` root, G1 shapes, `definitionKind: "enum"`, `enumMemberDeclarationKeys` even when empty, wrong-kind/unknown/duplicate escaped-equivalent properties, malformed UTF-8/surrogates, extra signatures/content, comments/commas, wrong versions/types and signed/fractional/exponent integers reject. Preserve valid escaped scalar keys, whitespace-only keys and distinct Unicode normalization forms. |
| Enum boundary | The four parent schemas reject enum-bearing closure rather than retagging it as int32 or selecting an enum decoder. Complete the separately specified enum-bearing successor/version-2 cases; version 1 plus a parent schema is not that acceptance gate. |
| Snapshot/bounds | Test stdin, all-input count/order/path mismatch, same-content distinct paths, edits in an unmapped source, CRLF and supplementary characters before declarations, consumed BOM versus interior U+FEFF, uppercase/malformed hashes and stale spans. Cover ordinal 255 with 256 inputs, zero length, exact EOF, past-EOF and Int32 end overflow, key byte limits and checked aggregate capacity refusal. |
| Selection/composition | Reject absent/wrong/case-altered schema, every parent/sibling/private mismatch, repeated/ineligible options and all three forbidden mapping-file combinations before source/stdin acquisition. Keep Core manifest using identities independent. For admitted API consumption, reject even an unused provider with the wrong exact schema; never widen artifact CLI support implicitly. |
| Publication safety | Exercise missing/unreadable files, recognized relative/case/link/hard-link aliases and a late alias recheck. For every failure family, assert exit/diagnostic and both existing-output byte preservation and absent-output preservation; verify no success artifact response. |
| Source-free handoff | Consume combined type/interface/generic/class keys through their admitted C1 reference APIs without source/debug/map files; preserve distinct aliases, requirement labels, selected generic definitions and real member/I/P/witness closure through re-emission/remapping/reconsumption under the same exact schema. Missing provider/member/Body/context facts must refuse, not select a G1 or type-only substitute. |

These are future acceptance gates. This document performs no compiler
implementation, build, test run, native execution or formal approval.
