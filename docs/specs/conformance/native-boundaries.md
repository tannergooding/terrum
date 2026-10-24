# Native boundary cases

These cases apply to the accepted [native contract](../native.md), the
[standard metadata schema](../il-metadata.md), and the selected target's
complete [layout and ABI](../data-layout-and-abi.md). Source acceptance,
metadata decoding, emission and execution are distinct claims. A target
implements the ABI it claims to support or rejects the affected use; the
cases do not prescribe a universal Terrum calling convention.

## 1. Declaration roles and resolution

| Case | Fixture | Required outcome |
|---|---|---|
| NBD-01 | Static bodyless callable `NativeImport` with omitted mode | Select `Link`; preserve its library identity and exact declaration/symbol binding |
| NBD-02 | Static callable or directly addressable static-field import with `Dynamic` | Use the selected target loader; retain the provider for the importing module/image lifetime |
| NBD-03 | Dynamic import with omitted or explicit `ResolutionPolicy` | Select the governing rule edition's `Default` or the exact requested policy; policy is per import and is not a security guarantee |
| NBD-04 | Nondefault loader policy on a Link import | Reject; Link uses configured linker semantics and has no loader policy |
| NBD-05 | Omitted `EntryName` or an explicit ASCII symbol name | Use the exact MethodDef/FieldDef name by default; do not infer decoration/suffixes; reject unencodable default names |
| NBD-06 | Instance method or instance field declared as `NativeImport` | Reject; expose instance-oriented behavior through an authored static wrapper with an explicit receiver |
| NBD-07 | Static callable import's signature contains scalar, pointer, function-pointer or directly interoperable `Blittable Copy` data | Admit only when the complete selected target mapping exists; no implicit marshalling, rooting, pinning or ownership |
| NBD-08 | `NativeAddress` is formed from a callable import without `NativeCallable` | Permit the native code address; retain caller-unsafety and provider-lifetime obligations |
| NBD-09 | Static method or directly addressable static data field has `NativeExport` | Emit the explicitly named/defaulted OS-visible symbol; ordinary Terrum visibility or `.trrm` `Export` alone does not do so |
| NBD-10 | Instance method/field or non-static data export is requested | Reject; instance methods require an authored static wrapper; instance fields are not exported |
| NBD-11 | Field-form `NativeExport` or field import is read/written | Access the provider's actual storage; data has no calling convention or callable implication |
| NBD-12 | Read-only import binds a mutable provider, or writable import binds a read-only provider | Permit the first without upgrading caller authority; reject the second |
| NBD-13 | `mut` is treated as hardware read-only protection | Reject that interpretation; it is a Terrum access contract and does not prevent unsafe native writes |

## 2. Callable identity and interface calls

| Case | Fixture | Required outcome |
|---|---|---|
| NBC-01 | A method has one `NativeCallable` contract | Bind its native-callable role and effective convention on that exact MethodDef |
| NBC-02 | An interface supplies a `NativeCallable` default | Apply it to methods declared there; inherited slots retain their declaring method's convention |
| NBC-03 | A declared method overrides its interface's default convention | Use the method's effective contract for that slot; do not rewrite inherited slots |
| NBC-04 | A method-form `NativeExport` names its MethodDef | Require and reference that method's unique `NativeCallable` clause; do not duplicate the convention |
| NBC-05 | A field-form export or callable import is examined for a `NativeCallable` clause | Do not require or infer one for those roles |
| NBC-06 | Native interface dispatch is requested with only `Layout(Blittable)` or only `NativeCallable` | Reject the missing half; vtable use requires both, while `NativeCallable` alone may still make a method addressable |
| NBC-07 | An abstract interface method's address is requested, or a concrete implementation satisfying its slot is addressed | Reject a standalone abstract code address; allow a resolved implementation address only under its complete contract |
| NBC-08 | `NativeCallConv` is applied immediately before `fnptr` | Include the closed enum value in complete function-pointer type identity; do not apply it to `func` |
| NBC-09 | Function pointers have different convention identities that map to the same physical target ABI | Keep their Terrum types distinct; no implicit ABI cast |
| NBC-10 | A bodyless, non-exported MethodDef anchors a native function-pointer signature | Resolve the TypeSpec directly to its complete MethodDef contract; no separate signature-table row or name/row-identity semantics |
| NBC-11 | Member calls include a receiver, hidden result storage, explicit arguments, or interface adjustments | Follow their order, registers, stack slots, adjustments and thunks from the selected target ABI and effective convention; reject unsupported mappings |
| NBC-12 | A compiler assumes one universal `this`/result/argument order or invents a Terrum stack-walk convention | Reject; call layout and unwind behavior follow the target ABI, including only ABI-permitted optimized/leaf departures |

## 3. Layout and direct representation

| Case | Fixture | Required outcome |
|---|---|---|
| NBL-01 | A type has a default Layout and exact TargetProfile alternatives | Each alternative owns a complete ordered field list; an exact registered profile selects at most one and replaces the complete default payload |
| NBL-02 | No exact profile alternative exists | Use the target-neutral default; do not follow a profile set, sparse merge or fallback chain |
| NBL-03 | A producer serializes aggregate size, physical alignment or field offsets | Reject the extra derived data; selected target ABI derives physical size, final alignment and offsets from declared inputs/fields |
| NBL-04 | Native interface uses multiple inheritance or subobject adjustment | Follow the selected ABI's base/vptr/adjustment rules; do not flatten slots by a universal rule |
| NBL-05 | Rootless boundary contains a `Blittable Copy` aggregate of any admitted size | Do not impose an artificial aggregate-size cap; require a complete target mapping and reject unsupported representations |
| NBL-06 | A foreign signature contains a managed/owning reference, text/array, or implicit conversion | Reject absent an independently admitted representation; no marshalling, pinning, rooting, lifetime extension or cleanup is inferred |

