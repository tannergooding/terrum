---
name: terrum-example-writing
description: Write, revise, or check Terrum code examples in documentation, proposals, samples, and library sketches. Use when showing Terrum syntax or explaining its contracts, including initialization, references, ownership, native FFI, and generated metadata.
---

# Terrum example writing

Use repository-relative paths below. Follow `.github\copilot-instructions.md`;
this skill supplies the example-writing workflow, not new language rules.

## Establish the example's contract

1. Identify the audience, destination, behavior being illustrated, and whether
   the source is intended to execute. Keep approval and implementation separate:
   an accepted contract can be unimplemented, and an implemented prototype can
   remain unapproved. State those distinctions where they affect the example.
2. Read the relevant productions in `docs\specs\grammar-spec.md` and the focused
   chapters linked from `docs\specs\README.md`. Check
   `docs\implementation.md`, nearby compiler code, and active tests for support.
   Read the governing proposal and explicit user decisions when illustrating
   unsettled behavior. Existing prose and examples are evidence, not authority
   over the governing contract.
3. Find a nearby example with the same mechanism. These are useful starting
   points, not proof that adjacent features work:

| Subject | Starting evidence |
|---|---|
| Runnable scalar source | `samples\native\factorial.terrum` and related CLI tests |
| Caller-unsafety | `tests\Terrum.CodeAnalysis.Tests\CallerUnsafeTests.cs` |
| Initialization outputs | `tests\Terrum.CodeAnalysis.Tests\PublicInitReferenceTests.cs` |
| Reference and pointer authority | `docs\specs\language\references.md` |
| Ownership and cleanup | `docs\specs\language\ownership-and-cleanup.md` |
| Layout and foreign calls | `docs\specs\data-layout-and-abi.md` and `docs\specs\targets\windows-x64-call-interface.md` |
| Standard metadata | `docs\specs\representation\README.md` and its referenced registry/contracts |

Parser fixtures in `samples\syntax` do not establish executable support.
Design samples, library sketches, excluded tests, and private persistence
paths do not establish accepted syntax or standard TRRM support.

## Write the smallest complete example

- Use Terrum productions and APIs, not C# spellings or familiar platform APIs
  imported by analogy. Check modifier placement, generic qualifiers, call
  arguments, construction, and declaration locations against the grammar.
- Keep descriptor rebinding, referent write authority, vacant initialization,
  ownership transfer, and backing lifetime distinct. `mut` and `write` grant
  different permissions. `init ref<T>` is an initialization-destination
  parameter mode, not a general spelling for a writable foreign pointer.
- Show the declarations, dependencies, output relation, and cleanup required
  to understand the behavior. If a fragment omits setup, identify it instead
  of presenting the fragment as a self-contained executable.
- For proposal-only forms, label the proposed syntax or metadata explicitly.
  Do not invent attributes, APIs, qualifiers, or numeric record assignments
  to fill an unresolved contract. Surface the actual missing decision.

### Native examples

- Raw foreign imports and native-callable declarations are caller-unsafe,
  including scalar-only signatures. An `unsafe` declaration propagates that
  obligation; its body still needs explicit unsafe blocks or expressions.
  `NoThrow` does not remove caller-unsafety.
- Boundary data needs the selected target's directly interoperable `Blittable`
  representation. Show stable backing and any explicit pin dependency;
  introduce no implicit marshalling, rooting, conversion, or lifetime extension.
- Use raw `ptr<T>` parameters for foreign pointer signatures. A checked
  `ref<T>` is not a raw pointer or a pin; `ptr<T>` and `pin ref<T>` are not
  aliases. Preserve dependencies through admitted conversions. Do not infer
  pointee initialization, retention, ownership, or complete C const/direction
  mappings from the pointer spelling.
- Derive output validity and cleanup from the provider contract on each
  outcome. Do not read, adopt, or release a failure output without evidence
  that the operation is valid. A caller-selected IID plus `void**` does not
  establish a fixed interface; typed acquisition must fix or validate the IID.
  Safe wrappers are ordinary authored Terrum code, not compiler-generated
  application policy.

## Check the claim being made

- For implemented examples, check the exact final source through the existing
  compiler APIs or CLI path appropriate to that feature. Use
  `docs\building.md` and the existing NUnit runner; confirm filters selected
  active tests in `tests\Terrum.CodeAnalysis.Tests\Terrum.CodeAnalysis.Tests.csproj`.
  Do not change compiler behavior merely to make a documentation example pass.
- `terrumc check` establishes syntax only; `analyze` establishes binding.
  Emission is separate from execution. Use the built `terrumc.dll` for machine
  protocols, and supervise any native execution under the existing test gates.
  Never launch visible UI as an incidental example check.
- A current compiler rejection does not invalidate accepted but unimplemented
  target syntax. Review those examples against their governing contracts;
  label the implementation gap rather than substituting legacy syntax.
- For metadata explanations, connect source declarations to the actual
  signature, effects, output, ownership, and target records. Distinguish logical
  requirements, assigned standard encodings, private formats, and proposed
  records. Do not claim byte-level or dependency support without its evidence.
- Present the example with a concise purpose, its support/approval boundary,
  and the explanation needed to understand it. Use tables for independent
  contract axes or metadata mappings. State material validation gaps without
  reproducing logs or implementation history.
