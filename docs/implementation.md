# Compiler and tooling implementation

## Current boundary: syntax language version 45, semantic body features v43–v49, semantic-module revision 54, native-10 through native-20

Semantic body feature numbers are milestone labels, not persisted format
versions. Structured semantic-module writers and readers use revision 54 only;
older revisions are rejected, not normalized or upgraded.

Revision 54 provisionally supports top-level caller-unsafe function contracts
and calls inside explicit `unsafe { }` blocks in structured semantic modules.
Readers reconstruct call authorization from the persisted block scope rather
than trusting a serialized call flag. `unsafe(expr)`, unsafe fields, accessors
and operators remain outside this slice; executable TRRM continues to reject
caller-unsafe declarations and calls. This implementation does not approve the
broader reference/access metadata proposal.

Hosted binding accepts named arguments, including `out` and `init ref<T>`
destinations, for uniquely resolved non-intrinsic calls. Arguments retain
source evaluation order while each records its formal parameter position for
verification and native lowering. Semantic-module revision 54 persists that
mapping and lexical view endpoints with explicit end-relative modes, allowing
dynamic absolute and end-relative bounds to be represented without duplicate evaluation.
Source-free indexed TRRM evaluates mapped arguments in source order
and materializes supported by-value arguments in formal order before emitting
its positional call. Overload selection remains outside this support.

The normative declaration grammar additionally requires explicit primary
visibility, fixed role-specific ordering, primary/secondary partial contract
ownership, static-class containers, explicit struct/class union storage and
primary-only complete external callable bindings restricted to top-level
functions and static methods, plus separate static foreign-data bindings.
Internal interface requirements restrict implementation
authority; private members are complete helpers. Constructors have their own
synchronous initialization prefix and partial completion contracts. These
source contracts also distinguish field storage/referent authority and
caller-unsafe access, with synchronous accessor-local visibility/safety and
sole-primary property/indexer implementation. Static operator/conversion
prefixes additionally specify caller-safety, partial ownership and eligible
implicit-reference inputs; associated bindings use attributes/visibility only.
Strict source-unit file visibility and explicit same-module friend grants
also require semantic scope identity, access provenance and generic grant
matching that current syntax/semantic modules do not implement. The `(1, 0)`
metadata visibility bits and optional diagnostic SourceFile rows are not a
file/friend encoding.
The target static-storage contracts additionally define load-scoped groups,
explicit-policy `static T()` bodies, separately lazy fields, normal explicit
concurrency opt-in even for reads, blocking initialization entry, cyclic
completion failure and unsafe recovery/suppression obligations. Static
`init` authority ends at group completion. These contracts require new
binding, initialization-state, effect, publication, dependency, root/cleanup
and unload support; the current compiler does not implement them.
No existing `(1, 0)` metadata flag is a static-initialization policy encoding.
These are not new compiler
support: the current parser does not enforce the new prefix/partial rules or
implement unions, static containers, async declarations or foreign external
binding. Existing compiler declaration defaults and accepted modifier orders
retain their current behavior until adopted through binding, verification,
persistence and lowering.

Hosted source binding checks caller-unsafe callable and field declarations,
unsafe getter accessors, static operator protocol requirements and explicit
`unsafe { ... }`/`unsafe(expr)` scopes within one compilation. Generic proof
bodies retain the operator requirement and must explicitly authorize its use.
Field reads and writes, including constructor writes and nested paths, require
an explicit unsafe scope; accessor-local support is currently limited to
getters. Semantic-module revision 54 provisionally persists top-level
caller-unsafe function contracts and reconstructs call authorization only
inside persisted `BoundUnsafe` scopes. Caller-unsafe methods, accessors,
operators and fields remain unsupported in semantic modules; `unsafe(expr)`
authorization also remains same-compilation-only. Executable TRRM still
rejects caller-unsafe declarations and calls because its Body cannot prove
explicit call scope. Complete-program scalar projection v85 additionally
source-audits caller-unsafe top-level declarations and calls enclosed by
explicit `unsafe { ... }` blocks; it also audits `unsafe(expr)` call-site
scopes. Both remain backed by the hosted verifier/emitter.

Target type matching also preserves generic/concrete consistency, including
reference/pointer forms, without pattern-driven reference formation. Checked
runtime type captures require witnesses and supported generic execution.
General source catches/filters select incrementally after unwinding to each
candidate's live scope. These are not current compiler support:
current `is some`/whole-type inference and the bounded Windows checked-failure
dispatcher retain their existing behavior. Runtime witnesses, general typed
filters and their post-unwind phase still need binding, persistence, lifetime
verification and native integration.

### Current syntax and scanner profile

Hosted source compilation supports transparent aliases declared at file scope,
including under the existing file-scoped namespace form, across source
snapshots, with forward references and unused-cycle diagnostics. Generic
aliases substitute supported source type arguments without changing nominal
identity or dropping managed-handle access/optional qualification; arity is
checked and expansion is bounded by the existing 128-declaration semantic
nesting limit. This does not add generic structs/classes or broaden the
supported type system. A public alias may be used in ordinary source
compilation only when its expanded target is public; library emission rejects
public aliases because their metadata encoding remains open. Internal aliases
can be erased during library compilation. This support does not extend the
bounded scalar frontend or add alias declarations to source-free TRRM;
Terrum-authored scalar frontend components still use the hosted
verifier/emitter.

A single loop label and its `while`, `do` or `for` introducer must occupy the
same physical source line (`TRM1012`). The colon attaches to the label; trivia
after it cannot contain CR, LF, CRLF, NEL, line separator or paragraph
separator, including inside comments. Stacked labels are invalid. The host
lexer/parser, trusted prelexed input and compiled scanner enforce this rule.
Native scalar input checks the raw gap before parser selection; its
source-bound reader independently audits label spans and loop targets. Label
identity, active-duplicate rejection,
disjoint reuse, scopes, transfers and cleanup follow the contracts described
below.

The lexer treats each maximal contiguous ASCII punctuation run from
`!%&*+-./:<=>?^|~` as one candidate. Implemented spellings are classified as
such; other runs produce one lossless Bad token and whole-run `TRM0011`.
The grammar-defined `..`, `..^` and `?.` spellings are now single lexical
tokens; parser consumption and binding remain unsupported. Other defined but
unsupported spellings produce one Bad token with `TRM0012`. Comments, literals,
numeric exponents and keyword escapes retain priority; grouping/list
delimiters, commas and semicolons are outside this glyph set. A deferred
`>`/`?` candidate is certified only when the parser
consumes it as a generic closer or optional suffix, including split and
four-or-more-character closing runs. Missing or recovery tokens cannot certify
it. Repeated optional modifiers
retain `TRM1005`; they are not reclassified as punctuation runs.

Spacing is determined from raw source gaps before name lookup, without
role-changing recovery. Conditional `?`/`:` and struct-conformance or
generic-constraint colons require trivia on both sides (`TRM1008`). Loop-label
and named-argument colons attach to the preceding name (`TRM1010`); following
trivia is optional. Optional-type `?` attaches to the preceding type token
(`TRM1011`). Binary symbolic operators, declaration/assignment `=`, supported
compound assignments and `=>` require trivia on both sides (`TRM1008`);
prefix `+`, `-`, `!`, `~`, `++`, `--` attach to the following operand, and
postfix updates attach to the preceding token (`TRM1009`). Grouping interiors
remain permissive. Conditional operators, declaration operators, keyword
operators, `const(expression)` versus `const (expression)`, and optional types
retain their syntactic roles regardless of spacing.
Recovery and the CST retain every source unit; invalid spacing never selects a
different parse role.

Dot and opening-delimiter attachment are role-specific. Qualified type names,
namespace declarations and imports require the dot attached on both sides;
expression member access requires at least one side attached (`TRM1007`
identifies the original dot). For supported calls, indexing, generic
applications/type arguments, generic declaration parameters, callable,
constructor and operator parameters, and attribute arguments, the opening
delimiter attaches to the preceding receiver or name. A detached generic
application reports `TRM1006`;
it is not reinterpreted as a relational expression. Whitespace, line breaks
and comments break the applicable raw boundary, while permitted interiors and
ordinary grouping retain their existing trivia rules. Hosted, prelexed and
compiled-scanner parsing share these checks. The native scalar parser's raw
snapshot check scans contiguous punctuation records, including the first and
last tokens, before spacing checks and parser selection; it has no
type-context exemptions. Maintained scalar probes use it too. Its independent
source audit rejects records that contradict the source.

Named by-value, scalar `out`, and `init ref<T>` arguments bind for uniquely
resolved, non-intrinsic calls. Overload selection remains outside current
support.
The bounded native scalar frontend supports its documented named loop
transfers and direct scalar calls, but excludes optional and managed-handle
forms, general binding/imports, indexing, qualified types, namespaces and
imports. This differs from the hosted compiler's `native-20` profile, which
supports optional managed-handle patterns; see the
[native profile](native.md). String and format interiors are not reclassified.
The complete approved source grammar and unsupported forms remain in the
[grammar specification](grammar-spec.md).

The closed ordinal `@keyword` escape catalog is the vocabulary and contextual
table in grammar §2, including keywords for unsupported forms. Primitive
spellings, `_`, arbitrary names and legacy-only `out`, `foreach`, `abstract`
are not escapable. Escaping suppresses keyword recognition at that occurrence;
binding uses the underlying name while tokens, trivia, CST and diagnostic
coordinates retain the raw spelling. The scanner's `EscapeKeywords` catalog
is read-only and follows the existing nine grants; the frontend output grant
is index 10.

Ordinary and documentation comments use exact whole-run closing fences:
maximal opening star runs select the closer, documentation closers require
two slashes, and payloads are opaque and nonnesting. Overlong runs with the
required slash suffix report `TRM0010` over that run and suffix; recovery
keeps them inside the comment until an exact closer or EOF, which reports
`TRM0003`. Documentation line/block trivia use kinds 4/5. Scanner adapters
validate source-owned trivia and diagnostics without re-lexing native tokens
or recomputing magnitudes.

