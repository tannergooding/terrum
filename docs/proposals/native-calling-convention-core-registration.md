# Exact Core registration for native calling conventions

**Status: unapproved; provisional implementation direction under the authorized
missing-contract workflow, not normative adoption.** This proposal complements
[the source-schema candidate](native-calling-convention-source-schema.md);
it does not supersede its exact enum identity, ordinary typed-constant
forwarding, argument schemas, defaults or declaration-role rules. All
registration descriptors, public identities and public enum assignments
recommended here remain **unapproved**. Existing standard wire assignments
are identified separately below.

## Decision and scope

The accepted specification supplies the nominal-identity, lookup and constant
semantics needed to implement exact Core recognition. Missing compiler support
for source enums is an **implementation prerequisite**, not an undecided enum
language contract. However, the accepted owners do not select the proposed
public `Terrum.Interop.NativeCallingConvention` declaration, its public integer
assignments, a concrete Core provider/export key, or a mechanism delivering
resolved Core enum/schema declarations to this front end.

Recommend a bounded **native Core registration adapter over shared resolved
type and constant machinery**. An explicitly selected compilation profile
supplies checked declaration handles in each logical Module's binding context.
The adapter exposes those declarations to ordinary lookup and constant
evaluation, then maps a checked value of the exact registered enum to the
existing normalized convention domain. It must not recognize source strings
as substitutes for declarations.

The additional provisional decision is the registration hand-off and its
failure boundary below, not new enum syntax, different constant semantics,
or a new serialized registration format. No production provider is selected
here. An implementation can implement the hand-off and exercise explicitly
configured synthetic Core providers; it cannot ship a guessed provider as the
default. General source-enum declarations and source-independent enum/schema
publication remain separate support gates.

## Accepted authority and current evidence

