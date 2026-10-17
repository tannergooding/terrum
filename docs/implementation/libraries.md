# Library implementation inventory

This chapter is informative. [Selected library contracts](../specs/libraries/adopted-inventory.md)
and their [provider/consumer scenarios](../specs/libraries/design-scenarios.md)
do not claim current source checking, code generation, runtime or artifact
support. The [implementation inventory](../implementation.md) owns the
implemented subset; it does not define language/library behavior.

The historical library design notes describe a self-hosted managed-node
collection candidate as a bounded consumer of source-defined classes and
optional edges. That candidate is not the generic List/LinkedList API
inventory and does not establish its guards, registered builders, lending
roles, invalidation or public replacement contracts.

The native compiler's bounded callable/output support and generic templates
are development profiles, not implementation of all Core/Foundation/
Application groupings. A supported private generic specialization is not a
public generic export or independently admitted protocol witness.
Hosted view/owner and managed-node examples are likewise distinct from a
complete Span/TextSpan, collection, task or native-resource library profile.

`library/*.api.terrum` and design samples inventory candidate declarations;
they are not executable reference assemblies. A method body in self-hosted
code can illustrate an operation without supplying a registered Core
identity, checked public effect/capture contract or independently usable
metadata/value/operation codec. Names and implementation details cannot
fill those gaps.

The parser preserves `..sequence` inside a bracket initializer as a
`CollectionSpreadElement`. Binding, verification, persistence and execution
of collection builders or spread protocols remain unsupported; bracket
initializers are still admitted only by the closed fixed-storage gate.
The parser preserves generic struct/class type parameters, conformances, and
`where` clauses, as well as interface formals and constraints. The binder
supports compilation-local closed specialization of internal generic structs
with type-only formals, fields, constructors and ordinary methods. Their
closed layouts and bodies use the existing bound representation; no open
generic definition is exported or imported. Public generic structs, generic
classes, nominal constraints/conformances, methods with their own type
parameters, independent generic metadata, interface witnesses and builders
remain unsupported.

Implementation work must state the actual selected source/runtime/target
profile and verify its transitive requirements. Parsing a signature, running
a finite logical fixture or emitting a private development artifact does
not establish the complete selected library contract. Unsupported facilities
must decline affected use rather than silently substitute default values,
blocking, allocation, lossy text conversion or a candidate protocol.

This separation intentionally does not migrate code or change the API
sketches. Compiler/library support should be tracked at its exact tested
revision, not inferred from the specification audit or from historical notes.

## Generic witness support

Current compiler support for comparison and unary witnesses is
bounded, while shift, heterogeneous-binary, increment/decrement and
`true`/`false` protocols remain unimplemented -- see
[implementation](../implementation.md#bounded-generic-functions) for the exact
current boundary.

The current implementation deliberately
keeps generic templates and their witness resolution compilation-local; that
is a bounded-implementation characteristic tracked in
[implementation](../implementation.md#bounded-generic-functions), not a
permanent library requirement, and does not foreclose a future public generic
contract. Current compiler and sample coverage does not extend to the
complete library inventories, primitive numeric conformance, mixed-result
operators, families or generic collection algorithms.

## Parser output support

The bounded executable parser/output profile uses legacy `bool`/`out`
forms. These private development signatures are not alternate language
signatures or requirements to preserve alpha declarations or artifacts.
The [output-profile inventory](output-profiles.md) tracks their bounded
support separately from the selected initialization/status contracts.
