# Static initialization metadata and attribute schema

> **Status: unapproved proposal.** This document records a recommended
> encoding and source-attribute schema for the already accepted
> [static initialization contract](../grammar-spec.md#static-initialization-and-load-scoped-storage).
> It does not change that contract or claim compiler support.

## Summary

The accepted source contract defines type-wide initialization groups,
explicit static-constructor triggers, individually lazy fields, ordered
initializers, completion publication, failure behavior and load-scoped
cleanup. The approved `(1, 0)` metadata design does not encode static storage,
initialization groups, trigger policy or lazy state. The source grammar also
leaves the concrete semantic-attribute schemas open. Consequently, compiler
binding, persistence and runtime adoption cannot proceed safely by treating
these attributes as ordinary names or by inheriting host static-constructor
behavior.

## Decision

Which resolved semantic-attribute schemas and required metadata records should
carry the accepted static-initialization contract?

**Recommendation:** define `StaticInitializer` and `LazyInitialization` as
compiler-recognized attributes with closed schemas, then persist their
normalized meaning in required static-initialization records. Keep the
accepted execution, failure, publication, sharing and unload rules unchanged.

### Attribute schemas

- `StaticInitializer` requires exactly one string constant argument, with one
  of the ordinal values `"grouped-static-field-access"` or
  `"runtime-static-member-access"`. It has no default and applies only to the
  primary authored static constructor. The resolved attribute identity, not
  its spelling alone, selects this schema.
- `LazyInitialization` takes no arguments and applies only to an ordinary,
  non-`extern` static field. It does not apply to constants or property
  backing storage.
- Neither attribute permits repetition. Partial declarations retain the
  accepted primary-only policy authorship; a secondary static-constructor
  implementation cannot restate or change the trigger.
- Both schemas use the ordinary checked constant evaluator and exact resolved
  identity rules. An arbitrary same-named user attribute cannot select these
  compiler behaviors.

The string values provide an explicit, readable closed choice without
requiring enum support or attribute-specific expression syntax. The compiler
normalizes them to a closed trigger tag; raw source strings are not runtime
policy.

### Metadata records

- Add required records for initialization groups, grouped members, lazy
  fields and authored constructor policy/implementation. A group is keyed by
  the loaded closed type identity, not merely a generic type definition.
- Reference existing type, field and method identities rather than encoding
  names as runtime identities. Store initializer order explicitly so linking
  preserves compiler-input order and source order, including property backing
  storage positions.
- Persist the normalized trigger and lazy designation as semantic facts.
  Do not overload existing flags, infer policy from row order, or serialize
  the source attribute text as the authoritative contract.
- Treat these records as required semantics. A reader that cannot validate
  their identities, order, effects, authority and runtime dependencies must
  reject the artifact rather than ignore or reinterpret them.

The exact row layouts, definition kinds and revision policy remain for the
metadata design to assign before implementation. Because Terrum is still
pre-1.0 and has no external artifact consumers, the implementation should
maintain one current writer and reader rather than add historical-revision
support. A version boundary for any shipped preview remains an explicit
release decision.

## Implementation boundary

This proposal does not authorize changes to the accepted trigger, initialization,
sharing, failure/recovery, cleanup or unload semantics. It does not make the
current `(1, 0)` metadata flags carry static-initialization policy, and it does
not establish hosted or native compiler support. Adoption still requires
binding, verification, persistence, runtime initialization state and
publication to carry the accepted contract end to end.
