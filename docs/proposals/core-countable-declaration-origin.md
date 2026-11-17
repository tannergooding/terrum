# Core Count declaration origins

> [!IMPORTANT]
> **Status: UNAPPROVED; provisional direction only; non-normative.**
> This completes only the declaration-origin input to
> [Core Count witness selection](core-countable-witness-selection.md#candidate-core-selection-handoff).
> It does not approve that proposal, amend accepted specifications or claim
> compiler support.

The authorizing user instruction is:

> “At a genuine missing contract, prepare the smallest concrete decision needed as a new proposal doc using gpt-6.1-sol at max reasoning, then continue with it being treated as a provisional approval/direction. I will go and appropriately review/approve those proposals at a future point; this allows you to continue work without being blocked.”

## Decision and boundary

Recommend **exact source occurrences for source-local selection, and explicit
Module/export selectors against checked references for external selection**.
Both resolve into the existing nonserialized, per-using-Module
`CountableCoreSelection`; they are not another witness or Core registry.

[Registered queries](../specs/language/registered-queries.md#identity-and-authority)
and [LCI-05](../specs/libraries/adopted-inventory.md#2-selected-declarations-and-compiler-roles)
remain the accepted owners. Preserve all Count behavior, authority, property/
getter validation, ordinary nominal witnesses and independent codec gates in
the existing proposal. This decision replaces no accepted rule and supersedes
none of that proposal's selections. It supplies the previously unspecified
way to identify the two declarations before role validation.

## Why a new decision is needed

These are source-inspection observations, not executed tests.

| Evidence | Remaining boundary |
|---|---|
| [`Compilation.cs`](../../sources/Terrum.CodeAnalysis/Semantics/Compilation.cs), [`CompilationReference.cs`](../../sources/Terrum.CodeAnalysis/Semantics/CompilationReference.cs) | Options and internal reference projections transport other explicit Core selections. There is no public source-facing interface declaration handle or `Compilation.CreateReference` factory. Existing reference construction includes artifact-backed host paths such as [`ModuleCompiler.CreateReference`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.cs); this proposal does not invent a source-reference factory. |
| [`SyntaxTree.cs`](../../sources/Terrum.CodeAnalysis/Syntax/SyntaxTree.cs), [`SyntaxNode.cs`](../../sources/Terrum.CodeAnalysis/Syntax/SyntaxNode.cs) | Actual immutable trees and declaration-node objects are available before semantic binding. `ContentId` expressly is not semantic identity. These objects can identify occurrences without a public bound-symbol API. |
| [`BoundProgram.cs`](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs), [`Binder.Interfaces.cs`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.Interfaces.cs) | Interfaces have `QualifiedName` and optional `ModuleImport`; members have an owner-scoped `DeclarationKey`. Source member keys are constructed by `InterfaceMemberKey`, not supplied as producer export keys. None substitutes for a checked source occurrence or actual external export map. |
| [Contract-failure selection](core-contract-failure-identity-selection.md#selection-carrier-and-authority) | Already proposes exact external selectors and permits an explicit checked local export map. It neither supplies source interface/property handles nor requires an otherwise source-only declaration to acquire an export. |
| [Source-layout registration](source-layout-core-registration.md#candidate-host-api-and-complete-schema-projection), [`NativeCoreRegistration.cs`](../../sources/Terrum.CodeAnalysis/Semantics/NativeCoreRegistration.cs) | The existing `CoreDeclarationKind`/snapshot catalog covers enums, enum members and attribute schemas, not interfaces/properties. `CoreProviderModuleBindings` also rejects a provider equal to the using Module. It cannot directly carry this local pair. |

## Complete candidate input

The following are **unapproved in-process fields**, not source syntax, wire
records or an assertion that the APIs exist.

`CountableCoreSelectionInput` has exactly two fields:

| Field | Meaning |
|---|---|
| `UsingModuleIdentity` | The host-selected logical Module whose semantics use this role. For source binding, associate it with this invocation's complete source/reference/options snapshot; later Module publication must agree. Neither a filename nor a namespace supplies it. |
| `Declarations` | Exactly one complete alternative below; interface and property cannot be selected through different alternatives. |

| Alternative | Exact fields |
|---|---|
| `SourceLocal` | `Interface` and `CountProperty`, each an occurrence pair `(SyntaxTree Tree, SyntaxNode Declaration)`. Both belong to this using Module's actual source inputs. |
| `External` | `ProviderModuleIdentity : string`, `InterfaceExportKey : string`, `CountPropertyExportKey : string`. Resolve both keys only through the exact `CompilationReference` already supplied to the using Module's ordinary binding; do not attach a second provider reference or dependency to the role selection. |

For `SourceLocal`, verify tree and node membership by object identity, node
kind (`InterfaceDeclaration` or `PropertyDeclaration`), actual primary
authorship and ordinary declared ownership. Resolve through the binder's
association from those occurrences to their actual declarations, including
ordinary partial-declaration assembly; do not scan bound definitions by
`QualifiedName`, member name or `DeclarationKey`. The property must belong to
the selected interface. Normal lookup inside the declarations' own types and
contracts is unchanged.

The resulting local identity is the actual declaration in its owning logical
Module/binding snapshot. A source occurrence is only a way to select it.
Paths, spans, tree ordinals, content hashes and role strings are not identity
keys. Reparse requires new occurrences; rebinding the same trees still creates
a new semantic context and revalidates the declarations. A node from another
compilation's source Module is not local to this invocation.

For `External`, resolve both keys in the selected provider's real checked
export map before comparing contracts. Under Standard metadata, the interface
key selects a public primary nongeneric **TypeDef of interface kind**, with
its owned InterfaceDef; InterfaceDef is not independently exported. The
property key selects a public **PropertyDef owned by that exact TypeDef**.
Derive the getter from PropertyDef; select no getter export key. These rules
follow [metadata](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics)
and [property binding](../specs/representation/binding-and-replacement.md#2-directed-satisfaction-and-exact-dimensions).

An in-process/private projection must retain the equivalent actual
declaration/export/owner correspondence under its explicitly selected
supported contract; it cannot pretend that Standard rows exist.
`ModuleImport` anchors and consumer expectation proxies alone are insufficient.
Keys and Module identities use ordinary nonempty, well-formed ordinal
decoded text without NUL, not normalized names. Do not derive any key from
source spelling, a member key, a path or a role.

## Authority, delivery and rejection

The designated Core authority/profile supplies the input. A referenced
package, exported declaration or Core compilation mode cannot authorize it.
No provider, key, namespace or default selection is chosen here.

Add optional `CompilationOptions.CountableCoreSelection` of the candidate
input type for the current source Module; options-bearing factories forward
it. Capture and resolve it during declaration binding, before affected live
Count binding. This avoids needing a semantic handle from an already completed
`Compilation`. Publish the existing resolved `CountableCoreSelection` only
after its complete declaration/role checks succeed.

Existing internal host/reference paths may carry
`CompilationReference.CountableCoreSelections`, pairing resolved selections
with their **originating** using-Module contexts. This does not add a public
reference constructor. Ordinary references do not implicitly designate their
declarations as Core or copy a provider's selection onto a new consumer.
Retained selections for other originating Modules stay attached to those
Modules.

Require at most one supplied entry per using Module; options/reference
duplicates reject even if identical. Each supplied pair must be complete and
valid, including when unused. Wrong source snapshot, nonprimary declaration,
wrong owner/kind, absent export, unauthorized selection or conflicting
provider/context rejects. Missing required selection or unsupported complete
role binding declines affected use under ordinary binding/admission reporting.
There is no import-order winner, name search, implicit dependency or
Count-specific failure category. Fixed Type/known-shape counts need no input.

## Snapshots, expectations and later use

The using Module's existing `CompilationReference` binding context must retain
the checked provider snapshot, including checked reference-set composition.
The selection cannot introduce a separate dependency. That context's host
construction path must retain the actual logical provider, unambiguous revision, exact
artifact schema/revision or in-process declaration-contract selection,
governing semantic requirements, target/runtime context and checked export
map. A normalized reference that discards these facts cannot serve this route;
extending that shared projection is an implementation prerequisite.

Capture the using Module's complete original expected interface closure and
property/getter contracts separately from the actual provider declarations,
including every relied-on guarantee. Aliased exports may resolve to the same
declaration; retain every original expectation and owner/equality obligation.
Another role registration selecting the same provider must agree with the
ordinary admitted snapshot, not merely its Module string. This does not require
native/layout registration or widening its role catalog.

Retain the resolved pair and original context through bound/reference
projections. Replacement explicitly selects a new actual snapshot and checks
all original expectations under ordinary directed binding; it does not
reselect Count from new names or current host defaults. Consumers validate
structure, support and binding, not a provider body's semantic truth.

Source occurrence objects do not survive independent exchange. Local
declarations must be remapped through the ordinary checked producer/consumer
declaration maps before a retained role can be validated source-free; local
authorship alone requires no invented export. Cross-Module selection requires
real exports as above. If the selected shared schema/reference route cannot
retain the pair, getter, witness and original context, decline that route.
This proposal chooses no codec or serialized authorization.

## Alternatives and remaining gates

| Route | Disposition |
|---|---|
| Source occurrence pair plus external checked exports | Recommended: uses existing prebinding objects locally and ordinary selected identity externally, without a new semantic-handle API. |
| Checked local export map for every source selection | Viable where genuine exports already exist, but not a prerequisite for source-local selection. Requiring exports merely to identify local declarations adds unnecessary keys/publication obligations. |
| New public bound-interface/member handles from `Compilation` | Viable broader API work, but unnecessary for selecting actual syntax before binding and would require an additional staged handoff. |
| Extend native/layout registration or build a universal Core catalog | Would require interface/property descriptors and local-origin support beyond the existing carrier. Reuse shared checking machinery, not unrelated role slots. |

Production provider identities, revisions and export keys remain unselected.
So do CLI/manifests, built-in Count implementations and any new public
source-reference factory. The existing proposal owns Count behavior and
shared persistence/dispatch gates. In particular,
`TryBindInterfacePropertyGetter` still rejects member/accessor attributes and
contract tokens: admitting the accepted Pure getter is necessary general
witness work, not permission to weaken that contract.

Future qualification must cover both origin alternatives; wrong/stale source
occurrences and partial ownership; same-shaped decoys; missing/duplicate
selections; wrong export kind/property owner; export aliases; independent
providers, replacement and row/packing remaps retaining every expectation.
These are required cases, not tests run here.

If adopted, integrate only this origin-selection refinement into registered
queries' **Identity and authority** section and reference it from LCI-05.
No normative edit, compiler/test change or implementation-inventory update
is part of this proposal.
