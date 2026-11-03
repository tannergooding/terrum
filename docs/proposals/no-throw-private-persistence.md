# Private persistence after NoThrow retirement

**Status: unapproved; provisionally actionable under the authorized
missing-contract workflow.** This recommendation supplies a private schema
decision for [NoThrow retirement](no-throw-retirement-and-proof-boundaries.md),
not approval of that decision, private assignments, compiler support or a
normative amendment. It does not reopen source retirement or introduce a
replacement published failure bound.

## Decision and prerequisites

Recommend **candidate private revision 91** for retirement on the current
revision-89 structured layout, followed by **candidate revision 92** for its
composition with [aggregate-output persistence](aggregate-native-output-private-persistence.md).
Keep that proposal's **revision 90 unchanged**: its exact definition is
revision 89 plus its output changes, not an implicitly NoThrow-free schema.
No implementation of 90 is a prerequisite for 91 or 92.

This sequence lets private NoThrow retirement complete before aggregate-output
persistence, without redefining an already described candidate:

| Private revision | Exact candidate and selection |
|---|---|
| `89` | Current structured writer baseline, including the declaration Boolean and textual NoThrow/MayThrow promises. |
| `90` | The separate, unchanged output-only proposal on 89: normalized declaration collections, removal of legacy output vectors/suffixes, and reservation tag 41. It does not select this retirement. |
| `91` | Exactly 89, minus the declaration Boolean and NoThrow/MayThrow contract tokens specified below. No new output collection or tag 41. Use for the retirement integration. |
| `92` | Exactly 91 plus the complete output-schema delta described for 90; equivalently, exactly candidate 90 minus this document's NoThrow fields/tokens. Use for subsequent aggregate-output integration. |

The source/proof retirement direction is the prerequisite for 91. Revision
92 additionally requires the aggregate-output source and complete private
contract bundle, including its dependencies and admission gates. This
document recommends 92, rather than emitting 90, for that later combined
implementation; it neither edits nor reinterprets the 90 candidate.
These are private module revisions, not standard versions, Features, tags in
another namespace, native-profile numbers or runtime-policy assignments.

Revising 90 explicitly would be a possible draft redesign, but is not this
recommendation. Defining a single later revision on top of 90 would require
the output codec before the retirement prerequisite could complete. Two
stages avoid that dependency reversal. Keeping an ignored Boolean or adding
a general proof-record family instead would retain ambiguity or introduce
an unnecessary carrier; neither is selected.

## Owners and historical baseline

The accepted [persistence taxonomy](../specs/representation/persistence.md),
[exact-schema policy](../specs/representation/format-versioning.md),
[complete binding relation](../specs/representation/binding-and-replacement.md)
and [producer/consumer boundary](../specs/representation/reader-trust.md)
remain unchanged. The source retirement proposal owns the intentional
removal of the general promise and preservation of role-specific obligations.
This proposal selects only its private persistence and reconstruction boundary.

The following source observations record the snapshot used when this proposal
was drafted; they are not current support claims or test/execution evidence.
Current private-module support is tracked in the
[implementation inventory](../implementation/modules.md).

