# Interchange and private-persistence conformance

These cases apply [persistence-taxonomy/1](../representation/persistence.md).
They distinguish a supported operation from an independent serialized
interchange claim. Schema/feature, binding, body truth, ABI and runtime facts
are supplied where their complete general implementations remain separate.
No fixture proves an arbitrary native image or private serializer correct.

## Artifact classification

| Case | Supplied boundary | Required result |
|---|---|---|
| IPTX-01 | Exact supported `.trrm` schema and its admitted semantic requirements | May claim the selected normative interchange, not every source feature |
| IPTX-02 | Supported development `.trm` saved IR with valid supplied contracts | Private operation may succeed; no standard independent byte claim |
| IPTX-03 | Supported `.trmn` package and compatible selected native contract | Private package operation may succeed; native obligations remain |
| IPTX-04 | Supported `.tnset` leaf-set container and complete supplied selected providers | Private set operation may succeed; no public container grammar inferred |
| IPTX-05 | Supported `.trng` container and complete supplied graph closure | Private graph operation may succeed; no public graph grammar inferred |
| IPTX-06 | PE/ELF image conforms to the supplied target format and ABI | Target/image claim is separate from Terrum semantic-byte interchange |
| IPTX-07 | CLI response conforms to its supplied exact protocol schema | Tooling claim does not standardize its input artifact |
| IPTX-08 | Known record shape but unassigned required `.trrm` feature | Decline affected use; taxonomy does not activate it |

## Actual schema identity

| Case | Supplied boundary | Required result |
|---|---|---|
| IPID-01 | Private `.trm` bytes relabeled `.trrm` | Relabeling does not establish normative schema or interchange |
| IPID-02 | `.trm` and `.trrm` both carry `TRRM` magic | Retain distinct actual schemas; do not choose interpretation from magic |
| IPID-03 | Two `.trrm` drafts both carry `(1, 0)` but disagree on required schema | Decline interpretation without exact supported schema agreement |
| IPID-04 | Registry allocation digest matches but governing semantics do not | Do not admit by digest alone |
| IPID-05 | Private package revision equals a known native-profile or semantic revision | Equality does not select the embedded schema, ABI or governing rules |
| IPID-06 | Supported private outer container embeds an independently interpreted `.trrm` payload | Admit that payload's own exact schema and complete requirements |
| IPID-07 | Valid normative payload inside a private outer container | Only the payload's selected interchange claim is normative |
| IPID-08 | Future independent implementation-specific agreement for private bytes | Agreement may support that operation, not Terrum-standard private serialization |

## Native binding and consumption

| Case | Supplied boundary | Required result |
|---|---|---|
| IPBD-01 | Private provider has matching DLL/export names but incompatible expected contract | Decline call/binding despite successful name resolution |
| IPBD-02 | Replacement preserves complete supplied public contracts and target ABI | Private format alone does not prohibit compatible native replacement |
| IPBD-03 | Private body/storage changes inside a jointly regenerated region | Allowed only while admitted observable contracts remain satisfied |
| IPBD-04 | A private semantic loader lowers at process start | Does not establish unchanged-precompiled-image or live-patching support |
| IPBD-05 | Precompiled composition has no selected adaptation/recompile policy | Do not silently supply source rebinding, allocation or a JIT fallback |
| IPBD-06 | A private checksum and platform load succeed but body/code truth is unknown | Integrity/loading is not semantic proof; use the actual trust/audit contract |
| IPBD-07 | Every supported scalar/compact/owner native call classification is implemented | Independently establish complete target storage, call and unwind behavior; document-only here |
| IPBD-08 | Real compatible image replacement preserves every caller observation | Independently establish actual image/runtime behavior; document-only here |

## Ownership, outputs and lifetime

| Case | Supplied boundary | Required result |
|---|---|---|
| IPLF-01 | An intermediate forwards a dependency-owned value | Retain supplied nominal origin and cleanup provider, not intermediate ownership |
| IPLF-02 | Type/cleanup obligation remains but no IAT call names its provider | Retain the dependency; absence of a machine import does not erase it |
| IPLF-03 | Required code/helper is unloaded while a value/call/cleanup can still use it | Decline that retirement under the supplied lifetime obligations |
| IPLF-04 | Package can encode a managed handle but selected boundary cannot transport it | Decline crossing; encodability is not authority or profile support |
| IPLF-05 | Owner-only forwarding and managed-private graph examples exist separately | Do not infer that their public crossing modes compose |
| IPLF-06 | Declared initialization output fails without successful publication | No fabricated default, readable partial value or success-shaped output |
| IPLF-07 | Actual failed unwind/continuation deregistration and code retirement | Independently establish OS registration and retained-code lifetime; document-only here |
| IPLF-08 | Actual cross-image root, cleanup and exceptional-output execution | Independently establish those admitted runtime/target protocols; document-only here |

## Tooling and admission scope

| Case | Supplied boundary | Required result |
|---|---|---|
| IPTL-01 | Valid CLI schema reports a successfully parsed private artifact | Report private kind/schema honestly; no normative byte claim |
| IPTL-02 | Inspector cannot recognize an artifact | May report unrecognized input, not execution support or an empty success |
| IPTL-03 | Executor cannot admit an actual schema or required semantic feature | Decline before affected execution; no guessed interpretation |
| IPTL-04 | Optional section attempts to carry required semantic behavior | Reject that use; private persistence does not weaken normative required forms |
| IPTL-05 | Private loader has small implementation-selected limits | Do not advertise them as standard portable minima |
| IPTL-06 | Normative payload work is nested within a private wrapper operation | Preserve the actual selected root accounting; nesting does not reset it |
| IPTL-07 | Ordinary trusted access selects one indexed object | Taxonomy adds no preliminary all-Body scan |
| IPTL-08 | Program returns a raw status numerically equal to a CLI failure code | Do not infer its result channel from the integer alone |

## Ownership of documentation and conformance

| Case | Supplied boundary | Required result |
|---|---|---|
| IPOW-01 | Development module/native/set/graph serialization and revision ledgers | Informative implementation references, not normative byte owners |
| IPOW-02 | Implementation guide states narrower source/profile support | Does not weaken canonical source/runtime/target obligations |
| IPOW-03 | Unpublished private alpha revision changes | No old-format reader or migration is required |
| IPOW-04 | Private format receives a new extension or current version number | Not an adoption of independent serialized interchange |
| IPOW-05 | Proposed promotion supplies only framing or known records | Still incomplete until required semantics/admission/rewrites and vectors are complete |
| IPOW-06 | Implementation references precisely document private framing and supported operations | Retain their implementation-specific status and canonical contract links; precision does not standardize the private format |
| IPOW-07 | Independent normative `.trrm` producers/readers implement every claimed feature | Requires their complete codecs and independent production/consumption; document-only here |
| IPOW-08 | Future public adoption of native package/set/graph bytes | Requires explicit complete schema and behavioral conformance; document-only here |