## 4. Pointer, dependency and lifetime boundaries

| Case | Fixture | Required outcome |
|---|---|---|
| NBP-01 | Raw native pointer or function-pointer type is used | Preserve caller-unsafety and ordinary provenance/access/lifetime obligations; the type adds no native-specific extent, alignment, output or retention contract |
| NBP-02 | An import's pointer parameter or result is assumed to initialize, retain, own or validate a pointee | Do not infer the fact from pointer spelling or the NativeImport row |
| NBP-03 | An ordinary pre/postcondition expresses an admitted result relationship | Use the shared predicate/dependency contract; otherwise require an authored wrapper/source generator or decline unsupported safe use |
| NBP-04 | NativeOutput/NativeParameter clauses or a native-output Dependency destination are supplied | Reject under the selected schema; no legacy alias or implicit pointer semantics are assigned |
| NBP-05 | A Dynamic import is used through an escaped raw `fnptr` | The importing image retains its provider; the unsafe holder remains responsible for ensuring any separately selected provider is live at use |
| NBP-06 | Safe `Func` or generated code retains a code module | Preserve the code-module dependency; do not unload while methods are active or live safe callable values require it |
| NBP-07 | Copy/Unique, field mutability, backing, or cleanup facts are expected from a native role | Read them from their shared type/ownership contracts; native metadata does not synthesize them |

## 5. Native exception boundaries

| Case | Fixture | Required outcome |
|---|---|---|
| NBX-01 | A native exception unwinds inward from an import with Feature `NativeExceptionUnwind` and complete target/runtime support | Normalize the event before source matching; preserve the active operation failure and do not publish an unrelated status/default result or output |
| NBX-02 | Native exception unwinding is requested without its Feature or runtime support | Reject affected use; runtime-policy values and calling-convention identity do not activate it |
| NBX-03 | Event has a dedicated Terrum mapping | Catch the existing dedicated exception class; do not match it as `NativeException` |
| NBX-04 | Admitted event is unmapped | Materialize an opaque `NativeException` implementing `IException<NativeException>`; both `catch (NativeException)` and `catch (IException<_>)` match |
| NBX-05 | Foreign event contains producer-language object data or text | Do not decode/copy it; preserve only Terrum-owned diagnostic identity/data |
| NBX-06 | NativeException is materialized | Expose read-only `TargetProfileId: u32`, `TargetProfileRuleRevision: u32`, `EventDomain: Text`, `EventCode: nuint`, and Terrum-owned `Message`; use `A native exception occurred.` when no more descriptive Terrum-owned text is available/warranted |
| NBX-07 | Caller-unsafe `GetNativeContext()` is unavailable for the target/event | Return `null`; a non-null pointer's availability, lifetime and copy requirements follow the selected ABI |
| NBX-08 | NativeException is retained after handling | Preserve ordinary exception-object identity/lifetime without extending a borrowed native-context pointer's validity |
| NBX-09 | Native exception would unwind outward through a native frame, or Terrum exception reaches a reverse boundary | Contain reverse-boundary propagation; do not expose a foreign exception-object ABI |
| NBX-10 | Local mapped exception is explicitly caught in unsafe source without a runtime-policy opt-in | Permit under ordinary exception semantics; if unhandled, apply the module's fail-fast policy |
| NBX-11 | Compiler-invoked owner Cleanup runs during normal retirement or throws | Invoke only on normal retirement; propagate a thrown failure and do not resume generated field cleanup during that unwind |
| NBX-12 | Exceptional unwind crosses an owner that has Cleanup | Do not run compiler-generated owner Cleanup; authored crossed `finally` blocks perform explicit exceptional cleanup |

For Windows x64 event mappings, stack-overflow recovery and OS unwind evidence,
see the [target cases](target-windows-x64.md). The selected target ABI/runtime
defines which native exceptions can cross the boundary; support is qualified
for targets that claim it, without requiring an exhaustive platform list.

## 6. Metadata producers and consumers

| Case | Fixture | Required outcome |
|---|---|---|
| NBM-01 | Required NativeImport/NativeExport sections occur beside ordinary `.trrm` Import/Export sections | Keep native symbol binding distinct from Terrum module-link keys |
| NBM-02 | NativeImport/NativeExport has a tagged owner | Validate the exact static MethodDef/FieldDef kind and owner reference; reject owner-kind mismatches and duplicate owners |
| NBM-03 | NativeExport method references its callable contract | Resolve the exact unique MethodDef-owned NativeCallable clause; data exports use no clause |
| NBM-04 | Layout/field/interface rows are reordered or remapped | Preserve complete typed ownership, declaration order and all references; do not byte-substitute or infer row identity |
| NBM-05 | TargetProfile reference has the right row number but wrong registered pair | Reject the profile mismatch; selector identity is the exact `(profileId, ruleRevision)` |
| NBM-06 | Producer emits a structurally valid artifact but claims unsupported native behavior | Consumer may rely only on validated supported contracts; an unsupported required capability declines before affected use |
| NBM-07 | Feature IDs 1-7 are present | Validate exact revisions and complete prerequisite closure; registration is not implementation, emission or runtime evidence |
| NBM-08 | Artifact uses retired native-output destinations or a different draft schema with equal format bytes | Reject under exact-schema selection; no old decoder, alias or version-byte fallback is required |

These cases are requirements, not results established by the specification.
The current prototype's native sections, `NoThrow` effects and
`checked-exceptions` policy remain implementation gaps until migrated and
qualified against this schema.