| Implementation owner | Current behavior relevant to the decision |
|---|---|
| [`ModuleCompiler.cs`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.cs), `Write`, `ReadModuleHeader` | In this snapshot, `TRRM` magic was followed by a little-endian **i32** private revision. The final writer selection was `ArithmeticOperationVersion=89`, even without arithmetic. The header admitted **76, 78, 79, 80, 82–89**; 77/81 explicitly rejected. |
| `PutFunction`, `GetFunction`, `Contract`, `TryContractPromises`, `CompatibleContract` | Every stored function has a strict Boolean `IsNoThrow` between `IsOperator` and `DoesNotReturn`. Every contract string appends `NoThrow` or `MayThrow` before its return-continuation marker. Directed comparison preserves an expected NoThrow promise. Bodyless imports retain that promise in their string even without the Boolean. |
| `Declaration`, `CreateReference`, ordinary `Link`; [`ModuleCompiler.ModuleSets.cs`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.ModuleSets.cs) | Declaration projection uses record copying. Linking/restoration imports and remaps provider declarations or bodies; removal from serialization alone would leave in-memory promise propagation. |
| [`ModuleCompiler.Graph.cs`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.Graph.cs), `RecordedImportPromises`, `ValidateGraphImports`, `ReadGraphLibrary` | Set/graph restoration reapplies recorded NoThrow, no-return and caller-unsafe promises to replacement declarations. Graph libraries have a separate read path sharing header/function readers. |
| [`NativeModuleCompiler.cs`](../../sources/Terrum.CodeAnalysis/Modules/NativeModuleCompiler.cs), [`ModuleSets`](../../sources/Terrum.CodeAnalysis/Modules/NativeModuleCompiler.ModuleSets.cs), [`GraphPackages`](../../sources/Terrum.CodeAnalysis/Modules/NativeModuleCompiler.GraphPackages.cs) | `TNPK`, `TNSP`, `TNGP` use outer u8 version **2**, structured semantic `Contract` bytes, stored images and SHA-256 integrity checks. Inner contract admission is independent of outer framing. |
| [`TrRmExecutableCompiler.cs`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.cs), [`TrRmContractTableReader.cs`](../../sources/Terrum.CodeAnalysis/Modules/TrRmContractTableReader.cs), [`TrRmScalarGraph.cs`](../../sources/Terrum.CodeAnalysis/Semantics/TrRmScalarGraph.cs) | The separate development `.trrm` producer still constructs Effects bit 1 and reconstructs `IsNoThrow`; its reader permits mask 7 and couples Cleanup to that bit. Loaded scalar graphs copy a NoThrow declaration context. These are not structured-module fields or a new proof carrier. |

## Exact structured schema

### Function records and contract strings

For **both 91 and 92**, remove exactly the one-byte `IsNoThrow` field from
every `PutFunction`/`GetFunction` record. The affected Boolean sequence is:

```text
IsConstructor, IsPrivate, IsInternal, IsStatic, IsOperator,
DoesNotReturn, RequiresUnsafeCaller, IsCleanup, HasPropertyName
```

Keep each remaining field's existing encoding/order, optional payloads,
source spans, bounds and strict 0/1 Boolean rules. There is **no zero
placeholder**, reserved proof bit, optional trailer or replacement field.
This applies to constructors, accessors, cleanup, native-callable functions,
helpers and exported implementations alike, not just ordinary public methods.

Remove both `|NoThrow` and `|MayThrow` as semantic contract tokens. The
canonical string is the retained signature/role prefix, followed immediately
by exactly one `|DoesNotReturn` or `|MayReturn`, and then optional
`|CallerUnsafe`. All other retained contract dimensions remain independent.
In 91, keep revision 89's legacy output representation and
`NativeVTableOutput:` suffix. In 92, apply 90's complete replacement of those
vectors/suffixes with its sole declaration-owned output collection.

Validate this grammar on **every declaration**, including imports without a
body. Retired, duplicate, misplaced or unknown semantic suffix tokens reject;
do not strip them and compare the remainder. An identifier merely spelled
`NoThrow` is not a retired semantic token. Keep body/declaration consistency
checks against the newly generated complete contract.

Revision 91 assigns no new expression tag. Reject aggregate-output
collections/reservations unsupported by 91, including declaration-only
requirements; do not project them into old output tuples. Revision 92 adopts
the exact collection placement after `ModuleFunction.HasOwnershipTransfer`,
three legacy-vector removals, selector/dependency records and tag-41 payload
from the output proposal. Its output ownership, formation, correlation,
remapping and consumer obligations are unchanged. These changes and NoThrow
retirement are mandatory together in 92, including empty output collections.

Retain revision 89's per-operation checked/wrapping bytes in both candidates.
Neither retirement nor the runtime failure policy changes arithmetic,
requirements, initialization triggers, failure instructions or cleanup order.

### Header selection and old readers

