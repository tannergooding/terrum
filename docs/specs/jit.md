# JIT execution and replacement

JIT and AOT are implementation strategies, not different source languages,
contract defaults or artifact schemas. The
[scope and conformance rules](overview/conformance.md) require the same
observable behavior for the same selected features. This chapter applies
existing execution, admission and target rules; it selects no public JIT,
scheduler, code-servicing or sandbox API.
The [semantic operation display map](il-instruction-set.md#operation-identity-encoding-and-presentation)
keeps selected source/semantic identities distinct from schema names and
target lowering; JIT/AOT timing does not create a different operation.

## Admitted execution

Before affected execution, the provider admits the complete
[semantic requirements](representation/requirements-and-admission.md),
[binding and replacement obligations](representation/binding-and-replacement.md)
and actual runtime/target dependencies. An ability to regenerate code does not
admit a missing representation, root protocol, failure transport or managed,
view/task/callback boundary.

The [Windows x64 deployment contract](targets/windows-x64-deployment.md#3-native-image-stack-and-unwind-support)
owns that profile's fixups, write/execute separation, instruction-cache
publication, stack/unwind records and registration lifetime. Private backend
objects and current worker support do not define those requirements.

## Replacement and code lifetime

Retained code, helpers, unwind data, roots and cleanup providers remain valid
through their last actual use under the [native lifetime contract](native.md#runtime-and-code-lifetime).
Replacement preserves every retained published promise and any stronger
provider facts used by an optimization. Regeneration cannot reinterpret a
fixed layout or silently retain stale coupled layout observations.

These obligations select neither hot unload nor mandatory JIT/IR fallback.
The [boundary contract](data-layout-and-abi.md#private-implementation-versus-independently-replaceable-boundaries)
distinguishes jointly regenerable code from independently replaceable
crossings; private serialization, successful loading and process supervision
are not proof of compatibility, authentication or sandboxing.

## Informative implementation proof

The [development Windows x64 JIT proof](../implementation/jit.md) preserves
the implemented process-start worker, memory/unwind behavior, commands and
support limits. Its [experimental responses](../implementation/cli-protocol.md)
are not implementation of the selected
[CLI supervisor protocol](cli-protocol.md). Raw worker return status remains
distinct from that protocol's normal program-return data and CLI outcome.
