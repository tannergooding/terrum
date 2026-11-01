# Unit-valued conditional expressions

**Status: unapproved; provisionally actionable only under the user's explicit
missing-contract instruction.** This is a source-contract recommendation,
not normative adoption, Body-profile admission or runtime evidence.
[Proposal index](README.md).

The governing instruction is: “At a genuine missing contract, prepare the
smallest concrete decision needed as a new proposal doc using gpt-6.1-sol at
max reasoning, then continue with it being treated as a provisional
approval/direction.” That provisional direction does not make this proposal
an accepted specification.

## Finding and recommendation

Recommend admitting `condition ? whenTrue : whenFalse` when both arms have
exact `void` as their normally produced result type. Preserve the existing
primitive-Boolean condition, contextual/common-result checking, selected-arm
evaluation, failure, cleanup and initialization rules. Normal completion
contributes one `()`, with no payload.

The [specification index](../specs/README.md) reaches these owners:

| Evidence | Contract boundary |
|---|---|
| [Grammar §3.4](../specs/grammar-spec.md#34-expressions-and-precedence), production `ConditionalExpression = OperatorExpression, [ "?", Expression, ":", Expression ]` | Both arms are expressions. The production and its accompanying constraints contain no non-`void` restriction; syntax alone does not settle an operation's admitted result types. |
| [Unit values](../specs/language/expressions-and-statements.md#the-zero-data-void-value) and [result use](../specs/language/expressions-and-statements.md#using-results-and-explicit-discard) | `void` is inhabited, Copy and has exactly `()`. Ordinary value/result positions admit it; unit expression statements and expression-bodied unit results are meaningful. There is no implicit discard/conversion from arbitrary T to `void`. |
| [Evaluation](../specs/language/expressions-and-statements.md#evaluation-and-expression-boundaries) and [Boolean composition](../specs/language/initialization-and-flow.md#boolean-and-pattern-composition) | A conditional evaluates only its selected branch and joins facts from possible reached edges. Neither passage excludes a unit result. |
| [Authority](../specs/overview/conformance.md#normative-status-and-authority) | Compiler behavior and implementation inventories do not define or narrow normative contracts. |
| [Binder](../../sources/Terrum.CodeAnalysis/Semantics/Binder.cs), `SyntaxKind.ConditionalExpression`; [verifier](../../sources/Terrum.CodeAnalysis/Semantics/SemanticVerifier.cs), `BoundConditional` validation | The earlier source implementation rejected `resultType == PrimitiveType.Void` with TRM3007 and the verifier rejected `conditional.Type == PrimitiveType.Void`. Provisional source support now admits exact Unit/Unit arms in return and expression-statement contexts and exercises both selected arms through native JIT. Explicit/inferred Unit local declarations remain a separate source gap; none of this approves the proposal or establishes A2EU Body/runtime support. |

This is a **genuine missing explicit applicability rule**, not evidence of an
intentional accepted non-`void` contract. First-class unit does not make every
operator applicable to unit, so do not infer unrestricted operator admission;
equally, do not elevate the Binder's exclusion into normative authority.
The existing value and evaluation rules favor this narrow admission.

An explicit non-unit-only rule would also settle the gap, but would split
ordinary unit value composition without an identified type, lifetime or
representation need. Requiring an `if` workaround does not supply an
expression result in an initializer, argument or callable result position.
The exact-unit amendment needs neither a broader conditional type-join design
nor a new effect-only expression category.

## Candidate normative amendment

**Unapproved insertion:** append the following conditional-specific paragraph
to [“The zero-data `void` value”](../specs/language/expressions-and-statements.md#the-zero-data-void-value)
in `docs\specs\language\expressions-and-statements.md`, before “Using results
and explicit discard”:

> Under the ordinary condition and contextual/result-type checking rules, a
> conditional expression whose two arms each have exact complete result type
> `void` on normal completion is a `void` value expression. Its condition must
> produce a checked primitive `bool`. Evaluate the condition once, then
> evaluate only the true arm for true or only the false arm for false, once
> on that reached path. Normal completion of the selected arm produces the
> conditional's one value `()`, without payload. Failure or non-completion
> of the condition or selected arm produces no normal conditional result.
> Both arms remain subject to ordinary static validity, initialization,
> access, caller-unsafety, effect and lifetime checking. Unit's lack of
> payload permits no elision of either arm's calls or checks; their execution
> remains conditional. Preserve reached effects, failure propagation,
> applicable cleanup and edge-dependent state under the ordinary contracts.
> There is no implicit conversion or discard of a non-`void` arm to obtain
> this result.

In [grammar §3.4](../specs/grammar-spec.md#34-expressions-and-precedence),
cross-link this rule from the semantic commentary for `ConditionalExpression`;
**leave that production, precedence and associativity unchanged**. Preserve
the existing selected-branch clause in
[initialization and flow](../specs/language/initialization-and-flow.md#boolean-and-pattern-composition).
No accepted non-unit clause is being superseded.

The future implementation projection replaces only the Binder's
`resultType == PrimitiveType.Void` rejection and the verifier's
`conditional.Type == PrimitiveType.Void` rejection. Retain the known/expected
result requirement, exact arm/result agreement, primitive-bool validation
and checking of unreached arms. Update the TRM3007 wording to remove the
non-`void` condition, not its remaining mismatch diagnostics.

Unit introduces no cleanup payload of its own. Condition/arm arguments,
temporary acquisitions and dependencies still have their ordinary scopes,
retirement and failure obligations; the unselected arm acquires nothing.
Observation-equivalent optimization remains governed by the existing rules,
not by a zero-payload shortcut.

## Fixed-array capture and Body consequences

The unapproved [fixed-array source candidate](standard-native-fixed-array-source-contract.md#proposed-initialization-and-publication)
and [A2EU unit-initialization candidate](standard-trrm-unit-fixed-array-initialization.md#evaluation-failure-and-publication)
remain independent prerequisites for that construction route. A normally
completed unit conditional counts as **one** of the exact N initializer
expressions, not zero expressions or one expression per arm.

At that source position, complete the condition and selected arm's evaluation
and checks before the next initializer. Keep cleanup at its existing
expression/capture/scope boundary, not an invented earlier branch join.
A condition failure executes neither arm; a selected-arm failure executes no
remaining part of that arm's normal continuation. Either failure prevents all later
initializer calls and all final-destination element stores/publication for
that attempted array construction. Earlier external effects are not rolled
back; settle actual private acquisitions under ordinary failure/unwind.
For replacement, failed RHS capture leaves the old destination unretired.
Previously completed private children retain their own cleanup obligations.

**Representation recommendation, not a support claim:** reuse assigned
Standard [conditional control flow](../specs/il-instruction-set.md#control-flow-instructions-and-large-operands),
[cross-block state](../specs/il-instruction-set.md#cross-block-values-and-phi-equivalent-merges)
and [CallSite semantics](../specs/il-instruction-set.md#typed-operand-records)
where the exact selected schema and execution profile admit the required
operations. Branch on the once-evaluated Boolean, retain each arm's reached
calls/argument staging/checks/failure edges, and join only normally completed
edges. Selecting already evaluated alternatives is not permission to execute
both arms. An exact unit CallSite uses the existing None result selector;
only normal return supplies logical `()`. The target's checked failure/
no-return contract, not None alone, determines continuation.

The unit result needs no payload phi or fabricated register/Home value.
All other incoming types, initialization, loans, origins and cleanup
responsibilities still need their ordinary joins. After **all N captures**
complete, A2EU's existing exact typed R14 `()` and ordinary unit stores can
install those logical values. R14 is not a predecessor binding, proof of
completion, or permission to initialize early; None is not a unit store source.

The source-level both-arm behavior is exercised by
[`UnitConditionalExecutesOnlyItsSelectedArm`](../../tests/Terrum.CodeAnalysis.Tests/Semantics/ConditionalExpressionTests.cs).
Explicit and inferred Unit locals are now implemented across hosted and
standalone source paths, including vacant/live state, reads, assignments,
lifetime ends and Unit-call results. The focused API/CLI/native-PE/JIT
selection passed both runtime policies; its result is recorded in
[`cli-protocol.md`](../implementation/cli-protocol.md). This does not establish
A2EU Body/runtime support, which remains a separate gate.

Current [array Body production](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.NativeAddress.cs),
including `IsSupportedNativeFixedArrayInitialization`, now provisionally
admits nested Unit-valued `BoundConditional` initializers. It emits
multi-block `E4`/`ConditionalEdge` branches with `E3` joins using assigned
records, and performs the Unit stores only after the selected-arm effects
complete. Condition support now includes Boolean Home reads, direct Boolean
NativeImport results, constant selection, logical-not, short-circuit `&&`/`||`,
nested Boolean conditional expressions, and non-Boolean scalar Home-read
comparisons (`==`, `!=`, `<`, `<=`, `>`, `>=`) using assigned `0x40`/`0x41`
instructions. Boolean Home-read equality/inequality also lower through
assigned `0x32` XOR and `0x33` NOT. Comparison operands may be Home reads or
admitted direct NativeImport calls; a left Home read is snapshotted before a
right-side call, and nested checked argument failure prevents the branch. The
focused A2EU producer/reader/JIT/AOT selection passed 60/60 with zero skips at
`artifacts\\tst\\Debug\\a2eu-comparison-call-operands-final2\\a2eu-comparison-call-operands-final2.trx`.
Boolean literal operands, non-source predicate masks, and call forms outside
the direct NativeImport capture profile remain outside this Body profile. The
corresponding
[source-free reader](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.NativeAddressReader.cs)
reconstructs structured `BoundIf` control flow and canonically re-emits the
artifact. The active Body regression covers nested arms, checked
int32/uint32 division/remainder captures, a checked fixed-array-address
argument, and rejection of an invalid condition register. This establishes
Body encoding/reconstruction and selected-provider Body-row remapping,
including compatible and incompatible replacement checks. Separate
supervised source-free JIT/AOT cases exercise Boolean and scalar-comparison
conditions under FailFast, including selected comparison-call failure;
CheckedExceptions and general cleanup/failure transport remain open. These are
source/test observations, not checks run by proposal authoring.

Allocate no conditional record, opcode, wire ID, Feature or schema successor.
Keep A2EU's exact schema and unit-store meaning unchanged; do not infer
decoder support from the presence of Standard branches. Required failure/
cleanup transport remains independently gated. Unsupported affected Bodies
must decline use rather than omit an arm, check, failure edge or cleanup.

## Concrete source cases

These are **candidate source conformance cases**, not runnable-support claims.
The fragments assume live `bool` inputs `flag` and `other`; ordinary
`CheckCondition()` returns `bool`, while `Left()`, `Right()` and `Later()`
return exact `void` and may have independently admitted effects/failures.
`Raw()` is an independently declared caller-unsafe unit callable; no foreign
attribute, provider or ABI is supplied by these names.

| Fragment | Candidate outcome |
|---|---|
| `void token = flag ? () : ();` | Accept a contextual exact unit result; initialize one live unit local. |
| `var token = flag ? Left() : Right();` | Accept the known exact `void`/`void` pair without a result context; infer `void`. |
| `flag ? Left() : Right();` | Accept a genuine unit expression statement, not an implicit discard. |
| `public void Pick(bool flag) => flag ? Left() : Right();` | Accept an expression-bodied exact unit result. |
| `void token = flag ? (other ? Left() : ()) : Right();` | Accept nested unit conditionals with ordinary reached-branch evaluation. |
| `void token = int32(1) ? () : ();` | Reject non-Boolean condition; unit adds no truthiness. |
| `void token = flag ? Left() : int32(1);` | Reject mixed unit/non-unit arms; no implicit discard. |
| `void token = false ? int32(1) : ();` | Reject the ill-typed arm even though it is not executed. |
| `flag ? int32(1) : int32(2);` | Reject the bare non-unit result under the unchanged result-use rule. |
| `init void missing; void token = flag ? missing : ();` | Reject a possible vacant-unit read; zero data is not initialization. |
| `void token = flag ? Raw() : ();` outside an explicit unsafe scope | Reject missing caller-unsafety authorization even though the result is unit. |
| `[void; 2] values = [flag ? Left() : Right()];` | Reject underfill: the conditional supplies one initializer, not two. |

Under the separate fixed-array/A2EU candidates:

```terrum
init [void; 2] values;
values = [CheckCondition() ? Left() : Right(), Later()];
_ = values;
```

On success, the observation order is condition, selected arm, `Later`, two
logical element installations, complete publication. Test both Boolean
outcomes. Condition/selected-arm failure leaves `values` vacant and reaches
neither `Later` nor those stores. N=0 remains the separate empty construction;
positive N never becomes empty because its values have zero payload.

## Minimum integration and acceptance gates

1. **Source and verification:** public compiler-API regressions cover the
   positive/negative cases above, both branch outcomes, contextual and inferred
   unit results, nesting and exact N. Preserve existing scalar conditional
   cases in [ConditionalExpressionTests](../../tests/Terrum.CodeAnalysis.Tests/Semantics/ConditionalExpressionTests.cs).
   Reject malformed bound conditions/arm types; verify branch-specific output,
   initialization and failure facts without inventing unit values from vacancy.
2. **Shared traversal and lowering:** retain the typed `BoundConditional`,
   reached call/check order, source anchors and every argument's loan/backing/
   owner/code-origin facts through ordinary visitors, rewrites and native
   lowering. Exercise unrelated synthetic callables, not sample recognizers.
   Unit result erasure must not erase condition/arm computation or cleanup.
3. **Standard Body and source-free reconstruction:** produce and consume a
   bounded conditional-capture CFG using already assigned branch/CallSite/
   state semantics and the exact A2EU unit stores. Reconstruct the conditional
   and its normal/failure continuation without source or private persistence.
   Round-trip/re-emit both arms and nested checked call/address arguments.
   Reject malformed branch/record/map references, wrong condition/arm types,
   illegal entry state and unsupported failure/cleanup transport. Producers
   owe truthful evaluation/publication facts; consumers perform the required
   [safe-consumption checks](../specs/representation/reader-trust.md), not a
   semantic re-proof of arbitrary provider Bodies.
4. **Providers, remapping and replacement:** independently selected providers
   retain their exact schemas, method/signature/Import contracts and original
   expectations versus actual origins. Remap Block/ConditionalEdge targets,
   record indexes, Home/Type/Method maps, layout/provider references and source
   data together; do not substitute bytes or flatten branches into eager calls.
   Re-encode into the explicitly selected supported schema, then verify/load
   and test compatible and incompatible replacement providers. Retain actual
   stronger facts used by optimizations; no schema sniffing or private fallback.
5. **Runtime qualification:** supervised headless source and source-free
   Standard executions must observe condition/arm/later sentinels exactly once
   on reached paths, no execution of the other arm, and stores/publication only
   after every capture succeeds. Exercise condition and selected-arm failures,
   nested calls, checked division/remainder and address-argument checks;
   verify ordinary acquired-state cleanup and unchanged old-value state on
   failed replacement where independently admitted. Cover both JIT and emitted
   images and the applicable failure policies with explicit compiler/configuration
   identity, deadlines and actual exit results. Emission, reconstruction and
   existing single-block tests alone are not runtime evidence.

The only source decision requested is exact-`void` conditional admission.
Unrelated syntax, mixed no-normal-value/bottom typing, general throw-expression
integration, arbitrary expression graphs, new library/ABI policies and new
wire allocations are out of scope. Approval and each integration gate remain
separate; this document completes none of the compiler/runtime gates.
