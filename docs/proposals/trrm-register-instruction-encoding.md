# Register IL instruction encoding

**Status: User-approved design handoff; not integrated.** The user approved
the core design and consolidated worksheet with final numeric allocations,
post-compact-body tiny ranking and outer Body/SourceMap integration kept
separate. Recorded semantic decisions are settled; illustrative assignments
and explicitly deferred members are not allocated or approved by implication.
This document changes no canonical specification, approves no Batch 4/5
integration or other proposal, and claims no compiler or runtime support.

The objective is an instruction representation suited to real code, not
compatibility with the existing IL or unpublished numeric assignments.
The [operation-family proposal](trrm-operation-encoding-families.md) and
current instruction set are context to critique, not constraints on the
replacement. Neither is retired by this document.

## Review boundaries

| Area | Current discussion position |
|---|---|
| Value naming | User-selected layout: fourteen general registers, R14 contextual zero, and R15 all bits set or discard/unused according to field role. Boolean uses valid true; floating interpretation follows the raw all-one pattern, including decimal signaling NaNs. |
| T/H banks | Not part of this candidate. Register choice makes no purity or effect-ordering promise. |
| Control flow | Candidate: registers can cross blocks; consumers construct SSA merges if wanted. |
| Widths | One mixed-stream candidate: sixteen one-byte slots, 176 two-byte primary values and 64 four-byte family values. The partition is provisional; the tiny members and final opcode assignments are not selected. |
| Future encoding | Reserve a code-body instruction-codec selector for later encodings; it does not select semantic features or authorize decoder guessing. Exact header packing remains open. |
| Method implementation forms | User-selected exact field-accessor and identity-forwarder descriptors without instruction streams, retaining method contracts and identity. Other adaptations/operations use an ordinary stream. |
| Selector families | Arithmetic, higher math, comparisons, ternary logic and selected vector operations can merit dedicated four-byte families with a full control byte. No family must exist at all three widths. |
| Integer source default | User-approved: ordinary integer add/subtract/multiply/negate are checked in development/debug and wrapping in release. Explicit policy remains fixed in the emitted IL. Normative integration is deferred. |
| Arithmetic compactness | Both wrapping and checked are supported. Checked and saturating variants need no one-/two-byte forms; use full four-byte representations. |
| Register transfer | User-selected value-alias `mov`, not a source copy/transfer/clone. Alias availability follows the same resource identity; storage and cleanup actions remain separate. |
| Parameter access | Loads, transfers and exposed parameter homes must refer to one coherent logical parameter instance; the exact home codec is not selected. |
| Boolean complement | Recommend type-directed `not` for integers and `bool`, not a separate `bool.not`. |
| Comparison results | User-selected: binary `cmp` produces a one-hot uint8 outcome; `setcc`/`jcc`/conditional movement test ordinary uint8 masks. Scalar three-input predicate chains produce bool. Predicate bits and cmp3 nibble order are selected. |
| IEEE signaling | IEEE exception handling is disabled by default, with no language-visible NaN signals. Future opt-in handling may be software-only. Four-bit predicates do not need an in-band signaling bit in this default profile. |
| Constants and limits | Explicit typed ConstantMap; user-selected sixteen-bit method-local indices, unsigned thirty-two-bit inventory counts and independently wider global references. A wider local-index codec is deferred. |
| Storage writes | User-selected typed semantic `store.*` for initialization/replacement, with explicit destination state, authority and source-use contracts. Cleanup/commitment follows the complete transition, not an unconstrained drop/store pair. |
| Element/range operations | User-selected internal typed place handles retain target/check/backing/access facts; reference formation is explicit. A dedicated slice opcode remains unselected. |
| Calls | User-selected compact windows plus u16-indexed general call-site records with completed register/home inputs and restricted typed destination preparation; explicit staging precedes checks/preparation/invocation/publication. |
| Return and failure | One signature-aware `ret`; explicit `failfast`, `throw` with a payload kind, runtime `require`, and `unreachable` that raises an ordinary language failure with unwinding. No separate rethrow opcode is necessary. |
| Numeric allocation | Every value below is illustrative and may be regrouped or reassigned. Unselected cells are not frozen holes. |

The recorded decisions and consolidated handoff are user-approved within
their stated scope. A space-budget entry, reserved cell or candidate number
does not approve an unselected operation, source API, executable profile
or registry assignment.

## Semantic owners and intended amendments

The proposal would intentionally replace the instruction envelopes,
per-result wire naming and relevant Body records in
[the instruction-set chapter](../specs/il-instruction-set.md), together with
their [registry assignments](../specs/representation/registry.md).
The exact Body/operand-record/SourceMap changes would also amend
[IL metadata](../specs/il-metadata.md). No replacement is integrated here.

The following contracts supply meaning unless a later review explicitly
amends them:

| Contract | Owner |
|---|---|
| Complete types and normalization | [Complete types](../specs/language/types-and-normalization.md) |
| Scalar domains, shifts and comparisons | [Primitive operators](../specs/language/primitive-operators.md) |
| Conversion pairs and policies | [Conversions](../specs/conversions.md) |
| NaNs, sign operations and floating observations | [Floating environment](../specs/language/floating-environment.md) |
| Abs/Min/Max/Clamp | [Abs](../specs/language/absolute-value.md), [numeric selection](../specs/language/numeric-selection.md) |
| Values, places and zero-data unit | [Values and execution](../specs/language/expressions-and-statements.md) |
| Initialization and joins | [Structural flow](../specs/language/initialization-and-flow.md) |
| Transfer, replacement and retirement | [Ownership and cleanup](../specs/language/ownership-and-cleanup.md) |
| References and backing | [References](../specs/language/references.md), [dependencies](../specs/language/dependencies-and-views.md), [lifetimes](../specs/lifetimes.md) |
| Call staging and publication | [Call contracts](../specs/language-contracts.md), [calls and inference](../specs/language/calls-and-inference.md) |
| Exact-schema access and admission | [Reader trust](../specs/representation/reader-trust.md), [format versioning](../specs/representation/format-versioning.md) |

IEEE exception handling is disabled by default in this design. That agrees
with the current value-only floating-environment direction: NaNs do not
produce language-visible IEEE signals by default, and incidental hardware
status is not a portable observation. Future opt-in handling, possibly
provided only in software, remains an intended extension. Its exact API,
effects, metadata and version availability are not integrated here.

An operation's inclusion below does not register a primitive, approve an
intrinsic by method spelling, select a foreign ABI, or complete a pending
logical-record codec. Current implementation boundaries remain in
[the implementation reference](../implementation.md).

## Names, values and storage

| Concept | Meaning in this candidate |
|---|---|
| Computed value | A typed result at a computation site. A consumer may give it an immutable SSA identity. |
| `R0..R15` | Sixteen encoded operand slots: fourteen general value bindings plus the selected zero and all-bits-set/discard roles described below. |
| Argument | A logical parameter binding described by the selected signature and parameter mode, not a permanent machine-register assignment. |
| Local | Typed logical storage with initialization, access, backing and lifetime state. Promotion does not change its source contract. |
| Place | Selected storage that can be read, initialized, replaced, borrowed or transferred under its own permissions. Not every place exposes a data address. |
| Immediate | A literal value encoded by an instruction form, not a read of a register or place. |
| Physical register/stack slot | A backend choice. One logical value may need several machine registers or backing storage. |
| SSA definition | A consumer's logical identity. Its numbering need not occupy the wire operand namespace. |

A class handle, a checked reference descriptor and a raw pointer are distinct
types with different authority and dependencies. Value types include all
primitives and aggregates; they are not restricted to machine-word payloads.
One large or generic value still occupies one logical register name.

Every ordinary operation reads its sources before publishing its destination
bindings. Rebinding a name does not itself write a local, retire a value,
release backing, or duplicate ownership.

All computations have ordered program positions. A consumer can immediately
create ordered value/operation nodes instead of deferring effectful trees.
Register selection does not authorize reordering a call, failing check or
memory observation. A stack format could also be imported this way; the
choice of register encoding is not a claim that stacks inherently require
effectful expression trees.

### Selected special operand slots

The user selected two high-numbered special slots, preserving R0/R1 for
common compact tuples. This is a wire operand assignment, not a reservation
of physical hardware registers.

| Encoding | Source role | Destination/unused role |
|---|---|---|
| R0..R13 | Currently available computed value | General result binding |
| R14 | Context-typed zero under the selected contract below | Not a writable general destination |
| R15 | All bits set in the admitted value representation; Boolean true as below | Discard/no destination where allowed; ordinary unused register field, with the recipe combined-token exception below |

The same R15 encoding has different meanings in different operand roles;
it does not hold mutable storage. A used source field means the constant.
An unused field is not read, and an admitted discard field does not publish
a register binding.

R14 is contextual zero. R15 is representation-oriented all bits set, not a
per-type maximum-value constructor. For an admitted same-width bit view,
its pattern is the one obtained by bitcasting an all-one integer into that
type. Floating-point is included, especially for masking and other
bit-oriented uses. Boolean retains its selected valid true contract.

| Admitted type | R14 source value | R15 source value |
|---|---|---|
| Signed integer | Zero in that complete integer type | All bits set, therefore -1 |
| Unsigned integer | Zero in that complete integer type | All bits set, therefore the maximum value |
| Bit-vector/mask domain | All zero | All one |
| Boolean | False in the valid Boolean representation | True in the valid Boolean representation |
| Binary floating-point | Positive zero | Negative quiet NaN with every payload bit set; the complete binary pattern is all ones |
| Decimal floating-point | Positive zero, coefficient 0 and exponent 0 | All bits set; under BID this is a negative signaling NaN with noncanonical raw payload |
| Eligible vector | The corresponding zero in every lane | The corresponding integer/mask/Boolean/floating constant in every lane |

Other types require their own admitted constant contracts; R14 does not
implicitly construct a null reference or default aggregate. Negative
floating zero and alternate decimal zero cohorts use their exact typed
ConstantMap entries. R15 is an explicit bit-pattern constant, not a
NaN-processing arithmetic result. Loading/copying it preserves its exact
sign, quiet/signaling state and raw payload; it does not quiet the value,
normalize a noncanonical decimal payload or substitute the positive
zero-payload constant-arithmetic recipe. An operation consuming it still
follows that operation's own result contract; admitting a constant does not
admit every operation on its type.

