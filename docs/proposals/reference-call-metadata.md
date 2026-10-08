# Reference-call metadata proposal

**Status: partially accepted.** Section 3's bounded indexed-Body unsafe-region
contract is accepted and specified in the
[instruction set](../specs/il-instruction-set.md). The dependency-relation
profile is deferred until a concrete persisted provider/consumer path needs
it. General output-call migration and all other extensions remain unapproved.
Provisional implementation notes are evidence of support only and do not
settle those decisions.

## Current boundary

The accepted [lifetime contracts](../specs/lifetimes.md) distinguish readonly
references, writable references, initialization loans, backing, roots and
ownership. The source grammar separately defines caller-unsafe declarations
and explicit unsafe scopes. Their cross-call metadata and general executable
support remain incomplete.

The approved metadata design already assigns:

- Contract clause `0x0001` Effects, including bit `0x04` for an unsafe-caller
  requirement.
- Contract clause `0x0002` OutputState for result-correlated `out` and
  `init ref` publication.
- Contract clause `0x0004` Dependency, whose payload identifies a result and
  source ordinal, but whose kind and flag registry remains unassigned.

The bounded scalar frontend's `out` and `init ref` projections do not fill
this gap: they cover scalar call modes and still rely on the hosted verifier
and emitter.

## Questions for direction

### Provisional implementation slice: semantic-module caller obligations

**Pre-slice boundary:** Same-compilation binding already enforces
caller-unsafe function declarations and explicit `unsafe` scopes. Before the
provisional slice below, structured semantic modules retained `BoundUnsafe`
scopes but rejected caller-unsafe declarations and calls. Executable TRRM
Bodies also previously lacked a validated unsafe-call scope; the bounded
indexed-Body support is described separately in section 3.

**Recommendation:** Provisionally implement round-tripping caller-unsafe
contracts for top-level functions and calls enclosed by explicit `unsafe { }`
blocks in structured semantic modules only. Persist the declaration
obligation, retain the `BoundUnsafe` scope, and reconstruct call authorization
from that scope when reading; do not trust a serialized authorization bit by
itself. Revision 65 separately adds a narrow `unsafe(expr)` slice for
caller-unsafe top-level functions with scalar by-value signatures; expression
scopes still do not authorize unsafe fields, members or reference/output
loans. Keep broader unsafe fields and operators outside this semantic-module
slice. Top-level declarations also provisionally round-trip the assigned
Effects bit in executable TRRM, but that metadata alone does not authorize
calls; executable call support is limited to the separately validated indexed
unsafe regions in section 3. This slice changes the internal semantic-module
format and must advance its revision. It is an implementation experiment, not
approval of general reference metadata or the broader proposal.

**Implemented provisionally:** Semantic-module revision 61 persists top-level
caller obligations and the bounded method, instance-getter and static-getter
contracts described below.
Authorization is reconstructed only from explicit `BoundUnsafe` scopes. Safe
imports reject caller-unsafe replacements; caller-unsafe imports may use a
safe replacement. Revisions 59 and 61 are rejected rather than upgraded or
normalized. `CallerUnsafeTests` cover round trips, replacement compatibility,
adjacent-revision rejection, writer refusal of a retained authorization
outside its unsafe block, and reader rejection after a persisted unsafe block
is stripped. Revision 59 additionally covers public `Copy` method values in
semantic links, native graphs and module sets under both runtime policies,
including stack-passed scalar arguments, reordered provider type tables, safe
replacements, forged unique contracts and bounded rejection of repeated
nested-layout imports. These checks do not approve the broader proposal or add
source-free executable authorization.

### Implemented provisionally in semantic-module revision 66: scalar expression-scoped calls

**Summary:** The accepted grammar lets `unsafe(expr)` authorize caller-unsafe
operations within one expression. Earlier hosted binding flattened that scope
into authorization facts on bound operations, so semantic-module writers could
not distinguish an expression-scoped call from an unscoped forged
authorization and rejected it. The existing `BoundUnsafe` statement scope
remains independently retained and reconstructed structurally.

**Implementation:** Revision 65 first persisted expression scopes for
caller-unsafe top-level functions. Revision 66 extends the same structural
wrapper to ordinary static methods on eligible public, non-unique `Copy`
structs. Both call forms require scalar by-value parameters and results.
Readers reconstruct authorization from the wrapper's structural containment
rather than trusting a serialized authorization bit. Instance methods,
getters, operators, fields, expression-scoped reference/output loans and
executable TRRM expression scopes remain outside this slice. This preserves
the source contract without approving the broader caller-obligation metadata
proposal.

**Revision-59 recommendation:** Keep the by-value aggregate-signature
extension provisional and limited to public, non-unique `Copy` structs whose
complete recursively exported field layouts are likewise public `Copy` values
or scalars. Existing semantic-module type descriptors and aggregate ABI paths
already carry those shapes, while the explicit whitelist avoids silently
admitting ownership transfer, cleanup, managed handles, buffers, views or
reference/output modes. This recommendation does not approve the proposal's
general reference metadata or change source semantics.

The provisional method experiment admits ordinary readonly instance methods
on public, non-unique `Copy` structs with exportable public layouts, scalar
by-value explicit parameters and scalar results. Revision 58 also admits
ordinary static methods on those owners, with no synthesized receiver and the
same scalar by-value signature boundary. Revision 59 additionally admits
public, non-unique `Copy` structs as explicit by-value parameters and results
when their recursively exported layouts contain only public, non-unique `Copy`
structs and scalar fields. The writer and bodyless reader share that strict
shape boundary; unique, managed, buffer/view, cleanup-bearing and
non-exportable layouts remain rejected. Calls still require an explicit
persisted unsafe block; the unsafe declaration does not authorize operations
in its own body. Revision 60 additionally admits readonly instance getters on
those owners when the result is scalar and the persisted property identity
matches its canonical getter name. Mutable-receiver methods, constructors,
class members, other accessors, operators, fields and reference/output modes
remain excluded. This is a narrow persistence experiment, not approval of
general caller-unsafe member metadata.

