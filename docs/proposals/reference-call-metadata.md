# Reference-call metadata proposal

**Status: unapproved.** This records a recommended first implementation slice
for the roadmap's general reference/access/loan call contracts. It does not
change accepted source semantics or the approved metadata schema, and does not
approve the broader proposal. Its marked provisional implementation slice now
has hosted structured semantic-module support; that implementation boundary is
described in [the module format](../modules.md) and [implementation notes](../implementation.md).

## Current boundary

The accepted [lifetime contracts](../lifetimes.md) distinguish readonly
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
Bodies still do not retain a validated unsafe-call scope.

**Recommendation:** Provisionally implement round-tripping caller-unsafe
contracts for top-level functions and calls enclosed by explicit `unsafe { }`
blocks in structured semantic modules only. Persist the declaration
obligation, retain the `BoundUnsafe` scope, and reconstruct call authorization
from that scope when reading; do not trust a serialized authorization bit by
itself. `unsafe(expr)` remains same-compilation-only because its scope is
currently represented only by a bound-call authorization fact. Keep unsafe
fields, accessors and operators outside this slice, and keep all caller-unsafe
declarations and calls rejected in executable TRRM until its Body contract
can prove explicit call scope. This slice changes the internal semantic-module
format and must advance its revision. It is an implementation experiment, not
approval of general reference metadata or the broader proposal.

**Implemented provisionally:** Semantic-module revision 55 persists the
top-level function obligation and reconstructs call authorization only from
explicit `BoundUnsafe` scopes. Safe imports reject caller-unsafe replacements;
caller-unsafe imports may use a safe replacement. Revision 54 and future
revision 56 artifacts are rejected rather than upgraded or normalized. These
checks are covered by the `CallerUnsafeTests` regressions for round trips,
replacement compatibility, adjacent-revision rejection, writer refusal of a
retained authorization outside its unsafe block, and reader rejection after a
persisted unsafe block is stripped. They do not approve the broader proposal or
add source-free executable authorization.

### 1. Which dependency relations belong in the first callable profile?

**Summary:** A cross-module caller needs the published relationship between a
returned reference/view and the receiver or parameter that keeps its backing
valid. The existing `Dependency` clause has a place to encode that relation,
but the supported relationship kinds and flags have not been selected.

**Recommendation:** Define a closed initial registry for results/views
dependent directly on the receiver or a parameter, preserving access
authority in the declared types and lifetime provenance in the dependency
clause. Keep capture, storage into longer-lived state, asynchronous retention,
and arbitrary transitive backing graphs outside this first profile. Require a
valid declared relationship wherever a reference/view result needs one;
reject unknown kinds and flags rather than infer dependencies from bodies.

### 2. How should caller-unsafe declarations cross an import boundary?

**Summary:** The grammar gives `unsafe` on a callable declaration a caller
obligation, distinct from unsafe operations inside its body, but the source
to metadata mapping is open. The metadata design already reserves Effects
bit `0x04` for exactly the unsafe-caller requirement. That function-level bit
does not authorize a call inside a source-free executable Body: the current
`Call` record carries no checked provenance for an explicit unsafe scope, so
a reader cannot distinguish a scoped call from an unscoped one.

**Recommendation:** Map the source declaration to that assigned Effects bit
and propagate the obligation at each call site. Only an explicit unsafe
caller scope discharges it; an implementation's internal unsafe block or
body analysis must not silently change the published contract. Before
executable support admits such calls, assign and validate a Body representation
for explicit unsafe scopes. Until then, keep caller-unsafe declarations and
calls outside the executable TRRM profile; do not treat the callee's Effects
bit or hosted source verification as proof that an individual call was
authorized.

### 3. How should executable Bodies validate caller-unsafe scope?

**Summary:** The source caller obligation and assigned Effects bit `0x04` are
settled, and semantic-module revision 56 reconstructs authorization from
explicit `BoundUnsafe` scopes. Executable TRRM Bodies still reject these
declarations and calls: their current region kind `0x06` is reserved, and a
call-site authorization bit would not prove explicit unsafe-block enclosure.

**Recommendation:** Define a first executable slice for indexed, fail-fast
Bodies with root-free Copy-scalar shapes only, using region kind `0x06` as a
structurally validated lexical unsafe region. Assign compact-indexed
`bodyLayout` bit `0x04` as its Body-local feature requirement, set exactly when
the Body contains an unsafe region; retain bit `0x08` as reserved and keep
compact-linear Bodies scope-free. Split blocks at unsafe-scope boundaries and
derive authorization only from validated enclosing-region membership.

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

Writers and readers must audit every call and every Body, including uncalled
functions, and reject malformed, stripped, bypassed or leaking scope
structures. This validates Body structure under the existing producer-trust
model; it does not authenticate original source authorship. Gate support behind
assigned Body feature requirements. This is a representation proposal, not
executable support or source-contract approval.

### 4. Which source rules and metadata gate general output-call migration?

**Summary:** Grammar §5.1 accepts the explicit `init` call-site spelling but
leaves complete place matching, overload binding and conditional-output
metadata open. The hosted scalar projections implement bounded existing
`out`/`init ref<T>` cases; they do not determine general reference, aliasing or
publication rules.

**Recommendation:** Specify eligible destination places, exact selected-callee
and argument mapping, and live/vacant/exceptional publication states before
general migration. Preserve the distinct explicit `out` and `init` modes and
do not infer either mode or publication from a body or method name. Reuse the
assigned `OutputState` clause only for an explicitly declared Boolean result
relationship; keep non-Boolean normal-return completion and exceptional
non-publication distinct. Until those source and verifier contracts are
settled, retain the existing bounded hosted/scalar cases without widening
them.

## Constraints retained

Keep `ref`, writable reference access, and `init ref` distinct. Preserve the
accepted initialization defaults and use the existing `OutputState` clause
for declared result-correlated publication; do not infer publication from a
method name. This proposal does not select new source syntax, new clause
numbers, a standalone verifier, or general reference migration.

Relevant boundaries are the [roadmap](../roadmap.md), [source contracts](../grammar-spec.md),
[lifetime contracts](../lifetimes.md), and [metadata design](../il-metadata.md).
