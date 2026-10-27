# NativeImport LibraryName source contract

**Status: unapproved; provisionally actionable only under the user's explicit
missing-contract authorization:**

> At a genuine missing contract, prepare the smallest concrete decision needed as a new proposal doc using gpt-6.1-sol at max reasoning, then continue with it being treated as a provisional approval/direction.

This document supplies that missing decision, not normative adoption,
compiler support or approval of adjacent linker/loader designs. No normative
specification, code, registry or persistence revision changes here.

## Decision and baseline

Recommend one **nonempty printable-ASCII bare library name** value domain for
`NativeImport.LibraryName`, shared by Link and Dynamic and by callable and
static-data imports. Preserve its decoded spelling. Require neither a
particular suffix nor the prototype's five-character minimum or
256-character maximum. Reject source-authored paths rather than introduce
target-dependent path semantics through this argument.

The [source-schema candidate](native-calling-convention-source-schema.md#complete-nativeimport-schema)
already proposes a required compile-time string-literal argument, followed by
the selected metadata String encoding/extent checks. Retain that proposed
argument form; this document resolves its value validation, not the complete
Core registration or other arguments.

Preserve these accepted decisions:

* [Native declaration roles](../specs/native.md#native-declaration-roles)
  distinguish configured-linker Link, which may select static or shared
  inputs, from the selected target loader's Dynamic behavior. Link has no
  runtime loader policy. Windows Default uses
  `LOAD_LIBRARY_SEARCH_DEFAULT_DIRS`: application directory, System32 and
  registered user directories, without current-directory or `PATH` fallback.
  CompatibilitySearch retains ordinary unqualified loader search. Linux
  retains bare-name `dlopen` search, including process-start
  `LD_LIBRARY_PATH`, for both policies.
* Dynamic retains the provider library for the importing module/image
  lifetime, not a per-pointer unload lease. Exact symbol spelling,
  caller-unsafety and all independent boundary obligations remain unchanged.
* The accepted [NativeImport metadata row](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics)
  already requires a **nonempty ASCII** library String. Its general name
  rules already exclude embedded zero scalars and preserve ordinal decoded
  text without normalization. Unicode admission is therefore not an
  undecided extension to select here.

The new choices are the common bare-name domain and its exact printable
character restrictions. Extending Link's source argument to paths, or
extending the accepted ASCII domain, would be separate amendments.

## Exact candidate validation

Apply these rules to every authored import, including uncalled declarations:

1. Bind the required LibraryName argument under the registered schema's
   proposed positional/named and uniqueness rules. It must be an admitted
   string literal, not a runtime expression, concatenation or a reference to
   a string-valued constant. Decode it once using the language's checked
   literal decoding, not raw token spelling.
2. The decoded value contains at least one scalar.
3. Every scalar is in **U+0020 through U+007E inclusive**, except **U+002F
   (`/`), U+005C (`\`) and U+003A (`:`)**. The entire value must not equal
   `.` or `..`.
4. There is no further source alphabet, prefix, suffix or native-name length
   restriction. Selected serialization and supported-capacity checks remain
   separate, as below.
5. Retain the complete decoded sequence unchanged. Do not trim whitespace,
   fold case, normalize, interpret it as a command option, expand it in a
   shell, append an extension/prefix, extract a basename or resolve a path.
   A backend must transport it as one library-name operand, not splice it
   into command syntax.

Consequently, punctuation such as `+`, leading/trailing spaces, and a
one-character name are source-valid. A value consisting of a single space
is also source-valid; nonempty is not a request to trim. ASCII control
characters U+0000..U+001F and DEL U+007F reject. Non-ASCII text and ill-formed
scalar text reject, including when written using escapes. Both slash
directions and colon reject everywhere, covering rooted, relative,
drive-relative, UNC and URI-style source paths without host-dependent
parsing. Rejecting `.` and `..` excludes the two bare directory selectors.

These are not portable filesystem-validity or security guarantees. Other
reserved filenames, trailing dots, metacharacters and target limitations
remain matters for the selected linker/loader mapping. Linker input paths
and search directories can remain explicitly configured outside this
source argument; this proposal assigns no new configuration interface.

| Question | Decision |
|---|---|
| `.dll`, `.DLL`, `.lib`, `.a`, `.so`, versioned suffix or no suffix | Preserve exactly; no suffix is required, inferred, added or removed by Terrum. |
| Upper/lowercase | Preserve exactly in normalized facts and persisted text; do not lower-case for storage or deduplication. |
| Unicode beyond ASCII | Reject, preserving the accepted NativeImport field domain; another String encoding does not widen it. |
| Embedded NUL, including escaped NUL | Reject the whole value; never accept a prefix before the NUL. |
| `/`, `\`, `:` or the whole value `.`/`..` | Reject under the proposed common source contract, even if a particular linker could consume it. |
| Spaces and other admitted printable punctuation | Preserve literally, without trimming, quoting-as-data changes or Terrum-side expansion. |

Passing the exact name to the selected native API does not override that
API's already documented case, suffix or loaded-module behavior. In
particular, no claim that Windows distinguishes `probe` from `probe.dll`
is made. Native API behavior is not permission for Terrum to synthesize a
second spelling or try a second policy after failure.

## Distinct validity and support boundaries

| Boundary | Required check and failure |
|---|---|
| Source/schema binding | Check the literal form and decoded value above. An invalid argument diagnoses at the declaration and establishes no usable import. Do not defer a source error to a missing-library failure. This does not prescribe a diagnostic number or wording. |
| Standard NativeImport semantics | Preserve its existing nonempty ASCII field and the same normalized name. On eventual adoption, cross-reference the proposed value domain from the NativeImport row semantics; do not impose its bare-name rules on all String rows. A source-free consumer selecting that contract checks the decoded name before native use, without requiring the original literal or re-proving bodies. |
| Standard String representation | Use the existing `u8 flags, u24 unitCount`, counted content, encoding-width zero terminator and minimum four-byte-alignment padding. `unitCount` is at most `0xFF_FFFF`, excludes terminator/padding, and bounds the complete unsegmented value. Check row/index agreement and independent section/artifact byte bounds with checked arithmetic. Unrepresentable or malformed selected data cannot be emitted/consumed; do not truncate, segment or wrap a count. |
| Supported resource envelope | A smaller declared implementation capacity may refuse an otherwise valid/representable operation. Report that limit, not a new source rule or a portable minimum. Numerical portable interchange minima remain [unassigned](../specs/representation/resource-accounting.md#1-capacity-validity-and-portable-minima). |
| Current private Windows admission | Preserve its separately selected limits until explicitly changed. A broader source-valid name does not thereby become usable in the private pipeline. Decline the affected private operation with an implementation-specific admission/limit failure, rather than relabel its private DLL grammar as source semantics. |
| Target/backend support | Require an actual mapping for the chosen mode/policy and name. Source validity and metadata representability do not promise a library exists, that a target accepts its filename, or that native binding/emission/execution is implemented. Unsupported mappings or unresolved required bindings decline affected use under the existing native contract; no new runtime failure object or sentinel is assigned here. |

The standard String rules and lack of a unique canonical String encoding
remain owned by [metadata](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics)
and [encoding canonicality](../specs/representation/encoding-canonicality.md#1-ordinary-reading-writing-and-auditing).
ASCII is a constraint on this field's decoded content, not a new requirement
that every producer choose encoding tag zero. All four assigned encodings
can represent ASCII with one encoded unit per character. Their different
unit widths still determine terminator, byte extent and padding. No UTF
tag can authorize a non-ASCII library name.

A value longer than `0xFF_FFFF` admitted characters can satisfy the source
content rule but cannot be represented in the selected standard String
schema. A standard-producing compilation must refuse that output. Conversely,
a 257-character value is not source-invalid merely because a private native
descriptor currently caps names at 256.

### Current implementation evidence and required separation

This is inspected source evidence, not tests run or support claimed here:

* [`DirectNativeImportContract.IsValidLibraryName`](../../sources/Terrum.CodeAnalysis/Semantics/DirectNativeImportContract.cs)
  currently requires length **5..256**, a case-insensitive `.dll` suffix,
  and only ASCII letters/digits, underscore, dash and dot.
* [`Binder`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.cs)'s registered
  and legacy import decoders invoke that predicate during source binding.
  The shared descriptor validator, the
  [private native expectation validator](../../sources/Terrum.CodeAnalysis/Modules/NativeModuleCompiler.cs)
  and the [provisional native-import table reader](../../sources/Terrum.CodeAnalysis/Modules/TrRmNativeImportTable.cs)
  reuse it. Replacing it indiscriminately would widen unrelated private
  admission, not merely repair source validation.
* The [resolution-persistence candidate](native-import-resolution-private-persistence.md#unchanged-stored-directnativeimport-payload)
  independently retains the private inner i32-length UTF-8 String's
  1,024-byte limit, aggregate budgets, and the outer u16-length strict UTF-8
  String codec. Neither is the standard tagged String. Its existing native
  name limits and private Windows comparison rule remain private.

Implement a source/normalized-name check separately from the retained private
Windows admission predicate. Binding, shared traversal/rewriting, mandatory
verification, reference projection, persistence and dependency consumption
must preserve the exact decoded name, including after source removal.
Do not silently revalidate every source name with the private predicate.
Private target-specific comparison may retain its selected relation without
rewriting the original text or becoming a portable name-normalization rule.
Re-encoding remaps String references and rebuilds affected extents, indexes
and checksums; it does not edit matching name bytes in place.

## Material alternatives

| Alternative | Assessment |
|---|---|
| **One bare-name source contract for every mode** | Recommended. Completes validation once, preserves accepted Dynamic unqualified/bare-name behavior, and lets configured Link choose static/shared inputs without making every source import a Windows DLL. |
| Allow target/linker-specific paths for Link only | Coherent, but adds a mode-dependent domain and needs exact relative-path base, linker operand and retained/build-input rules. Not needed to close this gap; configure linker paths separately. |
| Allow paths for both modes and defer their interpretation to the target | Substantive redesign. Requires relative-path bases, drive/UNC/POSIX forms and interaction with loader policy and deployment. It would also revise the accepted Dynamic bare/unqualified mapping, rather than merely define its missing source validation. Not selected. |
| Promote the current DLL-only whitelist/256 limit to source | Rejects legitimate static/shared and non-Windows names, including `libstdc++.so.6`, for private implementation reasons. Not selected. |
| Accept every nonempty ASCII value and let targets decide all restrictions | Leaves path/control-character admission and phase of failure target-dependent. The explicit portable value domain above is the bounded decision instead. |
| Admit Unicode using UTF-8/UTF-16 Strings | Requires an intentional amendment to the accepted NativeImport ASCII domain, not simply use of an already assigned String tag. Not selected. |

## Owners, supersession and separate gates

On formal integration:

* [native.md, Native declaration roles](../specs/native.md#native-declaration-roles)
  owns the exact LibraryName value domain above, while retaining all existing
  resolution, unsafe and lifetime behavior.
* Supersede **only the LibraryName portion** of the
  [source-schema candidate](native-calling-convention-source-schema.md#complete-nativeimport-schema)'s
  “nonempty ASCII names admitted by the selected target” deferral with these
  exact source rules and separate backend admission. Preserve its literal
  decoding and argument schema. EntryName, defaults, symbols, Core authority
  and conventions are unchanged.
* The standard NativeImport row should reference that semantic owner without
  changing its layout or generic String encoding/extent rules. The
  [private-resolution candidate](native-import-resolution-private-persistence.md)
  retains its private name limits, schema composition and resolution facts.
  This document assigns no private revision, standard ID or format pair.
  [Exact draft agreement](../specs/representation/format-versioning.md#2-beta-draft-and-preview-artifacts)
  remains required; equal version bytes do not adopt this proposal.

There is no remaining LibraryName source-policy choice in this recommendation.
Formal adoption and the unapproved source/Core schema prerequisites remain
separate. Actual configured-linker Link support, each selected target's
Dynamic policy implementation, complete private static-data transport and
applicable target filename/capacity support remain implementation gates.

In particular, the prototype's **SystemDirectory lookup is neither Link nor
Dynamic/Default nor Dynamic/CompatibilitySearch** merely because a valid name
reaches a DLL. This proposal selects no loader helper, mutable import cell,
static-link bridge, suffix synthesis, alternate target or fallback. Do not
enable affected native use by relabelling that route.

## Required tests

These are future acceptance cases, not validation results from this
documentation task. Values below denote decoded text, not additional Terrum
literal syntax. Use public compiler APIs and independent String fixtures;
separate source acceptance, serialization, emission and execution evidence.

| Family | Cases and required result |
|---|---|
| Positive source | Admit `a`, `probe`, `probe.dll`, `PROBE.DLL`, `probe.lib`, `libprobe.a`, `libprobe.so.1`, `probe.dylib`, `libstdc++.so.6` and `probe name`. Preserve every character. These establish source validity, not target availability. |
| Mode/owner combinations | Apply the same names to Link, Dynamic/Default and Dynamic/CompatibilitySearch on callable and static-data declarations. Keep existing mode/policy omission rules; an unsupported field pipeline refuses its separate gate, not the name. |
| Literal identity | Different admitted literal spellings decoding to the same name produce identical normalized content. Escapes decoding to a separator, control or non-ASCII scalar reject. Runtime expressions, concatenation and constant-variable arguments do not become literals. |
| Negative values | Reject empty text; NUL at start/middle/end; all other C0 controls; DEL; non-ASCII letters, supplementary scalars and malformed surrogate text. No ASCII replacement, loss or prefix-before-NUL acceptance. |
| Paths | Reject `dir/probe`, `dir\probe`, `/probe`, `\probe`, `.\probe`, `../probe`, `C:probe`, drive-rooted and UNC paths, and `file:probe`. Reject exactly `.` and `..`; do not strip a path and accept its basename. |
| Character boundaries | Admit a single U+0020 space, U+007E tilde and leading/trailing spaces without trimming. Reject U+001F, U+007F and U+0080. Exercise admitted punctuation as one literal operand, not linker/shell options. |
| Short/private-length boundaries | Names of lengths 1, 4, 255, 256 and 257 satisfy the source content rule when otherwise valid. Independently retain the private `.dll`/alphabet/5..256 refusal boundaries; no accidental private widening or source reclassification. |
| Standard String bounds | Test `0xFF_FFFF-1`, `0xFF_FFFF` and one-over-limit unit counts in focused writer/reader fixtures, with sufficient explicitly selected capacity. Check ASCII/UTF encodings of identical ASCII content, terminator exclusion, padding, adjacent offsets and containing extents. One-over-limit cannot wrap, truncate or segment. Do not route these format tests through a native loader. |
| Standard malformed input | Reject selected invalid scalar text, embedded zero, invalid reserved flags, bad name references, truncated/mismatched row extents and non-ASCII NativeImport content, even with a UTF tag. A physically valid empty String still cannot serve as LibraryName. |
| Candidate semantic metadata | Under an explicitly selected contract incorporating this candidate, reject paths, controls and `.`/`..` in the normalized LibraryName even when their general String rows are structurally valid. Do not retroactively label the unchanged accepted schema as adopting this proposal. |
| Source-free retention | Round-trip and remap the exact name through definitions, retained expectations and supported standard/private paths after removing source/debug data. A separately selected private case-insensitive comparison must not rewrite stored spelling or merge differing mode/policy expectations. |
| Target refusal and behavior | A source-valid unsupported name or mode diagnoses the actual backend/container gate. Where independently supported, qualify configured static/shared Link and both Dynamic policies with unrelated headless providers, unavailable bindings and module/image retention. No SystemDirectory relabelling, alternate spelling, policy retry or target fallback. |

Native execution qualification, if later undertaken, must use explicitly
selected final compiler/configuration/inputs and existing supervision and
opt-ins. Creating this document establishes none of those results and does
not complete a compiler or roadmap acceptance gate.
