# Frozen native-module fixtures

The `scoped-view-*` packages preserve the semantic-v16 wire format introduced
in `1633a6f`. Their source programs are in `samples/native/graph`:
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

All six packages have valid package checksums and contain actual version-16
`TRRM` semantic contracts. The graph packages are format v2; the readonly
replacement leaves are format v1 and carry the `native-16` profile. Roots and
intermediates retain their original `native-10` or `native-11` profiles.
Do not regenerate these fixtures with the current version-17 serializer:
their historical wire version is the compatibility test.
