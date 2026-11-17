# Explicit CLI contract-failure identity selection

> [!IMPORTANT]
> **Status: UNAPPROVED; provisional direction only; non-normative.**
> The user explicitly authorized preparing the smallest genuine missing-contract
> decision and continuing under it provisionally, pending later review. That
> permits implementation against this candidate; it does not adopt this
> proposal, its prerequisites or the proposed ensure-construction amendment.

## Decision, evidence and owners

Recommend one independent host input, **`--contract-failure-core-manifest <path>`**,
for the already proposed per-Module
[`CoreContractFailureIdentitySelection`](core-contract-failure-identity-selection.md).
Select its complete grammar with
**`schema: "terrumc.contract-failure-core-manifest/1"`**. Supply exact role
selectors, a bounded checked source-local export map where needed, and no
provider loader or serialized authority.

Source inspection establishes a transport decision, independently of the
reported checked-exceptions failure:

| Evidence | Established boundary |
|---|---|
| [`CompilationOptions`](../../sources/Terrum.CodeAnalysis/Semantics/Compilation.cs), [`CompilationReference`](../../sources/Terrum.CodeAnalysis/Semantics/CompilationReference.cs), [selection carrier](../../sources/Terrum.CodeAnalysis/Semantics/CoreContractFailureIdentitySelection.cs) | Options carry the current Module's selection; references retain originating Modules' selections. Duplicate using-Module supply rejects even when identical. Table validation alone does not resolve actual exports. |
| [`Binder.BindDeclarationContractClauses`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.cs), [active API tests](../../tests/Terrum.CodeAnalysis.Tests/Semantics/CoreContractFailureIdentitySelectionTests.cs) | Default declaration requirements and declaration ensures need their respective selection and otherwise report `TRM3005`, under either runtime policy. Existing positives with selector strings establish bounded transport/predicate binding, not real provider, exception-witness or constructor validation. |
| [`Program.Compilation.cs`](../../sources/terrumc/Program.Compilation.cs), [`Program.TrRmExecution.cs`](../../sources/terrumc/Program.TrRmExecution.cs), [`Program.cs`](../../sources/terrumc/Program.cs) | Source factories populate Layout, NativeCore, NativeOutput and evaluation selections, but never this selection. The E1 artifact-only anchor option cannot supply it to source compilation. |
| [`Program.Frontend.cs`](../../sources/terrumc/Program.Frontend.cs) | Frontend build/library/run immediately compile original source through a child C# compiler process. Neither the wrapper nor its forwarding contract carries this selection. The test-only component projection is a different route. |
| [Manifest composer](../../sources/terrumc/CoreManifestRegistrationComposer.cs) and [Layout](../../sources/terrumc/LayoutCoreManifestReader.cs), [NativeCore](../../sources/terrumc/NativeCoreManifestReader.cs), [NativeOutput](../../sources/terrumc/NativeOutputCoreManifestReader.cs), [evaluation](../../sources/terrumc/EvaluationCoreManifestReader.cs) readers | Their closed grammars and typed payloads assign no contract-failure slot. Native/Layout's provider carrier excludes local providers and has no managed-class payload. |

This blocks supplying the required context through those source routes; it
does **not** establish that the reported `frontend-build-library`
`TRM4002 Invalid bound program` has that cause.
`CompileTrees` maps any library-emission `ArgumentException` to `TRM4002`.
The current statement-require binder produces `BoundRequire` without consulting
this selection. Moreover, [`NativeEmitter.EmitLibraryCore`](../../sources/Terrum.CodeAnalysis/Native/NativeEmitter.cs)
reverifies checked exports with scoped no-failure proofs, independently of
Core role selection. The active
[`NativeScalarRequirePreservesThreeParameterAbiAsync`](../../tests/Terrum.CodeAnalysis.Tests/Cli/CliTests.FrontendRequire.cs)
already expects checked-library refusal and preservation for its potentially
failing export. Neither that case nor a stale success assertion diagnoses an
unspecified failure. Its exact source, current test expectation, final compiler
inputs and failing verifier boundary are needed before attributing it.

