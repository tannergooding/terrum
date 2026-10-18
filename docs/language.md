# Language overview

Terrum is an experimental systems programming language with a checked,
ownership-aware native execution model. The accepted language contracts are
designed independently of the subset currently implemented by the C# compiler
host.

The current design favors explicit writable access, checked initialization and
cleanup, and target-defined behavior. Places are readonly by default; `mut`
marks writable places or receivers, while `write` grants writable
reference/view access. `init ref<T>` lends a vacant initialization destination
under its [declared publication contract](specs/language-contracts.md#22-declared-outcome-relationships).
The [source grammar](specs/grammar-spec.md)
and [reference model](specs/language/references.md) define canonical signatures;
parameter-only `out T` is not an alternate form. The compiler's retained legacy
`out` modes are tracked separately in the
[hosted output profiles](implementation/output-profiles.md).

Use the [specification index](specs/README.md) for accepted language,
runtime, metadata, and native contracts. See the
[implementation summary](implementation.md) for the hosted compiler and
self-host component boundaries, the [roadmap](roadmap.md) for pending work,
and the [proposal index](proposals/README.md) for unapproved alternatives.
Accepted contracts are not claims that every feature is implemented.
They are also a [mutable pre-v1 baseline](specs/overview/conformance.md),
not a frozen destination for proposal design.