The current scanner is version 8/schema 8; it appends diagnostic kinds 11/12
for invalid/unsupported maximal punctuation runs to the prior diagnostic
catalog. Earlier raw records remain readable where compatible, while current
snapshot adapters reject forged framing, invalid escapes and conflicting
classification. The bounded scalar frontend's current v85 single-function projection uses a
392-word buffer and enforce the documented limits: 16,384 UTF-16 source units, 64 tokens,
eight total local identities including hidden discards, 12 statement records,
32 expression nodes, three parameters, 16 expression-recursion levels, six
structural levels and a shared 100,000-visit budget. Source-bound audits
require current syntax and reject every obsolete projection tag (v1–v84).
V85 also uses a separate complete-program buffer; the source/token limits remain
per snapshot, and the local/statement/expression/parameter limits apply per
function. Both v85 projections carry the caller-unsafe declaration bit and
source-audit calls against explicit unsafe-block spans and explicit
`unsafe(expr)` wrappers and audits their contained call scopes. Projection
v85 adds named by-value and scalar `out`/`init ref<T>` arguments to bounded
direct self/global calls in both the single-function and complete-program
buffers. Call children remain in source evaluation order while the packed
formal-position map is checked against source parameter names. Overload
selection remains unsupported. Semantic verification and emission remain
hosted.
Single-function compact expression records and tree/statement records retain
their existing layouts. Complete-program headers and every function frame
carry v85, as do single-function diagnostic records. No obsolete tag is accepted
at any reader boundary, and no product, semantic-module or native ABI version
changes for this cleanup.
V85 is the baseline for every implemented source shape, not a feature tier.
Readers do not reinterpret it as a historical version or use version numbers
to select capabilities. Both layouts remain because current publication uses
them; neither is retained for historical comparison.
These syntax and
scanner contracts do not change semantic-module, ISA, Contract, TRRM or native
ABI formats.

The approved [source grammar](grammar-spec.md) is broader than this
implementation. Supported `=>` bodies include top-level functions
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

