# Compiler and tooling implementation

## Current boundary: syntax-43, semantic v43–v45 updates, native-10 through native-20

Syntax-43 enforces maximal contiguous ASCII punctuation runs from
`!%&*+-./:<=>?^|~`. A run must be one implemented spelling; unlisted runs
produce one lossless Bad token and whole-run `TRM0011`. Defined but currently
unsupported `..`, `..^` and `?.` similarly retain one Bad token with
`TRM0012`; this does not implement ranges, slicing or optional access.
Comments, literals, numeric exponents and keyword escapes retain priority.
Grouping/list delimiters, commas and semicolons are outside the glyph set.

Only a deferred `>`/`?` candidate retains existing tokenization. The hosted
parser certifies actual generic closers and optional suffixes it consumes,
including split closers and four-or-more closing characters. Missing/recovery
tokens cannot certify a boundary. Repeated optional modifiers still reject
with precise `TRM1005`, not a replacement run diagnostic. This is not a
general expression allowlist for `>`/`?` runs.

The compiled scanner independently classifies runs, while completed scanner
adapters audit supplied framing without replacing records. Trusted prelexed
caller classifications retain their distinct contract. The native scalar
snapshot linearly checks contiguous punctuation records, including the first
and last tokens, before spacing checks, modern/legacy selection or maintained
probes; it has no type-context exemptions. Historical/current successful
projection audits independently enforce current syntax.

Scanner-8/schema-8 appends diagnostic kinds 11/12 to the previous 1–10
catalog. Historical raw records remain readable; completed current adapters
reject old split-run or fabricated name/literal framing contradictions.
Token IDs, buffer/record widths, projections 1–51/392 words, all resource/work
bounds and semantic/ISA/Contract/native ABI formats remain unchanged.
Native glyph classification uses a guarded ASCII bit mask and publication
reuses equivalent operation predicates/scans to stay within the existing
whole-program verification bound. No unrelated language forms are added.

Syntax-42 adopts the remaining currently supported contextual punctuation
boundaries. Conditional `?`/`:` and struct-conformance/generic-constraint
relationship colons require raw separating trivia on both sides (`TRM1008`).
Loop-label and named-argument colons attach to the preceding name (`TRM1010`);
trivia afterward is optional, so `name:value` remains valid. Optional-type
`?` attaches to the type's final token (`TRM1011`), including split generic
closers; repeated modifiers retain their precise `TRM1005` rejection even
with intervening trivia. Selection is syntactic before lookup, with no
spacing-driven fallback, and recovery/CST retain every source unit.

Hosted, prelexed and compiled-scanner input share these checks. The native
scalar frontend's `?`/`:` are conditional connectors only; its shared raw
snapshot check rejects invalid boundaries before modern/legacy selection
and maintained probes. Independent historical/current successful-projection
audits enforce current source syntax. Interface-base and enum-underlying
lists, tuple names and attribute targets have no current parser surface;
native labels/named arguments/optional types are not introduced. Named arguments
retain their parsed role but still reject in binding with `TRM3011`; this
spacing adoption adds no named-call semantics. String and
format interiors are not reclassified. Scanner-7/schema-7, projections 1–51,
all resource/work bounds, semantic/ISA/Contract/native formats, evaluation
and ABI remain unchanged.

Syntax-41 adopts symbolic expression operator, binding and expression-body
arrow spacing in supported forms. Binary arithmetic, shifts, comparisons,
bitwise/Boolean/coalescing operators, declaration/assignment `=` and supported
compounds, and `=>` require trivia on both sides (`TRM1008`). Symbolic
`+`, `-`, `!`, `~`, `++`, `--` prefixes attach to the immediate following
operand token; postfix updates attach to the preceding final token
(`TRM1009`). Raw spans distinguish all whitespace/newline/comment trivia;
grouping interiors remain permissive. Syntactic role and precedence are
unchanged: declaration operators, keyword operators, `const(expression)`
versus `const (expression)`, optional types and conditional `?:` retain
their existing classification. Invalid spacing never selects another role.

Hosted/prelexed/component parsing shares these checks. The native scalar
frontend validates supported operator boundaries before modern/legacy shape
selection; maintained probes use the same snapshot check. Host successful
projection audits independently require current syntax, including compatible
historical projection shapes. Legacy publication reuses monotone operation
threshold classification to retain the existing whole-program verification
work bound. Scanner-7/schema-7, projections 1–51, token identities, all source/
token/local/statement/node/parameter bounds, semantic/native formats and ABI
are unchanged. This introduces no new constructs, conditional/colon or
optional-suffix spacing, or operator-run lexical validation.

Syntax-40 adopts role-specific dot attachment. Supported qualified type names,
namespace declarations and imports require both raw token boundaries attached.
Expression member access requires at least one side attached, including when
lookup resolves the receiver to a type or namespace. Whitespace, LF/CRLF and
ordinary/documentation/line comments break the corresponding boundary.
`TRM1007` identifies the original dot; CST, trivia and incomplete-input
recovery retain the source. Generic shape selection stays syntactic and does
not fall back after a qualified-dot error. Existing attached generic interiors
may still be multiline. Hosted and current prelexed/component parsing share
these checks before binding.
Qualified attribute names and optional member access are not currently
supported and are not introduced here. Native scalar input has no supported
dot-bearing forms: no namespace/import, member or qualified type constructs
are added. Its existing successful-projection current-syntax audit follows
syntax-40; existing record/source audits still reject forged dot-bearing
shapes that no scalar projection can represent. Scanner-7/schema-7, projections
1–51, resource bounds, semantic/native formats, name identity and ABI remain
unchanged. This migration adds no unrelated syntax.

Syntax-39 adopts receiver/name-attached opening delimiters across supported
calls, indexing, generic applications and type arguments, generic declaration
parameters, callable/constructor/operator parameters and attribute arguments.
The preceding raw token must end exactly where the opener starts; whitespace,
line breaks and comments break that boundary, including after escaped names
and compound receivers. Interior trivia, ordinary grouping, structural
keyword parentheses and external braces retain their existing rules.
Generic-call classification remains syntactic: an otherwise selected generic
application reports `TRM1006` for detached delimiters rather than being
reinterpreted as a relational expression. Lossless recovery retains all text.
The native scalar frontend and maintained scalar probes enforce function
header and supported scalar type-call attachment. Host projection audits
also require current source syntax, including for compatible historical
projection shapes; hostile records cannot authorize detached constructs.
This adds no native general calls, indexing or generics, indexer declarations
or broader attribute syntax. Scanner-7/schema-7, projections 1–51, resource
limits, semantic formats, persisted names and native ABI remain unchanged.
This migration adds no unrelated syntax.

Syntax-38/scanner-7 adopts the approved contiguous `@keyword` identifiers.
The closed ordinal escape catalog is exactly the vocabulary and contextual
table in grammar §2, including future unsupported constructs. Primitive
spellings, `_`, arbitrary names and legacy-only `out`, `foreach`, `abstract`
are not escapable. Their existing unescaped classification is unchanged;
this is not the complete canonical reservation/contextual migration.
Escaping suppresses keyword recognition at that occurrence. Hosted declaration,
lookup, generic/protocol and label routes use the underlying name, as do
native scalar parameter/local identity and reconstructed exports. Tokens,
trivia, CST and diagnostic coordinates retain raw source spelling.
Scanner-7 appends a read-only `EscapeKeywords` host catalog after the existing
nine grants; the frontend output grant consequently has index 10. Native
scanning consults that catalog rather than re-lexing through the host.
Token/diagnostic/trivia/integer widths, frontend projections 1–51 and
16K-source/64-token/eight-local/twelve-record/thirty-two-node/three-parameter
limits are unchanged. Raw historical records remain readable where compatible;
current parser/snapshot adapters reject invalid identifier escapes, and the
current lossless scanner adapter rejects conflicting historical classification.
No semantic, ISA, Contract or native format/ABI revision follows from a
source spelling escape. The wider approved grammar remains a target, not
additional implemented constructs.

Syntax-37 adopts generalized ordinary and documentation comment fences in the
host lexer/CST and executable scanner-6/schema-6. Maximal opening star runs
select the exact whole closing run; documentation closers require two slashes.
Payload is opaque and nonnesting. Overlong runs with the required slash suffix
report `TRM0010` over that run and suffix; recovery keeps them inside the
comment until an exact closer or EOF, which also reports `TRM0003`.
Documentation line/block trivia append kinds 4/5 without changing existing
token IDs or record widths. The adapter validates source-owned trivia and
comment diagnostics without re-lexing native tokens or recomputing magnitudes.
Historical fixed-fence results remain readable as raw records, but conflicting
results cannot be passed off as the current parser input. Compiled historical
implementations and semantic/native artifacts retain their own behavior.
Native frontend projections 1–51, buffers and resource limits are unchanged.
Other approved punctuation/spacing contracts and documentation extraction
remain separate adoption work; no metadata, ISA, Contract or native ABI changes.

The approved [source grammar](grammar-spec.md) is broader than this
implementation. Syntax-28 adds `=>` bodies to supported top-level functions
(including bounded generic templates), instance/static methods, static
operators, constructors and by-value getters. A property may use
`T Name => expression;` or an explicit `get => expression;` accessor.
Value-producing bodies use ordinary checked return binding; void methods
and constructors execute one supported statement, including assignment,
discard or `require`. Existing initialization, ownership, receiver, effect,
rooting and cleanup checks apply unchanged. Getter-local `[NoThrow]` still
requires an explicit accessor; direct property attributes do not acquire a
new target interpretation.

Host and compiled-scanner paths recognize the same `=>` token. Lowering
reuses existing verified operations, so scanner-5/schema-5, feature-selected
semantic module versions and native ABI/profiles remain unchanged.
Persisted semantic/native consumers use the same callable/property identity.
This does not implement conversion declarations, setters, init accessors,
auto-properties, indexers, lambdas, declaration contracts or async producers.

