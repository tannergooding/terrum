# Exact native calling-convention source schemas

**Status: unapproved; provisionally actionable under the authorized
missing-contract workflow.** The applicable authorization is:

> At a genuine missing contract, prepare the smallest concrete decision needed as a new proposal doc using gpt-6.1-sol at max reasoning, then continue with it being treated as a provisional approval/direction.

This recommendation supplies a missing source/Core contract. It changes no
normative specification, registry, compiler or persistence schema and
establishes no implementation support.

## Decision and scope

Recommend one optional, **named-only `CallConv`** argument on the registered
`Terrum.Interop.NativeCallable` schema and on callable
`Terrum.Interop.NativeImport`. Its exact type is the Core-registered
`Terrum.Interop.NativeCallingConvention` enum; omission selects its
`Unmanaged` member. An interface's NativeCallable annotation supplies the
default for its directly declared methods. A method annotation, including
a bare annotation selecting Unmanaged, overrides that default for that
method only.

Complete the NativeImport argument schema below so that its CallConv is
not attached to an otherwise unspecified overload. Preserve the existing
import resolution behavior, native declaration roles and one effective
convention per method. Do not add a convention attribute, alias, implicit
conversion or convention inferred from a receiver, vtable slot or target.

The standard **MethodDef-only `0x000C` NativeCallable clause is already
assigned**, including its 12-byte extent and convention values
`1=Unmanaged`, `2=Cdecl`, `3=Stdcall`, `4=Vectorcall`. This proposal does
not allocate or renumber them. The separate NativeCallConv attribute
immediately before `fnptr` keeps its existing type-use contract.
The exact per-Module Core enum/schema binding prerequisite is specified in
[Native calling-convention Core registration](native-calling-convention-core-registration.md).

### Exact owners and the gap

