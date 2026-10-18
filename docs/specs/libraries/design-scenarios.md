# Library design scenarios and deliberate departures

These scenarios apply the [selected inventory](adopted-inventory.md), not
candidate API breadth. They give providers and consumers reviewable paths
through input preparation, normal refusal, exceptional failure, publication
and responsibility. They add no standard type, member, policy or physical
encoding. Code fragments assume the stated authored declarations and admitted
profile; they are not claims of current compiler support.

Framework Design Guidelines, third edition, is design guidance, not an
imported runtime contract. Relevant sections are 2.2.1 (scenario-driven
design), 2.2.2 (low barrier to entry), 2.2.4 (layering), chapter 3 (naming),
5.8 (parameters), 7.5.2 (Try), 8.3 (collections), 9.2 (async), 9.4.3
(scoped operations) and 9.12.3-4 (Try-write and partial progress).
The [provenance record](../references.md#language-and-framework-design-prior-art)
identifies the editions and scope. CLR idioms do not override Terrum's
selected ownership, output, access or failure rules.

## 1. Parse ordinary input or construct a contextual literal

**Provider:** an authored Copy Port type supplies a whole-input ordinary
parser with the [selected literal-adapter signature](../language/literal-construction.md#signature-and-eligibility).
If registered, its target module supplies the exact Literal record,
ConstEval body/intrinsic support, validity and admitted frozen result.
Runtime parsing remains that ordinary member's contract; no IParsable
interface or primitive parser catalog is inferred.

**Consumer:** after supplying a live immutable TextSpan input and vacant
Port destination, branch on the actual Boolean:

```terrum
mut Port value;
if (Port.TryParse(text, init value))
{
    Use(value);
}
else
{
    ReportInvalidInput(text);
}
```

Use and ReportInvalidInput are ordinary authored operations, not selected
library names. True establishes one complete valid Port; false establishes
vacancy, not a default Port or stale value. A destination already containing
a live value cannot be reused as vacant output. An unexpected parser failure
propagates under its declared contract and is not translated to false.
An adapter must not expose a prefix parse as whole-input success.

`Port(80)` instead selects the registered Integer construction from the
contextual token. Binding occurs before parsing/range/rounding acceptance;
construction refusal diagnoses without trying another overload or runtime
parser. Typed `Port(value)` is ordinary conversion, not re-parsing.
Compiler-time parser dependencies need not become runtime dependencies.

This follows the Try principle for expected rejection, while replacing the
CLR convention of a live default `out` value on false with checked vacancy.
`require` can consume an established Boolean/output relation but supplies
neither overload selection nor missing outputs.

## 2. Format typed content without an obligatory text allocation

**Provider:** a selected ITextWriter implementation accepts validated text,
single runes and typed formatted values through the three exact public
requirements. A value formatter implements IFormattable and uses the
supplied writable writer reference. It preserves its supported format,
effects, scalar boundaries, dependencies and possible partial acceptance.
No mandatory string conversion, locale, encoding or heap is involved.

An ordinary generic consumer can forward selected eager content:

```terrum
public static bool TryWrite<TContent, TWriter>(
    [TextTarget] TContent content, write ref<TWriter> writer, TextSpan format)
    where TContent : IFormattable
    where TWriter : ITextWriter
{
    return content.via<IFormattable>.TryFormat(writer, format);
}
```

**Consumer:** with ReadValue returning an authored formattable type and
already prepared writer/format inputs, the call can receive interpolation:

```terrum
bool accepted = TryWrite($"value={ReadValue()}", writer, format);
```

ReadValue executes once during eager capture before this site's drain.
Even a refusing/disabled writer cannot suppress that effect. Formatting
occurs at drain; refusal prevents later fragments and cannot undo earlier
output. The selected formatted-value operation invokes that formatter at
most once, including when false follows partial progress. A caller must not
automatically retry the entire content merely because it sees a Try prefix:
replay may duplicate formatting effects and external bytes.

Ordinary synchronous borrowing does not authorize a retained logger queue.
A retaining consumer needs its explicit complete capture/storage/lifetime
contract. TextTarget alone supplies no queue, handler acquisition, async
buffer or hidden adapter. Unicode-scalar alignment, exact format data and
nested writer forwarding retain the [operation rules](../language/construction-handlers.md#standard-text-operations).

The FDG Try-write and partial-progress guidance motivates explicit refusal
and progress contracts; Terrum does not copy a grow-and-retry convention into
an effectful typed stream. Incremental/deferred content remains unselected.

## 3. Construct and traverse an authored collection

**Provider:** an authored UserNumbers target owns its CollectionBuilder
recipe and one complete ICollectionBuilder relationship for int32 items and
exact UserNumbers result. The selected factory initializes a builder after
capture, TryAdd accepts/owns each supplied item under its outcome contract,
and consuming TryBuild publishes the result only on true. Partial/residual
state is retired exactly once on refusal, exception or abandonment.
This example does not adopt a standard List type or allocator.

**Consumer:** First/Last are ordinary int32-producing calls, and source
is a Copy factory source with an admitted exact synchronous int32 sequence:

```terrum
UserNumbers values = [First(), ...source, Last()];
```

Capture First, the spread source and Last once in source order before
factory acquisition. Drain the spread only when reached. Factory/element
refusal cannot suppress Last's already reached capture effect; refusal
before reaching the spread does not enumerate it.
A reached cursor closes after its last live item retires, before the
supporting source/header dependencies end. Named affine source transfers
still require explicit move, including an alternative direct affine cursor
source. No borrowed collection is consumed to satisfy
a by-value cursor role.

A standard value loop selects ISequence/ICursor nominal witnesses. True
publishes one complete item; false publishes none. An absent optional item
or `()` can be a successful item, not exhaustion. Reference-output loops
instead select one of the four distinct lending families. Writable lending
requires writable source authority, and output attenuation does not create
covariant init storage or an implicit readonly witness.

Concrete mutation/version/guard behavior belongs to the authored collection's
published contract. A cursor that promises failure on invalidation must still
permit required cleanup-safe close. The selected protocols do not invent
List guards, stable backing, reentrant mutation rules or snapshot APIs.
Async traversal additionally preserves its pending output reservation,
observed completion and awaited close; acquisition itself stays synchronous.

## 4. Forward a view without retaining descriptor storage

**Provider:** with an admitted Span<int32> family, an ordinary source-level
forwarder can publish its actual backing-only relationship:

```terrum
[return: DependsOn(nameof(source))]
[return: ForwardsBacking(nameof(source))]
public static Span<int32> Forward(Span<int32> source)
{
    return move source;
}
```

**Consumer:** ending the input descriptor binding need not end the returned
view if its descriptor storage was excluded. Its actual data and shape
backing, owner/lease/guard, root, load and affinity dependencies must remain.
Empty/end views are not dependency-free. A backing-only annotation does not
permit owner disposal, structural mutation or writing through readonly access.

An interface consumer retains the published broad contract unless a checked
witness/member refinement supplies the narrower one. Private implementation
inspection cannot remove bounds. A hypothetical tensor view can carry both
data and external shape sources; its unselected representation/API cannot
be repaired by dropping one. This path adds no Span constructors or codecs.

## 5. Own and retire a native resource

**Provider:** an authored unique resource admits acquisition, complete and
failed-construction retirement, nested dependencies, actual thread affinity
and any required native ABI. Its resolved Cleanup schema selects a synchronous
parameterless readonly void instance operation, with checked NoThrow and
nonsuspending retirement under its authored visibility/access/effects.
There is no mandatory public IDisposable member, finalizer or heap handle.

**Consumer:** keep one live responsible holder, borrow only for admitted
uses, and move explicitly to a consuming operation/holder. Transfer vacates
the source and preserves every resource and cleanup obligation. A consuming
callee must settle or transfer them on every exit; a failed call does not
restore the source. A borrowed view cannot outlive the actual resource merely
because its descriptor is Copy or the result uses an indirect ABI.

Complete Cleanup runs only for a complete receiver before transitive
remaining-field retirement. Failed construction retires actual initialized
children without fabricating a complete receiver. Partial field moves
require their own structural/access support and can prevent whole-value use.
This supplies scoped responsibility as motivated by FDG 9.4.3, without
copying CLR Dispose/finalizer inheritance or adding blocking task destruction.

## 6. Observe and consume an async operation

**Provider:** an ordinary ReadAsync supplies exact Core Task<int32>, with
its payload, completion, effects and complete capture/profile contract.
Establish responsibility before its async body/checks execute; subsequent
failure is terminal operation failure even when execution never suspends.

**Consumer:** inside an admitted async body:

```terrum
Task<int32> operation = ReadAsync();
int32 value = await move operation;
```

The named affine owner is consumed on both successful and failed wait exits.
IsCompleted observes terminal state without settling payload/failure/output
responsibility or proving publication to that exact destination. A fresh
task result can transfer directly; a borrowed observer cannot settle it.
On any other exit, settle or transfer the outstanding owner through an
admitted holder/operation. Dropping it, synchronous Cleanup, a cancellation
request or an Async suffix is not settlement.

Pending async cursor init destinations remain reserved until the exact
completion is observed. Await preserves those operation/output versions;
success Boolean, payload availability and terminal state are distinct facts.
Task-returning shape alone does not register completion-phase outputs.
Required scheduler/suspension/root/cleanup support must be supplied, not
silently replaced by blocking. No public scheduler, detach, cancellation,
observer or reusable task API is selected by this scenario.

## 7. Contain a foreign failure before local propagation

**Provider:** both directions of a selected bridge supply paired contained
adapters, exact ABI and result/failure states, affine packet/domain authority,
local mapping and callback responsibility. A callback's failure is contained
before returning through the foreign caller. No arbitrary local event
unwinds across a non-unwinding foreign boundary.

**Consumer:** establish the exact call/borrow/ownership contract first.
Normal completion publishes only its valid normal result. Failure publishes
the admitted failure record instead, then the adapter maps or retires it
exactly once under its declared policy. Mapping failure remains a real
failure; it is not empty success or a default normal result.
Transferred payload/cause resources and surviving foreign code/domain
lifetimes cannot be copied away by matching bits or a shared type name.

The [Windows bridge](../targets/windows-x64-failure-bridge.md) supplies one
selected target direction, not universal native unwind or exception layout.
No public managed-packet class, foreign payload codec or transparent CLR
Exception interoperability is implied.

## 8. Use one coherent generic numeric capability

**Provider:** exact public static INumericSelection requirements supply
Min/Max/Clamp together, inherited comparison/equality and Copy self evidence.
No implicit conversion, formatting, algebraic law, key hash or ConstEval
program is inferred from conformance.

**Consumer:** the selected same-type helper can preserve that requirement:

```terrum
public static T Limit<T>(T value, T min, T max)
    where T : [Copy], INumericSelection<T>
{
    return T.via<INumericSelection<T>>.Clamp(value, min, max);
}
```

Arguments evaluate normally once before invocation. Clamp checks min <= max
before choosing a result, including for a NaN value. Invalid bounds select
InvalidBoundsException rather than value-dependent overload fallback.
Floating selection's signed-zero/cohort ties and NaN rules remain the exact
[numeric contracts](../language/numeric-selection.md); ordinary comparison
is not IEEE totalOrder. Integer/native identities do not merge at equal widths.

## 9. Consume the machine protocol without interpreting program status

**Provider:** selected `--machine terrum.cli/1` emits one complete JSON
envelope and LF, with exact checked integers and separately routed program
stdout/stderr bytes. It reports actual final output/worker settlement, not
an in-progress success-shaped response. Deadlines/cancellation are explicitly
selected, not a mandatory fixed 30-second limit.

**Consumer:** validate the version, closed fields, input/result/execution
shape and actual selected command. Distinguish CLI outcome/status from
signed int32 normal program return data. A normal -1 program return is not
itself an execution-error. Preserve the reported output/host failures and
do not treat missing/broken JSON as an empty successful result.
Legacy schema-1/schema-2 commands and a native worker exit alone are not
evidence of this selected contract or normative artifact admission.

## 10. Deliberate departures from CLR framework idioms

| Topic | Terrum contract and reason | Relevant FDG comparison |
|---|---|---|
| Type/object roots | No universal Object/Exception base, implicit boxing or concrete inheritance is needed for selected value/category roles | Layering, type design and explicit interfaces |
| Try outputs | Boolean init defaults are live on true and vacant on false; failure preserves actual ordinary state and any distinct protocol cleanup/publication obligations, not fabricated defaults or rollback | 7.5.2 and 5.8.3 |
| Try writes and retry | Exact writer progress/effects govern false; typed content and streams cannot generally be replayed by grow-and-retry | 9.12.3-4 |
| Preconditions | require is a safe once-only check with source-owned failure construction; unsafe recovery and ensure's fixed category remain distinct | Argument validation and standard failures |
| Equality and keys | Ordinary numeric equality/ordering, representation ties and candidate key/hash domains stay separate | 5.7 and collection key design |
| Cleanup | Affine responsibility and structural partial-state retirement, not mandatory IDisposable/finalizer inheritance | 9.4 and 9.4.3 |
| Async | A task owns completion; await consumes it, and cancellation/terminal observation alone cannot settle it | 9.2 and async-disposal/iteration patterns |
| Conversion and layout | Exact complete types, explicit selected conversions and checked public target commitments; equal width/layout is not identity | Parameter/conversion/buffer and interoperability guidance |
| Text | No default encoding or obligatory temporary string; immutable validated views retain actual backing and scalar boundaries | Naming, buffers and low-allocation scenarios |
| Public names versus identities | PascalCase types/members, I-prefixed interfaces and descriptive camelCase parameters remain the review convention; case-only distinctions should be avoided in new public API design, without changing exact case-sensitive semantic/native identity | Chapter 3; the CLR cross-language motivation is guidance, not a Terrum name-folding rule |
| Substrate and tiers | Core roles do not mandate a collector, allocator, scheduler or OS; each selected feature supplies its real transitive support | 2.2.2 and 2.2.4 |

These departures are explicit contracts, not a blanket exception to framework
design discipline. Review each new API from a concrete consumer path, publish
normal refusal separately from failure, keep low-level authority explicit,
and add convenience only without hiding required responsibility or substrate.
Broader containers, algorithms, query catalogs and incremental content remain
[unselected surfaces](adopted-inventory.md#3-direction-and-deferred-surfaces).

The [conformance cases](../conformance/source-library-contracts.md) distinguish
finite inventory/scenario checks from actual source checking, full provider
implementations, filesystem/OS behavior and independent physical admission.