The parser and binder support post-tested `do { ... } while (condition);` and lexical loop
labels on `while`, `for` and `do`, with named `break`/`continue`. Transfer
targets must be enclosing loops; transfers unwind exited cleanup and managed
root scopes, and cannot escape a `finally` action. A post-tested `continue`
checks its condition; a `for` continue executes the selected loop's iterator.
Definite assignment includes the first post-tested iteration and all transfer
edges. Required constant evaluation uses the same target/iteration order.
The implemented subset rejects duplicate simultaneously enclosing labels
and permits disjoint reuse, matching the approved separate label namespace.
The [source attachment rule](grammar-spec.md#33-statements-and-local-declarations)
requires one label on the loop introducer's source line; the current parser
and native source audits enforce that additional restriction.
The v39 feature milestone added retained post-tested loops and non-nearest
transfers. Current modules serialize them under revision 54, with no native
ABI, scanner-5 record or syntax-protocol schema change.

The parser and binder support standalone `init T name;` and `init var name;` vacant locals,
including a `for` initializer. These readonly slots have no default value;
checked `init` completion publishes them, while false or failure leaves them
vacant. Inferred declarations require a type established by a checked
initialization. Existing definite-assignment, owner cleanup and managed-root
rules apply. The v40 feature milestone added persisted vacant declarations;
current modules serialize them under revision 54, with no native ABI change.
The bounded constant evaluator does not support vacant declarations.
Hosted `=` assignment can also initialize a definitely vacant `init` local.
The first supported assignment fixes an inferred local's type; later
initializations retain that type. This is initialization authority, not
permission to replace a live readonly value. An ordinary immutable local does
not gain this authority when its owner is moved. Assignment evaluates the
right-hand side before publication, cleanup eligibility and managed-root
activation; checked failure leaves the destination vacant. The right-hand side
cannot also reserve or initialize that destination.
Flow joins distinguish definite vacancy from possible publication. Ignoring
a conditional output's Boolean status therefore does not authorize another
initialization; checked false/failure paths retain vacancy. Loop-local slots
begin a fresh dynamic lifetime on each iteration, while outer slots retain
their joined state.
Never-published inline initialization outputs may still retry after failure;
vacancy after retiring an immutable value does not grant the same authority.
The v46 feature milestone added persisted assignments to initialization locals.
Current revision 54 records the assignment and vacant declaration together;
the native ABI is unchanged.
Hosted `name = _;` is a distinct lifetime-end statement, not assignment of a
discard/default value. It requires a definitely live, resolved named local
(not a parameter, receiver, const alias, field or indexed place), performs the
current value's scope-exit obligations at that statement and leaves its fixed
binding vacant. Ordinary immutable locals cannot initialize again; standalone
initialization locals and mutable storage retain their existing authority.
Unique cleanup clears the same live bit used by scope exit and unwind, so the
ended value cannot be dropped twice. Managed handles retire only that local's
root; other handles and the shared object survive until ordinary collection.
Bounded buffer-view locals end their loan, not the backing owner. Possible
live loans at control-flow joins still prevent ending or replacing their
actual named owner, including buffer fields reached through an owner.
The v47 feature milestone added retained lifetime-end nodes. Current semantic
module revision 54 records these nodes where present; cleanup targets,
local-only destinations, definite liveness and initialization authority are
reverified on import and before native PE/JIT lowering under both policies.
Modules without an end node also use revision 54. This changes no scanner
record, native ABI/profile or TRRM encoding. Source-free executable TRRM still rejects this statement; the
bounded scalar frontend represents visible scalar local endings in projection
v60. Enums, general references/pins, arbitrary guards/ARC and view rebinding
remain outside the existing bounded compiler. The bounded constant evaluator
also rejects lifetime-end statements. Nullable inferred `var?` syntax remains
outside the native scalar frontend.

The parser and binder support right-associative `condition ? whenTrue : whenFalse` expressions
below binary-operator precedence. The Boolean condition runs once; only its
selected arm executes. Both arms must satisfy one non-void result contract,
from the expected type or the same known arm type; one known arm can provide
context for an untyped arm. Numeric promotion, inferred nullable/readonly
joins between different known types and bare-literal fallback are not
introduced. Existing scalar, Copy, unique and
managed result contracts apply, including conditional output publication,
moves, cleanup and exact roots. Inactive arms remain type/effect checked.
Required constant evaluation evaluates only the selected arm, while retaining
its declaration checks. Retained conditional nodes were added at feature
milestone v41 and serialize under current module revision 54. Conditional
access, throw expressions and target-typed lambdas remain unsupported.

The parser and binder support right-associative `??` below `||` and above `?:`, and
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
existing rules. Coalescing nodes were added at feature milestone v42 and
serialize under current module revision 54. Scalar/unique optionals,
conditional access and managed ConstEval are not added, and the native ABI is
unchanged.

The parser and binder support prefix/postfix `++` and `--` for initialized writable
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
Primitive update nodes were added at feature milestone v43 and serialize
under current module revision 54, without changing semantic/native consumer
contracts. `NoThrow` cannot promise an unproven checked update. Getters, temporaries, readonly/vacant
places, `char16`, nested owning-child paths and nominal static update
operators are outside this primitive-update contract; this does not defer the
approved static operator grammar or authorize updating a unique value itself.

The parser and binder support nominal static `++`/`--` calls for the current eligible writable
`Copy`-struct place envelope. The place is evaluated once; postfix preserves
the old value and prefix returns the replacement. The operator may fail
before writeback, without rolling back earlier operand/place effects. For
these updates the exact result type must equal the operand type, unlike other
unary operators that may return a distinct type. Existing bodyless static
operator requirements, self/no-self protocols, constrained generic
definition-site proofs, and nominal witness identity apply. This does not add
C#-style instance mutation, owning-value updates, arbitrary `char16`
arithmetic, new equality/default protocols or a witness ABI. Nominal update
calls were added at feature milestone v44 (expression tag 25, with a named
operator target) after primitive update nodes at v43/tag 24. Supported places
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

The parser and binder support primitive binary shifts `<<`, `>>` and `>>>`, plus the
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
`char16` and unique-value update destinations remain unsupported. Shift and
compound-assignment nodes were added at feature milestone v45, and compound
statements use expression tag 26 within an expression statement. Current
module revision 54 serializes them without changing the native ABI or profile.

### Scalar frontend projection milestones (retired tags)

Only v85 is produced or accepted for either projection family, including
complete-program function frames. The following notes preserve implementation
history and record encodings; their older version selections, restrictions and
reader servicing describe retired implementations, not compatibility support.
Current v85 composes all implemented subsets described below, including compact
expressions, tree/statement bodies, direct self/global calls, scalar out/init-ref,
initialization tests, unsafe scopes/caller requirements and named by-value/output
calls.
The source-bound audits, verifier/emitter boundary and buffer layouts are unchanged.

The executable Terrum scalar frontend additionally supports bounded nested
expression trees and expression-bodied scalar functions through provisional
projection v20, plus initialized scalar locals and assignments in v21.
Up to eight typed or inferred locals and twelve ordered declaration/assignment
statements may precede the return envelope used by projections through v47.
Only `mut` locals can be reassigned; names bind sequentially, and inferred
numeric literals have no fallback type. Ordinary calls and vacant locals remain
outside that early native component. Its own parser and scalar binder produce up to 32 postorder nodes
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
structural nesting levels remain fixed. Prior projection tags are not
compatibility targets; keep only selected examples where useful for validation.

Projection v48 carries structured scalar returns in these branch/loop bodies.
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

Projection v49 carries the hosted primitive integer shift and mutation envelope
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

Projection v50 represents initialized typed/inferred scalar declarations inside
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

Projection v51 represents braced scalar `do` and `for` loops through the existing
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

Provisional projection v52 represents nested empty or nonempty standalone lexical
blocks using existing brace syntax; it adds no grammar spelling.
Older projection layouts cannot identify or authorize a source-bound
standalone-block scope. Kind 14 is admitted beginning in v52: a recognized
block selects at least v52, and projections v53 or later may carry it
alongside newer record kinds. Versions 1–51 retain their standalone-block
restrictions.
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
identities and discards in a `for` header reject. Kind 15 is admitted
beginning in v53; later projections may carry it alongside newer record kinds.

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
reinterpretation. This projection version does not change the current syntax,
scanner-10/schema-10, diagnostic inventory, semantic/ISA/Contract/TRRM formats,
native formats or ABI. Scanner token identities 104–106 are defined separately
by the current scanner contract. Only the provisional frontend feature version
is appended.

Provisional projection v54 appends labeled loop headers and named transfers
within the existing 392-word projection and five-word statement records:
kinds 16/17/18 are labeled `while`/`do`/`for`, and 19/20 are named
`break`/`continue`. Versions 1–53 reject these kinds; existing kinds 6/7
remain unnamed nearest transfers and are never reinterpreted. A labeled
header's `slot` is its exclusive body-end index and `root` is its existing
condition (`-1` for an omitted `for` condition). Its exact raw span covers the
label, colon and loop, while the independently audited `LoopSpan` covers the
underlying loop keyword through its end for hosted `BoundProgram` spans.

A named transfer's `slot` identifies an actually enclosing labeled-loop
header and its `root` is `-1`. Its full span covers the exact keyword, raw
identifier and semicolon; `IdentifierSpan` preserves the raw token, including
an `@` escape, while name matching separately uses ordinal normalized
identity. The source-bound reader independently verifies active loop ancestry
and ordinal normalized names; forward, inactive, sibling, non-loop,
wrong-kind/depth/span/root/version targets reject. Blocks and branch
labels are not transfer targets. Duplicate active normalized loop labels
reject and disjoint scopes may reuse a label, matching the approved collision
contract. Same-line label attachment remains separate implementation work.
The binder
derives the existing one-based `TargetDepth` from audited enclosing loops and
uses the existing `BoundBreak`/`BoundContinue` lowering. Selected `continue`
reaches the target `while` header, `for` iterator or `do` condition exactly
once. Selected `break` bypasses the target `for` iterator or `do` condition.
Intervening statements and exited inner-loop
iterators or `do` conditions are skipped, while effects before the transfer
remain ordered as in the hosted source semantics.

Native flow analysis accounts for reachable nested-loop transfers targeting
an outer loop, including literal-false preconditions and termination by other
transfers; the shared verifier remains authoritative for fallthrough and
inactive-source checks. Both runtime policies use the same verified lowering.
The existing error catalog is reused: code 3 reports a missing label at its
raw span, code 4 a duplicate active normalized label at its declaration, and
code 1 unsupported, spacing or non-fallthrough failures. Shared lookup,
parsing and version classification preserve the existing semantic work limit.
Invalid source, spans, versions or targets fail closed without publishing a
partial projection, and failed CLI builds retain existing PE bytes. Existing
local, source-depth, record, node, visit and backend resource limits remain;
the projection width, scanner and semantic formats, indexed-TRRM protocol and
native ABI do not change.

Provisional projection v55 appends statement kind 21 for standalone
`require condition;` in the existing 392-word projection and five-word
statement record. `slot` is zero, `root` is the Boolean condition, and the
span covers the exact `require` keyword through semicolon. Kind 21 selects at
least v55; v55 may also carry the existing block, discard and named-transfer
records. Versions 1–54 retain their prior shapes and reject kind 21.

The parser binds the condition to `bool`; lowering reuses the existing
`BoundRequire` under both runtime policies. It creates no hidden local and
does not extend source scope. The condition is evaluated once, preserving
existing lazy Boolean/conditional evaluation, selected-arm effects and update
ordering. A checked condition failure occurs before the existing
`0xE0540003` requirement failure. Shared hosted flow verification remains
authoritative: Boolean forms it proves false may establish non-fallthrough;
an unknown comparison or variable condition does not. No synthetic return or
new flow rule is added.

The independent source-bound audit checks the exact keyword, Boolean root,
span, postorder/source coverage and local membership; requirements in `for`
headers reject. Shared terminated-expression parsing and maximum
statement-kind classification preserve the existing verification work cap.
There is no hosted-success fallback. Failure clears provisional extensions
and leaves completion last; failed CLI source preserves prior PE bytes.
Existing source, token, local, record, node, expression-depth,
structural-depth, verifier and backend limits remain unchanged, as do source
grammar, scanner, semantic/ISA/Contract/TRRM/native formats and ABI.
`static require`/`static ensure`, output-expression forms, custom failure
selection, declaration conditions, calls, richer types, ownership, managed
cleanup and new policy or metadata contracts are outside this projection.
This projection does not add label-attachment enforcement.

Provisional projection v56 admits one `void` function with a braced body,
including an empty body or implicit completion. Return-type code zero is
reserved for these headers; v56 is not a scalar-return projection.
Statement kind 22 represents `return;` or a balanced parenthesized unit-literal
return such as `return ();` or `return ((()));`: `slot` is zero, `root` is
`-1`, and the span covers the exact keyword through semicolon. Unit returns
allocate no expression node. Their parentheses retain the existing
16-level expression-recursion bound. Versions 1–55 reject void headers and
kind 22.

Lowering uses the existing `BoundReturn(null, sourceSpan)` for each explicit
return and mirrors hosted binding's appended implicit null return at the
source closing-brace end, including after an explicit return. This completion
adds no projection statement record, scalar result, hidden local or source
scope. Supported scalar statements preserve their effects, laziness and
checked failures under both policies. Parameterless `void Main()` exits zero;
public void exports use normalized names and the existing zero-through-three
primitive-parameter ABI. Invalid source still preserves prior PE contents.

The independent reader audits the void keyword, header, null-return root,
spans, grouping, complete source coverage and existing scalar roots and local
visibility. Scalar-valued returns and void parameters or locals reject; the
six-primitive expression/local/parameter domains and all resource limits
remain unchanged. Expression-bodied void functions in v56, general calls and void
call-return expressions, a general unit-expression domain, vacant storage,
ownership and managed cleanup remain outside this projection. No grammar,
scanner, semantic/ISA/Contract/TRRM/native format or ABI contract changes.

Provisional projection v57 represents existing void expression-bodied requirements
and non-void scalar discards, for example `void Main() => require true;` and
`public void F(int32 x) => _ = x + 1;`. It requires return-type code zero,
expression-body flag 41 set to one, envelope roots 38–40 set to `-1`, and
exactly one existing kind-21 requirement or kind-15 discard record. Requirements
have no local; discards have exactly one readonly hidden identity after the
parameters. The Boolean-target condition and self-determined scalar discard
rules are unchanged, including rejection of `_ = 42;` without a numeric type.
Versions 1–56 retain their void-arrow restrictions; braced void payloads without
`else if` remain v56.

The independent reader consumes the entire header, arrow and statement,
auditing the exact root, span, type, hidden identity, precedence and postorder
coverage. Lowering retains hosted statement, function and arrow-body spans,
the outer managed lexical block, existing requirement or hidden-discard
lowering, and implicit null completion at the semicolon end. Supported lazy
evaluation and checked-failure order are preserved under both policies.
An arrow body is a statement, not an implicit unit-valued return: `=> ();`
and `=> return;` are not hosted-valid void callable forms. General calls,
assignment/update arrows, declarations, static/output conditions, unit
expressions/storage and ownership remain excluded. Existing buffer/record
widths, resource limits, grammar, scanner and semantic/native/ABI contracts
are unchanged.

Provisional projection v58 represents existing `else if` chains in braced scalar
or void functions. It requires an actual kind-23 marker, expression-body flag
41 set to zero and envelope roots 38–40 set to `-1`. The marker's `root` is
`-1`, `slot` is the exclusive end of exactly one nested `if` subtree, and its
raw span runs from `else` through that subtree. The nested `if` span excludes
the preceding `else`; the parent `if` span includes the complete chain.
Versions 1–57 reject kind 23. Feature selection takes precedence over v56 for
braced void chains without widening v57 arrow payloads.

The independent reader certifies the marker's association, sole child,
subtree end, Boolean roots, source spans, node ownership, local visibility and
loop ancestry. It rejects an end that consumes a sibling statement. Lowering
uses a direct nested `BoundIf` as the false arm, not a synthetic lexical block.
Actual source blocks retain hosted lexical flags, and v58 preserves hosted
function, body, branch and completion spans. Older representations are not
compatibility targets. Each selected condition evaluates once, with existing lazy effects
and checked-failure order under both policies. Unselected conditions and
bodies do not execute, but unsupported inactive or following source still
rejects before publication.

Branch siblings restore visible names while retaining monotonic declaration
identities; outer mutations, hidden discards and nearest/named loop transfer
targets retain their existing contracts. Braced void completion and shared
non-void fallthrough verification remain unchanged. Each nested `else if`
consumes a real conditional structural level and marker record; there is no
unbounded chain shortcut or extra source scope. All resource caps, widths,
grammar, scanner, semantic/ISA/Contract/TRRM/native formats and ABI remain.
General calls, richer types and ownership/cleanup remain excluded; this
projection does not itself enforce same-line label attachment, which the
current source parser checks independently.

Provisional projection v59 encodes the existing distinct `[NoThrow]` and
`[DoesNotReturn]` contracts on top-level scalar/void functions, including the
existing supported primitive-parameter forms. Header word 2 uses bit 0 for
public visibility, bit 1 for NoThrow and bit 2 for DoesNotReturn; v59 requires
an actual effect bit. Attributes precede visibility and use exact raw
spellings, in either order. Unknown, duplicate, escaped, case-folded or
argument-bearing attributes reject. Attribute tokens count against the
existing 64-token limit. Versions 1–58 retain their masks and source/body
restrictions.

Effect selection takes precedence over v56–58. Annotated braced scalar/void
bodies use structured statement records with no terminal envelope roots.
Scalar arrows retain the typed expression envelope; void arrows retain
exactly one requirement or non-void discard record. The independent reader
checks the entire source, effect flags, body shape, node ownership and spans,
including unsupported unreachable or following source. Lowering preserves
hosted lexical scopes, full function/body spans and arrow-return spans.
DoesNotReturn is void-only and suppresses implicit null completion; ordinary
void completion remains at the source body's end without a projection record.

`Binder.FoldScalarConstant` and `SemanticVerifier.VerifyLibrary` remain the
shared authorities for scalar folding, NoThrow proof and normal-completion
checking under both runtime policies. Reachable normal returns or fallthrough
reject for DoesNotReturn. Readonly or inactive expressions do not gain purity
or weaker effect checking, and NoThrow does not imply termination. Lazy
evaluation, checked operand-before-require failure order and the existing
zero-through-three primitive-parameter entry/export ABI remain unchanged.
Nonterminating fixtures are compiled and audited, not executed.

All projection widths and source/token/local/record/node/depth limits remain,
including the shared 100,000-visit verification budget. General attributes,
calls, richer types and ownership/cleanup remain excluded. This adds no
grammar, scanner, semantic/ISA/Contract/TRRM/native format or ABI contract;
same-line loop-label enforcement is checked separately by the current parser.

Provisional projection v60 adds `name = _;` for a visible named scalar local.
Statement kind 24 stores the local slot, root `-1`, and span through the
semicolon (or excludes the delimiter in a `for` clause). The source parser and
independent reader reject parameters, unknown/inactive names and non-local
targets. The hosted semantic verifier remains responsible for definite
liveness and existing cleanup and reinitialization authority; ending a mutable
local does not grant immutable locals new assignment authority. The projection
reuses `BoundLifetimeEnd`, the shared verified emitter and both runtime
policies. This v60 operation adds no vacant-local declaration, native
ABI/profile or persisted semantic format.

Provisional projection v61 adds explicit `init T name;` declarations for the
six scalar types. Kind 25 declares a vacant local with root `-1`; local flag
`0x2` records initialization authority. Existing assignment records initialize
only a definitely vacant declared local, and the shared hosted verifier
enforces publication, definite assignment and reinitialization after lifetime
ending. The source audit binds the exact type and name, including a scoped
`for` initializer.

Projection v62 adds bounded `init var name;` scalar locals. Kind 26 marks the
inferred declaration; the producer fixes its scalar type from the first
assignment whose expression already has a known type, and the source-bound
reader verifies the `init var` spelling and resolved local type. Untyped
numeric literals do not establish the type, and unresolved or incompatible
initializations remain errors. Nullable locals and other vacant storage remain
outside this frontend. These provisional projections change no native ABI,
source-free TRRM or semantic-module format.

Projection v63 adds block-local `const T name = literal;` declarations for the
six scalar types. Kind 27 marks the declaration and local flag `0x4` records
constant authority. The source-bound reader accepts only a direct scalar
literal initializer and inlines later reads as its verified constant value;
constant expressions, inferred const locals and `for`-initializer const
declarations remain outside this bounded subset. The projection adds no native
ABI, source-free TRRM or semantic-module format.

Projection v64 adds kind 28 for typed scalar `const` initializers whose
expression tree the bounded constant folder reduces to a `BoundConstant`.
Direct literals remain projection v63 kind 27; folded initializers may use
earlier constant locals. Constant-selected conditional arms and Boolean
short-circuit paths follow only the evaluated branch. Runtime-dependent
evaluated expressions, unsupported forms, inferred const locals and
`for`-initializer const declarations remain outside this bounded subset. The
projection adds no native ABI, source-free TRRM or semantic-module format.

Projection v65 adds kinds 29 and 30 for nested `try { ... } finally { ... }`
regions in the bounded scalar statement subset. The source-bound reader
validates both block extents and lexical visibility before lowering to
`BoundTryFinally`; catches remain unsupported. The shared verifier enforces
the hosted cleanup contract, including NoThrow cleanup, no return or transfer
out of cleanup, and the existing bounded cleanup-flow restrictions. This
projection adds no native ABI, source-free TRRM or semantic-module format, and
continues to depend on the hosted verifier and emitter.

Projection v66 adds kind 31 for explicit `unsafe` blocks and kinds 32 and 33
for a bounded bare `try`/`catch` inside those blocks. Recovery requires the
checked-exceptions runtime policy; typed catches, filters and `finally` on a
catching region remain unsupported. The source-bound reader validates the
unsafe and handler extents, then lowers to `BoundUnsafe` and `BoundTryCatch`;
the selected runtime policy is used during shared verification. This adds no
native ABI or source-free format and still depends on the hosted verifier and
emitter.

Projection v67 adds kind 34 for the optional NoThrow `finally` body attached to
a supported bare catch region. The source-bound reader validates its separate
extent before setting `BoundTryCatch.Finally`; the hosted verifier retains
responsibility for failure-edge and cleanup rules. Typed catches and filters
remain unsupported, and recovery still requires explicit `unsafe` plus the
checked-exceptions policy.

Projection v68 adds kinds 35 and 36 for bounded body `static require` and
outer-body `static ensure` proofs. The reader validates their source spelling
and scope, folds the condition through the shared scalar folder, and erases
only proven-true conditions before runtime verification and emission. A
`static require` may refer to earlier immutable scalar constants; `static
ensure` must be closed and cannot read locals or parameters. False or
unestablished proofs and `ConstEval` calls remain outside this frontend subset.
The proof folder applies constant-selected conditional arms and Boolean
short-circuiting without evaluating unselected branches; reachable
runtime-dependent expressions remain unproven.

Projection v69 uses header word 16 as a three-bit ordinal mask for scalar
`out` parameter declarations. The independent reader requires exact agreement
between each bit and the source `out` keyword, then creates mutable output
variables for the shared verifier. Hosted Boolean-return and definite
assignment/publication rules remain authoritative. This bounded declaration
support does not add general `out` call-site or `init ref<T>` migration.

Projection v70 adds a direct self-call expression for the one projected
function. Value arguments retain declaration order; `out` parameters accept
only existing mutable local or output-parameter destinations, audited by the
reader before lowering to a recursive `BoundCall`. Node operation 102 stores
the argument-root indexes and count; operation 103 stores an existing output
slot. Shared verification remains responsible for conditional output
publication. Other function targets, `out var` declarations and `init ref<T>`
call arguments remain outside this bounded subset; this is not general
call-site migration.

Projection v71 adds an `out var` destination to a direct self-call. The
source-bound reader assigns the inferred parameter type and next local slot,
then lowers operation 104 to a declaring `BoundOutArgument`; shared
verification remains responsible for path-sensitive assignment. This does
not enable `out var` for other call targets or general output call-site
migration.

Projection v72 permits a direct self-call with a non-void result as an
expression statement. Kind 37 roots the statement at the audited operation
102 call; the shared verifier and emitter retain responsibility for call
semantics. The existing parser restriction against void self-calls remains,
and this does not add calls to other functions or settle `init ref<T>`
call-site metadata.

Projection v73 permits a void-returning direct self-call as an expression
statement. The operation-102 node has type zero only in this version and only
for a kind-37 statement; hosted call verification and emission retain
responsibility for its behavior. Void calls are not admitted in value contexts,
and calls to other functions remain outside the bounded frontend.

Projection v74 is a separate complete-source, multi-function shape. The
Terrum-authored producer inventories declarations before parsing their bodies.
Top-level global function names must be ordinal-unique after identifier
normalization, even when signatures differ. Zero through three positional
scalar parameters and scalar return values use the existing six scalar types.
V74–v76 use by-value parameters; v77 adds the existing `bool`-returning scalar
`out` parameter modes and destinations. V80 additionally supports scalar
`out` parameters on non-Boolean/void-returning functions, with existing
destinations only; the hosted verifier requires those outputs initialized on
every normal return. Non-scalar outputs on non-Boolean/void functions remain
unsupported. Forward, self and mutual calls are permitted; visible
locals/parameters shadow function names. Void callers are supported, but
v74 callees must return a scalar. Single-function projections, including
v72/v73 output and void self-call statements, retain their existing shapes
and behavior.

Complete-program v83 adds the existing caller-unsafe top-level callable
modifier. The independent reader matches the declaration flag to source,
derives block-scoped call authorization only from an enclosing explicit
`unsafe { ... }` statement, and propagates the declaration requirement into the
bound function. Version 84 adds kind 110 for a source-audited `unsafe(expr)`
wrapper and authorizes calls whose complete source spans are contained by that
wrapper. The hosted verifier remains authoritative for call obligations; an
unsafe declaration does not authorize its own body, and a safe wrapper can
discharge the obligation internally. Neither version persists caller-safety
metadata or makes the frontend standalone.

Projection v85 encodes the source-argument-to-formal-position map in each
direct scalar call node while retaining argument children in source order.
The independent reader rechecks each label against the callee declaration and
the complete map before lowering. It applies to by-value calls only; named
`out`/`init ref` arguments and overload selection remain unsupported.
Single-function v85 applies the same contract to self-calls; complete-program
v85 applies it to direct self/global calls.

Single-function projection v82 adds kind 110 with the same source-bound
`unsafe(expr)` audit; v83 additionally carries the caller-unsafe declaration
flag and permits such declarations to authorize calls only within a projected
unsafe block or wrapper. These tags belong to the single-function buffer and
do not change complete-program v82/v83. The hosted verifier and emitter remain
authoritative.

### Current v85 complete-program layout

The current `Host.FrontendProgram` buffer at index 11 has 4,284 `int32`
words. Its four-word header is `(85, 1, function count, 0)` for every supported
source shape. Header bit 3 records the caller-unsafe requirement in the existing
per-function payload. Ten source-order frames of 428 words follow:

| Frame words | Contents |
|---|---|
| 0–3 | Inclusive token start, exclusive token end, declaration source start/length |
| 4–395 | Existing 392-word per-function header/body layout, with version 85 |
| 396–427 | One zero-based callee function index per operation-105 node; `-1` for every other or unused node |

Unused frames are all zero. Operation 105 uses the existing eight-word node
shape: scalar result type, exact call span, first/second/third argument roots
(`-1` when absent), and argument count in the high word. Its target identity is
in the dedicated table, not operation 102's occupied fields. Single-function
success and rejection use the single-function buffer and a zero program buffer;
complete-program success has a zero single-function buffer. No partial program
is published on rejection. Diagnostic records also carry v85.

The independent host reader lexes/parses the entire source to inventory names,
signatures, declaration order, token boundaries and full body spans. It
requires frame agreement and complete non-trivia source coverage, then audits
each body using the existing source-bound grammar with absolute spans retained.
It checks every call's source-resolved target and lexical shadowing,
callee-specific type/arity, ordered argument roots/spans and exclusive node
ownership. Only after all functions pass does it lower `BoundCall.Function`
to the corresponding `BoundProgram.Functions` declaration index and verify
the complete program. CLI JSON includes the ordered function inventory and
call targets; executable commands select `Main` by name rather than assuming
function zero, and library builds export the public declarations.

The source bound remains 16,384 UTF-16 units and 64 tokens including EOF.
Ten frames derive from `floor((64 - 1) / 6)`: even an empty void declaration
needs six tokens. The existing 32-node, 12-statement and eight-local capacities
apply to each function; no verifier/emitter budgets change. V85 admits scalar
out/init-ref parameters with matching existing or declared destinations and
requires normal-return publication for non-Boolean/void results. It preserves
the true-publishes/false-vacant contract for Boolean outputs. General reference
parameters, default/params arguments, overload/conversion search, namespaces/imports,
templates/members, external bindings and callable values remain unsupported. This adds source
frontend support, not
source-free TRRM checked-exception Bodies or a bootstrap-gate claim.

### Retired complete-program feature milestones

The version selections below are historical; current headers and frames
always carry v85 and reject all obsolete tags.

Program projection v75 retains the same framing and scalar-call target table,
and selects header version 75 only when an operation-105 call targets a void
function. Such a call is admitted only as the direct root of a kind-37
expression statement, including supported `for` clauses; void calls remain
invalid as values, arguments or nested expressions. The independent reader
checks the target return type and statement-root ownership before lowering.
Version 74 continues to admit scalar-returning callees only. Earlier
single-function projection tags are implementation/validation details, not
compatibility guarantees.

Program projection v76 retains the same framing and admits the existing
`[DoesNotReturn]` contract on void declarations, including its
independent combination with `[NoThrow]`. A v76 header is selected whenever a
function declares `[DoesNotReturn]`; the reader audits the exact declaration
effects and the shared verifier rechecks each body for absence of reachable
normal completion. Calls to such functions remain zero-typed operation-105
nodes rooted directly in kind-37 expression statements. The source producer
uses the inventoried target contract to account for those calls when checking
structured normal completion; the host reader independently resolves the
target and the shared verifier/emitter retain execution authority. V74/v75
still reject these declarations, and v76 without one is rejected.
`[DoesNotReturn]` does not imply `[NoThrow]`; other call positions,
output/reference parameters, general binding and source-free TRRM
checked-exception Bodies remain outside this projection.

Program projection v77 carries the existing scalar `out` parameter-mode mask
through complete declarations and resolves each operation-105 argument against
the target's parameter mode. It admits the existing explicit output
destinations and `out var` declarations only for matching scalar `out`
parameters; those functions retain the established `bool` return contract.
The independent reader audits the declaration mode, argument position,
destination type and source spelling, while the shared verifier checks
definite publication and cleanup under both runtime policies.

Program projection v78 adds a distinct init-ref parameter mask and matching
`init name`/`init var name` destinations for the existing six scalar types.
The independent reader audits declaration modes, argument positions,
destination types and source spellings; the shared verifier retains definite
publication authority. V74–v76 reject output modes, and v77 rejects init-ref
modes. These projections do not add general reference arguments, external
output targets or new publication semantics; single-function v1–v73 layouts
remain unchanged.

Single-function projection v79 carries the existing scalar `init ref<T>` and
non-Boolean scalar `out` modes through recursive self-calls. Header word 17
stores its init-ref mask separately from the `out` mask in word 16; operations
107/108 encode existing and declared init destinations. Non-Boolean `out`
accepts matching explicit or `out var` destinations in recursive self-calls.
The reader audits modes and destinations before lowering; the hosted verifier
remains authoritative for vacancy, conditional publication and normal-return
initialization. This does not expand general reference migration or make the
frontend standalone.

Program projection v80 carries the existing scalar `out` parameter mask for
non-Boolean/void-returning declarations and admits matching explicit or
`out var` destinations in direct calls. Its host reader rejects the same
records under v74–v78, and the hosted verifier/emitter require normal-return
initialization and publish outputs after a successful call regardless of its
result value. Non-scalar non-Boolean outputs and standalone compilation remain
outside its support. Managed-module output exports retain their separate
existing shape restriction.

Single-function v79 uses the same non-Boolean/void scalar `out` contract as
complete-program v80 for bounded recursive self-calls, with explicit and
`out var` destinations. These projection tags are alpha implementation
details, not product-version increments or compatibility commitments; a
preview version, if any, is chosen explicitly when it is shipped.
Single-function projection v80 and complete-program projection v81 additionally
project `is init` tests on named scalar locals and parameters as operation 109.
The source-bound reader requires a direct named-storage read; hosted definite-
initialization verification and native emission remain authoritative. This
does not make the scalar frontend standalone or broaden the hosted form beyond
its existing named-storage contract.

The independent source-bound reader validates brace spans, nested extents,
visible names and local identities before lowering to the existing managed
lexical `BoundBlock` and shared verified path. Names use ordinal normalized
identity: initializer references resolve only to preceding visible declaration
identities, active names cannot be shadowed, and closed sibling scopes may
reuse spellings with monotonic slots. Scope exit removes inner name/write
authority through the existing cleanup path without undoing outer mutations;
returns and nearest or named loop transfers retain their audited targets. Failed
validation publishes no partial projection.

The current v85 projection uses kind 14 for standalone blocks, kind 15 for
non-void scalar discards, kinds 16–20 for named loops/transfers and kind 21 for
requirements. Both scalar/void braced and supported arrow bodies use v85.
Obsolete tags reject rather than selecting earlier feature restrictions. The
392-`int32` buffer and all record widths remain fixed:
16,384 UTF-16 source units, 64 tokens, eight total named/hidden local
identities, 12 statement records, 32 expression nodes, three parameters,
16 expression-recursion levels, six source structural levels and the shared
100,000-visit limit. Compiler-generated discard blocks do not consume source
depth. Scanner-10/schema-10, diagnostic kinds 1–12, token IDs through 106,
semantic/ISA/Contract/TRRM formats and native ABI remain unchanged.

### Compiler library boundary

`Terrum.CodeAnalysis` is a reusable C# compiler library. `terrumc` exercises
that library; it is not an independent parser or binder. Syntax snapshots remain
immutable and usable independently. The separate [native profiles](native.md)
bind a bounded scalar/compact-value language and emit Windows x64 executables
through verified structured operations. A clean syntax result is deliberately not a
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
and binary integer literals without a fallback type, decimal and hexadecimal
floating literals directly targeted to `float32` or `float64`, numeric digit
separators, and checked numeric type-call conversions for `int32`, `uint32`,
`int64`, `uint64` and `char16`. Floating literal construction rounds directly
to the requested target format; hexadecimal `p`-exponent forms are rounded
from their exact binary significand, including subnormal and ties-to-even
boundaries. Unary `+` and `-` are supported for floating values; based integer
literals can construct directly as floating values. Empty `0x`/`0X` and
`0b`/`0B` prefixes diagnose as `TRM0007` and `TRM0008`; malformed digit
separators diagnose as `TRM0009`. Based integer literals construct
target-width bit patterns, while decimal integers retain numeric range
semantics. Typed conversions between `float32` and `float64`, and from
`int32`, `uint32`, `int64` or `uint64` to either floating format, follow
IEEE-754 nearest/ties-to-even behavior, including overflow to infinity and
underflow to subnormal or zero. The hosted binder and emitter also
provisionally convert `char16` to either floating format as its unsigned
code-unit value, exactly representable in both; its eligibility remains an
unapproved [proposal](proposals/char16-floating-conversion-eligibility.md).
The hosted binder and emitter provisionally
implement checked conversions from `float32` and `float64` to `int32`,
`uint32`, `int64` and `uint64`: they truncate toward zero, then range-check;
NaN, infinities and out-of-range truncated results fail. Direct conversion to
`char16` is excluded. This pair matrix follows an unapproved
[eligibility proposal](proposals/checked-float-to-integer-eligibility.md) and
is not an accepted language-contract change. Native lowering isolates every
supported conversion involving a floating format from ambient MXCSR rounding,
flush and exception-mask settings, restoring the caller's MXCSR after
successful conversion and before checked float-to-integer failure dispatch.
Hosted binding, semantic verification, semantic-module persistence and native
lowering support same-format `float32`/`float64` `+`, `-`, `*`, `/` and IEEE
comparisons. Operations temporarily select round-to-nearest, gradual underflow
and masked floating exceptions, then restore the caller's floating control
state. These operations add no checked failure edge and are valid in `[NoThrow]`.
Arithmetic NaNs currently use positive quiet encodings `0x7FC00000` and
`0x7FF8000000000000` provisionally; the exact encoding remains an
[unapproved proposal](proposals/floating-arithmetic-nan-encoding.md).
Floating `%`, mixed-format operators and floating `[ConstEval]` remain
unsupported. The bounded Terrum-authored scalar frontend projects same-format
`float32`/`float64` unary signs, target-typed decimal and hexadecimal floating
literals, based integer literals directly targeting either floating format,
`+`, `-`, `*`, `/` and IEEE comparisons through v85. Its authored parser
publishes literal types and source spans; the hosted reader validates the
token spans and reconstructs exact target-format bits. Floating conversions
between `float32`/`float64` and from `int32`, `uint32`, `int64` and `uint64`
to either format are also projected through v85. `char16`-to-floating
conversion is provisionally projected through v85 under its still-unapproved
eligibility proposal; verification and emission remain hosted.
Floating-to-integer conversions are provisionally projected through v85 under
the still-unapproved pair-matrix proposal; verification and emission remain
hosted. Source-free TRRM
provisionally supports same-format floating binary arithmetic under an
unapproved proposal. Same-format
comparisons and exact-bit constants, explicit conversion between the two
floating formats, conversion from the four fixed-width integer types and
unary sign negation are also provisional under separate unapproved
[comparison](proposals/floating-trrm-comparisons.md),
[float-format conversion](proposals/floating-trrm-format-conversions.md),
[integer-to-floating conversion](proposals/integer-to-floating-trrm-conversions.md),
[negation](proposals/floating-trrm-negation.md) and
[arithmetic](proposals/floating-trrm-arithmetic.md) proposals.
`char16`-to-floating conversion is also provisional under separate unapproved
eligibility and TRRM proposals. Float-to-integer conversion is provisionally
encoded as a distinct fail-fast operation under its unapproved
[TRRM proposal](proposals/floating-to-integer-trrm-conversions.md); checked-
exception TRRM remains unsupported. These are not part of the approved
`(1, 0)` Body profile.
The Terrum-authored scalar frontend publishes result/error objects through
vacancy-only `init ref` outputs: a false return publishes no object, while a
handled parse diagnostic is carried by a constructed result. Frontend
verification/emission remains hosted.
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
holes and a public generic ABI remain unsupported. Custom literal and
registered string-target execution remain unsupported.

The compiler supports a bounded compile-time requirement: `const T name = expression;`
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

The parser also supports `const expression` as a scalar subexpression
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

The compiler supports body-only `static require boolExpression;` and outer-body
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
non-owning receiver loans were introduced at milestone v25. Current module
revision 54 serializes supported receiver contracts; older revisions are
rejected wholesale. The bounded owned-`int32` buffer and lexical-view path
preserves natural element alignment on Windows x64; general
`out`-to-`init ref<T>` migration remains scheduled.
Current semantic-module revision 54 records private managed access authority
directly. Artifacts from retired semantic-module revisions are rejected rather
than normalized or upgraded while reading.
Semantic v27 permits ordinary `mut` methods on unique structs through
initialized named mutable owners or forwarded writable `this`. The methods
may update mutable scalar/Copy fields; they cannot replace owning fields,
move `this`, or widen the readonly NoThrow cleanup contract. Receiver
reservation rejects argument-side invalidation before invocation, and
mutation neither transfers ownership nor adds a cleanup obligation.
Readonly unique loans cannot invoke these methods. The v27 feature milestone
added these methods; current module revision 54 serializes them when present.
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
Current module revision 54 serializes supported loan contracts when present;
retired artifact revisions are rejected even though the field-path encoding
remains unchanged.
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
The expanded export contracts use current semantic-module revision 54; the
v31 milestone adds no syntax or scanner change and does not change the native
physical ABI or profile versions. The persisted-graph contract added at
milestone v32 permits managed roots and
intermediate producers in acyclic native graphs. Their imports and exports
communicate only through the established scalar and handle-free public `Copy`
by-value/result and
`bool`/`out` contracts; managed or unique values, views, reference modes and
mutating-receiver ABI still cannot cross a managed participant boundary.
Each persisted native image retains its own heap and root chain, and calls
preserve the caller's roots while another image runs. `ManagedHeap.Collect()`
remains image-local. Current semantic-module revision 54 supports private
managed consumers through the existing single-provider semantic `CreateConsumer`/`Link` path and native
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
managed heaps concurrent. General cross-module trusted-callable and
reference/access/loan contracts beyond the bounded top-level caller-unsafe
semantic-module obligation, thread-transfer/share capability metadata,
`MemoryPrimitives`, explicit memory orders and collector-aware reference
atomics remain unsupported. Existing `unsafe` recovery and checked ownership
verification are bounded mechanisms, not a general unsafe escape hatch. Their
consistency work is ordered in the
[memory-model implementation sequence](roadmap.md#memory-model-implementation-sequence).

| Surface | Implemented | Not yet implemented |
|---|---|---|
| Text | Immutable UTF-16 source, half-open ranges, line mapping, exact malformed-editor-input preservation | Rope storage, byte maps, generated-source maps |
| Lexing | Pinned Unicode identifiers with standalone `_` reserved and closed-catalog `@keyword` escapes, single-scalar character tokens, lexical `..`, `..^` and `?.` tokens, ASCII decimal, `0x`/`0X` hexadecimal and `0b`/`0B` binary numeric tokens, decimal fractional/exponent and hexadecimal `p`-exponent boundaries, digit separators, generalized ordinary/documentation comment fences, leading trivia, strings, diagnostics including `TRM0006`–`TRM0009` for numeric suffixes, missing based digits and malformed separators, `TRM0010` for overlong comment closers, `TRM0011`/`TRM0012` for invalid/unsupported maximal punctuation runs, EOF; the scanner-10 integer record retains suffix metadata only for diagnostic-bearing tokens | Complete canonical unescaped reservation/context migration; interpolation, raw/native literals, directives; range/slice/optional-access parser consumption and binding |
| Declarations | Using, file-scoped namespaces, functions, compact structs and `unique struct` with primitive/nested Copy/owning fields, bounded source classes including `mut class`, readonly default and writable `mut` class receivers, non-owning compact-struct `mut` methods with named mutable-local receiver writeback, explicit constructors/methods, checked `[NoThrow]`/`[DoesNotReturn]` and unique `[Cleanup]`, same-compilation caller-unsafe declarations; compilation-local static operator protocols and closed constrained functions; library binding and validated references, including combined leaf-producer [module sets](module-sets.md) and explicitly selected acyclic [native graphs](native-graph.md) | Inheritance, unique/inline classes, general interfaces, enums/unions, owning fields in Copy structs, general attributes and generic classes/structs (struct and field attributes diagnose `TRM3001`), parameter-only `implicit ref<T>` binding and ordinary reference-lifetime contracts, caller-unsafe methods/accessors/operators/fields in semantic modules, caller-unsafe declarations/calls in executable TRRM, public generic witnesses, cyclic dependencies, automatic version resolution and resilient module layouts |
| Types | Qualified named types, nested type arguments, `int32`, `uint32`, `int64`, `uint64`, `float32`, `float64`, `char16`, `bool`, `void`; one `?` per type nesting level; source-class and `ManagedNode` optional handles with `write` capability preserved through fields, parameters, results and inference; repeated `?` diagnoses | `pin ref<T>`, scalar options, general generic optional normalization after substitution, family defaults/conditional conformance, general ownership/form qualifiers and general borrowed references, transparent-wrapper ABI contracts |
| Statements | Initialized typed/inferred locals, standalone vacant locals, local value lifetime ending `name = _;`, `mut`, blocks, `if`/`else`, `while`, scoped `for`, post-tested `do`, nearest/named enclosing-loop `break`/`continue`, return, statement-form `require`, bounded constant-only body `static require`/outer-body `static ensure`, structured `try`/`finally`, unsafe checked `try`/`catch` (optionally with `finally`), assignment, primitive integer compound assignment, managed optional `??=` and expression statements, standalone `_ = expression;` with statement-end owning cleanup | `defer`, policy blocks/expressions and variants, `for (... in ...)`, general switch/patterns and wildcard patterns, other context-specific discard destinations, declaration pre/postconditions and symbolic static proofs, typed requirement-failure selection, general exception objects/throw, async, nominal compound assignment |
| Expressions | Names, raw literals, single-scalar character literals in representable `char16` contexts, `()`, grouping, unary/binary operations, caller-unsafe-scoping `unsafe(expr)`, target-checked `?:` and managed-handle `??`, context-typed decimal/hexadecimal/binary integer literals without fallback (based values construct target-width bit patterns), target-typed decimal/hexadecimal `float32`/`float64` literals with unary signs (based integers also construct directly as floats), numeric digit separators, same-format `float32`/`float64` `+`, `-`, `*`, `/` and IEEE comparisons, checked numeric type-call conversions for `int32`/`uint32`/`int64`/`uint64`/`char16`, IEEE-754 conversions between `float32`/`float64`, conversion from those four integer types to either floating format, and provisional `char16`-to-floating conversion under an unapproved eligibility proposal, member/index access, calls, scalar required `const` subexpressions and bounded internal `[ConstEval]` calls, explicit closed static-protocol calls including nominal `++`/`--` updates on the eligible writable `Copy`-struct place envelope with exact operand/result type equality, bounded inference of multiple independent type parameters for top-level generic function templates over eligible `Copy` structs, existing scalar value types and existing qualified source-class/`ManagedNode` handles (preserving readonly/write and supported optional qualification) from typed arguments or typed existing `out` destinations (expected-result context fills unresolved parameters present in the return type), generic signature-based result-type lookup, `new`, named whole-owner `move`, by-value unique arguments, conditional unique `bool`/`out` results, readonly unique and initialized owning-field loans, qualified source-class/`ManagedNode` writes and mutable-class receivers, named-place and writable nested-field loans for compact-struct `mut` receivers including fully initialized unique roots/children, named/ref/out argument syntax, checked synchronous `OwnedInt32Buffer` operations including `Set`/`TryAppend`/`Clear` and lexical `SliceMutable` through writable owning fields, and lexical readonly buffer slices | Floating `%`, mixed-format floating operators, direct floating-to-`char16` conversion, custom literal binding and registered borrowed-text parsers, floating `[ConstEval]`, public/generic constant-evaluation contracts, `require` output-expression form and exception-type selection, implicit `ref<T>` call-site loan introduction and temporary materialization, general character targets beyond `char16`, partial owning-field moves, owning field replacement, general expression temporaries, temporary/managed-class-field mutating-struct receivers, partial-constructor mutation, rebinding `this`, public mutating-receiver ABI, general stored loans/references, evaluation outside the native profile, user-defined conversions, unique/owning generic arguments, views, generic classes/structs, user structs containing managed fields, primitive operator conformance, general argument/result inference and partial holes, constructor/method inference, ordinary overload declarations (rejected with `TRM3003`), contextual discard/lambda `_` forms, general conditional access |
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
as a temporary expression. The hosted parser and binder also permit a half-open
range to initialize a readonly lexical view, for example
`ReadonlyInt32BufferView tail = buffer[1..^0];` or `var all = buffer[..];`.
This first range binding is limited to `OwnedInt32Buffer` receivers and readonly
views. Omitted bounds use zero and the initialized count; absolute and
end-relative lower and upper expressions may be dynamic and evaluate once in
source order. End-relative offsets are checked against the initialized count
before conversion to absolute positions. The verifier reserves the owner while
the bounds evaluate; the checked view-scope operation then validates
`0 <= start <= end <= initialized count` without clamping.
For the existing compiler-supplied `Host` buffers, indexed reads and writes
also accept constant and dynamic `^offset` bounds, lowered to the ordinary
checked buffer index path; dynamic offsets evaluate once, and `^0` and other
out-of-range positions fail there. This does not add indexing
to `OwnedInt32Buffer` or authorize arbitrary indexer contracts.
Range syntax does not add mutable slicing, general indexer contracts,
first-class range values or optional access. `mut MutableInt32BufferView window =
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
exports and the managed-backed compact handle-free `Copy` export contract
added at milestone v31; managed state remains private. DLL callers must serialize calls and
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
lifting, `is null`, `is some value` and the equivalent `is var value` spelling
for optional managed handles. Successful extraction creates a non-null handle
snapshot; clearing the original edge does not invalidate it.
Patterns participate in conditional flow, including short-circuit conditions,
`while` and `require`. The payload name belongs to the enclosing lexical scope;
reads additionally require proof that extraction succeeded on the current path.
For the named-place `is var` and `is var?` forms below, a successful test also
proves that the source storage is initialized on that path. It does not change
the source's nullable value or narrow an aliased field.
An initialized absent value is not an uninitialized
variable or an output from a failed call. Constructors must explicitly initialize
optional fields, and direct optional dereference is rejected. Optional scalar
types and generic normalization remain unsupported.
`is var item` is initialization-aware for a named optional-handle local or
parameter: it binds the non-null payload only when that storage is initialized.
`is var? item` is supported for the same named places and binds the complete
optional value, including initialized null; vacant storage returns false
without reading its payload. The `is var` alias on other supported
optional-handle expressions retains the existing `is some` behavior. On a
non-name optional-handle expression, `is var?` evaluates the expression once,
captures its initialized optional value, and succeeds when evaluation returns.
`is init` is supported for named locals and parameters of any currently
supported value type. It reads only initialization state: managed handles use
their active-root flag, unique values use their live-owner flag, and queried
scalar storage uses a frame flag updated on initialization, output publication
and lifetime end. A successful or failed test narrows initialization flow
without reading the stored value. Fields and non-name expressions are not
supported. Semantic-module revision 54 persists the tested variable slot; the
native call ABI is unchanged.
Inferred nullable `var?` declarations and contextual multiple-output matching
remain unimplemented. Ordinary `var` infers the complete initializer type,
including optional handles. Source approval does not change that support or
existing artifacts.
### Retired semantic-module revision history

The following feature notes record implementation milestones; they do not
describe current writer selection or reader compatibility. Current writers
always emit semantic-module revision 54, and readers accept only revision 54.

Revision 18 admitted private `ManagedNode` code in a leaf producer whose
exported declarations were scalar and by-value only. Revision 19 extended that
private-leaf contract to source classes and optional handles, serializing
checked layouts, construction and root metadata. Revision 20 added inline Copy
fields, retaining private nested layouts and remapping type IDs during semantic
linking. At the time, writers selected the smallest revision that represented
the artifact; that selection behavior is retired.
Revision 22 added Copy-struct constructor arguments, including arguments that
retain only scalar portions. Revision 23 lifted the three-parameter constructor
limit. High-arity functions retain the existing page-safe frame bound and exact
roots for potentially managed spills. These revisions did not change the
native profile, scalar export boundary or private-leaf dependency restriction.
The current semantic-module revision records managed class/`ManagedNode`
access capabilities directly; retired revisions are rejected without
normalization. The v25-v30 milestones successively added the distinct
`MutReceiver` contract, nested mutable-field loans, unique receivers and roots,
mutable buffer-field loans, and mutable buffer-field views. Their revision-
specific header gates and writer selection are retired; current modules encode
supported contracts under revision 54.
The private managed-state boundary and absence of a public managed-handle ABI
are unchanged. The v31 milestone expanded leaf exports to managed-backed
compact handle-free `Copy` values by value. The v32 milestone added private managed
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
per actual line start and, only when needed, one 32-bit packed CRLF-width word
per 32 non-final lines rather than one word per source code unit, preserving
maximum-sized sparse inputs within the existing buffer capacity. Construction
counts first and fills unknown starts in a second scan; already known endpoints
and fully dense single-unit breaks need no second source scan.
It also returns a line's full UTF-16 extent (including its break) by ordinal
from adjacent starts and its content extent using construction-captured CRLF
width bits, without rereading source; the streaming candidate walks line ranges
to provide the same extent without owning a table. `SourceLineIndex`
construction and position, offset and extent lookups, and the streaming
`SourceLines` range and coordinate queries, expose vacancy-only `init ref`
outputs. Invalid construction or queries publish no value, while a later valid
request may fill the same vacant destination.
`SourceRange.TryCreate` uses `init ref<SourceRange>` as its sole output mode:
invalid bounds leave the destination vacant, while valid empty ranges publish
normally.
General borrowed references and stored loans remain unsupported. The accepted
`ref<T>`/`ptr<T>`/`box<T>`/`val<T>` forms, general `write` views, `own<T>` and
general `init ref<T>` invocation still requires its representation,
lifetime, cleanup and ABI implementation; it is scheduled implementation
work. The current compiler admits the bounded `init ref<T>` parameter and
`init name` call mode for supported output values, with `init var name`
introducing a vacant destination in the call. A named destination must still
be vacant; an initialized value cannot be overwritten by `init`. A Boolean
result publishes only on true and leaves the destination vacant on false;
non-Boolean and void results require complete initialization on every normal
return. Exceptional paths do not publish. This hosted result contract does not
make the bounded scalar source projections standalone: single-function v81
and complete-program v82 now carry this contract for scalar `init ref<T>`
declarations through the same hosted verifier and emitter.
The distinct `out` mode now also accepts ordinary non-unique struct values
for non-Boolean and void functions, provided the output is initialized on
every normal return. Public semantic modules, native modules and native
graphs admit these outputs only when their existing value-shape checks
establish public, handle-free Copy layout. Boolean `out` retains conditional
publication; unique outputs and non-scalar `out` in the bounded scalar
frontend remain outside this extension. This does not migrate `out` to
`init ref<T>` or make the scalar frontend standalone.
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
Bare `break` and `continue` target the nearest enclosing loop; named forms
select the enclosing loop with that label. They run the normal
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
The parser accepts nested generic type-argument forms such as
`Option<Option<int32>>` and deeper nesting, preserving them losslessly. This
does not provide or bind a generic `Option<T>` library type; current optional
managed handles use the distinct `C?` form, with at most one `?` per type
nesting level.

### Bounded generic functions

Bounded generic functions support static operator interfaces, explicit struct
conformance, generic function constraints and generic-name call expressions.
The executable subset is deliberately compilation-local: each binary protocol
has three type parameters and one bodyless `static` operator declaration; an
internal Copy struct explicitly implements a closed homogeneous instance. Parameter/result
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

**Comparison operator protocols.**

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
**Unary operator protocols.**

Compilation-local interfaces may declare one bodyless static unary `+`, `-`,
`!`, or `~` with two formals (`TOperand`, `TResult`), such as
`interface Negation<T, R> { static R operator -(T value); }`.
Closed nominal conformances require a `Copy` struct operand and a supported
`Copy` result. The result is independent of the operand and `!` need not
return `bool`; result-only overloads are not supported. Unary and binary
`+`/`-` have distinct arity-aware identities. Generic bodies require explicit
nominal constraints and use the declared effects; concrete operands may call
accessible declared operators without conformance. Closed specializations
lower through existing verified calls and private module servicing. Primitive
witnesses, nullable operand operators, `true`/`false` protocols and a public
generic ABI remain unsupported. Nominal `++`/`--` place updates use the
separate exact operand-shaped contract described above. The two-formal shape
is a bounded implementation of the approved interface grammar.

**Static requirements and implementing-type formals.**

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
one through the existing conditional `bool`/`out` contract. The vacancy-only
`TrySelectCopyInit` implementation now owns the selection logic;
`TrySelectCopy` is an explicit forwarding adapter that publishes its `out`
value only after successful selection. Scalar return typing and output modes
remain part of each helper's declared signature during definition-site proof
and closed specialization.
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

General interface dispatch, primitive operator conformance, general generic
inference, member access on a type parameter and public generic/witness
metadata remain unsupported. A public concrete wrapper may use a private
specialization without publishing its template or witnesses.

Persisted semantic modules retain feature-selected compatibility gates. The
reader rejects contracts whose required revision is newer than the header;
the writer selects a revision from retained features rather than applying the
latest revision to every module.

| Revision | Compatibility contract |
|---|---|
| 21 | Retained nearest-loop transfers require v21; older readers reject them. |
| 33 | Init parameter and call modes are distinct from legacy private `out` spellings; public declarations use the `init:` contract. |
| 34 | Getter member identity, body and contract are persisted; exported Copy-struct getters retain the declared member identity. |
| 35 | Transparent-layout intent is persisted for private and exported structures; replacements changing that intent are incompatible. |
| 38 | Primitive-owned getters require v38 in producer and consumer modules; other nominal getters use their feature-selected revision. |

The supported one-field scalar wrapper and ABI boundary are in the
[native profile](native.md#implemented-profiles-and-versioned-contracts).

Numeric literal, generic operator, discard and other source-syntax support is
described by the current contracts above. The source syntax version is
separate from persisted semantic-module compatibility gates.

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
storage. Indexed lookups, cursor lookahead and sequential reads use
vacancy-only `init ref<Token>` outputs. Public cursor creation retains its
`out` form; an internal `init ref` wrapper serves scanner construction because
managed-handle `init ref` parameters are compilation-private. Failed indexes,
end-of-chain and invalidated cursors leave the destination vacant; copied
tokens remain valid after their source links are collected.

The bounded [Terrum-authored scalar frontend](../self-host/ScalarFrontend.terrum)
consumes one successful scanner token snapshot. Its single-function fallback
reuses the managed token chain materialized during program inventory rather
than rescanning source. The published in-process typed result contains
parameter types and names, expression operators and parameter references,
optional conditional branches, declared `public`/`internal` visibility, and
UTF-16 spans. It accepts one
function with zero to three scalar parameters, parameter references, Boolean
literals, unary `!` over a Boolean literal or named parameter (including grouped operands), unary `~`
over a contextual integer literal or named integer parameter (including grouped
operands), and unary `+` over a contextual integer literal or named parameter
(including grouped operands),
and signed `-` over a named integer parameter or grouped contextual integer
literal (with grouped named operands),
context-typed unsuffixed integer literals (including signed unary minus and
decimal signed minima) in return expressions; single-scalar character literals
only in representable `char16` contexts, decoded by the shared Terrum
[CharacterLiteral](../self-host/CharacterLiteral.terrum) component and
independently validated by the host reader; and at most one binary operation
per expression, including either order of
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
It matches the hosted integer-magnitude decoder's repeated digit separators
for decimal, binary and hexadecimal slices, including based-literal leading
separators; other radices remain separator-free.
Signed and unsigned 32-bit paths bound each accumulated digit to the
destination range rather than first parsing a 64-bit value. AOT/JIT fixtures
cover every radix, both widths and both failure policies; malformed slices,
invalid bounds and overflow leave vacancy-only `init ref` outputs unpublished.
It is not wired into `SyntaxTree.Parse(LexResult)` or the scanner-backed
integer record: that record still supplies the binder's contextual literal
magnitude.

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
construction and scalar/cursor reads publish only through vacancy-only
`init ref` outputs, leaving destinations unpublished on invalid requests.
Its owned-buffer encoders fail rather than substitute, with exact-capacity
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
records or source storage. The self-hosted `SourceText` component exposes a
smaller invocation-scoped facade over immutable `Host.Source`: exact UTF-16
length and code-unit reads plus source-bounded half-open range validation. It
does not own source text, carry a path, or return a runtime string, and is not
the hosted `SourceText` collection.

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

## Current semantic compilation boundary

`Terrum.CodeAnalysis.Semantics.Compilation.Create(IEnumerable<SyntaxTree>)`
binds multiple lossless source snapshots after declaring their inputs and
before binding bodies. A successful result exposes typed operations; errors
leave its program unavailable. `Compilation.SyntaxTrees` contains every input,
and each `SourceDiagnostic` in `Compilation.SourceDiagnostics` carries the
`SyntaxTree` that owns its diagnostic span. `Compilation.SyntaxTree` is only
the first snapshot: do not interpret every multi-source diagnostic against it.
Ambiguous entry points reject. These APIs provide bounded analysis/build
operations, not general semantic queries, analyzers, generators or refactoring.

### Compilation-local token cursor

The hosted compiler supports readonly `Span<Token>` values only through the
compiler-owned projection from a validated `TokenSnapshot`. The 16-byte
Windows x64 descriptor carries an address and count; `uint64` supplies
unsigned native-width index semantics for this slice without adding
source-level `nuint` or general native-sized arithmetic. The compiler verifies
the fixed 12-byte `Token` layout, the owned-buffer extent and alignment, and
the snapshot's scanner-record and source-range invariants before publishing a
view or reading a token.

`[Captures(nameof(tokens))]` is limited to the `TokenCursor` constructor. Its
stored dependency facts follow copies, moves, joins, staged arguments and
exceptional cleanup; the mandatory verifier rejects owner invalidation while
any dependent view or cursor remains live. Span values cannot escape through
returns, outputs, arbitrary fields, semantic modules or native exports.
General `Span<T>`, mutable/writable views, slicing, parser integration and
source-level `nuint` remain unsupported. These hosted verifier/emitter
capabilities do not extend the separate Terrum-authored scalar frontend.

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
and by-value parameters. Callable parameters and results use `bool`, `char16`,
`int32`, `uint32`, `int64`, `uint64`, or `void`; Body-local values also accept
their assigned optional forms. Source-free float signatures, same-format
comparisons and arithmetic, unary negation, and explicit float, fixed-width
integer and `char16` conversions are provisionally implemented under separate
unapproved proposals, not as part of the approved `(1, 0)` Body profile. An
executable entry must be a local parameterless `int32` function; `--library`
produces a no-entry module.
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
Independently supplied linear Bodies with integer conversions, wrapping
operations, shifts, calls or combined division/remainder use the same internal
graph; other linear Bodies retain their existing structured lowering. The internal
representation-conversion node does not change checked source casts or add a
development-module wire tag.
The loader accepts `ConstBits`,
`ConstSigned`, `Copy`, integer `Convert` (wrapping, saturating and equal-width
bit reinterpretation), eager `Select` from already-defined Copy values,
Boolean NOT, integer bitwise NOT/unary plus,
scalar equality/inequality, integer ordering/bitwise operations and explicitly
wrapping integer add/subtract/multiply, checked
integer arithmetic/negation/count shifts and dual-result `CheckedDivRem`,
checked numeric conversion,
equal-width integer bit reinterpretation, module-local scalar `Call`, `Branch`,
`BranchIf`, indexed `Switch`, `Fail` and `Return`. `Switch` compares the
selector's exact scalar bits against strictly increasing unsigned patterns and
transfers through exactly one matching or default edge. `Fail` reasons retain
the approved TRRM meaning and lower to the existing fail-fast status: requirement failure
to `0xE0540003`, overflow and invalid shift count to `0xE0540001`, and
division by zero to `0xE0540002`.
Integer division/remainder retain their distinct signed-minimum behavior.
`CheckedDivRem` checks zero and signed-minimum divided by `-1`, then defines
the quotient and remainder together; the quotient truncates toward zero and
the remainder follows the dividend.
Wrapping Body `Binary` operations use low-width modular bits without overflow
failure; they do not change the checked semantics of source arithmetic.
`char16` has conversion/equality semantics, not integer arithmetic or ordering.
Under the same unapproved proposal, `float32`/`float64` accept exact-width
`ConstBits` bit patterns with width-minimal payloads and same-format IEEE
comparisons via the distinct `FloatCompare` opcode `0x19`. Same-format
addition, subtraction, multiplication and division are provisionally encoded
by `FloatBinary` opcode `0x1D`; any arithmetic NaN is canonicalized according
to the separate unapproved encoding proposal. Explicit conversion between the
two floating formats is separately provisional via `FloatConvert` opcode
`0x1A`; conversion from the four fixed-width integer types is provisionally
implemented via `IntToFloat` opcode `0x1B`; `char16`-to-floating conversion
uses the distinct `Char16ToFloat` opcode `0x1E`; unary sign negation is
provisionally implemented via `FloatNegate` opcode `0x1C`, which toggles only
the sign bit; and `FloatToInteger` opcode `0x1F` converts to `int32`, `uint32`,
`int64` and `uint64` by truncating toward zero before range checking and
failing with the existing overflow status. Float-to-integer conversion cannot
satisfy `NoThrow`, and checked-exception TRRM remains unsupported. These
operations remain unapproved; remainder and other numeric conversion pairs
remain unsupported.
Body-local optional scalar values support `MakeOptional`, `None`, `IsPresent`,
`Copy` and eager `Select`, preserving presence independently from payload bits.
Optional `char16` also supports equality and inequality; optional equality for
other payload types, optional arithmetic/conversions, and optional callable
parameters, results and calls remain unsupported. `Select` consumes only
already-defined values; its alternatives are never conditionally evaluated or
transferred as owners. These Body operations do not extend the source-language
scalar optional type system or native callable ABI.

Body and instruction widths are emitted minimally; loading performs a strict
minimal-width audit. Source binding may fold an expression to a constant
before this boundary; only the supported retained operations are executable.
Required constant evaluation and body static proofs are
discharged by the binder. Compilation-local `[ConstEval]` eligibility is not a published
TRRM contract. The executable Effects clause supports `NoThrow` and
`DoesNotReturn`. A retained `NoThrow` promise rejects checked operations and
`Fail` terminators except for the unreachable fallback after a terminal call
to a separately audited `DoesNotReturn` function. No other static proof
excuses a checked operation or failure. A retained `DoesNotReturn` promise
requires a void function with no reachable normal return; the graph audit
follows indexed edges, prunes known Boolean conditions and carries constant
block arguments. The other Effects bits and contract clauses reject, as do
unsupported sections, parameter modes, generic/nominal declarations and
qualified types.

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
Provisionally, this reader also recognizes float TypeSpecs, float `ConstBits`,
`FloatCompare` `0x19`, `FloatConvert` `0x1A`, `IntToFloat` `0x1B`,
`FloatNegate` `0x1C`, `FloatBinary` `0x1D`, `Char16ToFloat` `0x1E` and
`FloatToInteger` `0x1F` under their unapproved TRRM proposals; this is not
approved executable-profile support.
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
(including void), `Switch`, `Return` and `Fail`. Provisionally, the indexed
reader also recognizes float `ConstBits` and same-format `FloatCompare` under
the unapproved comparison proposal, `FloatConvert` and `IntToFloat` under
their separate unapproved conversion proposals, `FloatNegate` under its
unapproved proposal, `FloatBinary` under the arithmetic and arithmetic-NaN
proposals, `Char16ToFloat` under its unapproved conversion proposal, and
`FloatToInteger` for the eight proposed fail-fast pairs under its unapproved
conversion proposal; the approved `(1, 0)` profile does not.
The bounded call inspection
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
Historical semantic-module revision readers and feature-dependent revision
selection are retired. The writer always emits revision 54 and the reader
accepts only revision 54; supported artifacts still require structural
decoding, binding and declared-contract checks.

The current backend emits Windows x64 machine instructions and PE32+ images
directly, with no CIL/LLVM translation
or linker dependency. Source functions use a saved RBP frame base for
locals and spills, separate from the RSP-relative outgoing call area. Shared
PE/JIT unwind metadata describes both the saved register and allocation.
Cleanup has out-of-line funclets that borrow the source frame in RDX
while reserving their own call areas; they are invoked explicitly on normal
exits and by Windows on checked-policy exceptional exits. Versioned scope
records and filters admit only four compiler-generated checked failures.
Those filters discriminate the bounded Windows failure identity; they are not
authored source `when` filters. General post-unwind source-filter selection
cannot be implemented by executing those predicates in Windows' first-pass
search callbacks. See the [ABI probe boundary](native-seh.md).
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

## Design references

Prior art and the design conclusions drawn from it are collected in
[design sources and provenance](references.md).