Preserve [requirement selection/construction/defaults](../specs/language/requirement-failures.md),
[postcondition ownership](../specs/runtime/standard-failures.md),
[Core authority](../specs/libraries/adopted-inventory.md#1-status-and-identity),
[ordinary binding/replacement](../specs/representation/binding-and-replacement.md)
and [consumer checking](../specs/representation/reader-trust.md).
The prerequisite remains the semantic owner, including its independent exact
IException context and schema/construction publication gates. This fills only
its deferred CLI/local-map handoff. No accepted text is superseded.

| Candidate | Disposition |
|---|---|
| Selector-only repeated flags/file | Smallest external-only transport, but source CLI has no ordinary checked class/schema reference input; strings cannot supply actual local exports or schemas. Sound only with refusal at that boundary. |
| **Independent bounded manifest below** | Recommended: delivers the existing record and actual local declaration associations without changing unrelated grammars. External delivery still requires real ordinary references. |
| Extend evaluation, NativeCore or Layout; introduce a universal Core registry | Larger ownership/payload changes, including managed classes and local origins. Reuse checking helpers, not their role slots or version tokens. |

## Exact manifest grammar

Every object has exactly the required properties below; property order is
irrelevant. Only explicitly nullable fields accept null. Require strict UTF-8,
at most one leading signature, one root object and trailing JSON whitespace
only. Reject duplicate decoded properties at every depth, malformed scalar
text, comments, trailing commas, wrong types and unknown fields. There are no
alternate versions, schema/option aliases, inferred fields or fallback readers.

`Text` is nonempty well-formed scalar text without NUL, compared ordinally
without trimming, case folding, normalization or path interpretation.
Whitespace-only identity/key text remains valid. `u32` is an unsigned decimal
integer token in `0..4294967295`, without sign, fraction, exponent or coercion.
Source ordinals/spans additionally fit `Int32.MaxValue`, with checked sums.

| Root property | Required value |
|---|---|
| `schema` | Exact token above; host configuration, not an artifact schema or ABI. |
| `coreAuthorityIdentity`, `usingModuleIdentity` | Each `Text`; explicit authority designation and using logical Module. |
| `requirementExceptionClass`, `postconditionExceptionClass`, `requireExceptionSchema` | Each Selector or null; at least one is nonnull. |
| `localDeclarations` | Local or null; nonnull exactly when a selected provider equals the using Module. |

**Selector** has exactly `providerIdentity: Text`, `exportKey: Text`.
Map the three properties directly to the existing selection's three slots.
Missing required roles decline affected use; omission never selects a default
provider/key. Every supplied role is checked even when unused. Reject repeated
provider/key pairs across roles and roles resolving to the same declaration.

**Local** has exactly:

| Property | Shape |
|---|---|
| `origin` | Origin below, with `moduleIdentity == usingModuleIdentity`. |
| `sourceSnapshotSha256s` | Ordered array containing one digest for every source tree in this compilation, not merely trees with mapped classes. |
| `classExports` | Array of zero through two ClassExport objects. |
| `requireExceptionDeclaration` | SchemaDeclaration or null; nonnull exactly when the selected schema is local. |

**Origin** has exactly `moduleIdentity: Text`, `revisionIdentity: Text`,
`schema: { identity: Text, revision: u32 }`,
`governingSemanticEdition: u32`, `targetRequirement: u32`,
`runtimePolicy: u32`. Require nonzero schema revision, governing edition 1,
target requirement in `0..1` with ordinary target/requirements compatibility,
and runtime policy in `0..1` equal to the selected compilation's policy.
These are explicit actual source/catalog contract
and snapshot inputs, not artifact decoder requests or authentication.
Do not invent a production catalog identity or infer one from this file.

**ClassExport** has exactly `exportKey: Text`, `sourceInputOrdinal: u32`,
`declarationStart: u32`, `declarationLength: u32`. Require positive length,
an in-range ordinal and nonoverflowing in-snapshot end. After snapshot checks,
select exactly one actual `ClassDeclaration` node with that complete `Span`,
then retain the tree/node-to-bound-declaration association. Select no name,
name span, body span, generated declaration, imported expectation or enclosing
approximation. Require a public primary nongeneric managed class with the
prerequisite's complete role contract. Ordinary partial assembly remains
ordinary binding; a secondary occurrence cannot supply the primary export.

**SchemaDeclaration** has exactly `exportKey: Text`, `qualifiedName: Text`.
The latter is an explicitly supplied ordinary source-qualified name, not a
guessed namespace. This object **defines** one actual public primary,
nongeneric, nonrepeatable semantic AttributeSchema in this local host catalog:
exactly the complete RequireException schema in
[requirement failures §3](../specs/language/requirement-failures.md#3-callable-owned-defaults-and-the-core-exception-class),
with its single required direct-typeof `ExceptionType` recipe and full
primary callable/accessor/interface/generated-slot applicability. These fixed
declaration properties are this grammar's definition, not optional defaults
or a name-based provider lookup. Create the actual declaration once, install
it in ordinary attribute lookup and export that same object. Reject declaration-
space collisions; do not merge by name/shape or synthesize aliases. It is not
an attribute class/constructor or permission to emit an AttributeSchema DefId.

Local export keys are unique across both kinds. Every local entry must be
selected by exactly one corresponding role; require one map entry for each
local selected class, with no duplicate occurrence or added export aliases.
Existing ordinary reference aliases retain their original equality/expectation
obligations; they cannot supply a missing requested key or collapse distinct
roles. This map supplies explicit host source exports, not new TRRM rows.

Each digest is exactly 64 lowercase ASCII hexadecimal digits for SHA-256 over
the complete decoded UTF-16 snapshot, code units little-endian, without BOM,
terminator or normalization, as in the
[existing source mapping contract](standard-trrm-source-generic-method-export-inputs.md#source-order-and-stale-map-checks).
The vector's count/order matches the positional input followed by `--source`
occurrence order. Equal text in two inputs still denotes separate tree
occurrences. Check every digest before binding, including unmapped inputs.
Ordinals, spans and hashes locate occurrences; none becomes a Module identity,
export key, authorization or reusable semantic admission.

For the source API, keep the existing selection record unchanged and propose
one optional companion,
`CompilationOptions.CoreContractFailureLocalDeclarations`, with the Local
origin, class exports and optional schema declaration above. Its class exports
carry `(SyntaxTree, SyntaxNode Declaration, ExportKey)`, not CLI coordinates;
require reference-identical membership in the actual supplied trees and the
same primary-node checks. Snapshot the collections. The CLI converts validated
coordinates to these occurrences before binding; rebinding resolves fresh
declarations rather than reusing a prior bound association. The schema input
defines the fixed actual declaration above. No public source-reference factory
or new field on the existing role-selection record is needed.

## Authority, snapshots and composition

The designated host supplies the manifest outside authored source/packages.
Passing a path designates configuration; `coreAuthorityIdentity` authenticates
nothing. No path, output name, namespace, `--corelib`, runtime policy, matching
shape or referencing package grants Core authority.

For **local** selectors, resolve the exact requested key in the checked map
above, not by class/schema spelling. Validate selected class identities,
distinctness and complete self-typed witnesses against the separately selected
checked Core IException anchor. Bind ordinary zero-supplied construction at
the actual check, preserving its owner, access, defaults, effects and cleanup.
This manifest chooses no interface, Unreachable payload, constructor key or
factory. Missing exception-interface context or unsupported complete role
checking rejects; the current strings-only transport is not sufficient.

For **external** selectors, use only this compilation's already selected
ordinary `CompilationReference` actual provider/export projection. Require
one unambiguous admitted revision, exact artifact schema or source contract,
edition/requirements/target/runtime, public primary provider-local definition
of the right family, and every original expected contract. A manifest-local
definition, consumer proxy or Native/Layout snapshot cannot impersonate it.
This manifest contains no external declaration definitions, provider paths,
automatic dependencies or loader. Current eligible standalone source routes
expose no such reference input: reject external/mixed selections until that
ordinary route can actually supply the required evidence, including unused
selectors. Do not select another provider or schema.

Read selected configuration files once in argv option order, before direct
source/artifact acquisition. Check `compile-trrm --identity` agreement before
source reading. Capture source trees and selected reference bytes once, validate
the entire digest vector, then resolve local exports during declaration
binding before any affected clause/default use. Retain one immutable
options/source/reference context; never reread files to repair a stale map.
All trees belong to the explicit using Module; file names and diagnostic labels
cannot override it. Every options-bearing factory carries the selection and
local companion; reference projections retain their resolved declarations and
original contexts. This requires implementation, not acceptance of selector
strings alone.

Compose atomically through the existing manifest-admission owner. Keep Layout,
NativeCore, NativeOutput and evaluation grammars/eligibility unchanged.
Require exact authority/using-Module agreement and complete origin agreement
for every shared provider, including local evaluation catalogs and ordinary
references. Preserve actual snapshot/declaration correspondence, not merely
equal labels; a shared Module/key cannot designate different declarations.
Keep these local classes/schema outside the external-only
`CoreProviderModuleBindings` carrier. No option-order winner, shape-based
deduplication or partial registration becomes visible.

Options/reference duplicate supply for the same using Module rejects even if
identical. Other originating Modules retain their own selections; never apply
the caller's record to them. Preserve keys, original expectations, owner/phase,
schema/default/construction dependencies and actual origins through reference
projection, rewriting, verification, replacement and lowering.
Serialize neither this manifest nor authority into TRRM/private/debug/ABI
outputs. Retained semantic dependencies still require a supported ordinary
codec and accompanying originating-Module context; otherwise refuse output.
Direct normalized PE/JIT output gains no new runtime ABI from this input.

## CLI routes, forwarding and refusal

The option consumes one following argv element, once, before optional final
`--json`. Reject equals/short forms, missing/empty operands, `-`, dash-leading
operands and repetition. An explicit relative/absolute path can name a
dash-leading file. Resolve once against invocation cwd; require a regular
file. No extension, include, URL, environment expansion, stdin or discovery.

| Route | Candidate applicability |
|---|---|
| `analyze`, `build`, `build-native-import`, `build-library`, `jit` | One selection for the complete source compilation, including supported corelib/source-grant variants; preserve all independent import, export, arithmetic, safety and runtime gates. |
| `compile-trrm`, including `--library` | Source binding only, with ordinal `usingModuleIdentity == --identity`. Until a supported declaration/context codec can retain this selection and its obligations, refuse publication, including Development output; never drop it or infer E1/composite-schema support. |
| `frontend-build`, `frontend-build-library`, `frontend-run` | Subject-source selection in the original-source C# child only, as specified below. |
| All other routes | Usage error before consuming their source/artifact inputs. Includes syntax, scanner/component probes, `frontend-component`, private module/native/set/graph producers and source-free TRRM commands. |

For frontend wrappers, preflight argv/path identities, capture the original
subject text, then forward the absolute manifest operand exactly once with the
existing internal `--frontend-command`/`--source-name` handoff. **The checked
child alone captures/adopts the manifest**, before consuming forwarded stdin;
the wrapper must not independently admit a snapshot and then reopen its path.
Thus configuration is not promised to precede the outer wrapper's source
capture. Its local digest vector has one entry for the forwarded decoded
subject snapshot; UTF-8 re-encoding preserves those UTF-16 code units.
The diagnostic source name and component directory select no logical Module
or declaration. Allow this new option with the exact existing internal
frontend command pairs, without relaxing any other manifest's refusal.
Do not configure the frontend component, replay projected records, silently
drop configuration or retry an unconfigured route after child refusal.

Omission supplies no record; previously supported unconfigured programs remain
usable, while required missing dependencies still diagnose. In particular,
checked-exceptions alone neither requires these roles nor grants failure
transport or outward native unwinding.

Reject manifest aliases with any other configuration, source, selected
artifact, DLL/EXE/TRRM destination or ABI output using existing path/file-identity
checks before publication. Bad/ineligible argv and identity disagreement use
experimental `TRM2000`/2; invalid/unreadable configuration, stale maps and
unresolved supplied selections use `TRM2001`/2; forbidden aliases use
`TRM2002`/2. Ordinary source/missing-required-role diagnostics remain exit 1.
Later unsupported emission and output errors retain their actual route
categories; these are no new `terrum.cli/1` code assignments.

All admission, binding and emission refusals precede output replacement and
program execution, preserving existing artifact/ABI bytes. Reuse staged
publication and existing artifact-pair handling; each artifact replacement is
atomic, not a newly promised cross-file transaction. Report late publication
failure honestly, without success-shaped fallback or false rollback claims.
Human/experimental JSON writers remain unchanged. The selected
[`terrum.cli/1`](../specs/cli-protocol.md) gains no command, envelope or
configuration extension: reject combining that selection with this option.

## Integration gates and focused cases

Implementation remains unapproved. Integrate in the named readers/composer,
source and frontend option owners, shared API evidence/verification paths and
the [informative CLI reference](../implementation/cli-protocol.md) only after
the corresponding work exists. Do not amend normative specifications here.
Real exception-interface context, complete class/schema/constructor binding,
ordinary external reference delivery, retained-context codecs and declaration
clause runtime enforcement are separate gates, not support established by a
CLI parser. Current native lowering explicitly refuses declaration clauses.

Required cases below are candidate acceptance tests, **not executed evidence**.
The active API, [evaluation-manifest](../../tests/Terrum.CodeAnalysis.Tests/Cli/CliTests.EvaluationCoreManifest.cs)
and frontend-require fixtures provide patterns; the
[test project](../../tests/Terrum.CodeAnalysis.Tests/Terrum.CodeAnalysis.Tests.csproj)
currently includes these files without project-local exclusions.

| Area | Positive, negative and boundary obligations |
|---|---|
| Source/API | Actual local public primary class exports and schema-only catalog; independent optional roles; all options-bearing factories and retained reference contexts. Reject fictional providers, wrong tree/span/primary/kind, duplicate using Modules, missing required roles and invalid unused selections. |
| Exact identity | Explicit non-name-derived keys; differently cased/normalized keys remain distinct. Reject missing/nonpublic/proxy exports, two roles sharing a definition, alias splitting, mismatched revisions, original expectations or IException/self witnesses; same-spelled decoys grant nothing. |
| Local snapshots | Multiple trees/stdin, unmapped inputs, UTF-8 signature, CRLF, supplementary text and interior U+FEFF. Reject changed count/order/digest, partial/overlapping spans, zero length, bad ordinal, numeric overflow and reuse of a resolved declaration association across binds. |
| Schema/default/construction | Check the complete schema and primary/accessor placement; preserve explicit-type/default/fallback priority, fixed ensure role, ordinary zero-supplied ranking, actual creation failures and originating owner. Unsupported complete binding refuses rather than merely copying strings. |
| CLI grammar/composition | Every eligible variant and option order; valid whitespace-only identities/keys, strict JSON/Unicode/integer boundaries, unknown/version/duplicate properties, empty/all-null roles and missing files. Compatible manifests share context; authority/Module/origin/export conflicts and file/hardlink aliases reject atomically. |
| Frontend | All three wrappers deliver exactly one subject selection to the checked child for file/stdin and both policies. Test changed configuration before/after child capture, internal-pair validation, source labels, failure propagation and no component/configuration fallback. |
| Checked exceptions | Compare direct and frontend routes. Preserve scoped checked-export no-failure/native containment refusal and prior outputs. A dynamic statement requirement is not repaired by adding a declaration-role manifest; source binding, emission and supervised runtime evidence stay distinct. |
| Unsupported routes/output | Syntax/component/artifact/private routes reject the option; external selectors without actual references reject even unused. Unsupported TRRM context publication preserves prior bytes. Test output/ABI preservation on each prepublication failure, and no program start on refusal. |

No production authority, Module, revision, namespace, export key, runtime ABI,
registry ID or schema alias is selected. Review/adoption and the prerequisite's
semantic decisions remain open; the transport choice above is concrete enough
for provisional implementation without declaring those gates completed.
