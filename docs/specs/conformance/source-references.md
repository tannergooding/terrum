# Reference and grammar conformance cases

These cases apply the [reference chapter](../language/references.md),
[source productions](../grammar-spec.md), and
[lexical rules](../lexical-grammar.md). They are specification cases, not
a claim that the current compiler implements these forms.

Fragments assume ordinary visible declarations and otherwise valid enclosing
bodies. A diagnostic case requires rejection for the stated rule, even if
an implementation also reports another violation.

## Descriptors and access

Assume mutable `int32` places `x` and `y`, a readonly `int32` place `fixed`,
`mut write ref<int32> a`, `write ref<int32> b`, and readonly `ref<int32> ro`.
Assume the descriptors have valid, overlapping backing lifetimes.

| Case | Fragment | Required result |
|---|---|---|
| REF-01 | `a = b;` | Rebind `a`; do not modify either referent |
| REF-02 | `*a = int32(7);` | Write `a`'s referent; do not rebind `a` |
| REF-03 | `b = a;` | Diagnose immutable descriptor binding |
| REF-04 | `*ro = int32(7);` | Diagnose missing immediate write authority |
| REF-05 | `a = ro;` | Diagnose attempted readonly-to-writable upgrade |
| REF-06 | `ref x` | Form a descriptor once, retaining available immediate write authority |
| REF-07 | `ref fixed` | Form only a readonly immediate reference |
| REF-08 | `ref b` | Borrow `b`'s readonly descriptor slot, not `b`'s `int32` referent |
| REF-09 | `ref *b` | Reborrow the `int32` referent; preserve access and backing limits |
| REF-10 | `ref int32(7)` | Diagnose non-place formation; explicit `ref` does not materialize |
| REF-11 | `*a` | Produce an initialized `int32` place without requiring unsafe |
| REF-12 | `a = b;` after `b`'s backing ends | Diagnose invalid dependency; descriptor copying does not extend backing lifetime |

For a complete generic `T = ref<U>`, an argument expression of type `T`
remains a descriptor value. Passing it to a by-value `T` parameter does not
read `U`; borrowing its slot produces the nested reference type.

## Members and pointer access

Assume `r` is a valid `ref<S>`, `p` a `ptr<S>`, and `S.Member` a visible
instance member. `S` is not itself a reference-descriptor declaration.
Assume class handles `C` expose a mutable object member `Value`.

| Case | Fragment | Required result |
|---|---|---|
| REF-13 | `r->Member` | Same place/value, checks, and effects as `(*r).Member`; no implicit `S` copy |
| REF-14 | `r.Member` | Lookup on `ref<S>`, not `S`; diagnose if no descriptor member is eligible |
| REF-15 | `p->Member` outside unsafe | Diagnose missing unsafe authorization |
| REF-16 | `p->Member` inside unsafe | Still require pointer validity, lifetime, alignment, and access |
| REF-17 | `c.Value` for `write C c` | Ordinary class-handle member access; no explicit dereference needed |
| REF-18 | `c->Value` for `C c` | Diagnose: a class handle is not an explicit `ref` or `ptr` descriptor |
| REF-19 | `r->Value = int32(1);` for `write ref<C> r` | Diagnose missing object-write authority despite writable handle slot |
| REF-20 | `r->Value = int32(1);` for `ref<write C> r` | Permit eligible object mutation without handle-slot replacement |
| REF-21 | `*r = replacement;` for `ref<write C> r` | Diagnose missing handle-slot write authority |
| REF-22 | `r->Method(RebindR())` | Capture the original receiver before the argument; evaluate `r` once |
| REF-23 | `r -> Member` | Diagnose member-punctuator spacing; `r->Member`, `r-> Member`, and `r ->Member` parse |
| REF-24 | `*r.Member` | Diagnose missing explicit grouping of the dereference/member boundary |

## Parameters, identity, and grammar

