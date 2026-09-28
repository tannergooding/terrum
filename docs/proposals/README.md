# Terrum specification proposals

The metadata design is approved as current intent; the other documents remain
proposals, not approved specifications or claims of implementation. Approval
does not make the metadata encoding released or freeze pre-v1 revisions. An
**accepted direction** records a settled semantic boundary without approving
adjacent grammar, instruction, or API forms.

- [Grammar proposal](terrum-grammar-spec.md) defines notation, accepted
  semantic direction, candidate productions and unresolved grammar.
- [IL and metadata design](terrum-il-metadata-spec.md) specifies the approved
  current framing and metadata contracts, with remaining unsupported and
  unspecified surfaces called out explicitly. Current compiler support is
  documented in [implementation](../implementation.md).
- [Coded instruction proposal](terrum-il-instruction-set.md) assigns an
  initial Body encoding and operation subset alongside the metadata design;
  it does not describe the current module writer.
- [Core-library proposal](terrum-core-library-reference.md) records candidate
  declarations, dependency layers and unresolved API contracts.

Canonical language and library direction is described in
[language](../language.md), [libraries](../libraries.md),
[memory model](../memory-model.md), and [roadmap](../roadmap.md).
Implementation history and support boundaries remain in
[implementation](../implementation.md) and
[self-host components](../../self-host/README.md).
