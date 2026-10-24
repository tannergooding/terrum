# Owning fields, partial values and cleanup

This chapter owns logical `owning-fields-and-cleanup/1`. It defines unique
aggregate construction, field transfer and replacement, and complete versus
partial cleanup. [Structural flow](initialization-and-flow.md) supplies place
states; [dependencies](dependencies-and-views.md) supplies retained backing;
[task responsibility](task-responsibility.md) supplies completion obligations.
An ownership contract grants neither write authority nor a physical layout.
The shared [definitions](../language.md#definitions-and-notation) distinguish
copying, aliasing, references, lifetime and ownership.

## Values, storage and structural state

One live unique value has one responsible holder. Explicit transfer changes
that holder, vacates its source, and preserves the actual value's complete
type, resources, dependencies and outstanding protocol obligations.
Non-owning references/views to children, copied handles and collector roots
are not additional owners. A type promising Copy cannot contain a field whose duplication would
duplicate an owning obligation or declare an owning Cleanup operation.

Distinguish acquiring storage, initializing a field, establishing a complete
valid value, publishing it, retiring that value and releasing its storage.
Zero-data fields still have type, initialization and publication obligations.
Retired bytes are not live values, and a vacant field contains no default.

Structural tracking uses exact declaration/place identities and actual
storage generations. An aggregate's tracked fields include declared instance
fields and implicit property backing at that property's declaration position.
Computed properties and indexers are operations, not additional fields.
Partial type parts contribute their established compiler-input/source order.
Tuple positions and established active sum payloads need their admitted
structural contracts; overlapping raw Union layout alone supplies none.

A complete aggregate has every required live field and its checked type/value
validity. A partial aggregate retains its actual live fields and their
obligations, but is not a complete value usable as a whole. Vacancy means no
live value or retained acquired partial state remains in that place. Moving one
field does not make the remaining fields vacant.
Even an empty live-field subset does not by itself end retained aggregate
storage or establish whole-place vacancy. End its remaining state/obligations
before using the place as a wholly vacant initialization destination.

For inline aggregate storage, `aggregate is init` tests complete-value
availability. It is false for a partial aggregate, but that false edge does
not establish whole-place vacancy or permission to overwrite remaining live
fields. A field state test refines that exact field only.
An indirection's carrier and its payload are distinct places: a live ref,
unique class handle or own<T> carrier can still designate partial storage.
Testing its carrier binding does not prove the designated aggregate complete.
Existing descriptor projection rules are unchanged.

These states are not new type arguments or public bitmap/layout requirements.
Only supported, structurally identified storage can carry partial state.
Arbitrary properties, unknown indexed elements, raw overlapping fields,
opaque generic payloads or external aliases do not acquire such tracking by
spelling a field name.

## Construction and publication

New construction begins with vacant required storage. Declaration
initializers, constructor clauses/body, caller initializer entries and final
validation retain their
[existing phase order](../grammar-spec.md#510-properties-and-accessor-local-effects).
Initialization authority can establish eligible vacant subplaces; it is not
ordinary mutation authority after publication.

Before admitting a child as a complete value, storing that value or publishing
it, establish its complete type, validity, backing, root, access and cleanup
obligations. An initialized child is usable under its own contract even while
its parent is partial.
Whole-parent use or escape still requires the parent's complete-value gate.
An ordinary whole-value call cannot consume a partial aggregate as if it
were complete T; an admitted construction helper can receive only its
explicit initialization authority.

Acquisition order is the order in which actual live values become owned by
that construction. Skipped initializers acquire nothing. A field replacement
retires the former value under ordinary replacement rules; only the currently
retained acquisition remains outstanding. A moved child is no longer owned by
the constructing parent. Nested partial children retain their own acquired
state; a complete child transferred into a parent is one complete owned value.
Interleaved construction of partial child storage retains the actual order
of its outstanding acquisitions, not just the parent's field-start order.
Completing that child establishes its complete-value boundary; its subsequent
ordinary retirement uses the child's complete cleanup contract.

If construction or final validation fails before publication, unwind the
remaining acquisitions in reverse actual acquisition order, recursively
preserving the distinction between complete children and partial storage.
Never invoke the incomplete parent's complete-value Cleanup member, even
when all its fields happened to be initialized before validation failed.
A complete child still performs its own complete cleanup.
No false/default result, rollback of unrelated effects or cleanup replay is
introduced. Fail-fast/process termination retains its separate no-unwind rule.

Successful publication establishes the complete value once, with its ordinary
source/capture and output bounds. Constructor return alone need not be final
publication when declared caller initialization remains. A later failure
after publication retires that complete value under its responsible holder;
it does not retroactively classify it as failed construction.

## Structural field moves and repair

An ordinary field move requires a live field, a known unique owning aggregate
path, and that field's existing write or unpublished initialization authority.
Outside construction, the containing path must be writable and the field
must permit mutation. Readonly fields and readonly object access do not gain
that permission from unique. Whole-owner transfer remains possible from an
immutable binding under the ordinary named-move rule.

After publication, direct partial moves are admitted only for structurally
tracked unique aggregates whose cleanup is derived from their remaining live
fields. No authored complete-value Cleanup receiver may be left partial,
including an enclosing owned ancestor that requires a complete receiver.
A child's own Cleanup does not prevent moving the complete child when its
enclosing path meets those conditions.
Unpublished construction can transfer an initialized complete child under
its applicable initialization authority: failed-construction retirement does
not invoke the incomplete parent's Cleanup.

```terrum
public mut unique struct Pair
{
    public mut own<Resource> Left;
    public mut own<Resource> Right;

    public Pair(own<Resource> left, own<Resource> right)
    {
        Left = move left;
        Right = move right;
    }
}

mut Pair pair = new Pair(CreateResource(), CreateResource());
own<Resource> extracted = move pair.Left;
Use(ref pair.Right);
pair.Left = CreateResource();
Use(ref pair);
```

The fragment assumes admitted Resource, owning storage and Use contracts.
Between the move and successful repair, whole `pair` use rejects. Its Right
field remains live and owned. Failed repair leaves Left vacant and Right
live; it does not restore extracted. Ending that partial Pair cleans only
remaining live fields, not extracted or a fabricated Left.

Every whole read, reference formation, ordinary receiver call, by-value
transfer, return, conversion and whole-value pattern requires the affected
aggregate complete. Direct projection of an independently live child can
remain valid when its storage and full dependencies survive. State/tag
inspection requires the actual admitted discriminator, not a read of missing
payload. Ending a partial structural aggregate retires its live subset.
An eligible named-local `name = _` can end that retained partial state under
the same rules. `_ = move name` still requires a complete value for its
whole-value transfer. Ending the partial local does not end extracted children.

Repair initializes only eligible vacant fields. Live fields require ordinary
replacement; fixed fields are not reopened by a false state test.
When all required fields are live, establish every affected validity invariant
before a whole-value use. Neither refilling one slot nor copying unchanged
bytes proves a cross-field invariant. Existing checked validation facts may
be reused only under their actual mutation/dependency rules.
Repair does not rerun declaration initializers, constructors or caller entries.
Unsupported validity restoration diagnoses instead of inventing a Validate
call, hidden reconstruction or runtime default.

## Replacement and checked take/replace

Evaluate the destination place once, then its replacement expression once
in ordinary source order. The assignment itself does not retire the old value
until the replacement is complete and admitted. Construction failure cleans
only the new acquired state and leaves the old value in its actual current
state, subject to ordinary unwind. Permitted right-hand-side mutations remain
visible; assignment is not a snapshot or rollback of those effects.

Before commitment, check actual destination state/authority, surviving
dependencies, the staged replacement's dependencies, complete validity and
old-value retirement. A maybe-live mutable field retires its old value only
on live paths. An initialization-loan or other vacancy-only destination must
be wholly vacant; partial acquired state is not eligible empty storage.
An old task or guard cannot be discarded merely to install a new one.
Changing a nested child must establish every affected enclosing validity
invariant as well as that child's own complete-value contract.

After commitment, retire the old value exactly once and install the new
complete value without an exposed invalid-value interval. Required transfer,
retirement and publication operations must preserve their checked failure,
reentrancy, roots, address and affinity contracts. A callback, suspension or
escaping failure cannot observe an incomplete receiver or leave one for
generated Cleanup. No hidden retained old owner repairs a dependency conflict.
This confinement is not an atomic operation or a concurrency grant.

A type with authored Cleanup can expose a checked take/replace member
instead of a direct escaping field move. Stage an admitted complete replacement
before transferring the old field; return the old owning value while leaving
the field and every enclosing Cleanup receiver complete on all permitted exits.
An implementation-local transfer/repair interval is permitted only when its
checked operations cannot expose that interval or escape before repair.
The new value may be an explicitly constructed absent optional when the
field's complete type permits it, never an invented default Task/resource.
No Take/Swap/Exchange name, intrinsic, attribute or public library API is
selected by this contract. Implementation truth, not a method name or an
unchecked postcondition, must establish the boundary.

## Complete-value Cleanup and transitive field retirement

The resolved no-argument Cleanup schema designates one primary cleanup
operation on an eligible unique value, aliasable class object or boxed
payload. It is one object-level obligation, not one operation per handle or
box alias. It is nonrepeatable; multiple selected operations reject. Its
target is a synchronous parameterless readonly instance method returning
void. Static methods, constructors, accessors, async/extern or
caller-unsafe declarations cannot supply this automatic operation. The
method's ordinary name does not register it. Partial member authorship and
generic substitution preserve the exact selected operation identity.

Cleanup is invoked by value retirement, not directly through ordinary calls,
overload lookup, an interface-name convention or a Close method. It grants
no user-callable destruction alias. It may have its ordinary declaration
visibility; the registered retirement contract retains the selected member
without exposing otherwise inaccessible members.
The authored body receives a complete receiver and live fields.
That body cannot move/end its receiver or expose/leave an owning field vacant.
Any field update/take preserves the checked complete-receiver boundary.
Reachable writes, I/O or resource release still need their actual effect,
access, error and dependency contracts; Cleanup is not Pure or suppression.

On complete retirement, run the selected authored body once, then
automatically retire fields in reverse declaration order. Each complete owning
child recursively performs its own body-then-fields cleanup. Without an
authored body, perform just the derived field retirement.
Retire non-owning descriptors, roots and other field-held obligations under
their actual contracts; never destroy a referenced value merely because its
descriptor is a field. Release containing storage only after the value's
remaining transitive obligations permit it.

The backing policy selects when this one obligation runs. Normal shared class
or box backing invokes Cleanup once after the final strong hold and bounded
dependency retire; unreachable strong cycles under this policy are not
collected and can therefore keep Cleanup from running. GC backing invokes it
once after the collector establishes unreachability and before reclamation;
the time is nondeterministic. GC Cleanup cannot resurrect or republish its
receiver, and runs on an unspecified runtime cleanup thread. Its full
transitive cleanup must therefore be thread-neutral or safely transferable;
the language does not require an affinity-preserving cleanup scheduler.

This order is not reverse acquisition order. A field's later replacement or
repair does not change its declaration position. For an allowed partial
structural aggregate, retire the remaining live fields in that same reverse
declaration order, without reading or cleaning vacant fields.
Failure of unpublished construction instead uses reverse acquisition order.
There is no dynamic topological reorder to repair an incompatible lifetime
or callback dependency.

Compiler-invoked Cleanup runs only during normal retirement; it is not
generated as exceptional-unwind cleanup. If an invoked Cleanup throws, that
failure propagates under ordinary exception rules and the remaining generated
owner/field cleanup does not resume during that unwind. Retirement has not
completed, and the runtime does not infer that the remaining obligations were
discharged. Exceptional cleanup is authored explicitly in crossed `finally`
blocks. Nonsuspension remains required for this Cleanup operation.
Automatic Cleanup is not a separate user-callable finalizer API or a
universal Dispose convention. Its GC-triggered retirement path has the
backing-specific rules above. An unsupported destructor or dependency
schedule diagnoses before use.

## Dependencies, carriers and exceptional ownership

A field move transfers that field value's obligations, not the lifetime of
its old inline field storage. References to that storage block invalidation
unless their actual contract no longer needs it. Moving a unique handle
can preserve its object allocation while vacating the handle slot; own<T>
can preserve its selected allocation, not unrelated reachable resources.
Inline relocation, pinning and root/collector behavior require the actual
profile. Neither equal addresses nor bits prove a lifetime transfer.

Check every surviving dependent before move, replacement, partial repair or
retirement, including dependencies retained by sibling fields, descendants,
external holders and later cleanup. Precise checked field places can permit
independent sibling use; there is no blanket root exclusion. A broad published
whole-source relationship cannot be narrowed by private field inspection.
Reentrant whole-value observation likewise requires a complete receiver.
Readonly access, unique ownership and NoAlias remain distinct contracts.

Staged arguments, callee-owned inputs, saved returns and initialized outputs
retain their actual holders through failure. A later argument or postcondition
failure retires only the still-owned values, in the existing scope order,
without returning consumed values to their old fields. Conditional joins keep
per-field state and guarded owning/dependency facts; a complete path and a
partial path do not become an unconditionally complete aggregate.

Task-bearing fields retain explicit every-exit completion responsibility.
Automatic Cleanup never waits, blocks, cancels or invents a supervisor to
make a pending task disposable. Move or settle that responsibility under its
admitted protocol before field/aggregate retirement. A transferred or
published payload/element loan retains its independent backing and close
obligations. Storage roots alone discharge none of these.

## Retained contracts and independent admission

[Cross-thread authority](../runtime/sharing-and-handoff.md) likewise retains
every actual field/source and cleanup obligation. A complete child can cross
only with its own eligible form and admitted handoff; a partial parent cannot
be published as complete. Guarding a value does not change cleanup affinity
or authorize a surviving alias to bypass its current protocol.

Retained contracts preserve complete unique/Copy identity, structural field
identities and order, initial/partial/complete states, selected Cleanup and
its effects, field move/repair boundaries and complete source/protocol facts.
Invocation binds those declarations to actual value and storage generations.
Replacing a derived-cleanup type with an authored Cleanup type, changing
observable field-retirement order or weakening the complete-receiver boundary
is incompatible with callers relying on the old contract.

A source/body checker establishes actual state and cleanup truth; the producer
is responsible for publishing correct contracts. A binary consumer validates
encoded state and cleanup record structure, references and required support
before affected use, then may rely on published facts without semantic
re-proof. An implementation cannot claim support without required partial
storage, receiver/field/cleanup facts or rules; unsupported required input is
rejected before affected use.
Logical approval allocates no live-mask, acquisition journal, field-state
record, root program, feature bit, table or opcode. Physical ownership and
partial-state codecs, call crossings and managed/native storage profiles remain
independently admitted.
[Conformance cases](../conformance/source-ownership-and-cleanup.md) are
logical obligations, not compiler/runtime or physical-layout proof.
