# Portable proof and constant execution

This chapter owns the minimum scalar proof rules and the deterministic
constant evaluator. Its logical versions are `proof-floor/1` and
`constant-execution/1`. These names do not allocate metadata, opcode, or
attribute bytes. [Structural flow](initialization-and-flow.md) remains
independently mandatory; [primitive operators](primitive-operators.md)
supplies actual arithmetic, and [contract phases](../language-contracts.md#3-contracts-bracket-semantic-body-completion)
supplies entry, normal completion, cleanup, and publication.

| Request | Obligation |
|---|---|
| Ordinary expression or executable `require`/`ensure` | Preserve runtime evaluation, effects, checks and failure unless a sound proof permits their removal |
| Body `static require` | Establish the predicate at that point on every reachable normal incoming path |
| Outermost-body `static ensure` | Establish the predicate at semantic body completion on every normal exit, after body cleanup |
| Required `const` expression/declaration, literal construction, or constant-only source context | Execute the selected concrete request successfully and validate its publishable target value |

A static obligation is not a request to run a predicate once and observe
true. A constant result is not proof that a runtime call has that result
for arbitrary inputs, effects or unspecified choices. Neither request can
silently become a runtime check, parser, call or default value.

## Scalar proof facts

The floor tracks by-value scalar parameters, local bindings and staged scalar
snapshots. Each tracked binding has an identity and current value revision.
Copies have their own identity/revision; a saved predicate may depend on
several such pairs. Availability, references, authority and lifetimes still
need their ordinary structural proofs.

| Value domain | Minimum facts |
|---|---|
| Integer `I` | A closed interval within that exact type's range, minus a finite set of excluded values |
| `bool` | A subset of `{ false, true }`, with supported saved-predicate relationships |
| Floating `F` | An exact finite value, signed zero, infinity, known NaN representation, class-only NaN, or unknown |
| Code unit or `rune` | An exact valid value or unknown; no inferred integer operation permission |
| `void` | The live unit value; its availability and evaluated effects remain relevant |

An empty domain is bottom: no reachable normal state. It does not waive
source syntax, binding, accessibility, mandatory literal parsing, or value
validity. A singleton integer interval is an exact value. Native integer
ranges use the selected target, not the compiler host.
Nominal enums/wrappers keep their nominal identity and separately admitted
operations. A field layout does not grant primitive proof rules.

For an integer fact `(lower, upper, excluded)`, remove exclusions outside
the interval, sort/deduplicate the rest, and trim excluded endpoints.
If no value remains, the fact is bottom. All bound calculations use exact
mathematical integers as analysis data; this never removes an executable
overflow check or admits a mathematical rewrite of source arithmetic.

The callable's predicate-constant catalog contains the typed integer constants
in its supported predicates, including constant subexpressions, plus the
representable values `0`, `-1` and the type endpoints. Form it in source order
after binding and mandatory constant requirements; ordinary runtime calls
are not evaluated to discover catalog entries. It is finite and charged to
the proof budget.

### Reads, assignment and primitive transfer

| Action | Required transfer |
|---|---|
| Read/copy an available tracked scalar | Copy its current fact; do not share the new binding's revision |
| Initialize/replace a local | Advance that binding's revision and install the computed fact on the successful edge |
| End/move a value | Remove its numeric fact and invalidate relationships using that revision |
| Integer add/subtract/multiply or negation | Compute endpoint bounds under the actual same-type checked operation; prove safety only when every represented operand combination succeeds |
| Integer division | Require a nonzero divisor and exclude the signed-minimum/negative-one pair before proving totality |
| Integer remainder | Require a nonzero divisor; signed minimum remainder negative one is zero, not quotient overflow |
| Integer bitwise operation | Exact when operands are exact; otherwise the complete type range is sufficient |
| Integer shift | Establish the exact source count domain; an unknown count is not masked or converted first |
| Floating primitive arithmetic | No arithmetic failure for valid value operands; exact finite/class results use the selected IEEE format |
| Declared nominal operation, getter, or ordinary call | Use its published safety/effect contract; no private-body numeric inference is required by this floor |

Integer arithmetic transfer first determines whether all represented
combinations succeed, all fail, or safety is not established. On a normal
successful edge, intersect computed bounds with the result type's range.
This normal-edge fact does not prove that the preceding check cannot fail.
Except for exact results and identity/copy, output exclusions need not survive
arithmetic. Bitwise and shift result precision beyond exact constants is not
required; count safety is still required.

For division, split the dividend into negative/nonnegative intervals and the
divisor into negative/positive intervals, normalize each, and evaluate the
four endpoint pairs of every nonempty interval pair using mathematical
truncation toward zero. Include out-of-range quotient endpoints before
intersecting normal-result bounds with the type range; that intersection
does not prove division safe.
A zero contained anywhere in the represented divisor domain prevents a
nonfailure proof. Remainder is bounded in magnitude by the greatest possible
nonzero divisor magnitude minus one and by the dividend magnitude, with the
dividend's sign. Do not bound it using only endpoint remainders: interior
dividends can produce larger remainders than either endpoint. The minimum
divided by negative one remains a quotient failure even when its remainder
can be proved safe.

Exact floating arithmetic follows the primitive rules at each operation,
including subnormals, cohorts, signed zero and no implicit FMA. Runtime
arithmetic whose NaN bits are unspecified yields a class-only NaN proof fact,
even with exact operands. Do not run the constant evaluator's NaN policy to
prove that runtime payload/sign bits are fixed.

### Predicates and branch refinement

The minimum predicate catalog is scalar Boolean reads, `!`, eager Boolean
`&`/`|`/`^`, short-circuit `&&`/`||`, scalar equality/inequality, and the
admitted scalar ordered comparisons. Each operand must already be legal,
available and non-failing where a proof of predicate completion is required.
No source member name makes a library predicate intrinsic.

Integer comparisons use represented values, not wrapping endpoint arithmetic.
Two intervals can establish an order when their extrema establish it for
every pair; disjoint domains establish inequality. Equal singleton domains
establish equality. Identity of a stable integer/Boolean/code-unit value
establishes reflexive equality; floating identity does not exclude NaN.
Floating comparisons use actual IEEE observations. Unit equality still
includes the ordinary evaluation and availability obligations.

For an integer binding `x` and exact same-typed constant `c`, refine the edge
as follows; reverse the ordered comparison when the constant is on the left.
Clip mathematical bounds to the type domain rather than constructing an
out-of-range source value such as `int8(128)`.

| Predicate | True edge | False edge |
|---|---|---|
| `x == c` | Intersect with `{c}` | Exclude `c` |
| `x != c` | Exclude `c` | Intersect with `{c}` |
| `x < c` | Upper bound at most `c - 1` | Lower bound at least `c` |
| `x <= c` | Upper bound at most `c` | Lower bound at least `c + 1` |
| `x > c` | Lower bound at least `c + 1` | Upper bound at most `c` |
| `x >= c` | Lower bound at least `c` | Upper bound at most `c - 1` |

Boolean tests refine their binding's truth set. `!` exchanges edges.
`&&` refines the RHS under the LHS true edge; its false edge joins the two
ways to be false. `||` does the dual. Eager Boolean operators evaluate both
operands before using their truth tables; a determining LHS never removes
an effect or failure of the RHS.

A tested supported predicate is a known truth fact on its selected edge
while its scalar value dependencies are unchanged. Reusing that fact is
not a relational solver. The floor does not have to derive `x + 1 <= y`
from `x < y`, correlate arbitrary different expressions, or infer a numeric
summary from a called implementation.

A saved Boolean, and its direct scalar copies, may retain these edge facts.
Testing the saved value restores them only while its recorded subject
revisions remain current. Changing a subject removes the relationship, not
the Boolean's already copied value. This is the same distinction as
[saved outcome invalidation](initialization-and-flow.md#outcome-correlations-and-invalidation);
it does not infer a call relationship from private code.

### Joins, loops and effects

Join reachable integer facts by taking the interval hull. Candidate
exclusions are the union of incoming exclusions and that type's
predicate-constant catalog; retain a candidate only when no incoming domain
contains it. Normalize the result. This preserves, for example, exclusion
of zero when one arm produces negative values and another positive values,
without enumerating every value in an interval gap.

Join Boolean truth sets by union. Retain a saved predicate relationship only
when the same relationship and current dependencies hold on every incoming
edge. Join exact scalar facts by keeping an identical semantic value or
representation guarantee; otherwise use unknown. Class-only NaNs can join
as class-only NaN. Different decimal cohorts are not identical representation
facts merely because numeric equality is true.
Bottom inputs do not weaken a reachable input.

Numeric induction and invariant solving are not required. On a retained
source-loop backedge, use the ordinary structural CFG but reset loop-modified
scalar bindings to their full type domains at the cyclic header and loop
exit. Kill affected saved relationships. Unmodified independent copied
scalars retain facts; the current loop condition can refine the current
iteration. Join `break` and other exits conservatively. This reset is not
permission to omit the mandatory structural initialization fixed point.

Taking/exposing a binding's address, mutable capture, and writes through
possibly aliasing access participate in invalidation. A possibly affecting
ordinary call resets address-exposed/captured mutable local facts unless a
checked published footprint establishes noninterference. Independent
non-exposed scalar copies survive. Reads through arbitrary references,
getters, shared storage or volatile/atomic observations do not become stable
local facts just because access is readonly.

The floor consumes declared normal-outcome initialization relationships and
checked effects where the source contracts require them; general numeric
call summaries, alias-sensitive range reasoning and optional-presence
theorems are not added. An executed ordinary `require` can refine its
successful normal edge using a supported primitive predicate, but the check
itself still needs its failure/effect analysis.
An admitted published primitive clause maps into these same facts at its
successful boundary: preconditions over by-value parameters after their
phase, and normal-return postconditions over the saved scalar result or
unchanged scalar inputs. This is direct consumption of a declared guarantee,
not inference of a call summary from a private body. Backing-dependent observations require the
[checked input-state read dependencies](published-predicates-and-effects.md#read-roots-and-effect-records).
The complete logical input includes observed receiver/argument state;
readonly alone supplies neither purity nor an unchanged input.

## Proof outcomes and accounting

| Floor outcome | Meaning |
|---|---|
| Proven | The predicate and required completion hold on every represented reachable normal state |
| Disproved | Supported facts establish false, or establish definite failure before a truth result |
| Not established | The analysis cannot establish the obligation, including mixed truth/safety or an unsupported inference |
| Exhausted | The floor's defined work/state envelope is exceeded; this is not false |

An unreachable point obligation is vacuous after all ordinary source checks.
An outer-body `static ensure` joins every normal semantic completion,
including saved return values, `finally`, scope cleanup and callee-owned
parameter cleanup. It is not conditional on control reaching its textual
location. Escaping failure supplies no normal postcondition obligation.
Cleanup/contract-phase effects can invalidate facts before the boundary.

Required proofs reject when not established. A sound implementation extension
may establish more, including obligations outside this floor or using a larger
supported proof envelope. It cannot turn an actually false predicate into
true, invent access/availability, or weaken an unchanged source contract.
Ordinary runtime checks retain their effects and failure unless their
elimination is separately justified. A definitely failing ordinary operation,
direct or through a known local, follows the warning/promotable-to-error
policy; mandatory constant failure and violated no-failure obligations remain
errors.

The reference floor traverses the bound source CFG in source order,
processing branch true then false and joining their exits. It uses the loop
reset above rather than enumerating iteration histories. Callable predicate
constants are collected before that traversal. Parameter/body/postcompletion
identities use the bound source, not compiler-generated machine blocks.

| Proof work event | Charge |
|---|---:|
| Visit a bound value, operation, transfer, predicate or obligation node | 1 |
| Read, install, remove or logically copy a binding fact | 1 |
| Examine/emit one excluded value, catalog value or dependency pair | 1 |
| Compare or calculate one endpoint/value pair | 1 |
| Combine one incoming fact in a join | 1 |

These are additive semantic events, not a charge for arbitrary solver
machine instructions. Set operations process ascending values and charge
each logical entry examined/emitted; duplicate source insertions still
charge examination. Every endpoint combination actually required by a rule
is charged. Snapshots/copies charge their facts even if physically shared.
An implementation's caching, data structure or optimization cannot reduce
the reference floor's logical charge. Additional proof engines may have
separately identified sound acceptance beyond this reference floor.

For nonexact integer bounds, addition uses the lower/lower and upper/upper
pairs; subtraction uses lower/upper and upper/lower. Multiplication and each
division interval pair use lower/lower, lower/upper, upper/lower, upper/upper
in that order. Repeated equal endpoints still have these logical charges.
Negation calculates both endpoint negations. Exact operands instead have one
exact primitive result calculation. Bound clipping, extrema comparisons,
normalization and fact reads/installations have their separate listed charges.

Proof state uses this abstract byte accounting, not a host object layout:
32 bytes per binding fact header, plus its value payload; integer bounds use
two target-width payloads plus one for each exclusion; Boolean payload is one
byte; floating/code-unit exact payload uses its scalar width plus one class
byte; class-only NaN and unknown floating/code-unit facts use only that class
byte. Live unit facts have no payload. A saved predicate node uses
32 bytes plus 16 bytes per dependency identity/revision pair and its
constant payloads. Catalog entries charge one target-width payload.
These fixed headers account for logical identity, revision, type and kind;
they are budget conventions, not wire or runtime ABI encodings.
Concurrent logical branch snapshots count separately; retire states after
their last reference-floor use.

## Constant execution domain

`[ConstEval]` means the registered constant-execution opt-in on a declaration,
not arbitrary same-named metadata. It implies
[checked pure-form support](published-predicates-and-effects.md#checked-annotations-and-forms)
and explicit admission to required Terrum evaluation. It does not imply
ordinary-form `NoThrow`, termination, all-input success or constant-only
invocation. `[Pure]` alone can authorize backend folding, not required
Terrum interpreter admission. The body and all source binding still validate
normally, including the declared form/effect guarantees.

The minimum value domain includes the 26 scalar values, admitted nominal
enums, and inline Copy tuples/optionals/unions/nominal value aggregates whose
complete state, validity and target constant-value encoding are available.
Every emitted result additionally requires its transitive per-target
[Blittable layout commitment](../data-layout-and-abi.md#fixed-directly-interoperable-representations).
An Auto aggregate, uncommitted optional/union representation or merely
scalar-shaped private layout does not acquire this commitment from evaluation.
Construction and nominal operations still need their actual eligible bodies
or admitted intrinsic contracts. Layout equality or Copy alone does not
register an encoder, constructor, literal parser or constant-formal domain.
Ordinary aggregate/default constants do not enlarge the
[narrow constant-formal catalog](constant-generics.md#initial-domains).

Execution can use evaluator-owned initialized/vacant slots and inline regions,
private mutation, initialization-reference destinations and borrowed immutable
input sequences. Internal typed references/views point to those logical
regions, obey ordinary access/backing/lifetime rules, and are not host pointers.
These private/input values need not satisfy emitted-constant layout/codec
admission unless they themselves become emitted results. A selected pure
call's `init ref<T>` destinations are logical results, not permission to
overwrite pre-existing input state.
No result may reference an evaluator activation or temporary input region.
A published static-data-backed value requires its own admitted backing and
constant-value contract; no span or class becomes serializable implicitly.

The minimum does not admit managed/native resource identity, arbitrary object
graphs, host or target addresses, runtime dispatch with an unresolved witness,
I/O, clocks, culture, process/environment state, ambient mutable globals,
volatile/atomic observation, synchronization, blocking, suspension, producers,
foreign calls, or unsafe recovery/exception objects. A separate admitted
constant feature can extend a known operation/value domain; an ordinary
runtime feature does not supply that constant contract.

Read immutable published constants only through their recorded value/ABI
and publication/backing dependencies. Readonly RVA placement does not itself
prove the transitive region immutable. Constant execution does not initialize runtime static storage
or mark a runtime initialization group complete. Optional folding of a
runtime invocation must separately preserve any required runtime initialization
and other effects; constant eligibility is not permission to erase them.

### Executed-path eligibility and calls

After ordinary binding and complete structural validation, execute only the
selected path. Before a reached operation/call, require its constant effects,
input/result forms, body or intrinsic, and dependencies to be admitted.
An unselected branch can contain a known runtime-only call without running it
or requiring its constant body. The enclosing callable still satisfies its
pure-form guarantee; a mode guard can exclude ordinary effects:

```terrum
[ConstEval]
int32 Square(int32 x, bool trace)
    require x >= -46340 && x <= 46340
{
    if (!RuntimeHelpers.IsPureEval && trace)
    {
        Log(x);
    }
    return x * x;
}
const int32 result = Square(3, false);
```

The result is 9 for either trace input when the selected bodies/encoding are
available. The declared range keeps checked multiplication within int32.
An ordinary runtime form with true trace retains logging.
Using unguarded `if (trace)` would violate the pure-form promise on admitted
true-trace inputs; one successful request with false trace cannot excuse it.
An unknown instruction, malformed type/reference, incompatible rule version
or invalid source construct is not merely an unused runtime operation:
it rejects before affected execution. Required literals/constants embedded
in an unused branch still undergo their ordinary source-level formation.

Calls preserve receiver/argument evaluation, mapping and default selection
from [calls and inference](calls-and-inference.md). Execute the selected
closed body or exact intrinsic; do not trial another overload, inspect an
unrelated constructor, or infer a body from its name. A selected static
witness needs its constant body/contract. Reached opaque or unmarked calls
diagnose, not invoke native code.
Nested calls inherit true/true mode and require their own actual constant
admission. An ordinary live-receiver setter is not admitted merely because
one request supplies private temporary backing; its selected form must have
the actual permitted effect/initialization contract.

Loops and recursion are allowed within the defined fuel, depth and state
envelope; no numeric induction or termination proof is needed to interpret
a concrete request. Their ordinary checks, exit order, scopes, references,
cleanup and pre/postconditions remain active. False parser status with a
vacant output is a parse diagnostic, never a read/default of that output.
Failure in a selected path or cleanup prevents successful publication.

## Semantic actions and normalization

The evaluator interprets unoptimized bound semantic actions, not whatever
instruction sequence a backend happened to emit. The following normalization
defines charges. Source spans locate diagnostics but whitespace, comments,
physical registers, inlining, tail calls and helper implementation details
do not change these actions.

| Bound source form | Semantic actions |
|---|---|
| Typed literal/constant/available binding value | Read its already formed value |
| Unary/binary primitive expression | Evaluate operands in source order, then apply the selected primitive |
| Explicit conversion/representation operation | Evaluate its input, then perform the admitted operation/check |
| Conditional or short-circuit expression | Evaluate the controlling operand, branch once, then evaluate only the selected operands |
| Ordinary/vacant local declaration | Declare its logical slot when the declaration executes, then initialize from its evaluated initializer if supplied |
| Named constant declaration | Bind its separately formed frozen value; no runtime slot declaration or initializer replay |
| Ordinary assignment | Capture the actual place, evaluate RHS, then write its replacement |
| Scalar compound assignment | Capture target, read/get old value, evaluate RHS, apply primitive, write/set on success |
| Scalar increment/decrement | Capture/read target, read exact typed one, apply primitive, write/set; select saved old/new result |
| Getter/setter or nominal operator | Its ordinary selected call actions; no fabricated backing reference |
| Call | Stage receiver/arguments, enter selected callable, initialize formal slots, execute phases/body, save/publish result, retire activation |
| Return | Evaluate/save its logical result, complete ordinary cleanup/postconditions, publish that saved result |
| Loop | Its ordinary initializer/condition/body/iterator order, with branches and backedge transfers |
| Scope exit/early lifetime end | Retire current values in ordinary source cleanup order, executing any admitted cleanup bodies |
| Required static proof | No runtime evaluator action after source proof checking |

A numeric type-call on an untyped literal is its bound literal construction,
not a runtime conversion replay. Binding/mandatory literal parsing has its
own request; subsequent reads of that formed literal cost ordinary reads.
Required constant subexpressions similarly become validated frozen values
for their ordinary uses. `[ConstEval]` does not make an ordinary runtime
parameter a definition-time constant merely because one later call can be
evaluated.

Logical private slots include ordinary compiler-hidden materialization/output
storage defined by the source contract. They keep the same supporting scopes,
replacement, vacancy and retained-dependency rules as source execution;
constant mode introduces no per-iteration owner arena or shorter loan.
Supporting-scope hidden slots start vacant at that scope activation, even if
their expression is skipped. An unentered scope has no such activation.
Named constant declarations instead bind their required frozen Copy value,
not a replaceable runtime slot. Their runtime uses copy that value; reference
use needs ordinary permitted materialization, not assumed constant backing.
Their names follow the ordinary
[local declaration-space and point-of-declaration rules](local-declarations.md).
A constant name is a value expression: assignment, early lifetime end,
initialization-state tests and direct place borrowing are not its operations.

| Constant work action | Charge |
|---|---:|
| Declare, read, copy, initialize, replace or source-retire a scalar/unit/reference/view cell | 1 |
| Aggregate read/copy/write/retirement | 1 per logical root/tag/field cell visited, recursively; zero-data cells still count |
| Capture/select a place, field or sequence element, including its ordinary validation | 1 |
| Apply a selected scalar primitive, conversion, representation or requirement check | 1 |
| Branch, jump/backedge, enter callable, save/publish return | 1 each |
| Read or write an input/buffer code unit or element | 1 per element |
| Validate a final logical constant value | 1 per root/tag/field cell |
| Emit its target constant-value payload | 1 per encoded byte |

Do not add an arbitrary cost for machine arithmetic inside a fixed-size scalar
operation, or remove its semantic cost because it folded. A composite source
operation is expanded into the listed actions; it is not an extra cheap
single operation. Private bookkeeping allocations do not add language actions,
but their logical value storage still counts.
Releasing a dead internal operand snapshot is bookkeeping, not another
source-retirement action. Source scope/early-end/replacement cleanup remains
charged. A code-unit/element read is the cell-read action, not a second
additional charge for that same read; selecting its checked element is
the separate selection action.

A bulk intrinsic needs a versioned exact action expansion or resource recipe
over its validated logical inputs, including work and live scratch/value
storage. Repeated work, copying, decoding and element traversal cannot be
hidden in one native helper step. Without such a recipe or an available
eligible body, it is unsupported in required work. This applies particularly
to registered literal parsers and handlers. Registration admits the actual
body/intrinsic and input transformation, not a cheap method name.

Semantically repeated calls/parses cost their full expansion even if a cache
avoids physical execution. Materialization/argument copies and live state are
not erased for accounting by an optimizer. A tail call remains a call for
the depth rule.

## Portable resource envelope

The guaranteed default envelope is:

| Resource | Minimum supported envelope |
|---|---:|
| Scalar proof work per callable | 1,048,576 proof work units |
| Required constant request | 1,048,576 semantic steps |
| Active entered constant callable frames | 256 |
| Live abstract proof state or constant storage, per analysis/request | 16,777,216 bytes |
| Encoded constant result | 1,048,576 bytes |

These are minimum supported envelopes, not a fixed compiler maximum or a
promise that every program within a source-size limit succeeds.
Implementations may offer larger explicit envelopes and sound proof extensions.
An explicitly selected smaller envelope has its stated outcome; it does not
silently borrow the default. Record the actual envelope for reproducibility.
Proof work includes the callable's catalog, transfers and all scalar
obligations, not a fresh free budget for each statement.

Constant requests start with zero work and no entered callable frame; a root
expression is not a callable frame. Each entered parser/function/constructor/
accessor activation counts, including recursion and tail calls. Bodies on
unselected paths consume no frame/work. Source binding and structural decoding
have their own obligations/budgets, not free execution of an ineligible path.

Constant storage accounting uses target scalar payload bytes plus one state
byte per logical slot, including vacant slots and zero-data unit slots.
Inline aggregates add one root-state byte and their tag/field slots recursively.
An inline optional/union reserves its largest arm plus its tag/root state,
not the sum of mutually exclusive arms; value traversal visits the active arm.
Its admitted value ABI must supply the logical tag payload width and each
arm's slot shapes. Missing shape/accounting information is an incomplete
constant-value contract, not permission to use host layout or a guessed tag.
An internal reference/view costs 32 descriptor bytes plus its slot-state byte:
logical region identity, position, extent and kind/authority use four abstract
64-bit quantities. This is accounting, not a source reference or native ABI.
An entered frame adds 64 abstract bookkeeping bytes plus its value slots;
the fixed header accounts for call/phase/scope/continuation bookkeeping.
Immutable input regions count once while active; copied regions count
separately. Their code-unit cells have the same payload/state accounting.

Staged operands coexist with a new result until that result is established.
Retire staging after its last semantic use, named slots at their ordinary
scope endpoint, and source-required hidden storage at its supporting endpoint.
Replacement must preserve any build-before-drop or output-preparation ordering.
An output encoding buffer counts while live in addition to its source value.
Host allocator headers, caches, decoder objects and instruction implementations
are not the logical storage model. A tool failure to provide required resources
is not a different semantic budget result.

All ceilings are inclusive. Before an action, compute its exact work charge
and needed depth/live storage. If work would exceed the selected envelope,
diagnose work exhaustion without performing it; otherwise depth, then storage
exhaustion takes priority before execution. Negative or overflowing accounting
is not a wrapped small charge. Retiring storage releases its logical charge;
work is never refunded.
After successful execution/cleanup, validate the value, establish its selected
encoding and exact payload length, then reject an oversized result before
emitting a partial value. Encoding bytes still consume work and live storage.
An exactly-at-limit operation/result is allowed if its other obligations hold.

Wall-clock cancellation, unavailable machine memory or host failure is a
tool/resource failure, not a proof that a predicate is false or an alternative
constant value. Optional folding may decline; required work may not fall back
to runtime. A compiler cannot claim the portable default resource guarantee
while silently using its smaller private implementation limit.

## Deterministic NaNs and frozen results

Each admitted constant arithmetic, nonidentity numeric conversion, Min, Max
or valid-bound Clamp whose source contract
leaves a NaN result's representation unspecified chooses the selected format's
positive quiet NaN with zero payload. Apply this at that semantic operation,
not as a serialization-time rewrite of arbitrary inputs.
Identity, ordinary copies/unary plus, frozen constants, and explicit
bit-preserving operations keep their stronger representation contracts.
The [sign-only rules](floating-environment.md#2-sign-only-operations) also apply
to unary negation and Abs: preserve payload, quiet/signaling state and every
non-sign bit, even for a NaN formed by a preceding canonicalizing operation.
Finite decimal cohorts and signed zeros retain the ordinary primitive rules.

```terrum
const float32 nan = float32(0) / float32(0);
const uint32 bits = ToBits(nan);
```

With an admitted float32 bit-observation contract, `bits` is `0x7FC0_0000`.
The name `ToBits` here is illustrative, not intrinsic registration.
This fixed binary32 encoding does not select BID/DPD decimal storage.
An explicit NaN bit construction followed by a copy preserves those original
bits; following it with ordinary constant arithmetic may instead produce
the specified arithmetic NaN. A subsequent negation changes only that
canonical value's sign; it does not canonicalize again.
The selection recipe supplies no missing ConstEval opt-in or operation/
constructor/value/resource program. Invalid Clamp bounds still prevent a
required result rather than returning a canonical NaN.

Required constant results are frozen values. Reading the emitted constant
does not rerun its producer. The constant-only policy does not canonicalize
ordinary runtime NaNs or make runtime NaN payload observations provable.
`[ConstEval]` authorizes its checked pure/constant forms, including explicitly
guarded ordinary effects. It is not permission to discard unrelated argument
effects, runtime initialization, cleanup or failures.
Bit-sensitive transformations preserve the complete selected operation's
observation contract, not just numeric equality or a purity flag.

## Dependencies, availability and admission

A required execution request identifies its bound inputs and complete types,
source/rule versions, selected target/value ABI, input transformations,
callee/constructor/witness/intrinsic identities and executed code bodies,
immutable constant dependencies, and actual resource envelope.
It additionally identifies the selected evaluation form, published read/effect
records, initialization/publication obligations and surviving backing.
Emitted layout requires the published Blittable commitment and selected
target profile; today's replaceable private layout or an informal rebuild
intention is not that commitment.

[Registered value-layout queries](registered-queries.md) additionally retain
their exact complete subject, target and representation dependencies.
A `nuint` result has its own scalar value codec; this does not freeze the
queried layout. A retained layout-dependent observation requires its published
fixed promise or explicitly admitted coupled rebuild/rebind/invalidation
records, including dependent derived uses. `[Sized]` alone supports runtime
queries, not an unresolved generic definition's numeric constant.
The intrinsic recipe reads one immutable target layout fact under ordinary
call/read/return accounting without allocating or traversing a T value.

Constant metadata records typed scalar/field/tag values and their promised
representation observations. Target codegen supplies endian, alignment,
packing and layout; host object blobs and uninitialized padding are not
logical constants. Native-sized integers use the selected target width.
Persisted pointer/address values need a separately admitted symbolic relocation
recipe, not captured interpreter or process addresses.

Documented constant-result behavior is part of the published compatibility
promise. Changing it incompatibly is breaking even when the new body remains
pure. This is an author obligation, not a guarantee that consumers can decide
arbitrary semantic equivalence. Changes within declared latitude remain valid,
and new execution still records its actual body/value dependencies.

Required execution needs the selected body's constant program or an admitted
intrinsic even when ordinary runtime binding needs only its signature.
An absent body, incompatible schema/rule, missing encoding, invalid value,
unsupported reached effect, arithmetic/requirement failure and resource
exhaustion are distinct diagnostic categories. None is parser refusal,
false proof, another overload choice or a success-shaped placeholder.

Bodies/values still require full structural validation before use. A known
runtime-only call on an unused edge differs from an unknown instruction in
the selected body. Optional deeper implementation proof or signer identity
does not replace safe decoding, dependency resolution or value validation.

The logical floor does not assign a proof certificate, constant-program wire
format, registration record, feature byte or target codec. Those retained
facilities need their complete admitted metadata/ABI records; absence is an
explicit unsupported requirement. A private evaluator's source/implementation
inventory cannot stand in for them.

[Conformance cases](../conformance/source-proof-and-constant-evaluation.md)
separate exact constants, branch/exclusion facts, invalidation, floor-unknown
proofs, executed-path eligibility, NaN observations and resource boundaries.
