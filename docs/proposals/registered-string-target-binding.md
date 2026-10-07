# Registered UTF-8 string-literal target

**Status: unapproved**

## Current boundary

The self-hosted string-literal component validates and decodes string literals,
and the string-row component already measures and encodes the UTF-8
`StringNameRow` representation. The scalar source-grant companion exposes
literal queries, but registered string targets remain unsupported. The
accepted builder/handler direction requires explicit target selection,
eager source capture, defined ownership transfer, refusal without rollback,
and publication only after validation and freezing; it does not define this
registration's identity or lifecycle.

This proposal does not change literal parsing, introduce a new encoding, or
move verification or emission from the hosted compiler.

## Question

Should one registered target bind a validated `StringLiteral` to the existing
UTF-8 `StringNameRow` encoding, with an owned row published only on success?

### Summary

The existing encoder provides one narrow, testable target: flag value `1`,
nonempty names, embedded-zero rejection, UTF-8 payload, a terminator, and
four-byte row alignment. Binding it requires a concrete registration and
ownership/failure contract; the general builder/handler direction does not
settle those per-target details.

### Recommendation

Add exactly one closed target registration, `StringLiteral` to
`StringNameRowUtf8`, backed by the resolved core-library declaration
`TryBuildStringNameRowUtf8`. Resolve its identity from the declaration and
containing library, never from a same-named method alone. Keep the registration
specific to this signature and target; do not add a general flags parameter,
registration attribute, or builder framework in this slice.

The recommended signature shape is:

```terrum
bool TryBuildStringNameRowUtf8(
    StringLiteral literal,
    OwnedInt32Buffer workspace,
    init ref<OwnedInt32Buffer> row)
```

The contract is:

- `literal` is already validated. Its source range borrows immutable
  invocation `Host.Source` for the duration of the call; the handler retains
  neither the source grant nor a source-backed view.
- `workspace` is transferred to the handler on entry and consumed regardless
  of the result. The handler may move it into the existing
  `EncodeStringNameRow` implementation; it is not returned on refusal.
- Preflight the existing `StringNameRow` constraints and required byte length
  before publishing anything. The fixed registration uses flags `1`; it does
  not expose flag selection to callers.
- Return `false` for a target refusal, including an empty name, embedded
  zero, or insufficient workspace. On refusal, release any partial workspace
  state and leave `row` vacant. Workspace consumption is not rolled back.
- Propagate runtime failures according to the ordinary checked/fail-fast
  behavior; do not translate them into a successful row or a refusal result.
- On success, publish the owned row exactly once through `init ref` only
  after the UTF-8 payload, terminator, and four-byte padding are complete.

The target restrictions and existing `StringRow` ownership operations are
evidence-based. The resolved registration identity, wrapper signature,
transfer-on-entry rule, refusal mapping, cleanup responsibility, and
success-only publication are recommendations that require review; they are
not claims about existing behavior.

## Scope and validation

Keep interpolation, other literal forms, additional encodings or row kinds,
general builder/handler registration, source-grant retention, and native
procedure-call support out of scope. The hosted verifier/emitter remains the
execution boundary.

Validate empty and embedded-zero names, UTF-8 byte measurement, terminator
and alignment, insufficient-workspace refusal, consumed-workspace cleanup,
vacant output on refusal, complete publication on success, and absence of a
retained source grant after the invocation. Compare encoded rows with the
existing host writer and exercise the registered path through the current
scalar-frontend projection tests.
