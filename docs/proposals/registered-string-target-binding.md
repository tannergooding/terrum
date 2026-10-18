# Registered UTF-8 string-literal target

**Status: the source-reading semantic-module-JIT registration is deferred,
not recommended for integration. A future registered target remains
unapproved; ordinary source-grant row helpers are implementation evidence.**

## Current boundary

The self-hosted string-literal component validates and decodes string literals,
and the string-row component measures and encodes UTF-8 `StringNameRow` and
`StringNamespaceRow` rows. Its success-only row builders accept a source range
and validate it against the current `Host.Source` invocation before encoding.
The scalar source-grant companion exposes row byte-length queries, but
registered string targets and row emission through that native companion
remain unsupported. The accepted [literal contract](../specs/language/literal-construction.md)
instead supplies decoded, validated text to an exact target-owned parser
under bounded required constant execution. The
[construction handler contract](../specs/language/construction-handlers.md)
separately owns interpolation/collection capture and drain. Neither registers
this row target or turns a source-grant helper into a literal parser.

This proposal does not change literal parsing, introduce a new encoding, or
move verification or emission from the hosted compiler.

## Deferred route and existing helper

The deferred candidate was one compiler-owned registration from
`StringLiteral` to `StringNameRowUtf8`. Its helper shape already exists in
[`StringRow.terrum`](../../sources/Terrum.CodeAnalysis.Terrum/Modules/StringRow.terrum):

```terrum
bool TryBuildStringNameRowUtf8(
    SourceRange spelling,
    OwnedInt32Buffer workspace,
    init ref<OwnedInt32Buffer> row)
```

This is a reference signature for an ordinary development helper, not an
eligible `[Literal(LiteralKind.Text)]` declaration. It validates the spelling
against invocation `Host.Source`, consumes its workspace input, leaves the
row output vacant on false and publishes a complete owned buffer on true.
The source reader, owned workspace and extra ordinary input are incompatible
with the selected literal parser's fixed two-parameter input/output shape.
Runtime identity-bearing buffer ownership is not an admitted frozen constant
merely because that helper succeeds.

The helper uses flag `1`, a nonempty decoded name, embedded-NUL rejection,
UTF-8 content, its terminator and four-byte alignment. These are ordinary
encoder operations over the accepted [String row format](../specs/il-metadata.md#section-kinds-and-row-layouts),
not a new row encoding. StringNameRow is a semantic use of String data, not
a separate standard table or a reserved target identity.
The candidate registration, invocation lifecycle and any future target type
remain unapproved; helper implementation does not approve them.

[`StringLiteralComponentTests`](../../tests/Terrum.CodeAnalysis.Tests/Modules/Strings/StringLiteralComponentTests.cs)
contains ordinary helper cases for complete publication, refusal and
maximum-grant exact capacity; [`StringRowModuleTests`](../../tests/Terrum.CodeAnalysis.Tests/Modules/Strings/StringRowModuleTests.cs)
exercises private semantic-module reuse across source grants. Neither is a
registered literal path or independent standard-TRRM constant program.

## Recommended integration boundary

Do not add a source-reading JIT handler or make `Host.Source` part of Core's
literal API. A minimal integration would use ordinary decoded immutable
`TextSpan` input and the selected target-owned Text literal parser, with an
admitted pure/constant body or intrinsic. It would reuse the accepted row
codec rather than expose quoted spelling, source offsets or a grant to that
parser. Reusing the runtime helper unchanged is not this integration.

Before recommending an actual target, decide:

- the target's exact nominal identity, Blittable frozen Copy representation,
  finite capacity/size domain and constant-value codec;
- whether the value is bounded inline encoded data, a separately admitted
  immutable resource reference or something else; an owned runtime buffer
  cannot silently stand in for any of them;
- the exact parser/registration identity, whole-input name restrictions,
  false/exception/publication contract and transitive dependencies;
- the UTF-8 measurement/encoding work and live-storage recipe under
  [constant execution](../specs/language/proof-and-constant-evaluation.md),
  including the 24-bit code-unit bound and total artifact/section limits.

Those are substantive target/value decisions, not merely registration or
collision-free allocation. No new target, API, schema or ID is selected here.
The [generic construction metadata proposal](generic-nominal-construction-metadata.md)
does not supply a literal-registration codec; the
[literal logical records](../specs/representation/logical-records.md#literal-construction-and-registrations-encoding-pending)
and [proof/constant follow-up](static-proof-and-constant-evaluation.md)
own those physical prerequisites.

## Conformance if a target is selected

Use the accepted Text input transformations, including ordinary/raw decoded
text, rather than revalidating a quoted source range inside the parser.
Check empty/NUL restrictions, supplementary scalars, UTF-8 count versus
scalar count, terminator/padding, exact capacity and one-over-capacity,
maximum unit count and checked row extent. Compare decoded values and rows
with an independent String writer/reader. False/failure must leave the output
vacant; success publishes a valid frozen value without source/evaluator
backing. Required execution must enforce its exact resource recipe and never
fall back to JIT/native work.

Registration conflicts, target authority, source-free program/value
consumption and replacement need separate cases. Interpolation, general
builder registration, resource-bearing constant extensions and native
source-grant row emission remain outside this deferred candidate. These
gates do not call for implementing the rejected route.
