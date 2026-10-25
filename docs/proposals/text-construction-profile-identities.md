# Text-construction profile identities

> [!IMPORTANT]
> **Unapproved proposal.** This is a concrete provisional direction under the
> user's compiler-adoption authorization, not specification approval. It changes
> no normative contract, compiler code, row layout, registry assignment or
> support claim.

## Finding and decision

**No existing profile identity carrier reaches source-independent selected-use
validation. Recommend an out-of-band, immutable identity context supplied by the
selected profile for each using Module, alongside ordinary declaration binding.**
Import/Export already carries the declaration edges; the missing contract is
which resolved declarations the profile authorizes for the selected Core roles,
and how that selection reaches the before-use check.

The accepted [inventory §1](../specs/libraries/adopted-inventory.md#1-status-and-identity)
requires profile-supplied exact identities and expressly makes Core an authority,
not a fixed package identity. [Standard text operations](../specs/language/construction-handlers.md#standard-text-operations)
already selects the signatures and behavior. Neither owner supplies a callable
profile-context interface or concrete role-to-declaration mapping.

| Current source evidence | Boundary |
|---|---|
| [Fragment reader inputs](../../sources/Terrum.CodeAnalysis/Modules/TrRmTextFragmentShapeTable.cs#L54-L69), [requirement/locality checks](../../sources/Terrum.CodeAnalysis/Modules/TrRmTextFragmentShapeTable.cs#L322-L387) | Receives tables, not a profile or binding resolver. InterfaceMethod membership, per-shape consistency and evidence relationships cannot establish Core role identity or complete role signatures. |
| [Structural validator entry points](../../sources/Terrum.CodeAnalysis/Modules/TrRmStructuralMetadataValidator.cs#L175-L207), [fragment composition](../../sources/Terrum.CodeAnalysis/Modules/TrRmStructuralMetadataValidator.cs#L429-L443), [CLI caller](../../sources/terrumc/Program.TrRm.cs#L36-L53) | Passes artifact/schema and structural tables. The development-profile Boolean selects a private schema path, not Core declarations. No caller supplies a text-role profile. |
| [Import reader](../../sources/Terrum.CodeAnalysis/Modules/TrRmImportTableReader.cs#L10-L22), [binding accessor](../../sources/Terrum.CodeAnalysis/Modules/TrRmImportTableReader.cs#L142-L151), [Export reader](../../sources/Terrum.CodeAnalysis/Modules/TrRmExportTableReader.cs#L10-L13) | Retains producer/key/requirements and checks local expectations/public exports; explicitly does not resolve providers or compare complete contracts. Their writers reuse these structural checks. |
| [Nominal TypeSpec helper](../../sources/Terrum.CodeAnalysis/Modules/TrRmTypeSpecTableReader.cs#L256-L290) | An Import yields its expected local TypeDef, not a resolved provider or registered role. |
| [TargetProfile](../../sources/Terrum.CodeAnalysis/Modules/TrRmTargetProfileTableReader.cs#L10-L17), [source Core entry point](../../sources/Terrum.CodeAnalysis/Semantics/Compilation.cs#L633-L645) | TargetProfile carries layout identity/revision only. CreateCoreLibrary selects source primitive-declaration compilation, not a source-independent text-role map. Neither is the missing carrier. |
| [Existing fragment fixture signatures](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmTextFragmentShapeTableTests.cs#L275-L282) | Synthetic methods have no parameters or method formals. These tests establish structural behavior, not complete standard-text role validation. |

This proposal supplements the unapproved
[fragment reference forms](text-fragment-reference-forms.md#formatter-evidence-and-requirement-identity),
without replacing its row-only references or resolving its independent
generic/witness/projection representation gates.

## Proposed caller context

The embedding compiler, linker or validator selects the profile independently of
the artifact's claims. It supplies:

* The exact selected profile/rule context and dependency revision selection.
  These are environment inputs, not a new universal profile number or TRRM field.
* A mapping for each using logical Module to the exact public primary
  IFormattable and ITextWriter TypeDefs, their TryFormat, TryWrite,
  TryWriteScalar and TryWriteFormatted requirement MethodDefs, and the complete
  validated erased TextSpan type used by the signatures.
* Ordinary binding access to the selected provider artifacts and complete
  published contracts, including supported transitive requirements.

The role labels above are descriptive, not serialized names, numeric IDs or
recognition rules. Each identity can be selected by an exact
`(Module.identity, exportKey, definition kind)` and resolved to a declaration
handle; an already resolved handle is equivalent only with the same checked
binding context. Distinct export keys for the same definition remain aliases.
Module/key strings use the existing ordinal decoded-text identity rules.
Concrete module identities and keys must come from the selected profile, never
from guessed namespace/member spelling or dependency order.

The context's authority is the profile's designated Core registration authority.
An arbitrary package referencing Core cannot supply or override it. An artifact
may request a dependency but cannot self-authorize a role mapping. Possession of
the context does not exempt any referenced declaration from structural,
signature, binding or capability checks.

For a shape, the using Module is the originating generated TypeDef's Module,
obtained from its checked Module partition. Importing the generated type,
packing its provider beside a consumer, or moving rows does not substitute the
consumer's profile. Distinct Modules may have distinct profile selections;
sharing structural rows never merges those contexts.

Keep byte/table validation separate from context-dependent selected-use
validation. Compose the latter after structural decoding and declaration
resolution, before affected binding, instantiation or lowering is accepted.
Existing structural-only APIs/CLI may retain their stated scope, but their
success is not a claim to have validated profile-selected text operations.
The host configuration format and concrete API type names need not be
standardized to make this obligation explicit.

## Required selected-use checks

Resolve identities before comparing signatures, following
[binding and replacement §§1–4](../specs/representation/binding-and-replacement.md#1-scope-and-selected-identities).
For each retained shape:

1. Obtain its using-Module context. Resolve every selected MethodDef, its
   declaring interface and retained expected contract. The two interface roles
   are distinct nongeneric declarations. A literal's writer must resolve to the
   profile's TryWrite; a formatted fragment's formatter and writer must resolve
   to its TryFormat and TryWriteFormatted respectively. Membership in an
   arbitrary interface, a matching signature or an implementation/default
   MethodDef is not sufficient.
2. Check the actual public requirement declarations against the accepted
   logical signatures below. These are existing contract fragments, not new
   runnable examples or physical signature codecs.

   | Requirement | Receiver | Method formals/constraint | Ordered logical inputs; result |
   |---|---|---|---|
   | IFormattable.TryFormat | Readonly instance | One type formal TWriter, constrained to the selected ITextWriter | `write ref<TWriter>`, validated `TextSpan`; `bool` |
   | ITextWriter.TryWrite | Writable instance (`mut`) | None | Validated `TextSpan`; `bool` |
   | ITextWriter.TryWriteScalar | Writable instance (`mut`) | None | `rune`; `bool` |
   | ITextWriter.TryWriteFormatted | Writable instance (`mut`) | One type formal TValue, constrained to the selected IFormattable | `ref<TValue>`, `int32`, validated `TextSpan`; `bool` |

   All are public synchronous ordinary instance requirements, not static,
   caller-unsafe, async or initialization-output operations. The unmarked method
   type formals retain the accepted
   [Copy-capable, non-null default domains](../specs/language/types-and-normalization.md#generic-formal-domains-and-specialization).
   Map owner substitution first, then method formals by kind and ordinal;
   compare complete normalized types, every qualifier/constructor placement,
   receiver access, parameter count/order/modes/flags, generic domains,
   constraints, defaults and required contracts. TextSpan is the profile's
   resolved erased type, not any text-shaped view or another code-unit
   instantiation; bool/int32/rune retain their reserved scalar identities.
   No reference attenuation, boxing, added caller obligation, argument
   permutation or same-layout substitute repairs a mismatch.
3. Preserve the requirement's complete published safety/effects, failure,
   input-validity, ownership/backing, retention and cleanup contracts through
   witness and provider satisfaction. Do not infer Pure, ConstEval, NoAlias,
   no-throw or allocation freedom. Apply the existing directed satisfaction
   rules to implementations; do not replace requirement identity with a
   compatible implementation. ITextWriter's required-member closure includes
   TryWriteScalar although no fragment field directly selects it.
4. Check effective alignment and format capture types as exact int32 and the
   selected TextSpan. Retain the reference-forms proposal's exact closed
   witness-to-capture/requirement relationship and open formal/constraint
   closure, now against the resolved profile identities. At a reached drain,
   TValue is the capture's complete type and TWriter is the actual writer;
   ordinary supported evidence must establish their constraints. The context
   cannot invent a witness, project qualified self or erase a formal scope.

[Consumer verification](../specs/representation/reader-trust.md) still separates
mandatory structure/support/binding checks from producer truth. No source,
private-body inspection or semantic re-proof of formatter behavior is required.
An identity context supplies neither validated instance data nor a TextSpan
representation, generic dispatch, executable formatter or runtime capability.

## Import, Export, packing and replacement

Use the existing
[Module/Import/Export identities and ownership](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics):

* A source producer names external requirements through its using-Module-owned
  bodyless expected MethodDefs and their Imports. Bind the expected interface
  TypeDef independently; the selected provider method must belong to that exact
  provider interface. Importing the interface does not select a member by
  name, declaration order or vtable slot. A raw expected MethodDef row cannot
  bypass its Import.
* Compare each retained expectation with the one selected provider, including
  all complete signature/constraint/contract edges. Compare that resolved
  declaration with the profile role too. An exact role match does not waive an
  incompatible expectation, nor does compatible structure establish role
  identity. Missing or ambiguous providers/keys and wrong definition kinds
  fail; do not try another provider or overload.
* Exports remain public provider-local definitions, never re-exported imported
  expectations. There is no new Import/Export kind or role-registration row.
* A supported checked pack may rewrite an expected method edge to the provider
  MethodDef in the same artifact. Preserve the originating Module's profile,
  original expectations and every relied-on stronger fact. Remap all nested
  types/formals/witnesses and clone shared contextual rows when their meanings
  differ. Current fragment-reader locality checks reject direct foreign-Module
  method rows; packed direct edges need a separately implemented
  context-aware path, not a claim that packing already works.
* Replacement rechecks the complete retained graph against the actual selected
  revision and profile. Same module/key, row number or prior validation does
  not prove compatibility. Profile changes cannot relabel an old shape's
  requirements: retain its selection or explicitly rebind/revalidate every
  affected use. Cache reuse requires unchanged artifact, schema, profile,
  using-Module/formal context, bindings and relied-on contracts.

## Missing context and acceptance cases

Before selected text use, an absent, partial, conflicting or unsupported
profile mapping must diagnose or decline that use. A missing profile is not
necessarily malformed artifact bytes. Structural inspection may still
succeed, and an unrelated selected closure needs no text profile. An empty
literal still retains its writer identity edge. No null-context success,
name-based fallback, inferred Core module or optional-audit-only check is
permitted for affected selected use.

These are proposed regression requirements, not tests run by this document.
Use public compiler/metadata APIs with small independently built artifacts.

| Case | Required outcome |
|---|---|
| Exact profile with unrelated synthetic declaration spellings | Accept correct local or separately imported role identities and complete signatures; spelling does not grant or remove authority. |
| Missing/partial context, including no-hole or empty-literal shapes | Decline affected selected use; structural-only inspection retains its narrower result. |
| Same names/signatures in another module, or another requirement in the correct interface | Reject role mismatch, including substituting TryWrite for TryWriteFormatted. |
| Signature mutations | Reject wrong receiver access, result, parameter type/order/mode, reference qualification, TextSpan identity/argument, generic arity/kind/domain/constraint or added caller-unsafe obligation. |
| Exact role with incompatible Import, wrong bound owner, nonpublic export or missing provider | Reject before affected use; no signature- or name-based alternative lookup. |
| Closed/inherited witness and open formal evidence | Accept supported exact evidence; reject wrong implementing capture, declaring requirement or formal/constraint context. Unsupported projections remain gated. |
| Two using Modules with different profiles | Validate independently, including an imported generated type retaining its origin; identical physical row numbers do not share a validation result. |
| Export aliases and checked packing | Aliases resolving to one declaration preserve identity. Independent row/Module permutations and direct-bound rewrites preserve expectations and originating profile; imported expectations cannot be re-exported. |
| Replacement/cache changes | Recheck changed profile, provider revision, constraints or relied-on facts; reject incompatible retained contracts despite unchanged module/key. |
| Boundary of support/trust | Missing required codecs or capability closure declines use. No semantic body-truth audit or executable-support claim follows from an identity/signature pass. |

## Current prototype evidence

The unapproved context recommendation is provisionally represented by
`TrRmTextConstructionProfile` and the public
`TrRmStructuralMetadataValidator.ValidateTextConstructionProfile` overload.
It takes exact selected rows per using Module and checks the fragment's role
references, direct InterfaceMethod ownership, public/non-generic interface
owners, result/receiver/ordered-parameter signatures, passing modes, generic
formal domains and constraints, TextSpan identity, and the absence of
DoesNotReturn/caller-unsafe requirements. The structural-only `Validate`
entry point remains profile-agnostic.

This API consumes already resolved identity rows; it does not select a profile
from module/key configuration or resolve actual Import providers. No current
CLI or source-free execution caller supplies it. Complete provider contract
comparison, Import/Export replacement, packing/remapping and source/runtime
text construction remain unimplemented. The focused selection passed **7/7**,
and the active TRRM/metadata/CLI selection passed **1,745/1,745** with zero
skipped. The normal Release analyzer build still reports **1,514
repository-wide diagnostics**, with none on changed C# lines. The proposal
remains unapproved; no normative specification or registry assignment changed.

## Alternatives and adoption boundary

A global fixed Core module ID would make current artifacts appear self-contained
but contradicts the accepted registration-authority boundary. Name/signature
recognition cannot establish nominal roles. Adding role tags or profile fields
to TextFragmentShape would duplicate a profile selection across rows and require
new interchange/authority rules. A complete serialized profile manifest could
be designed separately, but is unnecessary for this bounded decision.

The proposed context reuses existing exact declaration binding without changing
source syntax, standard text behavior, the 40-byte fragment row or any registry.
If adopted separately, the inventory's profile-supply obligation and the
binding/verification before-use rules should point to this context contract;
the reference-forms proposal keeps ownership of its edge encoding.
Concrete profile provisioning, complete provider comparison and dependent
generic/witness/value/runtime support remain implementation prerequisites.
No normative integration or compiler implementation is authorized by this
document alone.