| Case | Fragment or comparison | Required result |
|---|---|---|
| REF-25 | `write ref<int32> value` in a parameter list | Parse as an ordinary complete descriptor type |
| REF-26 | `mut ref<int32> value` in a parameter list | Permit local descriptor rebinding, not referent writes |
| REF-27 | `ref int32 value`, `mut ref int32 value`, or `out int32 value` | Diagnose noncanonical parameter form |
| REF-28 | `ref<init int32> value` with `Create(ref init destination)` | Require vacant storage and the declared publication relation |
| REF-29 | Rebind a direct `ref<init int32>` parameter, including a `mut` binding | Reject rebinding; its promised destination is fixed |
| REF-30 | `implicit ref<int32> value` versus `ref<int32> value` | Same semantic signature; adaptation does not distinguish overloads |
| REF-31 | `mut ref<int32> value` versus `ref<int32> value` | Same signature; local binding mutability does not distinguish overloads |
| REF-32 | `write ref<int32>` versus `ref<int32>` | Distinct complete parameter types; attenuation cannot upgrade readonly input |
| REF-33 | `implicit ref<ref<int32>> value` supplied an existing descriptor place | Borrow the descriptor slot, rather than dereference it |
| REF-34 | `(write ref<int32> value) => ...` | Parse the same parameter type as a named callable |
| REF-35 | `gc ref place` for one initialized, addressable place in GC-backed storage | Form an independent root for that exact place; do not promise a stable address or sibling access |
| REF-36 | `ref place` with expected type `gc ref<T>` | Form the same exact-place root under the expected complete type |
| REF-37 | `ref *root` where `root` is `gc ref<T>` | Reborrow the exact referent as stable `ref<T>` and pin backing for the bounded result |
| REF-38 | Copy a `ref<T>` produced by `ref *root` | Preserve the existing pin dependency through every live copy; do not broaden access |
| REF-39 | `C(gcObject)` where `gcObject` has type `gc C` | Explicitly produce stable `C` by pinning the whole object; copies share support until the final converted handle retires |
| REF-40 | `unsafe(&*stableRef)` | Explicitly extract a non-owning, non-rooting `ptr<T>`; retain its backing/pin dependency and require unsafe authorization |
| REF-41 | `gc ref<init T>` | Reject; no GC-rooted initialization-reference form is selected |
| REF-42 | `gc ref<T>` passed directly to a `ptr<T>` parameter | Reject; no implicit pointer conversion or overload-ranking edge |
| REF-43 | `write ref<init int32>` initializes vacant `x`, then writes `2` and `3` through the reference | Permit initialization before reads, followed by repeated whole-place writes; the initialization capability attenuates to `write ref<int32>` |
| REF-44 | `ref<init write C>` initializes a writable class handle, then mutates its object member | Permit inner object mutation after initialization; reject replacing the whole handle slot without outer `write` |
| REF-45 | `write ref<init write S>` for a mutation-capable inline `S` | Permit both inner mutation and repeated whole-place writes after initialization |
| REF-46 | `write gc ref<T>` where rooted `T` permits writes | Permit the combined rooted/writable form; preserve the exact root and access obligations |
| REF-47 | `ref<init gc C>` for a storage-eligible class-handle destination | Permit initialization of the GC-backed handle value; do not root or pin the destination place |
| GRAM-01 | `own<val<C>>` | Parse reserved `own` as a type constructor; enforce family eligibility separately |
| GRAM-02 | `Lookup<int32>.Found(value)` or `Lookup<int32>.Missing` | Parse a generic receiver without requiring an intervening type call |
| GRAM-03 | `public int32 Count { get; init; } = int32(0);` | Parse the backed-property initializer suffix; enforce construction rules separately |
| GRAM-04 | `ref place` in an initializer or ordinary argument | Parse explicit reference formation, not an argument-only passing marker |

## Metadata fragments

The [metadata contract](../il-metadata.md) gives these exact TypeSpec row
payloads. A containing artifact must additionally provide framing, ownership,
feature requirements, and all required tables; these row fragments are not
standalone executable artifacts.

| Row | Bytes | Complete type |
|---:|---|---|
| 1 | `01 00 06` | `int8` |
| 2 | `01 00 8B 06` | `ref<int8>` |
| 3 | `01 01 8B 06` | `write ref<int8>` |
| 4 | `07 00 02 00 00 00` | `ref<ref<int8>>`, referring to earlier row 2 |
| 5 | `0E 00 01 00 00 00` | `ref<init int8>`, referring to earlier row 1 |
| 6 | `0E 01 01 00 00 00` | `write ref<init int8>`, preserving outer write authority |

With `typeSpec=5` or `6`, Param mode `00` is an ordinary descriptor parameter.
Param mode `01` is reserved and invalid in producer output. Flag `01` on mode `00` publishes implicit
adaptation without changing identity and cannot form or forward an
initialization capability. The following fragments violate the selected
producer schema:

| Case | Invalid producer output | Producer obligation |
|---|---|---|
| META-REF-01 | Param mode `01`, `02`, `03`, or `04` | Emit only valid assigned modes; initialization is carried by the complete `ref<init T>` type with ordinary mode `00` |
| META-REF-02 | Param mode `00`, flag `01`, complete type `ref<init T>` | Do not publish implicit adaptation that forms or forwards an initialization capability |
| META-REF-03 | `ref<init T>` is encoded as mode `01` plus `ref<T>` | Emit it as a distinct complete TypeSpec, not a parameter-only mode reinterpretation |
| META-REF-04 | TypeSpec uses `gc ref<init T>`, `gc ptr<T>`, or `ptr<init T>` | Reject the invalid modifier combination; `ref<init ptr<T>>` initializes the pointer cell |
| META-REF-05 | TypeSpec uses the `init` qualifier outside a Span argument or encodes `ref<init T>` as Param mode `01` | Reject; Span init is whole-range element state, and reference init uses TypeSpec form `0x0E` with Param mode `00` |
| META-REF-06 | TypeSpec `07 00 06 00 00 00` as row 4 | Do not emit a forward TypeSpec reference |

Recognition does not itself establish executable support. Before use, a
consumer validates complete type and place/publication record structure,
references and transitive capability prerequisites; it rejects affected use
when required forms are unsupported or structurally invalid. The producer
remains responsible for the truth of published place/publication facts.
Unsupported forms are not reinterpreted as scalar values. A partial
implementation profile does not define a reduced TRRM 1.0 conformance floor.
