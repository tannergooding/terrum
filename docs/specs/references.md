# Design sources and provenance

Sources below identify their exact editions or source revisions. Repository
sources were read from local, read-only checkouts; pinned links prevent
subsequent upstream changes from changing that evidence. These are design
inputs, not Terrum implementations or endorsements of identical language/ABI
behavior. [Libraries](libraries.md) draws the library conclusions;
[lifetimes](lifetimes.md) records receiver/storage and contract limits.

## Language and framework-design prior art

| Reference | Scope used |
|---|---|
| ECMA-335, sixth edition, June 2012 | Partitioned source/runtime/metadata/library responsibilities and explicit profiles; Terrum does not import the CLI object model, instruction assignments or library identities |
| [C# standard draft-v14, revision 1bab4ce218cf9b4b800aeb63e4bc1a1e1cfe6b8b](https://github.com/dotnet/csharpstandard/tree/1bab4ce218cf9b4b800aeb63e4bc1a1e1cfe6b8b/standard) | Conventional grammar, checking/binding/evaluation organization and statement/expression contract presentation; not implementation-dependent Terrum semantics |
| Framework Design Guidelines, third edition, full 626-page PDF edition | Scenario-driven design (2.2.1), low barrier to entry and layering (2.2.2/2.2.4), naming (chapter 3), parameters/Try/collections (5.8/7.5.2/8.3), async/scoped responsibility (9.2/9.4.3) and Try-write/partial progress (9.12.3-4) |

The full third-edition framework text is the design reference. Its printed
pages 16, 42, 282, 362, 387, 452 and 458 correspond to PDF pages
61, 87, 327, 407, 432, 497 and 503 in that edition.
[Library scenarios](libraries/design-scenarios.md) state deliberate departures
from CLR conventions, including vacant Boolean init outputs, affine completion
and cleanup, exact target layout, no mandatory text allocation/default encoding
and explicitly optional substrate. This reference does not register candidate
APIs or override the selected contracts.

## .NET Runtime

.NET Runtime revision [`bd6d336bfd95a333e8b3f22f26259bbb659974cf`](https://github.com/dotnet/runtime/tree/bd6d336bfd95a333e8b3f22f26259bbb659974cf). Inspected source:

| Source | Evidence used |
|---|---|
| [`IAdditionOperators<TSelf,TOther,TResult>`](https://github.com/dotnet/runtime/blob/bd6d336bfd95a333e8b3f22f26259bbb659974cf/src/libraries/System.Private.CoreLib/src/System/Numerics/IAdditionOperators.cs) and [`INumberBase<TSelf>`](https://github.com/dotnet/runtime/blob/bd6d336bfd95a333e8b3f22f26259bbb659974cf/src/libraries/System.Private.CoreLib/src/System/Numerics/INumberBase.cs) | Granular operator operand/result types versus the much broader scalar protocol; checked/saturating/truncating numeric creation. |
| [`INumber<TSelf>`](https://github.com/dotnet/runtime/blob/bd6d336bfd95a333e8b3f22f26259bbb659974cf/src/libraries/System.Private.CoreLib/src/System/Numerics/INumber.cs), [`IFloatingPointIeee754<TSelf>`](https://github.com/dotnet/runtime/blob/bd6d336bfd95a333e8b3f22f26259bbb659974cf/src/libraries/System.Private.CoreLib/src/System/Numerics/IFloatingPointIeee754.cs), [`IDecimalFloatingPointIeee754<TSelf>`](https://github.com/dotnet/runtime/blob/bd6d336bfd95a333e8b3f22f26259bbb659974cf/src/libraries/System.Private.CoreLib/src/System/Numerics/IDecimalFloatingPointIeee754.cs) | Distinct floating categories, IEEE max/min variants, FMA and decimal quantum/cohort-related operations. |
| [`ISimdVector<TSelf,T>`](https://github.com/dotnet/runtime/blob/bd6d336bfd95a333e8b3f22f26259bbb659974cf/src/libraries/System.Private.CoreLib/src/System/Runtime/Intrinsics/ISimdVector_2.cs) | **Internal**, not an exported public generic-vector interface; composes granular operators rather than inheriting `INumber`. Vector `Equals`/`LessThan` return lane-mask vectors, `Dot`/`Sum` return scalar elements, and `LoadUnsafe`/`StoreUnsafe` have explicit element offsets. Terrum proposes a distinct boolean mask type rather than copying that mask encoding. |
| [`Vector128<T>`](https://github.com/dotnet/runtime/blob/bd6d336bfd95a333e8b3f22f26259bbb659974cf/src/libraries/System.Private.CoreLib/src/System/Runtime/Intrinsics/Vector128_1.cs), [`Vector256<T>`](https://github.com/dotnet/runtime/blob/bd6d336bfd95a333e8b3f22f26259bbb659974cf/src/libraries/System.Private.CoreLib/src/System/Runtime/Intrinsics/Vector256_1.cs), [`Vector512<T>`](https://github.com/dotnet/runtime/blob/bd6d336bfd95a333e8b3f22f26259bbb659974cf/src/libraries/System.Private.CoreLib/src/System/Runtime/Intrinsics/Vector512_1.cs) | Fixed ABI-width implementations compose lower halves for software paths. `Vector128<T>.operator ==` reduces lane equality to **one `bool`**; public `Count`/support checks depend on the lane type. Intrinsic recognition and portable fallback are implementation concerns, not a scalable ABI. |
| [`TensorPrimitives.Add`](https://github.com/dotnet/runtime/blob/bd6d336bfd95a333e8b3f22f26259bbb659974cf/src/libraries/System.Numerics.Tensors/src/System/Numerics/Tensors/netcore/TensorPrimitives.Add.cs), [`IBinaryOperator` dispatch](https://github.com/dotnet/runtime/blob/bd6d336bfd95a333e8b3f22f26259bbb659974cf/src/libraries/System.Numerics.Tensors/src/System/Numerics/Tensors/netcore/Common/TensorPrimitives.IBinaryOperator.cs) | Caller-owned output spans, equal-length/capacity validation and no displaced overlap, scalar/fixed-vector dispatch, short-length and tail strategies; no universal allocation from elementwise `Add`. |
| [`TensorPrimitives.Sum`](https://github.com/dotnet/runtime/blob/bd6d336bfd95a333e8b3f22f26259bbb659974cf/src/libraries/System.Numerics.Tensors/src/System/Numerics/Tensors/netcore/TensorPrimitives.Sum.cs), [`Dot`](https://github.com/dotnet/runtime/blob/bd6d336bfd95a333e8b3f22f26259bbb659974cf/src/libraries/System.Numerics.Tensors/src/System/Numerics/Tensors/netcore/TensorPrimitives.Dot.cs), [`SoftMax`](https://github.com/dotnet/runtime/blob/bd6d336bfd95a333e8b3f22f26259bbb659974cf/src/libraries/System.Numerics.Tensors/src/System/Numerics/Tensors/netcore/TensorPrimitives.SoftMax.cs) | Empty-sum identity; dot fusion without intermediate products; documented platform-dependent floating results and softmax's exp/sum/divide cost and ordering. |
| [`ReadOnlyTensorSpan<T>`](https://github.com/dotnet/runtime/blob/bd6d336bfd95a333e8b3f22f26259bbb659974cf/src/libraries/System.Numerics.Tensors/src/System/Numerics/Tensors/netcore/ReadOnlyTensorSpan_1.cs), [`TensorSpan<T>`](https://github.com/dotnet/runtime/blob/bd6d336bfd95a333e8b3f22f26259bbb659974cf/src/libraries/System.Numerics.Tensors/src/System/Numerics/Tensors/netcore/TensorSpan_1.cs), [`Tensor<T>`](https://github.com/dotnet/runtime/blob/bd6d336bfd95a333e8b3f22f26259bbb659974cf/src/libraries/System.Numerics.Tensors/src/System/Numerics/Tensors/netcore/Tensor_1.cs), [`TensorShape`](https://github.com/dotnet/runtime/blob/bd6d336bfd95a333e8b3f22f26259bbb659974cf/src/libraries/System.Numerics.Tensors/src/System/Numerics/Tensors/netcore/TensorShape.cs) | Split owning managed tensor and byref tensor spans, rank/strides, validated flattened versus actual backing extent, inline small-rank metadata and allocation for larger ranks. Runtime's current stride policies differ from the proposed Terrum signed-stride view and are **not** adopted blindly. |
| [`Decimal32`](https://github.com/dotnet/runtime/blob/bd6d336bfd95a333e8b3f22f26259bbb659974cf/src/libraries/System.Private.CoreLib/src/System/Numerics/Decimal32.cs), [`Number.DecimalIeee754`](https://github.com/dotnet/runtime/blob/bd6d336bfd95a333e8b3f22f26259bbb659974cf/src/libraries/System.Private.CoreLib/src/System/Number.DecimalIeee754.cs) | BID decimal encode/decode and distinct operator equality, `Equals` and hash. Hash normalizes finite coefficient/exponent cohorts, all zeros and NaNs; Terrum still must define its own key comparer and ABI. |

Targeted authored public design discussions, read for rationale rather than treating proposals as accepted APIs:

- [`dotnet/runtime#71648`](https://github.com/dotnet/runtime/issues/71648): Tanner Gooding's generic-math operator-result discussion contrasts bool comparison with a generic comparison result, and notes complex/real trigonometric result mismatches.
- [`dotnet/runtime#87097`](https://github.com/dotnet/runtime/issues/87097): Tanner Gooding's AVX-512 mask proposal evaluates separate mask types versus recognizing existing conditional-select patterns; documents API-size and JIT-throughput tradeoffs.
- [`dotnet/runtime#100924`](https://github.com/dotnet/runtime/issues/100924): Tanner Gooding's `Tensor<T>` API proposal motivates multidimensional span and tensor shapes.
- [`dotnet/runtime#130573`](https://github.com/dotnet/runtime/issues/130573): Tanner Gooding's span terminal-operator proposal distinguishes eager terminal/destination-fill algorithms from lazy or allocating sequence producers.

## TerraFX

TerraFX revision [`9def09f6c24a0465035ce004ab240ecce0a97a04`](https://github.com/terrafx/terrafx/tree/9def09f6c24a0465035ce004ab240ecce0a97a04), verified at the canonical public repository:

- [`sources/Core/TerraFX.csproj`](https://github.com/terrafx/terrafx/blob/9def09f6c24a0465035ce004ab240ecce0a97a04/sources/Core/TerraFX.csproj) and [`sources/Graphics/Graphics/GraphicsBuffer.cs`](https://github.com/terrafx/terrafx/blob/9def09f6c24a0465035ce004ab240ecce0a97a04/sources/Graphics/Graphics/GraphicsBuffer.cs) show source/package layering and native graphics dependencies, with resource-creation options, ownership and explicit failure. TerraFX's actual layering is an input, not a mandate to clone its package graph.
- [`sources/Core/Numerics/Vector4.cs`](https://github.com/terrafx/terrafx/blob/9def09f6c24a0465035ce004ab240ecce0a97a04/sources/Core/Numerics/Vector4.cs) shows a domain vector with typed component operations layered over `Vector128<float>`. Geometric vectors, fixed hardware vectors and scalar numbers should not share a single accidental contract.
- [`sources/Core/Collections/UnmanagedValueLinkedList\`1.cs`](https://github.com/terrafx/terrafx/blob/9def09f6c24a0465035ce004ab240ecce0a97a04/sources/Core/Collections/UnmanagedValueLinkedList%601.cs) and its neighboring collection files illustrate explicit unmanaged versus observable/managed storage choices; Terrum's three layers keep those ownership differences without imposing allocator policy parameters on every ordinary interface.

## Compiler and runtime

These sources inform compiler architecture and compatibility boundaries; they
do not select Terrum APIs or implementations.

| Source | Design input |
|---|---|
| [Roslyn syntax model](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/work-with-syntax) | Full-fidelity syntax and explicit recovery are useful shared tooling surfaces; a compact CST does not require premature red/green reuse or a parallel AI AST. |
| [Roslyn incremental generator design](https://github.com/dotnet/roslyn/blob/main/docs/features/incremental-generators.md) | Immutable declared transform inputs enable caching; pipeline APIs also require explicit generation authority. |
| [rustc incremental queries](https://rustc-dev-guide.rust-lang.org/queries/incremental-compilation.html) | Cached results depend on actual query dependencies, not only file timestamps; source identity alone is not semantic validity. |
| [Clang AST](https://clang.llvm.org/docs/IntroductionToTheClangAST.html) and [AST importer](https://clang.llvm.org/docs/InternalsManual.html#the-astimporter) | Written syntax and semantic identity/redeclaration graphs serve distinct purposes and should not be collapsed into a lossy syntax tree. |
| [LLVM language reference](https://llvm.org/docs/LangRef.html) | Explicit IR contracts and linkage distinctions matter; LLVM linkage is not Terrum lexical visibility and does not select LLVM as a backend. |
| [.NET runtime type-loader design](https://github.com/dotnet/runtime/blob/bd6d336bfd95a333e8b3f22f26259bbb659974cf/docs/design/coreclr/botr/type-loader.md) | Type identity, metadata validation and load phases affect compatibility and failure timing; these require explicit boundaries rather than accidental JIT-time discovery. |

## Other official library-specific prior art

- [Arm ACLE, “Sizeless types”](https://arm-software.github.io/acle/main/acle.html#sizeless-types): SVE vectors **and predicates** have sizeless types. ACLE permits automatic locals and function parameters/results but disallows array elements, `sizeof`, and ordinary struct members. Terrum's proposed scalable profile draws a boundary from these restrictions, without claiming its own ABI or suspension semantics follow automatically from C/C++ ACLE.
- [Rust `std::simd::Mask` documentation](https://doc.rust-lang.org/std/simd/struct.Mask.html): a mask represents per-element boolean inclusion/exclusion, and its layout is expressly unspecified. This is a conceptual contrast with a numeric vector of all-one bits; neither Rust's API stability nor its ABI is a Terrum commitment.

## Windows x64 platform and decimal evidence

The [Windows target profile](targets/windows-x64.md#3-imported-platform-rules)
identifies the exact Microsoft source revisions and portions it imports.
Only that explicit import is normative; this provenance chapter adds no
platform rule or foreign primitive alias.

| Pinned source | Evidence |
|---|---|
| [LLVM 21.1.8 Windows classification](https://github.com/llvm/llvm-project/blob/llvmorg-21.1.8/clang/lib/CodeGen/Targets/X86.cpp) and [register rules](https://github.com/llvm/llvm-project/blob/llvmorg-21.1.8/llvm/lib/Target/X86/X86CallingConv.td) | Confirms positional Win64 carriers and documents indirect extended-scalar arguments with compiler-extension XMM0 results; that extension is not a formal Microsoft integer primitive mapping |
| [GCC 15.2.0 x64 classification](https://github.com/gcc-mirror/gcc/blob/releases/gcc-15.2.0/gcc/config/i386/i386.cc) | Independent implementation contrast, not selection of a MinGW/System V or RDX:RAX Terrum convention |
| [GCC 15.2.0 libbid extraction](https://github.com/gcc-mirror/gcc/blob/releases/gcc-15.2.0/libgcc/config/libbid/bid_internal.h) | Intel BID field extraction, special-value precedence and noncanonical coefficient/payload handling for decimal32/64/128 |
| [.NET pinned BID encode/decode](https://github.com/dotnet/runtime/blob/bd6d336bfd95a333e8b3f22f26259bbb659974cf/src/libraries/System.Private.CoreLib/src/System/Number.DecimalIeee754.cs) | Independently confirms finite/cohort extraction and canonical production; ordinary .NET API, comparer/hash and arithmetic helper choices are not thereby Terrum contracts |

IEEE 754-2019 sections 3.5.2 and 6.2 are the decimal interchange and special
value reference. Terrum's [target field rules](targets/windows-x64-data-layout.md#3-ieee-decimal-bid)
state the actual selected bit layout explicitly, without requiring a reader
to infer it from an implementation or substitute CLR System.Decimal.

IEEE 754-2019 section 5.5.1 distinguishes sign-only negate/abs from
NaN-processing arithmetic; section 7.1 shows that non-stop handling can still
raise status flags. The [floating-environment chapter](language/floating-environment.md)
selects Terrum's representation/result contracts and no ambient status
facility explicitly, rather than importing a full floating-environment API.
Sections 5.10 and 9.6 supply the representation-order/selection latitude used
by [numeric selection](language/numeric-selection.md), without adopting
number-only or raw-bit-order variants.