### Provisional extension: Copy-value expression-scoped calls

**Summary:** Revision 66 persists expression-scoped caller-unsafe calls only
for scalar by-value signatures. The existing provisional method contract also
supports recursively exportable public `Copy` values, but that shape must not
be inferred from `Copy` alone: private fields, unique ownership, cleanup,
cycles and unsupported nested layouts remain outside the boundary.

**Recommendation:** Provisionally extend `unsafe(expr)` persistence to
ordinary caller-unsafe top-level functions and eligible ordinary static
methods on public, non-unique `Copy` owners when every explicit parameter and
the result is either a scalar or a recursively exportable public, non-unique,
cleanup-free `Copy` value. Reuse the existing module value-type whitelist and
reconstruct authorization from the same structural wrapper. Preserve
by-value-only modes; do not admit instance receivers, getters, operators,
fields, outputs, reference loans, managed handles, buffers/views, empty or
cyclic layouts, executable TRRM, or scalar-frontend projection. This changes
eligibility but adds no wrapper payload; it still requires a new current
semantic-module revision under the single-revision reader policy. This
recommendation remains provisional and does not approve the broader metadata
proposal.

**Implemented provisionally:** Semantic-module revision 67 admits these
top-level and static call signatures, using the same recursively validated
layout boundary for writer checks and reader authorization. Reader resolution
shares its layout cache across the module's body reconstruction to avoid
repeatedly traversing nested signatures. Regression coverage exercises
top-level and static calls, scalar-only and aggregate-only sides, nested and
floating-field values, named-argument parameter mapping, producer/consumer
round trips, safe replacement, native module sets and graphs under both
runtime policies. This remains a persistence experiment, not approval of the
broader metadata proposal.

### Provisional extension: caller-unsafe field reads in `unsafe(expr)`

**Recommendation:** Extend revision 67's retained expression wrapper to
caller-unsafe value reads from public instance fields on eligible public,
non-unique `Copy` structs with recursively exportable layouts. Validate the
receiver owner, field index and declared/result type at every projected field
edge, and reuse the existing structure-table-scoped layout whitelist/cache.
Readers reconstruct authorization only where the wrapper structurally
contains the read; a safe projection outside the wrapper remains outside its
scope. This changes expression eligibility and therefore requires a new
semantic-module revision, but adds no wrapper payload.

Keep the slice read-only. Do not admit field updates, compound or nominal
updates, field/reference/output loans, getters, instance methods, operators,
class/static/foreign fields, TRRM expression scopes or scalar-frontend
verification/emission. Existing unsafe-block field reads, writes, updates and
supported mutable receiver loans remain unchanged. This recommendation does
not approve the broader caller-obligation metadata proposal.

**Implemented provisionally:** Semantic-module revision 68 retains
`unsafe(expr)` around eligible caller-unsafe field-value reads, including
nested paths, safe projections outside the wrapper, and mixed expressions
with revision-67 calls. Writer and reader checks use the same recursive
public-`Copy` boundary. Expression-authorized field paths are rejected when
used as mutation places, and the existing expression-scope loan exclusions
remain in force. Both runtime policies are covered through semantic modules,
native graphs and module sets. The proposal remains unapproved.

### Provisional extension: caller-unsafe getters in `unsafe(expr)`

**Summary:** Hosted binding already supports caller-unsafe getter access
inside an explicit unsafe caller scope, and semantic modules persist eligible
getter calls inside unsafe blocks. The expression-scope wrapper currently
excludes getters, so this proposal changes persistence eligibility, not source
syntax or getter ABI.

**Recommendation:** Extend revision 68's retained expression wrapper to
caller-unsafe readonly instance getters and storage-free static getters on
eligible public, non-unique `Copy` owners. Allow scalar or recursively
exportable public `Copy` results. Preserve canonical getter identity,
accessor-local effects and receiver shape: an instance getter has exactly one
readonly by-value receiver and no explicit parameters; a static getter has no
receiver or parameters. Keep “readonly” as a receiver contract, not a purity
or `NoThrow` promise.

Retain the wrapper for getter-only expressions, and reconstruct authorization
only for getter accesses structurally within it. Validate serialized getter
identity strictly, while allowing only the established internal producer
qualification during linking. A getter result remains a value, not a backing
place: do not add setter, mutation, loan, auto/backed-property, `field`,
initializer, mutable/class getter, TRRM or scalar-frontend support. Preserve
evaluation-once behavior and accessor-local `NoThrow` checks. This changes
expression-scope eligibility and therefore requires the next semantic-module
revision under the single-revision reader policy, without adding payload
fields. The recommendation remains unapproved and does not approve the broader
reference-call metadata proposal.

Acceptance evidence should cover getter-only and mixed expressions, canonical
and producer-qualified identities, recursively exportable results, wrapper
stripping and scope boundaries, evaluation-once and selected-branch behavior,
`NoThrow`, value-versus-place and loan exclusions, safe replacement, and
semantic links plus native graphs/module sets under both runtime policies.

**Implemented provisionally:** Semantic-module revision 69 retains getter-only
and mixed `unsafe(expr)` scopes for the recommended readonly instance and
storage-free static getter slice, including recursively exportable `Copy`
results. Serialized getter identity remains canonical; linking admits only
the exact producer-qualified identity. Regression coverage exercises scalar
and aggregate results, forwarding through a producer that does not export the
getter directly, wrapper stripping at writer and reader boundaries, safe
replacement, semantic links, and native graphs/module sets under both runtime
policies. This remains an unapproved persistence experiment; it does not add
TRRM or scalar-frontend verifier/emitter support.

### Provisional follow-up: caller-unsafe instance methods in `unsafe(expr)`

