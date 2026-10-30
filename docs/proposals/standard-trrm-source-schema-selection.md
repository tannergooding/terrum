# Standard TRRM source schema selection

**Status: unapproved candidate; provisionally actionable only under the
user's authorized missing-contract workflow.** This fixes an API selection
direction, not normative adoption, implementation support or execution
evidence.

## Decision, scope and prerequisites

Add one public **exact-schema-selected Standard emitter**, separate from
the existing Development emitter. Reuse
[NominalEnumsDraft](nominal-enum-persistence.md#exact-schema-selection-and-feature-composition)
and every selected schema's existing contents unchanged. Do not expose a
constant-return or enum-only helper as the general selection contract.

This completes only the public producer/CLI shape deliberately left open by
[draft-schema selection](trrm-draft-schema-profile-selection.md#explicit-caller-selection-and-operation-boundaries).
It neither supersedes a schema identity nor changes its composition.
Prerequisites for successful production are a checked bound program, its
actual declaration/provider origins and original expectations, and an
implementation of the selected schema's complete structural rules/catalog
and the retained semantics being emitted. A known identity alone supplies
none of those capabilities.

The [specification index](../specs/README.md) leads to the controlling
[format-versioning](../specs/representation/format-versioning.md),
[persistence](../specs/representation/persistence.md),
[admission](../specs/representation/requirements-and-admission.md),
[Module metadata](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics)
and [consumer-checking](../specs/representation/reader-trust.md) owners.
Their artifact-wide exact agreement, separate target/runtime requirements,
per-Module closure and producer/consumer obligations remain unchanged.

### Source evidence

These source boundaries are current implementation observations, not proposal
approval. Native/runtime and broader producer evidence is recorded in the
implementation inventories.

| Owner | Current boundary |
|---|---|
| [`TrRmExecutableCompiler.Emit`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.cs#L39-L49) | The existing two-argument method still selects Development. The provisional `EmitStandard` API selects the exact Standard baseline, NominalEnumsDraft, or F profile identity. It emits baseline scalar and bounded enum declaration/constant-return shapes, plus a bounded F NativeAddress/CallSite shape for one direct NativeImport and caller. |
| [`TrRmArtifactIndex`](../../sources/Terrum.CodeAnalysis/Modules/TrRmArtifactIndex.cs) | `TrRmArtifactSchema` and candidate factories remain internal. The public default index remains Standard; equal version bytes do not expose or select a source-producer schema. |
| [`RegisterWriter`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.RegisterWriter.cs), [`NominalEnumsWriter`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.NominalEnumsWriter.cs), [`NativeAddress writer`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.NativeAddress.cs), [`NativeAddress reader`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.NativeAddressReader.cs), [`Standard consumer API`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.StandardConsumption.cs) | The selector dispatches to internal helpers. Blittable enum declarations require Module target requirement 1; declaration and constant-return production otherwise preserves explicit 0/1. The F writer requires target requirement 1 and supports bounded direct-import calls, including staged calls in argument expressions. `LoadStandard` binds one retained MethodDef Import to one selected F provider and preserves byte-identical re-emission. Multi-Module provider closure, general replacement/remapping, broader Bodies and execution remain unsupported. |
| [`Program.TrRmExecution`](../../sources/terrumc/Program.TrRmExecution.cs), [`CLI usage`](../../sources/terrumc/Program.cs) | `compile-trrm --trrm-schema` forwards exact source-output identities; omission retains the existing Development default. The standalone CLI has no explicit NativeCoreRegistration input, so its current source path cannot author NativeImport source for the F writer. Artifact-loading commands keep their fixed routes. |
| [`Enum table tests`](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmEnumTablesTests.cs), [`foreign invocation tests`](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmForeignInvocationAddressEmitterTests.cs), [`CLI tests`](../../tests/Terrum.CodeAnalysis.Tests/Cli/CliTests.TrRmExecution.cs) | API tests cover the exact selectors, bounded enum values, F NativeAddress/CallSite reconstruction, and unchanged Development default. CLI tests cover the baseline/enum selectors, usage errors and no output replacement on invalid selection; F artifact execution remains a separate gate. |

## Exact public library contract

Add this method to `Terrum.CodeAnalysis.Modules.TrRmExecutableCompiler`;
all four arguments are required, with no optional/default schema:

```csharp
public static byte[] EmitStandard(
    BoundProgram program,
    string moduleIdentity,
    string schemaIdentity,
    uint moduleTargetRequirement);
```

`schemaIdentity` is the complete, case-sensitive out-of-band identity,
compared ordinally without trimming, normalization or component parsing.
The first source-producer integration uses these existing selections:

| Exact token | Selected schema |
|---|---|
| `trrm/1-draft-layout-queries-1` | The existing Standard baseline, unchanged. |
| `trrm/1-draft-nominal-enums-1` | Exactly the existing NominalEnumsDraft composition, not any enum composite. |

The initial public producer recognizes **only** the two identities above.
Other identities defined by the
[scalar registration](scalar-frozen-value-registration.md),
[CopyOrigin](reference-copy-origin-trrm.md#feature-schema-selection-and-canonical-writing),
[general-predicate](general-predicate-graph-schema.md),
[foreign-invocation](trrm-draft-schema-profile-selection.md#exact-compositions-and-identities),
[RootPin](trrm-root-pin-schema-selection.md#exact-proposed-selection) and
other candidate owners remain outside this method until their exact whole
schema and source-emitter support are deliberately added. A profile identity
must never be accepted just because its rows are understood by a reader or
because a common subset can be emitted. Later support adds one explicit exact
entry without changing the method signature or any default. New compositions
still need their own exact definition; the lookup never constructs one from
flags or name fragments. A token denotes the owner's fixed whole
schema/catalog, not future moving text at its links.

`moduleTargetRequirement` carries the existing Standard Module field
unchanged. Validate its assigned domain under the selected schema, then its
compatibility with the program and dependencies; do not infer it from the
schema, enum base or filename. Runtime policy comes from `program` and must
be representable under the selected schema. Neither parameter introduces a
target, ABI, runtime policy or lowering agreement.

Current bounded implementation: the Standard baseline executable producer
supports target requirement 1. NominalEnumsDraft declaration and
constant-return writers accept assigned requirements 0/1, with 1 required
when a retained enum is Blittable; the source-free scalar-return execution
helper remains target-1-only. Broader target support requires the selected
producer and consumer to support it, not just a different Module word.

The operation must:

1. Validate arguments and resolve/support-check the exact selection before
   schema-dependent production. Unsupported complete profiles cannot be
   reduced to a supported constituent because the program uses fewer facts.
2. Verify the program through the appropriate existing executable/library
   and retained-reference path. Keep one immutable selection through metadata,
   Body encoding, manifest closure and final structural validation; do not
   reset a helper to Standard or Development. Selection is per invocation,
   not ambient mutable state.
3. Emit the whole admitted program, preserving retained declarations,
   bodies, nominal identities, original binding expectations and required
   facts. Select independently of sample names or body patterns. Unsupported
   retained operations may decline; dropping them, erasing enum identity,
   substituting another codec or returning only declarations is not success.
   The same selected path must also handle supported programs with no enum
   use; an enum-bearing profile does not require every Module to use enums.
4. Establish schema-valid output and truthful producer facts, then return
   one complete owned byte array. No file is written by this API. A failure
   returns no artifact and never retries a different schema or legacy loader.

Each separately supplied dependency still has its own explicit selection
before decoding/binding. This method receives already bound facts; its
destination token is not a resolver instruction or a dependency default.
Packing/conversion remains governed by its existing preservation contract,
not by copying bytes into the chosen destination.

### Failures and preserved defaults

| Condition | Required public result |
|---|---|
| Null `program`, `moduleIdentity` or `schemaIdentity` | `ArgumentNullException` naming that parameter. |
| Invalid/empty Module identity, unknown/empty schema token, or malformed bound program | `ArgumentException`; argument errors identify the parameter where applicable. Existing verifier-invalid programs remain errors. |
| Unassigned `moduleTargetRequirement` | `ArgumentOutOfRangeException` naming that parameter. |
| Known schema without complete source-emitter support, or valid retained semantics/requirements the selected producer cannot represent/support | `NotSupportedException`, identifying the selection and unsupported obligation. No partial success. |

`Standard`, `NominalEnumsDraft`, `latest`, version pairs, numeric internal
enum values, altered-case/whitespace tokens and ad hoc composites are not
aliases. Development/private formats are outside this method's domain.
Resource and internal invariant failures remain failures, never empty
successful arrays; this proposal adds no recovery encoding.

`Emit(BoundProgram, string)` continues to select Development with its current
support/rejection behavior. Default indexes, writers, validators and loaders
keep their existing selection. In particular, producing selected candidate
bytes does not make `Load`, the default index or a fixed CLI consumer
automatically accept them. Common-subset bytes can be valid under several
explicitly selected schemas; no provenance detection is promised.

## Prospective CLI transport

Extend **only `compile-trrm`** with the optional pair
`--trrm-schema <exact-schema-identity>`. Omission calls the existing
two-argument `Emit`; presence calls `EmitStandard` with the exact token and
the command's existing Windows x64 Module target requirement. Preserve its
current runtime, arithmetic, input, identity, output and `--library` rules.
The option changes output selection, not source semantics or Core authority.

Check missing, empty and repeated option values during normal usage parsing:
current command exit `2`, `TRM2000`. After successful source binding, forward
the token unchanged to the library, whose selection/support preflight precedes
encoding. Map its unknown-`schemaIdentity` argument error to exit `2`,
`TRM2000`; a known unsupported selection or unrepresentable retained program
maps to exit `1`, `TRM5001`. Earlier source diagnostics retain their existing
failure path; I/O/output failures retain their categories. Selection, source
or encoding failure must not create/replace the destination; publish only
after complete production and validation.

These statuses describe this existing command and its experimental
`--json` surface, not adoption or amendment of
[`terrum.cli/1`](../specs/cli-protocol.md). No response-envelope field is added.
Other commands reject this new option as usage error; it does not select
their input schemas. Public reader/loader or dependency CLI selection is a
separate surface decision, not an implicit consequence or a prerequisite
for this producer API.

## Alternatives and tradeoffs

| Alternative | Assessment |
|---|---|
| Public `EmitNominalEnumsDraft` with a fixed schema | Smallest isolated wrapper, but proliferates entry points for every already-defined exact composition and invites treating a bounded helper as the whole producer. Coherent for a deliberately single-profile API, not the recommended reusable selection boundary. |
| **`EmitStandard` with the required exact identity; recommended** | One method, no public internal-enum ordinals, options object or profile algebra. Typos need runtime validation, but the token is the existing exchange identity and can later name other explicitly defined profiles. Keeping the target argument explicit reuses the helpers' existing input rather than inventing a schema-dependent target default. |
| Make the internal enum public, or add capability flags/options | A public closed enum would still need an exact identity mapping and an API change for new entries. Flags would additionally need a new composition/precedence contract. Neither is needed to close this gap. |
| Change `Emit`'s default or auto-select from retained Features | Changes existing callers or guesses a whole schema from content. Rejected; explicit selection and stable defaults solve a different problem. |

## Required acceptance cases

These are future checks through the public producer API and CLI, not results
of this documentation change. Reuse the existing enum/structural fixtures
and independently authored selected-schema artifacts; no new friend assembly
or source enum syntax is needed.

| Case | Required outcome |
|---|---|
| Defaults and baseline | Existing two-argument scalar production and omitted CLI option preserve Development behavior. Explicit baseline emits supported Standard content; retained enum facts reject there without candidate retry. |
| Nominal selection | The exact nominal-enum token emits an admitted owned/imported enum library, retaining its complete declarations, values and original provider expectations. Exercise unrelated enums and ordinary supported scalar content through the same public route, not a special public constant-return API. |
| Empty feature use | A supported enum-free program still emits under the explicitly selected enum schema. Derive only requirements of retained content, using that schema's catalog rather than forcing every available Feature or borrowing another Module's closure. |
| Invalid selection | Null, empty, changed case, leading/trailing whitespace, internal enum names/numbers, `latest`, fabricated composites and unassigned target values produce the specified argument failures. Unsupported known selections fail before production even for a common-subset program. |
| Cross-feature/context | NominalEnumsDraft rejects retained CopyOrigin, foreign-invocation, general-graph or RootPin facts not in its schema. An already-defined exact enum composite succeeds only when its whole source-emitter/schema support exists; otherwise decline, never silently upgrade/downgrade. Interleaved differently selected calls cannot leak selection. |
| Retention and target | Uncalled declarations, nested enum types and original imported expectations survive or explicitly decline. Invalid provider origins, missing required facts, incompatible targets/runtime or unsupported operations cannot disappear to obtain output. Reuse the enum owner's binding and manifest negatives. |
| Wrong consumers | Candidate-only records fail the existing default Standard route; Development bytes do not become Standard by an extension/header rewrite. Shared valid subsets alone are not evidence of originating schema. |
| CLI boundaries/output | Exact option reaches the same library selection; omission, missing/repeated/unknown tokens and known unsupported profiles take the specified paths. Other commands reject the option. Failure preserves a sentinel destination, emits no success artifact and keeps existing diagnostic/JSON behavior. |

No selection/API choice within this recommendation remains unresolved.
Formal approval, implementation/qualification of each producer profile,
source declaration collection, independent missing codecs and separately
requested reader/loader surfaces remain distinct gates. On adoption,
document the public method and CLI usage and update support inventories only
with actual implementation evidence. No accepted specification is amended
by this proposal.

**Non-goals:** new schema identities/compositions, enum rows or operations,
source enum syntax, compatibility aliases, version-based inference,
fallback/private encodings, private revision-97 semantics, host/provider
authorization, native/runtime policy, conversion APIs or execution support.
