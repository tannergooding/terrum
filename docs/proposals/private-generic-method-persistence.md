# Open generic methods in private semantic modules

> [!IMPORTANT]
> **Superseded as the current provisional selection by
> [G108S](private-span-type-term-persistence.md).** Retained as the exact
> generic-method base; no independent revision-107 support or compatibility
> is selected.

## Decision and boundary

Earlier recommendation: **`terrum/private-semantic-107-generic-methods-1`**,
abbreviated **G107**: the exact private revision-106/-2 base below plus
method-owned formal identities, open declaration/body records, bounded
operator evidence, retained closed-instance origins and one typed generic-call
expression. The earlier candidate used private little-endian **i32 revision
107**, not another interpretation of 106.

The required result is independent producer/reference/consumer use, including
semantic sets, graph libraries, replacement and source-free re-emission.
Consumers can instantiate a public generic definition that the producer never
instantiated. Checked reference results retain their complete substituted type
and declaration-owned `DependsOn` source ordinals. Native execution first
closes instances into ordinary checked functions; no open native entry,
dictionary ABI, runtime source inspection or specialized export-name protocol
is selected.

G107's initial open-method domain is **module-level functions with ordered
type formals in the unmarked non-unique, Copy-capable, non-null domain
(`formalKind=1`, domain tags `1/1`)**. It includes public definitions,
internal implementation templates, type defaults representable by the terms
below, generic-to-generic forwarding and the bounded single-static-operator
requirements below. `[Copy]` is not a constraint. In particular,
`where T : [Copy]` remains rejected, not a legacy spelling to normalize.

This is an explicitly bounded private codec, not a reduction of accepted
generics. Constant formals, other ownership/nullability domains, declaring-type
formals, arbitrary constructed nominal terms, general interfaces/witnesses,
associated relations, property-refinement branches and existential evidence
remain accepted-semantic obligations without a G107 carrier. A retained
declaration needing them refuses G107 transport; do not omit an unused formal,
constraint, default or body to make it fit. The final section identifies these
separate gates. There is no unresolved wire choice **within G107**.

## Owners and inspected implementation

The [specification index](../specs/README.md) leads to the following semantic
owners. None is replaced by a private record or a current backend restriction.

