# Artifact-local fixed-array metadata boundary

**Status: unapproved; provisionally actionable only under the user's explicit
missing-contract authorization.** This is a separate projection and
replacement decision for
[private fixed-array persistence](native-fixed-array-private-persistence.md).
It changes no bytes, schema identity, runtime admission or approval status.

The complete revision-100 metadata and projection contract is integrated in
the owner's [metadata-only nested and zero-payload types](native-fixed-array-private-persistence.md#metadata-only-nested-and-zero-payload-types)
and [dependencies, structural remapping and replacement](native-fixed-array-private-persistence.md#dependencies-structural-remapping-and-replacement)
sections. This record preserves the independently unapproved choice:

* Full modules, sets, graph libraries and source-free execution artifacts
  retain the complete closure of their retained implementation records,
  including valid descriptive nested-array, nominal-field, pointer-chain and
  function-pointer recipes.
* Bodyless references and Contract-only projections retain only existing
  legally exported runtime roots and their required dependencies. Unrooted
  metadata-only recipes may be pruned; a type name, implementation row or
  metadata presence does not create a type-only export/import identity.
* Pruning cannot repair malformed selected input, admit an excluded runtime
  occurrence, erase an offending export, or discard a non-array fact actually
  relied on by a retained contract. A pruned bodyless reference cannot
  recreate implementation bodies or descriptions.

The exact candidate remains
`terrum/private-semantic-100-structural-fixed-arrays-1`. The private byte
layout, revision-100 selection, flat direct-local operations and Standard A2
separation remain owned by the base proposal. This decision adds no source,
API, wire carrier, provider search, compatibility reader or runtime feature;
formal approval and implementation qualification remain outstanding.

Current support belongs in the
[implementation inventories](../implementation.md), not this decision record.
Passing prototypes or a private projection test would not
approve the candidate or establish Standard TRRM support.