**Status: unapproved recommendation.** Revision 69 retains eligible getter
calls in expression-scoped unsafe wrappers, while the existing unsafe-block
method slice already persists readonly instance methods on eligible public
`Copy` owners. Extending the expression wrapper to those methods is an
eligibility change, not a new call representation or receiver loan.

**Recommendation:** Provisionally admit ordinary caller-unsafe readonly
instance methods on public, non-unique `Copy` owners when the existing
recursive value whitelist accepts the result and every explicit by-value
parameter. Reuse the existing `BoundCall` representation: exactly one
readonly by-value receiver at parameter zero, then explicit arguments with
their existing source evaluation order and parameter mapping. Reconstruct
authorization only from retained unsafe-scope structure; add no authorization
bit or wrapper payload. Require an exact owner-relative name with one ordinary
member identifier and preserve the full name, declaring owner and signature in
import-contract comparisons; do not normalize or suffix-match identities.
Validate the stored identifier using the existing Unicode XID rules and the
lexer’s underscore exception: `_` may start or continue a member name, but
standalone `_` remains the discard. Retain ordinal spelling, reject malformed
scalar sequences, and neither require nor preserve the source `@` keyword
escape. During
linking only, also admit the exact shared-owner transformation
`Owner.$producer.Method`. For a non-shared owner, the existing
`$producer.Owner.Method` qualification remains owner-relative to its remapped
declaring type. The current bound/import shape has no separate
method-provenance field, so this recommendation promises canonical name shape
and recorded-identity consistency, not detection of a consistently rewritten,
otherwise-valid member name as a different historical alias. Preserve caller
effects, `NoThrow`, `DoesNotReturn`, replacement compatibility and the
existing call verifier.

Keep mutable or loan receivers, constructors, operators, accessors, reference
or output modes, ownership transfer, unsupported layouts, TRRM, and
scalar-frontend verification/emission outside this extension. A readonly
receiver does not imply method purity. This is a provisional persistence
recommendation and does not approve the broader metadata proposal.

Acceptance evidence should cover receiver-first and exactly-once evaluation,
source-ordered explicit arguments with named-argument mappings, nested and
mixed expression scopes, safe forwarding bodies, scalar and recursively
exportable results, exact owner-relative name shape and linker-only
qualification (including rejection of malformed/repeated qualification),
stripped wrappers, replacement directionality, type remapping, excluded
receiver and parameter modes, and semantic links plus native graphs/module
sets under both runtime policies.

**Implemented provisionally:** Semantic-module revision 70 retains eligible
caller-unsafe readonly instance-method calls in `unsafe(expr)` using the
existing `BoundCall` representation. Writer and reader enforce one by-value
receiver, scalar or recursively exportable public `Copy` parameters/results,
and an exact owner-relative Unicode-XID member name; only linking accepts the
single shared-owner `$producer` qualification. Regression coverage includes
source-ordered named arguments, one receiver slot and one serialized slot per
source argument, aggregate parameters/results, safe forwarding and replacement,
malformed identities, wrapper stripping, and native graphs/module sets under
both runtime policies. Existing call semantics evaluate the receiver and each
source argument once; the implementation reuses `BoundCall` and adds no
evaluation steps. Observable side-effect probes are intentionally absent
because this extension excludes mutable/reference/output loans from the
expression scope. The extension preserves receiver and method effects
without adding a loan or purity promise. It remains a provisional persistence
experiment and does not approve the broader metadata proposal.

### Provisional follow-up: mutable field-receiver loans in `unsafe(expr)`

**Status: unapproved recommendation.** Semantic-module revision 63 already
persists existing synchronous mutable receiver loans through eligible
caller-unsafe field paths inside explicit `unsafe { ... }` blocks. Revision 70
expression scopes excluded those loans, even though the hosted binder and
native emitter already represent and execute the same `BoundMutableFieldLoan`
shape.

**Recommendation:** Provisionally persist only a `BoundMutableFieldLoan`
appearing directly as source argument zero and mapped to formal parameter zero
of its existing ordinary synchronous mutable struct-instance call, with the
complete invocation structurally enclosed by `unsafe(expr)`. Require at least
one caller-unsafe field edge in the loan path and retain the recursive public,
non-unique `Copy` layout gate for every owner and field value along that path.
Reconstruct authorization independently from each field declaration and the
enclosing expression scope; persist no access or loan authorization bit. Keep
the existing place, initialization, mutability, ownership, reservation,
evaluation-order and cleanup checks. Treat `BoundUnsafeExpression` as
transparent when detecting invalidation of an already reserved mutable root;
the scope wrapper must not hide a conflicting loan through a sibling field.

Keep readonly loans, named receiver loans, output/reference arguments,
wrapped/reordered receiver loans, returned or stored references, caller-unsafe
mutable method contracts, constructors, operators, accessors, class/unique/
managed layouts, TRRM and scalar-frontend verification/emission outside this
extension. This is a provisional semantic-module persistence increment, not a
new source rule, receiver ABI, or general loan contract, and does not approve
the broader metadata proposal.

Acceptance evidence should cover leaf and nested field paths, canonical
receiver mapping with named explicit arguments, expression-wrapper stripping
at writer and reader boundaries, conflict rejection across a reserved root
and sibling path, semantic links plus native graphs/module sets under both
runtime policies, and unchanged explicit unsafe-block authorization.

**Implemented provisionally in semantic-module revision 71:** The writer and
reader/link paths admit this existing loan only as source argument zero mapped
to formal parameter zero of a synchronous mutable instance call. At least one
field edge must require caller-unsafe access; every edge's declaration,
initialization, place, mutability, ownership, reservation and recursive public
non-unique `Copy` layout obligations remain independently enforced. The
receiver and call representations, ABI and authorization metadata are
unchanged. Regression coverage includes pruned structure-index remapping for
producer and consumer artifacts, named explicit arguments, rejection of
wrapped/remapped receiver shapes and caller-unsafe mutable targets, malformed
serialized receiver wrappers at producer-reader and consumer-link boundaries,
unique-layout rejection, reserved-sibling conflicts, and semantic/native
graphs and module sets under both runtime policies. This remains a provisional
experiment; the broader metadata proposal is still unapproved.

