# Construction handlers and registrations

This chapter owns target-directed builder registration, type-recipe binding
and eager construction evaluation. Its logical rule sets are
`builder-type-recipes/1`, `text-target-parameters/1` and
`eager-construction/1`, with `builder-acquisition/1` and `builder-completion/1`
for the nominal source-builder lifecycle.
`construction-operations/1` supplies the standard text and collection drain
roles below.
[Complete types](types-and-normalization.md) supply type identity and forward
substitution; [calls and inference](calls-and-inference.md) select the target
before complete construction checking. A recipe is not an open-generic type
value, a constructor invocation or a search for similarly named methods.

The recipe identifies the builder contract to check. It does not by itself
admit that contract's operation catalog, construction/refusal protocol,
capture representation or persistent ABI. Those requirements must also be
available before the affected construction is used.

## Target-owned builder schemas

Core's resolved `TextBuilder` and `CollectionBuilder` schemas each take
exactly one type-reference argument, written directly as `typeof(Type)`.
Each is nonrepeatable on one effective target type and authors that target's
primary nominal declaration contract. Inline type placement and an exact
`type` target denote the same effective placement. A secondary partial part
cannot introduce, replace or duplicate that primary contract.

```terrum
[CollectionBuilder(typeof(ListBuilder<T>))]
public mut class List<T> { ... }
```

This is a registration illustration, not the complete declaration or
operation protocol of either type. For `List<int32>`, the recipe supplies
`ListBuilder<int32>` by ordinary substitution. A non-generic target can
likewise name a non-generic builder. Both builder kinds may be present on
one target only when their independently checked contracts are supported;
one does not imply the other.

The target-defining module owns the record. Primitive targets retain their
reserved language/Core authority. The owner may explicitly name an external
builder, preserving its actual definition, access and load dependencies.
An unrelated module, extension, alias or imported package cannot add or
override a target's registration. An owned nominal adapter has its own
target identity and is the extension route.

The key is the exact target definition/pattern and construction kind.
Repeated imports of one record identity deduplicate. Distinct claims for
that key reject even when they name equal builder types or the same
operation implementations. Inaccessible claims do not disappear to make
a competing record win. No import-order, priority, conditional-record or
arity preference is supplied.

Callable, parameter, field, result and arbitrary attribute-object placements
are not substitutes for this type contract. Same-spelled user attributes
have no compiler-recognized meaning. The call-side `TextTarget` parameter
schema below, a literal parser, formatter or generic constraint does not
author a builder registration merely by naming this target or builder.

## Type-reference arguments are recipes

For these two resolved schemas, the direct `typeof(Type)` argument binds a
type tree. It is not an ordinary emitted constant or a runtime reflection
object. It needs no type-object allocation, generic-constructor value,
runtime static initialization or execution of the builder's constructors.
Ordinary surrounding parentheses preserve the query's type-reference
category; they do not permit a computed type-object expression.
A general value expression, user `@typeof` call, string name, method group
or computed type-object expression cannot substitute for that tree.

Bind the tree in the target's declaration environment, including that
target's own type and constant formals, even though its attribute prefix
precedes the written formal list. Formal identities are the exact owning
declaration and ordinal, not parameter spelling. Ordinary lexical type
lookup, accessibility, arity selection and transparent expansion apply.
The attribute does not capture a caller's formals, a method-local binder,
a use-site import or a same-named type from another module.

A recipe may retain, reorder, repeat or omit the target's formals, use
ordinary nested constructed types, or name a fixed builder. It need not
have the target's generic arity. Every written argument still matches
its selected declaration's type/constant formal kind.
Constant arguments use already admitted constant-formal domains and the
ordinary checked expression/default rules; this schema admits no new
constant domain. Defaults and associated projections keep their existing
declaration/witness dependencies.

An unresolved ordinary `_` inference hole is not a recipe parameter.
`typeof(ListBuilder<>)` is not admitted: an unapplied generic constructor
is not a `Type` in this grammar. No explicit argument mapping, higher-kinded
formal or inferred arity expansion is added. Use a complete symbolic
application such as `typeof(ListBuilder<T>)`.
Nominal definitions remain anchors; transparent alias/associated expansions
must terminate under the ordinary type rules.

### Definition checking and substitution

Check the recipe under the target's declared constraints for every admitted
target instantiation. It must denote a well-formed builder type and retain
the declared construction obligations. A stronger builder constraint must
follow from the target contract; one convenient concrete use or current
implementation cannot repair an invalid generic registration.
An unconstrained formal is not a builder capability, and a recipe cannot
publish private hidden requirements on otherwise valid callers.

At a use, first establish the complete target by the ordinary source rules.
Substitute its complete type/constant arguments forward into the already
bound recipe, normalize, and check formation and the retained construction
contract. Preserve nominal identities, qualifications, constant-domain
identity, representation and selected witness dependencies. Do not infer
the target backwards from a desired builder or silently attenuate a type
argument to satisfy the builder's constraints.

The selected recipe is unique before body execution. Wrong arity/kind,
unresolved obligations, inaccessible required operations, missing protocol
support or an invalid result diagnose without trying another builder,
constructor, conversion, literal parser or imported registration.
No type spelling or currently available private body supplies missing
operation identities or authority.

## TextTarget parameter schema

Core's resolved `TextTarget` is an optional, no-argument, nonrepeatable
parameter attribute under `text-target-parameters/1`. It authors the primary
parameter contract of an ordinary named callable, constructor or accessor,
including a corresponding bodyless requirement. It is not a callable-wide
attribute selecting a parameter by a string argument.

