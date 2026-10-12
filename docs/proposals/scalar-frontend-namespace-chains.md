# Scalar frontend namespace block chains

**Status: UNAPPROVED recommendation.**

Recommend extending the complete-program scalar frontend from one nested child
block to a single namespace-block chain of up to eight levels total. Each
parent block must contain only the next namespace block; direct declarations
are allowed only in the terminal leaf. Siblings, repeated nested blocks,
declarations mixed with a child block, and namespace chains beyond the bound
remain unsupported. File-scoped namespaces and repeated flat same-identity
blocks retain their existing separate rules.

Up to twelve internal, non-generic scalar aliases may be declared directly in
the terminal leaf and are visible only to functions in that leaf, following
the separate [nested namespace alias recommendation](scalar-frontend-nested-namespace-aliases.md).
Parent aliases are not inherited. Keep v88's innermost namespace-name span;
the hosted reader must independently validate every namespace header, brace,
leaf span and fully qualified declaration identity from the original source.
Function, token, source and alias capacities remain unchanged.

This bounded implementation recommendation does not amend canonical grammar,
approve general namespace lookup, or change the proposal status.