Under the current
[Windows x64 BID layout](../specs/targets/windows-x64-data-layout.md#3-ieee-decimal-bid),
the signaling bit has the opposite sense to the binary quiet bit. An all-one
decimal container is a negative signaling NaN. Its all-one raw payload also
exceeds the canonical decimal payload range and decodes to numeric payload
zero, without making the raw NaN container invalid.

For decimal32 BID the selected constant is exactly `0xFFFFFFFF`, as from
bitcasting signed integer -1. Decimal64/128 likewise use all one bits in their
complete containers. Do not clear the signaling bit or replace the pattern
with a largest canonical payload. Classification follows the selected value
representation, not host floating arithmetic.

These cannot be arbitrary typed storage registers containing universal
valid bit patterns:

- Integer zero and all-bits-set can mean zero and signed -1/unsigned maximum.
  A Boolean true is a Boolean value, not permission to create an invalid
  Boolean storage representation.
- The selected floating zero is a value/representation contract, not
  permission to substitute arbitrary numerically equal decimal cohorts.
  Reference and aggregate constants still need explicit admissibility
  and type information.
- If every input is a special constant, an untyped destination may not
  establish the result type. A typed constant entry or explicit typed form
  supplies that information; there is no default integer type.
  A previous destination binding does not permanently type that register.
  An RMW operation can obtain the type from its actual ordinary source.
- A discard destination does not cancel checks or effects, and cannot
  abandon an affine cleanup responsibility. Nontrivial results need their
  explicit retirement/transfer contract.
- An unused register field is not a value read. Its canonical encoding can
  reuse a constant slot without implying that the operation consumes that
  constant. Arithmetic recipes instead use their selected combined
  register/negation absent tokens.

Register-field parsing remains four bits. A writable general result must
name R0..R13. R15 is valid only when that result can be discarded under its
actual responsibility/effect contract. Pair loads/staging require general
destinations; an operation with independently discardable scalar results
can use R15 for those results. Distinct published general results must use
distinct bindings; repeated discard fields do not publish the same binding.
Other indices are not register encodings: local or method-map index 15
remains an ordinary valid index, not a discard sentinel.

### Cross-block values and phi-equivalent merges

A block declares its available incoming general-register bindings
(R0..R13), with their complete types and required state. General registers
not admitted by that entry state cannot be read merely because a predecessor
happened to leave bits in the corresponding location.

R14/R15 source constants are not incoming bindings or phi inputs. Their
admissibility follows their typed operand contracts at each use; they do not
need a predecessor to establish a mutable register value.

If `R0` is live into a join, an `int32` definition on one incoming edge and
a `float32` definition on another do not form one typed value. The producer
must convert both to an agreed type or explicitly construct a common
representation. Equal widths, nominal similarities and implicit boxing
do not supply that agreement.

If the join overwrites `R0` before any subsequent use, its incoming binding
is dead and the incoming types need not agree. A use in a later successor
before overwrite also makes the incoming binding live; this is not only
about the first instruction in the join.

The merge is phi-equivalent. Its result is an ordinary typed value and may
flow across further blocks. A consumer wanting SSA constructs the merge;
the wire need not serialize its version number. A definition counter handles
straight-line renaming, but joins and loop backedges still require merge
construction.

Type agreement does not erase distinct incoming roots, loans, ownership
responsibilities, reservations or place states. Their guarded relationships
must survive the merge. Promotable locals may require SSA merges too, even
if register flow were prohibited.

## Method implementation forms

The user selected compact implementation descriptors for exact field-backed
accessors and identity-forwarding methods. They need no conventional Body
instruction stream.

| Selected implementation form | Required content and meaning |
|---|---|
| Instruction stream | Ordinary register/control-flow code with its required state and index records. |
| Field read | A selected field reference, receiver/static binding and exact read/result contract. |
| Field write | A selected field reference, input binding and initialization/replacement/access contract. |
| Field reference | Only if actually admitted: exact reference access, backing and result dependency contract, not an ordinary value getter reinterpreted as a borrow. |
| Identity-forwarding call | A selected bound method reference with exact receiver/parameter and result relationships under the ordinary invocation protocol. |

These can link to a FieldDef or MethodDef where appropriate; imports,
constructed owners and selected dispatch may require a richer bound
reference. A link alone does not define an adaptation. Only fully specified
direct mappings qualify. Conversions, boxing, unrepresented borrowing/
ownership changes or additional body operations use an ordinary stream;
declaration entry/exit contracts still apply to every body form.

The accessor/forwarder remains its own method declaration. Preserve its
identity, contracts, receiver authority, entry/exit phases, required type or
static initialization, caller-unsafety context, normal/exceptional output
state, retained origins and selected source/debug behavior. It is not a
method or field alias that erases the wrapper's obligations.

Field writes still have the actual typed-store/replacement semantics.
Reference getters do not acquire implicit pinning or lifetime extension.
An eligible forwarding call still stages and invokes under its source
contract, including preparation and cleanup where applicable.

Neither a name such as `get_...` nor a producer attribute alone proves
eligibility. The producer must establish the implementation descriptor's
truth, and consumers apply the same access, binding and semantic-admission
policy as for an ordinary Body. Unsupported required body kinds decline;
they are not treated as empty methods.

This is a method implementation choice, distinct from a per-block
instruction-width codec. Its potential saving includes instruction-stream
maps and framing, not just the few bytes of a getter or forwarding sequence.
The MethodDef/implementation reference layout and kind tags belong to the
consolidated codec review; selecting these body forms does not allocate them.

## Width and operation-family alternatives

The active physical candidate below uses a small set of selected one-byte
forms, with two-/four-byte forms as the primary encoding. It does not use
the earlier two-windows-for-every-operation scheme.

The size study measured that earlier implied-window scheme, which consumed
128 first-byte values. Its results explain the potential benefit and the
concentration in a small subset; they are not a definition of the active
allocation or an optimized measurement of the new layout.

### Format-pressure study

A static study inspected 29 selected methods from the installed .NET 11
RC2 CoreLib and LINQ assemblies, including actual
`System.Numerics.Decimal32` methods and their generic IEEE-decimal helpers.
Both assemblies identify revision
`11.0.0-rc.2.26504.105+820230c386db5b0106c5430b3e5aee92a7826bf5`.

The study maps CLR evaluation-stack positions mechanically to registers,
without register allocation, inlining, local promotion or SSA optimization.
All models use the same resulting operation sequence. Consecutive argument
loads can form one load-pair operation. A stack pop emits no operation;
a duplicate emits a register transfer. This tends to favor low-register
windows and retains CIL local traffic that an optimized register lowering
might remove. It is not an optimal register allocation or a completed
Terrum instruction selection.

The following figures are **modeled instruction-stream bytes**, not actual
Terrum encodings, complete artifact sizes, native code sizes or runtime
measurements:

| Selected group | Methods | All operations four bytes | Mixed 2/4 | Current implied-window 1/2/4 | Entire block either two or four bytes |
|---|---:|---:|---:|---:|---:|
| Collection methods | 4 | 548 | 328 | 261 | 520 |
| LINQ selection/iteration | 3 | 612 | 364 | 301 | 550 |
| Scalar methods and wrappers | 15 | 496 | 284 | 202 | 386 |
| Generic IEEE-decimal helpers | 4 | 2,872 | 1,880 | 1,744 | 2,694 |
| CLR Decimal software helpers | 3 | 5,752 | 3,512 | 3,215 | 5,358 |
| Total of this selection | 29 | 10,280 | 6,368 | 5,723 | 9,508 |

Mixed 2/4 forms save about 34-43% against the same operations all using
four bytes in these groups. The implied one-byte windows save a further
7.2% in the generic IEEE-decimal helpers, 20.4% in the collection methods
and 10.1% across this selection. Tiny wrappers benefit more than the large
helpers. The total is not a representative frequency distribution of
CoreLib execution or of all applications.

#### Instruction counts and percentage bases

The modeled instruction sequence is held fixed. Supporting shorter forms
changes its encoded byte count, not its instruction count. Argument-pair
combining has already been applied equally to every model. These are not
counts of native instructions or unchanged original CIL instructions.

| Method | Modeled instructions in every format | All four-byte bytes | Mixed 2/4 bytes | Implied-window 1/2/4 bytes |
|---|---:|---:|---:|---:|
| `List<int>.get_Item` | 10 | 40 | 24 | 18 |
| `List<int>.Add` | 30 | 120 | 70 | 50 |
| `List<int>.Insert` | 52 | 208 | 124 | 101 |
| `List<int>.RemoveAt` | 45 | 180 | 110 | 92 |
| Collection subtotal | 137 | 548 | 328 | 261 |
| `Number.CompareDecimalIeee754` | 79 | 316 | 188 | 161 |
| `Number.AddDecimalIeee754` | 335 | 1,340 | 912 | 865 |
| `Number.MultiplyDecimalIeee754` | 98 | 392 | 244 | 222 |
| `Number.DivideDecimalIeee754` | 206 | 824 | 536 | 496 |
| Generic IEEE-decimal subtotal | 718 | 2,872 | 1,880 | 1,744 |

The width distributions are:

| Group | Encoding | One-byte instructions | Two-byte instructions | Four-byte instructions | Total instructions | Bytes |
|---|---|---:|---:|---:|---:|---:|
| Collections | All four | 0 | 0 | 137 | 137 | 548 |
| Collections | Mixed 2/4 | 0 | 110 | 27 | 137 | 328 |
| Collections | With 1/2/4 | 61 | 52 | 24 | 137 | 261 |
| Generic IEEE-decimal | All four | 0 | 0 | 718 | 718 | 2,872 |
| Generic IEEE-decimal | Mixed 2/4 | 0 | 496 | 222 | 718 | 1,880 |
| Generic IEEE-decimal | With 1/2/4 | 136 | 360 | 222 | 718 | 1,744 |

Collections save 220 bytes when two-byte forms become available, then another
67 bytes when one-byte windows become available. The 20.4% is that additional
67-byte reduction relative to the 328-byte mixed stream. Some implied
one-byte forms replace a four-byte form directly, not a two-byte form.

The IEEE-decimal group saves 992 bytes when two-byte forms become available,
then another 136 bytes with one-byte windows. The 7.2% is that additional
136-byte reduction relative to 1,880 bytes, not an increase in size and not
7.2 percentage points relative to the all-four-byte baseline.

The 1/2/4 stream is smaller in every comparison here. The separate concern
is primary opcode-space cost. The model does not spend the opcode space freed
by removing one-byte forms on making more operations short, so it does not
compare two independently optimized final encoders.

In the same model, the sixteen most useful potential one-byte forms account
for 519 of the 645 saved bytes: 80.5% of the one-byte benefit and 8.2% of
the mixed 2/4 stream. Thus one-byte forms need not be all-or-nothing.
A smaller set may retain much of the benefit without allocating both
windows for every one of 64 common operations. The selected sample is not
large enough to choose that set.

Roughly sixteen one-byte forms are therefore a reasonable first budget to
evaluate. They are not proven to be the best final sixteen or representative
of a complete .NET 11 CoreLib scan.

The ranking is for instruction-stream bodies. Field-backed and forwarding
implementation descriptors could remove argument loads, field accesses,
calls and returns from that population entirely. The sample contains fifteen
methods in its scalar/wrapper group, not fifteen certified forwarders.
`List<int>.get_Item` has bounds/access behavior and is not a plain automatic
field getter. Classify eligible compact implementations before extrapolating
the ranking; the change could be material rather than a small adjustment.

The next comparison should first select body representations, then lower
the remaining streams into the chosen operation families and compare total
artifacts, including descriptors/maps/indexes. The existing figures do not
measure the new condition-consumer families or arithmetic recipes.

Important limitations:

- Maps, constants, operand/call/switch records, block-entry state, indexes,
  mode tags, source data and padding are excluded.
- The two width models share the same short-form eligibility; the study
  does not use newly available opcode space to compress more families.
- Its eligibility predates the full-only checked/saturating policy and
  any special-register reservation. It is not a measurement of the
  completed updated encoder or a development/release runtime comparison.
- Metadata indices use an explicitly chosen small-index policy and maps
  ordered by first appearance. A final codec or allocator can differ.
- Comparisons producing values use a general four-byte form; comparison
  branches are modeled as fused four-byte operations.
- Memory/address and prefixed operations conservatively use full forms.
  This is not a complete type-aware lowering or a proof that the
  mechanical transcription is executable Terrum.
- CLR shift, Boolean, initialization and ownership semantics are not
  silently adopted. The sample does not qualify Terrum's affine or
  first-class-unit contracts.

Detailed method, assembly-hash, model and form-frequency evidence is retained
as session research data. These figures justify further encoding comparison,
not a schema selection or performance claim.

### Family-specific long forms

There is no requirement that every four-byte instruction begin with a
separate prefix and an operation byte. For the following shape, all four
bytes are already occupied:

```text
operation:8 | immediate/control:8 | destination:4 | input0:4 | input1:4 | input2:4
```

Adding an independent width-prefix byte cannot preserve that shape in four
bytes. A long-operation first-byte range can identify the size and operation
family together. That first byte is the operation, not an additional prefix.

Examples of possible four-byte family layouts, without numeric allocations:

| Family | Payload after its first operation byte |
|---|---|
| General scalar operation | Function selector:8, destination and up to three inputs:16 |
| Compare condition | Control:8, condition destination:4, up to three inputs:12 |
| Ternary bit logic | Truth table:8, destination and three inputs:16 |
| Arithmetic recipe | Recipe/control:8, destination and three inputs:16 |
| Higher math | Selected operation/control:8 and its registered input/result layout |
| Condition value selection | Predicate:8, destination:4, condition:4, true input:4, false input:4 |
| Conversion policy | Destination type index:16, destination/source registers:8 |
| Bound call | Method index:16, result register:4, argument-window base:4 |
| Constant/local access | Register:4, index:16, reserved/control:4 |
| Boolean branch | Boolean register:4, edge-record index:16, reserved/control:4 |
| Condition-test branch | Predicate:4, condition register:4, edge-record index:16 |
| Larger compound/control form | Typed operand-record index:16 and its registered control fields |

The table illustrates field budgets, not approval of those result contracts,
fallthrough rules or index limits. Wider cases still need an explicit
record or bounded extension.

A mixed 2/4 decoder can reserve one contiguous first-byte range for long
families and use the remainder for two-byte operations. The split need not
be equal. Two-byte forms can then use an operation byte and a full operand
byte, without preserving the old six-bit logical-opcode packing.

The working short-form budget is a sixteen-entry one-byte island, with the
remainder divided between short operations and long families. That is a
candidate budget, not sixteen selected members. Any later change in its size
would revise the partition explicitly; the tiny set is not generated
mechanically for every operation and window.

An operation need not have all three widths. Simple frequent forms may
deserve one or two bytes, while general arithmetic, higher math, comparisons,
ternary logic and selected vector operations can justify dedicated four-byte
families. Their motivation is useful semantics and compact explicit controls,
with efficient intrinsic lowering where available, not a claim of measured
time or energy savings.

### Ternary logic controls

For one bit from each of A, B and C, index the truth table as
`(A << 2) | (B << 1) | C`:

```text
index  A B C
  0    0 0 0
  1    0 0 1
  2    0 1 0
  3    0 1 1
  4    1 0 0
  5    1 0 1
  6    1 1 0
  7    1 1 1
```

Reading each input down the rows as an eight-bit column gives:

```text
A = F0
B = CC
C = AA
```

Apply a Boolean expression to those columns, restricting complement/results
to eight bits, to obtain its control byte. These values are truth-table
data, not instruction opcode assignments.

| Function | Control |
|---|---|
| Constant false | `00` |
| Constant true | `FF` |
| A, B, C | `F0`, `CC`, `AA` |
| `not A` | `0F` |
| `A and B` | `C0` |
| `A or B` | `FC` |
| `A xor B` | `3C` |
| `nand(A,B)` | `3F` |
| `nor(A,B)` | `03` |
| `xnor(A,B)` | `C3` |
| `(A and B) or ((not A) and C)` | `CA` |
| Majority: at least two inputs are one | `E8` |
| Minority: at most one input is one | `17` |
| `((not A) or B) xor C` | `65` |

Majority is `(A and B) or (A and C) or (B and C)`. Minority is its
complement. For the last example:

```text
((not F0) or CC) xor AA
= (0F or CC) xor AA
= CF xor AA
= 65
```

An immediate can therefore select every Boolean function of three inputs,
not merely a permutation of the inputs. The same construction extends to
more inputs, but requires one truth bit per input combination: three inputs
fit exactly into eight bits; four need sixteen bits.

On an admitted integer/bit-vector type, apply that function independently
at each bit position and preserve the declared width. Constant true then
means all bits set, not integer one. Scalar Boolean and vector-mask result
representations need their own admitted contracts. This does not make raw
bitwise operations valid on arbitrary owners, reference descriptors or
invalid value encodings.

The `CA` function is per-bit selection by A's mask. It is not the same as
whole-value conditional movement on one Boolean/condition result unless
the required full-width mask relationship has been established.

Named register operands must still be valid typed inputs. A control byte
that ignores one input does not erase a preceding effectful computation,
check or required cleanup. The instruction has no implicit memory operands,
short-circuit argument evaluation, or permission to reinterpret a reference
as bits.

### Arithmetic recipe controls

An analogous `ternarith` family can use the full control byte for selected
recipes built from `neg`, `add`, `sub` and `mul`. Unlike Boolean truth
tables, eight bits cannot describe every arithmetic function over arbitrary
numeric values. They select a finite, explicitly defined recipe catalog.

All four operations are eligible even when they can overflow. A recipe
compactly expresses the operations to perform; encoding it does not require
a proof that they succeed. JIT/AOT import reconstructs their typed, ordered
AST/IR nodes and then performs its normal canonicalization and optimization.
The recipe is not restricted to mathematical functions known to be total.

Failure/rounding distinctions below constrain semantics-preserving rewrites,
not whether a potentially failing operation can be represented. An importer
can normalize the tree or retain individual checked nodes as appropriate;
the wire need not serialize those intermediate node identities.

Both operation slots select all four operations, not only the binary three.
The user selected this control-byte layout:

| Field | Bit positions | Meaning |
|---|---:|---|
| Inner operation | 0..1 | `0` neg, `1` add, `2` sub, `3` mul |
| Outer operation | 2..3 | The same operation-code convention |
| Outer operand order | 4 | Zero: inner result first; one: remaining input C first |
| A negation/modifier | 5 | Negate A, unless part of the absent token |
| B negation/modifier | 6 | Negate B, unless part of the absent token |
| C negation/modifier | 7 | Negate C, unless part of the absent token |

These are recipe-control data, not primary opcode assignments. The four
register nibbles remain destination plus A/B/C. Unary slots have explicit
roles:

```text
inner = innerOp(A, B)      // inner neg uses A; B is unused
result = outerOp(inner, C) // outer neg uses inner; C is unused
```

The user selected combined source-selector/modifier patterns for absence:

| Recipe domain | Absent source pattern | Meaning |
|---|---|---|
| Integer | R14 with its negation/modifier bit one | Absent, not an evaluated negative zero |
| Floating | R15 with its negation/modifier bit one | Absent, not an evaluated negated NaN |

Recognize the combined token before reading or negating a value. Every
unused unary source must have that domain's token; a required source must
not have it. Inner negation therefore puts the token in B, and outer
negation puts it in C. Unmodified R14/R15 keep their constant meanings.
The integer encoding gives up no distinct zero value; an actual floating
negated all-one pattern remains expressible through a normal register or
typed ConstantMap.

This is a recipe-specific combined operand code, not the ordinary bare-R15
unused-field convention. It requires no extra bit or operand byte.

The order bit can instead make a binary outer operation
`outerOp(C, inner)`. Renaming the input registers then expresses right-nested
forms without a separate permutation selector. Outer `neg` ignores neither
the inner operation nor its failures; it negates the inner result.

Input-negation steps belong at their selected tree positions. For the
inner-first form, negate participating A/B, compute the inner operation,
then negate C if requested and perform the outer operation. For the
C-first form, perform its requested negation before the inner subtree.
The modifier bit belonging to an absent token is one, but causes no
negation step. Outer negation requires the irrelevant outer-order bit to
be zero. These unary rules admit 182 of the 256 raw control patterns;
equivalent recipes remain valid, so this is not a claim of 182 distinct
arithmetic functions.

The binary-only examples remain:

```text
(A op0 B) op1 C
A op0 (B op1 C)
```

Operand permutations can already be expressed by the three register input
fields. No on-wire local or SSA ID is required for the inner result.

Normalization is separate from encodability. In wrapping fixed-width
arithmetic, `x + (-y)` and `x - y` agree modulo the width. In checked
arithmetic they need not have the same failure behavior: for `int8`
`x = -1, y = -128`, `neg(y)` overflows, while `x - y` is the representable
value 127. Both recipes are valid to encode; the importer must preserve
the check or prove the rewrite valid rather than prohibit the recipe.

| Candidate recipe | Required distinction |
|---|---|
| `(A * B) + C` | Strict multiply followed by add is not single-rounding FMA. |
| `(A + B) * C` | Preserve its grouping, intermediate result and failure site. |
| `A - (B * C)` | Preserve noncommutative operand order. |
| `(neg A) + (B * C)` | Negation and the product have their own ordered semantics. |
| Input/result negation variants | Float sign-only behavior, signed zero, NaN state and checked minimum/unsigned failure cannot be replaced by algebra alone. |

Each selected control must define its operation tree, ordered steps, admitted
types, intermediate precision/rounding, overflow policy, floating signaling
effects, result publication and source/failure identities. A code meaning
negate C before the inner multiply is not generally interchangeable with a
source expression that multiplies first and only then negates C.

The family explicitly selects one wrapping, checked, saturating or
strict-floating policy for the recipe's active nodes; the control byte
does not silently choose it. Mixed policies require separate instructions
unless a later explicit form is selected. Fused operations remain distinct,
not another interpretation of a strict mul/add tree. Exact primary family
assignments and source/failure-record codecs remain unallocated.

Identity padding is not required. It is not universally equivalent even
without signaling NaNs: checked unsigned negation of all-ones fails rather
than supplying one; floating negation of all-ones supplies NaN rather than
one; adding negative decimal zero at quantum 0 can change a supplied
cohort. Producers may use actual identity operations where their complete
value/effect/representation contract permits, not as an implicit codec rule.

Fusion must also respect computations outside the recipe. In
`(a * b) + F()`, moving a checked multiply until after F's result exists
could execute F before an overflow that originally prevented it. Use a
compound form only when its complete ordering/effect contract matches,
or retain the ordinary sequence.

A fused multiply-add needs its own single-rounding contract. A compound
strict recipe must not contract to hardware FMA merely because the target
has that instruction. Future IEEE status and trap observations make the
intermediate steps significant even where ordinary numeric values coincide.

### Selected integer profile policy and explicit IL semantics

The user-approved policy follows Rust's development/release
overflow-checking policy for ordinary integer add/subtract/multiply/negate:

| Source compilation profile | Ordinary operation policy |
|---|---|
| Development/debug | Checked |
| Release | Wrapping |
| Explicit checked or wrapping operation | Its explicitly selected policy in every profile |

This is an approved amendment of the current source default, awaiting
separately authorized canonical integration. Profile selection must happen
before relevant source checking, constant evaluation and serialization.
It cannot silently weaken a declared NoThrow/purity/contract obligation to
make a development build pass.

The wire must distinguish the selected arithmetic semantics, not defer them
to the consumer's build configuration. A JIT/AOT compiler importing a checked
artifact cannot silently reinterpret it as wrapping because it is optimizing
a release build. The approved source profile choices are resolved before
serialization and retained in the actual operations/contracts.

The IL supports both policies. Wrapping forms can use compact opcodes;
checked and saturating variants use full four-byte representations and
do not require matching one-/two-byte forms. Checked recipes remain valid
even when they may overflow. Internally, an
optimizer can use unchecked arithmetic with explicit required checks,
eliminate checks under valid proofs, or retain checked nodes. It must
preserve the selected failure and observation contract.

A proven-equivalent lowering can use a compact wrapping operation when a
checked result cannot overflow. This does not create a compact checked
opcode or let optimization discard a required failure.

Wrapping is not C-style undefined signed overflow or permission to mark a
potentially overflowing operation as impossible. Any separately proposed
assume/proven-no-overflow operation would need its own explicit contract.

An overflow analyzer remains useful, especially for unintended release
wrapping, but is not a substitute for checked semantics where selected.
A warning for every unknown-input
operation that could possibly overflow would be extremely noisy. Definite
failures, suspicious size/index calculations, arithmetic before widening
and relevant flow facts are better candidate diagnostics, with explicit
wrapping intent recognized. Exact warnings and source syntax are not
selected here.

Rust's [Cargo profile defaults](https://doc.rust-lang.org/cargo/reference/profiles.html)
enable overflow checks for development and disable them for release;
they are configurable. Its [overflow reference](https://doc.rust-lang.org/reference/expressions/operator-expr.html#overflow)
also retains checks for signed minimum division/remainder by -1 even when
overflow checks are disabled. The [overflow RFC](https://rust-lang.github.io/rfcs/0560-integer-overflow.html)
explicitly discusses the difficulty of static detection and runtime cost.
These sources explain the tradeoff; they are not measurements of Terrum's
performance.

Division by zero, division/remainder edge policies, shift counts, literal
construction, numeric conversions and floating exception handling remain
separate decisions. Choosing wrapping add/subtract/multiply does not
silently change all of those behaviors. The static format-pressure study
measured encoded-size choices, not runtime costs of checking or vectorization.
Runtime `require` and explicit throws do not become debug-only checks.

### Comparison conditions and consumers

The selected scalar comparison model produces ordinary `uint8` outcome
bits, with consumers for Boolean values, branches and conditional movement:

| Family | Intent |
|---|---|
| `cmp` | Compare admitted values and produce exactly one uint8 outcome bit under the selected comparison/signaling policy. |
| `setcc` | Materialize a uint8 predicate-mask test as ordinary `bool` or an admitted mask result. |
| `jcc` | Transfer control according to the uint8 predicate-mask test. |
| `movcc` | Conditionally replace a register binding; the nonselected path retains the old destination. |
| `selectcc` | Select one of two already available complete values into a destination. This can be a distinct form of the conditional-move family. |
| Floating classification | Test properties such as NaN, signaling NaN, finite, zero or sign without assuming the effects of an IEEE comparison. |

For a binary ordered numeric comparison, the natural condition information
is less/equal/greater/unordered. Integer/character comparisons do not produce
unordered; equality-only domains do not acquire ordering.

The user selected condition-test mask bits 0..3 for less, equal, greater
and unordered, respectively. Every subset is valid:

| Mask | Matching outcomes | Meaning for a single cmp outcome |
|---|---|---|
| `0` | None | Always false |
| `1` | Less | Ordered less than |
| `2` | Equal | Equal |
| `3` | Less/equal | Ordered less than or equal |
| `4` | Greater | Ordered greater than |
| `5` | Less/greater | Ordered not equal |
| `6` | Equal/greater | Ordered greater than or equal |
| `7` | Less/equal/greater | Ordered |
| `8` | Unordered | Unordered |
| `9` | Less/unordered | Less than or unordered |
| `A` | Equal/unordered | Equal or unordered |
| `B` | Less/equal/unordered | Less than or equal, or unordered |
| `C` | Greater/unordered | Greater than or unordered |
| `D` | Less/greater/unordered | Ordinary not equal |
| `E` | Equal/greater/unordered | Greater than or equal, or unordered |
| `F` | Every outcome | Always true for a cmp result |

These are predicate data values, not opcode allocations. In an admitted
domain that never produces unordered, the U bit simply never matches;
it is not an invalid control. Predicate 0/F does not erase effects of an
already executed comparison or its operand producers.

The `cmp` result is exactly `uint8(1)`, `uint8(2)`, `uint8(4)` or
`uint8(8)`. Consumers accept every uint8 value, not only values with
comparison provenance, and use the test `(value & predicate) != 0`.
High uint8 bits do not match a four-bit predicate. Thus predicate F is
not unconditionally true for arbitrary masks: zero or high-bits-only masks
test false. Predicate 0 always tests false. This is a defined bit test,
not validation of a hidden enum or permission to infer a comparison.

These are four bits of predicate selection, not four independent possible
outcomes at once: a binary numeric relation has one of four outcomes.
Adding an independent signaling-policy flag gives five control bits and
32 truth/policy combinations, as distinct from a fifth relation state.
With IEEE exception handling disabled, the current predicate needs no such
in-band policy bit. Two four-bit predicates fit one control byte.

The outcome is an ordinary typed value, not a hidden architectural FLAGS/NZCV
register or a new opaque Body type. It uses existing uint8 storage, calls,
SSA construction, block-entry types, merges and liveness. A backend may use
native flags while their lifetime fits and the integer bits are not
observed; when observed, it must supply the promised uint8 value, not a
target-specific flags image. Scalar compound predicate chains instead use
the existing Boolean type.

```text
cmp   R3, R0, R1
setcc R4, R3, lt.ordered
jcc   R3, lt.ordered, Btrue, Bfalse
movcc R5, R3, eq, R6
```

These are proposed semantic forms, not fixed encodings. The first comparison
may supply a stored/returned Boolean through `setcc`, a branch through `jcc`,
or a whole-value selection. This addresses value comparisons without
requiring every relation to own an independent semantic opcode.

`movcc` is register movement, not a conditional memory store. Its retaining
form requires a valid old destination on the nonselected path. Selection
does not execute deferred input expressions, implicitly drop a nonselected
owner, or replace checked reference/value semantics with bitwise masking.
Affine selection still needs its explicit responsibility-transfer contract.

The full control byte need not be in the same stage for every family.
A binary compare can carry signaling/evaluation controls while its consumer
carries the relation mask. `selectcc` fits a predicate byte and four register
nibbles. `setcc` needs fewer register fields and may have a compact form.
`jcc` also needs successors: arbitrary predicate/register/target combinations
cannot all be promised in four bytes. Use an explicit fallthrough contract,
target record, restricted form or bounded extension where necessary.

Compact relation spellings or fused compare/set/branch forms remain possible
physical encodings. They need not be available at every width. Their code
size and instruction-count effects require a new study; the published
format-pressure figures hold a different operation sequence fixed.

### Three-input comparisons

Three-input comparison families cover expressions such as `a < b && b < c`.
The selected control byte stores the A/B predicate in its low nibble and the
B/C predicate in its high nibble. AND versus OR belongs to the selected
family, not another bit stolen from those predicates.

Two ordered-less predicates give control `11`; ordered A<=B and B<C gives
`13`. This is not an arbitrary truth table over two four-way relations;
that would need sixteen result bits.

The three inputs need the same admitted complete comparison domain, or an
explicitly selected richer contract. The family does not invent numeric
promotions or replace arbitrary nominal comparison calls.

The user selected an ordinary Boolean result for scalar predicate chains:
apply the two selected predicates and combine them by the family's AND/OR
rule. Do not return a pair of raw relations or introduce another condition
kind. The result uses ordinary Boolean branching and selection; an extra
`setcc` is not required.

The semantic family labels `cmp3.and` and `cmp3.or` describe the two combining
rules without assigning primary opcode values. Producing Boolean IL does
not require a native Boolean register or `setcc`: a backend may retain the
result in flags and fuse its consumer when the operation/effect contract
permits. This is representation freedom, not a current support claim.

Signaling and evaluation order constrain this design:

- An eager predicate chain performs both primitive comparisons. It is not
  automatically a source short-circuit conjunction.
- A short-circuit chain must evaluate the first comparison, test it, and
  skip the second when required, including its invalid-operation effects.
- Producing C can itself have effects or fail. A three-input instruction
  consuming existing values cannot justify evaluating C's producer on a
  source path that never evaluated it.

With the default exception-disabled profile, the byte can be dedicated to
the two four-bit predicates. Conjunction/disjunction and eager/short-circuit
selection must still be specified by the family. Future exception-observable
operations can use an explicit helper/extended form rather than squeeze two
five-bit controls into eight bits.

### IEEE signaling and classification

IEEE exception handling is disabled by default. Ordinary floating operations
and comparisons do not produce language-visible NaN signals for either quiet
or signaling NaN inputs. Their specified values, quieting/sign-preserving
boundaries and representation rules still apply.

An implementation may incidentally set hardware flags under the selected
ABI, but those are not a portable default observation and software need not
emulate them. This does not disable checked integer failures, runtime
requirements or explicit language throws.

Future opt-in IEEE handling may be software-provided only. It need not
unmask native hardware traps or match the target's condition/status-register
representation. A special comparison/handling helper or explicit extended
operation can carry that policy without spending a fifth bit in each of
the default predicate nibbles. The helper API and activation mechanism are
not selected.

Keep three axes separate:

| Axis | Question |
|---|---|
| Predicate | Which comparison outcomes make the selected condition true? |
| NaN signaling | Does invalid arise for sNaN only, for any NaN, or under a separately defined value-only/classification operation? |
| Handling/observation | How is invalid represented, observed, masked or trapped under the selected floating environment? |

When IEEE exception-observable behavior is enabled, a quiet comparison
signals invalid for an sNaN; a qNaN
does not. A signaling comparison signals invalid for either kind of NaN.
The Boolean relation/unordered outcome is separate from that effect.
The SoftFloat [comparison functions](https://www.jhauser.us/arithmetic/SoftFloat-3/doc/SoftFloat.html#8.8)
describe both forms; AArch64
[FCMP](https://www.scs.stanford.edu/~zyedidia/arm64/fcmp_float.html) and
[FCMPE](https://www.scs.stanford.edu/~zyedidia/arm64/fcmpe_float.html)
provide a concrete quiet/signaling pair.

In that opt-in setting, signaling does not itself prescribe a Terrum
language exception or an unmasked hardware trap. The current accepted
[floating-environment chapter](../specs/language/floating-environment.md)
is value-oriented and exposes no ambient status/trap API. The intended
extension needs an explicit status/handling contract, mode availability,
effects and source/metadata/target requirements. A v1 implementation can
decline a declared unsupported mode; it cannot silently execute it quietly.
Encoding capacity alone does not establish full IEEE conformance.

If opt-in handling makes such effects observable, they occur at the selected
comparison/helper, not when `setcc`, `jcc` or `movcc` reads its condition.
Consumers do not repeat the original invalid event. Conversely, CSE or
fusion cannot discard, merge or move signaling comparisons merely because
their relation values are equal. Different signaling policies, intervening
status observations/clears, traps and required event origins matter.

A consumer selecting a less-than predicate does not change the signaling
policy of an earlier quiet compare. If the required operation has another
policy, produce that comparison or establish the exact effect equivalence;
relation equality alone is not sufficient.

Classification needs special care once exception behavior is observable.
In the default value-only profile, the unordered result of self-compare can
supply `is.nan` where the complete value contract agrees. In an IEEE
exception-observable profile, an sNaN would signal invalid for a quiet
self-compare but not for a non-signaling classification. The earlier warning
is about that effect distinction, not a prohibition on default no-signal
comparisons. A classification family remains useful for direct NaN/finite/
signaling-NaN/other tests. The RISC-V [FCLASS contract](https://docs.riscv.org/reference/isa/v20260120/unpriv/f-st-ext.html#20-1-9-single-precision-floating-point-classify-instruction)
is an example of classification without floating exception flags, not a
selection of its representation for Terrum.

The comparison condition must retain unordered as distinct from greater
or equal. `.NET CompareTo` can deliberately order/equate NaNs and therefore
is not a substitute for this raw relation contract.

### Per-block codec choices

Two proposals should not be conflated:

| Choice | Assessment |
|---|---|
| Pure two-byte blocks versus pure four-byte blocks | One operation that cannot use the narrow form widens the whole block. |
| Compact mixed 2/4 blocks versus full-word blocks | Avoids that narrow-block restriction, but introduces another decode/encoding table. Full-word mode must earn its benefit through capacity, fewer records/operations or measured import cost. |

For example, twenty narrow operations plus one full operation occupy 44
instruction bytes in a mixed stream, versus 84 when the entire block becomes
four-byte instructions. The study's fixed-by-block policy costs 49.3% more
stream bytes than mixed 2/4 across the selection, before mode tags or padding.
That does not measure possible reductions from a differently designed
full-word instruction repertoire.

If a block codec is retained, its mode must be explicit and independently
available with the block index. Both codecs must preserve the same semantic
contracts. Rooted values, owners, calls or the presence of a branch must not
automatically force a different semantic profile or serialized SSA.
Do not split real basic blocks merely to manufacture width boundaries.

The initial candidate uses one mixed codec, not these per-block modes.
A future codec can introduce explicit block modes if their benefit is
established; no per-block mode is inferred from its contents.

### Breadth before final byte assignments

| Area | Representation pressure |
|---|---|
| Scalar arithmetic | Type-derived width/signedness/format, explicit checked/wrapping/fused policy; not one opcode per scalar identity. |
| Comparisons/consumers | Typed condition results, `setcc` Boolean/mask materialization, `jcc`, conditional movement, unordered and signaling behavior. |
| Classification | Non-signaling NaN/sign/finite tests or class results, distinct from comparison side effects. |
| Bit operations | Shifts/rotates, counts and bit selection; arbitrary ternary logic genuinely needs all eight truth-table bits. |
| Conversions | Source/destination identities and selected rounding/overflow/bit-preserving policy. |
| Storage and references | Locals, fields, indexed places, views, access authority and retained backing. Hardware addressing modes need not be copied wholesale. |
| Aggregates and calls | Multiple values, selected targets, larger lists and non-word-sized values; records remain legitimate. |
| Atomics | Operation, ordering and result contract. Some useful operations have more than four logical operands/results and need another form or aggregate result. |
| SIMD | Lane/mask semantics, shuffles, widening/narrowing and controls; constant tables or records handle controls that are not eight-bit immediates. |
| Lifetime/control | Transfers, cleanup, handlers, suspension and required state facts, without a register-class purity promise. |
| Cold target-specific functionality | Explicit registered intrinsics or calls; not every native mnemonic needs a primary portable opcode. |

.NET exposes much of its hardware breadth through callable intrinsics rather
than dedicated CIL instructions.
[FloatComparisonMode](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.intrinsics.x86.floatcomparisonmode)
includes ordered/unordered and signaling/non-signaling choices.
[VPTERNLOG](https://www.felixcloutier.com/x86/vpternlogd:vpternlogq)
uses all 256 truth-table values.
[VCMPPS](https://www.felixcloutier.com/x86/cmpps) has an immediate predicate
field. These facts motivate full selector fields and deliberate portable
signaling semantics, not a requirement to reproduce every native encoding
or make native condition/status registers part of the wire model.

AArch64
[FCMP](https://www.scs.stanford.edu/~zyedidia/arm64/fcmp_float.html)
records a four-way floating relation in NZCV, including an unordered flag
pattern. A portable IL can retain explicit values while a backend chooses
native condition flags. The
[RISC-V compressed extension](https://docs.riscv.org/reference/isa/v20260120/unpriv/c-st-ext.html)
provides a relevant precedent for intermixing 16/32-bit forms with operand
restrictions; its published compression results are not Terrum measurements.

The working direction is family-specific four-byte forms and compact forms
where they earn their space, with roughly sixteen one-byte forms as an
initial study budget. Reevaluate that budget after compact method bodies
and the condition-consumer model are included. A pure fixed-width-per-block
split and the old 128-value one-byte allocation are not selected.

## Active mixed instruction encoding

This is the current physical candidate, not a final registry allocation.
It replaces the half-space implied-window layout as the proposal's active
format. All examples are proposed IL, not runnable Terrum source.

| First byte | Ordinary size | Available primary values | Role |
|---|---:|---:|---|
| `00..0F` | 1 byte | 16 | Individually selected tiny forms with fully implied operands |
| `10..BF` | 2 bytes | 176 | Short operations/forms with one operand byte |
| `C0..FF` | 4 bytes | 64 | Long families with three payload bytes |

These ranges are a provisional partition for review. The sixteen tiny
members, short operation numbers and long family numbers are not selected.
An unassigned value is invalid, not a default operation.

```text
one byte:
    tiny-form opcode:8

two bytes:
    short-form opcode:8 | operand:8

four bytes:
    long-family opcode:8 | family payload:24
```

The ordinary size decoder is:

```text
if firstByte < 0x10: 1 byte
else if firstByte < 0xC0: 2 bytes
else: 4 bytes
```

Only the first byte identifies the ordinary size. It is an actual primary
opcode/family, not an extra width
prefix. Later bytes are interpreted under that opcode's schema and do not
recursively select another instruction size.

The one-byte set is deliberately limited. There is no global window bit
that automatically gives every operation both an R0 and an R1 spelling.
Related short and long physical forms normalize to the same semantic
operation, but need not preserve a literal opcode byte across widths.

### Selected one-byte forms

Each chosen tiny code specifies its whole operand tuple. Possible useful
tuples include:

| Shape | Example tuple, if selected |
|---|---|
| Return | `return R0`, or the signature-defined unit result |
| Argument access | `R0 = a0`; a different selected code could mean `R1 = a1` |
| Argument pair | `R0:R1 = a0:a1` |
| Wrapping arithmetic | `R0 = op.wrap(R0, R1)` |
| Another wrapping tuple | `R1 = op.wrap(R1, R2)`, only if it separately earns a slot |
| Local/constant access | A particular register and small local/constant index |
| Common call | A particular selected target/window/result tuple |

These are candidates, not sixteen assigned members or a promise that all
shown shapes will be included. The observed top-sixteen ranking explains
the budget; compact implementation bodies and new operation families can
change the final set.

Where an operand is both source and destination, the form is register
read-modify-write (RMW). RMW applies to registers as well as memory and
does not imply atomicity. A register write does not itself mutate its
referent or acquire ownership.

### Two-byte forms

The complete operand byte can hold two four-bit fields:

```text
byte0: short-form opcode
byte1: YYyyxxxx

x = byte1 & 0F
y = byte1 >> 4
```

Unlike the previous study layout, no register bits are borrowed from the
opcode byte. All sixteen encoded selectors are representable; the selected
general/special partition determines valid sources and writable destinations.
The operation selects the operand roles:

| Family | Meaning of `x`, `y` |
|---|---|
| Unary value operation | Destination `Rx`, source `Ry` |
| Binary value operation | `Rx = op(Rx, Ry)` |
| Register `mov` | Bind `Rx` to the value currently named by `Ry` |
| Argument/local/constant load | Destination `Rx`, small index `y` |
| Argument-pair load | General destinations `Rx:R[x+1]`, arguments `a[y]:a[y+1]`; x must be at most 12 and the argument range must fit |
| Local store | Local index `x`, value `Ry` |
| Local take | Destination `Rx`, local index `y` |
| Field load/store | Selected compact field form; register roles and any implied selector are explicit in that form |
| Reference load | Destination `Rx`, reference `Ry` |
| Reference store | Reference `Rx`, value `Ry` |
| In-place conversion | Register `Rx`, small destination type index `y` |
| In-place `setcc` | Condition register `Rx`, admitted predicate `y`; bind its Boolean result to `Rx` |
| Non-unit return | Value `Rx`; unused bits are zero |

Other short forms can use a complete eight-bit index or immediate when
their register operands are implied. Control targets remain block indices,
not byte displacements. No operation is required
to provide every shape. Binary RMW forms read the old destination binding
before publishing the result.

These short arithmetic/conversion forms do not include checked or
saturating variants. Those policies remain full four-byte operations.

### Four-byte families

A long primary value identifies a family and its exact payload schema.
It does not universally introduce another opcode byte.

| Family layout | Payload after the first byte |
|---|---|
| General scalar arithmetic | Function/policy selector:8, register fields:16 |
| Compare or arithmetic recipe | Control:8, destination and up to three inputs:16 |
| Ternary logic | All eight truth-table bits, destination and three inputs:16 |
| Selected higher math/vector operation | Control:8 and a registered sixteen-bit input/result layout |
| Whole-value condition selection | Predicate:8, destination:4, condition:4, true value:4, false value:4 |
| Conversion policy | Destination type index:16, destination/source registers:8 |
| Integer immediate operation | Immediate:16, destination/left registers:8; operation/policy selected by the primary |
| Selected field access | Field reference index:16, two register fields:8 |
| Bound call | Method index:16, result:4, argument-window base:4 |
| Constant/local access | Register:4, index:16, reserved/control:4 |
| Typed operand record | Record index:16 and the registered operation-specific fields |

For a common control-plus-four-register form:

```text
byte0: family opcode
byte1: control/function
bytes2..3, little-endian:
    bits 0..3:   destination
    bits 4..7:   source 0
    bits 8..11:  source 1
    bits 12..15: source 2
```

The selected member can instead assign those nibbles to two results and two
inputs, or another explicitly registered layout. Reserved bits are zero;
ordinary unused register fields use R15. Families with combined operand
codes, such as arithmetic recipes, use their explicitly selected absent
patterns. Neither is a constant read.
An operation/type/control combination without a selected contract
rejects; fitting the fields is not semantic admission.

For illustration, `ADD2 52` could name a two-byte RMW add of R5 into R2.
`ALU4 ADD 32 F4` could name a four-byte add of R3 and R4 into R2,
with R15 in the unused third-source field.
`ADD2`, `ALU4` and `ADD` stand for unassigned byte values, not new numeric
allocations.

Integer immediate forms must state the literal's complete operand type and
range rule. A shift's encoded count can have an explicit `int32` control
type without converting it to the data type. Floating constants and other
literal domains use typed constants unless an exact immediate form is
selected.

### Instruction-codec reservation in the header

Use an explicit `instructionEncoding:u8` in the code-body header.
Initially only the mixed codec above is recognized. Other selector values
are reserved for future codecs; unused bits have defined zero values.

The approved worksheet uses a separate byte, not assumed spare header bits.
Its numeric codec identifier and placement in the complete outer Body
schema remain integration work. The study must not treat this byte as free.

| Concern | Required rule |
|---|---|
| Scope | Applies to instruction-stream implementations, separately from field/forwarding implementation kinds. |
| Current behavior | One mixed 1/2/4 codec; no width mode inferred from a block's types, effects or contents. |
| Future codec | Can supply another instruction encoding or explicitly described block-codec scheme. |
| Unknown selector | Decline/reject affected use; never guess from bytes or fall back to the current decoder. |
| Semantics | Does not select source rules, numeric policy, IEEE availability, ABI, producer trust or required semantic features. |
| Schema evolution | Changing instruction/Body records still needs exact-schema agreement under format versioning; a selector is not an automatic compatibility guarantee. |

This is an expansion point if primary space or other encoding requirements
eventually justify another codec, not a need to design a second current ISA.
Family controls and typed records already provide considerable capacity.
Their existence does not prove space can never become constrained.

### Maps, size and alignment

#### Explicit typed ConstantMap

The Body has a ConstantMap alongside its TypeMap and MethodMap. Loading a
constant names an existing typed entry; it does not require the decoder
to rediscover CSE or execute construction code.

| Property | Candidate rule |
|---|---|
| Identity | Complete type and exact admitted constant value/representation, not numerical equality alone. |
| Payload | A selected typed constant record or reference to one, with all required literal/data dependencies. Large values live in that record, not the instruction bytes. |
| Index | Selected unsigned sixteen-bit Body-local index, independently of register numbering. |
| Duplicates | Valid. Producer interning/deduplication is optional, not a consumer rescan or uniqueness requirement. |
| Construction | Compile-time/frozen construction must already satisfy its selected contract; loading does not invoke arbitrary constructors or factories. |
| Nonliteral identities | References, addresses, callable/data relocations or aggregates require their own admitted constant contract. Arbitrary bits do not manufacture an owner, checked reference or valid object. |

Same bits with different complete integer types remain different typed
values. Opposite signed zeros, distinguishable NaN payload/state, and
equal-valued decimal cohorts must not be merged by numerical comparison.
The map can contain both equivalent and identical entries without changing
operation typing or canonicality.

This supports deciding representation and reuse at the producing tool's
appropriate stage, rather than requiring one upfront global CSE or
constructor-normalization strategy. The selected register special constants
are only short operand forms; they do not replace the general ConstantMap.

#### Selected method-local index limits

The user selected unsigned sixteen-bit method-local indices. A zero-based
index can select 0..65,535, so the corresponding maximum count is 65,536,
not 65,535.

| Item | Selected width/scope |
|---|---|
| Local and argument slots | Sixteen-bit method-local indices; flattened receiver/parameter inventory must fit. |
| ConstantMap, TypeMap, MethodMap and selected field references | Sixteen-bit Body-local indices. |
| Operand/call/failure/edge records and region/block identities | Sixteen-bit method-local indices where selected by this codec. |
| Inventory counts | Unsigned thirty-two-bit fields; the selected local-index codec admits counts 0..65,536 without an ambiguous sentinel. |
| Global/module references inside map records | Keep their selected global width; a local map does not cap global table identities to sixteen bits. |
| Code extents, offsets, instruction counts and internal SSA definitions | Separate namespaces and widths; not automatically limited to sixteen bits. |

More than 65,536 computed values does not imply more than that many locals.
Internal SSA, optimization and inlining need not use sixteen-bit counters.
Reserializing an oversized result must diagnose the codec's limit or use
an explicitly supported future codec, not wrap an index, silently truncate
the inventory or reinterpret this encoding.

Every index value remains usable, including `0xFFFF`. Where a schema permits
absence, its kind/presence contract represents that absence without stealing
a valid table index. An empty inventory has count zero, not a wrapped or
sentinel count for 65,536.

The header reservation leaves room for a wider future codec if actual
requirements justify one; no wider local-index variant is selected now.
These are user-selected design limits, not a claim that every source
implementation or native ABI has the same limits. Their approval here
does not integrate canonical TRRM restrictions.

Register capacity is independent of argument/local counts, map counts,
instruction counts and offsets. Ten thousand definitions using `R0` do not
widen its selector.

Instructions are not individually padded. A Body row may be aligned to
four bytes, with padding outside its instruction extent; this does not imply
that every four-byte instruction starts at a four-byte boundary.

No per-result type array is required solely to decode the common stream.
Types come from incoming bindings, selected metadata and operation schemas.
Selective block decoding still needs its entry types/state. Direct lookup
of every historical definition is not free: an optional index or prefix
decode may be needed.

## Semantic family inventory

This inventory is not a numeric opcode map. The 176 short values can select
common operations or restricted forms, while a long primary can select a
family whose control byte selects many members. There is no mandatory
global operation byte or one logical-ID-to-primary-byte correspondence.

| Family | Members or scope | Likely encoding role, subject to selection |
|---|---|---|
| Scalar arithmetic | Add/subtract/multiply/divide/remainder/negate and full low/high integer products; explicit policies | Compact wrapping/common forms; checked and saturating variants use full forms; selected tiny wrapping tuples |
| Bit operations | AND/OR/XOR/NOT, shifts and admitted bit intrinsics | Short ordinary forms; long controls for wider functionality |
| Ternary logic | All 256 three-input Boolean functions | Dedicated long family with the complete truth-table byte |
| Arithmetic recipes | Explicit three-input operation trees and policies | Dedicated controlled long family, not silent FMA contraction |
| Higher math | Explicitly registered numeric operations and result layouts | Controlled long families or selected calls; no name-based recognition |
| Comparisons and consumers | `cmp`, `setcc`, `jcc`, `movcc`/`selectcc`, compound comparisons | General long controls; compact or fused forms only where useful and valid |
| Classification | NaN/signaling-NaN/finite/infinite/sign and admitted class queries | Defined non-signaling classification controls, not interchangeable comparison effects |
| Register transport | `mov` and any later justified binding operations | Short explicit fields; selected tiny tuples |
| Arguments/locals/constants | Loads, stores, takes and typed constants | Small-index short forms; larger inline indexes or records |
| Fields/references/indexing | Reads, stores, takes, reference/place formation, selected view operations | Registered access/layout forms; no implicit native pointer or lifetime extension |
| Conversions | Ordinary, wrapping, saturating and bit-preserving policies | Only admitted non-checked/non-saturating compact forms; full explicit typing for checked/saturating variants |
| Optional/aggregate values | Present/absent tests and construction, aggregate/field operations | Registered result/transfer layouts; records for larger lists |
| Calls | Bound targets, callable values, terminal calls and argument staging | Small windows, general inline windows and call-site records |
| Control flow | Block-index branches/switches/region exits, returns, `throw` payload kinds, `failfast`, `unreachable`, `require` | Short restricted forms, long forms or typed records; selected tiny returns |
| Lifetimes | Retirement, loans, backing, owners and required state transitions | Explicit admitted operations/records; not inferred from register reuse |
| Atomics | Selected observations, RMW/compare-exchange and order controls | Long families and records where operand/result counts require them |
| SIMD/masks | Selected lane, mask, shuffle, conversion and arithmetic contracts | Controlled long forms or records; not every native mnemonic |
| Extension | Required typed operation/operand records and future code-body codecs | Long family/record capacity plus the explicit header reservation |

Members, controls and type domains still need agreement. No operation gains
an executable contract merely because this inventory names it. Unselected
members stay invalid; no legacy alias is required to preserve a previous
draft's numbers.

## Operation meanings

### Scalar arithmetic, logic and selection

Let `I` be the twelve source integer identities and `F` the eight binary
and IEEE decimal floating identities. Character identities, `bool` and
`void` retain their separate domains. Nominal operations use selected calls,
not a numeric instruction chosen by representation size or method spelling.

| Operation | Inputs and result | Intent and important behavior |
|---|---|---|
| `add`, `sub`, `mul` | Two identical complete `T` in `I` or `F`; result `T` | Explicitly selected checked/wrapping integer policy or selected-format floating arithmetic. The wire does not inherit a JIT build's source default. No promotion or implicit FMA. |
| `div` | Two identical numeric `T`; result `T` | Integers retain zero-divisor and signed-minimum/-1 failures. Floating inputs retain IEEE outcomes. |
| `rem` | Two identical numeric `T`; result `T` | Integer remainder and truncating-quotient floating remainder retain their distinct exact contracts. Signed minimum modulo -1 is zero, not quotient overflow. |
| `neg` | One numeric `T`; result `T` | Integer negation uses its selected checked/wrapping policy; unsigned checked negation is zero-minus-value. Floating negation changes only the sign component. |
| Wrapping arithmetic | Admitted identical integer `T`; result `T` | Modular declared-width arithmetic. No ambient unchecked mode or floating policy change. |
| `abs` | One admitted numeric `T`; result `T` | Signed minimum can fail. Floating Abs clears the sign only; it does not quiet or normalize the input. |
| `and`, `or`, `xor` | Same integer `T`, or two `bool`; same result type | Eager bitwise/Boolean value operations. Not source short-circuit evaluation. |
| `not` | Integer `T` or `bool`; same result type | Integer: complement exactly the declared-width bits. Boolean: logical complement of the Boolean value. |
| Shifts | Integer data and integer count; result has data type | Validate the exact source count before representation bridging. `shr.logical` zero-fills even signed data. |
| `select` | Existing `bool`, existing `T`, existing `T`; result `T` | Choose an already computed value. Does not suppress effects that produced either input. |
| `min`, `max`, `clamp` | Same admitted numeric `T`; result `T` | Retain NaN, signed-zero, decimal representation and invalid-bound rules. Not arbitrary hardware min/max. |
| `copysign` | Two same-format floating values; result that format | Sign-component transfer; no numeric conversion or permission to normalize payload bits. |
| `length` | Admitted indexed/view family; family-defined count result | Placeholder for an explicitly registered length contract, not any member named Length. |
| `divrem` | Two same integer values; two same-type results | Checks required for the quotient remain even if only the remainder is later used. |

For the two-result `divrem`, a general payload can hold result0, result1,
left and right in its four nibbles. Published general destinations must be
distinct; independently discarded integer results may use R15. Both inputs
refer to their old bindings and both results publish only on success.
Discarding the quotient does not erase its required checks.
This is a different registered operand schema from a one-result
ternary operation, not a claim that every four-nibble payload has the
same roles.

Unifying IL `not` does not add source `~bool` or `!int32`. Source operator
spelling and eligibility remain in the language owner. A Boolean result is
still a valid `bool`; implementing it by complementing every byte of its
physical representation would generally be wrong.

`select` on affine values needs an explicit selected-input transfer and
remaining-responsibility contract. A nonselected owner does not disappear.
Until that contract is selected, such cases can use normal control flow;
the opcode table alone does not authorize ownership duplication.

### Full-product multiplication

The user selected only the full low/high integer-product form, provisionally
spelled:

```text
mul.full Rlo, Rhi, Ra, Rb
```

For admitted W-bit integer inputs, compute the exact 2W-bit product and
publish its W-bit low/high parts. This covers the largest input width
without inventing an int256 primitive or allocating arbitrary precision.
For two inputs of the same complete primitive integer type T, the user
selected unsigned low and high retaining T:

| Input T | Low result | High result |
|---|---|---|
| int8 | uint8 | int8 |
| uint8 | uint8 | uint8 |
| int16 | uint16 | int16 |
| uint16 | uint16 | uint16 |
| int32 | uint32 | int32 |
| uint32 | uint32 | uint32 |
| int64 | uint64 | int64 |
| uint64 | uint64 | uint64 |
| int128 | uint128 | int128 |
| uint128 | uint128 | uint128 |
| nint | nuint | nint |
| nuint | nuint | nuint |

Low is the unsigned low limb. High is the arithmetic upper part for signed
inputs and the unsigned upper part for unsigned inputs. Reconstructing the
mathematical product uses high times two to the W plus low. The complete
product always fits these two parts; no checked/wrapping/saturating variant
is required. Native width comes from the selected target; nint/nuint keep
their distinct identities even when a fixed-width type has the same size.
Mixed input types are not selected by this equal-type rule.

Two output selectors plus two input selectors fit the sixteen register bits
of a full four-byte form. Read both inputs before publishing outputs.
The independently unused integer halves may use discard R15; distinct
non-discard outputs must name distinct general bindings. Keeping only a
high or low half does not require another semantic multiply operation.

There is no separate widening-result IL multiply in the selected design.
Construct a wider primitive or aggregate from the halves where needed.
A backend may recognize that construction and select a natural whole-result
machine operation without changing the wire contract.

`MultiplyWidening` remains a possible library-level family name for a
constructed wider result, not another selected IL opcode. Its source API
and the existing [core-library proposal](core-library-reference.md) need
separate alignment during authorized integration.

The user deferred SIMD full-product multiplication; the agreed scalar
instruction remains active. No vector operand/result contract is admitted
by this scalar definition.

Possible library Lower/Upper/Even/Odd spellings describe selected input
elements, not product bits. These remain context for a future SIMD review,
not selected vector IL operations or a widening-result multiply.

### Floating predicates

The ordinary source predicate truth values are:

| Predicate | Less | Equal | Greater | Unordered: either input NaN |
|---|---|---|---|---|
| `eq` | false | true | false | false |
| `ne` | true | false | true | true |
| `lt` | true | false | false | false |
| `le` | true | true | false | false |
| `gt` | false | false | true | false |
| `ge` | false | true | true | false |
| `cmp.unordered` | false | false | false | true |

An ordered test is the Boolean complement of `cmp.unordered`. Numeric
equality equates opposite signed zeros and equal-valued decimal cohorts;
it is not representation equality or IEEE totalOrder.

The selected predicate masks also express the alternate unordered outcomes.
Descriptive spellings are aliases for these tests, not a requirement for
independent comparison opcodes:

| Spelling | Mask | NaN result |
|---|---|---|
| `eq.ordered` | `2` | false; ordinary `eq` |
| `eq.unordered` | `A` | true |
| `ne.ordered` | `5` | false |
| `ne.unordered` | `D` | true; ordinary `ne` |
| `lt/le/gt/ge.ordered` | `1/3/4/6` | false; ordinary relations |
| `lt/le/gt/ge.unordered` | `9/B/C/E` | true |

These names can denote tests of a comparison condition rather than separate
comparison instructions. The suffix specifies the unordered outcome, not
a total-order relation.
In particular, `not lt` is not ordinary floating `ge`: it is also true for
unordered inputs.

A NaN-truth toggle alone is not a signaling design. Predicate controls
belong to the condition-consumer design, while signaling and invalid handling
need the separate contracts described above. Comparison does not mutate the
input value to quiet it; status/trap effects remain separate.

### Register transfer versus value copying

The user selected one register value-alias operation:

```text
mov Rd, Rs
```

This binds `Rd` to the already computed value named by `Rs`. It does not:

- Invoke a user copy/clone operation.
- Construct another owner or another cleanup responsibility.
- Vacate a local or parameter.
- Destroy the previous value merely because its destination name is reused.

Register `mov` creates another name for the same logical value, including
its resource/dependency identity. A consuming use updates availability for
all aliases; it cannot consume that identity twice through different names:

```text
mov R1, R0
consume R0
consume R1       // invalid if the first consumption transferred this identity
```

`consume` above is explanatory notation for a consuming operation, not a
new instruction assignment. Rebinding cannot lose an unretired owner
responsibility. Another valid alias or explicitly tracked storage/cleanup
holder must retain it, or the responsibility must be discharged before the
last available name is lost.

The importer reconstructs identity from definitions, alias edges and typed
control flow; it does not need serialized per-expression SSA numbers.
Independently formed loans/views keep their own backing/access contracts;
register aliases do not create those contracts or extend them.

A source-level Copy, transfer or clone is a different contract. A read
from storage, a store, a parameter binding or a selected callable must state
whether it copies, transfers or borrows. Register movement is not evidence
that any of those actions are legal.

### Storage reads, stores, takes and retirement

The names should describe what happens to storage, not copy familiar
source-language assignment terminology into the opcode list.

| Family | Intended meaning |
|---|---|
| `load.*` | Obtain an eligible nonconsuming value read from a live typed source. Copy eligibility and dependencies still apply. |
| `store.*` | Write a complete typed value to the selected destination under its actual initialization/replacement authority. |
| `take.*` | Transfer a live value out of eligible storage, leaving that source vacant, without cleaning the transferred value. |
| `retire.*` | End the selected live value under its ordinary cleanup/responsibility contract; not a byte clear or register pop. |
| Reference formation | Produce an admitted descriptor/loan for a place, not a copy of the pointee. |

Thus the working names replace `assign.local/ref/field/index` with
`store.local/ref/field/index`, and `move.local/ref/field/index` with
`take.local/ref/field/index`. A `take` still has a source-storage effect;
it is not register `mov`.

A non-Copy source does not become copyable because a load targets a
register. Appropriate take/reference operations or a selected copying
contract are required. The argument model must also distinguish reading
an incoming binding from taking or addressing a mutable parameter home.

The user selected one typed semantic store for initialization and
replacement, with explicit destination state/authority and source-use
contracts:

| Destination state | Transition | Obligation |
|---|---|---|
| Vacant | Initialize | No old live value exists to retire; require the actual initialization authority. |
| Live | Replace | Preserve required retirement, checks, commitment and confinement. |
| Maybe-live | Follow the admitted guarded transition | Do not assume vacant or unconditionally retire an absent value. |
| Partial | Preserve actual subobject state | Do not treat a partially initialized aggregate as vacant whole storage. |

An initialization-only loan does not grant replacement authority. The
source-use contract must also state whether a value is copied, transferred
or borrowed; the name `store` grants none of those permissions by itself.

Retain the semantic transition through verification and lowering. It may
become multiple machine operations, without promising a raw, nonthrowing,
no-call or physically atomic store.

Source replacement evaluates the destination once and the new value once,
then checks actual state/dependencies before commitment. Failed new-value
construction does not first destroy the old value. Retirement and
installation cannot expose an incomplete receiver to callbacks, reentrancy
or escaping failure. A naive unconstrained `drop old; store new` does not
establish that invariant.

Automatic authored Cleanup is checked NoThrow and nonsuspending, but may
have observable effects and reentrancy. Its complete/partial retirement
order and the surrounding checks still matter. Ending a reference
descriptor does not destroy its referent.

No dedicated `init.*` or hot `drop.*` allocation is selected here. Their
required semantic facts cannot disappear merely because the earlier names
were confusing.

### Fields, elements and ranges

| Operation | Intent |
|---|---|
| `load.field` | Read a selected stored field under its type/access contract. |
| `extract.field` | Obtain one field's value from an already computed aggregate value, without forming a storage address. |
| Field-place formation | Select a field's place without reading its payload. |
| `read.index` | Check the selected indexed access and read its value. |
| `store.index` | Perform the selected indexed write; source lowering must preserve when target formation/checks occur. |
| Element-place formation | Select and validate one element target without reading or initializing its payload. |
| Proposed `slice` | Form a bounded view of existing backing, if a dedicated operation is selected. |

Element-place formation selects one element's target without reading its
value. For an update, the abstract sequence can be:

```text
target = form_element_place(base, index)
old    = read(target)
rhs    = invoke F()
new    = add(old, rhs)
write(target, new)
```

This explains operation order; the physical place-record codec is not assigned.
Base and index are evaluated once. Required bounds/place checks are not
silently delayed past `F`, and rereading a rebound source variable does not
select a different target.

The user selected an internal typed place handle. It retains the selected
target and captured inputs, target type, backing dependencies, access/
initialization authority and required check timing for load/store/take.
It is not a public source type, checked reference, raw pointer, native
address or pin.

A handle can occupy a logical register and participate in typed flow.
Register aliases refer to the same selected place and authority; they
cannot duplicate an exclusive right or conceal a storage-state transition
performed through another alias. The representation need not be a single
hardware address or register.

Reference/address formation is explicit and must establish its actual
descriptor, loan, access, backing and stability obligations. Selecting a
place does not silently promise those stronger capabilities. Rebinding a
source name does not retarget the captured place, and retaining its backing
dependency does not create ownership or an arbitrary lifetime extension.

A value-returning property/indexer does not become a borrowable backing
place. Accessor updates use their selected getter/setter contracts.
Actual reference formation retains access, roots, backing and invalidation
dependencies; it neither pins storage nor extends an owner's lifetime.
Not every logical place exposes an address, including zero-data `void`.

The former `checked.range` also combined two possible intents:

1. Check a range without producing a view.
2. Construct a bounded view after validating a range.

Those should not have an ambiguous shared name. The working recommendation
is `slice(base, start, count)` for view construction, if it warrants an
opcode at all. Validate nonnegative start/count where applicable and the
mathematical conditions:

```text
start <= length
count <= length - start
```

Do not overflow an unchecked `start + count` or truncate a wider source
index before checking. The empty range at the end remains a possible
valid case under the selected family contract.

A slice does not copy elements, allocate an owner, pin backing or extend
its lifetime. Its complete result type/access/index domains must be supplied
by an explicitly selected view contract. An ordinary selected library call
may be sufficient; a dedicated opcode and result carrier are not approved.

### Optional values, aggregates and queries

| Operation | Inputs/result and intent |
|---|---|
| `some` | Construct a present optional from an admitted complete payload, preserving its ownership and dependencies; it does not clone an affine payload. |
| `none` | Construct absence for an explicitly identified complete optional type. No payload is read or fabricated. |
| `is.present` | Inspect an optional discriminator and return `bool`, without reading an absent payload or implicitly consuming the optional. |
| Aggregate construction | Build the selected complete aggregate from already computed field values under its actual validity, construction and transfer rules. |
| `extract.field` | Obtain one field's value from an existing aggregate value; copying or transferring a non-Copy field cannot be inferred from its bits or field number. |
| Registered query | Evaluate an exact admitted query contract with its declared type/effect/layout requirements; do not recognize arbitrary methods by name. |
| Loan/region events | Preserve specific logical access, backing and lifetime transitions; they are not automatically machine instructions or destruction of a referent. |

A present optional with a unit payload is distinct from absence. A presence
test is not itself an unwrap. Accessing an optional payload needs the
selected present-state or checked extraction contract.

Aggregate construction does not bypass authored constructors, required
validation, partial-value restrictions or publication rules. Moving a field
out of an owning aggregate requires its exact structural contract; selecting
a field value does not expose the aggregate's hidden ABI address.

Here "extract" simply means selecting the stored field value, for example
the second component of a computed tuple. It is not pointer arithmetic,
reference formation or a write to the original aggregate. A storage-field
read and a value-field extraction can have different access/effect rules
even if a backend implements both as loads.

The final physical carriers for these state/transfer facts are not selected.
Full-format capacity is not evidence that their codecs or execution are
already implemented.

### Conversions

| Operation | Intent |
|---|---|
| `convert` | The ordinary selected conversion for the complete source/destination pair. Integer narrowing and floating-to-integer checks are not interchangeable with floating-format rounding. |
| `convert.wrap` | An admitted explicit wrapping conversion, not an implicit change to the source operand. |
| `convert.saturate` | An admitted explicit saturating conversion under that pair's rules. |
| `bitcast` | An admitted representation-preserving conversion; size equality alone does not prove validity, authority or backing. |

The source type comes from the value. The destination type is named where
needed. A two-byte in-place form can hold a register and small type index.
A four-byte conversion-policy opcode can hold a sixteen-bit type index and
independent four-bit destination/source registers. A typed record handles
larger or other forms; no global width prefix is required.

Nominal conversion operators remain selected calls. The table does not
adopt conversion pairs absent from the chosen language/representation
contract.

## Calls

### Bound target versus callable value

`call` and `call.indirect` were different target carriers, not different
evaluation or ownership models. Clearer provisional names are:

| Name | Target |
|---|---|
| `call.method` | A selected bound callable/declaration reference, normally through MethodMap. |
| `call.value` | A captured, fully typed callable value in a register or argument home. |

The user selected one logical invocation protocol, with compact windows
and a general record form. Their provisional names do not expose native
instruction selection.

A bound call can still use virtual/interface/witness dispatch described by
its selected target. A callable value can be a function pointer with an
explicit foreign ABI. These are not respectively synonyms for one native
direct call and one native indirect call.

Overload resolution and source argument adaptation have already occurred.
No consumer selects a method from a matching name.

### Compact call forms

Logical inputs are the receiver, if present, then formal parameters in
declaration order. The signature supplies their complete types and modes.

| `call.method` form | Target | Input window | Result |
|---|---|---|---|
| Selected one-byte form, if justified | A fixed selected target, for example MethodMap 0 | A fixed window, for example starting at `R0` | Fixed selected register |
| Two-byte `x,y` | MethodMap `y`, 0..15 | Starting at `Rx` | `Rx` |
| Four-byte inline call | Sixteen-bit MethodMap index | Any register-window base | Any result register |
| Four-byte record call | Sixteen-bit call-site record index | Explicit sources | Explicit result location |

The inline call has one primary opcode followed by methodIndex:16,
destination:4 and argumentBase:4. One-byte call tuples must individually
earn a place among the sixteen tiny slots; no second window is implied.
These are encoding choices, not physical ABI rules.

| `call.value` form | Target | Input window | Result |
|---|---|---|---|
| Selected one-byte form, if justified | A fixed callable register | A fixed window | Fixed selected register |
| Two-byte `x,y` | `Rx` | Starting at `Ry` | `Rx` |
| Four-byte inline value call | Any target register | Any consecutive window | Any result register |
| Four-byte record call | Explicit captured callable | Explicit sources | Explicit result location |

The inline value call uses target:4, result:4 and argumentBase:4 in its
three-byte payload, with remaining bits reserved unless a further control
is selected. Record forms need no artificial register shuffles.

Compact staging windows must fit R0..R13; they cannot stage arbitrary
arguments into the two special slots. General call records can name special
constants only where the selected formal provides a valid type/domain.
This does not limit callable arity: other completed inputs use argument
homes. The logical count includes unit parameters even when the ABI passes
no payload for them.

All sources and the target are captured before result publication. Calls
change their result binding and any consumed inputs according to the
signature; other logical register bindings remain intact. Physical ABI
clobbers and hidden return buffers are backend concerns.

### General call-site record

The selected logical record, whose physical packing belongs to the
consolidated codec review, contains:

| Field | Required meaning |
|---|---|
| Selected target | Exact bound method/dispatch/witness target or captured callable value, including its complete signature/ABI obligations. |
| Input sources | Formal-order register values or completed argument homes, with any prepared initialization-destination entries identified. |
| Result destination | Register or proven-vacant result home; an exact unit result may use a unit-only sink. |
| Destination preparation | Restricted typed list of required dependency-valid retirement/vacancy preparation for reusable hidden observation/output homes. |

An argument home stores a completed argument value/descriptor. It is not a
recipe to reevaluate an argument expression or silently reread a changed
source variable. It can be an ordinary compiler-generated typed local;
this does not introduce an expression evaluation stack.

For `F(A(), B())`, selected calls can produce results directly into `R4`
and `R5`, followed by a call-site record mapping those inputs to F's formals.
More than fourteen simultaneously needed argument values can use argument
homes. Nonconsecutive register inputs need not be shuffled solely to fit
the compact form.

The ordinary signature has one complete result, including `void`. A tuple
result is one aggregate value. Initialization outputs follow their separate
state contracts; they are not arbitrary extra ordinary result registers.

### Invocation phases

| Phase | Obligation |
|---|---|
| Capture | Evaluate callable/receiver once at its source position, before arguments. |
| Stage | Evaluate written arguments in source order, including reference/place formation and actual copy/transfer effects. |
| Check | Perform invocation-only receiver/target checks at their required point. |
| Prepare | Retire previous hidden observation/destination values in a dependency-valid order and establish the required vacant destinations. |
| Invoke | Transfer ownership at the ordinary invocation boundary and enter the selected callable. |
| Publish | On normal completion, publish the result and declared result-correlated output states. |

Named arguments do not change source evaluation order. The record maps
already staged values into formal order.

The preparation phase is required by the
[hidden-destination contract](../specs/language/calls-and-inference.md#hidden-destination-instances-and-retirement).
It cannot simply be moved before receiver checks or argument staging.
The user selected a restricted typed preparation list in the general
call-site record. Each entry identifies its actual destination/place,
required transition and dependency ordering. It contains only the selected
invocation protocol's preparation actions, not arbitrary helper calls or
argument-expression recipes.

Capture and Stage are explicit earlier operations. At the call instruction,
the record's selectors name already completed values/descriptors in their
current register bindings or homes. The instruction performs Check, Prepare,
Invoke and normal Publish in that order; it does not recapture a changed
source variable or reevaluate an argument expression.

Compact forms are eligible only when their target/window/result and known
signature represent the complete protocol without an extra per-site list.
Otherwise use the general record, not an unchecked call plus guessed
preparation helpers.

If later argument evaluation fails, the caller cleans staged owners; moved
sources do not revive. Once hidden old values have been retired, later
failure does not roll them back. Borrowed materializations remain caller-owned
under their supporting scopes.

A by-reference argument is a descriptor, not a pointee snapshot. If permitted
aliasing lets a later argument mutate that pointee, the callee can observe
the mutation. An initialization destination's vacancy/authority and normal
outcome relationship remain explicit. Mode and state are not inferred from
an address or method spelling.

Exceptional completion has no normally published result. Handler entry
state does not automatically promise the old destination register or rollback
of mutated places/captures. Nonreturning calls have no normal result or
continuation; their terminal encoding is distinct from a normal unit result.

Caller-unsafety, required dependencies and foreign data/ABI admissibility
survive every call form. No form grants marshalling, pinning, rooting or
lifetime extension. Preserving a logical register across a call does not
promise a machine register survives it.

## Return and abnormal completion

### One return operation

The recommendation is one `ret`, not an extra `return.unit`:

| Method result | Proposed meaning |
|---|---|
| Non-unit `T` | Return an initialized complete `T` from the selected value operand. |
| Exact `void` | Return its known unit value without requiring a payload register. |
| No normal completion | A normal `ret` is not permitted by that callable's contract. |

For exact unit return, unused register/operand bits must have a defined
canonical value, rather than reading whichever unrelated value occupies R0.
The ordinary method signature is already available to establish this case.

This does not erase required effects or make an uninitialized unit-local
read valid. Other zero-data nominal types still require their own initialized
value and are not implicitly unit.

### Explicit failfast and unreachable

Use `failfast`, not a generic `fail` or `trap` mnemonic. It terminates normal
execution under the selected fail-fast target/runtime policy, without the
ordinary language unwind/cleanup promise. A diagnostic/reason operand can
be represented by its admitted record; its exact codec is not selected.
It is not a false value, a checked numeric exception or an IEEE invalid event.

`unreachable` is an explicit abnormal terminal instruction with no normal
continuation. The user selected an ordinary language failure if it executes:
prepare a fresh event at the instruction's origin, dispatch handlers and
perform cleanup under the normal event/unwinding rules. It is not failfast
or undefined behavior.

This is not a NoThrow operation merely because of its mnemonic. An optimizer
cannot use the instruction itself as proof that its incoming paths never
execute and erase their defined failure. Ordinary reachability/effect facts
still govern optimization and declared NoThrow guarantees.

The user selected a new exact Core `Terrum.UnreachableException`, following the
[standard runtime payload shape](../specs/runtime/standard-failures.md#1-exact-core-identities-and-construction):
a public nongeneric zero-data Exception/Copy struct with a public
zero-parameter constructor and no authored construction checks/effects.
The event carries origin and diagnostic context, not fields invented on that
payload. The fixed mapping does not inherit a callable's `RequireException`
default and does not select a similarly spelled provider type.

Actual event-preparation/transport failure retains its own admitted outcome;
do not fabricate an UnreachableException event if its preparation failed.
The selected type belongs in the Core/runtime failure inventory during a
separately authorized integration; no canonical Core addition is made here.

Typed checked operations retain their own failures and source origins.
They do not need normal CFG edges after every check and cannot silently
be replaced with `failfast`.

### One throw family, explicit payload kinds

Rethrowing need not consume another primary opcode. One `throw` family can
select the propagation route from a payload kind, the normalized operand
type or an explicit typed record:

| Payload kind | Meaning |
|---|---|
| Fresh Exception value | Stage a complete eligible value and establish a fresh event/origin at this throw site. |
| Active lexical handler event | Continue that exact event while the selected handler activation still owns it. |
| Captured event owner | Consume exact Core CapturedException and transfer its existing event, origin, causes and dependencies. |

Source bare rethrow can use the active-handler payload. Source
`throw move saved` with exact CapturedException can use the captured-owner
payload. The existing [captured-exception contract](../specs/language/captured-exceptions.md)
supplies those eligibility and ownership rules; this is not a new public
handler-token API or thread-global current-exception lookup.

An ordinary copied payload is not the old event. Throwing a payload value
does not preserve provenance merely because its bytes or identity match
something caught earlier. Conversely, captured propagation does not invoke
a constructor, clone the payload, replay cleanup or manufacture a new
top-level origin. The distinction must remain explicit even with one opcode.

All routes retain their actual cleanup, causal replacement and delivery
contracts. Operand construction/evaluation failure propagates its real
failure without fabricating the requested throw. Consumed event owners and
handler responsibility cannot be revived through another register alias.

### Runtime require instruction

Represent `require<E> condition` explicitly instead of requiring an encoded
conditional block and constructor/throw sequence at every site.
The operand is a Boolean or an admitted comparison-condition test; a
failure-selector record identifies the complete E and its selected ordinary
zero-supplied-argument construction plan.

| Outcome | Behavior |
|---|---|
| Condition succeeds | Continue with the ordinary unit/output result; do not construct E or create a failure event. |
| Condition evaluation fails | Propagate that actual failure; do not construct E. |
| Condition is false | Execute the selected ordinary construction once, then propagate the complete result with this requirement's fresh event/origin. |
| Construction fails | Preserve the actual constructor/initialization/cleanup failure and cause rules, not a fabricated E result. |

The existing [requirement-failure owner](../specs/language/requirement-failures.md)
fixes selection: explicit E, otherwise the semantic owning callable's
resolved default, otherwise exact Core RequirementException. Resolve this
before serialization; an inlined or called helper does not acquire its
caller's default.

Zero supplied arguments does not mean exactly zero formal parameters.
Ordinary constructor lookup, accessibility, optional defaults or an admitted
empty params expansion apply. The failure selector retains that binding.
It is not a failure factory, invented diagnostic argument or guessed
default value. Generic Exception evidence alone does not prove constructor
availability.

A proposed four-byte Boolean form can hold failureSelector:16,
conditionRegister:4 and reserved/control:4. A condition-test form can hold
failureSelector:16, conditionRegister:4 and predicate:4. Exact primary values
and selector-record layout remain open.

Value-producing requirements over omitted initialization outputs must also
retain the selected availability checks before owning extraction. They may
use a richer typed requirement record or already lowered checks, but cannot
read a vacant payload while testing success. Completed outputs, mutations
and staged owners are not rolled back on a false requirement.

Runtime `require` is not debug-only `assert`, an optimizer assumption or
`static require`. It may be removed only under a valid semantics-preserving
proof, retaining effects that produced its condition. Runtime postconditions
keep their separate selected failure contract.

## Control-flow and large-operand sketches

| Operation | Candidate short/full choices |
|---|---|
| Unconditional branch | Small absolute block index in a short form; proposed full sixteen-bit block index or typed flow record otherwise. |
| Boolean branch | Boolean register plus a flow-record index; the record holds the absolute true/false block indices. |
| Condition-test branch `jcc` | Four-bit predicate, condition register and sixteen-bit edge-record index can fit four bytes. More general predicates/conditions use the appropriate typed record. |
| Switch | Typed case/default record whose successors are block indices, not an inline address list. |
| Region exit | Region/cleanup transfer record with a target block index and its obligations. |

Every normal control-transfer target is a block index, not a machine
address, bytecode address or byte displacement. Region identifiers name
regions, not code addresses. Block/terminator offset indexes locate the
chosen block's bytes for decoding; those offsets are not branch operands.
Native lowering can later choose native addresses/displacements.

The default general conditional record explicitly holds both successors.
Any compact implied successor must still denote a specified block index,
not an assumed byte-level fallthrough. No such compact variant is selected
merely because it fits. Transfers must satisfy the target's entry types,
state, regions and lifetime obligations without reinterpreting register bits.

Under the proposed method-local limits, a record-reference operation uses
a sixteen-bit index and its remaining operation-specific fields:

```text
record-operation opcode:8 | recordIndex:16 | operationFields:8
```

The record can hold full-width global references and admitted operand lists.
The current limit proposal needs no twenty-/twenty-four-bit local index
or wide-index instruction pair. If larger inventories become necessary,
the explicit header codec reservation can select a reviewed wider encoding.
It must not be activated implicitly by an out-of-range operand.

Unknown operations, unsupported required contracts and invalid
operation/control/record combinations reject at their applicable
access/admission boundary. The header codec reservation is not a substitute
for these checks or exact schema selection. Equal version bytes do not
authorize guessing between this draft and another schema.

## Worked expression and realistic workloads

For five by-value `int32` inputs under explicit wrapping/release policy, a sequence for
`(a * b * c) + (d * e)` is:

| Operation | Bytes with ordinary short forms | Bytes if its indicated tiny form is selected |
|---|---:|---:|
| Load `a0:a1` into `R0:R1` | 2 | 1 |
| `R0 = mul.wrap(R0, R1)` | 2 | 1 |
| Load `a2` into `R1` | 2 | 2 |
| `R0 = mul.wrap(R0, R1)` | 2 | 1 |
| Load `a3:a4` into `R1:R2` | 2 | 2 |
| `R1 = mul.wrap(R1, R2)` | 2 | 1 |
| `R0 = add.wrap(R0, R1)` | 2 | 1 |
| Return `R0` | 2 | 1 |
| Total | 16 | 10 |

Both columns have eight operations and three working registers, without a
per-intermediate local or encoded SSA ID. The ten-byte case requires five
particular tiny tuples: the first argument pair, the two multiply tuples,
the add tuple and the return. It is conditional on those members earning
slots; the measured top-sixteen shortlist does not itself guarantee them.

No numeric operation bytes are assigned by this example. Counts exclude
the code-body header/codec selector, maps, indexes and padding. These are
field-budget examples, not compiler emission or runtime performance results.

The corresponding checked/development sequence uses four bytes for each
of its three multiplies and one add. With the ordinary two-byte loads and
return, that is 24 bytes for the same eight operations. Selecting tiny forms
for the first argument pair and return reduces it to 22 bytes; checked
arithmetic itself remains four bytes. Compound full-form recipes can have
different operation counts, so this is not a claim of an optimal encoding.

The workload comparison must include more than that expression:

| Workload | Relevant pressure |
|---|---|
| `List<T>.Add`, indexing and insertion | Retained array/size snapshots, field accesses, ordered checks/stores, cold calls and several call inputs. |
| `Enumerable.Select` and iterator advancement | Captured callable/receiver, generic values, state-machine branches, field-stored enumerators and explicit by-reference access to struct state. |
| `Int32` operations and DivRem | Tiny operations, noncommutative ordering, multiple uses of a quotient, aggregate/multi-result alternatives. |
| `Single` | Ordered/unordered predicates, NaNs, sign operations and type-changing comparison results. |
| .NET 11 `System.Numerics.Decimal32` | Actual IEEE decimal32 arithmetic/helper usage, not a substitution of CLR `System.Decimal`. |
| Larger software arithmetic | Buffers, addressable temporaries, repeated local updates, long streams with small transient working sets. |
| Reference/owner/generic code | Root/backing retention, actual storage identities, cleanup, calls and non-word-sized values. |

A stack remains a legitimate density alternative and can import into ordered
SSA. The register recommendation must be judged by total artifact size,
decode/import work and representative usage, not by claiming that the
stack inherently forces deferred effectful expression trees.

## Consolidated final-review worksheet

The user approved this consolidated worksheet for the design handoff.
The semantic decisions recorded above remain settled. The bands and tuples
below are a concrete allocation worksheet, not final opcode registrations.
This approval does not authorize canonical integration, approve Batches 4/5,
or claim an implemented encoder.

### Header and implementation envelope

| Item | Consolidated recommendation |
|---|---|
| Method implementation kind | An explicit byte-sized tag selecting stream, field read, field write, admitted field reference, or identity forwarder. Numeric tags remain unallocated. |
| Stream codec | A separate `instructionEncoding:u8` in the stream header; initially one mixed register codec. Budget the full byte rather than assume free header bits. |
| Compact implementation | Carries its exact bound field/method reference and required binding/use contract, without a stream codec or fabricated empty code stream. |
| Local inventories | Unsigned u32 counts, range 0..65,536 for each u16-indexed namespace. |
| Global references | Retain the selected module/reference width; compact descriptors do not truncate FieldDef/MethodDef identities to u16. |
| Code and source extents | Retain their separately selected Body/SourceMap widths, not the u16 local-index limit. |
| Unknown kind/codec/profile | Reject or decline affected use; no guessed decoding, legacy alias or empty-body fallback. |
| Arithmetic/IEEE policy | Explicit in operations and semantic requirements, never inferred from the codec selector or the consumer's build configuration. |

The stream's first-byte partition remains 16 tiny, 176 short and 64 long
primary positions. Compact method bodies are another implementation kind,
not another instruction-width mode.

### Sixteen tiny-tuple candidates

This is a complete candidate shortlist so the budget can be reviewed.
The ordinal is a worksheet position, not an allocated opcode. It is not
claimed to be the optimal post-accessor/forwarder ranking.

| Position | Implied operation and operands |
|---|---|
| T0 | Signature-aware `ret R0`; exact void uses its known unit result |
| T1 | `R0 = load.arg 0` |
| T2 | `R1 = load.arg 1` |
| T3 | `R2 = load.arg 2` |
| T4 | `R0:R1 = load.arg.pair 0:1` |
| T5 | `R0 = load.local 0` |
| T6 | `R1 = load.local 1` |
| T7 | `store.local 0, R0` |
| T8 | `store.local 1, R1` |
| T9 | `R0 = load.constant 0` |
| T10 | `R0 = add.wrap(R0, R1)` |
| T11 | `R0 = sub.wrap(R0, R1)` |
| T12 | `R0 = mul.wrap(R0, R1)` |
| T13 | `R1 = mul.wrap(R1, R2)` |
| T14 | `R0 = and(R0, R1)` |
| T15 | `R0 = xor(R0, R1)` |

Loads/stores retain their full eligibility, state and source-use contracts.
Constant 0 means typed ConstantMap entry 0, not an untyped numeric zero.
Checked and saturating arithmetic are absent from this shortlist.
No call/field tiny tuple is assumed necessary after compact method bodies.

The measured 80.5% figure belongs to the earlier study, not this new set.
Final member selection and numbering must consider the changed stream
population and total metadata costs; it must not silently turn this
candidate ranking into a measured optimum.

### Grouped short-code worksheet

Each band has sixteen primary positions. A proposed member uses only a
payload schema already described in the two-byte section; unused positions
are invalid, not implicit additional operations.

| Provisional band | Related members/forms | Payload rule |
|---|---|---|
| `10..1F` | `mov`; small argument/local/constant access; argument pairs; return; reference reads/stores/takes | Two nibbles, with index/register roles fixed by the member |
| `20..2F` | Wrapping integer neg/add/sub/mul; strict floating neg/add/sub/mul; ordinary div/rem | Unary destination/source or binary RMW; type selects its admitted domain, not an ambient policy |
| `30..3F` | Integer/Boolean and/or/xor/not; integer shifts | Unary or binary RMW; count type and range retain the scalar contract |
| `40..4F` | Numeric `cmp`; in-place Boolean `setcc` | Two sources with RMW result, or uint8 register plus predicate nibble |
| `50..5F` | Admitted ordinary in-place conversions | Register plus small destination TypeMap index; no checked/saturating short variant |
| `60..6F` | Local-place selection; place load/store/take/retirement; explicit read/write/init reference formation | Place/value register pair or register plus small local index; the member fixes its access role |
| `70..7F` | Common computed-aggregate field extractions | Destination/source pair with an explicitly implied field selector and admitted read/use contract |
| `80..8F` | Small bound/value calls; small general call-record index | Existing compact call windows, or an entire u8 record index |
| `90..9F` | Known typed operand-record forms | Entire u8 record index; only selected record kinds/operations are executable |
| `A0..AF` | Short `jcc` predicate family | Low primary nibble selects the approved predicate; operand byte is condition register plus small edge-record index |
| `B0..BF` | Small absolute branch; Boolean edge; selected switch/exit/throw/require/failfast records; unreachable | u8 index, two-nibble restricted form, or fixed zero payload for an operand-free member |

This groups related semantics without dedicating a one-byte island to every
operation or pretending that every family has all three widths. A compact
operation can have inherent checks, such as ordinary division or a place
access; the full-only rule concerns the selected checked/saturating policy
variants, not every operation that can fail.

### Grouped long-family worksheet

These are proposed family roles within the sixty-four-position budget.
They do not assign every position or allocate function-selector values.

| Provisional band | Family roles | Registered payload |
|---|---|---|
| `C0..C6` | Scalar arithmetic/multi-result functions; bit operations; raw cmp; setcc; Boolean selection; cc selection/movement; ternary logic | Function/control byte plus four operand nibbles; each function fixes arity/result roles |
| `C7..CA` | Integer wrapping/checked/saturating recipes and strict floating recipes | The approved recipe byte plus destination/A/B/C nibbles |
| `CB..CC` | Scalar `cmp3.and` and `cmp3.or` | Approved low A/B and high B/C predicate nibbles plus destination/A/B/C |
| `CD..CF` | Capacity for separately selected higher-math/classification profiles | No executable member follows from this reserved capacity |
| `D0..D7` | Conversion policies, constants, full local/argument indexes and admitted typed views | Type/map/slot index u16 plus registered register/control fields; extra obligations use typed records |
| `D8..DF` | Bound field/place/value/reference forms | Field reference u16 plus two operand nibbles |
| `E0..EF` | Inline bound/value calls, general call/require/control/throw/failfast/operand records and home returns | The call/control layouts above, or u16 record/home index with fixed remaining fields |
| `F0..FF` | Capacity for separately selected native-memory/atomic/vector profiles | Unassigned members reject; no hidden variable-width escape |

For example, full product and DivRem are arithmetic function members whose
four nibbles mean two results and two inputs. They are not ternary
one-result operations. General source loads, stores, reference formation
and native boundaries retain their actual contracts regardless of band.

There are no eight-byte instructions or recursively decoded width prefixes
in this codec. Larger operand lists live in indexed records.

### Common record fields

The following packing is the consolidated candidate for side records.
It is not additional inline instruction payload.

| Field | Proposed shape and meaning |
|---|---|
| Value/location selector | `kind:u8, index:u16`; symbolic kinds Register, Home and None |
| Register selector | Index 0..15, upper index bits zero; source/destination/unused roles apply the selected special-slot rules |
| Home selector | Full u16 typed local/home index, including 0xFFFF |
| None selector | Explicit None kind with index zero; it does not steal a Home index or create a unit value |
| Typed record table | u32 inventory count; u16 entry references; exact kind-specific schemas with bounded record extents |
| Variable list | u32 count followed by its selected fixed-width entries; no variable-length instruction |
| Optional record reference | Explicit presence/kind information, not a reserved u16 value |

Selectors capture the current completed binding when their semantic
operation executes. They are not source-variable reevaluation recipes or
references to whichever value later occupies a reused register.
Record kind identifiers and the outer Body directory/byte ordering remain
the selected schema's integration work, not guessed compatibility.

| Record | Required logical fields |
|---|---|
| Call site | Bound-target or captured-value kind; MethodMap reference or value selector; formal-order input selectors; result selector; restricted preparation references |
| Destination preparation | Selected place/home, required transition, source-use/state/dependency references and the protocol's dependency-valid order |
| Place selection | Local/field/element kind; captured base/index where applicable; selected member/slot reference; target type, access/state and backing dependencies |
| Semantic store/retirement | Target place selector, source selector when present, actual use mode and required transition/state/dependency facts |
| Conditional edge | Absolute true/false block u16 identities and required edge-state relationships |
| Switch | Admitted key/case contract, typed keys/default, absolute target blocks and required edge-state relationships |
| Requirement failure selector | Complete payload type, selected constructor and ordinary zero-supplied-argument binding, including admitted defaults/params handling |
| Throw | Fresh/active-handler/captured-owner kind, corresponding value or lexical handler source, actual event/ownership contract |
| Region exit | Selected exit kind, target block where applicable, and required region/cleanup/state transition |
| Source/failure map | Instruction location and, for recipes, semantic step role: A-negation, B-negation, inner, C-negation or outer |

A kind-specific schema can omit fields that its kind makes implicit.
It cannot omit an obligation. Generic record execution accepts only known
operations with selected contracts; it is not a provider-defined helper
language or permission to execute an unreviewed family.

Recipe step roles identify failure/source sites without serializing a
growing per-expression value namespace. Consumers reconstruct the ordered
nodes and retain the actual active steps; absent tokens have no step.

### Compact body descriptors

The implementation-kind tag selects the descriptor rather than a code
stream. A descriptor carries its exact bound field or method reference
using the selected global/binding reference format. The declaration and
binding must completely determine receiver/input/result modes, authority,
entry/exit contracts and invocation requirements.

An identity forwarder stages the wrapper's existing bindings under the
ordinary call protocol; it does not reevaluate its caller's expressions.
If additional mapping, preparation, adaptation or body operations cannot
be represented by that exact descriptor, use a stream. A reference getter
requires its real declared reference/dependency contract; a value getter
does not become one.

### Deferred versus integration work

SIMD full products, future opt-in IEEE handling/helpers, a dedicated slice
operation, mixed-input full products and unselected higher-math/atomic/
vector members remain deferred. Reserved space does not approve them.
Their absence does not permit a quiet fallback or invented marshalling.

Final numeric primary/function/kind assignments, the post-compact-body tiny
ranking, complete outer Body record ordering and the corresponding registry/
SourceMap edits are integration work. They must preserve this decision
record and exact-schema agreement. A change needing another semantic
decision comes back to the user rather than being decided by the integrator.

## Recorded decisions and integration boundaries

| Decision | Status |
|---|---|
| Fallible recipes | Direction clarified: encode all four operations even when overflow is possible; reconstruct their ordered AST/IR and optimize on import. |
| Recipe controls | User-selected: inner bits 0..1, outer 2..3, order 4, source modifiers 5..7; neg/add/sub/mul=0/1/2/3. Integer absence is modified R14, floating absence modified R15; tokens are not evaluated. |
| Integer profile policy | User-approved: development/debug checks, release wrapping, explicit IL policy; checked/saturating variants have no dedicated compact forms. This is settled, with canonical integration separate. |
| Special operand slots | User-selected: fourteen general registers, R14 contextual zero, R15 all bits set including floating/vector forms, with valid Boolean true; discard/unused is role-specific. Decimal BID uses its raw negative signaling NaN, not a maximum canonical payload. |
| Method-local limits | User-selected: sixteen-bit indices, 65,536-entry bounds, unsigned thirty-two-bit counts and independently wider global references. No implicit widening; a wider local-index codec is future work. |
| Comparison results/controls | User-selected: binary cmp produces one-hot uint8 L=1/E=2/G=4/U=8; cc consumers accept any uint8 mask and test nonzero intersection. Scalar cmp3 AND/OR produces bool with low A/B, high B/C predicates. |
| Register movement | User-selected value-alias mov; no implicit source copy, transfer, cleanup or new owner. All aliases observe the same consumption state; pending responsibilities cannot be lost by rebinding. |
| Storage transitions | User-selected typed semantic store for initialization/replacement, with explicit state, authority and source-use contracts; preserve checks, retirement, commitment and reentrancy confinement through lowering. |
| Place carrier | User-selected internal typed place handle, not an implicit checked reference or address. Preserve captured target, checks, backing and authority; reference formation is explicit. |
| Calls | User-selected compact windows for representable sites and general records otherwise: captured bound/value target, formal-order completed register/home inputs, result destination and restricted typed preparation. Preserve the complete invocation/abnormal-state protocol. |
| Compact bodies | User-selected exact field-accessor and identity-forwarder descriptors without streams; retain method identity/contracts and reject unsupported required body kinds. Extra adaptations/operations use normal streams. |
| Full-product multiply | User-selected scalar form: low/high only, unsigned low and high retaining the equal input type, for all twelve primitive integer identities. Wider values are constructed. SIMD is explicitly deferred; mixed-input extensions are not selected. |
| Unreachable execution | User-selected: a fresh ordinary failure with exact Core Terrum.UnreachableException, normal handler dispatch and cleanup, not failfast or undefined behavior. The new zero-data payload follows the standard Core construction shape. |
| Consolidated physical worksheet | User-approved for handoff: dedicated codec byte, complete candidate tiny shortlist, grouped short/long bands and typed selector/record shapes. Numeric registrations and final measured member selection remain integration work. |
| Deferred functionality | Keep future opt-in IEEE handling and other unselected operations explicitly outside current executable support, without quiet fallbacks. |
| Final approval | User-approved design handoff only. Integration, final allocations and post-compact-body tiny ranking remain separate; no Batch 4/5 integration or implementation support is approved here. |

The design handoff is approved; integration remains a separate action.
The recorded decisions and deferred contracts define that boundary.
Recommendations, empty slots and implementation prototypes do not approve
anything beyond it. Canonical integration is not performed by this review.

## Review and acceptance cases

Before selecting a complete schema:

- Exercise the provisional first-byte size boundaries at `0F/10` and
  `BF/C0`, every selected tiny tuple and all short-form register selectors.
  Operand bytes that resemble opcodes must not be decoded recursively.
- Verify exactly sixteen primary values are budgeted for one-byte forms,
  with no implicit second-window allocation for every operation.
- Reject unknown header codec selectors, unassigned primary values and
  unsupported family controls. Do not infer another codec from the stream.
- Check explicit source/destination overlap and multi-result publication;
  no source reads a newly rebound destination accidentally.
- Verify Boolean `not` produces a valid Boolean without treating it as a
  raw complement of all storage bits.
- Cover every floating comparison outcome, both NaN placements, the
  proposed alternate unordered outcomes, signed zeros and decimal cohorts.
  Exercise all sixteen predicate masks and all 256 cmp3 predicate bytes,
  including low/high-nibble order and the two family combining rules.
- Cover quiet/signaling policies with both qNaN and sNaN, and distinguish
  invalid status from trap/language-failure handling. Classification must
  not acquire unwanted invalid effects through a self-compare lowering.
- Confirm the default profile produces no language-visible IEEE signals.
  Future opt-in software handling must not silently become enabled by
  a predicate or codec bit.
- Verify one compare condition can feed `setcc`, `jcc` and conditional
  movement without repeating the comparison or its signaling event.
  Check one-hot uint8 cmp outputs and arbitrary consumer masks, including
  zero, high-bits-only, multi-bit and all-one values. Predicate F is true
  for every cmp output, not for every possible uint8 value.
- Check three-input comparison chains with eager and short-circuit behavior;
  an unexecuted second comparison must not signal or evaluate its producer.
  Scalar predicate chains produce bool usable by ordinary branch/selection,
  not a raw relation pair or mandatory additional setcc.
- Exercise all 256 ternary-logic controls, operand permutations, constant
  controls, bit-width preservation, majority/minority and bit-mask versus
  whole-value selection.
- For arithmetic recipes, preserve association, ordered intermediate checks,
  source/failure sites and strict/fused rounding. Do not move an inner
  operation past an effectful producer of the third input.
  Check the 182 unary-canonical controls, both domain-specific absent tokens,
  rejection of absent tokens in required positions, and no dummy evaluation.
  Preserve real integer all-one and floating negative-zero source forms.
- Check full multiplication's exact low/high reconstruction and result types
  for signed/unsigned/native widths, zero, signed minima, both-negative inputs
  and unsigned maxima. Cover 128-bit inputs without a wider primitive, one
  or both discarded halves, and overlapping input/output selectors with
  distinct published result bindings.
- Check development/debug checked arithmetic and release wrapping, including
  relevant constant evaluation. Explicit policies remain fixed, including
  when a consumer imports an artifact under a different build profile.
  Checked and saturating forms must use full four-byte representations.
- Compare stream bodies with field/forwarding implementation forms before
  choosing the one-byte set. Preserve method identity, contracts and access/
  initialization obligations in the compact forms.
- Test same-type joins, incompatible live-in joins, dead incoming slots,
  transiting values and loop backedges.
- Preserve value identity through register aliases; reject double transfer
  or retirement through aliases of one affine responsibility and loss of its
  last available name without another valid holder or explicit retirement.
- Distinguish vacant, live, maybe-live, partial and pending-reservation
  storage; forbid replacement through initialization-only authority.
- Preserve complete-receiver confinement during replacement; no callback
  observes a deliberately exposed invalid interval.
- Preserve element target/check timing around calls and rebinding; do not
  create a reference through a value-returning accessor. Check internal
  place aliases/state changes without duplicated authority, implicit pinning
  or treating their representation as a public native address.
- Define range index domains, overflow-safe checks, empty ranges and exact
  view/backing results before selecting a slice operation.
- Cover direct/value/dispatch calls, nonconsecutive inputs, the 14/15-input
  register-window boundary and larger signatures, unit parameters/results,
  aggregates, staged-owner failure, hidden destination preparation and
  abnormal output/capture state.
- Cover typed ConstantMap duplicates, distinct zero/NaN/decimal representations,
  the selected 65,536-entry boundary and independent wider global references.
  Index `0xFFFF` is valid; absence must not steal it. Reject 65,537-entry
  inventories without treating count zero as a wrapped/full inventory.
- Validate special constant domains and type inference, disallowed writes,
  legitimate discard and unused-field roles without creating invalid
  references, implicit owners or abandoned cleanup responsibilities.
- Check R14's valid Boolean false, positive floating zero, decimal zero
  exponent 0 and per-lane vector zeros. Reject missing type witnesses and
  implicit reference/default-aggregate construction; preserve negative zero
  and alternate decimal cohorts in ConstantMap.
- Check R15's all-one patterns at every admitted width, valid Boolean true,
  binary negative quiet NaNs, decimal BID negative signaling NaNs and
  corresponding vector lanes. Preserve every raw decimal bit despite its
  decoded numeric payload zero; do not quiet/canonicalize the constant read
  or substitute a numeric maximum.
- Check require's true path, false-path construction, zero-supplied-argument
  binding, constructor failure, omitted outputs and source-owner defaults.
- Check all throw payload routes and preservation of event identity/causes,
  with no double propagation through aliases or after handler capture.
- Check executed unreachable's ordinary event origin, handler dispatch and
  required cleanup and exact Core UnreachableException identity; do not lower
  it to failfast or undefined behavior, inherit a RequireException default or
  fabricate the event after preparation failure. Preserve the independent
  terminal behavior of explicit failfast.
- Keep native callable typing, caller-unsafety and target ABI checks
  independent of register packing.
- Bound method-local record/map/block indices; reject overflow rather than
  treating an index as a code address or selecting a wider codec implicitly.
- Rebuild code indexes, block/terminator locations, operand references and
  SourceMap references on re-encoding.

This is a design handoff, not an allocated or implementation-qualified TRRM
schema. Final sign-off applies to the stated core design and explicitly
deferred scope; it does not perform registry allocation, outer Body/record
integration, compiler migration or native qualification. Those steps need
their own authorization and must not invent another semantic decision.
Existing tests, SDK examples and prototype bytes do not supply that approval.
