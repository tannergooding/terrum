# Terrum specification proposals

The [metadata design](../il-metadata.md) and bounded
[coded instruction set](../il-instruction-set.md) are approved current
designs outside this proposal directory. The documents below remain proposals,
not approved specifications or claims of implementation. Approval does not
make the metadata or instruction encoding released or freeze pre-v1
revisions. An **accepted direction** records a settled semantic boundary
without approving adjacent grammar or API forms.

- [Grammar proposal](grammar-spec.md) defines notation, accepted
  semantic direction, candidate productions and unresolved grammar.
- [Core-library proposal](core-library-reference.md) records candidate
  declarations, dependency layers and unresolved API contracts.
- [Static-proof and constant-evaluation proposal](static-proof-and-constant-evaluation.md)
  collects candidate minimum proof rules, effects and evaluator budgets;
  the portable-floor policy itself is accepted.
- [Semantic IL extensions](il-extensions.md) collect the unapproved inline
  built-in type word and compact-profile follow-up, separate from the
  approved `(1, 0)` designs.

These proposals are the homes for unresolved source syntax, API signatures
and format extensions, not a second checklist of accepted policy or
implementation tasks. Accepted call, output, cleanup and proof contracts are
in [language contracts](../language-contracts.md); detailed lifetime,
conversion, layout, type-family, memory and library direction remains in
its corresponding canonical document. Implementation and remaining
specification sequencing are in the [roadmap](../roadmap.md).

Canonical language and library direction is described in
[language](../language.md), [libraries](../libraries.md),
[memory model](../memory-model.md), and [roadmap](../roadmap.md).
Implementation history and support boundaries remain in
[implementation](../implementation.md) and
[self-host components](../../self-host/README.md).
