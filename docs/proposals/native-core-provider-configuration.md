# Native Core provider configuration

**Status: unapproved; provisional implementation direction only.** This does
not adopt a normative contract or establish production support.

## Finding and decision

Missing production construction of
[`NativeCoreRegistrationSet`](../../sources/Terrum.CodeAnalysis/Semantics/NativeCoreRegistration.cs)
is partly **implementation wiring**: the
[registration proposal](native-calling-convention-core-registration.md#proposed-registration-hand-off)
already specifies the exact semantic hand-off, authority and failure rules.
Its logical descriptor expressly does not select a public compiler API or
production provider. The remaining bounded decision is **where the production
host supplies that authority and selection**, rather than a new enum or native
language contract.

Recommend an **explicit immutable native-Core configuration input at the
hosted compilation factory boundary**, resolved by a host-authorized in-process
provider adapter in each using logical Module's dependency context. Do not
require a serialized profile manifest, discover installed providers or choose
a default.

[`CompilationReference`](../../sources/Terrum.CodeAnalysis/Semantics/CompilationReference.cs)
is public but its constructor and registration slot are internal. Current
production reference factories, including
[`ModuleCompiler.CreateReference`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.cs),
do not supply that slot; the registration inputs are test-supplied through
[`NativeCoreRegistrationTests.CreateRegistration`](../../tests/Terrum.CodeAnalysis.Tests/Semantics/NativeCoreRegistrationTests.cs).
[`Compilation.CreateCoreLibrary`](../../sources/Terrum.CodeAnalysis/Semantics/Compilation.cs)
provides a Core compilation mode, not the missing provider authorization.
Making an internal constructor public or copying the test identities would
not settle this boundary.

## Host input and adapter obligations

The host input is the immutable **NativeCoreRegistrationSet** already carried
internally by the compiler, not a wire record or permission for source
packages to construct semantic handles. Expose that validated registration
descriptor to the embedding host and add this property to the existing public
`CompilationOptions` record struct:

```csharp
public NativeCoreRegistrationSet? NativeCoreRegistration { get; init; }
```

`NativeCoreRegistrationSet.Create(...)` validates the complete enum/member and
schema shape before producing the immutable set. Every public
`Compilation.Create`, `CreateLibrary`, `CreateWithNativeImports` and
`CreateCoreLibrary` options overload forwards the selected set to binding;
overloads without explicit configuration select no registration. If an
internal/reference-carried registration and an options registration are both
supplied for one compilation, reject the duplicate even when they are equal.
Reference/set/graph construction must preserve originating contexts; merely
loading an ordinary `CompilationReference` does not designate its producer as
Core.

The public descriptors are configuration inputs, not authentication tokens or
proof that a provider is Core. The embedding host's checked adapter obtains
their identities and complete expectations from its explicitly authorized
provider selection; the compiler validates descriptor structure and source
binding but does not authenticate provider claims. Do not construct these
descriptors from untrusted artifact assertions or package-controlled source.

| Input | Required meaning |
|---|---|
| Using Module | Exact logical Module identity and immutable checked dependency-binding snapshot/generation. One configuration entry per using Module; lexical namespace/usings remain separate. |
| Authority and adapter | The embedding host explicitly designates the Core authority and authorizes the particular producer adapter for the selected semantic contract set. Authentication is an environment obligation, not an artifact assertion or a new trust bit. |
| Provider selections | Exact producer Module identities, unambiguous selected revisions, export keys, required declaration kinds and complete original expectations for the roles used. A coherent selection may span explicitly selected provider Modules. |
| Checked producer declarations | Actual enum, member and complete schema handles in that context, with equivalent exact semantic origins for an authorized in-process producer. Member identity is its defining enum plus actual member declaration; no independent member export key is required or invented. |

The adapter bridges the authorized producer to the existing registration
hand-off; configuration does **not** manufacture semantic identities.

1. Capture the host input and selected closure before affected source binding.
   Resolve selectors through that closure, identity before contract comparison.
   Checked local expectation declarations are proxies for the selected
   definitions, not new nominal declarations. A caller cannot turn arbitrary
   provider/key strings into checked handles by asserting they are Core.
2. Obtain declarations from the checked producer. An explicitly authorized
   in-process adapter may itself produce complete Core declarations before
   general source enum parsing exists, under its supplied real semantic origin.
   It may not pretend absent artifact rows/exports exist. Reading serialized
   declarations requires an independently admitted exact codec.
3. Validate the complete registration needed for each affected role under
   [the registration hand-off](native-calling-convention-core-registration.md#proposed-registration-hand-off)
   and [source schemas](native-calling-convention-source-schema.md).
   Preserve exact enum/member/schema owners, companion resolution-enum domains,
   argument/default contracts, access, placement and multiplicity. A NativeExport
   role handle does not supply an otherwise incomplete export schema.
4. Publish the immutable admitted registration only after its whole required
   closure succeeds. Bind source through ordinary lookup and typed constant
   evaluation, then normalize once. Configuration does not replace lookup,
   grant unsafe authorization or admit an unsupported interface/member/export
   contract, target mapping or value operation.

The supported public host path must reach this admission without reflection,
internal test constructors or a process-global registration singleton.
`terrumc` is another host: absent explicit configuration it cannot silently
select a provider. This decision adds no CLI option or configuration-file
grammar, plugin discovery protocol or provider package catalog.

## Failure boundary

Reject invalid submitted configuration at Module admission, or diagnose an
absent registration when affected source binding needs it. Never publish a
partial successful role or native artifact from failed admission. Unconfigured
unaffected source/inspection remains usable.

| Selection failure | Required behavior |
|---|---|
| Absent authority, adapter, provider, key or required declaration | Refuse affected source binding, including omitted CallConv; omission cannot bypass registration to choose Unmanaged. |
| Multiple entries for one using Module/role, even identical; ambiguous key or selected revisions | Reject, with no first/last/reference-order preference. Ordinary alias/import paths to one selected definition still deduplicate under ordinary lookup. |
| Stale snapshot, changed closure or foreign-context handle | Reject reuse; require explicit checked rebinding in the new context. Equal Module text or a prior acceptance flag is insufficient. |
| Unauthorized adapter/provider or provider self-assertion | Reject designation; package references, Core compilation mode, names, hashes and self-declared role flags do not authorize a provider. |
| Malformed or incomplete descriptor/declaration | Reject wrong kind, owner, access, enum base/member mapping, schema arguments/defaults or unresolved references before role use. |
| Incompatible original expectation or unsupported required schema/evaluation/representation | Refuse affected use under its actual failure category. Do not weaken expectations or try another provider. Constant failure/exhaustion and unsupported target lowering remain distinct. |

No namespace/member-name or integer-value inference, same-shape substitution,
compatibility alias, default-provider fallback or runtime retry is permitted.
Failure never yields zero, Unmanaged, an empty registration or an apparently
successful unsupported operation.

## Context, replacement and source-free consumption

Retain each using Module's original selections and complete expectations,
including every separately retained expectation for the same provider/key.
Cache admission only with the full binding generation, selected declarations,
rules and applicable evaluation/target context. A set or graph cannot flatten
these into one current Core map. Distinct Modules' equally named/equal-valued
enums remain distinct unless exact binding establishes the same definition.

Retain ordinary typed-value and required-evaluation dependencies: exact enum
origin, eligible program/forms, selected operations, target/layout observations
and any stronger provider facts actually relied on. Frozen results stay frozen;
replacement does not replay their construction or substitute the replacement's
own expectations. Check replacement against the original complete graph,
including exact schema/enum/member mapping and all surviving evaluation facts.
Changing configuration explicitly rebinds new work; it does not relabel old work.

Packing/remapping preserves those originating contexts and dependencies,
resolves actual references and rebuilds affected ownership ranges, imports,
type/signature/contract/value graphs, Body maps and optional source references.
Clone context-sensitive shared rows when meanings differ; do not merge nominal
identities or patch matching bytes/names.

Already normalized native contracts retain their effective conventions,
resolution facts, roles and exact owner references under their selected
standard/private schema. Source-free consumers perform structural, support and
binding checks, without reacquiring Core, reconstructing source attributes or
defaults, re-evaluating source, or re-proving producer bodies. Do not synthesize
a runtime Core dependency solely because normalization used the registered
convention enum. Any independently surviving raw-enum, signature/value or
evaluation obligation remains required.

**Source enum/member/schema/value/program codecs remain a separate gate.**
Neither this configuration nor normalized convention bytes reconstructs those
declarations. Raw source-independent consumption needs its own complete admitted
codec and capability closure; missing support declines that route. No new TRRM
record, Feature, DefId, private revision or serialized registration is selected.

## Alternatives

| Boundary | Assessment |
|---|---|
| **Recommended: explicit host-injected configuration and checked adapter** | Completes the existing in-process hand-off and exposes authorization at a usable production boundary. Needs no provider default or serialization; hosts must supply the actual inputs. |
| Serialized profile/provider manifest | Could make host choices portable, but needs exact framing, schema identity, authority establishment, selector/reference kinds, admission, replacement and version rules. It still needs checked producer declarations, including codecs when those declarations are serialized; it cannot authenticate its own Core claim. This is a broader separate decision, not needed for this boundary. |
| Discover a provider or publish the test registration as a built-in default | Rejected: substitutes compiler-authored identities and deployment guesses for an authorized producer selection. |

## Owners and integration

These are adoption destinations, not normative edits made by this proposal.

| Owner | Exact integration boundary |
|---|---|
| This proposal; [Core registration hand-off](native-calling-convention-core-registration.md#proposed-registration-hand-off) | This proposal owns the production host input and adapter admission boundary. It resolves only the delivery-mechanism deferral; the hand-off remains owner of exact registration facts. Concrete authority/provider/key inputs remain required, not selected here. |
| [Native source schemas](native-calling-convention-source-schema.md); [nominal enum forwarding](nominal-enum-constant-forwarding.md) | Preserve their exact domains, source/default/normalization and ordinary evaluator rules. Neither is superseded, and neither gains a codec or implementation claim. |
| [Core inventory §§1, 4](../specs/libraries/adopted-inventory.md#1-status-and-identity); [native declaration roles](../specs/native.md#native-declaration-roles) | On adoption, reference explicit host-authorized per-Module delivery for native source recognition. Preserve designated Core authority, all native roles and existing convention assignments; select no universal Core binary. |
| [Modules](../specs/modules.md); [binding/replacement §§1, 4, 5](../specs/representation/binding-and-replacement.md#1-scope-and-selected-identities) | Apply existing exact identity, using-context, full-graph and original-expectation rules to retained registration/evaluation edges; add this native application, not a second resolver or comparison relation. |
| [Reader trust](../specs/representation/reader-trust.md); [persistence taxonomy](../specs/representation/persistence.md) | Preserve mandatory structural/support/binding checks and producer truth obligations. No enum/schema codec or source reconstruction is authorized. |
| [Static trigger identities](static-initializer-trigger-identities.md#identity-authority-binding-and-replacement) | Compatible per-using-Module authority guidance, unchanged. Native configuration does not automatically register static or other Core roles. |
| [Hosted compiler reference](../implementation/terrum-code-analysis.md) | After implementation, document actual public host entry points and supported inputs. C# API spelling is not a new normative language or interchange contract. |

## Conformance cases

These are required future cases, not tests run. Exercise the supported public
host API with explicit synthetic authorized producers, not internal helpers.
On adoption, place native cases in
[native boundaries](../specs/conformance/native-boundaries.md), context/replacement
cases in [binding conformance](../specs/conformance/il-binding-and-replacement.md),
and codec cases in [persistence conformance](../specs/conformance/interchange-persistence.md).

| Case | Required outcome |
|---|---|
| Explicit authorized complete selection, including separately selected schema providers and ordinary aliases | Admit exact declarations in the using context; normalize NativeImport/NativeCallable/NativeCallConv and companion resolution values under their owner schemas. |
| No configuration on unaffected source, then on a native role with omitted convention | First remains usable; second diagnoses missing registration rather than selecting a default. |
| Duplicate configuration, multiple revisions, ambiguous/missing key or wrong kind | Reject admission without order preference or provider search. |
| Unauthorized provider claims Core; same names/values from another origin; fabricated handles | No role authority or identity conversion. Authorized configuration does not waive producer/declaration validation. |
| Stale handle/generation; A and B select different enum identities | Reject transplantation; admit explicit checked rebinding only when valid. A's raw enum does not become B's. |
| Partial enum/schema, wrong owner/default enum, missing companion domain or incomplete export schema | Reject affected role; do not infer missing contracts from the requested value or role label. |
| Typed constant forwarded through eligible calls; required body/dependency missing, failed or exhausted | Preserve ordinary value/evaluation dependencies in the positive case; refuse required evaluation in the negatives, without native-specific fallback. |
| Set/graph packing reorders rows and shares a structural row across distinct using contexts | Preserve both complete meanings and expectations, cloning/remapping affected uses as needed. |
| Replacement preserves aliases/contracts but reorders definitions; then changes a retained enum/member/schema fact | First remains valid after checked remapping; second fails even with unchanged Module/key or equal machine carriers. Check every original expectation, not the weakest one. |
| Remove source and Core configuration from normalized standard/private consumers; supply malformed convention/owner data | Valid supported normalized consumption needs no source registration; malformed data rejects, not repaired by defaults. |
| Raw enum/schema or enum-bearing program arrives without its admitted codec | Decline independent use; primitive payloads and host configuration cannot reconstruct missing declarations. |
| Interface default, method override, inherited member and export implication; unavailable target ABI | Preserve the source-schema/native owners' exact contracts and separate support gates; successful host admission supplies neither dispatch/export support nor missing lowering. |

## Remaining inputs and acceptance

The host must still supply an actual authorized producer adapter, authority,
producer revisions, exact declaration selections and full expectations. None
was supplied here; no production provider can be installed as a guessed default.
This is a deployment/configuration input, not a remaining choice of discovery
policy. Adapter implementation can proceed with explicitly configured providers.

This decision does not complete `native-callconv-core-registration-binding` or
narrow the compiler-adoption plan. Production integration, full per-Module
context/replacement binding and retained evaluation dependencies, exact
standard/private enum consumers/codecs, interface/member/export support and
broader qualification remain acceptance work under their existing owners.