| Owner | Preserved contract |
|---|---|
| [Complete types](../specs/language/types-and-normalization.md#generic-formal-domains-and-specialization), [grammar §3.2](../specs/grammar-spec.md#32-types-parameters-and-arguments), [calls/inference](../specs/language/calls-and-inference.md) | Formal owner/kind/ordinal, default domains, full-domain checking, complete substitution, earlier-formal defaults and already-selected calls. No new syntax or inference. |
| [Constant generics](../specs/language/constant-generics.md) | Exact domains, canonical values and forwarding remain required; absence of their G107 codec is not erasure to types or integers. |
| [Interface witnesses](../specs/language/interface-witnesses.md), [logical generic records](../specs/representation/logical-records.md#generic-domains-and-specialization-branches-encoding-pending) | Nominal conformance authority, exact requirement identity, all obligations and owner-before-method substitution. G107 selects only the bounded operator fragment described below, not general witness dispatch. |
| [References](../specs/language/references.md), [dependencies](../specs/language/dependencies-and-views.md), [flow](../specs/language/initialization-and-flow.md), [ownership](../specs/language/ownership-and-cleanup.md), [lifetimes](../specs/lifetimes.md) | Descriptor/referent distinction, exact stored type, source generations, broad versus backing-only dependencies, initialization, retention and cleanup on every outcome. |
| [Binding/replacement](../specs/representation/binding-and-replacement.md), [reader trust](../specs/representation/reader-trust.md) | Original expectations, directed satisfaction and stronger relied-on facts; mandatory safe consumption is distinct from producer semantic truth and optional audits. |
| [Persistence](../specs/representation/persistence.md), [versioning](../specs/representation/format-versioning.md) | Private implementation agreement, exact schema selection, no unpublished-draft compatibility presumption and no decoder guessing. |
| [Modules](../specs/modules.md), [sets](../specs/module-sets.md), [graphs](../specs/native-graph.md), [JIT](../specs/jit.md), [Windows x64 calls](../specs/targets/windows-x64-call-interface.md), [deployment](../specs/targets/windows-x64-deployment.md#4-selected-closure-origin-and-replacement) | Semantic lowering versus unchanged native images, complete selected closure, existing ABI/runtime admission and retained code/cleanup origins. |

The provisional
[Standard GenericCall proposal](standard-trrm-generic-method-call-body-encoding.md)
is the **semantic counterpart**: exact selected declaration, ordered actuals,
all constraint obligations, retained import expectations and closed lowering.
Its G1 schema, Feature 23/1, TypeSpecRefs, MethodMap, CallSite target kind 4 and
Body record `0x10` are **not composed**. No Standard artifact, row payload,
token or default decoder is embedded or inferred here.

These are inspected worktree facts, not newly executed tests:

* [`BoundProgram.cs`](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs)
  has ordered `BoundGenericMethodSignature` and
  `BoundGenericMethodDefinition` collections. Definitions retain an isolated
  proof program, entry function, source-input ordinal, visibility, caller
  unsafety, explicit formal-to-placeholder mappings and operator requirements.
  Used operator requirements refer by ordinal to a separate
  `BoundGenericMethodOperatorProtocol` catalog. Each record retains the
  qualified name, source-input/span identity, operator/arity, self role and
  caller-unsafety. `BoundGenericMethodInstantiation` identifies specialized
  functions and `BoundCall.GenericMethodCall`; it currently carries **type
  actuals only**. Type-formal defaults now retain syntax-free concrete,
  method-formal, checked-reference, optional and local source-generic type
  terms plus earlier-formal dependency ordinals; imported generic nominal
  terms retain exact provider/module/TypeDef identity. Native-pointer,
  fixed-array and function-pointer terms also retain their constructors;
  pointer/array formals require selected Blittable evidence and function
  pointers retain calling convention and ABI-supported type positions. Call
  binding applies defaults after inference or a supplied prefix and rejects
  self/forward defaults. These constructor terms have no selected G107 wire
  encoding; private persistence, provider-context remapping and constant
  method formals remain open.
* [`Binder.StaticProtocols.cs`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.StaticProtocols.cs)
  source-binds public templates, assigns domain tags 1/1 and binds reference
  formal/result patterns and declaration dependencies. Proofs currently use
  internal opaque `$T` structures and external operator/helper signatures.
  Those structures' four-byte scaffolding is **not a generic layout or wire
  representation**. Static protocols/conformances remain a bounded
  compilation-local model, not the complete nominal witness model.
* [`SemanticVerifier.cs`](../../sources/Terrum.CodeAnalysis/Semantics/SemanticVerifier.cs)
  validates catalog/definition correspondence, proof contexts and call versus
  specialization identity; the bounded catalog permits 32 definitions and
  eight formals per definition.
  [`BoundTreeTraversal.cs`](../../sources/Terrum.CodeAnalysis/Semantics/BoundTreeTraversal.cs)
  visits proof functions and rewrites instantiation type actuals. It does not
  already supply generic wire identity, import binding or complete
  catalog/formal remapping.
* [`CompilationReference.cs`](../../sources/Terrum.CodeAnalysis/Semantics/CompilationReference.cs)
  carries checked references, nominal contexts and source interface
  declarations, but no public/open generic-method catalog or implementation
  handle. Adding a collection only to `BoundProgram` cannot cross this boundary.
* [`ModuleCompiler.cs`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.cs)
  explicitly admits headers **104 and 106**. Its writer starts from 104 and
  selects 106 for the newer retained contracts. `Module`/`ModuleFunction`,
  type resolution and expression writing lack generic formal/declaration/call
  persistence. The separate
  [set](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.ModuleSets.cs)
  and [graph](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.Graph.cs)
  readers/remappers also need the new closure.
* The current 106 paths in
  [checked metadata](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.CheckedReferences.cs)
  and [typed places](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.PrivateBody.cs)
  retain declaration-owned general Dependencies and checked publication.
  Those fields can retain a **closed** generic reference-result specialization;
  they cannot retain its original open expected declaration.
* [`NativeModuleCompiler`](../../sources/Terrum.CodeAnalysis/Modules/NativeModuleCompiler.cs),
  its [sets](../../sources/Terrum.CodeAnalysis/Modules/NativeModuleCompiler.ModuleSets.cs)
  and [graphs](../../sources/Terrum.CodeAnalysis/Modules/NativeModuleCompiler.Graph.cs)
  use outer version **3**, separate semantic declarations from actual machine
  imports, and parse embedded private Contracts. The
  [graph JIT worker](../../sources/Terrum.CodeAnalysis/Modules/NativeModuleCompiler.GraphJit.cs)
  regenerates root code, not selected provider DLLs.

The [bounded-generic inventory](../implementation.md#bounded-generic-functions),
[module inventory](../implementation/modules.md),
[output profiles](../implementation/output-profiles.md) and
[CG-001/006 inventory](../implementation/conformance-gaps.md) distinguish
compilation-local specializations from independent open consumers.
`PublicGenericDefinitionsRetainCheckedBodiesAndRequirements` and
`PublicGenericReferenceResultsRetainDependencyOrdinals` in
[`StaticProtocolTests`](../../tests/Terrum.CodeAnalysis.Tests/Semantics/Generics/StaticProtocolTests.cs)
exercise the in-memory carrier, not this codec. The
[test project](../../tests/Terrum.CodeAnalysis.Tests/Terrum.CodeAnalysis.Tests.csproj)
has no Compile exclusions. No G107 support or passing runtime result is claimed.

## Selection, composition and alternatives

### Exact base and header

G107 composes the complete
[`terrum/private-semantic-106-raw-union-return-coverage-2`](private-raw-union-return-coverage.md#exact-selection-and-framing)
selection, including its B+C+R+U+A contracts, with **only** the deltas below.
The [checked-reference owner](checked-reference-private-persistence.md)
supplies the recursive reference types, typed places, adaptation and
declaration contracts. Its assertions about a then-current 104 implementation
and its earlier 97 candidate are historical, not G107 admission rules.

To remove composition ambiguity, the inherited function order is the current
106 writer/reader order:

```text
Name / Contract / Import / hasFunction / HasOwnershipTransfer
NativeOutput role and collection
general Dependency count and 12-byte rows
checked selector count/rows; checked output-state count/rows
raw-Union return-coverage count/bytes
CopyOrigin kind/payload
foreign-member discriminator/conditional variant
expected-direct-import Boolean/conditional descriptor
expected-vtable Boolean/conditional descriptor
conditional stored Function
```

In particular, the expected-direct descriptor precedes the expected-vtable
field; the older checked-reference composition sketch's opposite ordering is
not selected. In G107 the inherited foreign-interface directory count and
foreign-member discriminator remain **zero**, as in the implemented bounded
106 selection. General source interfaces are not smuggled through that native
directory. Preserve the existing complete callable/pointer contracts,
reference/optional rows, nominal-origin and enum directories, fixed-array
geometry, U coverage and checked output-state rules; G adds no new variant
to any of them except the explicitly scoped formal type leaves below.

The header is `TRRM` plus **`6B 00 00 00`** at offset 4: i32 107.
All mandatory G fields occur even when their counts are zero. No optional
trailer, feature sniff, `>=106` decoder or header-only conversion exists.
Revision 107 is unassigned in the inspected private revision constants and
proposal candidates; this proposal makes the candidate concrete, not stable.
104, 105, 106/-1, 106/-2 and the separate P98-I1 draft are not aliases.
Existing independently selected nongeneric artifacts may still be read by
their own exact decoders; packing into G107 requires decoded conversion.

| Alternative | Assessment |
|---|---|
| **Focused G107 extension — selected** | Reuses the existing closed type/declaration/structured-body machinery, adding explicit formal and call identity where missing. Every retained original expectation remains representable. It does not serialize proof scaffolding as real layout. |
| Amend unpublished 106 with the same records | Permissible; no stability rule freezes 106. A distinct revision is clearer because new mandatory directories, type meanings and call forms are needed. Do not select both spellings. |
| Universal private declaration/type/witness/register-Body graph | Could unify generic owners, constants and complete interfaces, but migrates all closed types, contract strings, bodies and native joins and requires those wider decisions now. It is not rejected for draft compatibility; it is unnecessary for this bounded method/reference-result route. |
| Compose [P98-I1](source-interface-witness-private-persistence.md) or Standard G1 | P98-I1 explicitly has no open generic Body codec and selects an older, different private composition. G1 supplies a Standard Body carrier, not this private one. Neither closes this gap by implication. |
| Persist only closed specializations or dump `ProofProgram` unchanged | Loses arbitrary new actuals, original expectations, formal opacity or operator/helper binding. A successful producer-local instance does not validate or publish an open definition. Rejected. |

### Shared notation, capacities and identity

All integers are little-endian. `str` is the inherited i32 UTF-8 byte length
and strict UTF-8 bytes, without terminator or padding. `P` means the complete
inherited 106 `ModuleFunction` record above, **without** the G attachment
introduced below. `P` is decoded structured data, not an opaque escape hatch.
Each length-delimited P must consume its extent exactly. It has no module
header and is parsed under G107, including G's scoped types and expression
55; it is not an embedded revision-106 artifact.

`R(T)` is a **one-based u32** row reference into this artifact's table T;
zero is absent only where explicitly permitted. It is not a Standard row,
DefId, token, native address or zero-based Function index. Positional
ordinals are zero-based u32. All counts/byte lengths below are nonnegative
i32. Unknown tags and nonzero reserved fields reject, without another parse.

Retain the existing private limits: 4 MiB semantic artifact, 100,000 total
charged nodes/rows/edges, depth 128, 1,024 ordinary functions, 256 variables
per stored Function and 1,024 UTF-8 bytes per ordinary string/Contract.
Additionally admit at most **32 generic declarations**, each with **1..8
formals**. Counts, nested P bodies, defaults and repeated constraints charge
the same artifact budget; there is no fresh budget per template or embedded
record. These are private capacities, not accepted-language or portable
interchange minima. Preflight remaining extents and checked products before
allocating; resource refusal never returns a partially successful catalog.

Module identities use the current private identity-input grammar:
`[A-Za-z][A-Za-z0-9_]{0,63}`, compared ordinally and case-sensitively.
Declaration/export/member keys are nonempty strict UTF-8, at most 256 bytes,
compared by decoded ordinal text, with no case folding or Unicode
normalization. A key is an explicit producer assignment, not a source path,
display name, source ordinal, hash of layout or specialization spelling.
Native filename collision checks remain separate.

An owned declaration identity is `(defining Module identity, declaration
key, declaration kind)`. An expected declaration has a separate local view
identity and a selected provider/key; it becomes a proxy for the resolved
declaration, not another nominal identity. Distinct expectation views never
merge just because they currently select one provider.
Owned keys are unique per Module/declaration kind; expected local view keys
are unique per using Module. Co-located copies of an owned identity can
deduplicate only after complete context/contract/body-closure agreement;
conflicting selected revisions reject rather than choosing an occurrence.
Identity inputs must capture generic declarations and operator origins for
the exact bound snapshot, extending the role of
[`PrivateModuleIdentityInputs`](../../sources/Terrum.CodeAnalysis/Modules/PrivateModuleIdentityInputs.cs).
The present string-only protocol and name/ordinal-only method carriers lack
these assignments. Implementation must retain them, not invent them at load.

## Private records

### Directory placement

After the inherited nominal-origin/enum directories and zero
`foreignInterfaceCount`, and immediately before ordinary `ModuleFunctions`,
insert one root identity and these **three mandatory tables**, in this order:

```text
str rootModuleIdentity
i32 genericFormalCount
GenericFormal genericFormals[genericFormalCount]
i32 genericMethodCount
GenericMethod genericMethods[genericMethodCount]
i32 operatorEvidenceCount
OperatorEvidence operatorEvidence[operatorEvidenceCount]
i32 ordinaryFunctionCount
GModuleFunction ordinaryFunctions[ordinaryFunctionCount]
```

`rootModuleIdentity` identifies the Module whose reference/export surface this
artifact presents; capture it explicitly, never infer it from the first
function, filename or native package's provider field. It uses the Module
identity grammar above. Packing may retain owned declarations from other
Modules under their original identities, without republishing their exports
through this root. An owned native producer's outer identity must equal its
semantic root; a native consumer's outer provider identity is instead a
dependency selection and need not equal its own semantic root.

`GModuleFunction` is P with the mandatory attachment field below. Generic
methods are not also ordinary Function rows. Earlier reference-type rows
may forward-reference formals; formal rows may forward-reference methods,
and generic bodies/evidence may forward-reference ordinary functions.
Decode bounded records first and validate the complete typed closure before
use. No reader validates an open referent as a concrete struct prematurely.

### Formal ownership and complete types

Each `GenericFormal` is:

```text
u32 ownerMethod                    // R(GenericMethod), required
u32 ordinal
str name
u8 formalKind = 1                  // type
u8 ownershipDomain = 1
u8 nullabilityDomain = 1
u8 defaultKind                     // 0 absent, 1 type term
i32 defaultType                    // zero filler when defaultKind=0
i32 requirementCount
OperatorRequirement requirements[requirementCount]
```

The containing method owns one exclusive contiguous range of these rows;
ordinals are exactly `0..formalCount-1`. Every formal row belongs to exactly
one range, including unused formals. Names are nonempty and unique within
that range, but are not identity or substitution keys. A type default may
use only earlier formals of that method and G107 type constructors, must
satisfy the domain and constraints for every admitted earlier substitution,
and retains its complete identity. `defaultKind=1, defaultType=0` is a unit
type default, not absence. Forward/self/default cycles reject.

**No constant-formal row or constant-actual tag is assigned in G107.**
`formalKind=2`, non-1 domain tags, explicit Copy/property requirements and
unknown kinds cannot be decoded as this row with some fields ignored.
Mixed type/constant declarations require another explicitly selected codec.

For zero-based formal row `i`, assign private i32 type code
**`int.MinValue + i`**, for `0 <= i < genericFormalCount`.
This is a symbolic leaf denoting that row's owner/kind/ordinal, never a
storage layout. The permitted band is disjoint from inherited checked types
`-1-i` for at most 100,000 reference rows. Other negative values reject.
Compute both bands using checked wide arithmetic, not `Abs(int.MinValue)`.

All inherited nonnegative type codes retain their meanings. The existing
eight-byte reference/optional table may recursively contain a formal leaf;
its constructor and qualifier rules remain unchanged. G107's open terms
are exactly formal leaves and those reference/optional constructors over
formal or complete inherited closed terms. Qualification and formation must
be justified over the whole declared domain; successful closed use does not
repair an invalid open term. Normalize after substitution and intern any
new closed reference rows then. A repeated optional layer normalizes under
the existing rules; distinct inner/outer qualifications do not collapse.

Do not put formal-dependent geometry in Structures, Classes, arrays, pointer
rows or function-pointer signatures. Their inherited records describe closed
facts, not layout recipes. They can still be closed leaves of an open method.
Unrepresented open constructors, owner instances or required runtime
agreements refuse transport/use rather than becoming closed placeholder types.

At every inherited textual type position add precisely **`gformal#i`** for
the same zero-based formal row, with unsigned minimal decimal spelling.
`ctype#i`, `fnptr#i`, `enum#i` and existing pointer productions retain their
own namespaces; nested reference constructors use `ctype#i` rows.
Parse and remap grammatical type atoms, not substrings in names or strings.
All binary type positions, declaration contracts, variables, expression/place
types, default terms, result terms and actuals use the same resolver.

A use containing formal leaves is valid only in that owner's declaration,
defaults, constraints or body, including a call's actual terms in that body.
A callee's formal is not implicitly in the caller's scope. A closed
ordinary function/instance may retain a generic origin marker but **no open
formal type use**. Cross-owner sharing must clone the term or refuse; global
row equality supplies no lexical capture.

### Generic method declaration and body

Each `GenericMethod` is:

```text
str usingModuleIdentity
str localDeclarationKey
str lookupName
u8 visibility                      // 1 internal, 2 public
u8 originKind                      // 1 owned, 2 expected
u16 reserved = 0
i32 exportKeyCount
str exportKeys[exportKeyCount]
if originKind=2:
    str providerModuleIdentity
    str providerExportKey
    u32 targetRequirement           // inherited 0 semantic or 1 Windows x64
    u8 runtimePolicy               // inherited 0 fail-fast, 1 checked-exceptions
    u8 reserved[3] = 0
u32 formalFirst                    // R(GenericFormal), required
i32 formalCount
i32 parameterLabelCount
str parameterLabels[parameterLabelCount]
u32 bodySourceUnit                 // owned nonzero semantic unit; expected zero
i32 declarationByteCount
u8 declaration[declarationByteCount] // exactly one P
```

`lookupName` is the selected namespace-qualified source lookup label, not a
link target. Export keys are unique aliases for this owned public definition;
internal definitions and expected views have none. A producer may assign an
arbitrary public key independent of the label. Within a defining Module, an
export key selects only one declaration/kind across the new and inherited
export surfaces; an ordinary function and a generic method cannot publish
different definitions under the same key. Expected views select one
provider identity/key of generic-method kind. Binding preserves alias equality
and checks every retained expectation, including uncalled views.

The P Name and Contract's name field equal `lookupName`; construct this
declaration name rather than copying the proof function's synthetic name.
Visibility agrees with the row. Caller-unsafety and all signature facts agree
between Contract and any stored Function; the bound catalog derives its unsafe
fact from that declaration rather than a second wire Boolean.
It is a module-level ordinary callable: no declaring type/receiver,
constructor, accessor, cleanup, native callable/import/vtable or intrinsic
declaration role. The inherited module-level static flag has its ordinary
meaning; it supplies no hidden receiver. Unsupported effects/contracts remain
unsupported, not zeroed. All general Dependencies, checked output states,
coverage and CopyOrigin fields are present outside `hasFunction`.

For an owned row, P has a real, nonexternal Function and `Import` equals
whether there are export keys. For an expected row, P has `Import=true`,
`hasFunction=false` and the complete original expected signature/contracts.
The selected provider must eventually supply an owned open implementation;
an empty Function, native address or one closed specialization cannot do so.
There is no bodyless-owned-definition alternative in this selected codec.

`parameterLabelCount` equals the explicit logical parameter count, including
unit positions; labels are nonempty and unique. They supply independent
source-call binding and agree with stored parameter names when a Function is
present. G107 does not add an ordinary parameter-default recipe codec:
retained ordinary defaults unsupported by the base refuse transport. Generic
type defaults above are separate. Already compiled calls retain evaluated
ordinary default operands and mapping, never replay a provider's new default.

The Function is an **open checked structured body**, using inherited
statements/expressions/places plus expression 55 below. It contains no source,
syntax nodes, parser requests or fake `$T` Structure rows. Convert each proof
placeholder to its explicit formal leaf, each synthetic generic-helper call
to a declared generic target, and each synthetic operator call to its exact
requirement target. Retain all ordinary helper/type/native dependencies.
Source binding must retain typed target links before this conversion; parsing
generated names or rebinding against proof bodies is not a substitute.
Generic-target operations inside updates/compounds must elaborate to these
calls and inherited place/read/store operations with once-only place capture,
prefix/postfix results and original failure ordering preserved. An unlinked
proof placeholder or unrepresentable operation refuses transport.
Unreferenced synthetic proof scaffolding is not an implementation dependency
or a reason to promote the program's runtime profile.
Function-local slots and control transfers remain ordinary private IR.
Formal copies, pass/return, initialization and checked referent operations
have their declared semantics, not four-byte load/store semantics.
Layout-dependent operations on a formal need an admitted capability/recipe;
the placeholder's hidden field is not one.

`bodySourceUnit` is a nonzero, Module-owned semantic source-unit discriminator.
For current source inputs, capture `SourceInputOrdinal+1` in the defining
Module; remapping keeps that identity distinct from destination row numbers.
It preserves the body's access principal without source text, filenames or
debug spans. Expected views do not acquire that private access. Retained
ordinary implementation helpers use the attachment below for the same reason.
Readers may intern `(Module, unit)` pairs into their own input-ordinal space;
they must not conflate equal ordinals from different Modules or require files.
Mandatory inherited spans may be rewritten to valid zero diagnostic spans;
they never select binding or substitute for this semantic discriminator.

### Bounded static-operator requirements and evidence

This selects a **single-static-operator protocol fragment**, not structural
conformance or the complete source-interface graph. Its nominal key is:

```text
ProtocolKey:
    str definingModuleIdentity
    str protocolDeclarationKey
    str requirementDeclarationKey
    u8 fragmentKind = 1
    u8 operation
    u8 hasSelf                     // exactly 0 or 1, first operand formal
    u8 requiresUnsafeCaller        // exactly 0 or 1
```

The keyed protocol has precisely one static, bodyless operator requirement,
no bases, associated members, additional constraints/defaults or other
requirements. Its generic positions are the operand slots followed by result;
each operand is the subject formal, and the result is the complete result
term below. `hasSelf` retains the explicit self role; an ordinary operand
equal to the subject is not inferred to be self. This is a truthful
declaration fragment, not permission to discard other interface members.
Copies of one keyed fragment must agree. It is contract-only data, not an
independently exposed interface declaration or authority to author another
type's conformance.

The closed operation domain is:

| operation | Required operator and arity |
|---:|---|
| 1..6 | Unary `+`, `-`, `!`, `~`, `++`, `--`, respectively |
| 7..14 | Binary `+`, `-`, `*`, `/`, `%`, `&`, `\|`, `^`, respectively |
| 15..20 | Binary `==`, `!=`, `<`, `<=`, `>`, `>=`, respectively |

These are private assignments, not serialized `TokenKind` values, Standard
opcodes or implicit primitive witnesses. `++`/`--` require exact subject
result type. No shift, compound-assignment or extra protocol operation is
inferred from a nearby token or name.

Each formal's `OperatorRequirement` is:

```text
u8 requirementKind = 1
u8 flags = 0
u16 reserved = 0
ProtocolKey protocol
i32 resultType
i32 contractByteCount
u8 contract[contractByteCount]      // exactly one bodyless P
```

The owner formal is implicit; requirement ordinal is its position in that
formal's ordered list. The P has `Import=true`, no Function, no receiver,
exact subject-type operands in operation order and the exact `resultType`.
It retains the complete supported declared effects, unsafety, dependencies,
state and summaries, not facts found in an eventual implementation body.
Native roles are absent. Its unsafe contract agrees with ProtocolKey.
The result must satisfy the fragment's default Copy-capable/non-null domain;
it supplies no owning or affine result protocol.
This P is a normalized static **call view**: callable kind F, static flag S,
declaring-owner field `-`, Operator present, no constructor/accessor/cleanup
role or ownership-transfer flag. ProtocolKey supplies the exact declaring
interface/member identity; it is not guessed from that P's display name.
Distinct protocol identities do not merge on operator shape; duplicate
identical effective requirements reject. A call selects one exact requirement.

Each `OperatorEvidence` is:

```text
u8 originKind                      // 1 owned publication, 2 expected publication
u8 flags = 0
u16 reserved = 0
str publishingModuleIdentity
i32 subjectType                    // closed nominal inline subject
ProtocolKey protocol
i32 resultType                     // closed
u32 implementationFunction         // R(ordinaryFunctions); zero iff expected
i32 contractByteCount
u8 contract[contractByteCount]      // exactly one bodyless P
```

An owned publication belongs to the subject's exact primary nominal authority,
resolved through the inherited nominal-origin graph. An importing Module
cannot attach it to somebody else's type. It selects a real ordinary static
operator Function owned by that authority, with its complete declaration and
implementation closure retained. A dependency attachment below preserves its
body origin. The selected target must satisfy the published operator contract;
its physical name is not witness identity. Hidden targets gain no ordinary
source visibility or automatic PE export.
The evidence P is the closed normalized call view specified above. Separately
validate the selected implementation's static/operator role and exact nominal
declaring owner, then compare its mapped logical types and complete contracts.
Protocol ownership and implementation ownership are not equal Contract
strings and are not erased; this checked static mapping introduces no receiver
adapter or access upgrade.

An expected publication retains the original complete nominal subject,
protocol instance, result and callable contract, with target zero. Resolve it
through the subject's retained nominal provider binding to that provider's
owned publication, not by an unrelated name/shape search. The provider
publication supplies the selected code link. Local consumers may publish
evidence only for their own authored nominal types. Primitive authority,
generic conformance templates, inheritance/default selection and projected
witnesses need their separate carriers.

The fragment does not publish an interface source-lookup surface. Authoring
a new consumer-side conformance additionally needs the actual selected
protocol declaration through a supported source/reference contract; an
opaque key alone cannot authorize it. Without that prerequisite G107 can
still bind already-published nominal evidence, but must refuse to manufacture
new evidence from similarly named operators. Extending interface publication
is a separate gate, not a wire guess in this fragment.

The evidence key is `(resolved nominal subject, exact protocol declaration
and requirement, ordered complete protocol instance arguments)`. Build a
plural obligation environment keyed additionally by the **generic formal and
its requirement identity**. For open forwarding, a caller requirement must
establish each callee obligation after substitution. For closed actuals, bind
the exact owned/expected evidence above. One operator target or one witness
never stands for every constraint on every formal. Conflicting publications,
missing evidence or an unsupported implication reject affected binding.
There is no runtime witness search or additional ordinary argument.

### Closed functions and implementation dependencies

In every ordinary `GModuleFunction`, immediately after
`HasOwnershipTransfer` and before NativeOutput, insert:

```text
u8 genericAttachment
if genericAttachment=1:            // closed method instance
    u32 method                     // R(GenericMethod), owned or expected view
    i32 actualCount
    TypeActual actuals[actualCount]
if genericAttachment=2:            // retained nongeneric implementation helper
    str definingModuleIdentity
    str declarationKey
    u32 bodySourceUnit             // required nonzero
```

Tag **0** has no payload. Other tags reject. Tag 1 has exactly one closed
actual for every method formal; its ordinary signature and all declaration
contracts agree with the fully substituted originating view. Tag 2 requires
a bodyful helper, preserves its declared access principal and creates no
export. Use it for ordinary private helper/operator code retained in generic
implementation closure. An instantiated function that also serves as a helper
uses tag 1; its origin already comes from that method.

Attachments are semantic identity/ownership, not proof that code is valid or
permission to substitute a cached instance for the public open definition.
They survive traversal, verification and re-emission. An expected declaration
and the selected provider remain separately retained even when a closed
instance or helper body has been copied into a jointly lowered image.

### Actuals and expression 55

Each `TypeActual` is exactly eight bytes:

```text
u8 argumentKind = 1
u8 flags = 0
u16 reserved = 0
i32 completeType
```

Actual position is method-formal ordinal. Complete terms may contain only
in-scope **caller** formals in an open body; attachments require closed terms.
All slots are explicit, including unused/defaulted formals. There are no
inference holes, missing slots, default markers, split type/constant lists or
type names to resolve later. Constant actuals have no G107 tag; neither raw
integer bytes nor another proposal's ConstantSource tag may occupy this row.

Assign private expression **55 (`37` hexadecimal) = GenericCall**. Tags 1..54
retain their base meanings, including reference operations 49..54; statement
allocations do not change. After the inherited 13-byte
`u8 tag; i32 resultType; i32 spanStart; i32 spanLength` envelope, write:

```text
u8 targetRole                      // 1 direct method, 2 operator requirement
u8 flags = 0
u16 reserved = 0
u32 target                         // R(GenericMethod) or R(GenericFormal)
u32 requirementOrdinal             // zero filler for direct role
i32 actualCount
TypeActual actuals[actualCount]
i32 inputCount
u32 parameterOrdinal[inputCount]
Expression inputs[inputCount]
```

Payload extent is exactly `20 + 8*actualCount + 4*inputCount + sum(inputs)`;
including the envelope it is `33 + 8*A + 4*I + sum(inputs)`. There is no
target Function index, native address, specialized function name or serialized
unsafe-authorization Boolean.

For role 1, target is the exact owned declaration or expected view,
`requirementOrdinal=0` and actual count equals its formal count. For role 2,
target is a formal of the enclosing generic method and the ordinal selects
that formal's exact OperatorRequirement; actual count is zero. Its dispatch
evidence is that precise open requirement, closed later through the plural
environment. General interface requirement/evidence forms are not aliases.
Direct calls carry no operator dispatch evidence.

Inputs occur in evaluation order. The parameter-ordinal vector is a complete
bijection to the substituted target's logical parameters and preserves named
argument mapping; unit positions are not omitted. Preserve the inherited
canonical implicit-reference materialization, once-only staging and checked
output preparation. The envelope's result type equals the substituted
contract's result exactly. Scalar, reference and initialization results do
not gain a different publication phase from being generic.

Inherited tag-5 ordinary calls may not target an open declaration or an
ordinary function with attachment 1: use 55 and retain the origin expectation.
Inside open bodies, ordinary nongeneric helper calls keep their existing
resolved private call edges and complete dependency closure. Proof-only
external placeholders and names such as `Helper<$T>` never stand for code.
For role 1, lowering to an ordinary BoundCall retains its method instantiation
and selected binding, agreeing with the target's attachment 1. Role 2 instead
retains its selected requirement/evidence binding and operator body origin;
it is not a new method instantiation.

When re-emitting a separately closed Function, substitute every type actual
and replace role-2 calls by base ordinary calls to the selected operator.
Retain the containing instance attachment, original declaration/expectation
and complete operator evidence environment. No out-of-scope formal/requirement
selector remains in a closed body; direct role-1 calls can remain 55 with
closed actuals. This is decoded specialization, not dropping the generic
origin or relabelling a placeholder call.

## Binding, reference results and verification

Resolve a method's provider/key/kind and declaration identity **before**
comparing or substituting. An expected declaration's type atoms, defaults and
contracts use its own table/context; actuals use the caller's. Correspond
expected/provider formals by kind and ordinal, not name or physical row.
There are no owner type arguments in G107; needing owner substitution is an
unsupported constructor/owner gate, not permission to guess it.

Substitute capture-avoidably through complete type graphs, defaults,
requirements, helper calls, variable/place/expression types and every
type-bearing contract. Forwarding to the same declaration uses distinct
caller and callee instance environments. Normalize and check each actual's
domain, every formation/constraint obligation and the resulting signature.
Inference, overload selection and selecting a type default happened earlier.
Do not substitute a replacement's current default or choose another overload.

The generic P's **general Dependency rows remain authoritative**. For a
normal result's `DependsOn`, use inherited `destinationKind=1`,
`destinationOrdinal=0`, phase 1; formal sources use `sourceKind=1` and their
zero-based **ordinary parameter** ordinals. Broad uses relationship 3;
backing-only uses relationship 2. Receiver sources are invalid for these
module-level declarations. Explicit empty relationships keep the inherited
zero-source row, distinct from an absent/unsupported relationship.
These are not type-formal ordinals, Function slots or current argument order.

At a call, map source ordinals through the parameter-ordinal vector to the
already staged actual values/places. Preserve every permitted source and its
storage generation, carried backing/root/guard/code lifetime, loan extent,
access and invalidation endpoint. A result selected from either of two
references remains bounded by **both declared possible sources**, not just
the branch observed during specialization. A copied reference descriptor
still borrows its designated storage; it does not borrow the callee's
descriptor slot or magically become backing-only.

Substituting a reference type for T does not flatten nested references or
upgrade readonly storage. An init capability remains non-Copy, with its fixed
destination and outcome correlation. Checked output-state vectors,
CopyOrigin, raw-Union coverage and NativeOutput remain separate contracts;
normal reference publication supplies neither raw pointee initialization nor
failure-path rollback. A concrete byte-coverage summary cannot be applied to
an opaque formal's unknown geometry: such a generic declaration needs a
separately representable symbolic guarantee or must refuse this codec.
Likewise, a summary's field path needs a closed declaring owner; an opaque
formal cannot acquire field identities from its proof scaffold. Whole-value
copy-origin forwarding can retain its existing path-empty contract.

Before affected consumption, mandatory checking includes exact schema and
P extents, counts, tags, strings, all row/range ownership, formal scope and
default acyclicity, valid complete type closure, signature/metadata/body
consistency needed for safe consumption, source-ordinal bounds, supported
contracts, provider satisfaction and complete operator bindings. Known
layout/state/initialization/unsafe obligations are not erased by substitution.
Expression 55 reconstructs authorization only in the inherited explicit
unsafe block/expression context; an unsafe declaration still requires those
contexts for unsafe operations in its body.

Producers must check authored generic bodies over the **entire declared
domain** and publish truthful facts. Source-free consumers may rely on
structurally consumable published body facts; G107 does not require rerunning
source binding or semantically re-proving every open/closed body. Creating
a new substitution/provider binding nevertheless checks its new complete
obligations and safe representation. Optional semantic audits remain separate.
Running the current stronger verifier is not authority to replace missing
metadata with newly inferred facts or to treat placeholder layout as proof.

## Independent routes, remapping and replacement

`CompilationReference` must expose the generic signature/default/constraint
and parameter-label catalog while retaining an immutable, source-free provider
implementation snapshot/handle for later instantiation. The public ordinary
Functions facade need not expose private bodies. A deliberately signature-only
reference can bind a declaration but cannot execute a new instance until the
selected provider supplies its checked implementation closure.
Reference lookup exposes only the selected root Module's public export
surface; retained transitive owned definitions and hidden helper bodies are
not automatically republished.

Private producer output retains all selected public/open definitions and the
internal generic/ordinary/type/operator dependencies needed by their bodies,
even with no producer-local call. A consumer saves its **own bodyless expected
declarations**, bindings and 55 calls. Its closed function cache is not the
only remaining evidence. Independent ordinary, set and graph-library readers
restore the same graph; no route obtains a missing body from source or from
another format.

| Decoded edge | Required rewrite/preservation |
|---|---|
| Generic declarations/formals | Remap method/formal references, exclusive ranges and owner/ordinal correspondence, all type defaults and requirement contracts. Rebuild catalog ordinals separately from physical ordinary Function indexes. |
| Formal type leaves/reference rows | Map the formal row and all recursive `ctype#i`/`gformal#i` atoms and binary uses in the correct using context. Closing a formal replaces its term, not its numeric row globally. |
| Expression 55 and attachments | Remap generic/formal target, requirement ordinal if reordered, ordered actual terms and nested inputs/parameter mapping. Reconstruct selected closed Function indexes and retain exact origin agreement. |
| Operator evidence/helper closure | Remap nominal subject/result types, exact protocol/member identities, implementation Function reference and complete original contracts; preserve primary authority and body access principals. |
| Base types/contracts/bodies | Apply all existing struct/class/enum/pointer/signature/array/reference/place/Dependency/CopyOrigin/coverage/native expectation maps. A scalar leaf reached inside an open signature does not excuse a missing nominal origin elsewhere. |
| Artifact/native framing | Rebuild string/record counts and byte lengths, declaration extents, embedded Contract lengths, package offsets/hashes and actual import/export lists. Diagnostic spans are not reference slots. |

Use shared bound-tree traversal/rewriting for proof declarations, defaults,
requirements, signatures, contract expressions, typed places, actual lists,
attachments and helper bodies. Visiting only the root `Functions` array or
rewriting just `BoundCall.Type` loses the generic contract. Source-free
re-emission may reorder tables subject to inherited ordering rules and rename
physical helper symbols but retains
semantic origins, all expectation views and alias equalities. Decode and
rewrite typed edges; never substitute matching bytes/text. Canonical writers
group formals by method and ordinal, preserve semantic argument/input order,
sort export keys and deduplicate only equal terms/evidence in one context.
Ordinary readers accept consistent method/range/table reorderings and
otherwise valid unused retained rows, not dangling or multiply owned ranges.
The base codec's canonicality/admission rules are not relaxed by this sentence.

Replacement is directed satisfaction of **every** original view plus every
stronger fact actually used. Formal/local names, physical row order and a
private body change need not break binding. Arity/kind/constant-domain changes,
stronger caller constraints or unsafety, lost result/state/backing guarantees,
changed nominal identity or incompatible witness/alias/target/runtime
relationships do. A supported relaxation can satisfy an old caller domain;
it never deletes that caller's retained old obligation. G107 assigns no
general constraint implication solver: exact supported identity/substitution
and complete available evidence must establish each implication, otherwise
decline the replacement.

Packing can bind an expected row to an owned definition but must retain the
expected row and its provider/key/context as a view; it cannot overwrite it
with the provider's current metadata. Equivalent aliases can share code but
not erase their separate expectations. Sets independently remap each selected
provider; graphs retain transitive generic/operator/type/code dependencies,
including edges with no machine call. Provider order, names or equal layouts
cannot choose an ambiguous origin. Keep the existing selected graph topology
and resource limits; generics do not legalize dependency cycles.

## Closed lowering and native containers

Only after binding/support checks, instantiate the selected open structured
body from metadata, substitute types/contracts, resolve every operator
obligation and recursively close helper instances. Build ordinary checked
BoundFunctions with retained instantiation identity and ordinary BoundCalls
whose result/dependency/ownership facts match them. Recalculate concrete
layout-dependent implementation data under the selected target; never copy
opaque proof geometry. Equal exact recursive instances may share work;
unbounded/growing specialization or exhausted budgets refuses completion.

An instance/cache key includes resolved declaration, ordered complete actuals,
each original expected view, plural operator bindings, selected provider
revision/binding generation, exact schema/rules, target, native lowering
agreement and runtime policy. Provider rebinding invalidates affected cached
decisions. Spelling a function `Select<int32>` is neither that identity nor an
export key.

Keep TNPK/TNSP/TNGP **outer version 3 field layouts**, selecting the exact joint
agreement **outer-3/G107** for embedded semantic Contracts. No new outer field,
Standard payload or generic PE ABI is needed. Their validators must make
these semantic distinctions:

* Generic export aliases live in the inner generic table, **not** PE exports
  or a requirement for an open machine entry. Ordinary Function image exports
  and NativeCallable entry names retain their existing rules.
* Existing outer import-name/machine-import lists continue to describe
  ordinary native entries only. Inner G107 bindings carry generic-only edges.
  The selected outer provider identities must include every such edge even
  when its ordinary import list and IAT usage are empty. Extend declaration
  closure validation accordingly; do not fabricate an import to satisfy an
  old set-equality check.
* Materialize a generic instance and its lawful jointly lowered helper
  closure in the consumer/root image before emission/JIT. Retain their
  original semantic access principals and obligations. This grants no
  independent source access to a hidden implementation.
* Cross-image calls still need real ordinary entries and the selected
  complete ABI. Copying a body that depends on provider-owned static state,
  unavailable private code, roots or an unadmitted crossing is not an
  implicit adapter. Retain an admitted linkage or refuse that native route.
  New reference-result tests can keep generic reference operations image-local
  after semantic specialization; they do not prove a new DLL reference ABI.
* Native graph JIT may instantiate the root from retained semantics but does
  not regenerate selected provider DLLs. Persisted-image execution validates
  its retained closure and runs the existing images; it does not secretly
  recompile on an incompatible replacement.

Semantic relinking/JIT and unchanged precompiled execution are separate
acceptance paths. For unchanged images, every retained expected contract,
stronger specialization fact, helper/type/code/load dependency and physical
ABI must still hold. A compatible source-level generic signature alone is
insufficient. If those conditions cannot be established, reject; source
rebinding, fallback JIT, hot patching and hot unload are not selected.

Both `fail-fast` and `checked-exceptions` preserve the same generic identities,
substitution, loan and publication rules. Retained provider policy must match
the selected route; no policy roll-forward is inferred. Preserve ordinary
Check/Prepare/Invoke/normal Publish order, no normal result after failure,
staged-owner cleanup and settlement on every outcome. Serialization neither
selects normal-backing/integrated-GC support nor admits an unsupported reference,
managed handle, owner or callback runtime boundary.

## Acceptance regressions and remaining gates

These are required implementation cases, **not executed results**. Use public
compiler APIs, independent encoded fixtures and independently compiled
consumers with producer source unavailable. Reusing the motivating selection
sample or only a writer/reader round trip is insufficient.

| Group | Required cases/outcomes |
|---|---|
| Open publication | Uncalled public identity and two-reference selection templates; two unrelated producer APIs and independent scalar/Copy-aggregate consumers. A new consumer actual absent from the producer must close and execute; retain every formal and original declaration. |
| Reference results | Preserve exact readonly/write and nested reference terms, result `DependsOn` ordinary source ordinals, named argument reordering and both possible supporting sources. Early source retirement, authority upgrades, dangling local/callee storage and invented backing-only permission reject. |
| Forwarding and recursion | An internal template forwards/reorders two caller type actuals to a public helper, then closes at an independent root. Test same-declaration recursion with distinct caller/callee environments, unused formals and finite exact recursion versus growing expansion. No source or inference replay. |
| Defaults/labels | Earlier-formal and closed type defaults, explicit supplied actuals overriding defaults, unit type actual/default and named ordinary inputs. Missing/extra actuals, inference/default holes, current/forward/out-of-scope defaults and unsupported ordinary default recipes refuse. |
| Operators | Multiple requirements on multiple formals; different protocols with the same operator shape; caller-formal evidence forwarding and already-published nominal actual evidence. Consumer-authored evidence additionally tests the original protocol-declaration prerequisite; an opaque fragment alone must refuse that authoring route. Preserve exact protocol/member/subject/result and every contract; reject missing/conflicting/foreign-authored evidence, extra unsafe obligations and name-only matches. |
| Generic versus nongeneric | Keep a same-labelled ordinary function, an open method, two providers with equal labels and closed caches distinct. Tag 5 to an open/attached instance, a forged specialization name or a proof placeholder masquerading as a concrete type rejects. |
| Bodyless expectations | Save and reload a consumer with unused expected methods, defaults, Dependencies, checked output states and operator publications. Removing a stored Function must not erase the complete expectation or copy new provider facts into it. |
| Compatible replacement | Rename formals/locals, reorder all relevant provider tables, change a private body and keep public keys/contracts. Bind every retained view and regenerate only in a semantic-lowering route; independently exercise unchanged-image admission under its full retained facts. |
| Incompatible replacement | Change arity/kind/domain/default contract, unsafe/effect/output/dependency promise, nominal origin, operator mapping or alias equality; use a different policy/target or missing provider. Reject the affected binding before execution, including a conflicting uncalled view. |
| Sets and graphs | Independent flat set, three-module forwarding graph and same-origin diamond; disjoint and duplicate labels, reversed provider order, metadata-only generic edges and ambiguous selections. Deduplicate only identical origins; preserve hidden code and original expectations without inventing machine imports. |
| Re-emission/remapping | Strip diagnostic source data, erase expected Functions, re-emit producer/consumer/set/graph contracts and independently permute methods, formal ranges, ordinary functions and every nested type table within its inherited ordering rules. Type actuals, textual atoms, requirement references, helper origins and inner/outer lengths/hashes must all be rebuilt. |
| Boundaries | Zero G counts; one and eight formals, 32 methods and refusal beyond private limits; first/last rows, reference versus formal negative-code bands, int.MinValue, string limits, node/depth/artifact exhaustion and checked extent arithmetic. Zero-data actuals keep logical inputs and side effects; invalid `ref<void>` formation still rejects. |
| Malformed bytes | Truncated table/row/string/P/input lists, surplus declaration bytes, negative/oversized counts, unknown tags, nonzero reserved/filler fields, wrong namespaces, missing/overlapping ranges, duplicate ordinals/keys and dangling/cyclic type/default references reject safely without another decoder. Include hand-authored fixtures, not only mutated writer output. |
| Mandatory verification | Mismatched closed attachment/call identity, result type, parameter mapping, state/dependency source ordinal, operator contract and forged owned/expected visibility/binding shape reject. Invalid authored open bodies fail full-domain source checking; a deeper loaded-body truth audit remains explicitly optional. |
| Native/runtime policies | For each policy, supervised private producer/consumer, set and graph root-JIT plus emitted-image execution of generic reference-result consumers; observe the selected referent/value, once-only staging, lifetime preservation, successful/failed publication and cleanup. Policy mismatches and unsupported native/reference agreements refuse before launch. No visible-window/hardware opt-in. |
| Closure and cache | Replace a provider/operator/type dependency after a prior instance, change a retained stronger fact, remove a private helper or require unavailable provider-owned state. Invalidate/rebind semantic caches or reject unchanged native code; do not use stale acceptance or silently regenerate provider images. |

Implementation must connect the new bound identities and source-free open IR
through binding, `CompilationReference`, mandatory verification, shared
traversal/rewriting, all private producer/consumer/set/graph codecs and closed
NativeModule/JIT lowering. Current source-bound proof programs are a useful
input, not the persisted program as-is. Normal developer builds and focused
active regressions must cover the final relevant compiler/configuration and
artifact inputs before a support claim; emission is not runtime evidence.

**Still outside this selection:** constant formals and mixed type/constant
actuals/defaults (including exact enum/Transparent domains and symbolic value
forwarding); unique/nullable domains and property branches; declaring-owner
generics and general constructed/open type families; complete nominal
interface inheritance, associated identities, defaults, primitive witness
authority, qualified-self and existential evidence; general constant programs,
shared-generic execution and new cross-image/runtime agreements.
These require their own complete carrier/admission decisions, not borrowed
Standard records, P98-I1 tags or sample-specific substitutions. Existing
pending semantic counterparts remain unapproved. None is needed to choose
G107's bounded type-formal/reference-result route, and none is implicitly
approved by choosing it.
