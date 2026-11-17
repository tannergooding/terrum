# Standard TRRM initialization-predicate failure identity

**Status: unapproved; provisional direction under the user-authorized
missing-contract workflow.** This document selects a candidate binding
handoff, not normative adoption, compiler support or execution evidence.

## Decision and exact gap

For exactly **`trrm/1-draft-init-reference-bodies-general-predicate-graphs-1`**
(B + I + G), recommend **required out-of-band, originating-Module selections
using the existing `CoreContractFailureIdentitySelection`**, followed by
ordinary exact TypeDef binding. A retained phase-2 declaration ensure requires
that Module's `PostconditionExceptionClass`. Preserve the selection alongside
the artifact and through source-free loading, references and re-emission.
Source-free does not mean context-free.

The [composition owner](standard-trrm-initialization-predicate-composition.md)
requires original phase-2 assertions, exact false-ensure PostconditionException
and complete binding context. The
[semantic selection owner](core-contract-failure-identity-selection.md)
already chooses the role record and originating-Module rule, but leaves
interchange integration gated. The
[source CLI owner](core-contract-failure-cli-selection.md) explicitly excludes
source-free TRRM commands. Neither the graph's phase/ordinal/reference nor an
ordinary class Import identifies which declaration was selected for this role.
This proposal closes that profile-specific transport/preservation decision;
the rejected declaration-clause implementation is a separate gap.

| Safe alternative | Assessment |
|---|---|
| **Accompanying Module-level role table plus ordinary bindings — recommended** | No new wire carrier or identity namespace. Reuses the semantic record and existing declaration references. Exchange must retain the producer's table and actual binding context; artifact bytes alone cannot recover or authenticate it. |
| Serialize an explicitly assigned phase-2 failure binding | A separate MethodDef-owned binding keyed by phase-local ordinal could reference the selected complete class through a full TypeSpecRef and ordinary TypeDef/Import. This would pin the per-clause declaration in bytes, but needs an assigned clause/reference schema, ownership/cardinality, canonicality, catalog and remapping rules. It still needs authorized Core designation and actual provider binding. Viable for a deliberate self-describing redesign, not needed for this bounded handoff. |

There is no assigned failure slot to reuse in `000B`: its payload remains
**12 bytes / 20 bytes including the Contract header**, with flags/reserved
fields zero. Mere presence of a class Import is not a serialized role
designation. Do not append an unassigned reference, use a graph operand,
populate `ExplicitFailureType` for ensure, or hide the selection in debug data.
No ID, Feature, section, opcode, version or serialized discriminator is added.

## Owners and preserved boundaries