### Provisional follow-up: caller-unsafe mutable receiver methods

**Summary:** Existing source contracts allow caller-unsafe mutable instance
methods. Before revision 72, semantic-module persistence rejected those
methods, even though revision 71 already persisted the existing mutable
field-receiver loan as source argument zero mapped to formal receiver zero
inside `unsafe(expr)`. Method contracts record mutable receiver identity with
`MutReceiver`; body-bearing declarations preserve and validate that marker
against the serialized receiver mode.

**Recommendation:** Provisionally extend the revision-71 expression scope to
caller-unsafe ordinary mutable instance methods on eligible public,
non-unique `Copy` owners with recursively exportable by-value explicit
parameters and results. Require the same direct receiver-loan shape and
canonical receiver mapping as revision 71, and require the complete call to
remain structurally enclosed by `unsafe(expr)`. Validate the complete
source-to-formal argument mapping: receiver zero is unique and explicit named
arguments may permute only their own formal parameters. Reconstruct the
caller-unsafe effect from the callable contract and the authorization from the
enclosing expression; persist no authorization bit. Keep the existing
per-field caller-unsafe declarations, layout, place, initialization,
ownership, reservation, evaluation-order and cleanup checks.

Separate declaration eligibility from invocation eligibility. Do not admit a
newly supported mutable caller-unsafe target from an unsafe block, a named
receiver, a receiver-only wrapper or any call not wholly under the expression
scope. Enforce this call-shape and scope rule independently when writing and
reading module bodies, including graph, module-set and consumer restoration.
Retain the existing `Exportable` boundary for mutable struct receivers and
reject bodyless mutable receiver imports; the eligible method remains an
implementation target behind a by-value module export, not a new mutable
cross-module ABI.

This follow-up does not admit other receiver loans, output/reference
parameters, returned or stored references, constructors, operators,
accessors, class/unique/managed layouts, or TRRM and scalar-frontend
verification/emission. It adds no source form or ABI and does not approve the
broader reference-call metadata proposal. Acceptance evidence should exercise
the writer, reader and consumer-link rejection boundaries for mismatched
receiver markers, complete mappings, and scope escapes, plus leaf/nested paths,
named explicit arguments, reservation conflicts, and native graphs/module sets
under both runtime policies. Safe mutable methods and existing explicit
unsafe-block behavior must remain unchanged.

**Implemented provisionally in semantic-module revision 72:** The writer and
reader admit caller-unsafe mutable instance methods only through that exact
field-loan shape and only when the complete call is structurally enclosed by
`unsafe(expr)`. The reader rejects such calls in unsafe blocks, local mutable
receiver loans and forged/wrapped/remapped receivers; method declarations
retain the `MutReceiver` body contract without making it importable or
directly exportable. Full argument mappings are checked, and caller-unsafe
authorization remains reconstructed from the method effect and scope.
Regression coverage includes producer and consumer-local calls, forged
contracts/mappings/scopes, and native module sets and graphs under both
runtime policies. The broader proposal remains unapproved.

### Provisional extension: caller-unsafe binary operators in `unsafe(expr)`

**Summary:** The accepted source contract permits caller-unsafe static nominal
operators, and `unsafe(expr)` authorizes operations evaluated within its
expression. Binary nominal operators already lower to ordinary `BoundCall`
nodes with declaration identity; the expression wrapper is the structural
authorization boundary. Their existing persisted support is limited to
`BoundUnsafe` statement scopes.

**Recommendation:** Extend only caller-unsafe static binary operators whose
recognized owner-relative operator key has arity two, whose operands are
both by-value values of the exact owner type, and whose result satisfies the
existing arithmetic/bitwise owner-result or comparison-`bool` contract.
Require the complete call to be within the persisted `BoundUnsafeExpression`,
validate the declaration using the existing public, non-unique `Copy` owner
and recursively exportable layout gate, and require the canonical positional
argument mapping. Reconstruct authorization from the resolved declaration
and wrapper; persist no authorization bit or new call representation.

Here, non-mutating excludes operator-place writeback, not effects or purity:
operand evaluation and the operator body retain their existing ordering,
once-only evaluation, effects and failure behavior. The bound representation
does not preserve whether an ordinary operator `BoundCall` originated from
infix syntax, so the eligibility rule is declaration-based rather than
syntax-origin-based.

**Implemented provisionally in semantic-module revision 73:** The binder
retains an operator-only expression wrapper for recognized binary operator
calls. Writers and readers enforce the same exact signature/layout gate,
complete-wrapper scope and positional mapping; stripped wrappers and forged
argument mappings fail closed. Regression coverage includes nested arithmetic,
comparison results, operand-only scope rejection, unary exclusion, consumer
local and imported calls, semantic linking, and native graph/module-set
execution under both runtime policies. Existing unsafe-block operator
support, nominal updates, operator lowering and ABI are unchanged. This
bounded persistence slice does not add TRRM or scalar-frontend
verification/emission support and does not approve the broader proposal.

### Provisional extension: caller-unsafe unary operators in expression scopes

**Summary:** Revision 73 handles eligible caller-unsafe binary static nominal
operators in `unsafe(expr)`, but unary operator calls still lose their
expression-scope wrapper during persistence. Nominal `++`/`--` are
`BoundNominalUpdate` nodes rather than unary `BoundCall`s and need separate
writeback treatment.

