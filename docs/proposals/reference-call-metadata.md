# Reference-call metadata proposal

**Status: unapproved.** This records a recommended first implementation slice
for the roadmap's general reference/access/loan call contracts. It does not
change accepted source semantics, the approved metadata schema, or current
compiler support.

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
bit `0x04` for exactly the unsafe-caller requirement.

**Recommendation:** Map the source declaration to that assigned Effects bit
and propagate the obligation at each call site. Only an explicit unsafe
caller scope discharges it; an implementation's internal unsafe block or
body analysis must not silently change the published contract.

### 3. Which source rules and metadata gate general output-call migration?

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
