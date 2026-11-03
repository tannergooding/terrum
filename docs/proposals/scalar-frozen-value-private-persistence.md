# Historical private scalar persistence draft

**Status: superseded, unapproved historical proposal.** This file records no
active codec, implementation target or compatibility promise. The sole
candidate owner is
[Wide scalar value carriers](scalar-wide-value-carriers.md).

## Supersession and preserved boundary

The former revision-94 recommendation is incorporated into the wide-carrier
proposal, which preserves its type-determined tag-1 payload rationale and
exact revision-89/90/91/92/93/94/95 composition while correcting the source
baseline. The host already has all 24 scalar identities and a two-u64
`ScalarBits` carrier; the remaining private gap is the complete type
catalog/codec integration, not creation of a wider host number.

Use the wide-carrier proposal for the exact revision-94 bytes, complete
scalar widths/identities, tag-1 replacement, source-free materialization,
remapping/replacement and required tests. Do not retain this file as an
alternative revision-94 layout or decoder; equal header bytes do not imply
schema agreement. The selected Standard Feature 11/1 scalar registration
remains a separate unapproved proposal and is not approved by private
persistence consolidation.

Historical implementation observations and acceptance details are not
repeated here. Current support belongs in the
[implementation reference](../implementation.md) and
[conformance handoff](../implementation/conformance-gaps.md); prototypes,
source baselines and private codec evidence do not establish Standard TRRM
support or language approval.