| Owner | Preserved contract and missing agreement |
|---|---|
| [Native declaration roles](../specs/native.md#native-declaration-roles) | Owns callable-import CallConv/Unmanaged, NativeCallable's effective convention, interface defaults, inherited declaring-method conventions and export implication. It lacks their complete registered source arguments/Core member agreement. |
| [Grammar §3.1](../specs/grammar-spec.md#31-source-file-structure-and-declarations) | Owns registered declaration attributes, ordinary `name:` labels, checked constant arguments and the distinct NativeCallConv-before-fnptr placement. No new production is needed. |
| [Contract clauses §2](../specs/representation/contract-clauses.md#2-clause-matrix-and-uniqueness), [registry](../specs/representation/trrm-1.registry.json) | Own the unique MethodDef clause, existing closed convention domain and expansion of interface defaults onto declared methods. This is not a missing numeric allocation. |
| [Metadata](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics) | NativeImport already has `callConv`; NativeExport references the owner's unique callable clause. TypeSpec form `0x0D` independently carries its function-pointer convention and bodyless signature anchor. |
| [Core authority](../specs/libraries/adopted-inventory.md#1-status-and-identity), [binding](../specs/representation/binding-and-replacement.md), [witnesses](../specs/language/interface-witnesses.md) | Require exact registered and selected declaration identities, complete retained contracts and checked implementations, not spelling or equal machine carriers. |
| [Windows member ABI](../specs/targets/windows-x64-call-interface.md#3-physical-argument-positions-and-stack) | Owns physical mappings after effective convention selection, including receiver/hidden-result order and rejection of unsupported mappings. |

The [foreign-interface NativeOutput identity](native-output-foreign-interface-method-identity.md)
and [private persistence](native-output-foreign-interface-private-persistence.md)
candidates consume an effective convention but do not select this source
schema. They remain unapproved dependent consumers, unchanged here.

### Alternatives

| Candidate | Assessment |
|---|---|
| Add CallConv to the existing role schemas, with the exact Core enum and normalization below | Smallest complete amendment. The declaration that establishes the role also selects its convention; existing normalized metadata is sufficient. **Recommended.** |
| Keep NativeCallable as a marker and introduce a separate declaration-only convention-selector schema shared by imports, methods and interface defaults | Viable with a new Core identity, exact role/selector pairing, missing-selector rules and cross-attribute precedence. It separates declaration convention authority from role authorship without improving this bounded contract. It must not overload the existing function-pointer-only NativeCallConv schema. |

The recommendation is not chosen to preserve prototype syntax. No
`Native`, `NativeAbi`, `CallingConvention`, positional-convention overload
or historical decoder is retained as a compatibility alias.

## Candidate Core registration and source arguments

All source identities and schema details in this section are **proposed**.
Core supplies an immutable, authorized registration in each using logical
Module's binding context:

| Registered role | Candidate public source identity and required shape |
|---|---|
| Callable convention enum E | `Terrum.Interop.NativeCallingConvention`: public nongeneric primary enum, exact `int32` base, exactly the four distinct member definitions below; no Flags role, extra members or duplicate-valued aliases. |
| Import schema I | `Terrum.Interop.NativeImport`: public nongeneric primary semantic attribute schema with the complete arguments below. |
| Callable schema C | `Terrum.Interop.NativeCallable`: public nongeneric primary semantic attribute schema with exactly the optional named CallConv argument below. |
| Existing pointer selector P | `Terrum.Interop.NativeCallConv`: the separate registered function-pointer type-use schema; its explicit enum argument binds the same E, not another same-shaped enum. |
| Existing export role X | `Terrum.Interop.NativeExport`: the registered native-export role whose method form implies C's normalized role. No new export argument or symbol schema is selected here. |

| Exact member of E | Public enum value | Existing standard convention byte |
|---|---:|---:|
| `Unmanaged` | 1 | 1 |
| `Cdecl` | 2 | 2 |
| `Stdcall` | 3 | 3 |
| `Vectorcall` | 4 | 4 |

These are exact member definitions owned by E, not globally recognized
member-name strings. Validate E's base, complete member set and this mapping
when admitting its registration. Public enum integers are not new metadata
IDs. Zero is neither an enum default for this schema nor an unspecified
convention.

Recognition resolves the registered schema, E and its members before
normalization. A supplied convention is an ordinary checked, encodable
compile-time constant of **exact E type**, with a value equal to one of
those registered members. Ordinary E-typed constant forwarding is allowed;
this adds no enum-member provenance requirement to constant evaluation.
An integer, string or unrelated enum with equal bits is not E and receives
no implicit conversion. Unknown E values reject. The closed domain is a
restriction of these schemas, not a change to ordinary enum semantics.
The [nominal enum forwarding candidate](nominal-enum-constant-forwarding.md)
specifies the shared value/evaluator integration prerequisite; a Core
registration handle or direct-member decoder alone does not satisfy it.

Registration identifies actual declarations through checked resolved handles
or exact selected Module identity/export key/kind bindings. Concrete provider
identities and keys are mandatory profile inputs, not universal names to
guess. An ordinary package cannot self-register by using these spellings or
referencing Core. Missing, ambiguous, conflicting, wrong-kind or incompatible
Core selections reject affected source binding; do not search another
provider or substitute a same-shaped enum/schema. Transparent source aliases
to the exact selected declarations preserve identity, not a second role.

Attribute schemas introduce no runtime attribute object, constructor
overload, writable property or FieldDef named CallConv. `CallConv` is the
case-sensitive argument identity of I or C, using the ordinary `name:`
grammar. Source-independent publication of Core enum/schema declarations
still requires an independently admitted declaration codec; this proposal
does not invent one. Consumers of already normalized native contracts need
not reconstruct source attributes or resolve Core merely to decode a
registered convention byte.

### Complete NativeImport schema

The two resolution argument types are proposed public nongeneric primary
Core enums with exact `int32` bases:

* `Terrum.Interop.NativeImportResolutionMode`: exactly `Link=1`, `Dynamic=2`.
* `Terrum.Interop.NativeImportResolutionPolicy`: exactly `Default=1`,
  `CompatibilitySearch=2`.

Their exact enum/member identities participate in the same authorized
registration checks as E. They type the already selected resolution
behavior, not a new loader policy or wire allocation.

| Argument | Exact type | Source form and omission |
|---|---|---|
| `LibraryName` | Compile-time string-literal argument owned by the registered schema | Required; first positional argument or named LibraryName. It is serialized by the existing metadata String rule, not represented by a runtime Terrum text value. |
| `EntryName` | Compile-time string-literal argument owned by the registered schema | Optional; second positional argument or named EntryName. Omission selects the exact declaration name. |
| `ResolutionMode` | Registered NativeImportResolutionMode | Named-only; omission selects its Link member. |
| `ResolutionPolicy` | Registered NativeImportResolutionPolicy | Named-only; on Dynamic, omission selects its Default member. On Link it must be omitted and has no value. |
| `CallConv` | Exact E | Named-only; on a callable, omission selects E.Unmanaged. On a field it must be omitted and has no value. |

Decode each source string literal once as a checked Unicode value, then apply
the selected metadata String encoding/extent rules. Do not compare raw token
spelling, admit runtime string expressions, introduce a `Text` value type or
implicitly convert another value.
[The separate LibraryName source-contract candidate](native-import-library-name-source-contract.md)
supersedes only this proposal's deferred LibraryName character/path
validation; it does not select target support. Supplied EntryName and the
default declaration name remain exact nonempty ASCII names admitted by the
selected target, without decoration, suffix inference, case folding or
replacement. No nullable/empty-string sentinel is introduced. Resolution
behavior and provider retention remain owned by native.md.

Positional arguments form only the LibraryName/EntryName prefix and precede
named arguments. Each schema argument binds at most once, including a
positional/named collision; duplicates reject even when equal. A third
positional argument, positional convention, unknown label, wrong type,
missing library or extra argument rejects. Explicit ResolutionPolicy on
Link, including Default, rejects; explicit CallConv on a field, including
Unmanaged, rejects. Contextual omission is not a runtime fallback.

### NativeCallable schema and applicability

C has exactly one optional named-only parameter:
**`CallConv: E = E.Unmanaged`**. There are no positional arguments.
`[NativeCallable]`, `[NativeCallable()]` and an explicit CallConv selecting
Unmanaged are equivalent. In particular a bare method annotation is an
explicit Unmanaged selection, not “inherit the interface's argument.”
`EntryName`, export names and loader arguments do not belong to C.

I and C are nonrepeatable, including identical repetitions and repetitions
through aliases. They are authored only on the composed declaration's
primary contract; a partial secondary cannot supply, repeat or change them.
Interface defaults apply to directly declared methods contributed by any
part of that interface, but are authored once on its primary.

| Role/target | Candidate applicability |
|---|---|
| I on a callable | An ordinary top-level extern function, represented by a static MethodDef, or an ordinary static extern method. It ends in `;`, has no authored body/secondary implementation and binds a native symbol. Instance imports reject. |
| I on data | Directly addressable static extern field, with no Terrum initializer. It has no convention or NativeCallable implication. |
| C on a method | An otherwise eligible ordinary function or method, static or instance, including an interface requirement or default-body method. It selects a stable native signature, not a symbol or vtable. |
| C on an interface | Supplies the default to its directly declared ordinary methods, instance or static, not inherited methods or members of nested types. Every affected method must be eligible for C. |
| X on a method | Existing static export restriction remains. It implies one NativeCallable role, using the normalization below, not another convention parameter on X. A field export implies no callable role. |

Untargeted placement and explicit `method:` on the callable, `field:` on
the imported field, or `type:` on the interface identify that declaration
itself. There is no named-member selector for these schemas. C does not
target classes/structs, constructors, operators, properties/accessors,
fields, parameters, results, receivers, Module/assembly or a type use.
An interface default does not turn such nonmethod members into native
callables. I's field form is its separate static-data role, not a C target.

I cannot combine with C or X on one declaration. An interface default that
would give C to an imported method is likewise a role conflict, not a
second convention source. Use separate authored wrappers when needed.
X plus an explicit C is permitted: implication and selection produce one
role, not duplicate annotations or clauses.

C alone does not authorize an ordinary concrete declaration to omit its
body, add extern, become abstract or acquire a foreign-interface role.
Bodyless interface requirements and the separately admitted bodyless foreign
role are eligible declarations; contract-only reference declarations retain
their independently valid roles. Abstract/foreign bodyless methods have no
standalone code address or generated export. A TypeSpec `0x0D` signature
anchor is noncallable: being a bodyless MethodDef does not give it C.
Default bodies and concrete implementations remain subject to ordinary
witness/body and native failure-containment rules; this proposal requires
no new default-interface runtime facility.

No placement alone admits an unsupported generic, asynchronous, variadic,
reference, ownership or target boundary. Native invocation remains
caller-unsafe, including scalar-only signatures. An unsafe declaration
propagates that obligation but does not authorize operations inside its body.

## Exact normalization, inheritance and witnesses

After binding the primary schema and arguments, normalize once:

1. A callable import takes its convention solely from I.CallConv, with
   the declared Unmanaged omission rule. A data import has none.
2. For a nonimported method M, an explicit C on M selects its CallConv.
   Otherwise C on M's declaring interface supplies that interface's
   selected value. Otherwise a method-form X implies bare C and therefore
   Unmanaged. Otherwise M has no NativeCallable role or convention.
3. Publish one effective NativeCallable contract for each such M.
   Do not retain an “inherit,” “unspecified” or zero convention for
   consumers to reinterpret. An explicit method selection supersedes its
   interface default; the default is not a second contract occurrence.
4. An inherited requirement keeps the contract of its exact closed
   declaring-member identity. A derived interface's default cannot rewrite
   it. A new directly declared same-named method is a distinct identity,
   not an inferred override. Selecting a replacement implementation/default
   for an inherited identity must satisfy that original contract.

These proposal-only declaration fragments illustrate source normalization,
not runnable support. Core/provider bindings and any interface layout or
foreign-role commitment are omitted:

```terrum
[Terrum.Interop.NativeImport(
    LibraryName: "probe",
    EntryName: "Read",
    CallConv: Terrum.Interop.NativeCallingConvention.Cdecl)]
public extern int32 Read(int32 token);

[Terrum.Interop.NativeCallable(
    CallConv: Terrum.Interop.NativeCallingConvention.Cdecl)]
public interface IProbe
{
    public unsafe int32 Read(int32 token);

    [Terrum.Interop.NativeCallable]
    public unsafe int32 PlatformRead(int32 token);
}
```

The import and IProbe.Read select Cdecl; IProbe.PlatformRead selects
Unmanaged. No layout, dispatch table, body or address is inferred.

For a native-callable requirement, the witness's selected implementation
must itself supply the native-callable role and **exactly the same
effective convention**, alongside its complete receiver, signature,
effects, output, ownership and dependency obligations. An ordinary
unannotated concrete method does not acquire C by name matching or witness
selection; explicitly annotate an eligible implementation or select an
independently authored suitable wrapper. A default body already retains
its declaring member's normalized contract.

One implementation cannot satisfy two requirements with different convention
identities merely because their physical mappings coincide. Separate
eligible implementations may satisfy the distinct identities. This adds
no implicit convention-changing thunk or conversion, including through
qualified-self forwarding or devirtualization.

Binding and replacement compare the original retained effective contract,
not a provider's current source defaults. Preserve exact selected owner/member
and convention identity, all independent expectations and declaration-only
requirements. Changing Cdecl to Unmanaged is not strengthening on Windows
x64. Dropping a required callable role, changing a Core member mapping or
reconstructing an expectation from the replacement rejects. Exact Core
registration dependencies, when retained for source/schema consumption,
also preserve their complete schema, enum/member-owner and value mappings.
They retain their originating Module's binding context; another Module's
Core selection cannot relabel an already normalized contract.

## Standard metadata, targets and dependent consumers

Normalize source into the existing carriers only:

| Existing carrier | Required mapping |
|---|---|
| NativeImport row | Callable `callConv` is the selected standard byte 1..4; a field uses zero. Existing owner, resolution and name rules remain. Link policy is zero; Dynamic policy is 1 or 2. |
| Contract `0x000C` | Exactly the existing eight-byte Contract header followed by `u8 callingConvention, u8 flags=0, u16 reserved=0`: 12 bytes total, MethodDef-only, at most one per owner. The convention is 1..4; zero/unknown values, nonzero flags/reserved, wrong owners and duplicate clauses reject. Absence means no C role, not implicit Unmanaged. |
| InterfaceDef/InterfaceMethod and method Contract ranges | Materialize the effective clause on every directly declared affected MethodDef, including uncalled/bodyless members. No interface-owned NativeCallable clause or serialized default is added. Inherited references keep their declaring contract. |
| NativeExport row | A method references its own unique `0x000C` clause; a field references zero. No copied convention, source-attribute row or export-name inference supplies it. |
| TypeSpec `0x0D` | Retains the existing explicit convention byte and bodyless, non-exported MethodDef signature anchor. NativeCallConv remains immediately before fnptr, with its one explicit E value, not a declaration annotation, func convention or cast. The anchor is not callable/addressable. |

The unapproved, provisionally actionable definition of anchor-owned
applicable Contract clauses and normalized signature identity is in the
[Standard function-pointer anchor contract proposal](standard-function-pointer-anchor-contracts.md).

Address formation and function-pointer use must preserve complete signature,
contracts and exact convention. Imports need no extra C marker to form
NativeAddress. Distinct conventions remain distinct fnptr types even where
the target emits the same machine sequence. The TypeSpec convention is that
type's identity, not authority to change its addressed method's convention.

Producers must publish valid records and truthful contracts. Consumers check
required shapes, references, owner/role relationships, complete bindings and
supported semantics before affected use, including retained uncalled
declarations. They need not reconstruct attributes or semantically re-prove
producer bodies. Missing required clauses cannot be repaired with a
consumer-side source default. Packing remaps decoded owners, signatures,
clauses/ranges, export references and call/type maps while retaining original
expectations; it does not substitute bytes or reuse row numbers as identity.

The effective convention is an **input** to the selected target mapping.
Unmanaged means that target's defined unmanaged convention, not “try other
conventions until one works.” Unknown conventions reject; known but
unsupported mappings decline affected native calls, addresses, entries or
dispatch rather than downgrade, ignore the argument or use a Terrum ABI.
This does not prevent inspecting otherwise supported normalized contracts
without generating native code.

On Windows x64, Unmanaged/Cdecl/Stdcall may share carriers while keeping
distinct identities. NativeCallable instance mappings place the receiver
before hidden result storage for these conventions; static explicit-receiver
calls do not thereby acquire member mapping. Vectorcall remains registered:
implement its complete selected MSVC mapping or reject affected use.
No new target mapping, unwind permission, NoThrow promise, marshalling,
pin, root, retain, ownership or code-lifetime extension is selected here.

### Composition with foreign-interface NativeOutput

The [identity candidate](native-output-foreign-interface-method-identity.md)
first resolves the exact declaring interface/member, then consumes this
method's normalized C contract. Its derived slot, opaque receiver type,
vptr offset and physical receiver position **never select a convention**.
Its flat bodyless role still requires its complete independent registration,
layout, unsafe declaration and target support. It admits only
Unmanaged/Cdecl/Stdcall; Vectorcall remains outside that bounded projection
even if a broader backend implements it.

The [private convention-persistence candidate](native-calling-convention-private-persistence.md)
selects an exact revision-97 projection: private `0=Unmanaged`, `1=Cdecl`,
`2=Stdcall`, `3=Vectorcall` for fnptr signatures, plus declaration-owned
NativeCallable/NativeImport convention tokens. Its foreign-interface
composition keeps the 96 identity and typed-call records but replaces the
foreign variant's convention byte with the common NativeCallable token.
Translate private values explicitly; they are not the standard bytes 1..4,
the public enum integers or casts of the current single-case host enum.
This source/Core proposal does not itself change private wire layouts.

Both consumers retain the native-output owner's existing eligible cells,
logical formal/receiver distinction, result correlation, reservations,
publication, dependencies, provider settlement and cleanup obligations.
Convention selection does not authorize outputs on an unadmitted vtable
prototype or alter any output/foreign-interface semantics.

This source/Core candidate allocates no standard section, DefId, clause,
Feature, opcode or format pair. The private candidate selects its own exact
revision without changing those standard owners. Existing native capability
closure remains required; the dependent output/interface candidates keep
their separate gates.
Exact draft agreement remains required under
[format versioning](../specs/representation/format-versioning.md); equal
headers cannot select a different Core or convention interpretation.

## Integration obligations and present gaps

These are future owner amendments, **not normative edits made here**:

| Owner | Integration after adoption |
|---|---|
| [Native roles](../specs/native.md#native-declaration-roles), [grammar §3.1](../specs/grammar-spec.md#31-source-file-structure-and-declarations), [library inventory](../specs/libraries/adopted-inventory.md) | Add the exact source/Core schemas, argument/default/target rules and normalization above; fill the unspecified source surface and applicability choices, not replace the native role semantics. |
| [Contract clauses §2](../specs/representation/contract-clauses.md#2-clause-matrix-and-uniqueness), [metadata](../specs/il-metadata.md#semantic-contracts-and-stable-binding), [registry](../specs/representation/trrm-1.registry.json) | Preserve all existing bytes/IDs; clarify source expansion, role-conflict checks and exact source-to-effective mapping without adding default or attribute carriers. |
| [Interface witnesses](../specs/language/interface-witnesses.md#nominal-conformance-and-member-identity), [binding/replacement §2](../specs/representation/binding-and-replacement.md#2-directed-satisfaction-and-exact-dimensions) | State exact convention/role satisfaction, explicit concrete implementation obligations and retained expectation/Core registration checks. Preserve other directed guarantee relations. |
| [Interface layout](../specs/data-layout-and-abi.md#fixed-directly-interoperable-representations), [Windows member ABI](../specs/targets/windows-x64-call-interface.md#3-physical-argument-positions-and-stack) | Keep layout independent of convention selection; retain physical member mapping and explicit unsupported-convention rejection. |
| [Native-boundary conformance](../specs/conformance/native-boundaries.md) | Extend the existing NBC/NBD/NBM cases with the matrix below, without claiming their implementation. |

Direct inspection establishes these **implementation gaps**, not missing
normative convention numbers:

| Inspected source | Current limitation and required work |
|---|---|
| [`Binder.cs`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.cs), `Declare` and `TryDecodeDirectNativeImportAttribute` | Recognizes qualified attribute text. NativeCallable is a parameterless marker and rejects parentheses/arguments; imports decode exactly two string literals. Implement registered identity/schema binding and checked arguments rather than extending token/name recognizers. |
| [`NativeFunctionPointerContract.cs`](../../sources/Terrum.CodeAnalysis/Semantics/NativeFunctionPointerContract.cs), Binder's signature construction | The host convention enum contains only Native; constructed callable/fnptr signatures use it. It is not the assigned four-case standard domain or a source alias. Preserve all effective identities and exact pointer matching through the supported layers. |
| [`BoundProgram.cs`](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs) | BoundFunction has a pointer-type reference but no effective-convention property; DirectNativeImport has no convention field. Carry one normalized fact per admitted role, including methods without a code address, instead of recovering it from pointer type, slot or receiver. |
| Binder's declaration collection, [`Binder.StaticProtocols.cs`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.StaticProtocols.cs) | Ordinary collection skips interface declarations; the separate protocol binder handles a restricted static-operator form, not general native methods/defaults/overrides or witnesses. Complete the relevant nominal binding before admitting this role. |
| [Implementation inventory](../implementation.md), [native reference](../implementation/native.md), [`NativeFunctionPointerTests`](../../tests/Terrum.CodeAnalysis.Tests/Native/Interop/NativeFunctionPointerTests.cs), [`DirectNativeImportTests`](../../tests/Terrum.CodeAnalysis.Tests/Native/Interop/DirectNativeImportTests.cs) | Existing prototypes exercise marker/two-string and old NativeAbi forms, plus private/experimental persistence. They do not establish this Core schema, all conventions, interface binding or the standard clause path. |

Implementation must preserve the normalized fact through shared bound-tree
traversal/rewriting, mandatory verification, signature interning, witness
selection, source-free references, standard/private persistence, dependency
binding/replacement and native lowering. Do not use prototype restrictions
as source semantics or report emission as execution. General Core
declaration/schema interchange remains independently gated; this proposal
does not assign its missing physical representations.

## Required conformance and qualification

These are future acceptance cases, not tests run or support claimed here.
Use public compiler APIs and unrelated synthetic headless providers. Separate
source/schema admission, standard/private consumption, emission and supervised
native execution evidence, with final compiler/configuration/input identity.

| Kind | Case | Required outcome |
|---|---|---|
| Positive | Bare/empty/explicit-Unmanaged C; each explicit E member, E-typed constant forwarding and exact-identity aliases; callable import with omitted/explicit CallConv | Exact normalization; all four identities retained. Native execution additionally needs the selected target mapping. |
| Positive | Import positional name prefix versus equivalent named arguments; omitted entry/mode and Dynamic policy | Same declared names and normalized contracts; Link/Default behavior exactly as specified. |
| Positive | Interface default Cdecl, direct method override Stdcall, bare method override, unannotated direct method | Respectively Cdecl default, Stdcall, Unmanaged and Cdecl; exactly one clause per affected method. |
| Positive | Base member Cdecl, derived default Stdcall, diamond paths to the same closed base identity | Base remains Cdecl; new derived methods use Stdcall; identical inherited identity deduplicates without redefaulting. |
| Positive | Bodyless requirement, eligible default body, explicitly matching concrete implementation, static export | Preserve each selected declaration/role; abstract member has no address. Export references its one effective clause. |
| Negative | Wrong Core attribute/E/member owner; same spelling/value from another provider; conflicting registration or extra/duplicate enum members | No native-role authority from resemblance; reject the incomplete or incompatible required registration. |
| Negative | Wrong label/case/type, unknown or zero E value, runtime value, raw integer, duplicate CallConv or repeated C/I through aliases | Reject; no ignored argument, merge, value-to-type conversion or last-wins selection. |
| Negative | Positional C argument, third import positional argument, positional/named collision or positional argument after a named one | Reject under the exact schema; NativeCallConv's existing explicit fnptr argument remains separate. |
| Negative | NativeCallConv on a method/interface/func, or NativeCallable used as the fnptr type selector | Reject misplaced schemas; no alias between declaration and function-pointer convention selection. |
| Negative | CallConv on data, ResolutionPolicy on Link, C on a class/accessor/secondary, C+I or X+I, C default reaching an imported method | Reject even when supplied values coincide with omissions or physical mappings. |
| Negative | Bare C on an otherwise illegal bodyless/extern concrete method; interface/foreign method treated as an exported code symbol | No body, foreign role, NativeImport or symbol is synthesized. |
| Negative | Cdecl requirement selected to Unmanaged implementation; unannotated ordinary implementation; one target for differing-convention requirements | Reject the native witness binding; no implicit convention adapter despite equal Windows carriers. |
| Boundary | Valid Vectorcall declaration with unsupported backend, or Vectorcall foreign-output member on a backend that otherwise supports it | Decline the first affected native mapping and reject the bounded foreign role in the second; never substitute Unmanaged. |
| Boundary | Mixed integer/floating/stack inputs and an indirect aggregate result, static versus instance method, matching and mismatched fnptr types | Follow the selected target's exact mapping; preserve receiver/result order and distinct convention identity without implicit casts. |
| Boundary | Unsafe declaration whose body lacks an unsafe operation scope; known invalid backing/code lifetime | Existing safety and lifetime failures remain; CallConv supplies no authorization or lifetime. |
| Metadata | Wrong owner/extent/range, duplicate `000C`, byte zero/unknown, nonzero flags/reserved, callable-import zero/data-import nonzero, wrong/missing export clause reference | Reject malformed or missing required facts, including retained uncalled declarations. |
| Separate compilation | Reference-only interface/import/export contracts without source annotations; reordered/remapped physical rows | Retain expanded effective clauses and original identities/expectations; no source lookup, reader default or row-number identity. |
| Separate compilation | Core provider replacement changes schema/E/member mapping; method provider replacement changes convention; two retained consumers expect different conventions | Check every original expectation; equal names, bytes or carriers cannot merge or repair it. |
| Separate compilation | Standard 1..4 and candidate-96 private 0..2 round trips, graph/reference projection and source-free regeneration | Explicit projection preserves identity in each exact schema; private Vectorcall and missing required carriers reject. |
| Cross-feature | Unrelated flat foreign interfaces at the same slot/receiver carrier with different effective conventions and NativeOutput cells | Bind owner/member first, honor the selected convention and unchanged output protocol; slot/receiver never chooses a convention. |

## Approval boundary and remaining decisions

This candidate leaves **no unresolved source selection or precedence choice**:
it recommends the exact enum/schema identities, arguments, defaults,
placements and checks above. Formal normative adoption is still required.
Actual authorized Core provider identities/keys are profile inputs, and
general Core schema codecs, target support and the dependent output/interface
implementations remain separate admission/qualification gates, not permission
to invent fallback source or wire contracts.
