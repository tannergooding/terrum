# Language overview

Terrum is an experimental systems programming language with a checked,
ownership-aware native execution model. The accepted language contracts are
designed independently of the subset currently implemented by the C# compiler
host.

Start with [values, copying and aliases](specs/language.md#definitions-and-notation).
Copying an eligible scalar creates independent data; copying a copyable object
handle or eligible reference descriptor instead gives another path to the
same object or place. A struct copy can therefore have independent field
storage while its reference fields still alias existing data. Unique values cannot be
implicitly duplicated: an explicit named `move` transfers responsibility and
vacates the source, rather than silently changing assignment into a move.

References and views are usable only while their referenced storage, backing
and required access remain valid. `ref<T>` is non-owning; it does not
universally extend the target's lifetime. An admitted managed root can keep
an object reachable, but that is separate from a native resource's release,
pinning, guard duties and cleanup. C#/.NET provides a useful copying/aliasing
analogy, not Terrum's runtime rules: tracing GC is not reference counting,
and C#'s historical `ref struct`, storage and escape restrictions are not
universal Terrum restrictions. Stored, returned or suspended references still
need an admitted complete contract and representation.

The current design favors explicit writable access, checked initialization and
cleanup, and target-defined behavior. Places are readonly by default; `mut`
marks writable places or receivers, while `write` grants writable
reference/view access. `ref<init T>` supplies a vacant initialization destination
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
