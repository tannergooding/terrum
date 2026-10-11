# Constant-generic domains and value identity

This chapter owns constant-formal eligibility and canonical generic-value
identity under `constant-generics/1`. [Complete types](types-and-normalization.md)
retain the declared domain and ordered arguments; [calls and inference](calls-and-inference.md)
select and substitute formals. [Constant execution](proof-and-constant-evaluation.md)
owns required evaluation, validity, emitted values and resource accounting.
Eligibility for an ordinary constant or default does not imply eligibility
for a constant-generic argument.

## Initial domains

Normalize aliases and complete type arguments before checking this catalog.
A type name, matching field layout or behavioral interface is not a domain.

| Domain | Required value identity |
|---|---|
| `int8`, `int16`, `int32`, `int64`, `int128` | Exact signed mathematical integer in that type's range |
| `uint8`, `uint16`, `uint32`, `uint64`, `uint128` | Exact unsigned mathematical integer in that type's range |
| `bool` | Exactly false or true |
| Admitted nominal enum over one of those fixed-width integers | Exact enum type and underlying integer value |
| Eligible Transparent primitive wrapper | Exact complete nominal wrapper type and its integer/Boolean carrier value |

Enum identity does not depend on a chosen member spelling. Duplicate-valued
members denote the same value in that enum; unnamed underlying values remain
valid under the [enum contract](../grammar-spec.md#31-source-file-structure-and-declarations).
Another enum or the underlying integer type remains a different domain.
`Flags` does not change this identity or restrict it to named combinations.
An enum using another underlying domain needs a separately admitted contract;
this catalog does not choose additional enum bases.

Target-width `nint`/`nuint`, character/code-unit types, floating/decimal values,
`void`, optionals, tuples, general aggregates, references, pointers, ownership
or managed handles, and type-query evidence are not initial constant-formal
domains. Their availability in other value/type positions or constant
execution does not enlarge this catalog. In particular, neither a known
native width nor a finite floating value turns it into a fixed-integer
constant-formal argument.

## Derived Transparent wrapper eligibility

The existing [Transparent commitment](../data-layout-and-abi.md#trivial-wrappers-are-not-arbitrary-small-aggregates)
supplies the carrier relationship. No `ConstantDomain` attribute, carrier-
exposure interface, generated member or second opt-in is added.
Eligibility is derived only when the complete inline nominal type satisfies
all of these checked obligations:

- Its resolved Transparent contract identifies exactly one instance field,
  with the carrier's representation and argument/result classification.
- That field's normalized complete type is one of the fixed-width integers
  above or `bool`. A transparent alias of that primitive is equivalent.
- Its value contract supplies Copy, frozen validity, the transitive per-target
  Blittable commitments and an admitted typed value codec. Each formed
  argument must be fully initialized and valid.
- The producer publishes the carrier relationship, validity and constant
  construction/value obligations for independent consumers, rather than
  deriving them from a replaceable private body or current field shape.

Static fields are not instance fields. An additional zero-data instance field
still violates the exactly-one-field requirement. `Auto`, `Sequential`,
equal size, Blittable alone or a same-spelled layout attribute does not supply
the Transparent relationship. Conversely, Transparent alone supplies neither
Copy nor frozen validity, Blittable or a value codec.

This is the selected one-field primitive-carrier subset. A field of enum,
another nominal wrapper, optional, floating or reference type is not silently
flattened into an integer/Boolean domain. Other wrapper families need their
own admitted rules; equal ultimate bits are not sufficient.
Generic wrapper instances use ordinary forward substitution of their
published field/contract terms. Different complete generic arguments remain
part of the wrapper's nominal identity even when none changes its carrier.
An earlier type formal may remain symbolic in that argument tree when the
published field and formation contracts already prove eligibility for every
admitted substitution. For example, a checked `Tagged<T>` with an int32
field can supply the domain in `const Tagged<T> N` without knowing its tag.
Retain that formal's identity until a closed use; do not erase the tag from
the constant domain or evaluate a value with missing required dependencies.

An unresolved domain is not repaired by inspecting eventual instantiations.
A generic declaration must establish its required domain/formation facts
from its declared terms and published guarantees. Copy, Blittable or an
integer-operation witness alone does not prove membership in this closed
catalog; it can also describe types outside it. Symbolic forwarding retains
its existing checked obligations and supplies no inverse carrier inference.

Eligibility does not expose a private field, create a literal parser,
conversion or operator witness, permit raw reinterpretation, or admit all
carrier bit patterns as valid wrapper values. A nonnegative or even-only
invariant remains meaningful. Available construction remains an ordinary
checked API, not a synthesized method selected by name.

## Canonical value identity

A canonical constant argument consists of its exact normalized complete
domain and its valid value from the table above. The domain retains nominal
definition/module/source-unit identity, representation and ordered complete
generic arguments. Transparent layout does not erase any of them.

Integer values compare mathematically in their declared domain; Boolean
values compare logically. Enum and eligible wrapper values compare their
carrier values only after their exact complete domains agree. Signed zero
spellings of an integer denote the same zero; range overflow or an invalid
representation does not wrap into a canonical value. Boolean carrier
encoding cannot introduce a third logical value.

For example, assume `NeverNegativeInt32` has the checked wrapper commitments
above and an accessible, eligible ConstEval `Create` factory:

```terrum
struct FixedBuffer<T, const NeverNegativeInt32 Length> { }

alias A = FixedBuffer<uint8, (NeverNegativeInt32.Create(16))>;
alias B = FixedBuffer<uint8, (NeverNegativeInt32.Create(8 + 8))>;
```

`A` and `B` denote the same complete type. Another nominal wrapper containing
16 does not supply that argument. `Create(-1)` still fails its declared
validity check; generic identity cannot manufacture a valid wrapper.

Canonical comparison, deduplication and internal hashing never invoke user
`Equals`, `GetHashCode`, comparison operators, constructors or formatters.
An authored `N == M` still needs its ordinary operator/witness contract.
Equality of constant arguments is not an automatically supplied source
operation or a permission to erase observable construction.

Source radix, separators, member aliases, factory identity, evaluation site,
target byte order, padding and physical metadata row numbers do not add
value identity. Target materialization follows the retained typed value and
ABI contract, not host object bytes. Equal values in different nominal
domains never merge solely because their storage encodings match.

## Formation, binding and forwarding

`const Domain P` retains the selected normalized domain and its formal owner/
ordinal. A supplied typed constant must have that exact complete domain;
carrier extraction, nominal wrapping, numeric promotion and representation
conversion are not implicit argument binding. An explicitly authored
conversion may form a new eligible value under its own contract.

An untyped literal receives the established domain's ordinary literal
construction context. Wrapper eligibility alone registers no parser.
Kind/signature selection precedes parsing and validity; failure does not
retry another formal domain, overload or construction API. Direct literals
and qualified constant names retain their existing generic-argument syntax;
other expressions, including factory calls, require grouping.

New value formation executes the selected eligible ConstEval body or
admitted intrinsic, including preconditions, initialization and validity
checks, under the existing constant-request resource envelope.
Only a complete valid admitted result supplies an argument. Refusal,
failed checks, unavailable dependencies, unsupported operations or exhausted
required evaluation diagnose; they do not select a runtime fallback,
default value, raw carrier construction or another overload.

A previously admitted constant or earlier constant formal can be forwarded
without rerunning its constructor. Its exact domain, value and dependencies
remain retained. This is reuse of a checked value, not permission to fold
away an independently authored construction with different obligations.

Explicit arguments pin their positions; inferred arguments use existing
typed/structural evidence. Forward matching can recover a known constant
argument from a matching injective nominal shape. It does not solve `N + 1`,
invert a wrapper constructor, strip nominal domains or infer an unknown
domain from convenient values.
Defaults depend only on earlier formals and must produce a valid value of
the selected domain. `_` still requires inference and cannot fall back to
a default; explicit `default` selects the published earlier-formal expression.
Ordinary aggregate/parameter defaults keep their wider constant domain.

## Retained contracts and compatibility

| Field | Required meaning |
|---|---|
| Rules and domain | `constant-generics/1`, exact normalized domain and applicable primitive/enum/wrapper kind |
| Carrier | Exact integer/Boolean type; enum base or checked Transparent instance-field relationship where applicable |
| Value | Complete initialized valid mathematical/logical carrier value, retaining the nominal domain rather than a host blob |
| Wrapper obligations | Resolved Transparent, Copy, frozen validity, per-target Blittable and typed codec commitments |
| Formal and expression | Declaring owner/ordinal, domain terms, earlier-formal default dependencies and already-bound operation identities |
| Dependencies | Construction/validity/value-codec rules, required programs or intrinsics, target ABI and definition/load identities |

The defining module publishes these checked contracts; an unrelated package
cannot attach eligibility to another type. Repeated imports of the same
definition/contract preserve identity, not import-order priority.
Consumers establish validity through the selected admission/trust policy,
not an unstated execution of the current constructor or equality body.
Unknown required rules, incompatible carrier/validity contracts or malformed
values reject before they can justify a generic instantiation.

Linking and remapping preserve the complete domain and canonical value while
changing physical references. Independently supplied records cannot erase
the nominal type or replace declared validity with arbitrary carrier bits.
Removing required eligibility, changing the carrier or canonical value
identity, or invalidating previously promised valid values breaks dependent
contracts. Documented constant-construction behavior retains its existing
compatibility promise. A private field rename or implementation change does
not alone change value identity, but must preserve every retained obligation.

The [metadata requirements](../representation/logical-records.md#constant-generic-domains-encoding-pending)
are logical only. No TypeSpec form, attribute schema/value tag, table, clause,
feature bit or opcode is activated; physical generic/value/validity records
remain separately gated.
[Conformance cases](../conformance/source-constant-generics.md) distinguish
domain eligibility, canonical identity, checked construction, forwarding
and independent compatibility from compiler or physical codec execution.
