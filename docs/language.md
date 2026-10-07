# Language overview

Terrum is an experimental systems programming language with a checked,
ownership-aware native execution model. The accepted language contracts are
designed independently of the subset currently implemented by the C# compiler
host.

The current design favors explicit writable access, checked initialization and
cleanup, and target-defined behavior. Places are readonly by default; `mut`
marks writable places or receivers, while `write` grants writable
reference/view access. Legacy `bool`/`out` and vacancy-only `bool`/
`init ref<T>` outputs have distinct caller contracts. These spellings and
their rules are defined by the [source grammar](specs/grammar-spec.md) and
[language contracts](specs/language-contracts.md).

Use the [specification index](specs/README.md) for accepted language,
runtime, metadata, and native contracts. See the
[implementation summary](implementation.md) for the hosted compiler and
self-host component boundaries, the [roadmap](roadmap.md) for pending work,
and the [proposal index](proposals/README.md) for unapproved alternatives.
Accepted contracts are not claims that every feature is implemented.
