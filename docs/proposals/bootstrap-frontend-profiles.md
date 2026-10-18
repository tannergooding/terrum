# Bootstrap and scalar-frontend profiles

**Status: unapproved support and private-representation recommendations.**
The independently selected code/image publication rule below is not reopened.
The [composition inventory](../implementation/bootstrap-frontend-composition.md)
owns current supported combinations and resource values; the
[Terrum-authored component inventory](../implementation/terrum-code-analysis.md) owns component coverage.
Implemented support does not imply profile approval.

This proposal is the decision owner for bootstrap expressions, locals, helper
chains, count-derived native planning, scalar token/node/function capacities,
four-parameter projection and bounded namespace coverage. Those are interacting
dimensions of two different paths, not a sequence of language amendments.
Persisted invocation, private spans and cleanup proofs retain their separate
owners.

## Integration and alternatives

| Owner | Intended integration |
|---|---|
| [Grammar](../specs/grammar-spec.md#3-grammar-productions), [local declarations](../specs/language/local-declarations.md), [lookup](../specs/language/names-and-binding.md) | Preserve source syntax, declaration identity and binding. Publish a constrained implementation envelope, not a second language grammar. |
| [Primitive operators](../specs/language/primitive-operators.md#integer-arithmetic-and-bit-operations) | Preserve checked intermediate results, truncating division and the distinct remainder rule. No bootstrap arithmetic policy. |
| [Dependencies and views](../specs/language/dependencies-and-views.md) | Preserve backing, staged arguments and retirement through the synchronous token-view path. Resource changes do not grant escape or ownership. |
| [Windows x64 calls](../specs/targets/windows-x64-call-interface.md#3-physical-argument-positions-and-stack) and [deployment](../specs/targets/windows-x64-deployment.md#3-native-image-stack-and-unwind-support) | Apply actual argument, stack, image and unwind obligations. A bounded emitter is not a general linker or ABI implementation. |
| [Private persistence](../specs/representation/persistence.md) and [resource accounting](../specs/representation/resource-accounting.md) | Keep projection tags and capacities implementation-specific; assign no TRRM record, opcode or portable minimum. |

**Recommended bounded integration:** use one checked expression/local policy
across the direct-source routes, one current scalar projection candidate and
explicit resource envelopes. Fully consumed leaf operations may retain their
current encodings or use equivalent checked folding. Preserve ordinary
unchanged-corpus source behavior, not unpublished byte assignments or readers.
Eliminate route distinctions based only on one/two/three locals, and do not
require a final folded return to mention a local merely to select an emitter.
These simplifications are recommendations, not current support claims.

A substantive alternative is a general bound-program handoff and native
backend, with ordinary lookup and lowering replacing source templates.
That is the appropriate longer-term compiler direction, but it needs general
control flow, effects, ownership, target planning and independent verification.
Do not obtain a bootstrap claim by declaring current source recognizers a
permanent language subset. For now, use linear chains and selected leaf
operations as an explicit ABI/emitter corpus, not application policy for the
shared compiler.

## Direct-source bootstrap

This route scans the original input and emits bounded code directly. It does
not consume the separately audited scalar-frontend program.

### Expressions and locals

Recommend one `int32` folding subset for direct `Main`, parameterless leaf
helpers, immutable local initializers and helper-call entry arguments:

- Integer literals and visible earlier immutable locals, balanced nested
  groups, one unary `-` per literal/local/group, and `+`, `-`, `*`, `/`, `%`.
  Multiplicative operators precede additive operators; each binary level
  associates left. Reduce a complete group before its unary negation.
- Every intermediate result must satisfy the ordinary checked `int32`
  operation. Direct signed-minimum literal construction is distinct from
  negating an already typed minimum or a grouped minimum.
  The signed minimum divided by `-1` fails; its remainder by `-1` is **zero**.
  A folder must implement that distinction or explicitly decline the case,
  not diagnose remainder overflow as language semantics.
- Local declarations initialize sequentially; only earlier locals supply
  initializer values. Reject duplicate declarations, self/forward reads,
  mutable locals and additional unsupported statements. Use ordinary name
  lookup: a local hiding the required call target cannot be ignored to find
  a helper farther out.
- Parse with checked, source-sized value/operator and local-name/value
  workspaces. There is no separate enumerated operation, local or grouping
  count. Capacity and representability failures decline the operation before
  code publication.

This is compile-time evaluation, not runtime local storage or arbitrary
expression lowering. Calls inside folded expressions, unary `+`, repeated
prefixes, conversions and other operators remain outside this candidate.
Fully consumed leaf operations may retain their current native encodings or
fold to an immediate return with equivalent behavior. Neither choice creates
compatibility obligations for unpublished bytes or historical readers.
A legacy parser's nonnegative-operand restriction must not constrain the
shared folder's otherwise valid signed arithmetic.

### Helper composition

Retain these distinct bounded cases for review and qualification:

| Case | Candidate coverage |
|---|---|
| Direct leaf | Public parameterless `int32 Main`, expression body or block ending in one return, with zero or more immutable locals |
| Parameterless helper leaf | Internal parameterless `int32` helper with a folded expression or local block; its locals and Main's locals have separate scopes and may reuse workspace only after the first scope's values are no longer needed |
| Parameterized helper leaf | `int32` parameters/result; identity or admitted left-associative addition, subtraction or product over parameters; preserve an already supported operand ordering |
| Linear forwarders | Zero or more internal same-signature forwarders, each calling its predecessor once with every parameter exactly once in declaration order; Main calls the outer helper with folded arguments |

The bounded chain corpus is callee-first. Reject unresolved, skipped,
recursive, signature-mismatched or reordered/omitted/duplicated/modified
forwarding, forwarding locals/blocks, arithmetic around a call result, extra
calls/declarations and branching graphs in this path. Parameterless forwarders
have no arguments. These exclusions describe admission to this emitter, not
invalid Terrum programs.

Emit the actual runtime calls and every nonleaf frame. Constant-folding or
inlining the chain would not establish argument forwarding, stack growth or
unwind correctness. Preserve the separate supported one-helper and no-helper
forms.

### Source namespaces

Recommend accepting either the global namespace or one leading dotted
file-scoped namespace, with the existing declaration corpus through EOF.
Main is the unique public parameterless entry; unqualified helper calls
resolve to that same source namespace. Namespace spelling must not change
code bytes.

Block namespaces, multiple headers, imports, aliases and qualified calls
remain outside this direct-source candidate. Validate the complete header and
duplicate identities; stripping a malformed namespace prefix is not binding.
This changes neither general namespace lookup nor metadata or linking.

### Native planning and publication

Derive argument count, chain depth, offsets and frame sizes from the admitted
source and supplied capacities. Four integer argument registers are not an
arity ceiling. For zero-based argument index `i >= 4`, the outgoing slot is
`[preCallRSP + 0x20 + 8 * (i - 4)]`; the incoming slot is
`[entryRSP + 0x28 + 8 * (i - 4)]`. A forwarding frame must account for its
own allocation before reading incoming slots. Preserve register arguments,
32-byte shadow space and pre-call 16-byte alignment; incoming home space
is not reusable outgoing storage.

Check every count, product, displacement, alignment and narrowing before
writing. Derive signed `rel32` targets, code lengths, `.text` extents,
`.pdata`, aligned unwind data and PE file/image sizes from the whole chain.
The candidate remains a single-text-section, import-free emitter over its
admitted exact code shapes, not a validator for arbitrary machine code.
For a large allocation, probe the complete required page range before the
final decrement and describe that decrement at its actual prolog position.
Each nonleaf needs its own sorted runtime-function entry and accurate unwind
states through prolog, call and epilogue. The prototype's 4-KiB probing and
14-byte parameterless call-frame encoding are implementation choices, not
universal language constants.

**Selected publication rule:** code emission and PE creation publish
independently. Each attempted phase resets its own published length; failure
never publishes a partial length. Successful emission publishes complete valid
code. A later PE refusal publishes no image but does not invalidate that code
or reset its valid length. Only success of both phases is source-to-PE success.
A composed entry must not expose a previous image as a newly successful one.
Do not collapse these outcomes into “both lengths are zero on every failure.”

## Scalar projection and resources

The complete scalar frontend inventories and parses source, then supplies
facts to a hosted source/type/flow audit, mandatory verification and emission.
A structural summary or standalone projection is not complete-program success.

Recommend one coherent candidate envelope:

| Dimension | Candidate |
|---|---|
| Source | 16,384 UTF-16 code units; an implementation envelope, not a file-input maximum |
| Complete-program functions | 32 frames; retire the competing ten-function recommendation |
| Scalar aliases | 12 declarations within the admitted namespace scope |
| Per-function state | 8 total named/hidden locals, 12 statements, 64 expression nodes, 16 expression-recursion levels, 6 structural levels |
| Indexed function token span | Source-bounded; no additional 64-token per-function gate |
| Standalone/complete identity | Current private v87/v88 candidate, selected exactly rather than by source feature |
| Complete frame/program | `4 + 712 + 64 = 780` words; `4 + 32 * 780 + 61 = 25,025` words |
| Frontend scratch/summary | `2 + 32 * 7 + 32 * 64 = 2,274` words |

These are independent limits: larger token windows do not increase nodes,
locals, recursion or function capacity. A constrained invocation can still
offer fewer total tokens. Extending the accepted bounded parser/window
coverage beyond 64 requires explicit adoption of this support delta; it
does not create a general Span limit or widen the separate
[accepted cursor exception](span-backed-token-cursor.md#accepted-bounded-exception).
Pass the validated view synchronously and retain scalar token positions in
parser state, not an escaping borrowed descriptor.

The current fixed-size candidate minimizes changes to the bounded producer and
auditor. An alternative variable-length record layout would remove coupled
fixed banks and packing limits, but needs checked indexes, ownership, output
capacity and a complete decoder/audit design. Prefer the coherent fixed
candidate for this experiment; do not make it normative merely because its
tags already exist. Unpublished private tags may be replaced or reassigned
under exact schema agreement, with no mandatory historical readers or aliases.

### Four-parameter calls

Recommend four scalar parameters/arguments in complete v88 frames, retaining
three in standalone v87. Route a four-parameter function through the complete
path; failure must not fall back to a three-parameter interpretation.
This supersedes the separate v86 encoding recommendation.

The fixed candidate uses body-header words 18–20 for the fourth parameter's
type/name span; all three words are zero when absent. Out/init-ref masks are
four-bit masks bounded by declaration arity. Call roots one through three
remain in `Left`, `Right`, `Low`; call-only `High` has:

| Bits | Candidate meaning |
|---|---|
| 0–2 | Argument count, 0–4 |
| 3–10 | Four two-bit source-ordinal-to-formal mappings |
| 11 | Named-argument marker |
| 12–17 | Fourth child index plus one; zero exactly when absent |
| 18–31 | Reserved zero |

Positional calls have zero mappings. Named mappings must cover each formal
exactly once, with unused entries zero; the named marker is invalid on an
empty call. Every child precedes its call node and is distinct as required by
tree ownership. With 64 nodes, the six-bit fourth-child-plus-one field can
represent every preceding child, including children in the overflow bank.
Do not retain a 32-node validation ceiling for that field. Normalize the
packed representation before binding, while preserving source evaluation
order, destination modes, callee identity and staged backing.

The seven-word summary descriptor packs return/four parameter types at
bits 0/4/8/12/16 of word `+4`, flags at 20, arity at 24 and out mask at 27;
word `+6` carries statement count at 0, local count at 4 and init-ref mask at
8. Unused bits/slots are zero. Summary decoding is structural; independent
source and semantic verification remain required.

### Namespace chains and aliases

Recommend complete-program support for a file-scoped namespace, a single
block, repeated flat blocks of one qualified identity, or a single nested
block chain of at most eight levels. In the chain corpus only the terminal
leaf contains declarations; no siblings or mixed child/declaration bodies.
Allow up to twelve internal nongeneric scalar aliases in that leaf and
resolve their chains under the existing scalar alias rules. Repeated-flat
alias composition and broader namespace combinations remain outside this
bounded candidate.

The accepted lookup contract **already** searches enclosing namespace
prefixes. “Parent aliases are not inherited” is not a competing language
rule: this narrow path can decline a source needing parent lookup, but cannot
bind it differently or claim that the rule is unspecified. Any future general
binding must use ordinary declaration/import scopes and cross-kind collisions,
not flattened same-spelled name tables. Audit every header, brace, source span
and qualified identity independently; the innermost name span alone is not
proof of the namespace chain.

## Separate composition decisions

- [Frontend invocation](scalar-frontend-invocation-profile.md) selects exact
  twelve-buffer identities, total-token ceilings, publication and owned
  audited results. Scanner-only or standalone success cannot satisfy it.
- [Private token spans](token-span-semantic-module.md) supplies persistence
  and mandatory reconstruction of backing/lifetime facts, not public span
  signatures, stored cursors or returned views.
- [Cleanup switch proofs](scalar-frontend-cleanup-switch-transfer.md) must
  establish finite cleanup independently of projection/resource admission.
- [Verifier architecture](self-host-frontend-verification-budget.md) remains
  deferred; increasing capacities does not authorize a budget bypass.

## Acceptance gates

Adopt the direct-source envelope, scalar resources and private encoding
explicitly and independently. Required evidence includes:

1. Expressions/local counts across every admitted route: precedence,
   left-associative intermediate overflow, signed minimum construction versus
   negation, minimum remainder by `-1`, zero divisors, nested group negation,
   sequential lookup and rejected malformed/unsupported expressions.
   Check unchanged-corpus behavior separately from new folding. Byte comparisons
   may detect changes in the current implementation, but are not an unpublished
   format-compatibility gate.
2. Register/stack transitions, nonuniform positional values at every forwarding
   hop, parameterless and parameterized chains, source-fitting maximum depth,
   arity, locals and grouping, and one-more-source-unit refusal. Supplied
   workspace capacity must be checked independently of the source limit.
3. Exact and one-short code/image capacities, late PE rejection with valid
   code retained, rel32/section/file/image/unwind representability boundaries,
   nested page probes and unwind restoration at each frame/PC class.
   Source-fitting stress cases alone do not reach every format bound.
4. Independent token/function/node limits: 64 versus 65 nodes, 32 versus 33
   functions, per-function and aggregate token boundaries, maximum source,
   four-argument calls with a late fourth child, named permutations, output
   modes and malformed/reserved packed fields.
5. Namespace identity across dotted/file/block/chain spellings, same-leaf
   aliases and rejection of unsupported parent/sibling/repeated-alias cases;
   never accept an incorrect alternate binding. Corrupt headers, braces,
   source anchors, call targets and unused words must reject.
6. Both runtime policies; separately identify syntax/binding, source audit,
   verification, emission, execution and persisted-profile evidence. None
   alone establishes an independent compiler bootstrap or feature acceptance.

Current evidence entry points are
[bootstrap component tests](../../tests/Terrum.CodeAnalysis.Tests/Native/Bootstrap/BootstrapInt32MainReturnComponentTests.cs),
[projection auditor](../../sources/Terrum.CodeAnalysis/Lexing/ScalarFrontendHost.cs),
[program auditor](../../sources/Terrum.CodeAnalysis/Lexing/ScalarFrontendHost.Program.cs)
and [persisted complete-program tests](../../tests/Terrum.CodeAnalysis.Tests/Semantics/Frontend/ScalarFrontendInvocationProfileTests.CompleteProgram.cs).
Use the support inventories for coverage; these links are not a claim that
every recommended simplification or boundary case is implemented or qualified.