**Recommendation:** Extend persistence only for recognized non-update unary
operator declarations (`+`, `-`, `!`, `~`) on eligible public, non-unique
`Copy` owners. Require one by-value operand of the exact owner type, the
existing unary operator result contract, a complete `BoundCall` within
`BoundUnsafeExpression`, and canonical default positional mapping. Reuse the
existing declaration/layout gate and reconstruct authorization from the
resolved operator and enclosing expression; do not add an authorization bit,
new call representation, purity promise, ABI, TRRM or scalar-frontend
verification/emission support. Keep nominal updates outside expression-scope
support.

**Implemented provisionally in semantic-module revision 74:** The binder
retains the expression wrapper for eligible recognized unary calls. Both
writer and reader apply the same unary signature/layout, scope and positional
mapping checks. Regression coverage exercises the four operator keys,
distinct result types, consumer-local and imported calls, forged mappings,
stripped wrappers, operand-only scopes, nominal-update exclusion, semantic
linking, and native graphs/module sets under both runtime policies. This
bounded persistence slice does not approve the broader proposal.

### Provisional extension: caller-unsafe nominal updates in expression scopes

**Summary:** The accepted update contract already binds nominal `++`/`--` as
`BoundNominalUpdate`, checks the exact owner-typed by-value operator and
result, saves the place value, and commits only after the operator succeeds.
Unlike unary non-update calls, the node is not a `BoundCall`; before revision
75 the binder could discard its `unsafe(expr)` wrapper and the module reader
rejected caller-unsafe updates inside a retained wrapper.

**Recommendation:** Persist only caller-unsafe nominal prefix/postfix updates
whose complete `BoundNominalUpdate` is enclosed in `BoundUnsafeExpression`.
Reuse the established public, non-unique `Copy` owner/layout gate and exact
static update-operator contract. Preserve ordinary writable-place,
initialization, effects, evaluation-once, commit-on-success and prefix/postfix
result rules. Continue rejecting caller-unsafe field paths as expression
mutation places, and reconstruct authorization from the enclosing unsafe
block or expression rather than a serialized flag. Do not widen update
semantics, ABI, TRRM, scalar-frontend verification/emission, or approve the
broader reference-call metadata proposal.

**Implemented provisionally in semantic-module revision 75:** The binder now
retains the complete expression scope around an authorized nominal update.
Writers and readers validate the exact matching `++`/`--` operator and the
eligible owner contract; the reader derives authorization from the retained
scope and rejects a stripped wrapper. Existing unsafe-block update behavior
and expression-place field-path rejection remain unchanged. This bounded
persistence extension does not settle or approve the broader proposal.

### Provisional extension: caller-unsafe constructors

**Summary:** The accepted source contract permits caller-unsafe instance
constructors, but semantic-module persistence previously excluded them.

**Recommendation:** Persist only constructors for public, non-unique `Copy`
owners with recursively exportable public layouts and supported by-value
explicit parameters. Validate canonical constructor identity, owner receiver
and result types, explicit parameter slots, and call argument mappings
separately from the implicit receiver. Preserve named mappings and derive
authorization only from the enclosing unsafe expression or block. Keep
managed-class factories, unique owners, reference/output inputs and
unsupported layouts outside this slice.

**Implemented provisionally in semantic-module revision 76:** Writers and
readers validate body-backed and bodyless constructor contracts and retain
complete authorized scopes. Regression coverage exercises consumer-local and
imported constructors, zero-argument and named-argument calls, unsafe blocks,
stripped scopes, semantic linking, and native graphs/module sets under both
runtime policies. This adds no ABI, TRRM or scalar-frontend
verification/emission support and does not approve the broader proposal.

### Provisional extension: caller-unsafe readonly getters

**Summary:** The accepted source contract supports caller-unsafe getter
accessors, and same-compilation binding requires an explicit unsafe caller
scope. Semantic-module revision 59 already records getter identity,
`NoThrow`, and caller-unsafe effects, but its writer and bodyless reader reject
caller-unsafe accessors.

**Recommendation:** Provisionally persist only readonly instance getters on
public, non-unique `Copy` structs with recursively exportable layouts, exactly
one bound receiver and no explicit parameters, and a scalar by-value result.
Require the canonical `Owner.#get:Property` name and matching `PropertyName`;
preserve accessor-local `NoThrow`. Reconstruct authorization only from an
enclosing persisted `BoundUnsafe` scope. Keep mutable/static/class getters,
Copy-valued results, setters, fields, operators, `unsafe(expr)`-only calls and
source-free TRRM outside this extension. Revision 60 versions the eligibility
expansion consistently with prior revisions; the prior encoding already held
the relevant identity and effect fields, so this adds no new bytes. This
recommendation and implementation remain provisional and do not approve the
broader caller-unsafe member contract.

### Provisional extension: storage-free static getters

**Pre-slice boundary:** The accepted grammar permits static named properties
and accessor-local caller-unsafe obligations, but hosted binding initially
rejected static properties. Authored getter bodies without backing storage do
not create static initialization groups; static storage and initialization
contracts remain separate roadmap work.

**Recommendation:** Implement getter-only static properties with authored
block or expression bodies, scalar by-value results, on non-unique `Copy`
structs. Give them zero parameters and retain the distinct
`Owner.#get:Property` identity, accessor-local `NoThrow`, and caller-unsafe
obligation. Permit unqualified lookup inside the declaring type and
type-qualified lookup outside it; do not add a receiver or implicit storage.
Provisionally persist caller-unsafe static getters only for public,
non-unique `Copy` owners with exportable layouts, reconstructing authorization
from an enclosing `BoundUnsafe` scope. Keep auto/backed properties, `field`,
property initializers, setters, classes, primitives, unique owners, aggregate/reference
results, executable TRRM members, and scalar-frontend projection outside this
slice. This implements an accepted source contract and does not approve general
caller-unsafe member metadata or static initialization semantics.