Syntax-29 adds post-tested `do { ... } while (condition);` and lexical loop
labels on `while`, `for` and `do`, with named `break`/`continue`. Transfer
targets must be enclosing loops; transfers unwind exited cleanup and managed
root scopes, and cannot escape a `finally` action. A post-tested `continue`
checks its condition; a `for` continue executes the selected loop's iterator.
Definite assignment includes the first post-tested iteration and all transfer
edges. Required constant evaluation uses the same target/iteration order.
The implemented subset rejects duplicate simultaneously enclosing labels
but permits disjoint reuse; the general source collision policy remains
[a question](proposals/grammar-open-questions.md#q37-loop-label-scope-and-shadowing).
Semantic v39 is selected only for retained post-tested loops or non-nearest
transfers; older headers reject those nodes. Ordinary pre-tested loops and
nearest transfers retain their earlier feature-selected formats. No native
ABI, scanner-5 record or syntax-protocol schema change is introduced.

Syntax-30 adds standalone `init T name;` and `init var name;` vacant locals,
including a `for` initializer. These readonly slots have no default value;
checked `init` completion publishes them, while false or failure leaves them
vacant. Inferred declarations require a type established by a checked
initialization. Existing definite-assignment, owner cleanup and managed-root
rules apply. Semantic v40 is selected only when a retained body contains such
a declaration; older feature-selected modules and the native ABI are unchanged.
The bounded constant evaluator does not support vacant declarations.

Syntax-31 adds right-associative `condition ? whenTrue : whenFalse` expressions
below binary-operator precedence. The Boolean condition runs once; only its
selected arm executes. Both arms must satisfy one non-void result contract,
from the expected type or the same known arm type; one known arm can provide
context for an untyped arm. Numeric promotion, inferred nullable/readonly
joins between different known types and bare-literal fallback are not
introduced. Existing scalar, Copy, unique and
managed result contracts apply, including conditional output publication,
moves, cleanup and exact roots. Inactive arms remain type/effect checked.
Required constant evaluation evaluates only the selected arm, while retaining
its declaration checks. Semantic v41 is selected for retained conditional
nodes; older module versions reject them. Conditional access, throw
expressions and target-typed lambdas remain unsupported.

Syntax-32 adds right-associative `??` below `||` and above `?:`, and
statement-form `??=` for existing managed-handle optionals. The left value or
destination receiver is evaluated once; a present value skips the fallback.
The result uses the expected supported handle contract or the matching
payload/optional contract established by its operands. Existing readonly
projection and optional lifting do not strengthen access or introduce numeric
promotion. A null literal requires handle context from the target or another
operand. Both operands remain checked even when the fallback is inactive.
`??=` requires an already initialized mutable local or mutable source-class
field reached through a writable handle, not a vacant slot, getter result or
general reference place. Its fallback satisfies the optional destination
contract, including null or another optional value. Computed field receivers
remain rooted across fallback calls and checked unwinding. Conditional
publication, cleanup and private semantic/native module servicing retain their
existing rules. Retained coalescing nodes select semantic v42; older headers
reject them. Scalar/unique optionals, conditional access and managed ConstEval
are not added, and the native ABI is unchanged.

Syntax-33 adds prefix/postfix `++` and `--` for initialized writable
`int32`, `uint32`, `int64` and `uint64` places. Supported places include
mutable locals, mutable compact-field paths rooted in a mutable local or
writable source-class handle, scalar fields of a mutable unique root, and
existing writable `int32` host-buffer elements. A computed receiver or index
evaluates once. The old value is saved, the replacement is checked, and only
success commits it; prefix returns the replacement and postfix the saved
value. Failure does not undo earlier receiver/index effects. Managed receivers
remain rooted through the operation and retire their staged root on checked
failure. Updates are valid expression statements and `for` iterators;
scalar-slot updates also execute in bounded ConstEval.
Semantic v43 is selected only for retained update nodes, with old-header
rejection and unchanged semantic/native consumer contracts. `NoThrow` cannot
promise an unproven checked update. Getters, temporaries, readonly/vacant
places, `char16`, nested owning-child paths and nominal static update
operators are outside this primitive-update contract; this does not defer the
approved static operator grammar or authorize updating a unique value itself.

Syntax-35 adds nominal static `++`/`--` calls for the current eligible writable
`Copy`-struct place envelope. The place is evaluated once; postfix preserves
the old value and prefix returns the replacement. The operator may fail
before writeback, without rolling back earlier operand/place effects. For
these updates the exact result type must equal the operand type, unlike other
unary operators that may return a distinct type. Existing bodyless static
operator requirements, self/no-self protocols, constrained generic
definition-site proofs, and nominal witness identity apply. This does not add
C#-style instance mutation, owning-value updates, arbitrary `char16`
arithmetic, new equality/default protocols or a witness ABI. Retained nominal
update calls select semantic v44 (expression tag 25, with a named operator
target) while primitive update nodes retain v43/tag 24. Supported places
include mutable locals, nested writable compact fields, writable class fields
with computed receivers, and eligible Copy fields on initialized unique roots.
The receiver root and saved Copy value survive the operator call; checked
failure retires a staged receiver root without committing a replacement.
The verifier checks exact target identity, signature, access and declared
effects. Private operator bodies and closed specializations are serviced
behind unchanged public entry points in semantic/native consumers; this
does not add cross-module source-operator discovery or public generic witnesses.
Nominal updates and struct construction remain outside the scalar-only
ConstEval interpreter and diagnose as unsupported required constant work.
No native ABI/profile change is required.

Syntax-36 adds primitive binary shifts `<<`, `>>` and `>>>`, plus the
statement-form compound assignments `+=`, `-=`, `*=`, `/=`, `%=`, `&=`, `|=`,
`^=`, `<<=`, `>>=` and `>>>=`. The destination must be an initialized writable
`int32`, `uint32`, `int64` or `uint64` place. Binary shifts take one of those
four integer types as the left operand. Arithmetic and bitwise right-hand
operands have the exact destination type; shift counts are `int32`, using the
existing checked shift-count rules. The destination address and old value are
captured before evaluating the right-hand side exactly once. Checked
computation must succeed before writeback; effects from evaluating the place
or right-hand side are not rolled back on failure. These operations reuse the
existing update-place/root machinery and add no destination-loan form.
Primitive scalar-slot compound assignments are supported in bounded ConstEval.
Nominal compounds are rejected even where a binary operator witness exists;
`char16` and unique-value update destinations are not added. Syntax-36 shift
and compound-assignment nodes select semantic v45, and compound statements
use expression tag 26 within an expression statement. This does not change
the native ABI or profile.

The executable Terrum scalar frontend additionally supports bounded nested
expression trees and expression-bodied scalar functions through provisional
projection v20, plus initialized scalar locals and assignments in v21.
Up to eight typed or inferred locals and twelve ordered declaration/assignment
statements may precede the return envelope used by projections through v47.
Only `mut` locals can be reassigned; names bind sequentially, and inferred
numeric literals have no fallback type. Ordinary calls and vacant locals remain
outside this native component. It retains existing v1–20 projections for their supported
shapes. Its own parser and scalar binder produce up to 32 postorder nodes
with bounded recursive descent; the host validates source anchors, types,
node order, roots and precedence before lowering to the existing verified
native backend. This is real native compiler-component execution, not a
standalone compiler, general language adoption or a native machine-code
encoder. See the [component boundary](../self-host/README.md).

Provisional projections v22 (without locals) and v23 (with locals) add checked
numeric `T(expression)` conversions for `int32`, `uint32`, `int64`, `uint64`
and `char16`. Literals construct in the target type, while already typed
operands keep their source type before conversion. The reader checks the
source spelling, active-name shadowing, literal context and operand type
before using existing `BoundCast` lowering. All 25 source/destination pairs
share the hosted checked semantics; Boolean conversions and ordinary calls
remain outside this native component.
The 392-word buffer, 32-node limit and existing v1–21 shapes are unchanged.

Projections v24/v25 add native target-checked conditional trees without/with
locals; v26/v27 combine conditionals with numeric conversions. All six scalar
types retain exact arm typing, right association and selected-arm execution.
Predicates never establish numeric context for literal-only arms. The
source-bound reader validates the third child, punctuation, precedence,
types and tree ownership independently of the native parser. Existing v1–23
payloads and zero-reserved checks remain unchanged, as do buffer/node/depth
limits. DLL byte parity, JIT/EXE execution under both policies and atomic
failed-output retention cover this bounded compiler component.

Projections v28–31 add pre-tested `while` bodies containing scalar assignments
and nested `while` loops, followed by the existing terminal return envelope.
Declarations remain at outer function scope and may occur between loops;
loop-body declarations, early returns, named
transfers, `break`/`continue` and native update expressions are rejected.
There are at most six nested loop headers, twelve total statement records
(headers included), eight locals and thirty-two expression nodes. Zero-local
empty loops are permitted. Versions 28/29/30/31 respectively select loops
alone, with conversions, with conditional expressions, or with both.
The component checks Boolean conditions before each iteration and preserves
checked failures and body order without a runtime iteration cap. Independent
source validation checks body extents, scope, mutability and structural
limits before shared `BoundWhile` lowering. These versions and v32–39 remain
transfer-free. Existing v1–27 payloads, the
392-word buffer and public ABI/profiles remain unchanged.

Projections v32–35 add braced `if`/optional `else` bodies containing assignments
and nested branches; v36–39 also admit loops nested in either direction.
Within each group, the next version adds conversions, the second adds `?:`,
and the third adds both. Exactly those features must occur in the records.
The six-level nesting bound is shared by branches and loops; else markers
count toward the twelve statement records. Conditions run once per branch,
and only the selected arm executes. Declarations remain outer-only.
Body returns/transfers, unbraced arms, `else if` shorthand and native update
expressions remain unsupported in v32–39. Existing terminal if-return shapes retain
their old projections. The independent reader checks both arm extents,
source delimiters, node ownership and local authority before `BoundIf`
lowering; the fixed buffer and public ABI/profiles do not change.

Projections v40–43 add bare nearest-enclosing-`while` `break` and `continue`
to the existing assignment-only loop bodies, without branches; v44–47 add
the same transfers to braced branch bodies, including loops nested in branches
and branches nested in loops. Within each four-version group, the base version
adds transfers, +1 adds numeric conversions, +2 adds `?:`, and +3 adds both.
Transfer statement kind 6 is `break` and kind 7 is `continue`; each record has
`Slot` equal to the statement-record index of its nearest `while` header,
root `-1`, and the exact keyword-through-semicolon source span. The source
reader independently validates nesting, target, type and source, then lowers
the existing verified transfer nodes. Each projection admits exactly its
listed features and leaves unused extensions zero. Named transfers, loop-local
declarations, early returns in v40–47, general calls and native update expressions
remain unsupported in v40–47. The 392-word buffer, eight locals, twelve statement
records (including transfers, headers and else markers), thirty-two
expression nodes, sixteen expression-recursion levels and six shared
structural nesting levels remain fixed; v1–39 payloads remain unchanged.

Projection v48 adds structured scalar returns to these branch/loop bodies.
Return statement kind 8 has slot 0, the typed result root and the
keyword-through-semicolon source span; header words 38–40 are `-1` because
the return statements, rather than terminal return roots, represent results.
The independent reader validates every root and source construct, then uses
the verifier's continuation rules to reject reachable non-void fallthrough.
At least one return record selects v48; provably non-fallthrough loops retain
the existing verifier treatment rather than requiring every execution to end.
Earlier projections retain their prior terminal-return and terminal-if-return
shapes. The component does not fall back to the hosted parser or synthesize a
terminal return. It adds no body-local declarations, general calls or native
updates. It keeps the 392-word buffer, 64-token input bound, 32 expression
nodes, 12 statement records, eight locals and six structural levels; the
existing 16-level expression-recursion limit also remains. It continues to
lower through the shared verifier/emitter and is not a standalone compiler.

Projection v49 adds the hosted primitive integer shift and mutation envelope
to this executable native component. `<<`, `>>` and `>>>` accept an `int32`
count in the checked range 0–31 or 0–63 for the left operand's width; counts
are not masked, and left shifts retain their truncating bit semantics.
All eleven compound assignments and prefix/postfix `++`/`--` operate on
initialized writable named `int32`, `uint32`, `int64` and `uint64` locals,
including grouped names. Shift counts cannot supply the shifted value's
literal or inferred type. Other compound operands have the destination type.
Update statements retain the hosted direct-update-only restriction: grouping
the place is supported, but grouping the whole update statement is not.
The component emits the existing `BoundUpdate` and `BoundCompoundAssignment`
nodes: the destination's old value is captured before the RHS, evaluation is
once and ordered, failure precedes final writeback without rolling back earlier
effects, and prefix/postfix select the replacement/saved value respectively.
Branches, nested loops, nearest transfers, lazy expressions and returns retain
their existing semantics under both runtime policies.

The unchanged eight-word node record uses operations 86/87 for updates
(`Low` is 1 for prefix, 0 for postfix), 88–90 for shifts and 91–101 for
compounds. Compound statement kind 9 carries the destination slot and root;
update statement kind 10 carries slot 0 and its root. Version 49 is selected
only when a new operation occurs, and supports either the existing terminal
envelope or structured return records. Versions 1–48 reject new operations
and retain their original payloads. The independent source-bound reader checks
place authority, active declarations, grouping, operand context, precedence,
spans, node ownership and statement extents before shared lowering. Buffer,
token/node/statement/local/depth limits, scanner records, token IDs and native
ABI/profiles are unchanged. Calls, fields/nominal mutations, floating values,
vacant locals, parameter mutation and new unchecked semantics are not added.
Independent projections, exact checked statuses, actual DLL exports, hosted
image parity and persisted semantic/native consumers exercise this boundary.

Projection v50 adds initialized typed/inferred scalar declarations inside
existing braced `if`/`else` and `while` bodies. The native parser and independent
source-bound reader track lexical visibility separately from monotonic local
identities. Initializers see only preceding visible declarations; parameters
and enclosing locals cannot be shadowed, but closed sibling scopes may reuse
spelling with distinct slots. Scope exits remove name/write authority without
discarding mutations of outer locals. Shared verified block lowering executes
initializers on each entered iteration and preserves nearest transfers,
structured returns and both runtime policies.
Only nested declarations select v50; versions 1–49 retain their original
shapes and reject the new placement. The six scalar types and existing
mutability rules remain unchanged. Eight locals means eight total declaration
identities across the function, not eight per scope. The 392 words, 64 tokens,
12 statement records, 32 nodes, three parameters, 16 expression-recursion
levels, six structural levels, scanner IDs and public ABI remain fixed.
Projection v50 did not add standalone block statements; projection v52 adds
them below. Vacant or owning locals, general calls, named transfers, richer
values and new persisted semantic/native formats remain unsupported.

Projection v51 adds braced scalar `do` and `for` loops through the existing
verified `BoundWhile` lowering under both runtime policies. `do` executes the
body before its condition; nearest `continue` reaches that condition. `for`
executes its initializer once, tests before each body, and executes its iterator
after normal body completion or nearest `continue`, but not after `break` or
return. Initializers may be empty, initialized scalar declarations, named-local
assignments, or existing integer compounds/updates; iterators admit the same
statement forms except declarations. Omitted conditions mean true. Header
locals span condition/iterator/body and expire after the loop; body locals
cannot appear in header clauses or the trailing `do` condition.

Kind 11 records `do`, with the body-end record index and trailing Boolean root.
Kind 12 records `for`, with the body-end index and Boolean root or `-1` for
omission. Exactly two following records represent initializer and iterator,
before body records; nonempty clause spans exclude their delimiter. Kind 13
marks an empty clause with slot 0, root `-1` and its delimiter's one-token span.
These records all count against the existing twelve-record limit.
The independent reader checks roles, source, scopes, roots and nearest targets.
Existing terminal-return envelopes remain valid; verified non-fallthrough
structured bodies need no synthetic return. In v51 alone, an infinite empty
`for` can have zero expression nodes. Reachable non-void fallthrough rejects.
All upper bounds, three-parameter ABI, scanner inventory and v1–50 servicing
remain unchanged. This adds no named transfers, standalone blocks, calls,
vacant/richer locals, source grammar decision or persisted-format revision.

Provisional projection v52 adds nested empty or nonempty standalone lexical
blocks using existing brace syntax; it adds no grammar spelling.
Older projection layouts cannot identify or authorize a source-bound
standalone-block scope. Kind 14 is admitted beginning in v52: a recognized
block selects at least v52, while a v53 projection may contain it alongside
kind 15. Versions 1–51 retain their standalone-block restrictions.
Statement kind 14 stores the exclusive body-end statement index in `slot`,
uses root `-1`, spans the opening through closing brace, and precedes its child
records in preorder. Only a recognized standalone block selects kind 14 and
its structured failure path. Raw-token resemblance cannot select it, and
there is no successful hosted-parse fallback.

Provisional projection v53 appends statement kind 15 for non-void standalone
`_ = expression;` over the existing six primitive scalar types. Its existing
five-word statement stores the one-based parameter/local identity in `slot`,
the intrinsically typed expression root in `root`, and the exact
underscore-through-semicolon span. The matching four-word local record stores
the scalar type, readonly bit zero, and the exact underscore source start and
length. The source-bound reader consumes the exact underscore, equals sign,
root and semicolon, and checks complete postorder-node/source coverage.
Duplicate slots, anchors or roots, wrong type/span/version, invisible
identities and discards in a `for` header reject. Only an actual kind-15 record
selects v53.

There is no contextual discard target type: `_ = 1;` rejects while
`_ = int32(1);` is well typed. Existing conversion, read, update, Boolean,
lazy, conditional and supported primitive-operator typing remains in force.
Lowering captures evaluation once in a readonly hidden `<discard>` local
inside a managed lexical `BoundBlock`; the hidden identity is never visible by
name. It counts toward the limit of eight total named and hidden identities.
The compiler-generated block consumes no source structural depth. The
392-word buffer, 16K UTF-16 source, 64-token, 12-record, 32-node,
three-parameter, 16-level expression and six-level source-structure bounds,
and shared 100,000-visit/resource gates are unchanged.

The independent audit also bounds parser recursion, not just postorder tree
height. The publisher shares local/parameter checks and header/version writing
to preserve the existing semantic work limit. Invalid input clears provisional
projection data and does not publish completion; failed CLI builds retain
existing PE bytes. There is no hosted parse fallback or raw-record
reinterpretation. Syntax-43, scanner-8/schema-8, diagnostic and token
inventories, semantic/ISA/Contract/TRRM formats, native formats and ABI are
unchanged. Only the provisional frontend feature version is appended.

The independent source-bound reader validates brace spans, nested extents,
visible names and local identities before lowering to the existing managed
lexical `BoundBlock` and shared verified path. Names use ordinal normalized
identity: initializer references resolve only to preceding visible declaration
identities, active names cannot be shadowed, and closed sibling scopes may
reuse spellings with monotonic slots. Scope exit removes inner name/write
authority through the existing cleanup path without undoing outer mutations;
returns and nearest loop transfers retain their existing targets. Failed
validation publishes no partial projection.

Standalone blocks use kind 14 beginning in v52; non-void scalar discard uses
kind 15 in v53. Versions 1–51 retain their block restrictions, and versions
1–52 retain their discard restrictions and prior source audits. The
392-`int32` buffer and all record widths remain fixed:
16,384 UTF-16 source units, 64 tokens, eight total named/hidden local
identities, 12 statement records, 32 expression nodes, three parameters,
16 expression-recursion levels, six source structural levels and the shared
100,000-visit limit. Compiler-generated discard blocks do not consume source
depth. Scanner-8/schema-8, diagnostic kinds 1–12, token IDs,
semantic/ISA/Contract/TRRM formats and native ABI remain unchanged.

`Terrum.CodeAnalysis` is a reusable C# compiler library. `terrumc` exercises
that library; it is not an independent parser or binder. Syntax snapshots remain
immutable and usable independently. The separate [native profiles](native.md)
binds a bounded scalar/compact-value language and emits Windows x64 executables through
verified structured operations. A clean syntax result is deliberately not a
valid-program claim: `var x = 1;` parses but semantic analysis rejects its missing
numeric context. General ownership/lifetime checking, general semantic IL and
richer TRRM metadata, general managed graphs and standard-library execution
are not implemented.
Primitive type names in the implemented set (`bool`, `char16`, `int32`,
`uint32`, `int64`, `uint64`) cannot be redeclared by ordinary structs,
classes, static operator interfaces or generic type parameters, even in
another namespace. `Compilation.CreateCoreLibrary` and
`terrumc analyze|build-library --corelib` and
`terrumc compile-module producer --corelib` and
`terrumc compile-native producer --corelib` opt into a bounded Core bootstrap
without a referenced standard library. The Core compilation may declare a
global primitive struct with exactly one private, uninitialized field of
its own type; the field reads as the compiler-owned primitive value without
recursive storage. Core-defined static and readonly instance methods bind,
export and execute through existing semantic modules and native DLLs;
consumers resolve them to the library implementation rather than a second
nominal type. Core primitive constructors, operators, setters,
conformances, writable backing and a full standard-library build are not yet
supported. Semantic module revision 37 marks primitive-owned methods in
both producers and consumers; earlier revisions reject those declarations
while retaining ordinary nominal layouts. The `--corelib` option is a
provisional compiler build mode, not a verified Core artifact identity or a
grant to ordinary dependencies.
Designated Core primitive declarations also admit explicit readonly by-value
getters for the six supported scalar types. A getter retains its own
visibility, accessor-local `[NoThrow]` effect, private backing access and
property identity distinct from an ordinary `get_Name` method. Semantic v38
is selected when a primitive-owned getter is retained by a producer or
consumer; older headers reject this shape, while primitive methods without
getters retain v37. Independent semantic/native consumers and compatible
producer replacements preserve getter calls without a new native ABI.
The current parser rejects attached numeric width suffixes with `TRM0006` and
has no prefix-cast production. It supports context-typed decimal, hexadecimal
and binary integer literals without a fallback type, numeric digit separators,
and checked numeric type-call conversions for `int32`, `uint32`, `int64`,
`uint64` and `char16`. Empty `0x`/`0X` and `0b`/`0B` prefixes diagnose as
`TRM0007` and `TRM0008`; malformed digit separators diagnose as `TRM0009`.
Hexadecimal and binary literals construct target-width bit patterns, while
decimal literals retain numeric range semantics.
Existing source function and local shadowing keeps its ordinary call-binding
precedence. Top-level generic function templates support multiple independently
constrained type parameters over eligible `Copy` structs, scalar value types,
and existing source-class/`ManagedNode` handles. Handle substitutions preserve
readonly/write and supported optional qualification; details are in
[bounded generic functions](#bounded-generic-functions). A complete
explicit type-argument list fixes each position; typed arguments and typed
existing `out` destinations infer each corresponding parameter independently.
Expected-result context can fill an unresolved parameter represented in the
return type before contextual argument binding. Contextual literals bind only
after their target type is known and never supply a default type; inference
does not convert an already typed argument. Ordered specialization keys
preserve parameter positions, including swapped forwarding. Each parameter
has independent definition-site `Copy` and operator constraints; the function's
declared effects, including `[NoThrow]` where present, are checked against the
operations permitted by those constraints. Distinct parameters stay distinct
even when they select the same type. Scalars support
Copy-only templates, without primitive operator witnesses. Unique/owning
type parameters, constructor/method inference, ordinary overloads, partial
holes and a public generic ABI remain unsupported. Floating literal binding
and custom literal/string-target execution remain unsupported.

Syntax-25 adds a bounded compile-time requirement: `const T name = expression;`
declares an immutable scalar local whose initializer must evaluate during
compilation. `[ConstEval]` is supported on internal top-level functions with
scalar inputs and result (`bool`, `char16`, signed/unsigned 32/64-bit integers).
Bodies support scalar locals/assignment, branches, `while`/`for` with loop
transfer, and calls to other `[ConstEval]` functions. Every reachable or
unreachable expression in such a body must use only scalar reads, checked
built-in operators/conversions and `[ConstEval]` calls: I/O, ownership,
managed state and calls to ordinary functions are not permitted. Evaluation
uses target-width checked arithmetic, short-circuit boolean operators, a
64-call depth limit and 4,096 statement/expression steps per required
initializer; invalid operations or exhausted limits diagnose without
executing the expression at runtime. A normal call to the same function
remains a normal executable call. The requirement is resolved after all
function bodies are bound, so forward calls work. The marker is not exported
or persisted as a promise in imported signatures; private functions and
their folded local values use existing semantic/native module representations
without a wire-version change. Required expressions cannot call imported
functions. Public/generic `[ConstEval]`, non-scalar values, registered
literal parsers and ambient I/O remain unsupported.
The private [integer-facts candidate](../self-host/IntegerFacts.terrum) supplies
bounded `[ConstEval]` predicates for checked `int32`, `uint32`, `int64`
and `uint64` arithmetic, including signed negation and absolute value.
The `int32` subtraction and multiplication checks compare widened `int64`
results, whose full `int32` input products fit safely. The 64-bit signed
checks rearrange bounds before arithmetic; unsigned addition compares
against remaining headroom and multiplication divides the maximum bound
only for a nonzero operand. The predicates themselves cannot overflow.
Zero divisors reject, signed-minimum division by `-1` rejects, and the
corresponding remainder is defined. Required static proofs and ordinary AOT/JIT
calls exercise these predicates under both runtime policies without publishing
an arithmetic contract or changing operator behavior.

Syntax-26 also supports `const expression` as a scalar subexpression
requirement in locals, returns, call arguments and conditions. It uses the
same evaluator, limits and failure diagnostics as required declarations,
but does not make the enclosing local or expression constant. `const F() + x`
requires only `F()`; `const (F() + x)` requires the entire parenthesized
expression. The value replaces the requirement at its original expression
position after all bodies are bound; surrounding runtime calls and
short-circuit behavior remain unchanged. The contextual `const(...)` spelling
without intervening trivia remains an ordinary named call, while
`const (expression)` is the parenthesized requirement. When `const` names
a local, `const + 1` and `const - 1` remain ordinary binary operations;
parenthesize required unary signs as `const (+1)` or `const (-1)`.
A required expression cannot read runtime locals, use non-scalar results,
or invoke ordinary functions, even in a short-circuited branch. No
semantic/native version or public contract changes.

Syntax-27 adds body-only `static require boolExpression;` and outer-body
`static ensure boolExpression;`. The initial constant-only proof floor uses
the same eligible internal scalar `[ConstEval]` functions, checked target
arithmetic and 64-call/4,096-step budget as required constant expressions.
`static require` establishes a point obligation from closed expressions or
earlier immutable `const` locals. `static ensure` is an outer normal-return
obligation, not a point check: it accepts only closed conditions independent
of local/parameter state, so one proven true value covers all normal returns.
Both erase before semantic verification and native emission. A known false
condition diagnoses `TRM3025`; unknown values or exhausted evaluation
diagnose `TRM3026`; unsupported effects/calls diagnose `TRM3027`. No
runtime fallback, public declaration clause, symbolic parameter proof,
result-valued `return` reference, generic template proof, or wire contract
is introduced. Ordinary executable `require` and its failure/unwind policy
remain unchanged.

Semantic v24 validates source-class/`ManagedNode` readonly-versus-writable
access and mutable-class receivers. Semantic v25 adds a distinct persisted,
non-exported `MutReceiver` contract for methods on non-owning compact structs;
source visibility may be public or private, but does not expose a mutating-
receiver ABI. Calling one on a named mutable local or its writable nested
struct fields, or forwarding the existing writable receiver through `this`
and its writable fields, borrows and updates that caller place,
including writes before a checked exception; default-readonly methods and
by-value arguments retain copy behavior, and the ordinary nonmutating receiver
ABI is unchanged. The verifier rejects forged receiver
loans from immutable places, and nested field writes within a valid receiver
loan are allowed.

Named mutable locals, their writable nested struct fields, and forwarding
through an existing writable `this` and its writable fields are supported
receiver places. A nested call such as `outer.Inner.Mutate()` requires
writable containing storage and fields at every step. The receiver reserves
its root before argument evaluation; arguments cannot invalidate that root.
Partially initialized receiver roots, temporaries, managed-class-field receiver paths,
rebinding `this`, public exported
mutating-receiver ABI and general references remain unsupported.
Semantic v26 persists the nested mutable-field loan path, including its root
and field identities; the verifier rechecks access, types, initialization
and mutability rather than trusting serialized byte offsets. Whole-local
non-owning receiver loans still use v25. Older module headers reject the respective
contracts; programs needing neither retain their existing semantic versions
and bytes. Primitive
buffer-view alignment and general `out`-to-`init ref<T>` migration remain scheduled.
Readers still normalize private managed artifacts from versions 18 through 23
without granting more authority than their original contract. Writer version
selection follows the required receiver contracts, not managedness.
Semantic v27 permits ordinary `mut` methods on unique structs through
initialized named mutable owners or forwarded writable `this`. The methods
may update mutable scalar/Copy fields; they cannot replace owning fields,
move `this`, or widen the readonly NoThrow cleanup contract. Receiver
reservation rejects argument-side invalidation before invocation, and
mutation neither transfers ownership nor adds a cleanup obligation.
Readonly unique loans cannot invoke these methods. Writers select v27 when
such methods are present; earlier headers reject them.
Semantic v28 extends nested mutable-field loans through fully initialized
unique owners and unique child structs. The same writable-path checks and
whole-root reservation apply; a borrowed child is not staged as an owning
argument or separately destroyed. Checked failures retain earlier writes,
and eventual recursive cleanup observes the updated state exactly once.
Semantic v29 permits the same writable owning-field paths for
`OwnedInt32Buffer.Set`, `TryAppend` and `Clear`, without widening the allowed
leaf or intrinsic target to arbitrary types or operations. Semantic v30 adds
lexical `SliceMutable` acquisition from those writable buffer fields. Range
arguments run once before activation and cannot invalidate the reserved root;
the active view excludes all other root access, including sibling fields.
Ending the scope or unwinding releases only the loan, not the buffer.
Partial-constructor mutation and owning-field replacement remain unsupported.
Earlier artifacts retain their existing bytes when they do not
require a newer loan contract; older headers reject the respective broadened
contract even though the field-path encoding is unchanged.
Semantic v31 permits managed-backed leaf producers to export existing public,
handle-free compact `Copy` structs of at most 16 bytes by value as parameters
and results, and through the existing `bool`/`out` conditional-output contract,
including scalar outputs. The existing Windows x64 value ABI and `NoThrow`
contracts are unchanged. Public-field visibility and nominal type provenance
requirements apply to each `Copy` type that crosses an argument or result ABI,
not automatically to an export's declaring owner. Existing scalar-only static exports on public owner
types with private fields remain valid, and a static declaration on a unique
owner does not transfer that owner. Snapshots are independent values, not
references into private managed storage. Managed handles, unique values,
nested managed-containing values, views and mutating-receiver ABI
remain excluded, as does importing another producer from a managed leaf.
Writers require semantic v31 for these expanded export contracts; scalar-only
managed producers retain their prior required version, and older headers reject
the expanded contracts. This adds no syntax or scanner change and does not
change the native physical ABI or profile versions.
The semantic v32 persisted-graph contract permits managed roots and
intermediate producers in acyclic native graphs. Their imports and exports
communicate only through the established scalar and handle-free public `Copy`
by-value/result and
`bool`/`out` contracts; managed or unique values, views, reference modes and
mutating-receiver ABI still cannot cross a managed participant boundary.
Each persisted native image retains its own heap and root chain, and calls
preserve the caller's roots while another image runs. `ManagedHeap.Collect()`
remains image-local. Semantic v32 supports private managed consumers through
the existing single-provider semantic `CreateConsumer`/`Link` path and native
single-provider and module-set entry points. Semantic modules statically link
into one image, remap private class identities, and use one heap/root domain.
Persisted native EXE/DLL sets retain separate image heaps and root chains;
calls preserve caller roots, and collection remains image-local. A compatible
semantic provider can be selected on each
`run-module` launch, which JITs from the persisted artifacts; `link-module`
must emit a fresh PE for each selected provider. Native package replacement
does not re-emit its persisted consumer image. Managed handles, unique values,
views and reference modes cannot cross these boundaries; there is no
cross-image shared collection, managed-handle ABI or cyclic graph support.
This support adds no public semantic `LinkSet` or native-set JIT API. These
contracts add no syntax, scanner, native physical ABI/profile or graph
package-format change.

The [accepted memory model](memory-model.md) does not make this compiler or its
managed heaps concurrent. General caller-unsafe/trusted callable contracts,
thread-transfer/share capability metadata, `MemoryPrimitives`, explicit memory
orders and collector-aware reference atomics remain unsupported. Existing
`unsafe` recovery and checked ownership verification are bounded mechanisms,
not a general unsafe escape hatch. Their consistency work is ordered in the
[memory-model implementation sequence](roadmap.md#memory-model-implementation-sequence).

| Surface | Implemented | Not yet implemented |
|---|---|---|
| Text | Immutable UTF-16 source, half-open ranges, line mapping, exact malformed-editor-input preservation | Rope storage, byte maps, generated-source maps |
| Lexing | Pinned Unicode identifiers with standalone `_` reserved and closed-catalog `@keyword` escapes, ASCII decimal, `0x`/`0X` hexadecimal and `0b`/`0B` binary numeric tokens, digit separators, decimal fractional/exponent token boundaries, generalized ordinary/documentation comment fences, leading trivia, strings, diagnostics including `TRM0006`–`TRM0009` for numeric suffixes, missing based digits and malformed separators, `TRM0010` for overlong comment closers, `TRM0011`/`TRM0012` for invalid/unsupported maximal punctuation runs, EOF; the scanner-8 integer record retains suffix metadata only for diagnostic-bearing tokens | Complete canonical unescaped reservation/context migration; interpolation, raw/character/native literals, directives; successful range/slice/optional-access tokens |
| Declarations | Using, file-scoped namespaces, functions, compact structs and `unique struct` with primitive/nested Copy/owning fields, bounded source classes including `mut class`, readonly default and writable `mut` class receivers, non-owning compact-struct `mut` methods with named mutable-local receiver writeback, explicit constructors/methods, checked `[NoThrow]`/`[DoesNotReturn]` and unique `[Cleanup]`; compilation-local static operator protocols and closed constrained functions; library binding and validated references, including combined leaf-producer [module sets](module-sets.md) and explicitly selected acyclic [native graphs](native-graph.md) | Inheritance, unique/inline classes, general interfaces, enums/unions, owning fields in Copy structs, general attributes and generic classes/structs (struct and field attributes diagnose `TRM3001`), parameter-only `implicit ref<T>` binding and ordinary reference-lifetime contracts, public generic witnesses, cyclic dependencies, automatic version resolution and resilient module layouts |
| Types | Qualified named types, nested type arguments, one `?` per type nesting level, `void`; source-class and `ManagedNode` optional handles with `write` capability preserved through fields, parameters, results and inference; repeated `?` diagnoses | `pin ref<T>`, scalar options, general generic optional normalization after substitution, family defaults/conditional conformance, general ownership/form qualifiers and general borrowed references, transparent-wrapper ABI contracts |
| Statements | Initialized typed/inferred locals, standalone vacant locals, `mut`, blocks, `if`/`else`, `while`, scoped `for`, post-tested `do`, nearest/named enclosing-loop `break`/`continue`, return, statement-form `require`, bounded constant-only body `static require`/outer-body `static ensure`, structured `try`/`finally`, unsafe checked `try`/`catch` (optionally with `finally`), assignment, primitive integer compound assignment, managed optional `??=` and expression statements, standalone `_ = expression;` with statement-end owning cleanup | `defer`, policy blocks/expressions and variants, `unsafe(expr)`, `for (... in ...)`, general switch/patterns and wildcard patterns, other context-specific discard destinations, declaration pre/postconditions and symbolic static proofs, typed requirement-failure selection, general exception objects/throw, async, nominal compound assignment |
| Expressions | Names, raw literals, `()`, grouping, unary/binary operations, target-checked `?:` and managed-handle `??`, context-typed decimal/hexadecimal/binary integer literals without fallback (based values construct target-width bit patterns), numeric digit separators, checked numeric type-call conversions for `int32`/`uint32`/`int64`/`uint64`/`char16`, member/index access, calls, scalar required `const` subexpressions and bounded internal `[ConstEval]` calls, explicit closed static-protocol calls including nominal `++`/`--` updates on the eligible writable `Copy`-struct place envelope with exact operand/result type equality, bounded inference of multiple independent type parameters for top-level generic function templates over eligible `Copy` structs, existing scalar value types and existing qualified source-class/`ManagedNode` handles (preserving readonly/write and supported optional qualification) from typed arguments or typed existing `out` destinations (expected-result context fills unresolved parameters present in the return type), generic signature-based result-type lookup, `new`, named whole-owner `move`, by-value unique arguments, conditional unique `bool`/`out` results, readonly unique and initialized owning-field loans, qualified source-class/`ManagedNode` writes and mutable-class receivers, named-place and writable nested-field loans for compact-struct `mut` receivers including fully initialized unique roots/children, named/ref/out argument syntax, checked synchronous `OwnedInt32Buffer` operations including `Set`/`TryAppend`/`Clear` and lexical `SliceMutable` through writable owning fields, and lexical readonly buffer slices | Floating/custom literal binding and registered borrowed-text parsers, public/generic constant-evaluation contracts, `require` output-expression form and exception-type selection, implicit `ref<T>` call-site loan introduction and temporary materialization, character literals, partial owning-field moves, owning field replacement, general expression temporaries, temporary/managed-class-field mutating-struct receivers, partial-constructor mutation, rebinding `this`, public mutating-receiver ABI, general stored loans/references, evaluation outside the native profile, user-defined conversions, unique/owning generic arguments, views, generic classes/structs, user structs containing managed fields, primitive operator conformance, general argument/result inference and partial holes, constructor/method inference, ordinary overload declarations (rejected with `TRM3003`), contextual discard/lambda `_` forms, general conditional access |
| Managed storage | Aliasable `ManagedNode` and source-class handles, optional edges and non-null pattern snapshots, readonly bare handles and explicit `write` capability, mutable-class receiver checks, qualified field/parameter/result/inference paths, handle-free compact Copy fields in classes, descriptor-based cycle tracing, complete-publication class factories, exact frame/temporary roots and explicit nonmoving collection under both policies, including exceptional root retirement and escaping-frame unlink | Managed handles inside user structs, stored interior pointers, implicit GC, native DLL/module graph managed-handle ABI |
| Tooling | Traversal, lossless reconstruction, deterministic content IDs, checked edit batches, structured CLI schema | Semantic queries, rename, formatter, generators/analyzer loading, LSP, incremental reuse |

Only the grammar below is supported. Unsupported constructs diagnose rather than
being ignored. Parser recovery is useful for partial files, but not a promise to
recover every later declaration optimally.

```text
file        = using* namespace? declaration* EOF
using       = "using" qualifiedName ";"
namespace   = "namespace" qualifiedName ";"
declaration = attributes modifiers (function | struct | class | interface)
attributes  = ("[" name "]")*
modifiers   = ("public" | "internal" | "private" | "static" | "mut" | "unique")*
function    = type name typeParameters? parameters constraints* block
struct      = "struct" name (":" type ("," type)*)? "{" member* "}"
class       = "class" name "{" member* "}"
interface   = "interface" name interfaceParameters "{" (attributes modifiers operator)* "}"
member      = attributes modifiers (field | constructor | function | operator)
operator    = type "operator" ("+" | "*") parameters (";" | block)
typeParameters = "<" name ("," name)* ">"
interfaceParameters = "<" "this"? name ("," name)* ">"
constraints = "where" name ":" (type | attributes) ("," (type | attributes))*
field       = type name ("=" expression)? ";"
constructor = containingTypeName parameters block
parameters  = "(" (mode? type name ("," mode? type name)*)? ")"
type        = "write"? (qualifiedName | "void") ("<" type ("," type)* ">")? "?"?
mode        = "ref" | "mut" "ref" | "out"
local       = localInitializer ";"
localInitializer = "mut"? ("var" | type) name "=" expression
statement   = block | local | if | while | for | tryStatement | unsafeStatement | return | require | assignment | "break" ";" | "continue" ";" | expression ";" | ";"
if          = "if" "(" expression ")" block ("else" (block | if))?
while       = "while" "(" expression ")" block
for         = "for" "(" initializer? ";" expression? ";" iterator? ")" block
initializer = localInitializer | assignmentClause | expression
iterator    = assignmentClause | expression
tryStatement = "try" block ("catch" block ("finally" block)? | "finally" block)
unsafeStatement = "unsafe" block
return      = "return" expression? ";"
require     = "require" expression ";"
assignment  = assignmentClause ";"
assignmentClause = expression ("=" | "??=") expression
```

Assignment is a statement, not an expression. Native places include initialized
mutable locals, accessible fields of mutable compact values, constructor fields,
conditional output scratch slots and checked writable host-buffer elements.
The `??=` subset accepts only initialized optional managed-handle locals or
writable mutable source-class fields, as described above.
Prefix/postfix integer updates are also expression statements and may occupy
a `for` iterator. Their writable-place subset is described above; they do not
make arbitrary value expressions valid statements.
Unique local places additionally track vacancy and cleanup obligations.
Composite owning fields are assigned once along sequential constructor flow;
complete owners can be transferred, but partial field moves are unsupported.
`OwnedInt32Buffer` is an opaque, compiler-owned, move-only native resource with
fixed capacity and an initialized `int32` prefix. `TryCreate(capacity, out buffer)`
publishes on true only; `Count`, `Capacity` and `Read(index)` borrow
an initialized owner, while `Set(index, value)`, `TryAppend(value)` and NoThrow
`Clear()` require a mutable named buffer or a writable buffer-field path
through a fully initialized unique owner. The latter reserve that original
root before evaluating arguments: moving or replacing it within an argument
is rejected even if its slot becomes live again. Readonly nested queries
remain possible. Field mutation neither moves the child buffer nor creates
an additional cleanup obligation; active views of that root block mutation.
An `out var`
binding is readonly; explicitly move it into a `mut OwnedInt32Buffer` binding
to append or set. `ReadonlyInt32BufferView starts = buffer.Slice(start, length);`
checks that the range lies in the initialized prefix once, then permits
`starts.Length` and checked `starts.Read(index)` within its braced lexical
scope. The owner (including an owning field's root) cannot be moved, replaced,
dropped or mutated while any of its readonly slices is active; multiple
readonly slices and completed readonly queries may coexist. Slice start and
length are evaluated once before capturing the owner place; calls in these
expressions cannot invalidate that owner. A view cannot be copied, assigned,
passed, returned, stored in a field, declared as a for initializer or used
as a temporary expression. `mut MutableInt32BufferView window =
buffer.SliceMutable(start, length);` requires a named mutable buffer or a
writable buffer-field path through a fully initialized unique owner and grants
exclusive access to `window.Length`, checked `window.Read(index)` and
`window.Set(index, value)` until its lexical scope ends. While it is active,
no other view or owner operation may access, replace, move or drop that root;
range expressions can query the original owner before the view becomes active
but cannot invalidate it. Exclusion is conservative for composite owners:
even reads or independent views of sibling fields are blocked while the
mutable field view is active. Scope exit or checked unwind ends only the
loan, preserving prior element writes and the owner's cleanup obligation.
Checked failure while evaluating a `Set` index or value, or a `TryAppend`
value, leaves that individual write unapplied; an active mutable view and
its owner remain usable after the exception is caught.
`Clear()` resets only the initialized count and
retains the allocation and capacity; it cannot run during any active view.
There is no resize, general aliasable view or generic
buffer or user-defined buffer representation. `TryAppend` is a bounded
initialized-prefix adapter for compiler components, not an implementation of
the proposed general `Buffer<T>`/`TryAdd` library contracts.
The `ManagedNode` proof copies handles without duplicating payloads or a
cleanup obligation. `ManagedNode.TryCreate(value, out var node)` publishes only
on true; `node.Value`, `SetValue`, `SetNext`, `ClearNext` and
`TryGetNext(out var next)` access a scalar and a single nullable internal edge.
Semantic version 24 applies readonly-by-default access to these intrinsics:
reads use readonly handles, while mutation requires a writable handle and
checked writable receiver. `write` qualification is preserved through optional
handles, fields, parameters, results and inference. A bare fixed handle cannot
mutate the node.
`ManagedHeap.Collect()` walks typed active roots across generated frames,
including staged call arguments, outputs and pending returns, then follows
descriptor-listed managed edges and reclaims unmarked cycles. This bounded collector runs only
at explicit calls and at executable entry teardown; it does not rely on
reference counts, stack word scanning or hidden allocation-triggered GC.
Managed handles cannot be placed in user structs or passed across external
calls, DLL exports or public persisted module contracts. Both runtime
policies admit closed executables/JIT and isolated native DLLs with scalar
exports and, under semantic v31, managed-backed compact handle-free `Copy`
exports; managed state remains private. DLL callers must serialize calls and
explicitly collect abandoned nodes before unload.
Checked unwind retires departed lexical roots after their nested user cleanup,
clears argument and unpublished output staging, and unlinks a root frame only
when unwinding out of that function. A same-frame catch keeps its surviving
roots; a pending managed return remains rooted through collecting cleanup.
Source classes use the same root and collection machinery. Their explicit
constructors initialize every field before the generated
`C.TryCreate(arguments, out C)` factory publishes an eight-byte handle.
Allocation failure returns false; checked constructor failure frees the
unpublished object and propagates. Collector-private zeroed reference slots are
not source defaults. Classes have at most 32 fields and payloads are bounded
to 128 bytes; semantic revision 36 retains more than 16 fields, including
alongside transparent layouts. Readonly/mutable field access, visibility and
partial-construction checks remain mandatory.
Thirty-two four-byte fields fill the 128-byte payload exactly; a smaller
field count does not bypass the independent payload limit.
At the 4,096-object heap limit, a live 32-field class retains its final-field
managed edge while collection reclaims unreachable wide classes and their edges.
Checked constructor failure also frees its provisional wide object before
recovery and leaves the unpublished final-field edge collectible.
Class fields may contain acyclic handle-free Copy structs up to 16 bytes, including
nested compact values. Those fields are copied as complete values, not traced as
potential pointers. Unique owners, views and managed handles inside structs are
not admitted by this extension. Factories accept at most 63 by-value
scalar, managed-handle or handle-free compact Copy arguments. Copy arguments
are complete snapshots, including when later arguments mutate their source;
the factory forwards them through the existing direct/indirect aggregate ABI.
The ordinary 64-argument limit includes the factory's output or constructor's
receiver. Arguments beyond the register positions use ordinary stack slots.
Neither the provisional object nor managed arguments can disappear during
constructor collection. Copy payloads are not conservative roots.
Classes have no inheritance, inline class representation or public managed
module ABI; `new C(...)` is rejected rather than inventing an allocation exception.
Semantic version 24 records nominal mutability and `write` access for source
classes. A `mut class` may declare mutable fields and writable `mut` class
receivers; bare class handles are readonly and writes through a handle require
`write` authority in addition to mutable member declarations. A writable
child handle stored in a field retains that capability when read through a
readonly parent; no deep-read propagation is applied. Constructors initialize
the unpublished receiver and publish only after complete initialization.
Writable storage is invariant, output destinations require the exact qualified
type, and value-level `write`-to-readonly attenuation is allowed. These rules
also apply through nested handles subject to private/internal access and
partial-construction checks.
`native-20` adds `C?` and `ManagedNode?`, contextual `null`, non-null-to-optional
lifting, `is null` and `is some value`. Successful extraction creates a non-null
handle snapshot; clearing the original edge does not invalidate it.
Patterns participate in conditional flow, including short-circuit conditions,
`while` and `require`. The payload name belongs to the enclosing lexical scope;
reads additionally require proof that extraction succeeded on the current path.
The pattern does not narrow the original variable or an aliased field.
An initialized absent value is not an uninitialized
variable or an output from a failed call. Constructors must explicitly initialize
optional fields, and direct optional dereference is rejected. Optional scalar
types and generic normalization remain unsupported.
Version 18 semantic modules separately admit private `ManagedNode` code in a
leaf producer whose exported declarations are scalar and by-value only.
Version 19 extends that private-leaf contract to source classes and their
optional handles, serializing checked layouts, construction and root metadata.
Class-free modules retain the version 18 format. Classes with inline Copy fields
use version 20, retaining their private nested layouts and remapping type IDs
during semantic linking. Version 19 still rejects those fields and remains the writer format for
scalar/reference-only classes with at most 16 fields when no later feature
requires a higher revision.
Classes whose constructors accept Copy structs require version 22, including
constructors that retain only scalar portions of those arguments. Older
versions reject aggregate constructor parameters. The native profile, scalar
export boundary and private-leaf dependency restriction remain unchanged.
Constructor signatures with more than three inputs require version 23; older
versions retain their original arity limit. High-arity functions retain the
existing page-safe frame bound and exact roots for potentially managed spills.
Semantic version 24 records managed class/`ManagedNode` access capabilities.
Managed artifacts requiring only this contract use v24; readers normalize
private managed artifacts from versions 18 through 23 into explicitly
qualified internal IR, translating legacy class/`ManagedNode` mutation
authority into `write` exactly where each older contract granted it. A v18-v23
header cannot contain v24 write identifiers. Methods requiring the distinct
v25 `MutReceiver` contract use v25 unless their bodies require v26 nested
mutable-field loan expressions, they mutate a unique receiver (v27), or
their field loans traverse unique roots (v28), or their mutable field loans
target the supported buffer intrinsics (v29), or they acquire mutable
buffer-field views (v30).
Older headers reject the respective contract
or expression. Programs needing neither retain their historical versions and
bytes regardless of managedness.
The private managed-state boundary and absence of a public managed-handle ABI
are unchanged. Semantic v31 expands leaf exports to managed-backed compact
handle-free `Copy` values by value. Semantic v32 supports private managed
consumers through the existing single-provider semantic `CreateConsumer`/
`Link` path and native single-provider/module-set entry points; persisted native graphs also
permit managed roots and intermediate producers through the same scalar and
handle-free `Copy` boundary. Semantic modules statically link into one image,
remap private class identities, and share one heap/root domain. Persisted
native images retain separate heaps and roots. Managed handles, unique values,
views and reference modes cannot cross these boundaries. Cross-image shared
collection, graph cycles and DLL-unload collection remain unsupported.
Compatible persisted consumer/intermediate images continue to work with leaf
replacement under AOT and root-only JIT.
The [source-line index candidate](../self-host/SourceLineIndex.terrum) uses `OwnedInt32Buffer`
to build and own a line-start table, with binary-searched UTF-16 positions checked
against host fixtures under both native failure policies. It reserves one word
per actual line start rather than per source code unit, preserving maximum-sized
sparse inputs within the existing buffer capacity. Construction counts first
and fills unknown starts in a second scan; already known endpoints and fully
dense single-unit breaks need no second source scan.
It also returns a line's full UTF-16 extent (including its break) by ordinal
from adjacent starts without rereading source; the streaming candidate walks
line ranges to provide the same extent without owning a table. Invalid
ordinals leave the conditional output vacant. Vacancy-only `init ref`
construction and position, offset and extent lookups reuse the original
`out` paths without changing their contracts. Invalid requests publish no
value, while a later valid request may fill the same vacant destination.
General borrowed references and stored loans remain unsupported. The accepted
`ref<T>`/`ptr<T>`/`box<T>`/`val<T>` forms, general `write` views, `own<T>` and
general `init ref<T>` invocation still requires its representation,
lifetime, cleanup and ABI implementation; it is scheduled implementation
work, not deferred to the Later capabilities bucket. Syntax-22 admits the
bounded `bool`/`init ref<T>` parameter and `init name`
call mode for supported output values, with `init var name` introducing a
vacant destination in the call. A named destination must still be vacant;
an initialized value cannot be overwritten by `init`. A true result requires
complete initialization; false and exceptional paths do not publish.
Compilation-private `init ref<T>` also supports qualified managed handles.
The call's checked scratch and exact temporary roots retain the handle until
successful publication; false and exceptional completion retire the scratch
without granting a readable output. Source binding and IR verification reject
public managed-handle init declarations, including forged public visibility.
The self-host managed scanner uses this mode to publish a complete chain.
Compilation-local `Copy` generic templates also accept `bool`/`init ref<T>`
conditional outputs. Definition-site proof checks publication before
specialization; explicit type arguments or a typed vacant destination may
establish `T`, while `init var` alone cannot infer it. Closed specializations
reuse the existing private init contract and output scratch. Semantic-module
AOT/JIT and separately compiled native producer/consumer fixtures exercise the
private specialization under both policies. AOT/JIT also cover forwarding
multiple scalar and 16-byte Copy init outputs through mixed stack arguments
without publishing any result on false. Private managed-handle
specializations remain image-local behind an unchanged scalar consumer,
including compatible native producer replacement; this does not export generic
declarations or add a new native ABI. General references and
declaration postconditions remain unsupported. Public init contracts support
eligible scalar, Copy-struct and unique outputs; managed graph boundaries
admit only public handle-free Copy outputs, not unique owners or managed
handles. Legacy `bool`/`out`
continues unchanged. Semantic v33 retains init parameter and call modes;
exported declarations distinguish `init:` from `out:` while historical
private declarations retain their earlier contract spelling. Both modes
share checked output scratch, cleanup and native ABI without matching each
other's public contracts.
For-loop initialization runs once, followed by condition, body and iterator;
the iterator does not run after a returning or breaking body. `continue` runs
the iterator before testing the next condition. An omitted condition is true.
Initializer locals remain in scope through the header and body, not afterward.
The iterator cannot reference body locals. Multiple comma-separated clauses
are not implemented; supported integer updates and compound assignments can
occupy the iterator.
Bare `break` and `continue` target the nearest enclosing loop; syntax-29 named
forms select the enclosing loop with that label. They run the normal
cleanup of exited scopes, including nested `finally` and owner cleanup, and
retire those scopes' managed roots; a `for` initializer remains live across
continue and retires when the loop exits. A transfer cannot escape a `finally`,
but a loop wholly inside that `finally` can use its own transfers.
Flow verification distinguishes loop transfers from return and fallthrough
when merging initialization and ownership states.
The binder retains the legacy `while` lowering for unaffected loops and a
distinct iterator target when a `for` needs it for `continue`.
Braces are required on control bodies. Supported precedence, strongest
first: postfix; unary `+ - ! ~ move`; `* / %`; `+ -`; `< <= > >=`; `== !=`; `&`; `^`;
`|`; `&&`; `||`. Binary operators associate left. Lexing does not fuse angle
brackets into shift tokens. Generic-call/comparison ambiguities are not solved by
guessing semantic types in the lexer. See [lexical grammar](lexical-grammar.md)
for precise number, string, identifier and newline rules.

`TRM1005` marks each extra optional modifier in `int32??`, `T??`, or
`T? /* trivia */ ?`. Recovery retains every modifier and continues parsing.
`List<int32?>?` and `T?` remain valid syntax; determining that `T` is already
optional and normalizing its substituted type is future binder work.
Explicit `Option<Option<T>>` syntax is unchanged. `syntax-1` introduced this
rejection rule; `syntax-2` added the lossless `RequireStatement` node.
`syntax-7` includes scoped for-loops, `try`/`finally`, parameterless declaration attributes and
legacy checked numeric prefix casts for supported 32- and 64-bit integer types and `char16`.
`syntax-8` adds `unsafe` blocks and the parameterless checked-policy `catch`,
with optional `finally`; a try requires at least one handler or cleanup clause.
`syntax-9` adds `unique struct` declarations and `move` expressions.

### Bounded generic functions

`syntax-10` adds static operator interfaces, explicit struct conformance,
generic function constraints and generic-name call expressions. Its executable
subset is deliberately compilation-local: each interface has three type
parameters and one bodyless `static` binary operator declaration (originally
`+` or `*`, extended in syntax-17); an internal Copy
struct explicitly implements a closed homogeneous instance. Parameter/result
types, accessibility and declared effects must match. An internal generic
function may have multiple `[Copy]` type parameters with independent
definition-site operator constraints, and may return or accept values of those
formal type parameters or supported scalars. A selected type parameter may be
an existing source-class/`ManagedNode` handle. Distinct parameters remain distinct
in the template contract even if a specialization selects the same concrete
type for both. The function's declared effects are checked against the
operations permitted by its constraints. Boolean-returning templates support conditional `out`
parameters for multiple independently selected types. The ordinary
publication rules apply: true requires
all outputs initialized, false or an exception publishes none, and a caller
cannot read an output without proving success. Output-only type parameters are
permitted, but generic construction and a default value are not implied.
Structured bodies use assignments, blocks, `if`, `while`, scoped `for`,
`break`/`continue`, `require` and return; operations on the type parameter
must come from its declared operator constraints. `[Copy]` alone suffices for
helpers that only copy or select values; it grants no operators. The definition is checked
even when never instantiated. Concrete capabilities cannot make an undeclared
generic operation valid. Calls select a concrete Copy struct, supported scalar,
or existing handle type for each parameter, explicitly or through the bounded
inference described above. An explicit type-argument list fixes every
position; partial holes are unsupported.
Inside a template, helper calls use its current type parameters, explicitly
or inferred; Copy-only helpers may also specialize concrete scalar types.
Swapped forwarding can use `TrySelectPair<TSecond, TFirst>(...)`
or let the reordered typed arguments establish the positions. Every nominal
protocol required by the callee must be declared for the corresponding caller
parameter. Identical
operator signatures do not make distinct
protocols interchangeable. Each callee is checked independently, and callers
use its declared effects, not stronger facts inferred from its body.
For a source-class or `ManagedNode` handle substitution, the complete supplied
type retains its readonly/write and supported optional qualification.
`[Copy]` permits copying the handle; it does not grant field, null-test or
operator operations that are not otherwise available under the template's
constraints. Typed arguments establish type parameters before expected-result
fallback. A permitted result weakening, such as `write C` to readonly `C` or
supported optional widening, can accept a result without retargeting the
inferred type argument. Conditional outputs retain the existing exact type and
qualification rules.
Generic handle calls, results and conditional outputs use the existing
precise-root and access contracts. Collection observes those roots as usual;
semantic linking preserves private class identity remapping, and persisted
native images retain their separate heap/root domains.
`ManagedHeap.Collect` is available inside a generic body with its real
intrinsic signature and effects; it grants no operations on opaque formals.
This managed-handle substitution does not add generic class/struct
declarations, managed fields inside user structs, unique/owning arguments,
views, concurrency semantics, or a public generic/managed ABI.

**Comparison operator protocols (implemented; syntax-19).**

The compilation-local protocol family uses one bodyless static binary
declaration per nominal protocol and the existing three-parameter interface
shape (`TSelf`, `TOther`, `TResult`). Arithmetic and bitwise operators
(`+`, `-`, `*`, `/`, `%`, `&`, `|`, `^`) close over homogeneous `Copy`
operands with result `T`, giving `(T, T) -> T`. Equality and comparison
operators (`==`, `!=`, `<`, `<=`, `>`, `>=`) close over homogeneous `Copy`
operands with result `bool`, giving `(T, T) -> bool`. These are separate
nominal protocols, with source-declared names rather than compiler-known
interface names. Each conformance and each generic operation constraint must
name the corresponding protocol; one operator's conformance does not imply
another operator or any comparison law.
Concrete operands can invoke their declared operator directly without a
protocol conformance; nominal witnesses are required for constrained generic use.
Operator spelling does not imply `[NoThrow]`; the function and witness
effects remain explicit and are checked under the declared constraints.

Generic comparison-driven selection and conditional outputs retain the
existing `Copy`, effect, and `bool`/`out` publication rules. These protocols
do not define universal value equality, key equality/hash, or managed identity;
they add no primitive or heterogeneous witnesses and no public generic ABI.
`syntax-19` adds the comparison declarations and their bounded generic use.
This adds no scanner-record/schema, semantic-wire-format or native-profile
version.

**Unary operator protocols (implemented; syntax-21).**

Compilation-local interfaces may declare one bodyless static unary `+`, `-`,
`!`, or `~` with two formals (`TOperand`, `TResult`), such as
`interface Negation<T, R> { static R operator -(T value); }`.
Closed nominal conformances require a `Copy` struct operand and a supported
`Copy` result. The result is independent of the operand and `!` need not
return `bool`; result-only overloads are not supported. Unary and binary
`+`/`-` have distinct arity-aware identities. Generic bodies require explicit
nominal constraints and use the declared effects; concrete operands may call
accessible declared operators without conformance. Closed specializations
lower through existing verified calls and private module servicing. This
syntax-21 two-formal protocol subset adds no primitive witnesses, nullable
operand operators, `++`/`--`, `true`/`false`, public generic ABI, new semantic
bytes or native-profile change. Syntax-35 separately adds the nominal
place-update call described above with exact operand-shaped replacement.
The two-formal shape is a bounded
implementation of the approved interface grammar.

**Static requirements and implementing-type formals (implemented; syntax-34).**

Requirements use `static R operator ...;` without `abstract`; `abstract`
lexes as an ordinary identifier. Its old token slot 77 remains reserved and
surviving token identities do not move. The shared compiled-scanner catalog
uses the same classification. A protocol may mark its first operand formal
with `this`, for example `interface Addition<this T, U, R>`.
A struct conformance `Addition<Value, Value>` supplies the remaining operand
and result arguments; self is the conforming struct. A constraint
`where T : Addition<T, T>, [Copy]` similarly binds self to its constrained
formal. Explicit extra self arguments, later/repeated self markers and
incompatible operand/result/effect contracts reject. No-self protocols
continue to pass all their ordinary arguments explicitly.

This retains the bounded homogeneous binary/comparison and independent unary
result contracts above, including definition-site proofs, inferred calls and
nominal witness identity. It does not add heterogeneous binary witnesses,
associated/default resolution, public generic/witness ABI or nominal updates.
Self is resolved before ordinary call lowering, so semantic/native formats
and persisted private-specialization servicing remain unchanged.

Forward calls, imported local templates, and direct/mutual recursion reuse
closed specializations; recursion has the ordinary native call/stack behavior,
not a compile-time evaluator or a termination guarantee. At most 32 templates
and 32 total closed specializations, including transitively reached helpers,
are admitted. These lower to ordinary verified calls.
The [quadratic example](../samples/native/static-protocol-quadratic.terrum)
executes for two distinct wrappers. The [integer-power example](../samples/native/static-protocol-power.terrum)
combines scalar loop control with a constrained square helper and explicit
caller-supplied identity.
The [binary-protocol example](../samples/native/static-protocol-binary.terrum)
uses subtraction/division/remainder and explicit bitwise witnesses in
separately constrained helpers. Witness bodies keep ordinary checked arithmetic:
division by zero and signed division overflow fail; signed-minimum remainder
by minus one yields zero. Bitwise primitives themselves do not fail, but an
operator call still has its declaration's effects rather than effects inferred
from its body.
The [Copy selection candidate](../self-host/CopyAlgorithms.terrum)
selects complete values without requiring operator conformance.
Its `TrySelectCopy` variant publishes a selected value only for index zero or
one. `TrySelectCopyInit` offers the same selection through vacancy-only
`init ref<T>` publication; it reuses the original `out` implementation without
changing existing callers. Scalar return typing and output modes remain part
of each helper's declared
signature during definition-site proof and closed specialization.
Copy-only templates also specialize for `bool`, `char16`, `int32`, `uint32`,
`int64` and `uint64`, using their existing concrete argument/result ABI.
This permits scalar selection and conditional output, not new arithmetic
rights on symbolic `T`.

A template can also compose helpers using concrete eligible `Copy` structs
alongside its symbolic parameters. Normal binding resolves concrete constructors,
factories, fields and methods, including imported declarations and inferred
helper results. The isolated definition proof brings in only reached
declarations and their nested value layouts; unrelated declarations do not
consume its function/type budget. Concrete witnesses and declared effects apply
to that concrete type, never to an unrelated unconstrained formal. Proof-only
declarations remain outside executable IR and persisted imports.

Concrete source classes can also participate through their existing
`C.TryCreate(..., out result)` factories, fields and methods. Lazy proof
descriptors preserve cyclic class layouts, readonly/write/optional handle
qualification and declared effects. This does not add `new C(...)` or grant
class operations to symbolic `T`. The isolated proof checks declarations
without importing constructor bodies; executable and persisted programs still
require complete constructor/factory pairs and reject external signatures
containing managed handles. Closed specializations use the ordinary managed roots and
conditional-publication rules.

General interface dispatch, primitive operator conformance, general generic inference,
member access on a type parameter and public generic/witness metadata remain
unsupported. A public concrete wrapper may
use a private specialization without publishing its template or witnesses.
`syntax-11` adds source class declarations; `native-19` adds their bounded
construction and descriptor-based tracing contract.
`syntax-12` adds optional-presence patterns; `native-20` executes them for managed
handles with exact roots and explicit absence.
`syntax-13` adds nearest-loop `break` and `continue`, including scoped `for`
iterators. Semantic modules use version 21 when retaining these operations;
native profiles and exported contracts do not change.
`syntax-14` removes numeric prefix casts, adds checked primitive type-call
conversions, and gives decimal integer literals contextual typing. Attached
numeric width suffixes diagnose as `TRM0006`.
`syntax-15` adds contextual hexadecimal integer literals with `0x`/`0X`;
`0x1f32` is one hexadecimal value, and a prefix with no following ASCII hex
digit diagnoses as `TRM0007`. At syntax-15, based literals used nonnegative
magnitude/range interpretation; syntax-16 supersedes that with target-width
bit-pattern construction. The scanner-4 record layout and semantic/native
artifact versions do not change.
`syntax-16` adds `0b`/`0B` binary literals, digit separators, and target-width
bit-pattern construction for based literals. A missing binary digit diagnoses
as `TRM0008`, and malformed separators as `TRM0009`. The scanner-4 record
layout and semantic/native artifact versions do not change.
At the `syntax-17` milestone, homogeneous static binary operator declarations
were extended with `-`, `/`, `%`, `&`, `|` and `^`, in addition to `+` and
`*`. Each closed Copy-struct
conformance and generic use still requires the matching nominal protocol and
declared effects. At that milestone, unary, comparison, shift and heterogeneous
protocols were unsupported. These specializations lower to existing concrete
calls: scanner-4, semantic module versions and physical native ABIs are
unchanged.
`syntax-18` recognizes qualified generic-call type arguments, including
`write C` and `write C?`, for bounded generic specialization over existing
source-class/`ManagedNode` handles. The managed generic producer examples use
existing semantic v24 contracts; version selection remains feature-based.
This does not change the scanner-4 record/schema, wire format or native profile.
`syntax-19` adds the bounded nominal equality/comparison protocols described
above. It does not change the scanner-4 record/schema, semantic wire format or
native profile.
`syntax-20` reserves `_` as a non-identifier and supports standalone
`_ = expression;`. Scanner-5/schema-5 classifies `_` distinctly while retaining
the scanner-4 integer-record shape. Discard evaluation and immediate owning
cleanup use existing semantic nodes, so feature-selected module versions and
native profiles do not change.
`syntax-21` adds unary static operator declarations for compilation-local
`Copy`-struct protocols with independent `Copy` results, using the existing
closed-call servicing path without changing scanner-5/schema-5, semantic
module bytes or native profiles.
`syntax-22` adds contextual `init ref<T>` parameters and `init name`/`init var name`
arguments for bounded boolean conditional initialization; v33 records the
distinct init parameter and call modes in semantic modules. Public declarations
use `init:` rather than `out:` contracts, while older private v33 declarations
retain their original `out:` spelling. The scanner record, native profile and
physical output ABI remain unchanged.
`syntax-23` adds explicit getter-only, by-value properties on compact Copy
structs, source classes and designated Core primitive declarations.
Accessor-local `[NoThrow]` is verified like the same contract on a method.
Member reads bind to a readonly-receiver call, not
a field or a `get_Name` naming convention. Semantic v34 records the property's
member identity alongside its function body and contract, including exported
Copy-struct getter declarations; existing field layouts and their mutability
are unchanged. Primitive-owned properties additionally require v38 in producer
and consumer semantic modules; existing nominal getters retain their selected
versions. Exported structs still require their existing public field
layout, and managed class handles remain private to their image. Setter,
auto-property and ref-return forms are unsupported; syntax-28 adds the
expression-bodied getter forms described above. No
scanner or native ABI/profile version changes.
`syntax-24` permits `[Layout(Transparent)]` on a compact Copy struct with
exactly one `bool`, `char16`, `int32`, `uint32`, `int64`, or `uint64` field.
The field retains its visibility and mutation rules; the nominal wrapper
does not implicitly convert to or from its carrier. Native Windows x64
storage and direct argument/result classification match that scalar carrier.
Semantic v35 persists the layout intent for both private and exported
structures; a replacement that changes the intent is incompatible, while
older non-transparent structures keep their layout and selected version.
This subset does not admit nested, owning or managed carriers, packing or
alignment overrides, or a general `Blittable`/public-field policy migration.
The scanner record and native profile/package format are unchanged.
Private compact Copy structs can provisionally select `[Layout(Sequential)]`.
Binding and semantic verification check private fields, nonpublic visibility,
natural declaration-order offsets and the existing 16-byte limit, including
eligible nested Copy values. The selection is recorded in source IR but not
serialized into public/module layout metadata: private module bodies retain
the same offsets and existing wire versions as Auto. It does not enable public
fields, frozen `Blittable` layout, packing, raw padding access or a separately
replaceable native aggregate ABI.
`[NoThrow]` on functions/constructors, `[DoesNotReturn]` on void functions and
`[Cleanup]` on eligible unique instance methods are currently bound;
unknown, duplicate and misplaced attributes diagnose rather
than becoming unchecked metadata. Parameterless attribute nodes retain their
brackets and name as three children; `Layout(Transparent)` and
`Layout(Sequential)` have a parenthesized
identifier argument. The `native-10` (fail-fast), `native-11`
(checked-exceptions) and `native-12` (compact unique owner) profiles support checked
signed/unsigned 32- and 64-bit integers, raw code units, conditional outputs,
fixed host-buffer capabilities and acyclic value layouts up to 16 bytes.
Fields are read-only after construction unless explicitly declared `mut`.
Binding and mandatory IR verification check mutation authority at every
nested field, independently of mutable local bindings and visibility.
Transitive module contracts and Windows x64 indirect argument/result storage
preserve those layouts. Statically rooted writable field paths enforce
initialization and visibility at every level. Larger aggregates, general
borrowed references and mutation through returned temporary values still
diagnose before emission.
`BoundFunction.IsNoThrow` is an explicit declaration guarantee checked by the
mandatory verifier, not inferred from a body for consumers. `TRM3017` locates
an operation whose throwing effect violates that bound. The bounded proof
accepts comparisons, bitwise operations, fields, total numeric conversions
and calls to other declared NoThrow functions. A host-buffer access is
NoThrow only with a statically constant index proven within that exact
compilation grant. Before verification, binding folds already-typed constant
integer arithmetic and bitwise operations, unary plus/negation/complement,
and supported integer/code-unit conversions when each intermediate result is
representable. Matching constant integer/code-unit comparisons and built-in
boolean operations with constant operands produce boolean constants usable by
the existing `require` and control-flow checks. Folding preserves the expression's type and source span; an
overflow, zero divisor or out-of-range conversion remains an operation with
its original runtime failure behavior. Calls, variable/field reads and buffer
contents are not evaluated or discarded, even behind a constant short-circuit
operand. This normalization is unconditional,
not an optional optimization or a general constant evaluator.
The verifier still conservatively rejects remaining checked arithmetic,
potentially failing conversions and general buffer indexes without range or
invariant inference. Its rules and persisted formats are unchanged: folded
source expressions use existing constant records, while externally constructed
or persisted arithmetic must independently satisfy the existing verifier.
After verification, native instruction selection replaces unsigned 32- and
64-bit division/remainder by a nonzero constant power of two with logical
shifts/masks, and signed division/remainder by a positive constant power of two
with a sign-corrected quotient and derived remainder. Both preserve numerator
evaluation and failures. Unlike binding normalization,
this shared AOT/JIT optimization does not establish additional source facts or
change `NoThrow` acceptance.
The [native contract](native.md) describes
these limits. `BoundFunction.DoesNotReturn` separately removes a call's normal
continuation. Its implementation must have no reachable return or fallthrough
(`TRM3018`). Constant-true loops, statically false requirements and declared
nonreturning calls can establish that proof. Unreachable syntax and IR still
undergo type and initialization checks; an unreachable return is not evidence
of normal completion. `BoundTryFinally` retains both regions, verifies NoThrow
cleanup independently of the function's own effect, and runs it on normal
fallthrough, returns and exiting loop transfers, inside-out. `TRM3019` rejects
returns and transfers escaping cleanup; a loop entirely inside cleanup may
exit locally. `TRM3020` conservatively rejects potentially executing cleanup
loops other than `while (true) { break; }`, and reachable `DoesNotReturn`
calls. This is not a termination proof for other NoThrow
callees. Return values are
snapshotted before cleanup; bool/out publication is checked after all cleanup.
Fail-fast checked failures do not unwind or run cleanup. Catch and other
effect annotations remain unsupported.
Source return diagnostics use that same mandatory continuation analysis,
including boolean negation and short-circuit constant feasibility; the binder
does not maintain a second, narrower return-path proof.
Existing single-source compiler JSON remains schema 1;
scanner and multi-source diagnostic schemas are versioned separately below.

## Public syntax API

Reference `sources/Terrum.CodeAnalysis/Terrum.CodeAnalysis.csproj`. Public APIs are
currently experimental; tools should pin the compiler and check schema/language
versions. No `InternalsVisibleTo` is used by tests.

```csharp
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;

SyntaxTree tree = SyntaxTree.Parse(
    SourceText.From("void Main() { return; }", "main.terrum"));

foreach (SyntaxNode node in tree.Root.DescendantNodes())
{
    Console.WriteLine($"{node.Kind}: {node.Span.Start}..{node.Span.End}");
}

string original = tree.Root.ToFullString();
SyntaxTree edited = tree.WithChanges(tree.ContentId,
    [new TextChange(new TextSpan(5, 4), "Run")]);
```

`SyntaxTree.Parse(LexResult)` accepts a completed lexical snapshot, including
one produced by the compiled Terrum scanner. It validates the exact source
partition, token/trivia text, EOF and diagnostic shape before invoking the same
parser; it does not invoke the host lexer. The supplied component remains
responsible for correct classification. Normal source-based parsing uses its
own trusted lexer output without repeating this boundary validation.
`SyntaxTree.Parse(LexResult, ImmutableArray<ScannerIntegerValue>)` additionally
requires complete, ordered integer records for that stream. Binding uses their
magnitudes rather than decoding digits again, including signed-minimum operands.
Coverage and tag validation do not prove a scanner's arithmetic; the component
must be trusted, with differential fixtures checking its numeric behavior.
Raw token text, source identity and lossless reconstruction remain unchanged.
The executed `ManagedScanner` self-host candidate can instead copy a successful
compiled scanner run's token triples into a fresh `ManagedTokenChain` of at most
64 tokens including EOF. It validates ranges against the original UTF-16
source, rejects malformed records and lexical diagnostics, and publishes no
chain on failure. AOT/JIT fixtures compare host token kinds and spans, repeated
scans, and collection after clearing links under both runtime policies. This
does not alter scanner records or make the bounded chain the parser's token
storage. Indexed and sequential cursor reads also offer `init ref<Token>`
publication beside their existing `out` forms. Failed indexes, end-of-chain
and invalidated cursors leave the destination vacant; copied tokens remain
valid after their source links are collected.

The bounded [Terrum-authored scalar frontend](../self-host/ScalarFrontend.terrum)
consumes one successful scanner token snapshot and publishes an in-process
typed result: parameter types and names, expression operators and parameter
references, optional conditional branches, declared `public`/`internal` visibility, and UTF-16 spans. It accepts one
function with zero to three scalar parameters, parameter references, Boolean
literals, unary `!` over a Boolean literal or named parameter (including grouped operands), unary `~`
over a contextual integer literal or named integer parameter (including grouped
operands), and unary `+` over a contextual integer literal or named parameter
(including grouped operands),
and signed `-` over a named integer parameter or grouped contextual integer
literal (with grouped named operands),
context-typed unsuffixed integer literals (including signed unary minus and
decimal signed minima) in return expressions, and
at most one binary operation per expression, including either order of
parameter/literal arithmetic or bitwise operations for integer widths
(including parentheses between a literal's unary sign and magnitude),
comparison for integer widths and `char16`, and Boolean parameter/parameter,
parameter/literal, or literal/literal equality, bitwise and short-circuit operations in either operand
order (with grouped named or literal operands), grouped unary `!` around one
two-parameter Boolean binary expression, plus two contextual 32-bit integer
literals in arithmetic or bitwise operations and whole-expression grouping
(including nested parentheses); an `if` branch returning a value may have
either an `else` return or a subsequent fallthrough return;
unsupported or invalid shapes return a code and span without a partial
artifact. Lexical failures remain scanner diagnostics. AOT/JIT execute this
phase with the host source buffers, but `build-component` still passes
scanner records to the hosted parser and binder. Connecting the native phase
to that compilation handoff remains open. A separate
[`ScalarFrontendProjection`](../self-host/ScalarFrontendProjection.terrum)
can publish its typed result through a fixed 392-word host buffer. The v20
tree layout occupies words 37–297 after the original header. Version 21 adds
statement/local counts at 298–299, eight four-word local records at 300–331
and twelve five-word statement records at 332–391. The host validates local
source names, declaration order, initializer types, live slots and mutability
before lowering ordered statements. Versions 22/23 add unary tree operation
83 for checked conversions, respectively without/with local records; its
type is the destination and its left child supplies the source value.
Untyped literal subtrees must use that destination as their context.
Earlier versions reject conversion nodes and zero their unused
extensions. Versions 1–18 retain their original 34-word payload with
three zero header-extension words.
Versions 24/25 add conditional operation 84 without/with local records;
versions 26/27 additionally permit conversion operation 83. A conditional's
left/right fields identify its condition/true-arm nodes, its low word
identifies the false-arm node, and its high word remains zero. All three
children precede the parent and participate in source, type, depth and
single-use validation. Only these new versions interpret operation 84's low
word as a child; earlier profiles reject the operation.
Versions 28–31 add statement kind 3 for a while header, followed by its body
records in preorder. Its `slot` field is the exclusive body-end record index,
its root is the Boolean condition, and its span covers the whole loop.
Nested body endpoints cannot cross an enclosing endpoint. Source validation
derives the braced body span rather than trusting an extra payload field.
Earlier versions reject this statement kind and retain variable-slot meaning
for their declaration/assignment records.
Versions 32–35 require branches without loops; versions 36–39 require both.
Within either group, +1 selects conversions and +2 selects conditional trees.
Kind 4 carries the Boolean condition root, whole-if span and exclusive
true-arm end index. An optional kind-5 record at that index carries root -1,
the else keyword/body span and the exclusive else-arm end. Body records
follow their header/marker in preorder. Both arms require braces; nested
endpoints cannot cross the enclosing arm, and an else marker cannot stand
alone. The reader derives both braced spans from independently checked
source tokens. Older versions reject these kinds; v1–31 payloads are unchanged.
Version 19 assigns words 34–36 to the third parameter's type and UTF-16
name span. Its parameter/literal operations reserve the disjoint 40000 offset
for the third parameter (using the first-parameter group before adding the
offset), including supported signed literal and grouped unary wrappers;
parameter/parameter operations retain token operator and operand indexes.
Version 3
encodes a parameter index and operand order in parameter/literal operations
and carries both literal magnitude words in the operand slots; two-literal
32-bit operations use one slot per magnitude. Version 4 distinguishes a
fallthrough return from an `else` return without changing the buffer size. The
version-5 operation offset distinguishes unary minus on either parameter/literal
operand (signed integer types only), preserving contextual decimal minima;
version 6 similarly distinguishes unary plus on integer literal operands.
Version 7 identifies standalone grouped signed-minus literals without
granting grouped decimal signed-minimum magnitudes. Version 8 identifies
negated, grouped two-parameter Boolean operations without a general nested
expression tree. The
`ScalarFrontendHost` reader bounds types, parameter indexes, operations and
source spans, independently checks projected integer bits against source
spelling and destination range, and checks that expressions occupy the complete
declared return/conditional body without unaccounted statements. It rejects
unpublished or malformed output. Its bounded
`BindLibrary` path lowers validated native facts directly to a
verifier-checked `BoundProgram` without invoking the hosted parser or binder
pass; signed unary literals reuse the scalar constant-folding helper;
visibility remains private unless declared `public` (including explicit `internal`). Native AOT/JIT execute
the program with test-local callers under both failure policies. This is an
in-process bridge, not a standalone compiler or persisted native bootstrap
artifact. The native compiler-primitives DLL does not include the frontend
because its token and `Host.Source` dependencies still require a source
adapter. `terrumc frontend-component self-host <input|-> --runtime fail-fast --json`
projects typed facts or scanner/shape errors for inspection. Compilation
errors in the bundled frontend sources use source-located multi-file
diagnostics rather than being attributed to the input or first component.
`frontend-build-library` uses those native facts to bind and emit an
explicitly public scalar DLL, preserving an existing output on failure;
`frontend-build` similarly emits a Windows x64 executable for a zero-parameter
`int32 Main()` (including a private declaration) through the hosted native
emitter. It rejects other entry signatures before replacing output;
`frontend-run` JIT-executes that same bounded entry in-process without
writing a PE file, returning its raw exit code with no success envelope;
the hosted compiler remains in parallel for general builds. This bounded
result is a provisional component protocol, not approved TRRM metadata or a
compiler self-build. Two hundred and thirty-seven shared scalar/conditional/Boolean/integer-literal sources produce
byte-identical DLLs through the Terrum-authored frontend and hosted compiler
under both runtime policies; this does not establish parity for unsupported
source shapes, independent native emitters or a self-build. Thirty-one bounded
zero-parameter `Main` shapes also produce byte-identical, executable images
through the same hosted emitter under both policies.

The separate [integer-parsing candidate](../self-host/IntegerParsing.terrum)
uses the immutable host source grant and checked wide-radix accumulation to
parse strict ASCII slices into signed or unsigned 32- and 64-bit outputs.
Signed and unsigned 32-bit paths bound each accumulated digit to the
destination range rather than first parsing a 64-bit value. AOT/JIT fixtures
cover every radix, both widths and both failure policies; malformed slices,
invalid bounds and overflow leave outputs unpublished. Four `init ref` entry
points reuse the existing `out` parsing paths, preserving the original APIs
and adding vacancy-only publication for accepted values. It is not wired into
`SyntaxTree.Parse(LexResult)` or the scanner-backed integer record: that
record still supplies the binder's contextual literal magnitude.

The [checked integer-conversion candidate](../self-host/IntegerCheckedConversions.terrum)
provides all twelve cross-type signed/unsigned 32-/64-bit pairs as named
`bool`/`init ref` functions. A value outside the destination range returns
false without publishing; successful conversions preserve its numeric value.
Four checked narrowings to `char16` retain vacancy on out-of-range input.
Four `char16`-to-integer widenings always return true and publish the unchanged
code-unit value, including surrogates; they do not validate Unicode scalars.
Independent semantic-module consumers execute the boundaries under AOT/JIT
and both failure policies. These provisional APIs do not replace the existing
throwing checked `T(value)` conversions, select witness policy or add syntax.

The separate [integer-saturation candidate](../self-host/IntegerSaturation.terrum)
provides all twelve explicit cross-type signed/unsigned 32-/64-bit integer
conversion functions with destination-range clamping where required.
Four additional `char16`-to-integer widenings preserve every code unit,
including surrogate values, without a Unicode scalar interpretation.
AOT/JIT exercise bounds and in-range values under both failure policies,
including a separately compiled module consumer of the published functions.
This does not add saturating conversion syntax, generic conversion witnesses
or a change to checked `T(value)` conversions.

The [integer-wrapping candidate](../self-host/IntegerWrapping.terrum)
provides all twelve cross-type 32-/64-bit signedness and width conversions that
retain destination-width low bits, rather than applying checked range
semantics. Four additional `char16`-to-integer widenings preserve every
code unit, including surrogate values. AOT/JIT compare against host bit-pattern
results under both policies, including an independent compiled-module consumer. This does not
introduce `unchecked` source syntax or change existing type-call conversion.

The [StringLiteral candidate](../self-host/StringLiteral.terrum) decodes
validated quoted spelling from the immutable source grant. Its ASCII and
ISO-8859-1 (Latin-1) length queries return false for unrepresentable scalars;
their owned-buffer encoders fail rather than substitute, with exact-capacity
and cleanup coverage under native execution. Explicit UTF-16BE and UTF-32BE
byte encoders share the existing endian-independent length queries and emit
no byte-order mark, with host/native parity and undersized-storage coverage.
Latin-1 is an explicit library option, not a `StringRow` metadata selector
or a default runtime encoding.

`SourceText` owns exact input text and a diagnostic path label. All library
offsets, line characters and lengths are UTF-16 units, not scalar/grapheme/display
columns; ranges are half-open. Lines and characters are zero-based. Only human
CLI diagnostics convert to one-based positions. Invalid UTF-16 is retained and
diagnosed by the library; file/stdin decoding is strict UTF-8 and rejects invalid
bytes before parsing. A UTF-8 BOM is transport metadata, not a source character.
`SourceText.TryGetOffset` reverses `GetLinePosition` for every valid offset,
including line-break code units; the start of the next line belongs only to
that next line, and end-of-source belongs only to the final line. Its behavior
matches the bounded self-hosted `SourceLineIndex` without changing scanner
records or source storage.

`SyntaxTree` owns source, lexical tokens, root and diagnostics. Nodes/tokens are
immutable; trees hold no mutable global tables, symbol state or parent pointers.
`SyntaxElement` distinguishes nodes from token leaves. `Children` preserves
source order including punctuation. `Span` excludes outer trivia; `FullSpan`
includes it. Trivia belongs to the following token; EOF owns trailing trivia.
Recovery introduces zero-width, empty-text missing tokens or explicit skipped
nodes, never discards input. `ToFullString` reconstructs from leaves rather than
returning the original string. `Tokens` is the lexical stream; the root traversal
also includes parser-inserted tokens.

The lexer reserves `_` as a distinct token; it never binds as an identifier.
Only standalone `_ = expression;` is a discard statement, evaluating once and
cleaning an owning result at statement exit through existing local/cleanup IR.
Wildcard patterns, generic type holes and lambda placeholders remain unsupported.

This compact kind-based CST avoids two divergent parser models. It lacks Roslyn's
typed convenience wrappers and structural reuse today. Add generated typed
accessors over the same slots as grammar stabilizes, not a separate lossy AST
for AI clients. Syntax nodes are owned by one snapshot; references/JSON indices
are not stable symbol identities.

`WithChanges` takes an expected `ContentId` and ordered, nonoverlapping original
source ranges. It rejects stale content, duplicate-position insertions, overlap
and out-of-range edits before exposing a result. It returns a full reparse and
does not mutate a workspace or write a file. Content IDs hash exact UTF-16LE
code units using SHA-256, including unpaired surrogates; they exclude the path
and are not project/semantic identities or revision counters. A future workspace
must also compare document identity and its own monotonically increasing
revision before atomically publishing edits. The current API prevents applying
old offsets to different content; it does not resolve concurrent edits.

Cancellation is checked during lexing/parsing and edit application. Recursive
grammar nesting is limited to 128 active parser frames, with `TRM1004` on excess;
this counts implementation frames rather than language blocks. On this failure
the root retains all tokens in one skipped node. Long left-associated trees and
public traversal use iteration. Source length remains bounded by .NET strings
and signed 32-bit ranges. No performance claims or unmeasured latency budgets
are attached to this first implementation.

## Human and machine protocol

```powershell
dotnet run --project sources\terrumc -- check samples\syntax\hello.terrum
dotnet run --project sources\terrumc -- tokens samples\syntax\control-flow.terrum --json
dotnet run --project sources\terrumc -- tree samples\syntax\hello.terrum --json
dotnet run --project sources\terrumc -- analyze samples\native\factorial.terrum --json
dotnet run --project sources\terrumc -- build samples\native\factorial.terrum -o artifacts\factorial.exe --runtime fail-fast --json
```

Use the built executable or `dotnet artifacts\bin\sources\terrumc\Debug\net11.0\terrumc.dll ...`
when stdout must contain only protocol output; `dotnet run` may print build
messages. `-` reads stdin. Commands exit 0 for no relevant errors, 1 for source
or compilation diagnostics, and 2 for usage/input/output failures. `tokens` performs only lexical
analysis. Human diagnostics go to stderr; JSON writes one object to stdout,
including failures. These inspection/build commands do not execute source.
`jit`, `scan-component` and `run-module` are explicit execution workers;
clients must supervise them with deadlines. JIT/module execution returns the
program's raw status, not the compilation-command status convention.
`run-native` validates its persisted consumer/producer packages and supervises
a separate native child with a 30-second deadline. It likewise returns the
program's raw status; a deadline failure reports `TRM5004`.

Schema 1 publishes `languageVersion`, coordinate system, path, length, success
and diagnostics. Parsed commands also include the content ID. Diagnostics have
stable codes, severity, message and source span/range; usage/input diagnostics
have no fictitious source position. Tokens include exact text, full/ordinary
spans, missing state and structured trivia. Tree output uses a flat node table,
root index and child references to node/token indices. This avoids JSON depth
limits on long expression chains and permits targeted consumers to index nodes
without recursively decoding source-sized nested objects.

Messages are human-facing and not stable dispatch keys. `TRM0xxx` denotes lexical
errors, `TRM1xxx` parsing, `TRM2xxx` CLI I/O/usage, `TRM3xxx` semantic
analysis, `TRM4xxx` native emission/runtime-host failures, and `TRM5xxx` bounded
module/scanner interchange errors. Schema changes require
versioning. Clients must reject unknown versions or explicitly negotiate them,
not infer meaning from enum ordinals or diagnostic English.

## Architecture evolution

The next front-end boundary is an immutable `Compilation` over source snapshots,
parse options, module/reference identities and expanded public contracts. A
semantic model resolves names/types/places and exposes structured queries:
symbol at range, required/proven obligations, call target, initialization and
escape facts, and why a diagnostic applies. Syntax is usable without binding.
The initial `Semantics.Compilation` already binds one syntax snapshot to
source-mapped, typed `BoundProgram` operations or diagnostics, never a partially
valid executable. It is not yet the multi-module semantic query service.
The multi-source overload declares all files before binding bodies, rejects
ambiguous entry points, and exposes `SyntaxTrees` and source-associated
`SourceDiagnostics`. Consumers must not interpret every multi-source diagnostic
against the legacy first `SyntaxTree` property.
Partial/error semantics must distinguish unknown, invalid and proven states;
absence of a diagnostic is not a safety proof.

Human/IDE and AI tools should consume the same model. A useful analysis response
includes snapshot identity, target/options, dependency identities, fact
provenance and related ranges, not merely generated explanatory prose. Edits and
refactorings return candidate edit batches and checked preconditions; callers
choose whether to publish. Semantic edits must recheck affected contracts and
diagnostics on the resulting compilation. Source comments or generated text do
not grant execution, filesystem, network or private-member privileges.

Generated source needs a stable document identity, producer/version/input
digest, authorization and a map from generated ranges to zero/one/multiple
original ranges. Preserve spelling and expansion locations separately rather
than forcing a lossy single-range mapping. Diagnostics may carry related
locations, rule arguments and suggested fixes tied to the same snapshot.
None of this source-map/semantic service is implemented yet.

Analyzer and generator APIs will be versioned consumers of snapshots, not
callbacks allowed to mutate compiler internals. Pin dependencies and input
manifests; isolate execution with explicit capabilities, cancellation and
resource bounds. Author-approved generation may access declared implementation
scope; consumer analyzers do not inherit those rights. Deterministic generation
requires explicit inputs, not hidden clock/environment/network reads. An
in-process interface alone would not be a security boundary.

Delay plugin discovery, persistent caches, remote services and query scheduling
until actual usage supplies requirements. Content identity already enables
safe exchanges; a later query cache additionally needs compiler/schema versions,
target, references, contract-rule version and authorization context. Changes in
serviced library bodies must not silently change a declaration-derived public
lifetime contract or a source program's validity.

## Executable TRRM scalar interchange

`TrRmExecutableCompiler.Emit(BoundProgram, moduleIdentity)` produces actual
approved `(1, 0)` metadata and compact-linear or compact-indexed Body bytes,
separately from the development `.trm` semantic-module encoding. The additive CLI path is:

```text
terrumc compile-trrm <source|-> --identity <name> -o <artifact.trrm> --runtime fail-fast [--library] [--source <path>] [--json]
terrumc build-trrm <artifact.trrm> -o <output.exe> --runtime fail-fast [--json]
terrumc build-trrm-library <artifact.trrm> --export <name> [--export <name>] -o <output.dll> --runtime fail-fast [--json]
terrumc run-trrm <artifact.trrm> --runtime fail-fast [--json]
```

Compilation uses the ordinary source binder and verifier, then the bounded
TRRM emitter. Loading uses only artifact bytes, never producer source or a
private semantic payload. `Load` checks framing and metadata, scans every
Body, audits the supported operations and declared effects, and reconstructs
typed immutable value definitions for the shared mandatory verifier and x64
backend. There is no producer-trust bypass or success fallback. JIT execution
returns the raw `Main` status; `--json` affects its diagnostics, not program
results. Failed compilation/loading leaves an existing output unchanged.
Input/output failures use status 2; invalid source or unsupported/malformed
executable contracts use status 1.

Scalar Body and indexed graph fields use canonical little-endian 1/2/4/8-byte
encodings. Each written byte selects its low octet explicitly, so full-width
magnitudes, high-bit payloads and multi-byte value/index fields remain valid
under the checked C# build policy. Internal validated widths still select
low-width two's-complement bits; this is not a new unsigned range check.
The String table's validated u24 unit count uses the same octet selection.
Minimal widths, tags, type identities, readers/auditors, malformed/truncated
input rejection, resource limits and every persisted/native format remain
unchanged. The TRRM runtime contract remains fail-fast only; checked-exception
source peers use the existing backend, not a new TRRM runtime policy.

The current envelope is one self-contained Windows x64 Module
(`targetRequirement=1`, `runtimePolicy=0`), with module-level scalar functions
and by-value parameters. Supported types are `bool`, `char16`, `int32`,
`uint32`, `int64`, `uint64`, and a `void` result. An executable entry must be a
local parameterless `int32` function; `--library` produces a no-entry module.
Every public function has one Export key equal to its full callable name;
private/internal visibility is not promoted, and arbitrary key aliases are
not supported by this loader. DLL exports use the existing scalar native ABI.
Independent native consumers do not imply TRRM Import/ModuleRef linking.

Initialized scalar locals, ordinary local reassignment, scalar discards,
and returns lower to immutable value IDs. Reassignment changes the
current source-slot mapping, not a serialized place; prior reads retain their
snapshots. Every operation is materialized once, in source order, even when
its result is discarded or reused. Lazy conditional expressions and Boolean
short-circuit operators, `if`/`else`, `while`, `do` and scoped `for` loops,
nearest `break`/`continue` and early returns use indexed basic blocks.
Assignments carry their current values through typed block parameters;
edge arguments are copied simultaneously, including cyclic loop-carried
copies. Native lowering emits direct branches, not a block-ID interpreter.
Inactive arms do not execute their operations or checked failures.

Post-tested loops enter a body header before testing their condition.
For initializers run once in the header's lexical scope; the condition runs
before each body, and the iterator runs after normal body completion or nearest
`continue`, but not after `break` or return. A distinct condition/iterator
continuation carries target-visible snapshots from every incoming body edge.
For forms already lowered with an appended iterator remain supported.
These are ordinary indexed loop regions: no new region kind, instruction,
flag or wire version distinguishes the source loop syntax.
The binder resolves the existing one-based target depth at compile time; no
label identity or runtime target-depth field is serialized. Non-nearest named
`break` and `continue` lower to scalar routing codes and equality branches
threaded through existing block parameters and designated single-exit loop
regions. Arguments carry values captured at the transfer, not loop-entry
snapshots; each exited scope progressively retires its local identities.
Intervening statements and exited loop iterators or `do` conditions are
skipped. The selected `continue` reaches its target header, iterator or
post-test condition exactly once; selected `break` bypasses it. Nearest
transfers retain their existing graph representation and add no routing state,
parameters, operations or blocks. Routing values, comparisons and parameters
consume the existing 256-value and 100,000-visit analysis budgets. Edges that
bypass a designated region exit remain unsupported; no new wire
representation, labels, opcodes, region kinds, widths, versions or ABI are
added.

Initialized locals of all six scalar types are supported in lexical blocks,
branch arms and loop bodies. Monotonic source variable identities remain
separate from current SSA values and lexical membership: leaving a block
retires its local mappings but retains outer mutations. Merge, header,
backedge and exit arguments carry only the identities visible at the target;
there are no invented values for declarations not yet initialized. Loop-local
initializers execute on each entered iteration. Return values and previously
evaluated call arguments retain their immutable snapshots after scope exit.
Closed scopes may reuse spelling under the hosted binder's existing rules.
These Copy-only scopes erase into existing plain/loop flow: reserved lexical
region kind `02` is not executable, and no place or cleanup protocol is added.

The four integer primitives also support retained `<<`, `>>`, `>>>`,
all eleven primitive compound-assignment statements, and prefix/postfix
`++`/`--` on initialized writable named locals. These select indexed emission
even without source branches. Updates capture the old immutable value before
evaluating the RHS once; only successful computation supplies the replacement
slot mapping. Prefix results select the replacement, postfix results the old
value. Earlier RHS effects remain ordered, including `x += x++`, conditional
RHS expressions and mutations in loop conditions.

Source shift counts remain `int32`. The emitter uses `Convert` policy 3 to
preserve their bits as the ISA's `uint32` counts, then `CheckedBinary` kind 6
or 7 enforces the original width bound. Negative counts therefore fail rather
than wrap or become masked counts. Signed logical right shift reinterprets
the payload to the same-width unsigned type, applies kind 7, and reinterprets
the result back. There is no kind 8 or checked numeric cast of a negative
payload. Policy 3 accepts only equal-width integer representations, never
`bool`, `char16`, optional or nominal values.

Module-local direct calls use assigned `Call` opcode `0x12`, selecting the
existing MethodDef ordinal and exact scalar by-value signature. Forward and
finite recursive calls, scalar/void results, and the existing register/stack
argument ABI compose with mutations, lazy expressions, branches and loops.
Arguments are materialized once, left-to-right, so later argument mutations or
conditional continuations cannot change earlier snapshots. Void calls retain
their operation ordinal and effects without allocating an SSA value.
Value definitions and last-use ordinals therefore advance independently.

Loading audits block extents, definition indices, value types, exact use and
liveness inventories, dominance, region entry/exit and backedges before
creating an immutable internal graph. The mandatory semantic verifier
rechecks that graph's parameter/result/effect contract against its containing
declaration and every call target's current signature, mode and effect.
A declaration prepass resolves forward targets; all declared Bodies, even
uncalled ones, are audited before native execution. Resultless operations and
their argument checks still consume the shared analysis-visit budget.
Independently supplied linear Bodies with calls, shifts or policy-3
conversions use the same internal graph; other linear Bodies retain their
existing structured lowering. The internal representation-conversion node
does not change checked source casts or add a development-module wire tag.
The loader accepts `ConstBits`,
`ConstSigned`, `Copy`, Boolean NOT, integer bitwise NOT/unary plus,
scalar equality/inequality, integer ordering/bitwise operations, checked
integer arithmetic/negation/count shifts, checked numeric conversion,
equal-width integer bit reinterpretation, module-local scalar `Call`, `Branch`,
`BranchIf` and `Return`.
Integer division/remainder retain their distinct signed-minimum behavior.
`char16` has conversion/equality semantics, not integer arithmetic or ordering.
Wrapping/saturating conversion, optional values, eager
`Select`, combined division/remainder and `Fail` are outside this bounded
executable implementation even where the approved ISA assigns them.

Body and instruction widths are emitted minimally; loading performs a strict
minimal-width audit. Source binding may fold an expression to a constant
before this boundary; only the supported retained operations are executable.
Required constant evaluation and body static proofs are
discharged by the binder. Compilation-local `[ConstEval]` eligibility is not a published
TRRM contract. A retained `NoThrow` promise is encoded as an Effects clause
and audited against every operation: the TRRM subset supplies no proof rule
that would excuse a retained checked operation. Other effect promises and
contract clauses reject, as do unsupported sections, parameter modes,
generic/nominal declarations and qualified types.

Calls to imported or packed cross-module targets, nominal/instance and
generic/witness calls, and non-value/owning parameters remain unsupported.
A `NoThrow` caller requires a declared and audited `NoThrow` callee; the
runtime policy does not manufacture that guarantee.
Vacant locals, direct edges that bypass designated exits, field/nominal mutations,
places, roots, ownership, cleanup, exceptional transfer and imported
declarations are not implemented here. Indexed loop emission requires a
reachable backedge and a designated exit; it does not synthesize a backedge
for a source loop whose body always transfers out. A `do` body that always
returns or breaks, for example, rejects rather than retaining an unreachable
condition block; acyclic source loops are not erased into plain regions by
this emitter. All retained inactive operations and every function Body still
undergo mandatory validation. Source scope support is
an implementation boundary, not an unresolved language contract.
Loaded indexed graphs execute but cannot be re-emitted through the structured
source emitter or serialized as development semantic-module IR.
Checked-exception source or execution
requests reject rather than relabeling a fail-fast artifact; the existing
source and development semantic/native commands still support both policies.
No new protected Body encoding, native ABI or public profile was invented.
The existing verifier bounds apply, including 1,024 functions, 64 parameters
per function and 256 materialized scalar values per function; method parameters,
operation results and block parameters share that last bound. Indexed
dominance auditing also uses the existing 100,000-visit analysis bound.
These are implementation limits, not new normative format limits.

Independent handwritten metadata/Body fixtures, malformed IDs/types/effects,
reserved Contract flags, handwritten merges and cyclic backedge copies,
source-free process loading, actual scalar DLL exports, exact persisted
EXE/DLL byte parity, inactive-arm behavior and ordered checked failures
exercise this boundary. The structural
`validate-trrm-*` commands keep their narrower inspection contracts; their
success alone never authorizes execution.

## Binding and backend path

Keep diagnostics and source mapping through syntax -> bound operations ->
validated structured Terrum operations -> target lowering. The semantic layer
must represent values versus places, copy/move/borrow, initialization and
conditional publication, blocks/edges/block arguments, exceptional cleanup,
regions, loops and suspension. Mandatory validation checks terminators,
dominance, joins, region entry, cleanup and lifetime contracts. Irreducible flow
is represented honestly, not disguised as one natural loop. Cached analyses
are optional/versioned; validating structure is not optional.

TRRM identifies the evolving Terrum IL/module format, not a CLI assembly or a
PE file. The [approved current metadata design](il-metadata.md)
specifies candidate `(1, 0)` framing and rows; it is not the implemented
semantic-module encoding. Implemented semantic revisions are pre-1.0
development revisions, not a stable 1.0 specification. Host-side candidate
readers validate `(1, 0)` header and directory framing. The index enumerates
validated directory entries with their
policy flags and borrowed payloads, allowing tools to preserve optional
nonsemantic sections without guessing kinds or dropping reference-slot policy.
For optional policy-2 sections, a bounded slot rewriter preserves opaque
payload bytes while replacing the complete slot array. Its artifact-level
operation validates both the original and recomposed framing and target rows
before returning new bytes; section-only rewriting requires that validation
at composition. A tool must establish Body
instruction identity separately before remapping its locations.
The same validated artifact boundary inserts a reference-slotted optional
section while preserving and rechecking the existing ordered entries; a
typed slot constructor supplies its prelude without hand-encoded offsets.
Both artifact-level rewrites drop unrecognized discard-on-rewrite policy-3
sections; policy-2 references to unchanged core rows retain their targets.
Table readers validate String
offsets and name rows, fixed Module rows and their partition cursors,
ModuleRef identities and co-located producer requirements, optional SourceFile
paths, nominal TypeDef ranges and FieldDef ownership, unique Layout row
ownership (not target ABI classification), and structural TypeSpec
forms and TypeArg ownership/ranges, plus structural MethodSig/Param ranges and
MethodDef module, signature and body ownership, PropertyDef getter
owner/result matching, and structural Import/Export ownership, visibility and
key uniqueness. GenericParam formal ordinals and Constraint row ownership,
flags and required types are checked structurally; unassigned kinds and
witnesses are not executable generic contracts. Contract row ownership,
singleton clauses and assigned payload shapes are also checked without proving
effects or making reserved clause modes executable. OutputState additionally
checks its Boolean return and `out`/`init ref` parameter ordinal, and Cleanup
requires a declared `NoThrow` effect on the same-type target method. These do not bind imported
contracts. A host-side
writer emits indexed String sections with required names or optional empty
namespace text; a fixed Module-row writer checks identities, partition
cursors, requirements and entry ownership against the supplied String table
and table counts; a ModuleRef-row writer checks producer names and matching
requirements for co-located modules, while retaining external references for
later binding. A SourceFile-row writer checks nonempty paths and Module
partitions without opening source files; a TypeDef-row writer checks
names, assigned flags and owned field ranges against the supplied tables.
A FieldDef-row writer checks TypeDef ownership, TypeSpec references,
field names, flags and explicit layout offsets against those rows. An indexed
TypeSpec-row writer validates form and operands against supplied table counts;
a set of typed payload constructors encodes the assigned built-in, reference,
constructed, wrapper and indexed-family forms before that validation;
a TypeArg-row writer checks constructed-type ownership, ordinals and references.
MethodSig-row and Param-row writers check disjoint signature ranges and
parameter ownership, ordinals, types and modes. A MethodDef-row writer checks
module, signature, contract and Body-row ownership without encoding instructions.
A Layout-row writer checks assigned target and unique published TypeDef
ownership; when Layout rows are available, fixed FieldDef offsets must fit
within the published size. Target-specific alignment, packing and ABI validity
remain outside structural validation.
A PropertyDef-row writer checks that its getter belongs to the owning TypeDef
and returns the declared TypeSpec, without inferring identity from its name.
GenericParam-row and Constraint-row writers check formal ownership, ordinals,
complete constraint ranges and required type references; unassigned kinds and
witness references remain structurally opaque, not executable contracts.
Import-row and Export-row writers check retained expected definitions and
public producer keys against local structural tables; they do not resolve a
producer or prove contract compatibility.
An offset-indexed Contract-row writer checks assigned clause shapes and
cross-table ownership, including cleanup's declared `NoThrow` target; it does
not prove effects or encode general executable contracts.
The self-host String-row
candidate emits individual rows. These table-level APIs exercise approved
metadata contracts, including a framed callable-table fixture; the executable
path above composes them with Body emission rather than replacing the legacy
semantic-module writer. A provisional structural
entry point composes the known table checks over one framed artifact without
claiming semantic import binding, target-layout validity or executable clauses.
`terrumc validate-trrm-structure <artifact> [--json]` exposes those same
framing and structural checks to tooling; success does not validate Body
instructions, imported contracts or an executable module.
It checks every String row's physical flags, count, terminator and padding,
including unreferenced rows; name/path/key references still validate decoded
text at their use sites. It checks Blob and Body offset indexes, including
empty Blob rows, and composes a two-module Import/Export fixture, but does not
interpret Blob contents or Body instructions. A Blob-table reader exposes
the validated row count and borrowed row bytes without interpreting them as
references or requiring nonempty payloads.
A Body-table reader likewise exposes the validated row count and nonempty row
bytes without decoding instructions or claiming that the Body is executable.
A separate compact-linear reader takes the owning MethodDef, MethodSig,
Param and TypeSpec rows and checks the fail-fast scalar subset of `ConstBits`,
`ConstSigned`, `Copy`, `Unary`, `Binary`, `Convert`, `Select`,
`MakeOptional`, `None`, `IsPresent`, `CheckedBinary`, `CheckedConvert`,
`CheckedNegate`, `CheckedDivRem`, static `Call`, `Return` and `Fail`.
Calls inspect authored local MethodDefs, packed direct references to public
global or public type-owned static MethodDefs in another included module with
a Body row, and retained imports of public global or public type-owned static
MethodDefs with an expected local definition and ModuleRef requirement.
External type-owned targets require a public, non-generic owner TypeDef.
Packed and imported calls require compatible runtime policy and no stronger
target requirement. Arguments and results (including void) are by-value scalar
or optional types. Receivers and generic methods reject; this does not
validate producer binding, effects or execution. Optional scalar
types retain their presence separately from their underlying atomic type;
`Return 0` is valid only for a declared `void` result.
It reconstructs parameter and operation
definitions, use-block counts and final uses from the one-block stream,
including mapped Boolean comparison results, rejecting unknown operations,
invalid IDs, types, modes, widths and trailing bytes.
This does not validate published effects, execute instructions, accept other
compact/general forms or make the structural CLI an executable Body verifier.
`terrumc validate-trrm-compact-linear <artifact> [--json]` opts into these
bounded checks for Body rows referenced by local MethodDefs after the same
framing and structural checks. Structural MethodDef validation requires every
Body row to have exactly one owner, so the opt-in pass inspects every Body row.
Its success does not validate published effects or executable contracts; the
structural command remains Body-opaque. When SourceMap is present, the opt-in
check also verifies that each instruction slot addresses the inferred sole
block and an existing operation or its terminator; it does not verify
source-file contents.
A separate compact-indexed reader checks the approved conditional-sum, merge
and bounded loop examples and their scalar subset:
root-free, fail-fast bodies with entry-header, sibling and nested loop regions,
each with a declared exit and reachable backedge;
`Branch` and `BranchIf` edges have checked typed block arguments. It accepts
scalar `ConstBits`,
`ConstSigned`, `Copy`, scalar `Unary`, `Binary`, integer `Convert`
(wrapping, saturating and equal-width bit reinterpretation), eager `Select`,
checked integer arithmetic/shifts, integer/`char16` `CheckedConvert`, signed `CheckedNegate`,
dual-result `CheckedDivRem`, static `Call` to global or type-owned local or
retained imported MethodDefs with scalar/optional parameters and result
(including void), `Switch`, `Return` and `Fail`. The bounded call inspection
rejects receiver/generic calls and generic owners for external members.
Packed direct MethodDef `DefId` calls require
an authored public global target or a public static member of a public
TypeDef in another included module with the same runtime policy and a
target requirement no stronger than the caller's; retained imports require a
public expected local definition and owner, plus compatible declared ModuleRef
requirements at each call. It checks signatures, not producer binding, call
effects or executable callee bodies.
Optional scalar values retain their presence-qualified identity through
`MakeOptional`, `None`, `IsPresent`, `Copy` and eager `Select`; they are not
interchangeable with the underlying scalar in operations or signatures.
`char16` and optional `char16` retain their code-unit identity; only canonical
`ConstBits`, `Copy`, equality, `Select` and optional presence operations apply,
plus checked range conversion between non-optional `char16` and integer values,
not integer arithmetic, ordering or policy `Convert`.
The indexed reader also accepts valid `F0` reference/data widths on those
operations and reference/count widths on supported `Branch`, `BranchIf`,
`Switch` and `Return` terminators and supported `Call` operations; `Switch`
patterns additionally have an independent data width. These widths are
independent of the Body-local width.
It checks type/definition/use indexes, operation and
terminator offsets and live-in/out inventories against the decoded blocks,
including the least live-value solution around loops. Entry-header, sibling
and nested loop regions are admitted with checked parent chains, designated
entry/exit boundaries and backedges; non-loop backedges and other assigned
indexed operations reject.
`terrumc validate-trrm-compact-indexed <artifact> [--json]` opts into this
subset across all owned Body rows after structural metadata checks; when
SourceMap is present it checks block and operation/terminator ordinals.
`terrumc validate-trrm-compact <artifact> [--json]` inspects mixed linear and
indexed rows using their respective bounded readers and location checks; it
rejects general and unsupported compact Bodies rather than assuming an
executable interpretation.
None of these compact commands is an executable Body verifier.
SourceMap rows check slots, spans and Body/SourceFile module
association. A bounded SourceMap writer emits those slots, reuses SourceFile slots
and rejects duplicate instruction locations; block/ordinal existence still
requires instruction-aware verification. A separate provisional artifact writer
frames caller-supplied sections under the `(1, 0)` directory and checks that
framing without changing the current semantic-module writer. An offset-indexed
writer emits opaque Blob rows (including empty rows) and nonempty Body rows,
but does not decode, generate or verify executable instructions.
Separate test-only fixtures independently decode approved general and compact
Body examples and check their indexes against the written bytes; they are not
an executable Body reader or an adopted metadata writer.
Historical-revision readers and feature-dependent writer
selection are temporary implementation support to remove before v1. That
cleanup must update writers and fixtures together and reject unsupported
revisions rather than reinterpret their bytes. Structural decoding, binding
and declared-contract checks remain required for supported revisions.

The current backend emits Windows x64 machine instructions and PE32+ images
directly, with no CIL/LLVM translation
or linker dependency. Source functions use a saved RBP frame base for
locals and spills, separate from the RSP-relative outgoing call area. Shared
PE/JIT unwind metadata describes both the saved register and allocation.
Cleanup has out-of-line funclets that borrow the source frame in RDX
while reserving their own call areas; they are invoked explicitly on normal
exits and by Windows on checked-policy exceptional exits. Versioned scope
records and filters admit only four compiler-generated checked failures.
`BoundProgram` retains structured blocks, branches,
loops, slot reads/writes and checked expressions; its verifier precedes
machine lowering. It is not a claim to implement the complete semantic IL.
Target-specific encodings, import fixups and unwind records live under `Native`,
not in binding or the public lossless syntax model.

An optional CIL/reference backend would need a concrete validation/deployment
benefit; it is not a prerequisite for native progress. The [JIT proof](jit.md)
reuses the encoder with W^X memory and dynamic unwind registration.
[Semantic modules](modules.md) persist verified bodies and normalized contracts
for bounded process-start replacement. Consumers bind against imported declarations
without producer source; private implementation layouts and helpers can evolve
without changing the persisted consumer. Native `.trmn` packages additionally
persist the EXE and DLL images, validate their contracts before launch, and
support compatible single-provider replacement without regenerating consumer
machine code. This is process-start servicing, not the general TRRM IL design or
hot replacement. Unsupported runtime/ABI capabilities diagnose rather than
inherit accidental CLR behavior.
Multi-source compilations are available through the API and repeatable
`--source` options on `analyze`, `build`, `build-library`, `jit`, `compile-module`
and `compile-native`.
Repeatable producer `--export` options select the module public surface.
Multi-source compiler JSON uses schema 2 with input-ordered snapshot identities,
diagnostic source indexes and, for successful builds, the native artifact.
Single-source and module-artifact response schemas remain unchanged.

## Design references and tradeoffs

These are specific prior art, not claims that another compiler lacks AI support
or that Terrum has already improved its performance or extensibility.

| Reference | Applied lesson and deliberate limit |
|---|---|
| [Roslyn syntax model](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/work-with-syntax) | Immutable full-fidelity nodes/tokens/trivia and explicit recovery are the shared tool surface. Start with a compact CST, not premature red/green reuse or a parallel AI AST. |
| [Roslyn incremental generator design](https://github.com/dotnet/roslyn/blob/main/docs/features/incremental-generators.md) | Declared immutable transform inputs enable caching. Defer pipeline APIs until we have binding and explicit generator authorization. |
| [rustc incremental queries](https://rustc-dev-guide.rust-lang.org/queries/incremental-compilation.html) | Cached results depend on actual query dependencies and their order, not just file timestamps. Don't serialize every cheap result or equate a source hash with semantic validity. |
| [Clang AST](https://clang.llvm.org/docs/IntroductionToTheClangAST.html) and [AST importer](https://clang.llvm.org/docs/InternalsManual.html#the-astimporter) | Retaining written syntax helps refactoring; semantic graphs and redeclaration/identity relationships are not interchangeable with a syntax tree. Keep our CST separate from later symbol graphs. |
| [LLVM language reference](https://llvm.org/docs/LangRef.html) | Explicit IR contracts and linkage distinctions matter. LLVM linkage is not Terrum lexical visibility, and this reference does not select LLVM as the backend. |
| [Runtime type loader design](https://github.com/dotnet/runtime/blob/bd6d336bfd95a333e8b3f22f26259bbb659974cf/docs/design/coreclr/botr/type-loader.md) | Type identity, metadata validation and loading phase affect compatibility and failure timing. Terrum contracts need an explicit binder/publication boundary instead of accidental JIT-time discovery. |

References were consulted on 2026-09-24; living design pages can change.