Under the retirement candidate, all new structured writes would select
**91** and its reader would select only exact **91**. Under the
combined-output candidate, all new writes and its reader would select only
exact **92**,
even without outputs. Regenerate 91 artifacts at that second boundary.
This deliberately proposes **no historical reader or automatic converter**:
current use rejects 76–90, and later current-92 use also rejects 91.
There is no unpublished-format compatibility obligation to override this cut.

The current through-89 reader already rejects 91/92 in `ReadModuleHeader`
before reading declarations. An implementation of the unchanged exact
90 candidate must likewise reject unsupported 91/92, not roll forward by
integer comparison. A distinct old tool may keep its exact old facility and
obligations; it is not admission to the retired-promise schema.

Select the private i32 schema **before record decoding**, through both ordinary
`Read` and `ReadGraphLibrary`. Shared magic or an extension cannot select
between it and standard/development `.trrm`'s u16 `(1, 0)` header.
Update the final writer selection, explicit header/structure-role and
producer-validation lists together. Review the fixed-layout, pointer,
function-pointer, vtable, callable-entry, invocation and arithmetic threshold
gates in [`FixedLayout`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.FixedLayout.cs)
and [`TokenSpans`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.TokenSpans.cs)
and the main codec; a higher constant must not accidentally enable an
unsupported form or reinterpret 91 as output-bearing.

Unknown/unsupported revisions reject; failed decoding never retries another
layout. Exact private draft agreement is still required. Replacing a header,
deleting a byte, changing a native-profile label or clearing a contract token
does not convert an old artifact or its dependent machine code.

## Proof facts, producers and restored programs

No analysis certificate is serialized by 91/92. Retain the operations,
control flow, resolved policies, supported role records and binding facts
needed to interpret or analyze the program; do not retain the retired
declaration promise under a new name.

* **May remain locally:** sound facts for an exact operation, path or closed
  region, including a proved total checked operation or contained language
  failures. A role-specific no-escape/nonthrowing obligation remains attached
  to its actual admitted role and full transitive program. Neither fact is
  an independently published ordinary-call NoThrow guarantee.
* **Must retire:** declaration/import/interface expectations, external
  reference projections, generic replacement promises, contract-string
  variance and native-image servicing assumptions based on general NoThrow.
  `MayThrow` also disappears as a published binary dimension; absence of a
  failure bound is conservative, not evidence that a particular call fails
  or cannot fail.
* **Must not be laundered:** the current publicly constructible
  `BoundFunction.IsNoThrow` is documented as a declaration guarantee. Remove
  that meaning from the migrated model; if the member remains during a
  transition, producer APIs reject a true retired claim before normalization,
  even for a constant body. Do not silently clear it or reinterpret it as
  validated proof. Local proof contexts must be separate from declarations
  and cannot survive merely because a record `with` expression copied them.

Source/public-IR producers establish every fact used for verification and
lowering. Persisted consumers still perform mandatory structural, support and
binding checks, not semantic re-proof of arbitrary producer bodies.
Optional audits remain separate. A checksum or decoded Boolean never proves
body truth or foreign behavior.

Projection through `Declaration`/`CreateReference` publishes no local proof.
An ordinary import without an independently supported failure bound is
potentially failing, regardless of a provider's constant current body.
`CompatibleContract` removes only the retired comparison dimension, while
preserving selected identity, exact signature/layout/output/ownership/role
checks, directed no-return and caller-unsafe checks, and the complete retained
contract graph. Reject old tokens before comparison rather than treating
legacy expectations as if their authors never requested NoThrow.
Retained failure observations, selected pure/constant forms and phase
contracts do not disappear; unsupported required facts still reject.

`RecordedImportPromises` restores only supported retained promises; it neither
copies NoThrow from a replacement nor invents it from the old string.
Do not substitute a provider's stronger local proof for the consumer's
recorded expectation. Preserve all remaining declaration and native-binding
facts through ordinary linking, set merging and graph validation.

