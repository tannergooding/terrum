# Address-only nested backing for native storage addresses

**Status: unapproved; provisionally actionable only under the user's explicit
missing-contract workflow.** This remains a separate source/storage admission
decision for
[nested storage-address paths](native-storage-address-path-private-persistence.md).
It changes no bytes, tag, Standard schema or normative owner.

## Decision and exact supersession

Admit a recursively initialized nested direct-local array only as backing
for an address path passed to an already admitted **direct NativeImport**
`NativeParameter` formal with exact type/access, `NoEscape`, and a supported
unsigned minimum extent. This supplies the previously missing positive
backing route for a nonempty tag-35 ancestor path; the path list by itself
does not create storage or initialization authority.

The complete unapproved rules now live in the path-persistence owner's
[construction-only nested backing](native-storage-address-path-private-persistence.md#construction-only-nested-backing)
and [capture, suffix, access and execution semantics](native-storage-address-path-private-persistence.md#capture-suffix-access-and-execution-semantics).
The base also carries the public-API fixture and acceptance gates. This
decision record preserves the additional approval boundary, not a second
complete codec or runtime proposal.

When selected with the path bytes, replace the draft agreement
`terrum/private-semantic-101-storage-address-paths-1` with the exact identity
**`terrum/private-semantic-101-storage-address-only-backing-1`**. Keep the
on-wire i32 header **101**, the 100-plus-path schema, tag-35 ancestor list,
final-index alias, tag-43 envelope and every record width unchanged. This is
an intentional change to the unpublished schema's semantic admission, not a
new byte decoder: the old identity is not an alias or supported alternative.
The exact identity is out-of-band, so matching bytes/header alone do not
establish agreement. No dual decoder, header-only conversion or historical
compatibility promise follows.

## Preserved exclusions and independent owners

The nested local is constructed from exact recursive tag-43 initializers,
with positive dimensions and an independently admitted positive-size,
rootless-Copy leaf. It is published only after complete ordered construction.
Use is limited to direct non-retaining address adaptation. This does not add
nested array reads/writes/copies, whole-array or field places, vacant
`init`/`init mut` backing, zero-count/zero-stride or zero-payload nested
backing, pointer-to-array values, indirect/vtable calls, NativeOutput,
ownership/root/pin/loan acquisition or lifetime extension. Tag 44–46 remain
flat-only. Failure publishes no partial root or address.

The [revision-100 private array candidate](native-fixed-array-private-persistence.md)
continues to own the 44-byte type table and tags 43–46; this amendment does
not reinterpret its exact 100 selection or combine it with candidates 93–98.
The uncommitted [revision-99 storage-address extent candidate](native-storage-address-private-persistence.md)
remains a distinct ancestor in the private schema chain. The source structural
array contract, Standard F+S+A2 schema, its NativeParameter/body routes,
Windows ABI, Runtime policy, and field/nonlocal place contracts remain
separate decisions.

Formal approval of the revised 101 identity and this address-only backing
admission remains outstanding. The base proposal's
[public-API acceptance fixture](native-storage-address-path-private-persistence.md#required-acceptance-cases)
is a future implementation gate, not evidence that the full private or
Standard route currently works.
