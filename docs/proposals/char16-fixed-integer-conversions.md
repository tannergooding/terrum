# Explicit char16-to-fixed-integer conversion

**Status: unapproved; explicitly provisional implementation direction.**
The authorized missing-contract workflow permits implementing this candidate
provisionally. It does not amend the accepted specification, approve a library
API, establish compiler support or stabilize an interchange schema.

## Recommendation and scope

Admit explicit checked conversion from an already typed `char16` value to
each of the ten fixed-width integer primitives:
`int8`, `uint8`, `int16`, `uint16`, `int32`, `uint32`, `int64`, `uint64`,
`int128` and `uint128`. Interpret the input as its unsigned raw code-unit
value, preserve that mathematical value when representable, and otherwise
fail with the existing checked-overflow contract.

This adds one source direction, not integer membership for characters.
`char16` retains its distinct complete type and its existing equality and
ordering operations. There is no implicit conversion, promotion, character
arithmetic, Unicode decoding or new source syntax. The three fallible
destinations are `int8`, `uint8` and `int16`; the other seven are total over
all 65,536 code units, including surrogates.

The immediate implementation dependency is the narrow-scalar conversion
batch. Full support for the recommendation also needs the existing 128-bit
integer binding, constant-value and lowering substrate; recognizing its
atomic type identities alone is insufficient. A bounded implementation must
diagnose unsupported pairs rather than accept and mislower them.

## The gap and the approved alternative

