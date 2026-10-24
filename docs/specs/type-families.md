# Type-family design

These are accepted semantic directions expressed as design contracts, not
implemented APIs. The [source grammar](grammar-spec.md) specifies approved
alias, interface, associated-member and witness forms. The spelling of family
declarations, conditional members and extension declaration/import eligibility
remains open; participation
itself requires an explicit family opt-in with declared projections and
constraints. The local pseudo-code below labels relationships rather than
specifying Terrum grammar. Current compiler support for any part of this
document is tracked in [implementation](../implementation.md), not restated here.

## Definitions

The shared [definitions](language.md#definitions-and-notation) explain
copying, aliasing, references and lifetime. Family-specific terms below do
not turn a non-owning view into an owner or change the dependencies carried
by a particular value.

| Term | Meaning |
|---|---|
| Family | A declared, explicitly opted-in set of related types (its members), together with members' common operations and any distinguished specialization. A shared name or generic arity does not by itself establish membership. |
| Member | One of the concrete types that belongs to a family, checked against the family's declared representation, validity, copying/moving, initialization/cleanup, tracing/write-barrier, access/backing-dependency and lifetime obligations. |
| Nominal witness | A type's explicit, checked declaration that it implements a behavioral contract (an interface). Conformance is never inferred from matching members, matching representation, or extension-member lookup alone. |
| Associated type / relation | A named type member of an interface witness (for example `IInteger<TSelf>.Wider`), bound to a concrete type by the implementing witness and resolved through that witness, not inferred backward from a result. |
| Projection | A checked, explicitly declared conversion between related types (for example an erased family member to/from a typed member, or an original type to an adapter) that preserves validity, representation, access, ownership and lifetime. Equal representation bits do not by themselves establish a projection. |
| Erasure | A specific, declared projection that discards static type information (for example a typed `TextSpan<char8>` to `TextSpan<void>`) while forwarding the member's backing, access and lifetime dependencies unchanged. |
| Adapter | An explicitly declared type that carries another type's representation and implements additional behavioral contracts without changing the original type's own witness set. |

## Generic qualification and substitution

The [complete-type rules](language/types-and-normalization.md) define finite
forward substitution and normalization. Ordinary family arguments retain
their complete qualifications. An explicit `box<T>` instead requests that
representation's outer access: `T = write box<S>` gives readonly `box<S>`
without changing `T`, allocating, or dropping qualifications inside an
unremoved inline payload.

Family substitution preserves the complete qualification chosen by the caller.
A concrete default-readonly type does not erase a caller-supplied `write`
capability from `T`; constraints must state which access is required. `mut` is
not valid as a generic argument qualifier. For example, a family member over
`Span<T>` must distinguish descriptor rebinding (`mut Span<T>`), writable
element slots (`write Span<T>`), and writable contained handles
(`Span<write C>`). A writable-storage contract is invariant; a readonly
projection is not automatically covariant and would need a checked
representation/lifetime/cost proof. Generic `write T` is not a universal
make-mutable transform. The [write requirement](language/types-and-normalization.md#generic-write-requirements)
`where T : write` checks the normalized argument's actual immediate access
without changing `T`; mutation operations still need their declared
member/interface/family contract.
A readonly-only constraint on the complete `T` is distinct from projecting an
unknown qualified `T` through a readonly view; projection does not rewrite the
type argument or discard its original authority.

[Whole-source dependencies](language/dependencies-and-views.md) propagate
through stored values, family erasure and checked projection independently
of type arguments. DependsOn/Captures select the result or retained destination;
ForwardsBacking refines source-storage permission, not ownership or access.
The exact member-contract constraint can expose a checked published witness
fact to a generic body. It supplies neither family membership nor conditional
family-declaration syntax, and does not select a Span/TextSpan anchor ABI.

## Explicit families

[Thread-capability contracts](runtime/sharing-and-handoff.md) are explicit
published facts of complete forms. Family erasure and wrappers preserve their
conditional premises and actual protocol/backing/cleanup duties; they cannot
derive an unannotated nominal capability from an eligible carrier or bits.
An alias preserves its normalized target rather than creating another opt-in.

A family opts in explicitly to type-argument expansion and declares its members, including any distinguished specialization. A shared name or generic arity does not establish membership. The checker must preserve each member's representation and ABI, validity, copying and moving, partial initialization and cleanup, tracing and write barriers, access and backing dependencies, and lifetime across projections. An erased member may need a representation tag, a checked dictionary or a changed descriptor ABI; none follows automatically from substituting `void`. Generic extension lookup alone cannot establish a member's validity, representation, interface witnesses or lifetime obligations. Family operations must not turn erased or borrowed views into owning values, invent a `default(T)`, or assume that all members are interchangeable. Projection is legal only where the declared relationship and its costs and effects are checked. This is a general facility, not an automatic rule that every `Type<T>` acquires `Type<void>`; `void` is no universal top or bottom type. Explicit adapters may declare extension-interface behavior without changing the original type's witness set; ordinary extension-member lookup is not conformance. No universal root is introduced merely to provide equality, hashing or formatting.

`Task<TResult = void>` has its zero-data unit-result meaning: `Task` expands to `Task<void>`. It does not thereby opt into an encoding-erasure projection or a general erasure rule. Hiding a result from `Task<T>` would require an explicit ownership, cleanup and observation contract for that result, not just a cast to `Task<void>`. Type defaults alone do not establish family relationships.

The registered [Core task contract](language/task-responsibility.md) is affine,
not Copy or covariant. Await consumes that completion owner and transfers
exact TResult. Wrapping or projecting it preserves responsibility, payload,
source/output and every-exit settlement obligations; a family name or equal
representation does not authorize duplicating or abandoning the operation.

Family projection preserves [owning-field contracts](language/ownership-and-cleanup.md):
complete versus partial state, selected authored/derived Cleanup, field order
and outstanding child/protocol obligations. A partial aggregate is not a
complete family member crossing an ordinary projection, and equal layout
cannot add Copy or erase its cleanup.

## Nominal witnesses and associated relationships

The canonical [witness chapter](language/interface-witnesses.md) defines
member/default identity, associated resolution, and checked access-qualified
self projection. This section supplies their family/representation context.

Behavioral conformance is nominal and requires a checked witness; conditional
member guarantees do not themselves establish conformance. Conflicting
type/interface witness claims are rejected rather than selected by import
order. A type declares its ordinary implementations; package import or loading
does not retroactively add a global conformance to an unrelated type. Explicit
adapter types are the intended way to add an extension-interface behavior set
without changing the original type's conformance.

ABI-transparent nominal wrappers and adapters serve different purposes. A
wrapper such as `NeverNegativeInt32` may share `int32` representation while
allowing only nonnegative values; equal bits do not make every `int32` a valid
wrapper or authorize arbitrary reinterpretation. Unused encodings are
potential niches, not an adopted niche encoding. Constant-formal eligibility
is a checked property of the selected
[constant-generic domains](language/constant-generics.md): an eligible
one-field integer/Boolean wrapper derives participation from its resolved
Transparent contract plus existing constant construction, complete
initialization, frozen Copy/Blittable and canonical typed value encoding,
while preserving nominal identity and validity.
Neither Transparent alone, Blittable alone nor today's field shape supplies it.
The [constant execution domain](language/proof-and-constant-evaluation.md#constant-execution-domain)
admits eligible inline ordinary values, with Blittable/frozen Copy value-codec
admission for emitted results, without making all such values
constant-formal arguments. Required construction executes actual admitted
bodies and checks, not a field-layout guess.
Independently compiled users rely on those published guarantees under the
[existing replacement contract](language-contracts.md#45-versioning-and-independent-implementations);
removing eligibility or incompatibly changing canonical value identity breaks
that contract. No separate marker or carrier protocol is added; physical
generic/value/validity records remain specification work.
An explicit `Int32Adapter`
may carry `int32` and implement `IExtra` without making `int32` conform to
`IExtra`. The behavior sets may use two dispatch tables/vtables or an
equivalent; the value need not physically store two vtable pointers.

An explicitly declared one-way `Original`-to-`Adapter` projection, potentially
including `List<Original>` to `List<Adapter>`, is a design possibility, not
automatic covariance or a general container reinterpretation rule. A checked
projection must preserve validity, representation, access, ownership, lifetime
and container representation; writable storage remains invariant. A narrowing
invariant is not a total projection just because its payload bits match.
Forwarding uses explicit checked members or opt-in generated declarations,
not a live wildcard. Compact metadata may share target/signature/contract
information without erasing nominal identities. Concrete adapter/projection
schemas, conflicts and dispatch encoding remain specification work.
`Blittable` is a separate fixed-representation promise. Applicable members outrank ordinary extension
members; the accepted
[explicit provider selector](grammar-spec.md#extension-provider-selection)
uses `value.via<Provider>.Member()` for an eligible nominal non-interface
provider, distinct from selecting an interface witness. Receiver evaluation
occurs once and supplies the extension receiver parameter; naming a provider
does not bypass lexical eligibility, access or loan checks. Extension
declaration/import eligibility syntax remains open. Ordinary
extension-member lookup never substitutes for a nominal witness; an explicitly
declared adapter has its own nominal extension-interface witness and leaves
the original type unchanged.

Associated types are named type members of a selected interface witness and
`Self` relationships express relations such as integer `Wider`, `Signed`, and
`Unsigned` types and a SIMD member's element type. For example,
`IInteger<TSelf>` may declare `type Wider;`, with an implementation binding
that member to a concrete type; `T.Wider` is resolved through the selected
`IInteger<T>` witness. A constraint may explicitly bind an associated type,
through `where T.via<ISource>.Item is U`. The `is` clause checks exact
complete resolved type identity after witness selection and ordinary type
inference; it does not replace ordinary `where T : I` conformance constraints
or add a new inference source. Transparent aliases normalize, while nominal
wrapper identities and access/ownership qualifications remain distinct.
This does not infer the
original `T` backward from an associated result. If multiple valid witnesses
or relations could supply a projection, the use must explicitly disambiguate
them. The witness chapter defines exact identity checking and finite binding
resolution; their physical metadata remains to be specified.
Inherited defaults require an unambiguous identity/default or explicit
resolution, never an inheritance/import-order winner.
Associated bindings do not replace independent
`[Copy]`, access, ownership, or family constraints.
`Item<'a>` remains an unselected associated lifetime-generic candidate.
[Standard lending iteration](language/lending-iteration.md) instead selects
separate readonly/writable reference-output identities and element/backing/
invalidation contracts. It does not adopt that associated syntax, infer a
relationship from a private body or select a physical iterator ABI.

[Named generated cursors](language/generated-producers.md) are affine
`mut unique struct` definitions with exact producer/formal/role identities.
Their generated contents retain ordinary complete storage, layout, tracing,
backing and transfer/sharing requirements. Equal body/layout does not merge
nominal identities, and compiler generation is neither Copy nor an erased ABI.
Generic definition mapping preserves all representation-relevant formals.

An interface may declare its implementing-type formal first as `<this T>`;
interfaces without a self dependency omit it. `type Item;` requires a
binding, while `type Item = uint8;` supplies a replaceable checked default.
Qualified implementations such as `type ISource.Item = uint8;` retain the
declaring interface's identity. `T.via<ISource>.Item` and
`value.via<ISource>.Member()` select an existing witness explicitly without
creating conformance, boxing or upgrading access. Conflicting claims for
the same type/interface pair reject. `default<I>.Member()` selects the
interface's default body only for the current implementing `this`; calls
inside that body still dispatch through the witness.
An inferred `write C` may use an existing self witness through a checked
projection, not by becoming `C` or by assuming `I<C>` equals `I<write C>`.
Every member/associated contract must adapt safely; an authored readonly
`C` result cannot satisfy a promised `write C` result. Ordinary interface
arguments and inner qualifications are not rewritten.

Namespace-level `alias` declarations are transparent type synonyms.
A source alias participates in the [type-name declaration space and arity rules](language/names-and-binding.md#type-name-arity-selection)
before expansion; equal targets do not authorize duplicate names.
A public alias exports its name and mapping as a source contract; executable
signatures use the resolved target identity. It does not create a nominal
type, new witness or changed access/layout contract. Expansion must be finite,
reject direct/indirect synonym cycles with their
declaration chain, and preserve complete qualified substitutions and
effective target accessibility. Alias metadata encoding remains open. See the
[alias and witness source rules](grammar-spec.md#54-aliases-and-associated-types-declarations-and-open-binding).

## Whole-span initialization

`Span<init T>` is a non-owning element view whose entire represented range is
vacant on entry. `init` qualifies the element type, not the Span descriptor.
The initial contract tracks only the whole-range vacant/complete transition;
it does not represent mixed or prefix-initialized elements. Element reads are
unavailable until the complete range is initialized.

`write Span<init T>` permits initialization writes and, after successful
completion, repeated whole-element replacement through `write Span<T>`.
`Span<init write T>` permits one-time whole-range initialization followed by
inner mutation through `Span<write T>`, where the element type permits it.
The descriptor's own rebinding authority remains separate (`mut Span<...>`).
The view retains Span's ordinary backing, access and lifetime dependencies.

A complete direct whole-range operation or a selected helper contract may
establish the complete state. A helper is identified by its declared
signature and `OutputState` relation (or an unconditional complete-on-normal
return promise), never by a method name. A false normal outcome leaves the
range vacant; a true outcome publishes every valid `T`. If failure can occur
after partial construction, the helper must settle and clean all partial
elements so the range is vacant before propagation, or its selected profile
must exclude that failure. This is settlement, not rollback. A `Clear`-style
helper counts only if it writes valid `T` values; it does not imply
`default(T)` or zero initialization. Callers retain the ordinary
indeterminate-state rule after an exceptional exit.

## Text views

`TextSpan<TCodeUnit = void>` is **one** family; bare `TextSpan` expands to exactly `TextSpan<void>`. The allowed members are `TextSpan<char8>`, `TextSpan<char16>`, `TextSpan<char32>` and the declared encoding-erased `TextSpan<void>`. `[TextCodeUnit]` describes only the three character types; it does **not** admit `void`. There is no unconditional `where TCodeUnit : [TextCodeUnit]` on the entire family. Here `void` is neither a zero-sized code unit, a default encoding, a top/bottom type nor a raw `Span<void>`.

Every member denotes validated immutable text. An erased view carries the *actual* UTF-8/16/32 encoding together with its validated text, immutable backing, access, root/lease and lifetime dependencies. Erasing a typed view forwards those dependencies without allocating, copying or transcoding. A checked typed projection succeeds only if the recorded encoding matches the requested character width; failure exposes no typed view. The caller cannot get mutable backing from a text view. A typed `AsSpan()` drops text validity as a *view* but retains the immutable backing and dependencies. Only typed members expose `AsSpan()` and `IContiguousSequence<TCodeUnit>`; an erased member exposes neither, and there is no `Span<void>`. `IsEmpty`, ordinal equality/comparison and formatting are common to all members. Ordinal operations compare Unicode scalar sequences across encodings without implicit normalization or locale; formatting retains the existing typed-writer protocol. `EqualsOrdinal(rune)` requires the *whole view* to contain exactly that one scalar, not a matching prefix, normalization or case folding. Thus `operation.EqualsOrdinal('+')` accepts only the ASCII plus operator regardless of backing encoding.

The projection direction is explicit: identity retains the fully qualified
member; declared typed erasure hides static encoding without losing its
runtime evidence. There is no unconditional erased-to-typed projection.
`TryProject<char16>` is instead a checked refinement of retained encoding
information, not an attempt to recover a type from the no-data `void` value
or infer it from a return context. This does not generalize to unrelated
`Family<T>` types, weaken writable invariance or select family declaration
syntax.

**Local pseudo-code -- conditional and extension syntax provisional:**

```terrum
family TextSpan<TCodeUnit = void> {
    members TextSpan<void>, TextSpan<char8>, TextSpan<char16>, TextSpan<char32>;
    common bool IsEmpty;
    common bool EqualsOrdinal(TextSpan other);
    common bool EqualsOrdinal(rune other); // whole view is exactly one scalar
    common int32 CompareOrdinal(TextSpan other);
    common bool TryFormat<TWriter>(write ref<TWriter> writer, TextSpan format)
        where TWriter : ITextWriter;

    when TCodeUnit : [TextCodeUnit] {
        implements IContiguousSequence<TCodeUnit>;
        Span<TCodeUnit> AsSpan();
        TextSpan<void> AsTextSpan();  // no copy or transcode
    }
    on TextSpan<void> {
        bool TryProject<TRequested>(ref<init TextSpan<TRequested>> result)
            where TRequested : [TextCodeUnit]; // encoding checked
    }
}
```

The existing `IContiguousSequence<T>.AsSpan()` is a read projection, not a writable escape hatch. Retaining an empty/end view does not drop its backing or lifetime obligations. Validation and immutable publication precede either typed or erased exposure; erasure cannot repair an invalid raw code-unit slice.

[Literal parser input](language/literal-construction.md#parser-input) is a
borrowed immutable member with recorded UTF-16 encoding, including after text
escape/raw transformations. That compiler input recipe does not select a
default runtime encoding, publish mutable source backing, or permit a
borrowed input descriptor to escape as an unsupported frozen constant.

## Character fragments

A target-typed single-scalar `'x'` literal has **no fallback type**. For `char8` it must be ASCII (one standalone UTF-8 code unit); for `char16` it must be a non-surrogate BMP scalar (one standalone UTF-16 code unit). `char32` and `rune` admit any Unicode scalar. Surrogates and out-of-range values never denote scalars. A text target's character fragment context is `rune`, including where the target writer uses UTF-8 or UTF-16: encoding happens at the writer boundary. `rune : IFormattable` forwards to `ITextWriter.TryWriteScalar(rune)` without constructing temporary `Text`; `SpanTextWriter<TCodeUnit>` checks space for the *entire* encoded scalar before writing. This adds one scalar primitive to the existing typed-writer path, not a matrix of character/text overloads. Scalar literal grammar and contextual target rules are specified by lexical grammar and conversions; [standard text operations](language/construction-handlers.md#standard-text-operations) select fragment forwarding and writer contracts. Their implementation and additional profiles remain separate work.

## Callable values and tuples

The bounded target families are `func<Args..., R>` (a lifetime-aware,
single-cast callable) and `fnptr<Args..., R>` (a raw target-defined code
pointer), with result last and each concrete signature closed at any supported
arity. A `func` retains its target and declared capture dependencies; it does
not automatically own state it only references or extend that state's
contractual validity. Required admitted managed roots are retained, not
discarded. A `fnptr` carries no independent environment, and its code must
outlive use. There is no
multicast callable, custom nominal-delegate declaration, arbitrary type-pack
expansion, full open/closed-by-thin/fat cross-product, fat `fnptr`, or
mandatory no-capture family. Bound callables may use a thin trampoline, but
their receiver dependencies remain explicit. Aliases, including generic
signature aliases, are transparent; nominal wrappers are separate.

Callable labels and tuple element labels are source-view metadata, not ABI
names or nominal identity. A call binds names from the statically visible
callable contract, and named arguments evaluate in source order. Tuples are
inline ordered heterogeneous values with fixed closed shape. Their
copy/uniqueness/borrow/access contracts follow the complete element types;
labels do not change representation, and function arguments are not
implicitly bundled into a tuple or assigned a tuple ABI. There is no
user-defined pack declaration, expansion, indexing, fold, or forwarding.

`fnptr` uses the Terrum calling convention by default. A foreign ABI is
selected with the checked type-use attribute
`[NativeCallConv(NativeCallingConvention.Cdecl)]` immediately before `fnptr`;
the closed values are `Unmanaged`, `Cdecl`, `Stdcall` and `Vectorcall`.
The selected convention is part of complete function-pointer identity and is
preserved through nested pointers, aliases, parameters and results. No
implicit convention casts are allowed. A `func` invocation has the Terrum
callable contract even when an explicit adapter targets another ABI.
Static functions, open instance receivers, and bound instances are distinct:
an open receiver is an explicit argument with access matching the method,
whereas a bound receiver is retained with its lifetime dependencies. The
spelling of a receiver marker remains open. Native callback pointer/context
pairs can be ordinary explicit aggregates with declared lifetimes; C++
member-function pointers are not generally raw code addresses.

## Fixed and scalable vectors

Fixed-width SIMD is a family of explicitly registered *members*, not an obligation for every vector to implement a nominal `ISimdVector` interface or call through a public witness service class. A member provides a concrete width and lane type through checked membership. There is no separate user-visible SIMD mask family or mask type parameter. Generic fixed-vector algorithms quantify over **any** registered member. Addition takes two operands of the same fully qualified instantiation and produces that instantiation. Comparisons also return the same vector instantiation, with each lane's bits guaranteed to be either all zero or all one. The compiler and target may retain a predicate-register representation internally when observable behavior is preserved; users do not select an ISA-specific mask representation. Writable stores require a writable view (`write Span<TElement>`), not a mutable descriptor (`mut Span<TElement>`). Membership checking verifies width, element granularity, operators, ABI and effects. Other unrelated types continue to use granular generic-math protocols such as `IAdditionOperators<TSelf, TOther, TResult>` without becoming SIMD members.

**Local pseudo-code -- family registration and constraint spelling provisional:**

```terrum
fixed_simd_family<TVector, TElement> {
    each T : [SimdElement];
    member simd128<T> with width 128;
    // Other valid members register their own concrete width.
    static on TVector {
        TVector Splat(TElement value);
        TVector Load(Span<TElement> source);
        TVector CompareEqual(TVector left, TVector right);
        // Each result lane is all-zero or all-one bits.
    }
}

where TVector : [FixedSimdMember<float32>],
                IAdditionOperators<TVector, TVector, TVector>
// The family witness binds the concrete width and element type.
```

The desired user-level constraint is “any fixed vector of `float32`”: its concrete width remains statically known for each instantiation. Comparison-result lane patterns do not change the complete vector type; the displayed family spelling is illustrative, not Terrum grammar.

`simd<T>` remains a separate genuinely hardware-scalable type with target/runtime-defined extent, vector length, predicate lowering and ABI. It is not an alias for a convenient fixed width or an automatic member of the fixed-vector family. Scalable-value storage, suspension and calling restrictions must be validated for each target.

## Open questions

The rules above are accepted; the following remain unresolved design work,
not settled contracts:

- Exact declaration syntax and metadata for family opt-in, conditional
  members and adapter/projection declarations. Bounded associated-type
  binding and explicit extension-provider selection are already specified
  by the source grammar; their generalized witness/registration records
  remain to be completed.
- Additional character-fragment/writer profiles beyond the selected standard
  text operations. Scalar literal grammar, contextual targets and
  representability follow lexical grammar and conversions.
- Exact spelling of a bound-receiver marker, and of foreign-calling-convention
  attribute names/grammar.
- Family/associated-member grammar for fixed-vector registration, including
  how an "any fixed vector of `float32`" constraint expresses the registered
  width/element relationship.