```terrum
public static void Write<TContent>([TextTarget] TContent content)
    where TContent : IFormattable;
```

Inline placement and `[param(content): TextTarget]` on that declaration bind
the same exact formal ordinal. Effective duplicates reject, including
duplicates across the two spellings. A selector must identify one existing
eligible formal. Secondary partial declarations cannot add, replace or
duplicate the primary contract. Only the declaring module authors it;
imports, aliases and unrelated extensions cannot attach it to an existing
parameter.
An argument-bearing `TextTarget(nameof(content))`, result/type/field/
receiver target, or untargeted callable placement rejects. Same-spelled user
attributes have no recognized meaning. No new lambda/callback schema or
implicit setter-value target is supplied.

The eligible input shape is by-value `TContent` or parameter-only
`implicit ref<TContent>`, where `TContent` is a declared type formal with the
checked `IFormattable` requirement. Ordinary parameter mutability remains
independent. `params`, explicit reference, writable-reference and
initialization-output positions are not content-construction targets.
A concrete text parameter can instead use its existing target-owned
construction contract; TextTarget is not a substitute for that registration.

### Typed eager content

For an interpolation bound to this parameter, form a generated typed
formatter whose ordered captures hold the already evaluated source values,
alignment and runtime format inputs, with a checked `IFormattable` witness.
The [standard text operations](#standard-text-operations) fix alignment to
`int32` and format data to validated `TextSpan`. Omitted alignment/format
supplies zero/empty instance data without executing another source expression.
Its fields and fragment operations
retain their actual complete types, modes, selected formatting witnesses,
text validity and backing relationships. It is not a runtime syntax tree,
type-object value, universal boxed fragment or deferred content program.

The generated nominal type's logical identity is its originating module and
normalized complete content shape. The shape consists of the ordered complete
capture types and modes and the ordered fragment-operation graph, including
exact selected formatting witnesses and required writer/member identities.
Within one module, equal closed shapes denote the same generated complete
type, including at different source occurrences. Ordinary complete-type
normalization applies; equal field layout or formatted output is not a shape
comparison.

Captured values, literal segment contents, alignment and format values are
instance data, not additional type arguments. Source coordinates, parameter
ordinals, the consuming callable and the eventual writer instance do not
distinguish otherwise equal shapes. A change that selects a different typed
capture, mode, operation graph or witness does distinguish the shape.
For example, `$"a={int32(1)}"` and `$"b={int32(2)}"` in the same module can
provide the same content type to two annotated parameters sharing one
`TContent` formal, when their complete shapes match.

Sharing a type does not share a formatter value, its captured data or backing
storage. Every construction evaluation still performs its own eager captures,
transfers and cleanup; each source site's materialization keeps its ordinary
supporting scope. Equal shapes in different modules remain distinct nominal
types. Linking/remapping preserves the originating module and shape, rather
than merging types by layout, printed text or a shared implementation body.
This is a logical identity rule, not a required interning table, physical
type name, allocation or layout.

This is a checked source construction, not an attributed permission to
inspect arbitrary callback bodies. Establish the capture types through their
ordinary source rules and the admitted text-fragment contract.
Character fragments retain `rune` context; no default numeric type,
implicit stringify operation, culture or encoding is introduced.
Alignment uses the standard Unicode-scalar minimum width, not terminal columns
or a writer's code-unit count. An unsupported alignment/format/fragment
contract diagnoses the affected use; TextTarget does not create missing writer operations.
Quoted and raw interpolation use their existing decoded/validated segment
rules rather than reparsing expressions during formatting.

The [call rules](calls-and-inference.md#eager-text-content-evidence)
can use this fixed generated type as forward evidence for the content
formal. They do not infer the types of unresolved holes from a desired
formatter or choose a callable by executing formatting code.
Already established explicit/typed generic arguments stay established.
Two distinct generated complete types do not become equal because their
field layouts or formatted output happen to match.
The annotation supplies no signature identity or ranking preference, and
cannot make an ambiguous callable group choose the attributed declaration.

At execution, [eager capture](#eager-capture-and-acquisition) occurs at the
argument's ordinary source position, before later arguments and invocation.
By-value formation obeys ordinary copy/transfer rules; a named unique
capture requires explicit `move`. The generated type is Copy only when its
complete stored captures permit Copy. Unique, borrowed or nonsized inputs
do not acquire copying, storage or representation authority from the
annotation. An implicit-reference input materializes/borrows the generated
value only under the existing supporting-scope contract; explicit reference
inputs do not gain that convenience.

The generated formatter drains its captured values through the selected
`ITextWriter` and individual `IFormattable` contracts. Ordinary formatting
calls may repeat while the content and its dependencies remain valid;
repetition formats the held values, never reevaluates source holes.
Refusal stops that drain's later fragment operations and can leave accepted
output. It does not replay completed fragments, consume the content as a
special single-use program, or turn a partial writer result into success.
Ordinary interface formatting does not itself grant a resumption protocol.

An already typed argument remains an ordinary argument, even when this
parameter is annotated. Its usual binding and formatting requirements still
apply; TextTarget does not defer `Compute()` or regenerate a value as source
interpolation syntax. An omitted default likewise retains its ordinary
published Copy-constant contract, not hidden hole execution.

### Consumers, witnesses and retention

The selected declaration owns the parameter's content-construction
capability. Interface calls retain the selected requirement's exact
parameter contract; forwarding preserves formed content and its signature
without repeating source construction or adopting an implementation's
different annotations, parameter names or private body.

A consumer can decline formatting after receiving eager content, or invoke
its ordinary formatting operations subject to their contracts. Source
capture effects have already occurred. Consumer status, output, failure and
cleanup remain declared behavior; TextTarget does not impose plain-value
construction's requirement failure on every logger or writer.
It grants no purity, ConstEval, NoAlias, allocation, access or transfer
guarantee beyond the checked operations involved.

Stored, returned or async-retained content needs an explicitly permitted
ownership/backing/completion relationship. The attribute supplies neither
noescape nor permission to extend caller backing lifetimes.
Copying a generated formatter preserves its captured reference/handle
dependencies, not a deep snapshot or fresh mutable backing.
A consumer's later generic use, callback invocation or suspension remains
subject to the ordinary captured-lifetime contracts.

## Construction use and admission

`TextBuilder` selects the builder contract for target-directed interpolated
text. `CollectionBuilder` selects it for target-directed collection
expressions, including an empty expression and supported spreads.
Noninterpolated [Text literal parsing](literal-construction.md) is distinct;
this registration does not redirect that parser or create an implicit
allocating string type. A typed value does not regain expression-construction
syntax because its type has a builder.

A complete construction contract must identify its result relation and
exact operations, input/capture modes, allowed fragments/spreads, access,
effects, failure/refusal, ownership, backing, initialization/publication and
cleanup. Operations use checked nominal protocols and retain exact selected
witness/member identities. Text output uses the selected `ITextWriter`
contract; formatting supplied values uses their `IFormattable` contracts.
Collection acceptance and publication likewise require their admitted
nominal protocol. An existing implementation can use an explicit nominal
adapter; this does not require boxing or dynamic dispatch.
Method names such as `Create`, `Append`, `Add` or `Build`, or a new
per-method role-attribute catalog, are not substitutes for these identities.
Ordinary overload/inference rules cannot infer a protocol from a set of
similarly named members. The selected acquisition, completion, drain and
call-side requirements remain separate from type-recipe registration.

The [expression contract](../grammar-spec.md#34-expressions-and-precedence)
retains eager once-only capture and ordered draining, explicit transfers,
no rollback/replay on refusal, and checked publication/cleanup.
The capture/acquisition order is specified below. Binding a recipe does not
supply a missing operation catalog or call-side adapter schema.
There is no implicit native constructor, array/List fallback, intermediate
`Text`, allocator selection, callback-body inspection or async lifetime
extension.

Naming a builder type does not initialize its static storage or acquire
an owner, write loan, pin or sharing permission. Actual generated operations
retain their ordinary invocation/initialization and access obligations.
An independent consumer needs the complete published construction contract,
not private imported layout or a caller's accidental access to a builder
implementation.

Required constant construction separately needs checked ConstEval/program,
operation/resource and target value-codec admission. An ordinary runtime
builder may allocate, refuse or have identity-bearing state without being
an emitted constant. Conversely, a Blittable or frozen Copy target does not
acquire a builder, literal parser or constant-formal domain through layout.

## Parameterless builder acquisition

`builder-acquisition/1` supplies the checked nominal factory role for plain
source construction. Core's resolved `IBuilderFactory` requirement has this
logical shape:

```terrum
public interface IBuilderFactory<this TBuilder>
{
    public static bool TryCreate(init ref<TBuilder> builder);
}
```

The bound complete builder type supplies this exact implementing self and
selected static member through its checked witness. A same-named method,
constructor, implicit default value or user-defined same-spelled interface
does not supply the role. Wrong input/output mode, an additional required
runtime argument, missing witness, inaccessible operation or unsupported
builder type diagnoses before the affected construction executes.
The ordinary type/constraint and witness rules retain exact formal identities;
one convenient specialization cannot repair a generic factory contract.

Source construction supplies vacant local storage of the complete recipe
type, then invokes the selected factory after that site's eager captures:

```terrum
require TBuilder.TryCreate(init builder);
```

There are no ordinary input arguments. In particular, the language supplies
no inferred allocator, destination, capacity hint, format provider, caller
receiver or captured caller-local context to this member. The type-only
registration is not a closure. Construction needing caller-selected runtime
context uses an ordinary explicit API, whose arguments and nested expressions
keep their normal staging and contracts.
A factory can use its own checked declared dependencies, initialization,
allocation and effects; parameterless does not mean effect-free or forbid
explicitly supported provider use inside its body. The language does not
choose that provider for it.

### Acquisition state and failure

The output is vacant on entry. Normal true initializes one valid live
`TBuilder`; normal false and checked exceptional exit leave it vacant,
with acquired partial state cleaned under the output contract. This includes
a post-body check or authored exit action that faults after forming the
builder: no value is published on exceptional completion.
A plain construction converts normal false to ordinary `require` failure,
whereas an explicitly called factory retains its Boolean result unless the
caller checks it. Operation exceptions propagate unchanged. There is no
default builder, fallback constructor, second provider or retry.

Before success, the factory owns its private partial state. Normal success
transfers exactly the output value and its supported obligations to the
source local; residual private obligations are cleaned there. False/exception
cleans acquired private state without making the output readable.
Only actually live owned values are cleaned, exactly once; generated owner
cleanup retains its nonthrowing/nonsuspending contract.
The source then holds that initialized builder through its reached drain
operations and transfers it into consuming completion. Failure before that
transfer leaves the source responsible for its actual remaining builder
state. The factory does not regain ownership or clean a published builder.

The source builder local has the ordinary place mutability needed by its
selected operations, not invented backing write or ownership permission.
Its complete acquired type and published dependency/transfer contract must
actually admit those operations and any later result publication.
A result borrowing retired factory locals or unsupported caller backing
rejects. Empty construction still calls the selected factory and completion;
it does not assume an allocation-free or side-effect-free result.
Repeated source evaluation acquires a new live value under existing
supporting-scope reuse rules, not a revival or reset of an old builder.
Call-side TextTarget formation does not itself invoke this factory or infer
a target-owned builder merely to hold already evaluated captures.

## Consuming builder completion

`builder-completion/1` supplies the checked nominal completion role for
plain source construction. Core's resolved `IBuilder` requirement has this
logical shape:

```terrum
public interface IBuilder<this TBuilder, TResult>
{
    public static bool TryBuild(TBuilder builder, init ref<TResult> result);
}
```

The selected complete interface instance identifies the implementing builder,
result type and static requirement member. Matching names, an instance
`TryBuild`, a constructor or a user-defined same-spelled interface do not
implement this role. A complete construction contract must retain the exact
witness and its checked relation to the already established target/result.
The type recipe alone does not discover a result type, choose among competing
completion witnesses, invert a representation wrapper or supply acquisition.
Wrong modes, unsupported transfer/storage, missing result relationships or
unavailable required operations diagnose before affected execution.

After its reached captures and accepts, source construction transfers the
private builder value into this by-value input. Schematically, with otherwise
admitted acquisition/acceptance and an exact vacant result destination:

```terrum
require TBuilder.TryBuild(move builder, init result);
```

The source builder local is vacant after the transfer. There is no source
reuse, reset or retry of that value on success, refusal or exception.
An owning/affine implementation retains its actual transfer and cleanup
obligations. A Copy implementation is not made unique: ordinary explicit
copies and their backing remain separate values, and moving this source
local does not end unrelated owners or copies.
The by-value input is not a reference loan and grants no new backing write,
pin, sharing or alias-separation authority. Implementation-local mutability
and operation access remain ordinary checked permissions.

### Result and failure relation

For this requirement, a normal true result initializes exactly one live
`TResult` output; normal false leaves that output vacant. This includes the
logical initialized state of zero-data `void`, not a fabricated default value.
The output destination is vacant on entry. Invalid output state/validity is
an invalid implementation contract, not refusal, successful partial output
or permission to manufacture a result.

Checked exceptional completion leaves no caller-readable completed result
and follows the output's exceptional-vacancy/partial-cleanup contract.
This includes a post-body check or authored cleanup action that faults:
forming the output inside the body does not publish it on exceptional exit.
Operation exceptions propagate unchanged. A plain construction maps normal
false to ordinary `require` failure after the consuming call has completed;
an explicit protocol call keeps its Boolean result unless its caller checks
it. No failure restores the transferred source builder.
Call-side staging/check failure after transfer but before entry likewise
uses ordinary staged-value cleanup, not a promise that the body executed.

The callee owns the supplied builder value and any private partial result.
Before returning or propagating a checked failure, it performs ordinary
cleanup for the obligations it still owns. Success can transfer storage or
accepted values into `TResult`; those obligations then belong to the result
and must not be cleaned again through the builder. Refusal/exception cleans
privately constructed output and residual builder state exactly once.
Captures never transferred into the builder remain with their actual holders.
Generated owner cleanup is nonthrowing and nonsuspending; authored cleanup
actions retain their existing failure/chaining rules.

The result may retain only backing that its checked output/transfer contract
keeps valid. It cannot borrow storage retired with the consumed builder or
extend a caller's captured view merely by returning successfully.
`init` and consumption imply neither NoAlias nor purity, ConstEval, allocation
or an independently valid value codec.

### Explicit reusable builders and adapters

An explicit reusable API can retain `mut bool TryBuild(init ref<TResult>)`
and its own post-success/post-refusal state. It is not the source completion
requirement. A checked nominal adapter can own/wrap that implementation,
transfer the complete result and clean whatever remains while implementing
the consuming member. A successful empty/reset builder state is not assumed
from the method name.

No separate source abandonment/reset method, implicit conversion to a
reusable builder, or body-discovered adapter is added. Failure before
completion leaves the private builder with its current owner for ordinary
cleanup; failure after transfer cannot make the caller clean it again.
The [factory](#parameterless-builder-acquisition) and
[drain](#standard-text-operations) requirements must be supplied separately;
this completion alone does not admit construction.
This completion role does not turn TextTarget's ordinary captured formatter
or a supplied writer into a single-use content program.

## Standard text operations

Under `construction-operations/1`, Core's resolved formatting and writer
requirements have these logical signatures:

```terrum
public interface IFormattable
{
    public bool TryFormat<TWriter>(write ref<TWriter> writer, TextSpan format)
        where TWriter : ITextWriter;
}

public interface ITextWriter
{
    public mut bool TryWrite(TextSpan text);
    public mut bool TryWriteScalar(rune value);
    public mut bool TryWriteFormatted<TValue>(
        ref<TValue> value, int32 alignment, TextSpan format)
        where TValue : IFormattable;
}
```

The types above denote their resolved Core identities. A same-spelled user
type, matching member list or private formatter body does not supply these
witnesses. These are synchronous Boolean operations, not implicit async
formatting, universal string conversion or per-primitive overload discovery.
Readonly value/reference modes preserve complete types and actual backing.
The writable writer input/receiver needs existing access authority;
none of these interfaces grants copying, NoAlias, retention or allocation.
Each chosen implementation still satisfies its published effect, failure,
input validity and dependency contract.

`TryWrite` accepts validated text; true accepts its complete scalar sequence.
False may leave an accepted prefix of complete scalars under the writer's
contract. `TryWriteScalar` accepts one valid `rune`; true accepts that scalar,
and normal refusal accepts none of it. Encoding/storage adapters must preserve
their declared scalar validity and external failure contract, not expose
half-written encoded scalars as a valid text view.
Neither operation changes the caller's source view or silently chooses an
encoding, culture, normalization or text allocation.

`TryWriteFormatted` is the typed interception point. It borrows the supplied
value, applies its selected formatting behavior and the alignment rule below,
and returns true only after the complete formatted fragment is accepted.
An implementation can intercept typed values or forward them through the
selected `IFormattable` requirement. Forwarding keeps that same selected
writer for nested values; it does not replace them with obligatory strings
or bypass a custom typed hook by inspecting a private body.
The supplied value formatter is invoked at most once by this formatted-value
operation; each nested operation has its own invocation. Two source holes
using the same value still have distinct formatted-value operations.
Repeated use alone does not authorize sharing a cached formatter result;
ordinary transformation rules still require a proof of equivalence.
A writer can refuse
before reaching formatting or while accepting its output; already performed
formatter effects and accepted output remain. Refusal stops later fragment
work. An unsupported type-specific format or ordinary checked failure follows
its declared failure behavior, not a successful empty/default fragment.
Exceptions propagate unchanged; a normal false is not a substitute for them.

### Alignment and format data

Alignment has the expected type `int32`, including a contextual numeric
alignment literal. This is a target for that role, not a default numeric
type for the supplied value hole. Its absolute mathematical magnitude is a
minimum output width measured in Unicode scalars. Positive values right-align,
negative values left-align, and zero adds no padding. Pad with U+0020 spaces
before or after the completed formatted scalar sequence as required;
content longer than the minimum is never truncated.
Combining marks count separately, and one supplementary scalar counts once
regardless of its UTF encoding. Grapheme clusters, bytes, code units and
terminal display columns are not this width domain.

The magnitude of `int32` minimum is 2,147,483,648. Compute magnitude, scalar
counts, differences and encoded-storage requirements in a domain that can
represent the mathematical result; unchecked `int32` negation is invalid.
Capacity/storage refusal and operation failure retain their own contracts;
neither wraparound nor a narrower target width may manufacture success.

Right alignment can require staging until formatted width is known. The
writer may stage under its checked storage, backing and refusal contract,
or use an equivalent in-place operation where its actual destination allows
it. It cannot format once to count and then replay to write, even when one
value appears pure. No universal heap, allocating `Text`, hidden capacity
pass or mandatory staging representation is supplied by the language.
Formatter, staging and padding effects occur at their declared operation
points, with ordinary cleanup on refusal/exception.

Format data is a validated `TextSpan` input. A static identifier format denotes
its spelling as text; quoted/raw format text uses its decoded segment rules.
A parenthesized runtime format expression binds under the ordinary expected
`TextSpan` contract and is captured once after alignment.
There is no implicit object stringify, format-provider argument or runtime
reparse of source expressions. Each value formatter owns its supported
format vocabulary. The generated interpolation formatter itself supports
only empty outer format data; nonempty outer format uses ordinary `require`
failure, not a Boolean output-refusal result.

### Standard interpolation graph

A supported interpolation has one ordered literal-data segment before/between/
after its value holes and one formatted-value node per hole. Literal data is
validated `TextSpan`; a segment with empty scalar content makes no writer call.
Each other segment uses the selected `TryWrite` requirement.
Every hole uses `TryWriteFormatted` with its exact captured value, effective
`int32` alignment and effective `TextSpan` format. A character literal receives
`rune` context; ordinary rune formatting can then forward to `TryWriteScalar`
without an intermediate `Text`. A typed hole still uses the typed hook rather
than a body-discovered direct scalar overload.

The complete logical shape retains these ordered segment/value roles and
effective argument types/modes/witnesses, even when a segment is empty or
alignment/format is omitted. Values, literal text and the zero/empty defaults
remain instance data. Equal closed shapes in one module therefore retain the
same generated content type without sharing evaluations, data or backing.
This logical graph does not mandate physical fields for static data.
An explicit user call to `TryWrite(emptyText)` retains its ordinary effects;
the source graph's empty-segment rule is not permission to erase that call.

Drain follows source fragment order, stops on the first normal false or
exception, and never replays a reached formatter.
For plain text values the builder must supply the selected `ITextWriter`,
factory and exact target-result completion witnesses. For TextTarget content,
formation does not acquire a library builder; the ordinary consumer supplies
the writer at each reached formatting call. Their Boolean/refusal mapping
remains distinct.
Nested typed formatting forwards the writer through ordinary witnesses.
Nested source construction still needs its own selected target and eager
capture/acquisition/drain; `IFormattable` alone does not authorize flattening
source syntax or avoiding observable intermediate construction.

## Collection acceptance and spreads

The standard collection construction requirement composes the same Core
factory/completion identities with one element-acceptance member:

```terrum
public interface ICollectionBuilder<this TBuilder, TItem, TResult>
    : IBuilderFactory<TBuilder>, IBuilder<TBuilder, TResult>
{
    public mut bool TryAdd(TItem value);
}
```

Retain its exact complete instance and inherited requirement identities.
The selected target's published registration/conformance supplies its
element term under the owning target formals; ordinary forward substitution
establishes it at use. This supports the existing typed element/spread evidence
in a known collection family, not inverse inference from a builder body,
result wrapper or associated projection.
The target/result and builder recipe must determine one such complete
construction relationship independently of source element values. Repeated
paths to one witness/member identity deduplicate under ordinary witness rules;
distinct eligible element relationships remain ambiguous and diagnose.
Do not choose among `TItem` witnesses by literal width, convenient values,
formatter behavior or a private body.
After selection, `TResult` is the exact normalized contextual target and the
completion output is of that complete type. Immediate access, ownership,
representation and nested arguments do not change the init destination.
An explicit adapter can perform separately admitted internal conversions/
attenuation before initializing that result; the source does not search for
such an adapter or silently convert a different completion output.
The same exact target-result relationship applies to a plain text builder.
Naming a nominal target does not admit an unsupported ownership/representation
carrier or author a registration for it.

`TryAdd` takes one by-value `TItem`. True appends exactly that item to the
ordered accepted sequence and retains its checked copy/transfer/backing
obligations. Normal false does not append the item; a consumed rejected input
is cleaned under ordinary parameter ownership before the call completes.
The previously accepted sequence remains with the builder. Refusal does not
roll back other permitted prior state changes or effects.
This is a logical input sequence, not a promise of one physical slot/node per
input or a universal result cardinality/order. The target's published result
semantics determine its representation, including any admitted deduplication
or aggregation. Cleanup obligations discharged under that target contract
still occur exactly once; no consumed owner becomes untracked.
An exception preserves its actual partial effects and owner transitions
under the selected contract, without restoring a moved source.
The source stops further accepts on false/exception and abandons actual owned
partial state, or transfers the fully accepted builder into completion.

Ordinary elements capture as their selected complete item type through the
normal value-binding rules. Named unique inputs require explicit `move`;
fresh owning results may transfer directly. Drain transfers a held non-Copy
item into the by-value acceptance input rather than silently cloning it.
There is no language-wide Copy/managed-storage restriction: the target's
declared element/storage constraints and lifetime contracts determine support.
A mutable builder place does not supply missing write access to its backing.
An instance `TryAdd` without this witness, or a `TryAdd(ref<TItem>)`, is not
this acceptance role.

A spread captures its source expression during eager capture, then uses its
selected [value-iteration contract](iteration.md) when drain reaches it.
Standard sources are exact ISequence<TItem, TCursor> factories or direct
ICursor<TItem> values, with unambiguous complete relationships.
TryMoveNext publishes each item into a vacant destination; the caller
does not separately observe Current. Factory acquisition occurs only at
the reached drain position. A direct cursor already adopted during capture
retains its required close even if drain never reaches it.
Exact acquisition/advance/close, item modes and backing remain required;
GetEnumerator, MoveNext, Current or Close names alone are not the contract.
There is no speculative length/count pass, replay, automatic async enumeration
or collection materialization.
Accept items in the declared iteration order. Stop before later advances/
accepts/spreads on refusal or exception. Retire actually live iteration state
once on normal exhaustion or unwind before later completion.

A borrowed item can be read/copied only when that exact iteration/item
contract permits it. Moving elements out of a borrowed sequence is not
inferred from spread syntax. An owning cursor may instead yield fresh
owned item results for by-value acceptance under its explicit transfer
contract. Retained item/backing dependencies must survive later advancement,
builder storage and result publication; an expired lending item cannot be
silently copied, boxed or lifetime-extended.
An untyped nested collection/spread expression still needs its own admitted
target/direct-fragment relationship. This standard acceptance member alone
does not select an array/List or intermediate collection family.
Lending/async and generated producer catalogs, together with nonstandard
construction extensions, remain independently admitted contracts.

## Eager capture and acquisition

`eager-construction/1` is the evaluation contract for supported text
interpolation and collection construction. Evaluate the source captures
once, in source order, before acquiring that construction's builder or
beginning its drain. A builder or consumer cannot suppress these evaluations
because it is disabled, refuses output or already knows its capacity.
Eager capture does not imply that evaluation succeeds: an ordinary failure
stops the remaining captures and follows the existing failure/cleanup rules.

For an interpolated hole, evaluate its value, then its supplied alignment,
then its supplied runtime format, before proceeding to the next hole.
Use their already checked target types and modes; no implicit numeric
width, stringify operation or deferred callback is introduced.
Literal segments are the validated text supplied by the source rules,
not calls to a formatter during capture.

For a collection, evaluate each ordinary element and spread-source
expression at its source position. Capturing a spread source does not
enumerate it. For example, this expression:

```terrum
List<int32> items = [A(), ..Source(), B()];
```

has the following protocol-independent order, assuming its complete
construction and iteration contracts are available:

```text
capture A() -> capture Source() -> capture B()
-> acquire builder
-> accept A's capture -> enumerate the captured source -> accept B's capture
-> complete and publish
```

This trace names semantic events, not new source operations or a protocol
inferred from method spellings. The spread's actual item copy/borrow/transfer
and iteration cleanup follow its selected contract.

Short-circuit and conditional expressions inside a capture retain their
ordinary control flow. A nested construction evaluated as a capture performs
its own capture/acquisition/drain sequence there; the outer rule does not
delay every nested builder until the outer capture phase ends.
An empty construction has no source captures. Its acquisition/completion
behavior still follows its checked protocol, not an assumption that it is
effect-free.

Each capture preserves ordinary copy, explicit move, reference formation,
availability and dependency rules. A copied handle or view does not
privatize its backing; a captured reference is not a deep snapshot.
Later captures may change reachable state only when the ordinary
alias/access/dependency contracts permit it. Subsequent formatting or
iteration observes the state at its own specified read points.

For [standard spread sources](iteration.md#eager-collection-spreads), retain
the factory's checked readonly receiver/place/backing without enumerating it.
A direct cursor capture instead stages/adopts the supplied traversal value
and registers its close obligation immediately. Later capture/acquisition
failure or earlier refusal closes that already-created cursor during ordinary
abandonment, without advancing or starting its producer body. This does not
acquire an unreached factory cursor or defer source expressions.

An affected builder's generated acquisition and associated initialization,
capacity or enablement operations occur after its captures. Additional
operations must belong to the checked protocol and preserve that order;
there is no speculative enumeration, formatter pass or replay of source
expressions to determine a capacity hint. An operation explicitly authored
inside a capture retains its ordinary evaluation point.

If capture fails, no acquisition for that construction has begun.
Already staged owning values follow ordinary unwinding and cleanup; a moved
source does not become live again. Nested operations that already completed
are not rolled back.

### Call-side capture and drain boundaries

For an admitted call-side text construction schema, capture the interpolation
at that argument's ordinary staging position. Evaluate later ordinary
arguments afterward; enter the selected callable only after normal argument
staging and invocation checks. The receiver/callable still evaluates before
arguments. A callee's enablement test, preconditions or decision not to format
cannot suppress earlier capture effects.

The [TextTarget schema](#texttarget-parameter-schema) identifies its exact
content parameter. Naming it does not defer arbitrary arguments or change
ordinary overload selection.
Eager construction does not require a new `ITextContent`/`ITextContext`
interface, consuming content program or per-consumer evaluation-mode
declaration. Captured values may be formatted through the admitted typed
`IFormattable`/`ITextWriter` path; their actual copying, retention and cleanup
remain part of the consumer's ordinary contract.

Formatting and spread enumeration are drain operations, not capture
operations. Refusal stops subsequent drain work, but does not undo earlier
capture effects or accepted output. Eager construction does not force an
unreached formatter, spread acquisition, iterator step or remaining item.
In particular, evaluating `Source()` is distinct from running all of the
body of a lazy sequence it returns.
No refusal retries source expressions, already accepted fragments or
consumed items. A required owning result is published only after its complete
checked construction succeeds; refusal cannot manufacture a successful
prefix or default target.

### Refusal in plain value construction

A plain interpolated-text or collection value construction requires all of
its selected acquisition, acceptance and completion operations to succeed.
A normal Boolean refusal from any of those operations uses the
[ordinary `require` failure](../language.md#safe-and-unsafe-boundaries);
it is not a successful prefix, a default result or a registration-selected
alternative failure. No additional source `require` is needed around the
plain construction.

The semantic expansion checks each selected operation's Boolean result once
with ordinary `require`, immediately after that operation completes.
False stops later drain/completion operations and raises the selected
ordinary requirement failure. Existing source captures already completed;
their effects and transfers are not undone.
An exception from evaluating an operation propagates as that exception,
not as false or a new requirement failure. Cleanup and causal chaining follow
the ordinary rules on the resulting control flow.

Acquisition output destinations retain their checked live/vacant relations.
The [consuming completion](#consuming-builder-completion) requirement uses
its fixed true/live, false/vacant and exceptional-cleanup relation.
A supported true/live result is published only through its admitted
ownership, access and backing contract. A missing or contradictory relation
is an invalid operation contract, not permission to fabricate a result.
On refusal, any private partial state remains with its actual current owner
for ordinary abandonment/cleanup; a caller-readable complete target is not
invented.

This rule applies to plain value construction, not every use of a builder
or formatter. An explicitly called `Try...` operation or an admitted
call-side writer/consumer keeps its declared status, output and failure
behavior. Such a consumer may decline draining after eager capture, or
report partial output on refusal. A false result is converted to a
requirement failure only when the consumer's own contract or authored code
requires it. The `Try` spelling alone supplies none of these contracts.

### Lifetimes, cleanup and transformations

Capture holders, the acquired builder and any reached iteration state retain
their actual live values and obligations. Failure or abandonment performs
ordinary cleanup exactly once for values still owned there. Never clean a
vacant value, replay a completed transfer or retire the published result
again through its former builder.
Cleanup retains its [existing guarantees](../lifetimes.md#owner-transfer-and-cleanup-contracts):
generated owner cleanup is nonthrowing and nonsuspending.
Authored `finally`/`defer` actions retain their ordinary effect, failure and
causal-chaining rules; they do not relax owner cleanup or add a transaction.
The [owning-field contract](ownership-and-cleanup.md) distinguishes failed
acquisition's reverse-acquisition unwind from complete body-then-fields
retirement. A moved/published child is not still owned by the builder;
partial field state never permits replaying its Cleanup.

Borrowed captures retain their real backing, root, access and lifetime
dependencies through every operation that uses or retains them. Acquiring a
builder cannot invalidate those dependencies or acquire extra write, pin,
sharing or transfer authority. Compiler materialization retains the
[existing supporting-scope rules](../lifetimes.md#parameter-only-implicit-readonly-loans);
ending a capture holder or drain does not automatically retire its backing
temporary. A returned/captured view needs a surviving admitted backing
relationship, not a fabricated extension of the holder's storage.
Async retention and later completion likewise require their separate
operation-bounded contracts; eager capture is not an escape permission.

Physical lowering can combine capture/drain storage or eliminate operations
only when permitted by the published contract or proved unobservable under
the ordinary transformation rules. Preserve evaluation and failure order,
alias observations, allocation/refusal, identity, backing, transfers and
cleanup. Equal final text or elements alone do not establish equivalence.

### Deferred evaluation is not admitted

There is no Core construction-evaluation policy domain, eager/incremental
case selector, deferred content producer, resumable drain or automatic
callback conversion in this rule set. No new keyword is added.
Ordinary `policy<...>` selection still selects checked callable variants;
it does not defer their receiver/argument evaluation or change this
construction schedule. A user enum/interface or same-spelled attribute
cannot add that authority.
Future incremental/lazy construction needs its own explicit source,
capture, effect, lifetime and completion contracts before it is admitted.

## Logical records and compatibility

| Field | Required meaning |
|---|---|
| Rules and kind | `builder-type-recipes/1`, `eager-construction/1`, `builder-acquisition/1` and `builder-completion/1` where construction shorthand is retained, exact resolved schema and TextBuilder/CollectionBuilder kind |
| Owner and target | Target-defining module, exact nominal definition and complete target pattern |
| Formal environment | Target type/constant identities and ordinals, domains, constraints and defaults |
| Builder recipe | Bound ordinary type tree, selected definitions, complete argument terms and supported normalization rules |
| Construction contract | `construction-operations/1`, exact Core text/collection witness/member and element/result roles, with access, effects, failure, ownership, backing and cleanup requirements |
| Acquisition | Selected complete Core IBuilderFactory witness/member, no ordinary runtime inputs, exact init-builder relation and partial/residual cleanup/dependencies |
| Completion | Selected complete Core IBuilder witness/member, by-value builder transfer, exact init-result relation, and residual/partial cleanup without source reuse |
| Evaluation | Source-order typed captures, after-capture acquisition and ordered drain/iteration, including actual transfer and dependency obligations |
| Refusal | Ordinary require failure for Boolean refusal in plain value construction; exact status/output behavior for separately admitted explicit operations and consumers |
| TextTarget | `text-target-parameters/1`, declaring callable/requirement and parameter ordinal, input shape, originating module and normalized complete content shape, checked formatter/writer identities and actual retention obligations |
| Text graph | Ordered literal/value roles, effective int32 alignment and TextSpan format data, Unicode-scalar minimum-width rule and once-only formatting/staging obligations |
| Spreads | Exact value-iteration factory/direct-cursor identities or separately admitted extensions, item transfer/backing, drain order and capture/adoption/close obligations |
| Dependencies | Builder/associated/witness/load identities and any required program, target/value ABI or resource recipes |

Independent consumers substitute retained bound identities, not a printed
type name or a guessed list of builder arguments. Relinking/remapping
preserves those identities and checks compatibility; it does not rerun
use-site lexical lookup. Renaming a source formal does not change its bound
ordinal or type recipe.

Removing a required registration, restricting the admitted target domain
or weakening published construction guarantees breaks dependent users.
A changed builder recipe is compatible only when the retained construction
contract and dependencies remain satisfied, including observable effects,
identity, refusal, backing and cleanup; equal final elements alone are
insufficient. Replacing eager capture with conditional or drain-time source
evaluation changes this contract even when one execution produces equal
output. No arbitrary body-equivalence test is promised.
Removing TextTarget or restricting its supported content domain breaks
source clients relying on that construction capability. Adding it can
affect applicability on recompilation, not retarget an already bound call.
Replacing consuming completion with a live/reusable source builder or
weakening its output/cleanup relation changes the retained contract.

The [metadata requirements](../representation/logical-records.md#builder-type-recipes-and-construction-handlers-encoding-pending)
retain this logical contract without allocating a TypeSpec form, Attr value
tag, table kind, clause, feature bit or opcode.
Physical capture/witness/program representations and nonstandard operation
extensions remain separately gated. [Conformance cases](../conformance/source-builder-type-recipes.md)
cover recipe schemas, authority, binding/substitution and admission boundaries;
[TextTarget cases](../conformance/source-text-targets.md) cover resolved
parameter schemas, eager formatter formation, binding, retention and
independent contracts;
[eager construction cases](../conformance/source-eager-construction.md)
cover capture/acquisition order, drain/refusal, dependencies and the
unadopted incremental boundary;
[acquisition cases](../conformance/source-builder-acquisition.md) cover the
parameterless nominal factory, initialized/vacant builder states, effects,
cleanup and runtime-context boundaries;
[completion cases](../conformance/source-builder-completion.md) cover exact
nominal identity, consuming transfer, output states, residual cleanup and
explicit reusable adapters. They do not claim execution of a builder
implementation.
[Operation cases](../conformance/source-construction-operations.md) cover exact
text/collection roles, Unicode-scalar alignment, typed/ordered draining,
result equality and separately admitted spread boundaries.