| Owner | This proposal's delta or preserved responsibility |
|---|---|
| [B + I + G composition](standard-trrm-initialization-predicate-composition.md) | Complete its deferred failure-identity binding input for phase 2. Preserve its exact identity, graph/Body composition, Feature closure and phase-3 separation. |
| [Core contract-failure selection](core-contract-failure-identity-selection.md#selection-carrier-and-authority) | Reuse its authority, text domain, optional role slots, ordinary binding and original-owner rules. Its **unapproved** zero-supplied ensure-construction amendment remains owned there; this document neither reopens nor independently approves it. |
| [Source CLI selection](core-contract-failure-cli-selection.md) | Keep its manifest grammar and source/local-map rules unchanged. Add a distinct artifact-only transport below, not a provider loader to that manifest. |
| [Runtime postconditions](../specs/runtime/standard-failures.md#2-runtime-postconditions) | Preserve exact PostconditionException for normally false ensure, not a RequireException override, selector falsehood or predicate/helper failure. Creation/enforcement remains independently gated. |
| [Binding](../specs/representation/binding-and-replacement.md), [logical records](../specs/representation/logical-records.md#standard-runtime-categories-encoding-pending), [reader trust](../specs/representation/reader-trust.md) | Preserve complete original expectations, required origins and safe-consumption checks. This input completes no missing class/witness/construction/provenance codec and requires no arbitrary provider-Body truth audit. |
| [Format selection](../specs/representation/format-versioning.md) | Keep `(1,0)` and the existing exact B + I + G token. This supplies its required accompanying context, not another wire composition or a compatibility reader. |

The [E1 anchor proposal](standard-trrm-core-exception-identity.md) remains
exactly B + E1. Its IException/Unreachable pair and
`--trrm-core-exception-anchor` do not select PostconditionException or this
profile. Required checked self-interface evidence remains the semantic
selection owner's prerequisite; supplying it does not select E1 or impose an
E1 Unreachable dependency. No E1/CheckedExceptions composition, failure-event
semantics, factory ABI, native unwinding or new runtime policy is decided here.

Current source evidence is narrower than this recommendation:

- [`BoundProgram.cs`](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs)
  retains `SelectedFailureClass`, `ExplicitFailureType` and
  `UsingModuleIdentity` on each declaration clause, plus the program's role table.
- [`Binder.cs`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.cs) selects
  PostconditionException for ordinary ensures;
  [`SemanticVerifier.cs`](../../sources/Terrum.CodeAnalysis/Semantics/SemanticVerifier.cs)
  checks the clause's exact pair against its using-Module selection.
  This is not proof of real provider/class/witness/construction resolution.
- [`Standard consumption`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.StandardConsumption.cs)
  accepts the separate E1 anchor record, not this record.
  [`TrRmExecutableCompiler.cs`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.cs)
  still rejects declaration clauses. Graph/selector support does not establish
  this bridge or phase-2 executable support.

## Required selection and resolution

Supply one immutable table for the operation's retained originating contexts,
not one record for the importing caller or first Module:

| Existing field | Candidate B + I + G obligation |
|---|---|
| `UsingModuleIdentity` | Exact logical Module that authored the retained ensure, including an originating inherited clause. |
| `PostconditionExceptionClass` | Required exact `(ProviderIdentity, ExportKey)` selecting that Module's authorized public primary nongeneric Core PostconditionException managed class. |
| `RequirementExceptionClass`, `RequireExceptionSchema` | No new requiredness or Standard carrier here. Preserve supplied slots and apply their owner's validation; their retained uses still need their independently supported representations. A postcondition-only record may leave both null. |

Require exactly one record per originating Module with any retained phase-2
assertion, including bodyless expectations, uncalled declarations, constants
and eliminated failed paths. Phase-3 selectors alone require no postcondition
selection; a false selector is a normal outcome. Fail-fast does not remove
the selection or retention obligation.

The designated [Core authority](../specs/libraries/adopted-inventory.md#1-status-and-identity)
supplies the producer's original role mapping and actual provider context.
The record authenticates nothing. A receiving host must receive or explicitly
establish that same originating mapping, not substitute its current defaults.
If only bytes and a new ambient Core preference are available, affected
semantic loading declines: bytes cannot distinguish two candidate class
Imports as the intended role. Structural-only inspection remains possible.

Use nonempty well-formed scalar text without NUL, compared ordinally without
normalization, trimming or case folding. Whitespace-only identities remain
valid text. Every supplied record/role is checked, including unused roles;
duplicate using-Module records reject even when identical. Reject partial,
malformed, unauthorized, missing-required or conflicting selections before
affected use. Existing source/reference selections are retained inputs, not
an option-order override or a table to merge to its weakest entries.

Before reconstructing or emitting an affected clause:

1. Snapshot the supplied table and selected artifacts/context. Select their
   complete schemas before decoding. For this bounded operation, require
   **the exact B + I + G selection for root and every supplied provider
   artifact**, preserving I's existing conservative boundary. No E1, standalone
   G, LQ or private decoder is inferred. A differently selected provider
   declines rather than being relabeled.
2. Resolve the exact provider Module and its one unambiguous selected
   revision/snapshot, then the exact key and TypeDef kind (`DefId` kind `0x13`).
   Require a public provider-local primary declaration, not an imported proxy
   re-export. Names, package paths, compatible shape and dependency order
   select neither Module nor key.
3. For an external anchor, retain the originating Module's ordinary TypeDef
   expectation/Import and ModuleRef target/runtime requirements. An authorized
   local Core selection uses its actual public Export and direct declaration.
   Preserve full types, class/self-witness/member expectations, selected
   construction bindings and applicable edition/catalog/target/runtime context
   under the semantic owner. Selector strings cannot supply missing evidence.
4. Establish the clause's semantic-origin correspondence. An ordinary local
   declaration uses its owning Module; imported, inherited, outlined or
   relocated clauses retain their original declaration/context. Do not infer
   origin from physical MethodDef placement. If the supported representation
   cannot recover that correspondence without source, decline that route.
5. Set/retain `UsingModuleIdentity` and `SelectedFailureClass` exactly;
   phase-2 `ExplicitFailureType` stays null. Compare every original expectation
   and the authorized selected class. Correct role identity cannot waive an
   incompatible contract, and compatible structure cannot establish the role.

Different originating Modules may explicitly select different authorized
classes; this is not a global singleton or a new delivery-family rule.
Legal keys may resolve to one nominal declaration, but retain each original
key/expectation and alias equality. A replacement must preserve the existing
binding correspondence and stronger relied-on facts; no alternative key,
class or constructor search repairs a failure.

## Concrete API and CLI handoff

Propose these additions in
`Terrum.CodeAnalysis.Modules.TrRmExecutableCompiler`; the names and signatures
below are **unimplemented candidates**:

```csharp
public static BoundProgram LoadStandardWithCoreContractFailureIdentitySelections(
    TrRmStandardArtifactInput input, string moduleIdentity,
    IReadOnlyList<TrRmStandardArtifactInput> providers,
    ImmutableArray<CoreContractFailureIdentitySelection> coreContractFailureIdentitySelections);

public static CompilationReference CreateStandardReferenceWithCoreContractFailureIdentitySelections(
    TrRmStandardArtifactInput input, string moduleIdentity,
    IReadOnlyList<TrRmStandardArtifactInput> providers,
    ImmutableArray<CoreContractFailureIdentitySelection> coreContractFailureIdentitySelections);

public static byte[] EmitStandard(
    BoundProgram program, string moduleIdentity, string schemaIdentity,
    uint moduleTargetRequirement,
    out ImmutableArray<CoreContractFailureIdentitySelection> coreContractFailureIdentitySelections);
```

The table is a required explicit argument, with no ambient/default provider.
Empty/default means no supplied selections, not automatic discovery. Existing
load/reference entry points keep their defaults and decline a required missing
context. The new names avoid ambiguity with the existing E1 collection overload.
Actual provider/eligibility context must be available through supported ordinary
binding; if it is not, these signatures do not make the operation succeed.

Source compilation continues to use
`CompilationOptions.CoreContractFailureIdentitySelection` for its actual Module
and originating selections on `CompilationReference`. The writer consumes the
checked bound table rather than accepting a replacement. Its new `out` result
returns the exact selections needed by the emitted retained contexts, alongside
the bytes; it is not serialized data. Byte-only emission does not make bytes
self-contained: callers exchanging them must preserve the accompanying table
and binding context. No new output filename or sidecar grammar is selected.

For `run-trrm`, `build-trrm` and `build-trrm-library`, propose only:

`--trrm-postcondition-anchor <using-module> <provider-module> <export-key>`

Consume exactly three following argv elements; allow repetition for distinct
using Modules only. Each occurrence creates a postcondition-only existing
record. Require explicit `--trrm-schema` with the exact B + I + G token,
`--trrm-module`, and independently selected `--trrm-provider` inputs. Reject
missing/empty operands, duplicate using Modules and ineligible command/profile
combinations; do not normalize identity/key operands or treat them as paths.
This flag supplies no provider bytes, source export map or Core authentication.

`compile-trrm` instead uses the source owner's proposed
`--contract-failure-core-manifest`, with exact `usingModuleIdentity == --identity`
and its unchanged real-declaration requirements. Do not accept the artifact
flag there, extend the source manifest's grammar, or make E1 flags aliases.
Other commands reject the new flag. Artifact commands pass the table through
the new loader and consume its checked program, never reload through an
unconfigured Standard/native helper. Known missing enforcement support still
declines before execution or output replacement.

Argv/profile misuse takes the existing usage path (`TRM2000`, exit 2);
selected-input binding/support failures take the existing artifact failure
path (exit 1). Source diagnostics retain their own paths. Add no
`terrum.cli/1` command/envelope or diagnostic assignment, and preserve
destinations on admission/emission failure.

## Source-free preservation and acceptance

Load/reference results retain the table, each clause's original semantic
owner/phase/ordinal, exact failure pair and all actual/original binding
dependencies. The reference exposes its originating selections rather than
restamping clauses with the next source compilation's selection. Re-emission
returns those selections without source lookup or predicate reprojection.
JIT/AOT consume that checked context and retain actual declaration/provider/code
dependencies; they do not reselect a failure class from the physical check site.

Rebuild typed TypeDef/Import/Export/ModuleRef/MethodDef correspondence,
Contract ranges, graphs/occurrence rows, complete types, witnesses, selected
members, Body maps/indexes/extents and per-Module closure under their owners.
Physical row permutations and declaration cloning preserve logical origins
and the role table. Changing an output Module identity is not automatic
permission to rekey a retained originating selection: decline when the operation
cannot preserve its original origin/binding correspondence. Optional source
data cannot repair a lost origin. A destination lacking any retained carrier
declines; snapshot-only byte passthrough is insufficient remapping evidence.

These are **future tests**, not results of this document:

| Case | Required result |
|---|---|
| Explicit source/API transport | Postcondition-only selection at a non-name-derived key and non-first provider survives a retained ensure even when its output selector is bounded. No requirement/schema role is invented. |
| Source-free load/reference/re-emission | Remove source/debug text, permute rows and clone declarations; preserve phase-2 predicates, origin, exact pairs, original expectations and returned table. Re-consume with that table; no phase-2-to-phase-3 substitution. |
| Several originating Modules | Caller/default changes and relocated/inherited checks preserve each originating selection. Accept supported exact alias correspondence while retaining all keys; reject conflicting retained/host tables and alias splitting. |
| Missing/invalid context | Missing required slot, identical/conflicting duplicate, malformed text, unauthorized selection, wrong originating Module or ambiguous provider revision rejects. Uncalled/proved/terminal paths cannot bypass it; phase-3-only use needs no slot. |
| Ordinary binding negatives | Wrong/private/missing key, proxy export, wrong kind/generic class, wrong complete self witness or incompatible member/target/runtime expectation rejects without shape/name/provider fallback. |
| Profile/CLI isolation | Reject E1/standalone-G/bounded-I substitutions, mixed supplied schemas, misplaced flags, missing operands and duplicate using-Module supply. Missing selection or unsupported obligations preserve sentinel destinations and start no worker. |
| Independent execution gate | A decoded graph and resolved identity with missing construction/origin/enforcement support declines. Once an independently admitted enforcement route exists, check the exact originating class on normally false ensure; true ensure and phase-3 falsehood do not construct it. |

Use public compiler APIs and existing TRRM/source-selection/CLI fixture patterns;
confirm future test selectors actually execute.

Within this recommendation the transport choice is settled provisionally.
Real provider declarations, complete class/witness/construction and semantic-
origin codecs, source-free transformations and enforcement remain acceptance
gates, not approvals supplied here.