| Evidence | What it establishes |
|---|---|
| [Native declaration roles](../specs/native.md#native-declaration-roles) | Resolved Core roles, the four closed conventions, callable-import omission as Unmanaged, separate NativeCallable/NativeExport roles and explicit NativeCallConv before fnptr. No standalone NativeAbi role. |
| [Grammar §3.1](../specs/grammar-spec.md#31-source-file-structure-and-declarations) and [§5.9](../specs/grammar-spec.md#59-static-proof-floor-and-constant-evaluation) | Ordinary nominal enum declarations/member constants; schema-resolved attribute arguments use the ordinary checked, encodable constant evaluator. General enums may have duplicate-valued members and unnamed underlying values. |
| [Names and binding](../specs/language/names-and-binding.md), [complete types](../specs/language/types-and-normalization.md), [constant domains](../specs/language/constant-generics.md#canonical-value-identity) | Declaration/module ownership, ordinary lookup and aliases, exact nominal domains and value identity independent of member spelling. The constant-generic chapter does not itself enlarge attribute or ordinary-constant eligibility. |
| [Core identity](../specs/libraries/adopted-inventory.md#1-status-and-identity), [binding/replacement](../specs/representation/binding-and-replacement.md#1-scope-and-selected-identities) | Core is a designated authority, not a namespace or fixed package identity. Selected provider/key/kind and complete expectations precede contract comparison; names, carriers and row numbers are not identity. |
| [`Binder.cs`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.cs), `BindingContext`, `BindType`, `TryDecodeDirectNativeImportAttribute`, `Declare` | The current BindingContext captures tree/namespace/usings, not Core registration authority. fnptr's NativeAbi argument and NativeImport's two string literals are textual paths; NativeCallable is a marker. These are not resolved schema/enum handles. |
| [`SyntaxKind.cs`](../../sources/Terrum.CodeAnalysis/Syntax/SyntaxKind.cs), [`Binder.ManagedClasses.cs`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.ManagedClasses.cs), `ResolveTypeName` | The source tree has no enum-declaration kind; ordinary source type resolution handles structs/classes/aliases, not a general enum declaration/member model. |
| [`Binder.ConstEval.cs`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.ConstEval.cs), [`BoundConstant`](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs), [`NativeFunctionPointerContract.cs`](../../sources/Terrum.CodeAnalysis/Semantics/NativeFunctionPointerContract.cs) | Required evaluation is scalar-bounded, constants use PrimitiveType plus scalar bits, and the host native convention enum has only Native. None supplies the proposed nominal E or its four source values. |
| [`TrRmConstantSourceValueValidator.cs`](../../sources/Terrum.CodeAnalysis/Modules/TrRmConstantSourceValueValidator.cs), [typed-value records](../specs/representation/logical-records.md#published-predicates-and-evaluation-forms) | A frozen-value carrier and primitive materialization do not supply a source enum declaration, its member definitions or a Core attribute-schema codec. |

Thus implementing existing nominal/constant rules is not itself a new design
decision. The source-schema proposal supplies the unapproved public surface;
this proposal supplies a concrete, bounded bootstrap delivery contract. Neither
fills the other proposal's explicit provider/profile inputs by implication.

## Alternatives and selected prerequisite

| Approach | Minimum work and boundary |
|---|---|
| **Selected: exact native registration/constant bridge** | Supply authoritative enum/member/schema handles for the native roles; add the shared nominal-domain, lookup and typed-constant support needed to consume them. Broader EnumDecl parsing and arbitrary schema registration need not be prerequisites. This is smaller only if these are ordinary symbols/values, not native-only token cases. |
| Minimum general enum/type-resolution prerequisite | Implement EnumDecl collection, exact underlying-type/member checks, nominal enum type/member lookup, aliases/import binding, E-typed constant formation/forwarding and resolved attribute-schema handles before native integration. This reuses the accepted enum rules and benefits unrelated enums, but still needs explicit Core provider selection and does not by itself supply missing declaration/schema codecs. |
| Direct-member-only recognition or a reserved-name pseudo-enum | Rejected. Accepting only E.Cdecl tokens, treating the qualified name as identity, or converting int32 bits into E weakens the source-schema candidate and bypasses ordinary lookup/evaluation. |

The bridge still requires a reusable nominal enum constant representation,
declaration-aware lookup and the ordinary evaluator's typed copy/forwarding
paths. It is not a one-branch change to BindType or the raw NativeImport
decoder. If the current PrimitiveType representation cannot preserve these
facts, extend the shared representation first; do not use an int32 surrogate.
The general prerequisite is the appropriate expansion when arbitrary authored
enums are needed, not a second language interpretation or fallback mode.
The [nominal enum forwarding candidate](nominal-enum-constant-forwarding.md)
makes the shared value-model prerequisite concrete, with bounded int32 enum
admission and ordinary ConstEval/runtime forwarding, not a native-specific
evaluator or a source-free persistence codec.

## Proposed registration hand-off

The following is an **unapproved logical in-process descriptor**, not source
syntax, a public compiler API, a TRRM row, a DefId kind or a Feature assignment.
Publish one immutable admitted registration set for an owning logical Module
M and its selected dependency closure.

| Descriptor field | Required meaning |
|---|---|
| Using context | M's semantic identity, its checked source/import scopes and complete selected dependency bindings. Lexical tree/namespace/usings remain separate inputs to each lookup. |
| Authority | The explicitly configured profile's designated Core registration authority and selected semantic contract set, including the source-schema candidate. A package reference or a coreLibrary Boolean alone is not this registration. Authentication remains an environment obligation. |
| Declaration selections | One exact checked semantic declaration handle for each required role. An external selector, where its declaration codec is admitted, contains the exact producer Module identity, export key, required declaration kind and complete retained expectation. No unresolved selector is a usable handle. |
| Enum descriptor E | The actual selected nominal enum definition and its complete public nongeneric primary declaration, exact int32 base, member definitions and constant values. A consumer-local expected declaration binds to that definition; it is not a second nominal enum. |
| Member mapping | Four distinct checked member handles owned by E, with the candidate names/values below. No process-global member-name dictionary, integer-only identity or ordinal-as-definition shortcut. |
| Schema descriptors | Exact selected NativeImport I, NativeCallable C, NativeCallConv P and, where used, NativeExport X schema handles, with their full argument, placement, repetition and default contracts. I.CallConv, C.CallConv and P's explicit argument refer to the same E handle. |
| Other import argument domains | I's resolution-mode/policy enum handles and complete member mappings from the source-schema candidate. Supplying E does not authorize leaving the rest of I as a raw-syntax decoder. |

Core may deliver checked semantic declarations through an explicitly authorized
in-process producer adapter before general source enum parsing exists. That
adapter is a producer of the selected Core declarations: it supplies their
real semantic origins and complete contracts. It cannot manufacture a new
nominal identity from a source spelling, pretend an absent metadata row exists,
or authorize an arbitrary external provider by accepting its claim to be Core.
A serialized provider requires its independently supported declaration codec;
the adapter is not a way around that boundary.

The unapproved public enum/type/member candidates repeat the source-schema
decision only to make the hand-off exact:

| Candidate member of public primary enum `Terrum.Interop.NativeCallingConvention` | Required member value in exact int32 domain | Existing standard convention byte |
|---|---:|---:|
| Unmanaged | 1 | 1 |
| Cdecl | 2 | 2 |
| Stdcall | 3 | 3 |
| Vectorcall | 4 | 4 |

The first two columns are **unapproved source assignments**. The last column
is already assigned by [metadata](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics).
Validate the enum's complete member set, exact base, ownership, access and
values before admitting the registration. E has exactly these four members,
no Flags role and no duplicate-valued aliases. This is a restriction on this
registered declaration's required shape, not a replacement of general enum
semantics or a rule making every E value a named member.

Member identity is the defining enum plus its actual member declaration
identity. Ordinary qualified lookup selects that declaration in E's member
space; equal terminal text under another owner does not select it. No separate
member export key or physical member token is invented here.
Schema-argument identity is its owning schema plus its declared argument
identity, such as I's case-sensitive CallConv; it is not a runtime FieldDef
or writable property. Public schema names, companion-enum assignments and
all argument/default rules remain those of the unapproved source-schema
candidate; this proposal selects no additional overload or export arguments.
X requires its independently complete source schema; a role handle alone
cannot choose missing export arguments or establish source export support.

### Context, resolution and failure

1. Establish M's explicit Core selections and validate the whole registration
   needed for the affected role before source normalization. Each role has
   one registration entry; duplicate entries, even identical ones, diagnose.
   Repeated ordinary import/alias paths to one semantic declaration instead
   deduplicate under ordinary lookup.
2. Resolve a source attribute/type/member through its actual lexical and
   selected Module binding context. Only an exact resolved registered schema
   acquires the native role. An alias to that declaration preserves identity;
   a same-named local declaration neither acquires the role nor triggers
   fallback to Core. Qualification does not choose between unresolved
   same-key providers.
3. Check all enum/schema owner relationships and complete contracts. Schema
   providers need not be the same physical Module, but every selection must
   be explicit and every convention argument must resolve to this one E.
   Mixed or independently inferred selections are not coherent registration.
4. Bind and evaluate supplied arguments, then apply the source-schema
   candidate's role/default/precedence rules. Even omission requires the
   admitted schema and its registered E.Unmanaged default; it cannot bypass
   a missing registration. P still has one explicit argument and no default.
5. Missing selection, ambiguity, conflicting revisions, wrong declaration
   kind, stale/foreign-context handles, unauthorized authority, incompatible
   expectations, wrong enum shape or mismatched schema E references reject
   affected source binding. Do not retry another provider, infer a key from
   a public name, or fall back to NativeAbi/Native.

A registration belongs to M's selected closure, not to the process or a
namespace. A cache must distinguish the owning binding context/generation,
selected declaration identities and retained dependencies. Passing a handle
between contexts requires an explicit checked binding to the same definition.
If A selects E_A and B selects a different E_B, an E_A constant does not become
E_B because their names/values agree. A replacement must satisfy each original
retained enum/schema expectation; reloading the current profile cannot rewrite
an earlier expectation.

Malformed source/registration, unsupported required representation/evaluation,
constant-execution failure/exhaustion and unsupported target lowering are
distinct failures. None produces a default-valued successful binding.
Unaffected compilation or inspection need not acquire otherwise unused Core
registrations; normalized source-free consumption has its own boundary below.

## Typed constants and exact normalization

The bridge supplies E as a nominal domain to **ordinary** type/member lookup
and constant evaluation. Its in-process constant value is the exact normalized
type E plus a checked mathematical int32 value, accompanied by the ordinary
retained evaluation/binding dependencies. Member handles are needed when
binding member expressions, not as provenance tags on every resulting value.

For each supplied convention argument:

1. Bind the expression in its ordinary source scope, with the selected
   schema's exact E argument contract. Normalize transparent aliases.
2. Use the [ordinary checked constant evaluator](../specs/language/proof-and-constant-evaluation.md),
   including eligibility, initialization, Copy/frozen validity, supported typed
   encoding, dependency and resource requirements. An attribute-specific
   evaluator or syntax-only constant table is not an alternative.
3. Require a completed constant of exact E type. Previously checked E
   constants, chains of accessible constant bindings and eligible ConstEval
   results forward normally. Retain E through copies, conditional results,
   parameters/results and any independently admitted closed substitution.
   Forwarding a checked value does not rerun its original construction.
4. Compare the resulting value to the admitted member-value mapping, not its
   spelling or construction history. The value 2 of E selects Cdecl even if
   its expression is not a member access. No member-origin requirement is
   introduced. An otherwise legal explicit value formation is not forbidden
   merely because it did not name Cdecl.
5. Map exactly once to the normalized standard convention. Unknown E values,
   including zero, reject at this schema use; they do not make an otherwise
   valid ordinary unnamed enum value globally invalid.

Integers, strings, unrelated enums and nominal wrappers receive no implicit
conversion to E, even with bits 1..4. This adds no literal parser, cast or
construction operation. Runtime values, unavailable constant programs,
invalid formations, cycles and exhausted required evaluation reject without
runtime fallback. A still-symbolic constant formal cannot supply a concrete
convention byte; this proposal adds neither a deferred wire convention nor a
new specialization rule.

These are full semantic requirements, not permission to advertise the bridge
as complete after only direct member expressions work. Broader unsupported
constant operations retain their ordinary support boundary rather than a new
native-only rejection rule. No E runtime ABI or generic-value interchange
codec is established by its compiler-local constant representation.

## Source-free persistence and remapping

Keep two boundaries separate:

| Consumption boundary | Required retained facts |
|---|---|
| Already normalized native contracts | Existing NativeImport callable bytes 1..4/data zero; MethodDef-only NativeCallable clause 0x000C with its effective byte; NativeExport's exact owning clause reference; TypeSpec form 0x0D's explicit byte and signature anchor. Consumers need no source annotation, E/member lookup or new Core selection merely to interpret these facts. |
| Source/schema or raw E-typed constant consumption from a dependency | Exact enum/type/member/schema definitions, selected origins, full argument contracts, nominal constant domain/value and applicable dependencies through an independently admitted representation. The in-process descriptor is not that representation. |

The accepted TypeDef flags currently select struct/class/interface, not a
complete enum declaration/member codec
([flags](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics)).
The assigned FrozenValueNode kind 1 includes scalar/enum values, but does not
by itself encode E's declaration/member set or register an attribute schema.
Existing generic Import/Export permitted definition kinds likewise do not
assign an enum-member or semantic-schema kind. Do not fabricate these as
FieldDefs, change int32 frozen values into E, or hide required authority in
optional Attr/debug data. Source-independent E/schema publication remains
gated; needing it requires a separate complete codec decision, not a guessed
extension in this proposal.

Normalized consumers validate the existing shapes, references, owner/role
relationships, schema support and selected complete bindings. They do not
reconstruct source defaults or re-prove producer bodies. Only required
source/schema dependencies retain Core selections: do not add a synthetic
runtime dependency on E merely because Cdecl was selected from source.
Independently normalized Cdecl contracts can agree across different explicit
Core contexts when all retained contracts agree; that does not make their
raw E types interchangeable. Conversely, Unmanaged and Cdecl remain distinct
conventions even where a target uses identical machine carriers.

Linking/packing resolves identities before remapping physical references.
Preserve using-Module context, original expectations, selected provider/key,
nominal/member/schema owners and any retained typed-value dependencies.
Rebuild Module/ModuleRef ownership ranges, Import/Export references,
TypeSpec/MethodSig graphs, signature anchors, MethodDef Contract ranges,
NativeExport clause references and affected Body maps under their actual
schemas. Remap retained SourceFile/SourceMap references and declared optional
reference slots too; source/debug data cannot supply missing Core authority.
Rebuild constant sources/value graphs too if an independently
supported enum-value codec is in use. Shared structural rows may be reused
only when their resolved meanings agree in every using context; clone/remap
only affected uses when one Module resolves an imported edge differently.
Row equality, name equality and byte substitution are not remapping algorithms.

Private persistence has its [separate unapproved convention candidate](native-calling-convention-private-persistence.md).
It must explicitly translate its selected convention encoding and preserve
the same normalized facts; casting the present single-case host enum is not
a projection. This proposal assigns no private revision or historical reader.
Missing required facts reject affected consumption. Standard/private
distinctions and exact draft agreement remain owned by
[persistence](../specs/representation/persistence.md) and
[format versioning](../specs/representation/format-versioning.md).

## Integration obligations and unresolved inputs

If adopted, integrate the registration hand-off with the source-schema
proposal's future amendments to native roles, grammar/schema semantics and
the Core inventory. General names, enum and constant rules are preserved,
not superseded. Binding/replacement retains the per-Module expectations
above. Existing metadata/registry numbers are unchanged; no enum/schema
codec, clause, table, Feature, opcode or version pair is allocated here.

Implementation must carry resolved types/values through shared binding,
traversal/rewriting, verification and dependency/reference handling until
normalization; thereafter all relevant verification, interning, persistence
and lowering paths carry the normalized role/convention. No target, sample,
DLL, receiver or vtable slot supplies a registration. Caller-unsafety,
backing, ownership, output and code-lifetime obligations remain unchanged.

| Still-required input or gate | Explicit boundary |
|---|---|
| Concrete authorized profile/Core authority and using-Module dependency selection | Not supplied by accepted short role names or selected here. Without it, affected source roles cannot bind. |
| Exact producer Module identities, selected revisions, export keys/kinds and complete expectations for E and each schema/companion enum | Mandatory profile inputs, not inferred from Terrum.Interop or copied from an arbitrary installed provider. In-process providers must supply equivalent exact semantic origins and handles. |
| General source EnumDecl/member support | Not required to deliver the selected bootstrap declarations, but required before claiming ordinary authored enum support. Shared nominal E-type/constant support is mandatory for this bridge. |
| Independent enum/member/schema/constant publication | Requires its own admitted codecs and capability closure. No source-free E constant import claim follows from normalized native round trips or primitive frozen-value tests. |
| Selected target/value/evaluation support | Ordinary constant formation must be supported. Known convention identity does not supply an unavailable native mapping; Vectorcall may bind yet affected lowering must decline. |

These gates are not permission to defer an identity choice to a name recognizer.
They are explicit unavailable inputs/support, while this candidate's
registration, constant-forwarding and failure policies are concrete.
Provisional use does not approve this proposal or the source-schema candidate.

## Required test matrix

These are implementation acceptance cases, **not checks run or support claimed
by this documentation change**. Use public compiler APIs, explicit synthetic
Core/provider bindings and unrelated headless APIs. Do not rely on the current
NativeAbi/two-string prototypes as evidence for this contract.

| Kind | Case | Required outcome |
|---|---|---|
| Positive | Each of the four exact registered member values through I, C and P; omitted callable-import/C convention and method-export implication | Same source-schema normalization, exact standard bytes, one effective role; omission resolves registered Unmanaged, not zero. |
| Positive | Transparent aliases to E and schemas; repeated import paths to one declaration | Preserve identity and ordinary lookup deduplication; no duplicate role is invented. |
| Positive | Accessible fixed E constant, multiple forwarding bindings, eligible E-returning ConstEval identity/conditional expression, otherwise legal E-valued formation yielding 2 without member access | Same value as a direct member; E survives all copies/evaluation. No direct-member spelling or provenance requirement. |
| Positive/boundary | Already checked constant reused; independently admitted closed constant substitution | Preserve exact domain/value/dependencies without rerunning construction. An unresolved formal does not yield a byte. |
| Negative | Same qualified schema/E/member names and values under another provider; unrelated int32/wrapper/enum | No role or implicit E conversion. Qualified text alone never establishes the registered origin. |
| Negative | Missing profile/key, multiple selected revisions, conflicting/stale/context-foreign handles, unauthorized registration, duplicate registration entry | Reject before affected normalization; no first-provider, current-profile, same-shape or default fallback. |
| Negative | Wrong kind/access/base/arity, missing or extra E member, duplicate-valued alias, Flags role, swapped member values or wrong member owner | Reject the incomplete/incompatible registration, even when the argument would use an otherwise familiar value. |
| Negative | Schema argument/default points to E' instead of E; invalid companion resolution enums or incomplete I schema | Reject; do not leave imports on the old raw decoder while admitting C/P. |
| Negative | Ordinary lookup selects a shadowing non-Core declaration, ambiguous distinct definitions, inaccessible constant or invalid alias | Preserve the ordinary lookup/access failure; do not retry the registered spelling behind it. |
| Negative | Runtime E value, non-ConstEval call, constant cycle, failed formation/check, unavailable dependency or resource exhaustion | Required-constant failure; no runtime evaluation, silent zero or alternate provider. |
| Boundary | Ordinary valid E values 0, -1, 5, int32 minimum/maximum; formation outside int32 range | First group remains ordinary enum values but rejects as convention arguments; out-of-range formation fails before mapping. |
| Cross-feature | Wrong placement, P omitted argument/on func, NativeAbi source, duplicated CallConv/schema, CallConv on data | Preserve the source-schema and accepted role/grammar failures; registration adds no aliases or ignored arguments. |
| Context | A selects E_A, B selects E_B with equal names/values; pass A's raw constant to B, then import A's already normalized Cdecl method | Raw value fails exact-domain binding; normalized contract consumption requires its retained ABI contracts, not consumer re-registration of A's E. |
| Source-free positive | Remove source/attributes/Core registration from normalized native-contract consumption; retain uncalled/bodyless callable declarations | Decode/verify the exact effective conventions and role/owner references; do not reconstruct defaults or require source E. |
| Source-free negative | Raw E constant/schema supplied without an admitted declaration/value codec, despite primitive bits or FrozenValueNode kind 1 | Decline affected use; no fabricated TypeDef/FieldDef/member identity or int32-to-E retagging. |
| Metadata boundary | Unknown/zero callable convention, data nonzero, duplicate/wrong-owner 0x000C, missing/wrong export clause, invalid fnptr anchor | Reject the relevant malformed contract; no source default repairs it. |
| Remapping | Reorder Modules/rows/maps/contracts; resolve one imported edge while another using Module retains its own expectation | Preserve every original meaning and owner; clone context-sensitive shared rows and rebuild references rather than patching byte values. |
| Replacement | Same provider identity but changed E base/member/value/schema mapping; native provider changes effective Cdecl to Unmanaged | Reject affected retained expectations, even with equal carriers; compare original expectations, not replacement-authored ones. |
| Target/safety boundary | All source conventions bind; selected backend lacks Vectorcall, or a call lacks unsafe authorization/backing/code lifetime | Decline unsupported lowering and preserve independent safety/lifetime failures; no convention downgrade or synthesized authorization. |

Keep source/schema admission, normalized standard/private consumption,
emission and supervised native execution evidence distinct. Qualification
must identify the final compiler/profile/provider inputs; no native execution
or broader enum/metadata conformance is established by writing this proposal.