**Implemented provisionally:** Hosted binding accepts authored block and
expression-body static getters on non-unique `Copy` structs for scalar
by-value results, preserving their zero-parameter `Owner.#get:Property`
identity. Semantic-module revision 61 persists caller-unsafe static getter
contracts only for public, non-unique owners with exportable layouts;
authorization is reconstructed from persisted `BoundUnsafe` scopes. This
remains outside executable TRRM member support and the scalar frontend.

### Provisional extension: Copy-valued getter results

**Summary:** Readonly instance getters already support non-unique `Copy`
results in hosted source binding, but caller-unsafe getter persistence is
scalar-only. Static getters currently accept the existing scalar set, while
their module persistence is also scalar-only. The revision-59 method slice
already supports recursively exportable by-value `Copy` results.

**Recommendation:** Extend static and readonly-instance getter results to
include non-unique `Copy` structs while preserving the current scalar set.
Static eligibility must add only struct values to its scalar whitelist;
reusing the broader generic `Copy` predicate would admit managed handles and
omit floating scalars. For caller-unsafe persistence, reuse the existing
recursive module Copy-value type cache for both body-backed writers and
bodyless readers. Keep canonical getter identity, zero static parameters,
one readonly instance receiver, public non-unique owners, and authorization
reconstructed only from persisted `BoundUnsafe` scopes. This is an eligibility
expansion with no new serialized fields; revision 62 remains provisional.
For persisted caller-unsafe contracts, reject unique, managed/reference,
cleanup-bearing, empty, cyclic or non-exportable result layouts. These
persistence limits do not narrow existing readonly-instance source getter
support. Do not add executable TRRM members or scalar frontend projection.

**Implemented provisionally:** Hosted static getter binding and verification
retain the existing scalar whitelist and add non-unique struct values.
Revision 62 uses the recursive module Copy-value cache for both body-backed
and bodyless caller-unsafe getter contracts. The source and persistence
boundaries remain provisional; this does not approve the broader metadata
proposal.

### Provisional extension: caller-unsafe Copy-struct field access

**Summary:** Same-compilation binding already requires caller authorization
for unsafe field reads and writes, including every unsafe field along a nested
path. Structured semantic modules currently reject those field declarations.

**Recommendation:** Provisionally persist the declaration obligation for
public instance fields on public, non-unique `Copy` structs with recursively
exportable public Copy-value or scalar layouts. Preserve existing field
mutability, initialization, ownership and evaluation rules. Persist the field
obligation, not an access-site authorization bit; readers reconstruct
authorization only from an enclosing `BoundUnsafe` block. Check every field
along nested read/write and field-place update paths, and require exact field
obligation compatibility across semantic links, native graphs and module
sets. Keep classes, static/foreign storage, unique or managed layouts,
field-reference loans, `unsafe(expr)`-only authorization, executable TRRM and
scalar-frontend projection outside this experiment. Revision 63 is
provisional and does not approve general caller-unsafe member metadata.

**Implemented provisionally:** Structured semantic-module revision 63
persists caller-unsafe field declarations on eligible public `Copy` layouts
and reconstructs read/write authorization from persisted `BoundUnsafe`
blocks. Direct nested reads and writes, existing update-place operations,
constructors, semantic links, native graphs and module sets retain exact field
obligations. Existing mutable receiver loans through eligible field paths are
also persisted with authorization checked at every field edge. Class fields
and readonly field loans remain rejected.

### Provisional follow-up: caller-unsafe mutable field-receiver loans

**Summary:** Revision 63 now provisionally persists existing mutable receiver
loans through caller-unsafe fields on eligible Copy layouts. Semantic bodies
encode `BoundMutableFieldLoan` as a loan of a `BoundFieldRead`, so its nested
field path supports lexical authorization checks. Readonly Copy receivers are
passed by value; the existing readonly `BoundFieldLoan` is restricted to
unique fields, which the revision-63 Copy-layout slice excludes.

**Recommendation:** Provisionally admit only existing mutable receiver loans
through public `Copy`-struct field paths, requiring every owner and field
value along the complete path to satisfy the existing layout gate. Every
caller-unsafe field on that path must also be enclosed by persisted
`BoundUnsafe` scope. Retain the existing place, initialization, mutability,
ownership and reservation checks, adding caller-unsafe authorization
validation at each field edge. Reuse the revision-63 field obligation; persist
no per-loan authorization bit and add no source form or ABI. Keep readonly
field loans, `unsafe(expr)`-only persistence, class/unique/managed layouts,
reference/output signatures, returned or stored references, and cross-module
loan contracts unsupported. Preserve evaluation-order and cleanup behavior.
A reader must reject a forged loan moved outside its unsafe block. This
bounded persistence extension does not approve the general reference-call
metadata proposal.

**Status:** Implemented provisionally in semantic-module revision 63. The
bounded implementation and regression coverage do not approve the broader
reference-call metadata proposal or establish a general stored-loan contract.

### Implemented follow-up: caller-unsafe static operators

**Implemented provisionally in semantic-module revision 64:** `BoundFunction`
and callable contracts now retain an explicit operator marker. Writers and
readers validate the marker against the declaring owner, static status,
recognized canonical `op_*` name, arity and signature for body-backed and
bodyless declarations. The persisted slice covers existing safe and
caller-unsafe operators on eligible public, non-unique `Copy` structs with
scalar or recursively exportable `Copy` by-value parameters and results;
caller-unsafe operators still require an enclosing persisted `BoundUnsafe`
scope.

Operator identity is structural across semantic links, native graphs and
module sets, while `NoThrow`, `DoesNotReturn` and caller-unsafe promises retain
their directional compatibility. Imported-signature reconstruction and
function remapping preserve the marker, including `Owner.$producer.op_*`
names. Nominal update verification requires the target marker in addition to
its existing name and signature checks, rejecting forged updates to ordinary
`op_Increment`/`op_Decrement` methods. Existing unary, binary, comparison and
nominal-update binding/lowering rules are unchanged. Revision-63 artifacts are
rejected rather than inferred or migrated; no native ABI or operator lowering
was added.