Reconstruction, symbolic `Resolve`, structure/class remapping and pointer/
function-pointer table merging invalidate transient proof caches unless their
complete subject, body, bindings, policies, target and cleanup dependencies
are preserved. Recompute needed local analysis in the new context; never
key a proof by a remapped function index, equal name or equal layout alone.
Keep diamond-graph provider/type/cleanup origins and every using declaration's
expectation. Revision 92 additionally retains and remaps its output collections
and reservation pointee identities under the output proposal's rules.

Closed optimization may use body evidence within an actually retained closed
unit. It cannot erase a failure edge across an independently replaceable
provider without retaining the precise relied-on dependency. These candidates
assign no new proof-dependency codec: where existing representation cannot
retain such a dependency, omit that optimization or reject the dependent
output/use. An old optimized native caller must be regenerated, not serviced
by editing its metadata.

Cleanup remains a selected role, not a general NoThrow guarantee. Preserve
normal throwing-cleanup propagation, nonsuspension and ownership ordering;
specifically nonthrowing residual/release roles retain their separate gates.
Native-callable entries retain their ABI, caller-unsafety, code lifetime and
failure-containment obligations. Prove the supported boundary subset, use an
independently supported authored containment path, or reject; no implicit
catch thunk, new carrier or outward Terrum unwind is introduced.

### Separate executable reconstruction

Private 91/92 do not select a `.trrm` schema or authorize its development
NoThrow facility. Apply the retirement direction coherently to
`TrRmExecutableCompiler`, its [Body](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.Body.cs)
and [Indexed](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.Indexed.cs)
paths and `TrRmScalarGraph`: stop constructing/reconstructing a general
NoThrow declaration or graph flag from Effects or a callee promise.
Graph-local proof contexts may remain only with the scoped meaning above.
Fail-fast, no-return and the mere presence of a native-callable role do not
turn ordinary operations into universally nonfailing ones.

For the migrated **exact development draft**, emit/read only mask bits
`02`/`04`, omit zero canonically, accept equivalent explicit zero, reject
every mask containing `01`, and remove the development-only blanket
Cleanup/NoThrow coupling. Do not silently drop an incoming public-IR promise
while emitting this mask. This is an explicitly selected development-draft
change under exact agreement, not a private revision selector or a standard
allocation. An old development reader may accept some new bytes under the
unchanged `(1, 0)` header; that is not exact-schema agreement or current
support. The accepted standard mask already has these meanings and does not
change. Unsupported role/body/output representations still reject.

## Native packages, replacement and regeneration

Keep outer versions at **2**; their framing does not change:

| Artifact | Required inner selection and execution boundary |
|---|---|
| `.trm` | Exact current structured schema for producer, consumer and graph-library reads, including reference creation, linking and load-time JIT. |
| `.trmn` / `TNPK` | Admit the embedded producer or consumer Contract independently; validate bindings/policy/image before using its stored DLL/EXE. |
| `.tnset` / `TNSP` | Admit the consumer Contract and every separately selected TNPK provider; preserve dependency ownership and declaration-only requirements. |
| `.trng` / `TNGP` | Admit root and intermediate Contracts and TNPK leaves throughout the closure, including snapshots and imported cleanup/type origins, before affected loading/execution. |

Writers derive retained contracts and images from the same checked semantics,
rebuild string/payload lengths and outer offsets, and recompute SHA-256.
Consumers retain bounds, strict decoding, declarations/import ownership,
selected provider, runtime-policy, target/image and code-lifetime checks.
A valid image/checksum or version-2 header cannot bypass rejected inner input.
No image-only, empty-program, source-rebinding or alternate-decoder fallback
is proposed.

Ordinary native package/set/graph AOT uses its stored image.
[`ExecuteGraphJitWorker`](../../sources/Terrum.CodeAnalysis/Modules/NativeModuleCompiler.GraphJit.cs)
restores and re-lowers only the root; provider DLLs remain precompiled.
Both paths must reject old contracts before affected code use. Root JIT
cannot repair an intermediate/leaf image's stale promise.

