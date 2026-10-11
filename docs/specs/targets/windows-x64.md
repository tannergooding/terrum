# Windows x64 native profile

## 1. Scope and identity

Logical `windows-x64-native/1` defines the Windows user-mode x64 fixed-layout
and ownership boundary. Its canonical chapters are:

| Chapter | Responsibility |
|---|---|
| [Data layout](windows-x64-data-layout.md) | All core scalar storage, validity, registered layout schemas, aggregate placement, padding, stride and packed access |
| [Call interface](windows-x64-call-interface.md) | Foreign primitive admission, carrier classification, argument positions, copies/results, borrows, initialization and ownership |
| [Deployment](windows-x64-deployment.md) | Preservation, non-unwinding default, image/unwind support, selected definitions, origin and retained code lifetime |

This profile completes that stated boundary, not every Terrum runtime
representation. Managed handles/interiors, stored checked-reference
descriptors, general views, tasks/continuations, erased witnesses, SIMD,
variadic/unprototyped calls, native C++ member/nontrivial-record conventions,
callbacks/COM and cross-language exception transport require separate
complete contracts. Exclusion from this profile does not remove a source
feature or a mandatory scalar.

The separately selected
[Windows x64 failure bridge](windows-x64-failure-bridge.md)
supplies paired contained exception transport and its exact synchronous
callback entry. It does not add general callbacks, COM, async/managed
closures or a shared native unwinder to this ordinary profile.