Regression coverage includes mixed safe/unsafe operators under both runtime
policies, unary results distinct from the owner, Boolean comparisons,
bodyless malformed contracts, identity-mismatched replacements, nominal
updates, and semantic/native links, graphs and module sets. The bounded
implementation does not approve the proposed general reference-call target
contract; mutable receivers, constructors, accessors, classes, generic
witnesses, reference/output parameters and unsupported layouts remain outside
this slice.

Executable TRRM round-trips the assigned caller-unsafe Effects bit on
top-level function declarations, including when composed with `NoThrow` or
`DoesNotReturn`. The accepted indexed-Body contract admits calls only inside
validated unsafe-scope regions as detailed in section 3. Compact-linear calls
and calls outside validated regions remain rejected. This bounded contract
does not approve the proposal's other extensions.

### 1. Which dependency relations belong in the first callable profile?

**Decision recorded:** defer the general dependency-relation registry until a
concrete persisted provider/consumer path needs it. Keep the current metadata
relation kinds unassigned and persistence rejection unchanged; this does not
change source-level lifetime rules or provisional same-compilation support.

**Summary:** The accepted lifetime rules distinguish borrowing a selected
source's storage from forwarding backing obligations already carried by that
source. A direct source selector must not silently erase those inherited
obligations. `DependsOn` names eligible parameters; every listed parameter
constrains the caller regardless of which branch the implementation selects.
A result with one eligible source defaults to the broad
source-storage-or-forwarded-backing relationship, while multiple sources
require an explicit permitted set. Synchronous borrowed inputs are not
retained except through the declared result relationship. Receiver selection
is a distinct source kind; its syntax and encoding are still open.

**Recommendation:** Define the first metadata profile for synchronous,
non-generic callables with one whole borrowed result and an explicit closed
set of parameter sources. Defer receiver-dependent results until receiver
selection has its own unambiguous selector; do not encode a receiver as a
parameter ordinal. Add a closed relationship tag that distinguishes borrowing
source storage, forwarding existing backing, and the accepted one-source
broad default. Preserve full access authority and inherited backing/root/lease
obligations through forwarding, copies, joins and reborrows. Do not make
scalars, outputs or unrelated borrowed inputs eligible sources by default.
Keep capture, longer-lived storage and asynchronous retention outside this
candidate profile. Require a valid declared relation where the selected result
needs one; reject missing, incompatible or unknown relations instead of
inferring them from implementation bodies. This is a proposed first slice,
not an approved restriction on the accepted source contract.

**Candidate registry and validation boundary (unapproved):** Fit the proposal
within the assigned Dependency payload rather than adding bytes: use candidate
`sourceKind = 0x01` for a parameter source, zero-based in declaration order
after name binding, and candidate `resultOrdinal = 0` for the single whole
result. Candidate `resultKind` values are `0x01` for borrowing selected source
storage or eligible substorage, `0x02` for forwarding backing already carried
by the selected source, and `0x03` for permitting either relationship when
exactly one eligible source exists. These are recommendations for a future
registry, not assignments in the approved metadata schema.

`0x01` preserves the selected source's access and every backing, root, lease
and lifetime obligation entailed by that storage; it cannot borrow a callee's
by-value parameter copy as though it were caller storage. `0x02` forwards
existing backing, roots and leases without borrowing the descriptor place,
acquiring a lease or transferring ownership. `0x03` permits either relation:
callers retain the union of applicable obligations, while a producer need only
satisfy one permitted relation, not both. Limit this broad default to a
singleton source set. Multiple eligible sources require an explicit permitted
set; every listed source constrains the caller. `DependsOn` selects sources,
not a relationship refinement. Reject unknown kinds, nonzero flags, duplicate
or invalid ordinals, and unsupported combinations.

Resolve the eligible source set under the accepted declaration rules before
applying the one-source default; an excluded receiver does not become a
parameter source merely because it is the only remaining ordinal. Formal
ordinals are not argument evaluation positions, local slots, table row
indexes, implicit receivers or hidden ABI arguments. “Synchronous” limits
declared retention, not the spelling of an `async` modifier. Whole borrowed
results may include admitted dependency-bearing value descriptors without
changing their by-value semantics into a borrow of the descriptor's storage.

At a replaceable boundary, compare the complete result/formal type, mode and
authority contracts, profile and canonical relation/source set. Producers
must verify that their result satisfies the declared relation; consumers
preserve it through copies, joins and reborrows. Reject an incompatible
replacement before lowering or execution. This proposal does not change the
separate compilation-local `Span<Token>` analysis: its dependency ordinals
remain unpersisted, and current semantic-module/TRRM rejection remains in
force.

**Provisional implementation gate (unapproved):** The current verifier facts
represent forwarded backing, not permission to borrow a source descriptor's
storage. Incoming descriptor roots are tracked separately from the formal
parameter place, so the candidate `0x01` and `0x03` relations cannot be
implemented by reinterpreting `ReturnsForwardedBacking` or its source list.
Keep those relations rejected. If beginning semantic-module persistence, the
first bounded slice should persist only body-backed, synchronous, non-generic,
top-level `Span<Token>` results that explicitly forward backing from a
nonempty parameter source set. It must keep dependency-bearing imports and
exports, bodyless declarations, receivers, `TokenCursor` capture, storage
borrowing, broad-default relations, TRRM, graphs and module sets rejected.
Before admitting even that slice, assign a new semantic-module revision,
persist and validate canonical source ordinals, include the relation in
retained contract compatibility, and preserve compiler-known type identity
through producer qualification and remapping; reject any link that cannot
prove that identity. The writer, reader, caller verifier and source-order
argument mapping must agree, and producer bodies—not serialized backing-root
sets—remain the proof. This is a sequencing recommendation, not an approval of
the candidate registry or a change to current rejection behavior.

