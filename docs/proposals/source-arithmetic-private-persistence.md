# Private persistence of resolved arithmetic operations

**Status: unapproved; explicitly provisional implementation direction.**
The authorized missing-contract workflow permits using this recommendation
provisionally. It does not approve the candidate private revision or field
values, amend a normative owner, or establish implementation support.

## Decision and scope

There is a genuine private codec gap: structured semantic modules have no
assigned field or node form for resolved wrapping arithmetic. Recommend one
per-operation mode byte in **unapproved candidate semantic-module revision
89**, shared by standalone modules and their native-container embeddings.
Reuse the development executable profile's existing explicit arithmetic
encodings; it needs implementation integration, not another wire assignment.

This fills only the unassigned private-persistence choice in
[source arithmetic profile selection](source-arithmetic-profile-selection.md#resolution-persistence-and-separate-compilation).
That proposal remains the prerequisite for source selection and resolution.
Preserve the accepted [arithmetic policies](../specs/il-instruction-set.md#integer-source-profiles-and-explicit-il-semantics),
[binding obligations](../specs/representation/binding-and-replacement.md)
and [persistence taxonomy](../specs/representation/persistence.md).
No normative text is superseded. There is no ambient source-profile field,
API/CLI change, runtime-policy change, new standard Body control, registry
allocation, Feature assignment or source syntax in this recommendation.

## Evidence and affected carriers

These are source observations, not new execution evidence.

| Path and implementation owner | Retained representation and consequence |
|---|---|
| Private `.trm`: [`ModuleCompiler.cs`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.cs), [`FixedLayout`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.FixedLayout.cs), [`TokenSpans`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.TokenSpans.cs) | `TRRM` magic followed by a private little-endian `i32` revision; the reader explicitly admits revisions 76–88 with revision-specific layouts. `Module.Functions[].Function.Body` retains structured operations. `PutExpression`/`GetExpression` encode unary/binary/update/compound nodes with operator tokens but no overflow policy. The resolved `OverflowBehavior` fact on the corresponding [`bound nodes`](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs) is not a persisted carrier. |
| Semantic linking, sets and graphs: [`ModuleCompiler.ModuleSets.cs`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.ModuleSets.cs), [`ModuleCompiler.Graph.cs`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.Graph.cs) | Use that same structured codec. Producers retain selected implementation bodies and their required helpers; consumers retain their own bodies and imported declarations, not copies of provider bodies. Graph libraries retain local bodies plus dependency declarations. Remapping and source-free restoration must preserve each body's operation policy. |
| `.trmn`: [`NativeModuleCompiler.cs`](../../sources/Terrum.CodeAnalysis/Modules/NativeModuleCompiler.cs) | `TNPK` version 2 stores `Contract` bytes produced by `ModuleCompiler` **and** an already emitted DLL/EXE `Image`. Its semantic payload is body-bearing, not merely a signature table. Ordinary native execution uses the image, not recompilation of those bodies. |
| `.tnset`: [`NativeModuleCompiler.ModuleSets.cs`](../../sources/Terrum.CodeAnalysis/Modules/NativeModuleCompiler.ModuleSets.cs) | `TNSP` version 2 stores the consumer's structured semantic `Contract`, emitted EXE and dependency/import metadata; provider DLLs arrive as separate packages. Its body carrier is again the embedded private module. |
| `.trng`: [`Graph`](../../sources/Terrum.CodeAnalysis/Modules/NativeModuleCompiler.Graph.cs), [`GraphPackages`](../../sources/Terrum.CodeAnalysis/Modules/NativeModuleCompiler.GraphPackages.cs), [`GraphJit`](../../sources/Terrum.CodeAnalysis/Modules/NativeModuleCompiler.GraphJit.cs) | `TNGP` version 2 stores a structured semantic `Contract` and emitted root EXE or intermediate DLL; leaves can be `TNPK` packages. AOT uses the existing images. Graph JIT calls `RestoreConsumerAgainstReference` and re-lowers **only the root**; provider DLLs remain unchanged. Both retained IR and lowered images therefore matter. |
| Development `.trrm`: [`TrRmExecutableCompiler`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.cs) and its [`Body`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.Body.cs), [`Indexed`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.Indexed.cs), [`Outputs`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.Outputs.cs) partials | Explicitly selects `TrRmArtifactSchema.Development`, with `(1, 0)` framing and compact-linear/indexed/ScalarInitOutput bodies. Existing encodings distinguish checked and wrapping arithmetic. Source emitters currently choose checked integer arithmetic; the output-body executable decoder does not handle the wrapping Binary kinds even though its structural reader admits them. |
| Loaded development execution IR: [`TrRmScalarGraph.cs`](../../sources/Terrum.CodeAnalysis/Semantics/TrRmScalarGraph.cs) | `BoundFunction.IndexedBody` holds internal source-free SSA, including `TrRmScalarWrappingBinary`; it is not a serialized schema. Linear decoding also uses this graph when representation operations require it. Private structured serialization and development re-emission currently reject loaded indexed bodies. |
| Standard `.trrm`: [`TrRmRegisterBodyReader.cs`](../../sources/Terrum.CodeAnalysis/Modules/TrRmRegisterBodyReader.cs) and the [register instruction owner](../specs/il-instruction-set.md) | Separate selected schema with already assigned per-operation arithmetic policies. Neither the development compact encodings nor the new private field is a standard register-Body extension. Standard production/execution migration remains separate. |

The [module](../implementation/modules.md),
[native](../implementation/native.md),
[set](../implementation/module-sets.md) and
[graph](../implementation/native-graph.md) inventories document these private
boundaries. Matching extensions, signatures, native-profile labels or outer
container versions do not select an inner semantic schema.

## Candidate structured schema

All additions and the revision/value assignments in this section are
**unapproved private candidates**.

### Operation records

Keep the existing expression envelope: tag, private type identifier
and source span. Insert exactly one `u8 overflowMode` immediately after the
existing little-endian `i32` operator token in these four payloads:

| Existing tag and bound carrier | Proposed payload after the unchanged envelope |
|---|---|
| `3`, `BoundUnary` | `operator:i32, overflowMode:u8, operand:Expression` |
| `4`, `BoundBinary` | `operator:i32, overflowMode:u8, left:Expression, right:Expression` |
| `24`, `BoundUpdate` | `operator:i32, overflowMode:u8, isPrefix:existing Boolean, place:Expression` |
| `26`, `BoundCompoundAssignment` | `operator:i32, overflowMode:u8, place:Expression, right:Expression` |

Candidate `overflowMode = 0` means **checked**; `1` means **wrapping modulo
the operation's exact integer width**. No unresolved/default/profile-relative
value exists. Map these values explicitly to
`BoundIntegerOverflowBehavior.Checked`/`Wrapping`; host enum ordinals do not
independently assign bytes.

Both values are valid for admitted integer negation, integer add/subtract/
multiply, integer increment/decrement, and integer add/subtract/multiply
assignment in their corresponding records. All other uses of these records
must encode zero, which is an inactive placeholder there: floating arithmetic,
unary plus, Boolean/bitwise/comparison operations, division/remainder and
shifts keep their existing semantics. In particular, wrapping is invalid on
a floating or code-unit operation, or on a division, shift or conversion.
Existing operator/type support limits still apply; this byte admits no new
scalar types or places. `BoundCast`, calls and nominal updates gain no field;
called operator bodies retain their own resolved operations.

Producers reject undefined modes and invalid mode/operator/type combinations,
rather than dropping the fact. Consumers reject malformed, unsupported or
inapplicable mode values before affected use; they never coerce them to zero.
Source spans/debug information are not an alternate semantic carrier.

### Exact revision and legacy behavior

Recommend one canonical writer schema: candidate revision **89** is exactly
the current revision-88 layout plus one mode byte in each occurrence of the
four node forms above, with existing absence/count fields representing unused
capabilities. All new writes use 89,
including entirely checked programs. Existing feature/layout selection must
not downgrade such output to 76–88; update the explicit revision gates in the
main codec, fixed-layout and span partials consistently without broadening
their admission policies.

For this candidate, retain the already supported exact 76–88 readers as
checked-only readers for these four old record forms. They consume **no mode
byte** and reconstruct `Checked`, regardless of any consuming source
compilation. This deliberately preserves existing private inputs; it is not
an inferred draft-compatibility obligation or a promise to retain those
readers indefinitely. No additional historical schema or migration is needed.
Do not emit wrapping nodes under an old revision.

Dispatch only supported exact private revisions. An old reader rejects 89;
the new reader rejects an unknown revision, including a numerically higher
one. Failed 89 decoding must not retry a legacy layout. Rewriting a header is
not conversion. Exchange requires agreement on the complete private schema,
not its revision number alone. The [format-versioning owner](../specs/representation/format-versioning.md)
requires exact draft agreement and creates no automatic compatibility
obligation; choosing a distinct private revision here is a fail-closed guard
for existing readers, not a rule requiring every unpublished change to bump.

The outer `TNPK`/`TNSP`/`TNGP` version stays **2**: their framing and image
contracts do not change. Their writers embed the selected module bytes and
recompute existing lengths/checksums. Their readers must admit the embedded
revision independently before binding or loading affected code. An unknown
inner revision rejects even under a recognized outer version; no image-only
fallback is authorized. Assign no new `native-N` label for source arithmetic.
The producer must lower its image from the same resolved operation facts;
consumers need not re-prove arbitrary machine code against retained bodies.

### Alternative considered

Four wrapping-only expression tags are materially plausible: private tag
space is available, and checked records could avoid the extra byte. They
would duplicate tag dispatch and payload handling for the same four logical
operations, while still requiring exact schema selection. Recommend the mode
field to keep one record family and one canonical writer. This alternative
assigns no tag numbers and is not a second admitted encoding.

## Development executable profile: reuse, not allocation

The following are **existing development encodings**, not new candidates or
standard assignments:

| Resolved operation | Development encoding |
|---|---|
| Checked integer add/subtract/multiply | `0x15`, kind `1`/`2`/`3` |
| Wrapping integer add/subtract/multiply | `0x05` Binary, kind `10`/`11`/`12` |
| Checked signed integer negate | `0x17` |
| Wrapping integer negate | Materialize same-type zero and use wrapping subtract, `0x05` kind `11` |
| Checked unsigned integer negate | Materialize same-type zero and use checked subtract, `0x15` kind `2` |

Apply these mappings in linear, indexed and ScalarInitOutput production and
reconstruction wherever their existing structural profiles admit the
operation. Preserve the exact operand type and evaluate negation's operand
once. No floating negation is rewritten as zero-minus-value.
Integer updates/compounds lower to the corresponding resolved operation;
their place/RHS order, prefix/postfix result and successful-store timing stay
unchanged. Unsupported place/control forms still decline.

Use the shared bound `OverflowBehavior` fact for reconstructed arithmetic.
Migrate `DecodeBody`, `DecodeOutputBody`, graph verification and lowering
together; any retained `TrRmScalarWrappingBinary` is an explicitly wrapping
internal adapter, never a checked `BoundBinary` by default. In particular,
handling output Binary kinds 10–12 is an executable-consumer change, not a
structural-reader or Feature assignment.

These mappings change no existing wire meaning, development Feature revision
or `(1, 0)` pair. Development selection remains explicit and separate from
standard register Bodies; equal version bytes are not decoder permission.
Until an affected producer/consumer pair preserves the fact, reject it.
Keep the existing loaded-indexed serialization/re-emission rejection unless
a complete lossless path is implemented; do not disguise a graph as structured
IR or try the standard decoder as fallback.

## Preservation, binding and acceptance

Shared traversal/rewriting, `ModuleCompiler` remapping/resolution, dependency
set/graph type and function remapping, and specialization must carry the mode
unchanged. Record copies preserve it; fresh arithmetic nodes in
`IndexedBuilder` and other adapters must explicitly copy it. Internal extent
and bounds arithmetic does not inherit wrapping from nearby source code.
Re-encoding rebuilds affected lengths, value identities, references, maps,
operation offsets and source maps; neither token nor version substitution is
a complete transformation.

No source-profile label enters import-contract equality or replacement.
Retain the existing runtime-policy, identity, layout, ownership and published
value/failure/effect obligations. A different producer profile is not itself
incompatible, nor permission to weaken a retained guarantee. Source-free
semantic linking and root graph JIT consume definition-site operation facts;
ordinary native execution keeps the already lowered image semantics.

Frozen constants retain their typed values. This adds no exported ConstEval,
open-generic or target-deferred execution schema. The existing
[`ConstEvalModuleTests`](../../tests/Terrum.CodeAnalysis.Tests/Semantics/ConstEvalModuleTests.cs)
distinguish retained private helper execution from unsupported imported
required-constant calls. Any subsequently admitted executable/deferred body
must retain the same resolved facts and dependencies; absent carriers decline
that use rather than rebind defaults or substitute runtime execution.

Implementation acceptance requires the following focused cases, not merely
agreement between a writer and its own reader:

| Area and existing test home | Required evidence |
|---|---|
| Private records — [`ModuleTests`](../../tests/Terrum.CodeAnalysis.Tests/Modules/ModuleTests.cs) | Independent 89 byte fixtures and round trips for all four tags and both modes; check inactive zero fields and unchanged type/span/operand ordering. Reject unknown mode, inapplicable wrapping, truncated payloads, unknown revisions and legacy-layout retry. Exact 76–88 fixtures remain checked under a Release consumer; new writes always use 89. |
| Arithmetic and rewriting — semantic/module fixtures, [`BoundTreeTraversal`](../../sources/Terrum.CodeAnalysis/Semantics/BoundTreeTraversal.cs) | Use runtime operands so folding cannot hide operations. Cover admitted signed/unsigned 32-/64-bit add/subtract/multiply boundaries, signed-minimum negation, unsigned zero/nonzero negation, prefix/postfix and compounds. Observe modular results versus checked failures, once-only evaluation and no failed replacement store after persistence/remapping. |
| Separate compilation/replacement — semantic module and native fixtures | Development producer with Release consumer and the reverse, under a common supported runtime policy; include producer, private helper and consumer-local arithmetic. Repeat source-free linking and compatible replacement without changing consumer bytes. Preserve promised value/failure/constant contracts rather than comparing profile labels. |
| Embedded bodies/images — [`NativeModuleTests`](../../tests/Terrum.CodeAnalysis.Tests/Native/Modules/NativeModuleTests.cs), [`NativeModuleSetsTests`](../../tests/Terrum.CodeAnalysis.Tests/Native/Modules/NativeModuleSetsTests.cs), [`NativeGraphJitTests`](../../tests/Terrum.CodeAnalysis.Tests/Native/Graphs/NativeGraphJitTests.cs) | Check 89 payloads in version-2 packages/sets/graph roots and intermediates; exercise native AOT and root-only graph JIT with both source profiles and both supported runtime policies. Compatible leaf replacement leaves consumer/root/intermediate bytes unchanged; graph JIT does not rewrite selected provider DLLs. Recompute outer checksums when corrupting inner revisions/modes so rejection tests reach semantic admission before image load. |
| Development execution — [`TrRmExecutionAuditTests`](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmExecutionAuditTests.cs), [`TrRmIndexedExecutionAuditTests`](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmIndexedExecutionAuditTests.cs), [`TrRmScalarInitOutputBodyReaderTests`](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmScalarInitOutputBodyReaderTests.cs) | Retain independent checked/wrapping byte cases; add profile-selected production/loading and executable output-body cases, including negation expansion and indexed updates. Exercise linear/indexed/output map and width changes. Preserve fail-fast-only admission and existing unsupported graph re-emission; structural acceptance alone is not execution evidence. |
| Unaffected contracts and unsupported work | Keep division/remainder, shift-count, conversion, literal, floating and extent semantics unchanged in both profiles. Check frozen modular constants and continued rejection of unsupported imported constant/generic/deferred bodies. Standard register output must use its already assigned controls, never these private tags or mode fields. |

Run only the relevant checks after implementation, using existing supervised
execution for failure cases. Documentation of this candidate is not compiler
qualification; implementation inventories should change only with the
corresponding implementation and evidence.
