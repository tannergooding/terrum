# Frozen native-module fixtures

The frozen `scoped-view-*` packages exercise compatibility with semantic
module revision 16, which records readonly-view contracts. Their source
programs are in `samples/native/graph`:
`consumer.terrum`, `middle.terrum`, and `scoped-view-leaf-replacement.terrum`.
The root and middle were compiled against `scoped-view-leaf.terrum`, whose
unchanged public contract permits the readonly implementation replacement.
The frozen graph returns 44 under AOT and root-only JIT. The compatibility
test replaces only the leaf with `writable-view-leaf.terrum`, returning 46
while preserving the frozen root and middle byte-for-byte.

| Package | SHA-256 |
| --- | --- |
| `scoped-view-fail-fast-root.trng` | `11958CC2BE99A8A7AD5E89F161D0809AB6986319237F1B602190CE1F82B00289` |
| `scoped-view-fail-fast-middle.trng` | `0E620A2328EFE99646FEFCDE03D025869AD0FE273D1BBBABC27CCA69CB389DA2` |
| `scoped-view-fail-fast-replacement.trmn` | `7D1732CF8E0770FB5DECB071BC6774A6002D8E9ADBD39ACEE2A1133D5D14DD33` |
| `scoped-view-checked-exceptions-root.trng` | `6EA5309F3C645E481A22B0AB25954738134D5275BDE3F4C8A16D98EE15BC4C7C` |
| `scoped-view-checked-exceptions-middle.trng` | `2DCDEE14F3F8DBDEA6107A9004D0C4397947B466A0BB42DF937269B13966CC1C` |
| `scoped-view-checked-exceptions-replacement.trmn` | `B25697180BB47E0BA6C9A535D6A1064927BA28A7A0E55FD1DED5CB856EC877A9` |

Each embedded `TRRM` contract is revision 16. The graph packages use graph
container format v2; readonly replacement leaves use package format v1 and
the `native-16` profile. Roots and intermediates retain their original
`native-10` or `native-11` profiles; semantic revisions, package formats and
native profiles are separate compatibility gates.

Keep these package bytes fixed. They are older-format and semantic-v16
compatibility inputs consumed by current readers, not golden outputs that
should track the current serializer. Regenerating them would replace the
semantic-v16 reader case with a newer contract.
