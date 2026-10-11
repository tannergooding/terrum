# Module linking and execution scopes

A logical Module owns selected definitions, its governing semantic rules and
complete retained requirements under [requirements and admission](representation/requirements-and-admission.md).
A consumer retains the exact declarations against which it was checked;
a producer supplies the selected definitions. A reference exposes admitted
published contracts, not authority to private implementation bodies.

[Binding and replacement](representation/binding-and-replacement.md) preserves
selected identity and directed expected/provider satisfaction. Source binding is not
rerun against a replacement's candidate set. Reordering physical rows,
changing private implementation bodies or co-locating Modules does not merge
nominal identities, change source access or discard retained requirements.

[Native package identity](native.md#native-package-identity-and-filename-collisions)
uses exact case-sensitive ASCII equality, independently of platform
filename-collision checks. Packaging or filesystem casing cannot merge
Module/type identities or select a compatible provider.

Semantic linking into one image and native composition of independent images
are different consumption scopes. The selected runtime/target contract
determines heap/root domains, identity remapping, transfer, failure, cleanup
and code lifetime. Supporting one mode does not admit another.

[Windows x64 native](targets/windows-x64.md) defines the fixed rootless
call/ownership/deployment scope. It does not infer a managed heap, task/view
crossing, startup adapter or exception interchange from module composition.

An admitted semantic consumer can lower verified retained semantics at process
start or emit a new image before execution. That is distinct from using an
unchanged precompiled consumer with a compatible native provider.
[Native boundaries](native.md) still require the applicable ABI and published
contracts; no mode implicitly supplies live patching, hot unload or source
recompilation.

Only `.trrm` is normative serialized semantic interchange.
The [private-persistence contract](representation/persistence.md) distinguishes
the development `.trm` structured IR even where it shares the `TRRM` signature.
Its writer revision, reader limits and command behavior are informative
implementation details, not an alternate standard Module encoding.

The [development module guide](../implementation/modules.md) preserves those
details and source-free implementation examples. Neither those examples nor
these logical scope rules complete missing feature records or native profiles.