The existing target requirement `1` still identifies Windows x64 native.
The logical revision above introduces no separate layout-profile identity
or new numeric selector. Exact rule agreement remains necessary; the target
number alone does not identify every promised layout, runtime or semantic
revision. In [current interchange](../representation/requirements-and-admission.md#3-before-use-admission),
required governing rules are agreed out of band, and missing Blittable,
ownership or borrow codecs remain unsupported before independent use.
Neither an optional Attr row nor reserved bits activate this profile.

This is a normative design contract, not a claim about compiler support.
The [bounded implementation slice](../../proposals/public-fixed-layout-profile.md)
and [development native guide](../../implementation/native.md) retain their
narrower support and implementation scopes.

## 2. Standard foreign mapping versus Terrum defaults

Physical storage, a conventional nonforeign carrier and a formal foreign
primitive mapping are different facts.

All 26 core scalars have the storage defined here, including formats without
direct machine arithmetic. The call chapter gives native-style default
carriers when jointly lowered Terrum code uses this profile. Those defaults
do not assert that Microsoft defines an equivalent foreign primitive.
Private calls may be specialized where both sides are regenerated together,
provided source observations and every retained boundary survive.

A direct foreign primitive requires a formally defined equivalent primitive
in the selected Windows ABI, plus the actual compatible declaration and value
contract. Accordingly, Windows x64 declines direct int128/uint128,
float16/bfloat16/float128 and IEEE-decimal foreign signatures.
Explicit, fully matched records or buffers can carry their bits; an implicit
conversion, Transparent wrapper or compiler-extension spelling cannot
manufacture formal primitive equivalence. Another target can independently
admit its own standardized equivalents.

No custom multi-register Terrum ABI is selected here. In particular, an
instruction producing RDX:RAX, a System V convention or a compiler's extended
integer XMM0 return does not define Microsoft's integer-result ABI.

## 3. Imported platform rules

The following Microsoft Learn sources are identified by their source
`git_commit_id` and the `msvc-170` view. Only the listed portions are
normatively imported, within this profile's admitted scope. Later page edits
and adjacent C++ language rules are not automatic profile revisions.

| Source | Source revision | Imported portions |
|---|---|---|
| [x64 calling convention](https://learn.microsoft.com/en-us/cpp/build/x64-calling-convention?view=msvc-170) | `e61067551f92a1c7beaad20c04b9f5ccfd2b5dd6` | Default positional parameter passing, admitted scalar/POD record results, shadow space, preservation, function-pointer code addresses, FPCSR and MXCSR obligations |
| [x64 ABI conventions](https://learn.microsoft.com/en-us/cpp/build/x64-software-conventions?view=msvc-170) | `d25e83731f0600bc021bd0b084ad5812b6cfe88f` | Scalar storage table, aggregate/array alignment, register preservation and cleared direction flag |
| [Struct member alignment](https://learn.microsoft.com/en-us/cpp/build/reference/zp-struct-member-alignment?view=msvc-170) | `feeb48729629a6d8ef41a2cef3179e1751e746f7` | Packing values 1/2/4/8/16 and the x64 default 16 |
| [align](https://learn.microsoft.com/en-us/cpp/cpp/align-cpp?view=msvc-170) | `feeb48729629a6d8ef41a2cef3179e1751e746f7` | Power-of-two alignment range 1 through 8192, explicit nested alignment and combination with packing; parameter alignment is distinct from object alignment |
| [Stack usage](https://learn.microsoft.com/en-us/cpp/build/stack-usage?view=msvc-170) | `36738035d38d4d2ab474a3f4681366fa341a6f6c` | Allocated stack extent, home-area ownership, call alignment, frame/leaf obligations and separately aligned storage |
| [Prolog and epilog](https://learn.microsoft.com/en-us/cpp/build/prolog-and-epilog?view=msvc-170) | `feeb48729629a6d8ef41a2cef3179e1751e746f7` | Describable prologs/epilogs and guarded-stack probing before allocation |
| [x64 exception handling](https://learn.microsoft.com/en-us/cpp/build/exception-handling-x64?view=msvc-170) | `e61067551f92a1c7beaad20c04b9f5ccfd2b5dd6` | Version-1 RUNTIME_FUNCTION/UNWIND_INFO/UNWIND_CODE framing, applicable operations, chains, function lookup and dynamic registration; no language-specific exception-object or handler-data format |
| [PE/COFF](https://learn.microsoft.com/en-us/windows/win32/debug/pe-format) | `55e845e9f3a6a3261f59a2f460f61edc25ffcdd5` | AMD64 machine identity, PE32+ headers, section/image extents, addressing/relocations, imports/exports and exception-directory framing for used facilities |

Optional ISA features are not baseline requirements. Code using additional
registers, instructions, OS state or a newer unwind encoding needs its exact
admitted CPU/OS support and applicable preservation/unwind rules before use.
This profile does not adopt the APX unwind preview by citing its parent page.

The precise Terrum scalar and aggregate rules in the data-layout chapter
resolve its own types. General C++ examples, defaults for other architectures,
nontrivial records, bitfields and compiler implementation extensions do not
override them. The imported procedure-call rules do not import C/C++ numeric
promotion, aliasing, object lifetime, union-punning or exception semantics.

## 4. Complete boundary admission

An admitted boundary retains the selected producer/definition, complete
normalized logical signature, exact target/rule revision, physical carrier
signature, layout/validity commitments, access and alias requirements,
initialization outcomes, transfers, failure policy and code/helper lifetime.
Its consumer establishes [directed contract satisfaction](../representation/binding-and-replacement.md),
not equality of a name, DLL path, byte count or private package version.

Body truth is separately checked or admitted through the explicit
semantic/native provider trust policy. A layout calculation, successful OS
load or integrity digest proves neither a NoThrow claim nor native ownership
and output behavior. Invocation still satisfies its actual preconditions.

Unknown or missing required mappings, records, rule agreement, helpers,
lifetimes or value modes decline before affected binding/lowering/execution.
No hidden boxing, pin, allocation, retain, ownership registry, source rebinding
or JIT fallback repairs an unsupported crossing. ABI-required by-value
supporting storage is not such a repair.

Default foreign calls are direct and non-unwinding. Ordinary failures use
their explicitly matched status/output contracts. Exception interchange is
specified by the separately selected
[contained bridge](windows-x64-failure-bridge.md); its adapters bear that
cost without changing every ordinary call. Windows unwind data alone
supplies none of that language interchange.

## 5. Conformance

[Required cases](../conformance/target-windows-x64.md) distinguish exact
storage and call classification from source/contract admission and actual
runtime obligations. Independent native producers and consumers are needed
for physical interoperability; Terrum-only round trips and supplied finite
contract facts are not sufficient execution evidence.

The normative `.trrm` schema and the private `.trmn`/`.tnset`/`.trng`
taxonomy are unchanged. This profile standardizes no native package bytes,
Core binary identities, import catalog, startup API or managed-machine ABI.
