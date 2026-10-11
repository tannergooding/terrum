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
| REF-28 | `init ref<int32> value` with `Create(init destination)` | Require vacant storage and the declared publication relation |
| REF-29 | `mut init ref<int32> value` | Diagnose: the promised initialization destination is not rebindable |
| REF-30 | `implicit ref<int32> value` versus `ref<int32> value` | Same semantic signature; adaptation does not distinguish overloads |
| REF-31 | `mut ref<int32> value` versus `ref<int32> value` | Same signature; local binding mutability does not distinguish overloads |
| REF-32 | `write ref<int32>` versus `ref<int32>` | Distinct complete parameter types; attenuation cannot upgrade readonly input |
| REF-33 | `implicit ref<ref<int32>> value` supplied an existing descriptor place | Borrow the descriptor slot, rather than dereference it |
| REF-34 | `(write ref<int32> value) => ...` | Parse the same parameter type as a named callable |
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
| 5 | `01 02 8B 06` | `pin ref<int8>` |

With `typeSpec=2`, Param mode `00` is an ordinary descriptor parameter;
mode `01` is an initialization destination. Flag `01` on mode `00`
publishes implicit adaptation without changing identity. The following
fragments reject:

| Case | Invalid record | Required rejection |
|---|---|---|
| META-REF-01 | Param mode `02`, `03`, or `04` | Reserved mode; no legacy passing alias |
| META-REF-02 | Param mode `01`, `typeSpec=3` | Initialization destination requires an unqualified outer `ref<T>` |
| META-REF-03 | Param mode `01`, flag `01` | Initialization and implicit materialization are distinct |
| META-REF-04 | TypeSpec `01 02 8C 06` | Pin qualification on `ptr<int8>` is invalid |
| META-REF-05 | TypeSpec `07 00 06 00 00 00` as row 4 | Forward TypeSpec reference rejects |

Recognition of these types or modes does not activate them in a narrow
execution profile. A consumer lacking their required execution contract
rejects before use instead of reinterpreting them as scalar values.
