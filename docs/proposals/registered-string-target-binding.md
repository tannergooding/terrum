# Registered UTF-8 string-literal target

**Status: Deferred. The proposed source-reading semantic-module-JIT route is
not the intended compile-time literal model.**

## Current boundary

The self-hosted string-literal component validates and decodes string literals,
and the string-row component measures and encodes UTF-8 `StringNameRow` and
`StringNamespaceRow` rows. Its success-only row builders accept a source range
and validate it against the current `Host.Source` invocation before encoding.
The scalar source-grant companion exposes row byte-length queries, but
registered string targets and row emission through that native companion
remain unsupported. The accepted builder/handler direction requires explicit
target selection, eager source capture, defined ownership transfer, refusal
without rollback, and publication only after validation and freezing; it does
not define this registration's identity or lifecycle.

This proposal does not change literal parsing, introduce a new encoding, or
move verification or emission from the hosted compiler.

**Decision:** The source-dependent JIT route is deferred. This proposal does
not establish a literal handler contract or source-grant requirement.

## Question

Should one registered target bind a string-literal source range to the
existing UTF-8 `StringNameRow` encoding, with an owned row published only on
success?

### Summary

The existing encoder provides one narrow, testable target: flag value `1`,
nonempty names, embedded-zero rejection, UTF-8 payload, a terminator, and
four-byte row alignment. Binding it requires a concrete registration and
ownership/failure contract; the general builder/handler direction does not
settle those per-target details.

### Deferred decision

Do not add this registration through a source-reading semantic-module JIT
handler. The intended compile-time model for `ConstEval` and user-defined
integer literals is pure, bounded computation based on compiler-known
invariants and ordinals, not arbitrary source-dependent JIT work. This
string-target registration and its ownership/refusal/output rules remain
unapproved. Any future compile-time string target needs a separately defined
pure input, work and size contract; this proposal does not imply a general
builder/handler route.

### Original candidate profile (unapproved)

The deferred candidate was one compiler-owned registration from
`StringLiteral` to `StringNameRowUtf8`, using the existing encoder's flag value
`1`, nonempty-name rule, embedded-zero rejection, UTF-8 payload, terminator
and four-byte alignment. Its proposed handler shape was:

```terrum
bool TryBuildStringNameRowUtf8(
    SourceRange spelling,
    OwnedInt32Buffer workspace,
    init ref<OwnedInt32Buffer> row)
```

The proposal would have revalidated the exact spelling against invocation
`Host.Source`, consumed the owned workspace on entry even on refusal, left
`row` vacant on refusal, and published the completed owned row exactly once
on success. The handler would not retain the source grant. These registration,
source-access, transfer, refusal, cleanup and publication semantics remain
unapproved; the sketch is preserved only to identify what was deferred.

The target restrictions and existing `StringRow` ownership operations are
evidence-based. The compiler-owned registration identity, same-invocation
module route, wrapper signature, transfer-on-entry rule, refusal mapping,
cleanup responsibility, and success-only publication are recommendations
that require review; they are not claims about existing behavior. The
ordinary core-library route is not sufficient because it does not provide
`Host.Source`; the native source-grant companion is not sufficient because
it does not support owned row results.

## Scope and validation

Keep interpolation, other literal forms, additional encodings or row kinds,
general builder/handler registration, source-grant retention, and native
procedure-call support out of scope. The hosted verifier/emitter remains the
execution boundary.

Validate empty and embedded-zero names, UTF-8 byte measurement, terminator
and alignment, insufficient-workspace refusal, consumed-workspace cleanup,
malformed spellings and ranges, vacant output on refusal, complete publication
on success, and absence of a retained source grant after the invocation.
Compare encoded rows with the existing host writer and exercise the registered
path through the current scalar-frontend projection tests.
