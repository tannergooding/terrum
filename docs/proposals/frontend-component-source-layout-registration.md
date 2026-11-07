# Source layout registration for the frontend component probe

**Status: unapproved candidate; provisionally actionable only under the
authorized missing-contract workflow.** This extension selects no production
Core authority/provider, changes no normative owner and makes no stability
promise.

## Decision and scope

The `frontend-component` probe compiles a fixed set of Terrum-authored
frontend component sources and a synthetic entry point through the options-
bearing source compiler. Those component sources use registered `Layout` and
`Blittable` schemas. The command currently has no way to supply the exact
`LayoutCoreRegistrationSet` required by the accepted source-layout contract.

Recommend allowing the existing version-1
[`--layout-core-manifest`](source-layout-cli-registration.md) on
`frontend-component`, for its component-source compilation only. Reuse the
base proposal's complete strict grammar, closed declaration producer,
explicit host authorization, checked Module binding, immutable original
expectations and no-default/no-fallback rules. Add no second manifest format,
Core resolver, native registration or artifact decoder.

This amends only the base proposal's `frontend-component` command exclusion
and the matching exclusion in the
[scanner-component extension](scanner-component-source-layout-registration.md).
The [private-module extension](private-module-source-layout-registration.md)
continues to reject the option on frontend commands, except for this separately
scoped probe. The base proposal remains the sole owner of manifest semantics.

`frontend-build`, `frontend-build-library` and `frontend-run` remain excluded.
They route the user's original source through their existing fallback; this
extension must not pass the component manifest into that source compilation.
No new `terrum.cli/1` machine protocol behavior is selected.

## Exact command and selection boundary

The candidate command form is:

```text
frontend-component <terrum-project-directory> <input|-> --runtime <fail-fast|checked-exceptions> [--arithmetic-profile <development|release>] [--layout-core-manifest <path>] [--json]
```

Inherit the base option's exact spelling and argv rules: one occurrence,
exactly one nonempty following operand, no equals form or alias, and an
optional final `--json`. Reject the option on every other command, including
the three original-source frontend fallback commands.

The manifest's `usingModuleIdentity` names the one Module containing the
selected frontend component trees and the existing synthetic entry point.
It is not the subject input, buffer set, component directory, command name or
an inferred namespace. The using identity must not equal a provider identity.
Keep ordinary lexical lookup and each source tree's imports unchanged; the
manifest does not inject `using` directives or authorize name-based retry.

The selected registration is passed once through
`CompilationOptions.LayoutCoreRegistration` to the component
`Compilation.Create` call. It does not configure the subject data, buffers,
the original-source fallback, a private/Standard artifact or a later source
compilation. Without the option, no layout registration is selected and the
component source receives its existing diagnostics.

## Admission, ordering and errors

Resolve the manifest path against the invocation working directory. Reuse
`LayoutCoreManifestReader.Read` and fully admit the manifest once, even when
the selected component source would not use a role. Require a regular file;
there are no nested file references, provider discovery, environment defaults
or path-only registration caches.

Validate the command and paths before reading component contents or consuming
subject stdin. The manifest must not alias any fixed component source selected
from the project directory or the subject input file. This command has no
output path. Use the existing path/file-identity relation rather than only
argument-string equality; fail closed when required identity inspection
cannot establish distinctness. A manifest may reside in the project directory
if it is distinct from the selected component sources.

Preserve the existing response families:

| Condition | Result |
|---|---|
| Ineligible command, invalid spelling/arity/operand, repeated option | `TRM2000`, exit 2, before source reads or compilation. |
| Manifest I/O/access, strict decoding, schema, origin, declaration, export or registration failure | `TRM2001`, exit 2; no component compilation or JIT. |
| Manifest aliases a selected component source or subject input | `TRM2002`, exit 2, before source reads or JIT. |
| Valid configuration followed by source/layout diagnostics | Existing located compilation diagnostics, exit 1. |
| Successful component compilation or invalid projected source record | Preserve existing `frontend-component` success, `TRM4001` or `TRM5001` behavior and response shape. |

Configuration errors use the existing input-error response and identify the
manifest path and, when established, the invalid property/index. Do not invent
source spans, alter projection versions or serialize host authorization.
No JIT worker starts before successful configuration admission and component
compilation.

## Required acceptance cases

- Compile the real frontend component sources with an explicit test manifest
  under both runtime policies. Preserve the existing projection shape, source
  snapshots and valid scanner-owned records.
- Without the manifest, verify that registered component attributes do not
  acquire roles by spelling or embedded defaults. Unannotated supported
  components remain usable.
- Reject duplicate, malformed, unknown-field, wrong-version and invalid
  identity/origin manifests before reading a component source or blocking on
  stdin.
- Reject aliases against every selected component file and the subject input,
  including relative/case/link/hard-link aliases where supported; allow a
  distinct manifest inside the component directory.
- Change the manifest at the same path and use equal spellings with distinct
  origins across invocations; prove no cached or cross-context registration is
  reused.
- Explicitly reject the option on `frontend-build`, `frontend-build-library`
  and `frontend-run`, retaining their original-source routing and sentinel
  outputs. Keep the existing no-manifest tests and add the manifest only to
  callers that intentionally compile the registered component bundle.

This extension remains unapproved. It does not make the subject compilation,
other source commands, native Core, or the selected machine protocol
configured.