The [specification index](../specs/README.md) identifies
[conversions](../specs/conversions.md#conversion-policies) as the owner of
numeric conversion pairs. That chapter explicitly distinguishes code units
from integer primitives and adopts `char16` to `float32`/`float64`, but does
not explicitly admit the reverse source direction into an integer target.
The general integer-to-integer row cannot supply it.

The maintained sources require exactly that missing direction:

- [`CharacterFacts.terrum`](../../sources/Terrum.CodeAnalysis.Terrum/Lexing/CharacterFacts.terrum)
  uses `int32(value)` for decimal and hexadecimal digit extraction.
- [`Char16FloatConversion.terrum`](../../sources/Terrum.CodeAnalysis.Terrum/Utilities/Numerics/Char16FloatConversion.terrum)
  uses `uint32(value)` before the integer-to-floating bit algorithms.
- The internal
  [checked-conversion helpers](../../sources/Terrum.CodeAnalysis.Terrum/Utilities/Numerics/IntegerCheckedConversions.terrum)
  additionally use the corresponding `int64` and `uint64` conversions.

There **is an already-approved, numerically lossless rewrite**. With a typed
`char16 value`, these expressions use only adopted pairs:

```terrum
int32 signedValue = int32(float32(value));
uint32 unsignedValue = uint32(float32(value));
```

Every input is an integer in `[0, 65535]`, exactly representable in binary32
and binary64. The subsequent adopted floating-to-integer conversion truncates
an already integral, in-range value. Substituting binary64 is also exact.
For a narrow integer target, a further checked integer conversion supplies
the appropriate range check; it does not require adopting direct
floating-to-`int8` or floating-to-`int16` conversion.

Thus an amendment is **not necessary merely to reproduce the functions'
numeric results**. Rewriting is a valid no-amendment fallback, not an
inexactness or Unicode-validity bug. It does, however, introduce floating
operations, their dependencies and additional constant-execution actions
into integer digit extraction and into an integer-only implementation of
the adopted code-unit-to-floating operation. In the latter helper it
delegates extraction back to a floating conversion instead of providing the
underlying integer operation. This is an implementation dependency concern,
not a claim that today's independently hosted emitter would recurse.

Retaining the direct integer-extraction source requires an amendment.
Recommend that amendment for the authorized adoption batch rather than the
floating detour. This preference does not invent an already accepted
integer-only source requirement. Historical Binder acceptance and existing
tests are implementation evidence, not approval of a source pair.

## Alternatives

| Candidate | Consequence |
|---|---|
| Rewrite through adopted binary32 or binary64 conversion | Preserves numeric results without a specification change; adds the floating dependency described above. |
| Admit only `int32(char16)` and `uint32(char16)` | Smallest source amendment for the two named files, but omits other maintained widening helpers. |
| Admit `char16` to `int32`, `uint32`, `int64` and `uint64` | Smallest compatibility-restoring amendment for the historical Binder's four integer destinations and existing helper family. All four conversions are total. Narrow extraction remains an explicit composition such as `int8(int32(value))`. |
| Admit `char16` to all ten fixed integers — recommended | Uses one unsigned-value/range rule, makes narrow boundaries explicit, and avoids treating historical compiler widths as language policy. Adds three fallible pairs and the total `uint16`/128-bit pairs; it requires their actual implementation and qualification. |
| Reclassify code units as integers or admit all character/scalar conversions | Changes operators, inference and unrelated character contracts. Neither the motivating sources nor this direction requires it. |

The recommended direct conversion and the composition
`T(int32(value))`, where `T` is a fixed integer, have the same successful
numeric result and conversion-overflow domain. This does not equate their
constant-execution costs or grant an optimizer permission to erase operand
effects, required dependencies or failure origin.

## Exact proposed source contract

Add the following pair policy to the conversion owner:

> For a typed `char16` expression `e` and a destination `T` equal to one of
> `int8`, `uint8`, `int16`, `uint16`, `int32`, `uint32`, `int64`, `uint64`,
> `int128` or `uint128`, `T(e)` is an explicit checked conversion. Evaluate
> `e` once and interpret the initialized raw code unit as the mathematical
> integer `u` in `[0, 65535]`. If `u` is within T's numeric range, the result
> has exact complete type T and value `u`; otherwise the conversion fails
> with exact `Terrum.OverflowException` under an admitted checked-exception
> policy. An admitted terminal policy retains its ordinary fail-fast
> behavior. No successful numeric result is published on failure.

For a signed N-bit destination the complete range is
`[-2^(N - 1), 2^(N - 1) - 1]`; for an unsigned destination it is
`[0, 2^N - 1]`. The intersection with the source domain is exact:

| Destination | Successful input and output values | Failing input values |
|---|---|---|
| `int8` | `[0, 127]` | `[128, 65535]` |
| `uint8` | `[0, 255]` | `[256, 65535]` |
| `int16` | `[0, 32767]` | `[32768, 65535]` |
| `uint16` | `[0, 65535]` | None |
| `int32`, `uint32` | `[0, 65535]` | None |
| `int64`, `uint64` | `[0, 65535]` | None |
| `int128`, `uint128` | `[0, 65535]` | None |

There are no negative successful results, signed reinterpretations, modulo
reductions, clamps or replacement characters. `0xD800..0xDFFF` are ordinary
raw inputs; they need not form a surrogate pair and undergo no scalar
validation. Conversion of zero produces integer zero.

The form uses ordinary type/name binding and exactly one positional value
argument. It is not a prefix cast, method-name convention, literal
registration or implicit overload adaptation. A typed argument retains
`char16` even when constant, grouped, imported or optimizer-folded.
Destination context cannot retarget it. An implicit assignment such as
`int32 result = value;` remains invalid.

The operation is checked in Development and Release alike. The
[source-arithmetic-profile candidate](source-arithmetic-profile-selection.md)
does not change it. Existing explicit policy syntax cannot manufacture a
wrapping or saturating variant for these new pairs.

On failure, preserve completed operand effects and ordinary cleanup;
there is no rollback or operand replay. An existing assignment destination
is unchanged and a vacant initialization destination remains vacant.
The [scalar failure owner](../specs/language/scalar-failures.md) supplies
event identity, origin at the failed conversion, creation/transport,
handler and fail-fast rules. Conversion itself requires no unsafe
authorization; recovery retains the separate local-failure authorization
rules. A smaller physical carrier must not truncate the input before its
range check.

### Direct literals remain a different operation

Preserve [literal construction](../specs/language/literal-construction.md)
and the existing [grammar](../specs/grammar-spec.md). The examples below
combine adopted literal rules with the **proposed** typed source pairs:

| Source expression | Required result |
|---|---|
| `int8(0xFF)` | Direct signless based-bit construction: `-1` |
| `int8(char16(0xFF))` | Typed unsigned value 255: checked overflow |
| `int16(0x8000)` | Direct signless based-bit construction: `-32768` |
| `int16(char16(0x8000))` | Typed unsigned value 32768: checked overflow |
| `uint16(char16(0xFFFF))` | Exact value 65535 |
| `int32(char16(0xD800))` | Exact value 55296; no surrogate rejection |
| `int32(char16('\uD800'))` | Invalid character construction before conversion |
| `int32(char16(65536))` | Invalid raw-unit literal construction before conversion |

`int8(128)` is an invalid literal construction, including in an unexecuted
branch. By contrast, the admitted typed conversion
`int8(char16(128))` has a value-dependent failure: ordinary code follows
the existing runtime/proven-failure diagnostic rules, while a required
constant request must diagnose failure. Folding must not reparse its input
as `int8(0x80)` or turn overflow into `-128`.
Character notation has its own registered-kind coverage; this proposal
does not admit direct `int32('A')`.

## Proof and constant execution

Use the same unsigned-value and destination-range algorithm in
[constant execution](../specs/language/proof-and-constant-evaluation.md),
ordinary execution and optional folding. A successful required request
publishes the exact typed frozen integer under its existing target
value-codec rules. An unhandled reached overflow prevents required
publication and diagnoses; it is not a runtime fallback, parser refusal,
default value or new constant-only conversion policy. A separately admitted
constant exception-handling feature retains its own rules; this candidate
does not supply one. Unselected execution paths keep the existing
constant-execution rules.

Retain ordinary `[ConstEval]` author opt-in, checked pure-form obligations,
resource accounting, body/intrinsic availability and executed dependencies.
Charge the existing explicit-conversion semantic action and actual
type-width work; do not treat an implementation helper as a free cached
result or charge the floating detour as one direct conversion.
Admitting a pair does not make an arbitrary enclosing function total or
constant-evaluable.

Extend the minimum conversion transfer without reclassifying code units:

- An exact input yields the same exact integer on success; an exact
  out-of-range input has no normal result.
- For an unknown valid `char16`, the successful integer fact is
  `[0, min(65535, T.MaxValue)]`. This normal-edge fact does not prove that
  the preceding narrow conversion succeeds.
- `uint16` and the six wider destinations are total by source/destination
  domains. The three narrower destinations are not all-input total.
  Any stronger nonfailure claim needs a sound supported proof of its actual
  input domain under the existing pure/effect rules.

The code-unit proof domain can remain exact-or-unknown. This does not add
general code-unit interval arithmetic, character operator permissions or a
new public NoThrow promise; preserve the separately
[proposed NoThrow retirement](no-throw-retirement-and-proof-boundaries.md).

## Specification integration and supersession

These are proposed edits on adoption, **not changes made by this document**:

| Owner | Exact integration |
|---|---|
| [Conversions: context and policies](../specs/conversions.md) | Retain “code units are not integer primitives”; add the explicit `char16`-to-fixed-integer policy and destination table above beside the existing char16-to-floating row. Cross-reference it from Character intent. This supersedes reliance on the integer-to-integer row to justify these source pairs, not the code-unit distinction. |
| [Scalar checked failures, operation mapping](../specs/language/scalar-failures.md#2-scalar-operation-mapping) | Add an explicit checked char16-to-fixed-integer out-of-range row mapped to exact `Terrum.OverflowException`; retain the existing reverse numeric-to-code-unit row and all event/state rules. |
| [Proof and constant execution](../specs/language/proof-and-constant-evaluation.md#reads-assignment-and-primitive-transfer) | Add the exact/unknown conversion transfer and pair-totality facts above; retain existing constant actions, admission and failure rules. |
| [Literal owner](../specs/language/literal-construction.md#target-selection) and [grammar](../specs/grammar-spec.md) | No production, registered kind or primitive parser change. Add only a cross-reference/example clarifying that typed code-unit conversion is not signless based-bit construction. |
| [IL conversions](../specs/il-instruction-set.md#conversions) | Apply the existing ordinary-conversion semantics to these exact pairs and the compact/long eligibility below; no new operation family. |
| [Literal cases](../specs/conformance/source-literal-construction.md), [scalar-failure cases](../specs/conformance/source-scalar-failures.md), [proof/constant cases](../specs/conformance/source-proof-and-constant-evaluation.md) and [IL cases](../specs/conformance/il-registry.md) | Add the corresponding cases from the qualification matrix below without presenting unapproved cases as current requirements before adoption. |

This focused candidate owns the proposed primitive source pairs. It does
not supersede or adopt the broader
[conversion-witness sketches](core-library-reference.md#6-numerics-and-conversions),
internal Try/wrapping/saturating helper APIs, or the separate
[wide scalar value-carrier candidate](scalar-wide-value-carriers.md).
No other proposal or implementation inventory supplies approval of this
direction.

## Compiler, persistence and lowering handoff

Static inspection of the in-progress sources establishes the following
integration points, not fresh passing validation:

| Layer | Required work and evidence |
|---|---|
| Binding | [`Binder.BindConversionCall`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.cs) currently uses [`PrimitiveTypeFacts.IsInteger`](../../sources/Terrum.CodeAnalysis/Semantics/PrimitiveTypeFacts.cs), which excludes code units, for integer-source admission. Add a distinct admitted-pair rule shared with verification; do not add char16 to the integer predicate. The historical Binder admitted it in the scalar source list, but that is not a normative rule. |
| Bound traversal and rewriting | Reuse [`BoundCast`](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs) with its complete operand/result identities and existing nested-expression hooks. Preserve checked semantics and the conversion source span through generic rewriting, cloning and dependency remapping. No sample/function-name recognition or new node is needed. |
| Source and reconstructed-program verification | [`SemanticVerifier`](../../sources/Terrum.CodeAnalysis/Semantics/SemanticVerifier.cs) currently excludes char16 from its integer-conversion pair and `IsTotalConversion` logic. Admit the exact new pairs and classify only the seven complete-domain pairs as total. Retain input availability/representation checks, failure edges, result-state and applicable scoped nonfailure proofs. |
| Folding and ConstEval | [`Binder.ConstantFolding`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.ConstantFolding.cs) already extracts raw char16 as a nonnegative scalar, but scalar extraction is not conversion admission. [`Binder.ConstEval`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.ConstEval.cs) admits char16 and the current narrow/32-/64-bit integer domain, not 128-bit evaluation or floating intermediates. Extend actual support as required; do not accidentally admit char8/char32/rune merely because the scalar extractor also handles them. |
| Private semantic modules | [`ModuleCompiler`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.cs) already persists a typed `BoundCast` as expression tag 6 plus its nested operand. Reuse that shape, exact type encoding and shared verification after reconstruction. Test independent producer/reference/consumer linking and rewrites. No new tag or private revision is proposed solely for these pairs; wider type/value support retains its separately selected schema. |
| Terrum frontend projections | [`ScalarFrontendHost`](../../sources/Terrum.CodeAnalysis/Lexing/ScalarFrontendHost.cs) and the [conversion projection tests](../../tests/Terrum.CodeAnalysis.Tests/Semantics/Frontend/ConversionFrontendProjectionTests.cs) have their own bounded type/source-anchor contract. Carry the selected pair and exact types through an explicitly supported projection; reject unsupported widths rather than accepting a name or truncating a payload. A private projection is not standard TRRM. |
| Native emission | [`NativeEmitter.CastPrimitive` and `EmitCheckedIntegerConversion`](../../sources/Terrum.CodeAnalysis/Native/NativeEmitter.cs) already contain 16-bit char16 width handling, but the current integer-source dispatch excludes it. Wire the admitted pairs through the actual conversion path; dormant width code is not support. |

For native lowering, load/canonicalize the **unsigned 16-bit** input before
any destination operation. For the three fallible pairs, compare the full
value with 127, 255 or 32767 before narrowing or storing. For `uint16` and
the wider destinations, preserve the value by zero extension. Signed
destinations do not sign-extend bit 15 of the source. A 128-bit result has
zero in every bit above bit 15, including its high 64-bit half; its complete
constant/runtime carrier must be initialized. Equal size does not authorize
reinterpreting `char16(0xFFFF)` as signed `-1`. No floating environment,
decoder, marshalling or allocation helper is required for success.

### Standard interchange and admission

Reuse the assigned [register conversion forms](../specs/il-instruction-set.md#conversions)
and [registry](../specs/representation/registry.md): ordinary long `D0`
names the destination TypeMap entry and gets the source type from its
register. Short `50` is available only for an admitted total conversion
meeting its existing in-place/register/TypeMap-index constraints. The three
fallible pairs use `D0`, not short `50` merely because one observed input
fits. This candidate adds no wrapping `51`/`D1`, saturating `D2` or bitcast
`52`/`D3` pair.

No new opcode, metadata row, Feature ID, TypeSpec, field, target ABI or
numeric allocation is needed. Integration changes the governing conversion
semantics, so draft exchange still requires exact agreement on those
semantics under [format versioning](../specs/representation/format-versioning.md)
and [requirements/admission](../specs/representation/requirements-and-admission.md).
Equal `(1, 0)` bytes or unchanged instruction shapes do not authorize an
old consumer to guess new pair support. This candidate is for the current
draft baseline, not a released-schema compatibility mapping.

Producers establish admitted source pairs, truthful totality/failure facts
and complete result representations. Consumers retain the
[required structural, reference, support and binding checks](../specs/representation/reader-trust.md);
they are not required to re-prove arbitrary producer-published Body facts.
An optional semantic audit can check pair/totality truth. An unsupported
required type, pair, value codec or exception substrate declines before
affected use. Existing register-body structural recognition and private or
retired executable codecs do not establish standard executable support.

Retain the exact governing rules, complete types, failure policy/origin,
selected Core exception/factory and actual target/helper dependencies
through linking and replacement. Folding to a frozen value retains its
required value/ABI and evaluation dependencies, without forcing discarded
compiler-only work into runtime imports. Re-encoding rebuilds decoded
TypeMap references, register/entry types, constants, instruction offsets
and affected source maps; it is not opcode-byte substitution.

## Qualification and acceptance

The [NUnit project](../../tests/Terrum.CodeAnalysis.Tests/Terrum.CodeAnalysis.Tests.csproj)
does not exclude the test files below. Their inspected assertions are
useful starting points, **not results from this documentation task**:

- [CharacterFactsModuleTests](../../tests/Terrum.CodeAnalysis.Tests/Lexing/CharacterFactsModuleTests.cs)
  composes the source with an independent module consumer and enumerates
  UTF-16 units under both runtime policies.
- [Char16FloatConversionModuleTests](../../tests/Terrum.CodeAnalysis.Tests/Utilities/Numerics/Char16FloatConversionModuleTests.cs)
  invokes the [Terrum exhaustive bit oracle](../../tests/Terrum.CodeAnalysis.Terrum.Tests/Utilities/Numerics/Char16FloatConversionTests.terrum).
- [IntegerCheckedConversionsModuleTests](../../tests/Terrum.CodeAnalysis.Tests/Utilities/Numerics/IntegerCheckedConversionsModuleTests.cs),
  [JitTests.IntegerCheckedConversions](../../tests/Terrum.CodeAnalysis.Tests/Native/Jit/JitTests.IntegerCheckedConversions.cs)
  and [CLI frontend conversions](../../tests/Terrum.CodeAnalysis.Tests/Cli/CliTests.FrontendConversions.cs)
  contain widening/module/AOT/JIT expectations that rely on the missing
  direct source direction. Their existence does not show that the current
  in-progress Binder passes them.

Use public compiler APIs and existing supervised, headless runners.
The candidate's acceptance cases are:

| Area | Positive, negative and boundary obligations |
|---|---|
| Complete pair matrix | Bind parameter-based functions for all ten destinations; check exact result type, not just a returned host value. Enumerate every source code unit against an independent unsigned-integer/BigInteger oracle. Expect the three failure ranges and seven total pairs above. |
| Critical edges | Include `0`, `1`, `127`, `128`, `255`, `256`, `32767`, `32768`, `0xD7FF`, `0xD800`, `0xDBFF`, `0xDC00`, `0xDFFF`, `0xE000`, `65534`, `65535`. Test all destinations, not only signed 32-bit or valid Unicode scalars. Check the complete zero high half for both 128-bit results. |
| Literal distinction | Exercise every example above, decimal and signed based literal refusal, grouped typed constants and imported frozen char16 values. Untyped `var value = 65` still lacks context. An unexecuted bad literal still diagnoses; an unexecuted, otherwise valid typed overflow is not newly required to execute. |
| No unintended admission | Implicit char16-to-integer assignment, mixed char16/integer arithmetic/comparison and Boolean conversion remain invalid. Do not newly admit integer extraction from char8/char32/rune, native-integer destinations, direct floating-to-narrow-integer or floating-to-char16 conversion. Malformed arity/named arguments retain existing primitive type-call diagnostics. |
| Constant/proof/effect parity | Successful required conversions freeze exact destination bits; reached required overflow diagnoses without publication/runtime fallback. Test the approved float bridge separately from the proposed direct primitive. Unknown narrow input cannot justify an all-input nonfailure/pure guarantee; total destinations can establish conversion-only nonfailure without proving the enclosing call total. |
| Runtime failure state/order | Under supported checked exceptions, test exact OverflowException, unchanged initialized destinations, still-vacant initialization destinations and exactly-once operand evaluation/cleanup. Under fail-fast, supervise expected termination. Preserve the conversion's failure origin. Do not claim nominal exception transport merely from the current runtime's scalar status codes. |
| Persistence and rewriting | Independently save/read/link producer and consumer functions with parameter inputs and frozen char16 constants; exercise traversal, remapping and native regeneration. Mutate structural type/map/index/payload fields to test safe-consumption rejection; separately audit forged admitted-pair/totality facts without imposing arbitrary semantic re-proof on ordinary consumers. |
| Standard register Bodies | Independently construct total `50` and equivalent `D0` cases, and fallible `D0` cases, with the actual exact schema and type/constant maps. Audit invalid short fallible forms, wrong source/destination pairs and wrong register/map references. Structural reader success alone is not execution qualification. |
| Adoption integration | Requalify both named source files and the existing widening/helper/frontend consumers, including all-code-unit floating-bit results, with no floating detour or sample-specific Binder exception. Use an independent host oracle for extracted integer values rather than using the conversion under test to compute its own expected value. |

Exercise runtime success and failure through AOT and JIT, both admitted
runtime policies, and both source arithmetic profiles; conversion outcomes
must not change with the profile. Keep emitted-image, execution, private
module and standard TRRM evidence separate. Unsupported 128-bit or
checked-exception transport paths remain explicit implementation gates,
not passing/skipped coverage of the complete recommendation.
Follow the [build and qualification rules](../building.md) and record actual
matched cases and final-input identity at the implementation checkpoint.

## Explicit exclusions and remaining decisions

No new conversion involving `char8`, `char32` or `rune` is selected. They
have different raw-width/scalar-validity contracts and no motivating source
dependency here; common storage or a shared scalar helper does not justify
them. Direct `char16` to `nint`/`nuint` is also excluded; native width and
identity remain separate. Preserve already admitted reverse
integer-to-char16, char16 identity and char16-to-binary32/binary64 behavior
without expanding their catalogs.

There is no new float16/bfloat16/float128/decimal pair, character parser,
Unicode decoder, FromBits/ToBits API, Try conversion, saturation/wrapping
variant, conversion witness, source arithmetic policy, public NoThrow
annotation, native ABI or private/standard wire identity.

The only design approval requested is the ten-pair source amendment rather
than the four-pair compatibility alternative or no-amendment rewrite. The
candidate fixes every pair's range, failure, constant, proof and
representation consequences; no further source or allocation decision is
needed to implement it provisionally. Approval, normative integration,
per-layer compiler implementation and qualified support remain distinct.
