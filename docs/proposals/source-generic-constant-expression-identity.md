# Symbolic constant expressions in open generic type terms

> [!IMPORTANT]
> **Unapproved proposal; provisionally actionable under the user-authorized
> missing-contract workflow, pending review.** This document activates no
> source support, metadata schema, registry assignment or persistence revision.

## Decision boundary

Recommend **typed structural recipes while open, canonical values when
closed**, with the bounded operation set below. The missing decision is
open comparison and inference behavior, not whether an internal AST needs
another variant.

| Accepted owner | Settled obligation; remaining boundary |
|---|---|
| [Constant generics](../specs/language/constant-generics.md#canonical-value-identity) | Exact normalized domain and canonical valid value; equal closed values are independent of expression spelling. It does not define equality of unevaluated arithmetic recipes. |
| [Formation and forwarding](../specs/language/constant-generics.md#formation-binding-and-forwarding) | Earlier-only defaults, retained bound operations/dependencies, exact-domain forwarding and no inverse arithmetic inference. |
| [Grammar §3.2](../specs/grammar-spec.md#32-types-parameters-and-arguments) | Grouped arithmetic, shifts and conditionals are already generic-argument syntax; grammar admission alone supplies no symbolic comparison rule. |
| [Complete types](../specs/language/types-and-normalization.md#forward-substitution-and-normalization) | Preserve unresolved constructor terms and perform finite forward substitution. Its structural constructor normal form does not select an arithmetic-expression normalizer. |
| [Calls and inference](../specs/language/calls-and-inference.md#finite-candidate-inference) | Establish terms from finite exact evidence, pin explicit defaults, then substitute forward; no inverse expression solving. |
| [Constant execution](../specs/language/proof-and-constant-evaluation.md#staged-constant-evaluation) | Concrete required requests preserve selected operations, checks, dependencies and resource accounting; no runtime fallback. |

This proposal supplements those owners with a bounded **open-equivalence
rule**. It replaces none of their closed-value, domain, syntax or evaluation
rules. The unapproved [CG1 carrier proposal](standard-trrm-constant-generic-methods.md#bound-constant-expression-templates)
already provides a candidate for transporting bound expressions, but
explicitly adds no inference rule; template sharing is not value equality.

Current [bound type arguments](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs)
have only `Value` and `Formal` variants.
[`TryBindGenericMethodTypeConstantArgument`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.StaticProtocols.cs)
forwards a named formal or requires a concrete evaluated value; its open
source-struct default route also rejects nontrivial dependent expressions.
The shared [substitution/dependency helpers](../../sources/Terrum.CodeAnalysis/Semantics/BoundGenericMethodTypeTerms.cs)
and typed inference likewise carry values/formals, not expression recipes.
These are implementation gaps, not independent evidence of a missing contract.

## Eligible recipes

Bind once in the declaring environment. Every node retains its exact
normalized type, selected operation and ordered operands. Leaves are already
formed valid constants or in-scope constant formals identified by resolved
owner, owner kind and ordinal, with their complete domain. Existing nominal
enum/wrapper value and formal forwarding remains unchanged.

New operational nodes are restricted to the ten accepted fixed-width integer
types and `bool`:

| Form | Admitted operations |
|---|---|
| Integer unary | `+`, `-`, `~` |
| Same-type integer binary | `+`, `-`, `*`, `/`, `%`, `&`, `\|`, `^` |
| Integer shifts | `<<`, `>>`, `>>>`; fixed-integer data/count types under the accepted count rule, including contextual `int32` counts |
| Same-type integer comparison | `==`, `!=`, `<`, `<=`, `>`, `>=`, producing `bool` |
| Boolean operations | `!`, `==`, `!=`, eager `&`, `\|`, `^`, short-circuit `&&`, `\|\|` |
| Conditional | `bool` condition; both arms have the same exact fixed-integer or Boolean type |

Preserve already-selected checked/wrapping policies, operand types and
conditional evaluation; use [primitive semantics](../specs/language/primitive-operators.md),
not host arithmetic or carrier operations on nominal values.
Parentheses add no node. Literal construction and named constant formation
retain their ordinary checks before supplying a leaf.

Open calls, nominal operators/construction, conversions, projections, layout
queries, mutation and other expression forms are outside this initial symbolic
slice. A wholly closed argument still uses the existing broader ConstEval
contract. These exclusions are not new grammar prohibitions or new
constant-formal domains.

## Identity and finite inference

Before closure, compare the pre-optimization typed recipe structurally:
exact domain, node/operation identity and policy, corresponding formal
identities, canonical leaf values and ordered child recipes must agree.
Normalize aliases and grouping, not arithmetic. Do not commute, reassociate,
cancel or eliminate operations, evaluate candidate bodies, or enumerate
formal values. Ordinary operator subtrees remain nodes until the argument
closes; independently required constants already supply frozen leaves.
Ordinary reference/formal remapping preserves meaning;
unrelated same-named formals do not merge.

Structural mismatch means **equality is not established while open**, not
proof of inequality for every substitution. Once closed and successfully
formed, use only canonical domain/value identity, even for different recipes.
Comparison/hashing never executes user equality or formation programs and
never authorizes eliminating a required evaluation occurrence.

A bare unresolved callee constant formal in an injective nominal argument
slot may receive the **entire** eligible caller recipe from exact typed
evidence. Retain its caller-owned dependencies. Do not decompose an operation
node to infer its operands. A pattern containing operations waits for its
callee dependencies to be independently established, then substitutes forward
and compares. Conflicting evidence rejects; `_` cannot select a default.

## Substitution and required formation

Substitute declaring-owner arguments, then corresponding method arguments,
capture-avoidably through domains, leaves and operands. An actual may itself
be an eligible recipe. Preserve bound operation identities and recompute
transitive dependencies; never rebind source names or select another overload.
Inserted actuals stay in the caller environment, including recursive calls;
do not recursively reapply the callee substitution to them.
Generic defaults keep their selected earlier-formal expression and cannot
capture a later, self or out-of-scope formal.

An open recipe is not a canonical value or an executed `ConstantExecution`.
Check its structure, types, domain eligibility and declared guarantees over
the admitted formal domain; do not require all-input evaluation success or
run it with invented inputs. Do not speculatively evaluate an operator subtree
under an unresolved conditional merely because that subtree is closed.

When the complete argument occurrence has its required generic and target
inputs, perform required evaluation using the existing selected-path evaluator
and request envelope. Validate its complete initialized result and exact
domain before interning a closed type or lowering it. Keep a legitimate open
recipe only within its retained formal environment; a use requiring closure
cannot defer missing inputs to program execution.

Malformed/cyclic/out-of-scope terms, unsupported nodes or comparison,
missing required inputs/dependencies, checked failures, invalid results and
resource exhaustion diagnose the affected binding/formation/output.
Never substitute zero, raw carrier bits, a plain formal, a sampled
specialization, an absent default, another overload or runtime evaluation.
Explicit wrapping semantics remain ordinary selected operations, not a
failure fallback.

## Integration and independent gates

A future implementation must preserve the same recipe through constructed
type identity, inferred generic actuals, shared traversal/rewriting,
substitution, dependency collection and mandatory verification. Reuse the
existing evaluator; add no second arithmetic language or generic runtime ABI.

[Logical constant records](../specs/representation/logical-records.md#constant-generic-domains-encoding-pending)
remain logical. Standard `.trrm` TypeSpec constant slots, ConstantSource/
ConstantTemplate programs, generic defaults/actuals and source-free
binding/remapping require a separately selected exact schema; CG1 remains
unapproved and is not activated here. Private `.trm`/`.trmn`, `.tnset` and
`.trng` codecs likewise receive no expression tag or revision from this
proposal. Refuse transport that cannot preserve the retained graph; do not
publish only observed closed instances in place of an open declaration.

## Candidate conformance cases

These are fragments using accepted syntax but proposed open semantics.
Assume `struct Cell<const int32 K> { }` and caller `const int32 N`.
They are not compiler or execution evidence.

| Situation | Required candidate outcome |
|---|---|
| `Cell<(N + 1)>` versus `Cell<((N + 0x1))>` | Same open recipe after grouping and typed literal normalization |
| `Cell<(N + 1)>` versus `Cell<(1 + N)>`; `Cell<(N + 0)>` versus `Cell<N>`; `Cell<(N + (1 + 1))>` versus `Cell<(N + 2)>` | No open-equivalence shortcut; each pair becomes equal after successful closure |
| Callee pattern `Cell<K>` meets caller `Cell<(N + 1)>` | Infer K as the whole caller recipe, not a value for N |
| Pattern `Cell<(K + 1)>` meets `Cell<4>` | Cannot infer K; independently supplied K = 3 permits forward checking |
| Earlier default `const int32 M = N + 1` remains open | Retain its selected expression and transitive earlier dependencies |
| Checked `N + 1` closes with maximum int32 | Required evaluation fails; no default/wrapping retry |
| `false ? 1 / 0 : N` closes with N = 3 | Result 3; the unselected division is not evaluated, but both arms must be structurally/type-valid |
| Open recipes differ in domain, formal owner or operation policy; remapping loses a required fact | Do not merge open terms or fabricate successful binding |

**Alternatives:** closed values/direct formals alone leave the admitted open
expression use unsupported. Algebraic or extensional equivalence adds
unnecessary equivalence machinery and failure/resource-sensitive rewrites.
Opaque helper identity ties comparison to lifting choices rather than the
bound expression. The selected structural rule is the smallest deterministic
open comparison floor; broader symbolic forms and physical transport remain
separate decisions.
