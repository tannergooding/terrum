# NativeOutput resource ownership

**Status: unapproved.** This is the distinct resource-adoption decision for
the proposed NativeOutput cell contract. Its complete source, failure,
backing, ownership and typed-interface boundaries are integrated in
[Result-correlated NativeOutput cells — Resource ownership, adoption and
typed interfaces](aggregate-native-outputs.md#resource-ownership-adoption-and-typed-interfaces).
That section is the semantic owner; this file preserves the independent
approval boundary without repeating its codec and acceptance recipe.

NativeOutput publishes a complete T and transfers no resource right or
responsibility by itself. Any authored acquisition/adoption wrapper must
establish the actual provider, backing, lifetime, unique responsibility,
cleanup and failure obligations. A raw pointer/handle cell is not a checked
owner, and a borrow is not transferred ownership.

Typed foreign-interface acquisition must fix or validate the actual interface
identity before publishing a typed owner. A caller-selected IID plus `void**`,
matching slot, pointer equality or cast is insufficient. Foreign
method/provider identity and binding remain in their separate proposal owners;
private persistence, checked-reference outputs and indirect foreign-call
contracts remain independent decisions.

This record assigns no ownership flag, automatic adoption, Cleanup synthesis,
COM API, IID, slot, release ABI, private revision or standard representation.
Consolidating its shared cell language does not approve any of those choices
or the resource extension.
