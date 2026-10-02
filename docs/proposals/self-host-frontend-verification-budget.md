# Self-host frontend verification-budget proposal

**Status: unapproved implementation architecture.** This does not change
language semantics, the accepted source grammar, projection version 59,
native ABI, or the shared 100,000-visit verification limit.

## Summary

The frontend CLI compiles and verifies the scalar parser, projection publisher,
and their scanner dependencies in one compilation. A mechanical addition to
parse and project the hosted `name = _;` lifetime-ending statement caused
compilation of that component to fail with `TRM3016`
(`Source exceeds the semantic verification work limit`).
The error is raised while verifying the component's own Terrum source, before
the new projection can be used. A shorter parser formulation still exceeded
the same fixed budget.

Moving methods into additional source files would not split that verification
unit. The attempted feature and projection changes were removed; projection
version 59 and the existing source-support boundary remain unchanged.

## Decision

How should authored scalar-frontend implementation grow while preserving the
current shared verification limit and all existing component behavior?

## Recommendation

Partition the frontend across independently verified phase components using
an explicit, bounded internal handoff record, retaining the existing
100,000-visit limit for each verification. Choose a boundary that removes
unrelated frontend bodies from the scalar component's verification unit
without changing its accepted inputs or published facts. Do not raise the
global limit or remove existing candidate support to make room for a new
feature. Keep hosted verification/emission dependencies explicit; this
partition would not make the scalar frontend a standalone compiler.

Before implementation, validate that existing fixtures preserve the same
source diagnostics, projection facts, AOT/JIT output and runtime-policy
behavior across the component boundary. The handoff is an implementation
detail of these hosted-verified components, not a new language-level
separate-compilation or module-ABI contract.
