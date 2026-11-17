# Cleanup declarations in private semantic persistence

> [!IMPORTANT]
> **Unapproved, provisional proposal for the continuing authorized plan.**
> This document recommends an exact private agreement; drafting it does not
> approve that agreement, implement class Cleanup or admit runtime execution.

## Scope, baseline and inspected gap

The [specification index](../specs/README.md) leads to the accepted
[class ownership](../specs/language.md#types-and-operations) and
[Cleanup contract](../specs/language/ownership-and-cleanup.md#complete-value-cleanup-and-transitive-field-retirement):
one object-level obligation, including for aliasable class objects, with an
exact selected readonly instance operation. Ordinary method visibility does
not expose a user-callable destruction alias. Preserve those rules and the
[retained-contract boundary](../specs/language/ownership-and-cleanup.md#retained-contracts-and-independent-admission).
No normative text is replaced.

The missing decision is how bounded, closed, nongeneric private type
expectations retain selected authored Cleanup declarations, including private
methods, without provider source. Structs already carry a Cleanup name but the
producer/reference path requires an ordinary export; class rows have no
selector. This proposal composes the unapproved
[CH109 ownership candidate](private-class-handle-ownership-persistence.md);
neither candidate supplies the other's approval or runtime support.

| Inspected source | Persistence consequence |
|---|---|
| [`BoundProgram.cs`](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs), `BoundClass`, `BoundPrivateClassSnapshot` | The working tree adds nullable `HasCleanup` and `CleanupMethod` class facts alongside nullable `IsUnique`. Original class snapshots retain type/origin tables, not selected method contracts or method maps; the private wire still has no class Cleanup selector. |
| [`Binder.cs`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.cs), Cleanup registration; [`SemanticVerifier.cs`](../../sources/Terrum.CodeAnalysis/Semantics/SemanticVerifier.cs), Cleanup checks | The working tree now binds class Cleanup identity and accepts the accepted ordinary visibility for class/struct methods; mandatory class-selector/function checks and source regressions are added but unverified. These edits do not establish class retirement or runtime support. |
| [`ModuleCompiler.cs`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.cs), `Write`, `ReadStructures`, `ReadClasses`, `PutFunction`, `ReadFunctions`, `GetFunction` | Structs serialize a presence Boolean plus a Cleanup **name**; Functions carry `IsCleanup` and declarations carry `\|Cleanup`. Pre-CC110 class rows do not carry a Cleanup selector. Bodyless declarations rely on retained declaration contracts. |
| Same file, `CreateProducer`, `ImportDeclarations`, `CreateReference`, `SameStructure`, `SameClass`, `Link` | Structs retain cleanup callees but require an ordinary exported Cleanup method for exposed unique types; this conflicts with accepted ordinary visibility. Reference projection ordinarily selects exported functions. Class rows have no selected-method edge. A retirement-only declaration role must cover both the existing struct name and the new class selector without making either method ordinarily callable. |
| [`ModuleCompiler.PrivateNominalOrigins.cs`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.PrivateNominalOrigins.cs), reference snapshots, provider checks and remapping; [`CompilationReference.cs`](../../sources/Terrum.CodeAnalysis/Semantics/CompilationReference.cs) | Original using/provider views and type maps exist, but lack the class-selected declaration closure needed for comparison and re-emission. |
| [`ModuleCompiler.ModuleSets.cs`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.ModuleSets.cs); [`ModuleCompiler.Graph.cs`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.Graph.cs) | Sets/graphs merge classes through `SameClass`; graph libraries have a separate reader calling `ReadClasses`. Existing graph struct Cleanup origins remain the struct carrier; the new class selector needs its own origin/provider mapping. Both must exclude retirement-only declarations from ordinary exports. |
| [`NativeModuleCompiler.cs`](../../sources/Terrum.CodeAnalysis/Modules/NativeModuleCompiler.cs), [sets](../../sources/Terrum.CodeAnalysis/Modules/NativeModuleCompiler.ModuleSets.cs), [graph packages](../../sources/Terrum.CodeAnalysis/Modules/NativeModuleCompiler.GraphPackages.cs) | Outer-3 TNPK/TNSP/TNGP parse embedded semantic Contracts. Set dependency accounting currently uses function declarations; graph `GraphTypeOrigin`/Cleanup origins describe **structures**, not class methods. Neither replaces an inner class Cleanup reference. |
| [`NativeManagedClassRuntime.cs`](../../sources/Terrum.CodeAnalysis/Native/NativeManagedClassRuntime.cs) | Normal-backing admission rejects Cleanup functions. A new persistence carrier must not bypass that gate. |

These are source observations, not newly executed validation results.
[Private persistence](../specs/representation/persistence.md),
[exact versioning](../specs/representation/format-versioning.md),
[binding/replacement](../specs/representation/binding-and-replacement.md) and
[consumer verification](../specs/representation/reader-trust.md) govern.
The [general ownership codec](../specs/representation/logical-records.md#owning-fields-and-cleanup-encoding-pending)
and Standard TRRM's narrow Cleanup clause are not this private encoding.

## Recommendation and exact selection

| Direction | Tradeoff |
|---|---|
| Integrate into unpublished CH109 | A concrete alternative is `terrum/private-semantic-109-class-handle-ownership-2`, still i32 109, adding the class selector and hidden retirement-only declaration semantics for both class and struct Cleanup. This is permissible, not a compatibility violation, but replaces the whole CH109 agreement and combines its ownership-byte review with the broader declaration/reference closure. `-1` would not be an alias. |
| **Separate exact successor — recommended** | Select **`terrum/private-semantic-110-class-cleanup-1`**, abbreviated **CC110**. It composes exact CH109 `-1`, adds the class selector, and defines retirement-only declaration handling for class and existing struct Cleanup relations. Struct record bytes remain unchanged. This separates the preservation/checking obligation while leaving CH109's recommendation intact; it does not stabilize unpublished bytes. |

CC110's complete base is exactly
`terrum/private-semantic-109-class-handle-ownership-1`, itself exactly
[`terrum/private-semantic-106-raw-union-return-coverage-2`](private-raw-union-return-coverage.md#exact-selection-and-framing)
plus CH109's mandatory ownership Boolean and obligations. Retain the bounded
foreign-interface directory count and foreign-member discriminator as zero.
Preserve the reconciled [106 declaration order](private-generic-method-persistence.md#exact-base-and-header):
checked output states, return coverage, CopyOrigin, foreign-member variant,
expected-direct descriptor, expected-vtable descriptor, conditional Function.
That link clarifies the inherited order; **G107 and G108S are not composed**.
All other directories, fields, type codes and operation tags remain unchanged.

Select the identifier ordinally/case-sensitively before schema-dependent
decoding. Write `TRRM`, then little-endian **i32 110**, bytes
**`6E 00 00 00`** at offset 4, not Standard's u16 version pair.
The assignment is a candidate, not an implemented constant or stable registry
allocation. Every participant on this selected route, including class-free
producers, consumers, graph libraries and supplied providers, selects CC110.

104, 106, CH109, the alternative CH109 `-2`, G107/G108S and Standard schemas
are not aliases, decoder retries or implicit conversion inputs. Equal magic,
profile labels or plausible class extents do not select CC110. Do not patch a
header or upgrade an older contract to CC110. Separately selected older routes
remain independent; exact pre-CC110 versions define class Cleanup as
unsupported.

## Host schema selection

The current public private-module APIs and `compile-module` CLI do not expose a
private schema selector. CC110 therefore requires a new exact-selection input,
not feature-triggered revision selection:

* Add exact-identity overloads:

  ```csharp
  ModuleCompiler.CreateProducerWithSchema(
      BoundProgram program, IEnumerable<string> exports,
      string schemaIdentity, PrivateModuleIdentityInputs? identityInputs = null)
  ModuleCompiler.CreateConsumerWithSchema(
      BoundProgram program, byte[] producer, string schemaIdentity,
      PrivateModuleIdentityInputs? identityInputs = null)
  ```

  Corresponding `CreateConsumerSetWithSchema`, `CreateGraphLibraryWithSchema`,
  and `NativeModuleCompiler` creation APIs carry the same `schemaIdentity`.
  Readers with an explicit expected identity require it to equal the embedded
  exact selector. Existing overloads retain their current version-selection
  behavior but never select CC110 automatically or fall back from an explicit
  CC110 selection.
* Add `--private-schema <exact-schema-identity>` to `terrumc compile-module
  producer|consumer`. The flag is required to write or consume CC110;
  consumer selection must match the producer and every selected provider.
  The existing private-identity manifest continues to identify source and
  nominal rows; neither its `usingModuleIdentity` nor its presence implies
  CC110. This proposal adds no manifest-v1 alias or default.
* Artifact-only link, run and validation routes dispatch on the exact embedded
  i32 revision and reject unsupported revisions. An optional expected-schema
  argument, when supplied, must equal that identity; no sniffing or retry.
  Native containers continue to select outer v3 separately from inner CC110.

These are candidate API/CLI contracts for review. Until implemented, no
current command or API writes revision 110.

## Exact class record and shared retirement role

Keep the class directory after Structures and before ImportedStructures.
Add one mandatory **i32** immediately after CH109's `isUnique`:

```text
i32 classCount
Class classes[classCount]

Class:
    str name
    i32 size
    u8 isInternal
    u8 isMutable
    u8 isUnique
    i32 cleanupDeclarationIndex       // NEW
    i32 fieldCount
    ClassField fields[fieldCount]

ClassField:                           // unchanged
    str name
    i32 type
    i32 offset
    i32 size
    u8 isPrivate
    u8 isInternal
    u8 isMutable
```

`cleanupDeclarationIndex` is **zero-based in this artifact's complete
ModuleFunction declaration table**, including bodyless declarations:

* **`-1`** (`FF FF FF FF`) means known **no authored Cleanup**. Derived
  field/descriptor retirement and the object's obligation are not absent.
* **`0..declarationCount-1`** names exactly one selected declaration.
  Forward references are ordinary: finish decoding declarations before
  resolving/checking this relation.
* Every other value is invalid. There is no unknown wire value, presence
  Boolean, optional trailer, name fallback or per-qualified-handle duplicate.

The delta from CH109 is exactly `4 * classCount` bytes, apart from declaration
closure that must now be retained. No new declaration field, table, Body tag
or Standard Cleanup clause is allocated. Integers remain little-endian;
`str` remains a nonnegative i32 UTF-8 byte length followed by strict UTF-8,
without padding/terminator, at most 1,024 bytes. Booleans remain exactly 0/1.
Retain all name/layout checks, i32 class geometry, 256 classes, 32 fields per
class, 1,024 declarations, and inherited node/depth and 4 MiB artifact limits.
Check counts, remaining extents and arithmetic before allocation.

The selector belongs to the **class declaration**, independently of
`isUnique`, mutability and visibility. Both aliasable and unique classes may
select Cleanup. All four existing class-qualified bands resolve to that one
row; they do not acquire four obligations. This adds no GC-qualified type code.
For a struct, retain its existing selected `CleanupMethod` name and structure
record bytes. Resolve that name to exactly one `IsCleanup` declaration in the
same Module; under CC110 it may be a retirement-only proxy rather than an
ordinary export. A class selector and a struct Cleanup name must each select
one distinct declaration owned by exactly that nominal type.

## Selected declaration validity and access

Before affected use, writers and consumers establish all of the following:

1. The target is a synchronous/nonsuspending, readonly instance void
   method of **that exact class or unique struct**: no explicit parameters, constructor,
   class constructor, accessor, static, async, authored extern, native-callable
   or caller-unsafe role. In the existing bound Function shape it has
   `ParameterCount=1`, solely the implicit readonly receiver of the exact
   selected nominal owner (canonical class type `1024+classIndex`, or the
   unique struct type); this is not a new source or ABI formal. The receiver
   is neither init/out nor a checked-reference-to-handle slot.
2. Its decoded owner, receiver and signature agree with its declaration
   Contract; with a Function present, its name/flags/signature also agree.
   The stored Contract has exactly the existing `Cleanup` marker; when a
   Function body is present it also has `IsCleanup=true`. A marker or method
   spelling alone is not selection. Preserve all other supported effects,
   failure and dependency contracts; Cleanup does **not** imply Pure,
   no-throw or an empty body.
3. Each class selector and each existing struct Cleanup name resolves to
   exactly one declaration owned by that nominal type. Reject dangling,
   wrong-owner/wrong-kind, unselected flagged declarations and one declaration
   selected by two owners.
4. An owned implementation retains a real Function/body for its selected
   method and its required implementation closure. Its declaration is local,
   `Import=false`; it is not an ordinary function export or graph export.
   Public/private/internal **method visibility remains its authored fact**,
   not a requirement to publish an independently callable destructor.
5. An imported class or struct expectation retains a **retirement-only proxy**:
   `Import=true`, `hasFunction=false`, with the complete original declaration
   contract and an owner whose nominal origin is `ImportedExpectation`.
   The class selector or existing struct Cleanup name identifies this role
   without a new flag. Such a proxy is not authored extern, a construction pair
   or an ordinary function import resolved by name/export key. A locally owned
   class or struct cannot use it to omit an implementation.

Retirement-only declarations are hidden from ordinary source lookup, overload
sets, ordinary call/address/native-export paths and independently callable
export lists, regardless of visibility or the overloaded `Import` bit.
The owned class export retains the operation through its selected relation;
it needs no separate method export key. Accepted Cleanup visibility permits
ordinary visibility for both class and struct methods. This class-record
proposal does not alter the separate struct wire carrier; the existing
public-only struct binding/export restriction is an implementation gap, not a
source-contract requirement or Cleanup callability grant.
Existing declaration framing remains: consumer/graph imports are bodyless;
local definitions have Functions; producer declarations have Functions.

## Original expectations and replacement

Resolve the exact class or struct Module/declaration/export origin and kind
first, preserving CH109's known class-ownership equality and all existing
nominal checks. Then compare **every original using-context expectation**,
including private, nested, type-only and uncalled ownership roots:

* Authored versus derived selection must agree in both directions. Adding or
  removing authored Cleanup is not harmless strengthening.
* For two authored selections, follow each class's class-row selector or
  each struct's existing Cleanup name. The proposed member correspondence is
  the **resolved nominal owner plus its selected Cleanup role**, not row
  equality, ordinary member lookup, matching signatures or a guessed method
  name. Within each snapshot the exact selected declaration remains
  authoritative. Local names/positions may be rebased; any other retained
  reference to that declaration must agree with this correspondence.
* Compare those selected declarations' complete receiver/callable shape,
  retained visibility, supported effects, dependencies and independent
  requirements in their original type contexts under the inherited supported
  binding rules. Unsupported comparison refuses; no new effect implication
  engine or body-equality requirement is selected. A replacement's private
  body may change while satisfying the original contract.
* Keep original expected selection, declaration contracts and origin separate
  from the selected actual provider and additional facts relied upon.
  Successful replacement must not overwrite the old expectation, import a
  missing one from the new provider or redirect cleanup to an intermediate
  forwarding module.

Extend `SameClass` and `SameStructure`'s comparison graphs and every
provider/snapshot/set/graph check to include the selected method edge,
including recursively reached classes/structs. Deduplicate agreeing
same-owned-origin nominal types and methods only after complete comparison;
retain each using expectation even when an actual row is shared. Conflicting
diamonds, ownership or selected-method maps refuse. Cached checks are
invalidated by changes to schema, snapshots, selections or binding context.

## Retention, remapping and containers

The host needs three distinguishable class states: **unavailable**, **known
derived** and **known selected declaration**. Host-created or otherwise
unprofiled class descriptors with no carrier are unavailable, not known
derived. Exact private 104/106/CH109 selections define class Cleanup as
unsupported; their writer must reject unknown/selected Cleanup facts and
their reader may record known derived retirement for that exact schema. This
does not upgrade those inputs to CC110. Struct Cleanup continues to use its
existing explicit nullable name field. Every retained CC110 class requires a
known selector even without a Body use. Unknown refuses publication or
affected binding; pruning is lawful only when the nominal type and its
surviving obligations can actually be removed.

Carry class availability/selection through `BoundClass`, `BoundProgram`,
Module and `CompilationReference` projection, shared traversal and manual
rebuilding. Extend `BoundPrivateClassSnapshot` and structure snapshots with
their original selected-declaration contracts and type/origin closure, and
class/structure reference mappings with checked expected-to-current
**declaration maps**. Current type-only snapshots are insufficient. Bodyless
hidden signatures must survive independently of the ordinary source-visible
`CompilationReference.Functions` export list. A numeric host carrier is valid
only paired with its owning declaration-table context; source Function,
ModuleFunction and projected-reference indexes are not interchangeable.

Source-free transformation must:

1. Allocate checked per-input class/structure and declaration maps before
   fixing references. Remap the class selector through the declaration map;
   resolve the existing struct Cleanup name to the corresponding declaration.
   Never use a class map or body-only function list as a declaration map.
2. Preserve original snapshots/maps separately from actual-provider maps.
   Rewrite all four class bands, receiver/field types, admitted nested type
   constructors, declaring owners, both Cleanup selectors, declaration
   Contract grammar and Body references in the correct context. Renaming a
   selected declaration changes all its local associations coherently, not
   matching numeric words/bytes.
3. Retain selected methods as metadata dependencies even without a machine
   call. Owned output retains their Bodies and required callees; reference
   output retains their original bodyless contracts. Dropping source/debug
   information or ordinary callable exports must not drop this closure.
   Existing struct Cleanup names and executable cleanup indexes keep their
   own remapping; the new class selector does not automatically remap either.
4. Rebuild records, declaration counts/order, references, containing lengths,
   offsets and hashes. Reopen/rebind the result without source. Do not turn
   an imported expectation into an owned class or synthesize an empty method.

Apply the same rules to ordinary producer/consumer readers, the separate graph
library reader, reference sets and graph merging. For native wrappers select
**outer version 3 / inner CC110**; no outer fields change. Semantic dependency
accounting must preserve retirement-only edges separately from actual machine
import/export tables. Existing structure graph origins remain the struct
carrier; the class selector needs its own origin mapping. A route unable to
retain or validate a required edge
must refuse, not drop it or invent a callable native import.
Checksums/profile labels do not establish this contract. Re-encoding a Contract
requires rebuilding wrapper extents/hashes, not patching an unchanged image
into compatibility. Native/profile admission remains independent.

Malformed framing, selectors, role/owner/signature contradictions and maps
use existing `ModuleFormatException` handling. Missing required facts/closure,
incompatible replacement or unsupported module consumption uses
`ModuleContractException`; source and native-support refusals retain their
existing channels. No partial successful reference/output or decoder fallback.
Consumers owe structural, support and binding checks, **not** semantic re-proof
of a structurally valid producer's published Cleanup/body facts.

## Required tests and remaining gates

These are required future cases, **not passing-test or runtime claims**.
Use public compiler APIs and independent encoded fixtures alongside round
trips, extending the [private class fixtures](../../tests/Terrum.CodeAnalysis.Tests/Modules/PrivateNominalClassPointerFieldTests.cs)
and existing module/set/graph fixtures. Keep unrelated native execution out of
the persistence gate.

| Area | Required cases/outcome |
|---|---|
| Positive selection | Unrelated aliasable/unique, mutable/immutable classes and unique structs, each with derived cleanup or one arbitrarily named authored method. Public, internal and private methods retain selection without ordinary callable exposure. Qualified class uses share the same row; struct record bytes do not change. |
| Exact bytes/schema | Independent class `-1`, first/last declaration and forward references at the specified position; class-free CC110 stays revision 110 and struct record bytes stay unchanged. Reject truncated i32, values below `-1`, index equal to count, extreme integers, invalid Boolean/UTF-8/counts, trailing bytes and wrong exact schema/header. Exact 104/106/CH109 readers yield known-derived class Cleanup; they are not CC110 aliases or auto-upgrade inputs. |
| Method validity/access | Reject wrong class/struct owner, wrong receiver/qualification/result/arity, static/constructor/accessor/foreign/unsafe role, declaration/Function disagreement, duplicate/orphan selection and a bodyless owned target. Reject ordinary calls, addresses and export aliases even for public selected methods. Accept correctly bound hidden class/struct proxies; they never create a construction pair. |
| Replacement | Both authored/derived changes fail; wrong Module/key/owner, incompatible selected contracts/effects/dependencies and ambiguous member maps fail. Compatible provider body/index/name rebasing preserves each class selector or struct Cleanup-name expectation; a later incompatible provider still fails. Include nested and uncalled/type-only roots. |
| Sets/graphs | Independent providers use colliding class/declaration rows and struct Cleanup names; reverse enumeration and reorder tables without capture. Agreeing diamonds share actual rows but keep all expectations; conflicting selections fail. Retain metadata-only cleanup/provider edges and exercise the separate graph reader. |
| Source-free persistence | Remove source/debug, project class and struct Cleanup contracts, link/re-emit/reopen producer and consumer closures, reference sets and graph libraries. Preserve selections, original declarations/origins/maps and inherited struct cleanup independently. Outer-3 readers either retain the exact CC110 relations or explicitly refuse unsupported use; no synthetic native thunk. |
| Boundaries/isolation | Class rows 0/255, 256 versus 257 classes; declaration rows 0/1023 and inherited declaration/field limits; combined closure and 4 MiB exhaustion, checked extent overflow and stale/incomplete maps. Preserve owned construction-pair checks and unsupported runtime/native-boundary refusals. |
| Trust | Malformed references and incompatible providers require rejection. A semantically false but structurally consistent producer assertion belongs to an optional truth audit, not a newly mandatory source/Body re-analysis. |

**Open for approval:** CC110, its candidate revision/selector, and the proposed
retirement-only declaration role shared by class selectors and existing struct
Cleanup names. The CH109 `-2` integration remains an alternative for review,
not another accepted spelling.

**Implementation prerequisites/gates:** the source Cleanup carrier/binding and
mandatory selector checks are in progress and unverified. Availability-aware
original method snapshots, exact codecs and binding/remapping routes remain
open; class retirement, effects and runtime execution remain separate gates.
This document changes none of their approval or validation status.

**Explicit exclusions:** no change to complete-receiver checks, one-object
obligations, body-then-fields order, exceptional behavior, backing-dependent
trigger timing or field retirement; no new call syntax or destruction alias;
no allocation/GC/root/header/scheduler/code-lifetime mechanism, calling
convention, native symbol, ABI or runtime behavior decision. Generic/static/
union class publication, boxes, Standard TRRM codecs and G107/G108S composition
remain separate. In particular, normal-backing Cleanup runtime admission is
still an open implementation gate, not established by this proposal.