### 2. How should caller-unsafe declarations cross an import boundary?

**Summary:** The grammar gives `unsafe` on a callable declaration a caller
obligation, distinct from unsafe operations inside its body. Assigned Effects
bit `0x04` carries that obligation and is provisionally round-tripped by
executable TRRM. It does not authorize a call by itself: call authorization
requires the separately validated indexed unsafe-region structure.

**Recommendation:** Map the source declaration to that assigned Effects bit
and propagate the obligation at each call site. Only an explicit unsafe
caller scope discharges it; an implementation's internal unsafe block or
body analysis must not silently change the published contract. Before
executable support admits such calls, assign and validate a Body representation
for explicit unsafe scopes. The provisional indexed-body implementation in
section 3 admits calls only when that region structure validates; do not treat
the callee's Effects bit or hosted source verification as proof that an
individual call was authorized.

### 3. How should executable Bodies validate caller-unsafe scope?

**Summary:** The source caller obligation and assigned Effects bit `0x04` are
settled, and semantic-module revisions 64–65 reconstruct authorization from
explicit `BoundUnsafe` and bounded `BoundUnsafeExpression` scopes.
Executable TRRM round-trips caller-unsafe declarations and supports calls only
inside validated indexed Body unsafe scopes. Compact-linear Bodies still
cannot prove explicit unsafe-block enclosure.

**Accepted contract:** Define the bounded executable slice for indexed,
fail-fast Bodies with root-free Copy-scalar shapes only, using assigned region
kind `0x06` as a structurally validated lexical unsafe region.
Do not change `bodyLayout`: bits 2–3 select the existing Body form, `0x0C` is
the invalid fourth form, and bits 4–7 remain reserved. Compact-linear Bodies
stay scope-free. Split blocks at unsafe-scope boundaries and derive
authorization only from validated enclosing-region membership.

For kind `0x06`, require a preceding parent region ID, an entry block inside
the region, an exit block outside its subtree but within its parent, and a zero
cleanup block. Every incoming control-flow edge from outside the region must
target its entry; every normal edge leaving the region must target its
designated exit. Nested regions are permitted, but loop backedges must stay
inside their loop region. Returns and fail-fast terminators may end a path
inside an unsafe region; transfers that leave it must obey the designated-exit
rule. The region grants no cleanup, exception-unwind or lifetime authority;
reject Bodies that require those behaviors. Limit the slice to top-level
caller-unsafe declarations and local calls; keep `unsafe(expr)`, imports,
unsafe fields, accessors and operators rejected.

Writers and readers audit every call and every Body, including uncalled
functions, and reject malformed, stripped, bypassed or leaking scope
structures. This validates Body structure under the existing producer-trust
model; it does not authenticate original source authorship. Region kind
`0x06` itself gates this support; no `bodyLayout` feature bit is added.

**Implemented:** Indexed fail-fast Bodies use kind-`0x06`
regions to represent explicit `BoundUnsafe` scopes without changing
`bodyLayout`. The writer lowers scopes into nested lexical regions and rejects
unsafe calls without one; the reader validates region ancestry, entry/exit
edges, loop backedges and the zero cleanup field before reconstructing call
authorization from region membership. Returns and fail-fast terminators may
end paths inside a region. Transfers that leave an unsafe region,
compact-linear unsafe calls, `unsafe(expr)`, unsafe members, cleanup, unwind
and lifetime authority remain unsupported.

**Decision:** Accepted for adoption in the instruction set. This decision is
limited to section 3: it does not approve the dependency-relation profile in
section 1, general output-call migration in section 4, or any other proposal
extension.

### 4. Which source rules and metadata gate general output-call migration?

**Summary:** Grammar §5.1 accepts the explicit `init` call-site spelling but
leaves complete place matching, overload binding and conditional-output
metadata open. The hosted scalar projections implement bounded existing
`out`/`init ref<T>` cases; they do not determine general reference, aliasing or
publication rules.
The accepted ordinary and compilation-local generic non-Boolean/void `Copy`
`out` profiles are separate bounded contracts; they do not resolve the general
metadata and migration questions in this section. See the
[language contract](../specs/language-contracts.md#22-declared-outcome-relationships).

**Recommendation:** Specify eligible destination places, exact selected-callee
and argument mapping, and live/vacant/exceptional publication states before
general migration. Preserve the distinct explicit `out` and `init` modes and
do not infer either mode or publication from a body or method name. Reuse the
assigned `OutputState` clause only for an explicitly declared Boolean result
relationship; keep non-Boolean normal-return completion and exceptional
non-publication distinct. Until those source and verifier contracts are
settled, retain the existing bounded hosted/scalar cases without widening
them.

**Recommended first profile (unapproved):** Admit named local destinations
and explicitly forwarded output parameters for supported scalar values and
recursively exportable, handle-free `Copy` structs. Preserve the selected
declaration, source-order evaluation and exact formal-parameter mapping;
require exact type and mode matches, and reject duplicate output destinations.
Keep field/indexed places, dependency-bearing views, public managed handles,
unique or cleanup-bearing values, and inferred publication contracts outside
this profile. Verify each mode's pre-call destination state, declared
Boolean-result publication, required full publication on normal completion,
exceptional non-publication and import/replacement compatibility before
adopting it. The profile adds no source syntax or clause IDs. It remains gated
on agreement about the exact forwarded-place set and aliasing rules.

## Constraints retained

Keep `ref`, writable reference access, and `init ref` distinct. Preserve the
accepted initialization defaults and use the existing `OutputState` clause
for declared result-correlated publication; do not infer publication from a
method name. This proposal does not select new source syntax, new clause
numbers, a standalone verifier, or general reference migration.

Relevant boundaries are the [roadmap](../roadmap.md), [source contracts](../specs/grammar-spec.md),
[lifetime contracts](../specs/lifetimes.md), and [metadata design](../specs/il-metadata.md).
