# Reference-call metadata proposal

**Status: unapproved.** This records a recommended first implementation slice
for the roadmap's general reference/access/loan call contracts. It does not
change accepted source semantics or the approved metadata schema, and does not
approve the broader proposal. Its marked provisional slices now include
structured semantic-module caller obligations, bounded readonly-instance and
static-method experiments and getter support on `Copy` structs, and validated
indexed executable unsafe scopes;
their boundaries are described in [the module format](../modules.md) and
[implementation notes](../implementation.md).

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
Bodies also previously lacked a validated unsafe-call scope; the bounded
indexed-Body support is described separately in section 3.

**Recommendation:** Provisionally implement round-tripping caller-unsafe
contracts for top-level functions and calls enclosed by explicit `unsafe { }`
blocks in structured semantic modules only. Persist the declaration
obligation, retain the `BoundUnsafe` scope, and reconstruct call authorization
from that scope when reading; do not trust a serialized authorization bit by
itself. `unsafe(expr)` remains same-compilation-only because its scope is
currently represented only by a bound-call authorization fact. Keep unsafe
fields and operators outside this semantic-module slice. Top-level
declarations also provisionally round-trip the assigned Effects bit in
executable TRRM, but that metadata alone does not authorize calls; executable
call support is limited to the separately validated indexed unsafe regions in
section 3. This slice changes the internal semantic-module format and must
advance its revision. It is an implementation experiment, not approval of
general reference metadata or the broader proposal.

**Implemented provisionally:** Semantic-module revision 60 persists top-level
caller obligations and the bounded method and getter contracts described below.
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

Executable TRRM also provisionally round-trips the assigned caller-unsafe
Effects bit on top-level function declarations, including when composed with
`NoThrow` or `DoesNotReturn`, and provisionally admits calls only inside
validated indexed Body unsafe-scope regions as detailed in section 3.
Compact-linear calls and calls outside validated regions remain rejected.
Kind `0x06` is still reserved by the accepted instruction-set contract, so
this bounded Body support remains provisional and does not approve the
proposed extension.

### 1. Which dependency relations belong in the first callable profile?

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
settled, and semantic-module revision 60 reconstructs authorization from
explicit `BoundUnsafe` scopes. Executable TRRM provisionally round-trips
caller-unsafe declarations and supports calls only inside validated indexed
Body unsafe scopes. Compact-linear Bodies still cannot prove explicit
unsafe-block enclosure.

**Recommendation:** Define a first executable slice for indexed, fail-fast
Bodies with root-free Copy-scalar shapes only, using proposed region kind
`0x06` as a structurally validated lexical unsafe region and its feature
discriminator.
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

Writers and readers must audit every call and every Body, including uncalled
functions, and reject malformed, stripped, bypassed or leaking scope
structures. This validates Body structure under the existing producer-trust
model; it does not authenticate original source authorship. The proposed
region kind itself gates this provisional support; no `bodyLayout` feature
bit is added.
The accepted instruction-set contract still reserves kind `0x06`, so this
recommendation and its implementation remain provisional until approved.

**Implemented provisionally:** Indexed fail-fast Bodies use kind-`0x06`
regions to represent explicit `BoundUnsafe` scopes without changing
`bodyLayout`. The writer lowers scopes into nested lexical regions and rejects
unsafe calls without one; the reader validates region ancestry, entry/exit
edges, loop backedges and the zero cleanup field before reconstructing call
authorization from region membership. Returns and fail-fast terminators may
end paths inside a region. Transfers that leave an unsafe region,
compact-linear unsafe calls, `unsafe(expr)`, unsafe members, cleanup, unwind
and lifetime authority remain unsupported. This bounded implementation
remains provisional and does not approve the broader proposal.

**Decision requested:** Approve or reject this bounded indexed-Body
unsafe-region contract for adoption in the instruction-set, including
assigning kind `0x06`. This decision is limited to section 3: it does not
approve the dependency-relation profile in section 1 or general output-call
migration in section 4. Until the decision and corresponding specification
change are complete, the existing implementation remains provisional; an
approval here would not authorize those other migrations.

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