Regenerate rejected artifacts and every affected transitive consumer against
the new contract/proof boundary, including images whose optimization or
failure/cleanup lowering relied on the old promise. This recommendation adds
no source-free legacy converter. An unchanged old consumer cannot be made
current by weakening its expectation, preserving a hash, or recompiling only
its provider. After regeneration, ordinary compatible replacement remains
available under all retained contracts without a general NoThrow dimension.

## Acceptance cases

These are future requirements, **not claims of tests run or support**. Use
public compiler APIs and independent byte fixtures; matching writer/reader
round trips alone do not establish the selected layout.

| Existing test home / gate | Required cases |
|---|---|
| [`ModuleTests`](../../tests/Terrum.CodeAnalysis.Tests/Modules/ModuleTests.cs), exact schema | Independent 91 function/import fixtures with no NoThrow Boolean or NoThrow/MayThrow token; strict remaining flags and body/string agreement. Include minimal ordinary modules, constructors, getters, cleanup, native-callable entries, forward calls and unrelated checked/wrapping arithmetic. At the next checkpoint independently test 92's complete composition, including empty output collections. |
| Header and malformed input | Verify old through-89 readers reject 91/92; current-91 rejects 76–90/92/unknown and current-92 rejects 76–91/unknown. Test truncated/old-layout records under misleading headers, malformed/missing/duplicate suffixes, bodyless legacy promises and no decoder retry. Shared `TRRM` magic or relabeling must not select another schema. |
| [`NoThrowModuleTests`](../../tests/Terrum.CodeAnalysis.Tests/Semantics/NoThrowModuleTests.cs), producer/projection | Replace published-promise persistence/strengthening expectations with retirement. A stale true public-IR claim rejects even for a total body; reference projection exposes no local proof. Scoped arithmetic/region proof remains sound and unsupported proof transfer rejects. Test no-return, unsafe, ownership, layout and output mismatches independently so retirement cannot hide them. |
| Semantic/native replacement | A newly regenerated ordinary consumer does not acquire a general guarantee from a constant provider or a more precise provider-local proof. Same retained contracts can select a different body without proof leakage; changed relied-on closed facts invalidate/reprove dependent code. Old semantic/native consumers reject rather than silently normalizing their expectations. |
| [`NativeModuleTests`](../../tests/Terrum.CodeAnalysis.Tests/Native/Modules/NativeModuleTests.cs), [`NativeModuleSetsTests`](../../tests/Terrum.CodeAnalysis.Tests/Native/Modules/NativeModuleSetsTests.cs), graph tests | Cover TNPK producer/consumer, TNSP and TNGP root/intermediate/TNPK leaf, including declaration-only imports and diamonds. Recompute outer checksums on invalid-inner fixtures so rejection reaches semantic admission; assert no affected image-load/invocation side effect. Preserve provider/type/cleanup origins through reordered function/type/pointer tables. |
| [`NativeGraphJitTests`](../../tests/Terrum.CodeAnalysis.Tests/Native/Graphs/NativeGraphJitTests.cs), stored-image distinction | Exercise source-free semantic/root restoration separately from actual stored-image package/set/graph execution. Compatible replacement preserves regenerated consumer bytes; root JIT never regenerates provider images to conceal a mismatch. Use admitted runtime policies and supervised headless execution. |
| [`TrRmExecutableCompilerTests`](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmExecutableCompilerTests.cs), [`TrRmContractTableReaderTests`](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmContractTableReaderTests.cs) | Exact migrated development and standard selection; masks 0/2/4/6 and all retired-bit combinations; Cleanup without bit 1; reconstructed calls/linear/indexed graphs do not inherit old promises. Preserve checked failures, selected arithmetic, unsafe authorization and independent native-role gates. Unsupported output/indexed reserialization remains rejected. |

No further private wire choice is deferred within this recommendation.
Review/approval of the **91-then-92** sequence remains pending; implementing
it and qualifying the final relevant patch are separate work. Missing
independent role/standard codecs remain their own gates, not permission to
restore a general promise. No normative owner, inventory or implementation
is changed by this document.
