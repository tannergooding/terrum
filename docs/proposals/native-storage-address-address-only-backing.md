# Address-only nested backing for native storage addresses

**Status: unapproved; provisionally actionable only under the user's explicit
missing-contract workflow.** This remains a separate source/storage admission
decision for
[nested storage-address paths](native-storage-address-path-private-persistence.md).

## Decision and supersession

The path-persistence proposal is the complete owner of the revised exact
private revision-101 candidate,
`terrum/private-semantic-101-storage-address-only-backing-1`. It incorporates
this construction-only nested-backing decision, including the wire recipe,
reconstruction, checks and public-API acceptance cases. The earlier
`terrum/private-semantic-101-storage-address-paths-1` identity is superseded,
not an alias. The identity is selected out of band: matching header 101 or
bytes alone does not establish schema agreement, and no dual decoder follows.

The selected path remains an **ancestor-only** tag-35 list; the existing final
`Index` is retained once as the suffix. Each ancestor and the final index are
checked and evaluated exactly once in order. The exact leaf minimum extent
must fit the final immediate parent's full `N-i` suffix; outer suffixes count
inner array objects and are never flattened into leaf bytes.

Positive nested backing is constructed recursively through exact tag-43
initializers, with positive dimensions and a positive-size, rootless-Copy
leaf. Publish the root only after complete ordered construction. This backing
is admitted solely for a direct, already-admitted `NativeImport`
`NativeParameter` with exact pointee/access, `NoEscape`, and a supported
unsigned minimum extent. The resulting address is non-owning and
call-confined.

## Preserved boundaries

The revision-101 identity retains the tag-35 ancestor path, final-index alias,
tag-43 envelope and existing record widths; it does not change the i32 header
or add a tag. It admits no nested reads, writes, copies or general value/place
operations; tags 44–46 remain flat-only. Vacant backing, zero dimensions,
non-positive extents, non-rootless-Copy leaves, indirect/vtable calls,
NativeOutput, ownership/root/pin/loan acquisition and lifetime extension
remain excluded. Failure publishes no partial root or address.

The [revision-100 private array candidate](native-fixed-array-private-persistence.md)
continues to own the 44-byte type table and tags 43–46; this decision does not
reinterpret revision 100 or combine it with candidates 93–98. The revision-99
storage-address extent candidate remains a distinct ancestor. Source structural
arrays, Standard F+S+A2, Windows ABI, runtime policy and other place contracts
remain separate decisions.

Formal approval of the revised 101 identity and this admission decision
remains outstanding. The path-persistence owner's
[acceptance cases](native-storage-address-path-private-persistence.md#required-acceptance-cases)
are future implementation gates, not evidence that the private or Standard
route currently works.
