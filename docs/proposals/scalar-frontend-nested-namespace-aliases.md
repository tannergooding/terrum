# Scalar frontend aliases in one nested namespace leaf

**Status: UNAPPROVED recommendation.**

The grammar permits namespace-level aliases and nested namespace blocks but
does not specify whether an alias in an enclosing block is visible in a nested
namespace. Recommend, for the bounded complete-program scalar frontend, allowing
up to twelve existing internal, non-generic scalar aliases declared directly
in the supported terminal namespace leaf and resolving them only for
declarations in that leaf. Do not inherit parent aliases or extend this slice
to repeated flat blocks, siblings, other namespaces, alias conversions,
metadata or persisted outputs. The separate
[namespace-chain recommendation](scalar-frontend-namespace-chains.md)
proposes a bounded chain whose terminal leaf retains this same alias scope.

This recommendation does not amend the canonical grammar or approve general
nested namespace alias lookup. The implementation remains provisional.
