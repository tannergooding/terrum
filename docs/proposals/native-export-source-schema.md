# Exact NativeExport source and Core schema

**Status: unapproved candidate; provisionally actionable under the user's
authorized missing-contract workflow.** This recommendation supplies one
missing source/Core schema. It does not approve or implement it, amend the
accepted specifications, extend CLI manifest v1, or select another native
ABI, runtime facility or serialized declaration codec.

## Finding and recommendation

Recommend the registered `Terrum.Interop.NativeExport` schema with **one
optional `EntryName` literal argument**, supplied positionally or by that
exact label. Omission publishes the exact declaration name. Apply it once
to the primary contract of an eligible static method or directly addressable
static data field. Method exports consume the existing NativeCallable
normalization; the export schema has no convention argument.

The missing decision is real, but narrower than native export support:

| Evidence | Already determined / still missing |
|---|---|
| [Native declaration roles](../specs/native.md#native-declaration-roles) | Determines static method/data publication, visibility independence, method-implies-NativeCallable, exact omitted names and image-symbol uniqueness. Does not declare the export argument's source label, literal domain, positional admission or complete Core schema. |
| [Grammar §3.1](../specs/grammar-spec.md#31-source-file-structure-and-declarations) | Supplies bracketed attributes, `name:` argument mapping, resolved identity, schema-owned applicability/repetition and primary contract authorship. Referring placements/defaults to native.md does not supply its missing concrete argument record. No new grammar production is needed. |
| [Metadata](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics), [clause matrix](../specs/representation/contract-clauses.md#2-clause-matrix-and-uniqueness), [registry](../specs/representation/trrm-1.registry.json) | Already assign the 16-byte `0x0022` row, its name omission, tagged owner and exact method-owned `0x000C` reference. This is not an undecided row, convention number or export ABI. |
| [Calling-convention source schemas](native-calling-convention-source-schema.md#candidate-core-registration-and-source-arguments) | Select I/C/P and identify role X, expressly without a new export argument/symbol schema. The original candidate in `c682b0ae` made the same exclusion. |
| [Core registration hand-off](native-calling-convention-core-registration.md#proposed-registration-hand-off), [provider configuration](native-core-provider-configuration.md#host-input-and-adapter-obligations) | Require X's complete arguments, placement, repetition and defaults where used; a role handle is insufficient. The hand-off's original `7f0dbc4c` likewise left export arguments unselected. |
| [CLI registration candidate](native-core-cli-registration.md#scope-and-narrow-supersession) | Intentionally excludes X from manifest v1 and forbids dummy schemas. That is a CLI delivery boundary, not a source-schema decision or permission to infer one. |
| Current [`NativeCoreRegistrationSet.Create`](../../sources/Terrum.CodeAnalysis/Semantics/NativeCoreRegistration.cs), [`NativeCoreRegistrationTests`](../../tests/Terrum.CodeAnalysis.Tests/Semantics/NativeCoreRegistrationTests.cs) | X is now nullable; `RegistrationCanOmitNativeExportWhenThatRoleIsNotSelected` covers absence. The factory no longer requires a fabricated export descriptor. Its compact `CoreNativeAttributeSchema` still lacks complete argument/placement/default records. |
| Current [`Binder.cs`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.cs), [`NativeCoreManifestReader`](../../sources/terrumc/NativeCoreManifestReader.cs) | Binder has no complete X admission/normalization. The manifest reader selects only I/C/P and passes `nativeExport: null`. Missing implementation alone would not justify this proposal; the source catalog above remains incomplete independently. |

Existing [`Program.Compilation.cs`](../../sources/terrumc/Program.Compilation.cs)
passes host-selected `--export`/`--export-all-public` names to NativeEmitter.
The [native implementation reference](../implementation/native.md#provisional-nothrow-source-retirement)
explicitly distinguishes that path from source NativeExport.
`NativeCallableEntryName` mutations in
[`NativeFunctionPointerTests`](../../tests/Terrum.CodeAnalysis.Tests/Native/Interop/NativeFunctionPointerTests.cs)
exercise another provisional carrier, not X's source schema. Neither path
chooses this decision.

## Exact proposed source schema

X is a public, nongeneric, primary semantic attribute schema with the proposed
source identity `Terrum.Interop.NativeExport` and the native-export role.
It is not a runtime attribute class, constructor overload or writable property.

| Argument | Literal/value contract | Supply and omission |
|---|---|---|
| `EntryName` | One noninterpolated `String` or `RawText` literal, decoded to the exact name value below | Optional; sole positional argument or named `EntryName`. Omission uses the target declaration's exact name. |

Ordinary grouping around the literal is transparent; the remaining expression
must be that literal, not a call, concatenation, constant binding, runtime text,
`nameof` recipe or interpolation, including interpolation with no holes.
Use the accepted [literal decoding](../specs/lexical-grammar.md#strings-and-character-notation)
and [raw framing rules](../specs/grammar-spec.md#2-lexical-notation). This
introduces no runtime Text type, conversion, allocation or separate evaluator.

`[NativeExport]` and `[NativeExport()]` are equivalent omissions.
`[NativeExport("Probe")]` and `[NativeExport(EntryName: "Probe")]` are
equivalent explicit selections when ordinary lookup resolves X.
There is no other argument or overload. Positional arguments precede named
arguments, and the argument binds at most once. Positional/named collisions
and repeated labels reject even when equal. Reject extra arguments, unknown
or incorrectly cased labels, `null`, characters and other value kinds.
`CallConv`, `ExportName`, `EntryPoint` and `Name` are not aliases.

### Targets, placement and multiplicity

| Target | Exact applicability |
|---|---|
| Method | An ordinary namespace-level function represented by a static MethodDef, or an ordinary static method. The completed declaration must identify an eligible code definition, not an unresolved requirement or signature anchor. |
| Data | A directly addressable static field exposing this declaration's actual storage. It is not an instance field, constant, property, accessor or imported extern storage. |

Untargeted placement, or explicit `method:` / `field:` directly on the
corresponding declaration, selects that declaration. There is no
`method(name)` or `field(name)` member selector, enclosing-type default,
result/receiver/formal target, Module/assembly target or type-use form.
Reject X on constructors, operators, accessors, types/interfaces themselves,
instance members, locals and other declarations.

X is nonrepeatable across all placements and aliases, including identical
repetitions. Author it on the member/callable's primary contract, not a
secondary partial declaration. Ordinary partial completion may provide the
required body; a new complete member introduced by a secondary type part
still owns its own primary declaration. Source order cannot change the role.

Terrum visibility is unchanged: an eligible internal/private declaration can
publish a native symbol without acquiring a public Terrum export key.
X neither makes an otherwise illegal bodyless declaration legal nor selects
an implementation of a bodyless interface requirement. Unsupported generic,
async, variadic, representation or addressability cases retain their existing
admission failure; no closing-instantiation selector or wrapper is invented.

### Name value, encoding and default

The exact value is **nonempty, well-formed Unicode scalar text without NUL**,
and must have a complete exact export-symbol mapping on the selected target.
Decode source escapes/raw framing once. Do not trim, normalize Unicode,
case-fold, decorate, append suffixes, infer a namespace/owner qualification,
interpret a path, or replace an unrepresentable character.

Omission uses the declaration's semantic identifier name, not its qualified
lookup name, ordinary `.trrm` Export key or raw token spelling. For example,
an escaped keyword declaration `@return` has the default name `return`.
An unencodable default rejects just like an unencodable explicit name.
There is no empty-string/null sentinel or fallback name.

The [Windows x64 imported PE/COFF rules](../specs/targets/windows-x64.md#3-imported-platform-rules)
govern that target's ASCII export-name representation; do not lossily encode
Unicode through an ASCII replacement fallback. This candidate does not
globally narrow NativeExport's existing target-dependent naming rule to ASCII,
nor change NativeImport's independently selected ASCII constraints.
Names are symbol values, not required source identifiers: punctuation is
permitted when the target admits it.

Use the existing metadata String encoding, unit-count, terminator, extent and
canonicality rules. Equivalent admitted String encodings do not change name
equality. The `u24` count bounds the complete encoded row to `0xFF_FFFF`
units, with separate checked section/artifact extents. Target/tool capacity
can additionally decline use; this schema does not adopt the prototype's
256-character or 1024-export limits as language minima or maxima.

Check the complete output image's symbols before publication, including
method/data exports, default/explicit selections and independently selected
host/generated symbols. Two declarations with the same default identifier,
including overloads or different owners/namespaces, collide; qualification
does not repair them. Distinct exact names remain distinct under the target's
symbol relation, not its DLL/file-path case relation. An alias to X does not
permit multiple export names on one declaration.

These **proposal-only declaration fragments** omit Core, target and runtime
selection; they are not runnable examples or an implementation claim:

```terrum
[Terrum.Interop.NativeExport]
internal unsafe int32 Probe(int32 token)
{
    return token;
}

[Terrum.Interop.NativeExport(EntryName: "Probe_C")]
[Terrum.Interop.NativeCallable(
    CallConv: Terrum.Interop.NativeCallingConvention.Cdecl)]
internal unsafe int32 Other(int32 token)
{
    return token;
}
```

Probe uses the implied bare callable contract; Other uses its explicit Cdecl
contract. X selects neither a second convention nor a second callable clause.

## Complete Core declaration record and authority

The following is a **logical checked producer declaration**, not new JSON,
TRRM fields, numeric IDs or required public compiler API spellings. Retain
these facts in the shared schema/expectation carrier rather than infer them
from a role number or source-qualified name.

| Logical field | Exact required X record |
|---|---|
| Identity | Actual checked schema definition; external selection uses the exact authorized provider Module identity, export key and attribute-schema kind. |
| Qualified name / role | Proposed `Terrum.Interop.NativeExport` / native-export. |
| Declaration shape | Generic arity 0; public and primary; nonrepeatable. |
| Placement / targets | Primary-declaration contract; exactly static-defined-callable and static-defined-data, with the applicability/target-selector restrictions above. These are logical projection tags, not new source tokens. |
| Arguments | Exactly one ordered argument, ordinal 0, case-sensitive name `EntryName`, literal kind `entry-name-literal`, no enum/type-reference argument identity, optional, positional and named admission both true, contexts exactly method/data. |
| Literal/name domain | Noninterpolated String/RawText after grouping, exact checked decoding, nonempty NUL-free scalar text and complete selected-target export-symbol admission as specified above. These constraints are retained argument-contract facts. |
| Default | Declaration-name mode in both contexts, not a literal value, zero, null, expression/program or provider-key-derived name. |
| Convention/resolution arguments | Absent. X's calling-convention, resolution-mode and resolution-policy argument references are null; method implication is a separate C dependency, not an X parameter. |

Apply the existing
[Core authority](../specs/libraries/adopted-inventory.md#1-status-and-identity),
[registration hand-off](native-calling-convention-core-registration.md#proposed-registration-hand-off)
and [host delivery](native-core-provider-configuration.md#host-input-and-adapter-obligations):
explicit host authorization, actual complete producer definitions/export maps,
one checked using-Module snapshot/generation, identity before comparison,
and complete retained original expectations. Provider identities, revisions,
schema selections and keys remain mandatory host inputs, not guessed defaults.
Transparent aliases to the selected declaration preserve identity; unrelated
names, suffix conventions, equal records or a package's Core claim do not.

Both forms require complete X. A method additionally requires the selected
C/E closure needed for its effective callable contract, including the exact
registered Unmanaged default when applicable. A field creates no convention
or C/E dependency merely by being exported. Independently submitted I/C/P
configuration still requires its own complete validation.

An authorized in-process producer can supply this complete record through
the hosted hand-off. The compact current native schema carrier needs the
already-selected complete-schema implementation, plus X's record; its
nullable slot or role flag is insufficient. Reject missing arguments,
placement/target/default/value-domain facts, incompatible originals and
stale/foreign-context handles before affected binding. Publish no partial
successful role from failed admission.

**Manifest `terrumc.native-core-manifest/1` remains I/C/P-only.** A
`nativeExport` property stays invalid and X source use without its complete
registration fails. This proposal does not extend that grammar, add a
provider default, manufacture X for the CLI, or turn `--export` into source
registration. A future CLI delivery amendment is separate from this decision.

## Binding and normalization

1. Resolve the actual selected X in ordinary lexical/dependency lookup; bind
   its arguments and primary target. Absence of X does not authorize name
   recognition or an omitted-name fallback. Compose the declaration and
   validate eligible code/storage and selected-target admission.
2. Reject X combined with NativeImport on the same declaration. An export
   does not automatically re-export foreign code/storage. A wrapper requires
   an independently authored eligible declaration.
3. For a method, preserve the
   [selected C normalization](native-calling-convention-source-schema.md#exact-normalization-inheritance-and-witnesses):
   explicit method C wins, otherwise a declaring interface's applicable
   default wins, otherwise X implies bare C and selects exact E.Unmanaged.
   Annotation order is irrelevant. Explicit X plus C yields one role and
   one effective `0x000C` clause; repeated authored C still rejects.
4. Preserve native invocation's caller-unsafety, complete signature/effects,
   outputs, dependencies and reverse-boundary failure containment. An
   `unsafe` declaration does not authorize unsafe operations in its body.
   X supplies no general NoThrow promise or convention-changing adapter.
5. For a field, retain the exact FieldDef storage identity, type, mutability,
   initialization, access, concurrency and load/cleanup lifetime obligations.
   Do not publish a copy, add a callable clause because its type is fnptr,
   grant write authority, initialize storage or extend a pointer's lifetime.
6. Carry export owner/name and the normalized callable fact through shared
   bound-tree traversal/rewriting, mandatory verification, persistence,
   dependency binding and lowering. Do not recover X from
   `NativeCallableEntryName`, visibility, host export lists or sample names.

Malformed schema/source use, unavailable representation/codec, target refusal
and later image-symbol collision remain distinct failures. None silently
chooses Unmanaged, removes an export, invents another symbol or emits a
successful partial image.

## Existing standard metadata mapping

Keep `NativeExport 0x0022` exactly
`u8 ownerKind, u8 flags=0, u16 reserved=0, u32 ownerRef,
u32 entryName?, u32 nativeCallableContract?`, with its existing 16-byte stride.

| Source fact | Existing required mapping/check |
|---|---|
| Method export | `ownerKind=1`; ownerRef names the exact static MethodDef, with no receiver. `nativeCallableContract` names that owner's unique MethodDef-owned `0x000C` clause, whose 12-byte extent and convention bytes 1..4 are unchanged. |
| Data export | `ownerKind=2`; ownerRef names the exact FieldDef with its assigned static flag. `nativeCallableContract=0`, including when the field type is a function pointer. |
| Omitted / explicit name | `entryName=0` uses the exact MethodDef/FieldDef name. If that stored metadata name differs from the source declaration identifier selected by omission, the producer writes that effective source name as a String row; explicit EntryName also references its checked String row. Compare effective symbol values, not physical String encoding or omission provenance. |
| Multiplicity and validity | One export row per owner; correct kinds, bounds, ranges, references and zero reserved fields. Reject duplicate owners/image symbols, missing or foreign-owner callable clauses and a field's nonzero clause reference. Do not repair a missing method clause at read time. |

### Composition with host-selected exports

Source X contributes its effective symbol independently of host-selected
ordinary exports. `--export` and `--export-all-public` retain their existing
host-emission meaning: they neither manufacture X nor suppress an authored X.
An output image combines both selected sets. If the same owner contributes
the same exact symbol through both paths, emit it once; if the host explicitly
selects the declaration's source name while X selects a different EntryName,
both symbols may reference that declaration. Different owners with colliding
effective names reject image publication. Neither source or host selection
renames the other, and no new CLI option is selected.

Ordinary `.trrm` Import/Export keys remain separate and do not confer OS
publication or make a native symbol a Terrum declaration binding.
Preserve the existing native capability/admission closure, including
`NativeExportEntries (4,1)` and its prerequisites where required. Allocate
no table, clause, Feature, opcode, flag or format pair. This source decision
does not provide missing static-storage or executable Body representations.

## Source-free dependencies, replacement and transformation

Supported normalized consumers need no source annotations, Core registration
or manifest merely to consume the export row and effective callable clause.
Resolving `entryName=0` from its retained owner name is the assigned metadata
rule, not source reconstruction. Perform structural/support/binding checks;
do not re-prove producer bodies or infer absent facts from optional Attr/debug
data. A retained dependency contract is not an instruction to export its
provider's storage/code again from the consumer image.

Retain every original selected declaration/owner and effective symbol,
convention, signature, representation, access, output, dependency and lifetime
expectation. Matching OS symbols or `.trrm` keys alone do not establish
compatible replacement.
Changing an owner's name changes an omitted export's effective symbol;
compare against the original expectation, not the replacement's defaults.
Omitted versus explicit naming can agree when the effective symbol and every
other required fact agree. Equal machine mappings do not equate conventions.

Retained source/schema consumption additionally needs X's complete declaration,
argument/default/placement/name-domain expectations and originating Core
binding context through an independently admitted codec. Keep independently
surviving enum/value/evaluation obligations. Do not add a synthetic runtime
Core dependency merely because normalization used X or E, or pretend a host
descriptor is serialized schema support.

Packing/remapping resolves actual identities first, preserves each using
context and rebuilds owner/String/Contract references, ownership ranges,
capability manifests and affected Body/optional source maps. Preserve the
effective name when transforming an owner: keeping zero while changing its
name is not an equivalent rewrite; an admitted transformation can materialize
the preserved explicit name. Recheck combined image collisions. Do not patch
matching bytes or reinterpret the provisional NativeCallable name carrier
as a standard export row. Any private projection remains a separate exact
schema integration, with no compatibility reader selected here.

## Alternatives and adoption boundary

| Alternative | Assessment |
|---|---|
| **Recommended: optional positional-or-named EntryName on X** | Completes the accepted role with one argument, ordinary omission and existing normalized rows. Reuses the native name-argument vocabulary without a second convention selector. |
| Named-only EntryName | Equally implementable and less positional syntax; requires rejecting `[NativeExport("Probe")]` without improving this one-argument schema's ambiguity or normalization. |
| ASCII-only source names | A concrete narrower portable subset, aligned with NativeImport and current PE output. Not recommended: the accepted export rule already delegates exact symbol encoding to the selected target; a universal restriction is unnecessary for the source catalog decision. |
| Separate symbol-name/alias schema or CallConv on X | Requires additional identities, pairing, precedence and/or repeated-name records. No such need is established; method convention already has C and metadata already has one effective clause. |

On adoption, add this complete X schema to
[native declaration roles](../specs/native.md#native-declaration-roles), with
the grammar/native Core catalogs referencing that owner. Preserve the
calling-convention proposal's normalization and replace only its **unselected
X source-schema boundary** by this recommendation. The registration/provider
proposals retain authority and delivery ownership; CLI v1's exclusion remains.
The existing metadata/registry owners need no new physical allocation.
No such owner or approval-status edit is made here.

## Required tests and remaining gates

These are **future acceptance cases, not tests run**. Use public compiler APIs
and unrelated explicitly authorized synthetic Core producers. The current
test project includes the inspected native-registration/CLI fixtures; their
existing absence/carrier cases do not establish complete X admission.

| Group | Required cases and outcomes |
|---|---|
| Positive source | Bare/empty omission, positional/named EntryName, grouped ordinary/raw literals and exact schema aliases normalize identically when values agree. Internal/private eligible declarations publish without changing Terrum visibility. |
| Callable normalization | X alone gives Unmanaged; explicit C for each registered value and eligible declaring-interface defaults give one exact clause. Reverse annotation order is equivalent; bare explicit C retains its selected override behavior. |
| Data | Mutable and fixed static storage; a field containing fnptr. Retain actual storage/mutability and no callable implication. Exercise an independently valid field registration without inventing C/E dependency. |
| Arguments | Extra/duplicate/case-wrong/unknown labels, positional/named collision, CallConv on either X form, null/character/runtime/forwarded/computed/interpolated text reject, without ignored arguments or aliases. |
| Placement | Repetition through aliases/targets, secondary partial annotation, instance member, constructor/operator/accessor, enclosing type/default, named-member selector, signature anchor, unresolved bodyless requirement and X+I reject. A properly completed primary partial method remains eligible. |
| Name boundaries | Escape-equivalent values compare equally; `@return` defaults to `return`, not a qualified/token name. Empty/NUL/ill-formed literals fail. A well-formed name unencodable by PE declines without replacement; admitted punctuation remains exact. |
| Collisions | Default overload/same-name owners, explicit/default method-field collision and host/generated collision reject image publication. Exact differently cased PE names remain distinct; input/annotation order cannot pick a winner. |
| Capacity | Test declared String/target limits at and beyond their checked extent boundaries; never truncate or adopt prototype limits as the schema. Keep target support refusal separate from malformed literal/row failures. |
| Core admission | Missing X, role-only record, missing/changed argument/domain/default/target/repetition fact, wrong identity/kind/key, unauthorized producer, conflicting revision and stale/foreign-context handle reject. Equivalent aliases do not become duplicate registrations. |
| CLI isolation | Manifest v1 still rejects nativeExport properties and cannot admit source X. Existing I/C/P and independent host export controls retain their selected behavior; no dummy X is produced. |
| Standard rows | Round-trip both owner kinds and omitted/explicit names. Reject bad stride/extents, reserved fields, wrong/nonstatic owners, duplicate owners, missing/duplicate/wrong-owner `000C`, invalid convention bytes and a field's nonzero clause reference. |
| Source-free/replacement | Remove source/Core configuration after normalization; consume supported facts without defaults reconstruction or body re-proof. Reordered rows and equivalent explicit/default names preserve meaning; changed effective symbol/convention or retained schema facts fail against every original expectation. |
| Remapping | Pack distinct using contexts, reorder String/owner/Contract rows, transform an owner with an omitted name and collide selected output names. Preserve original identities/effective symbols and actual reference/maps; never byte-substitute or relabel old expectations. |
| Native evidence | After source/metadata gates, separately qualify supported headless method calls and actual static-field address/storage observations. Follow existing deadlines/process ownership/cleanup; emission alone is not execution, and X grants no extra lifetime or ABI support. |

No argument, default, target, alias, multiplicity or naming-policy choice
remains open within this recommendation. It remains unapproved. Complete
shared schema carriers, hosted X binding, verification/persistence/replacement,
static-data/code lowering and final-input qualification are implementation
gates; CLI delivery and raw schema publication remain separate. This proposal
does not claim native conformance completion or reopen adjacent ABI/runtime
contracts.
