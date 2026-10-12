# Bootstrap source file-scoped namespace

**Status: UNAPPROVED recommendation.**

The provisional direct-source bootstrap route accepts the bounded
`int32 Main()` and helper subset only from the global namespace. Recommend
allowing one leading file-scoped namespace declaration with one or more
dot-separated identifier components, followed by the existing declaration
subset. The namespace applies through end of file; `Main` remains the unique
public parameterless entry point, and helper calls remain unqualified and
resolve only to declarations in that same source namespace. Keep declaration
order, helper bodies, argument forwarding and emitted PE bytes unchanged.

Do not add nested namespace blocks, aliases, imports, qualified call
expressions, multiple namespace declarations or declarations outside the
file-scoped namespace. Reject malformed headers and duplicate declarations
without publishing source code; the existing PE writer retains its independent
length reset and image validation. Namespace identity is source-local for this
route; this does not add linking, general name lookup, a namespace metadata
encoding or a canonical grammar change.

This recommendation is a bounded parser milestone, not acceptance of a
language or metadata contract. Any implementation remains provisional until
the recommendation is reviewed.
