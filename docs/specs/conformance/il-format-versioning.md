# TRRM format-version conformance

These cases apply [format versioning](../representation/format-versioning.md),
[metadata framing](../il-metadata.md#header-and-directory) and
[requirements and admission](../representation/requirements-and-admission.md).
The current draft is `trrm/1-draft-layout-queries-1`, format `(1, 0)`.

[Fixed vectors](il-format-versioning-vectors.json) include a complete current
artifact containing one empty Module and its identity String. Draft identities
and released catalogs used to assess selection are supplied schematic facts,
not additional registered schemas or release assignments. Exchange context and
exact agreement are not encoded as a header flag or an artifact field.

Finite assessments cover selection over those facts and natural framing of
the fixed bytes. They do not prove provenance, complete semantic agreement,
arbitrary body truth or general conversion. The two document-only cases retain
those independent obligations.

| Case | Situation | Required result |
|---|---|---|
| IFMT-01 | Current beta artifact has pair `(1, 0)` and exact current-schema agreement | Select the current schema, subject to its remaining admission rules |
| IFMT-02 | Pair matches but the agreed draft schema is unsupported | No support claim for that exchange; reject affected use, with diagnostics left to the implementation |
| IFMT-03 | Pair matches but no exact draft-schema agreement is supplied | No support claim for that exchange; equal bytes are not agreement, so reject affected use |
| IFMT-04 | Two incompatible preview schemas reuse `(1, 0)` | Permitted before release; each exchange selects its exact schema independently |
| IFMT-05 | A current draft consumer receives another pair, or an older same-pair draft without support | No support claim for that schema; no preview update or compatibility duty, and no handling behavior is prescribed |
| IFMT-06 | Only a registry digest, build label, filename or container name is supplied | It does not replace complete draft-schema and governing-rule agreement |
| IFMT-07 | A supplied released catalog assigns the pair to an exact supported schema | Select that assigned schema; no separate schema-ID field is required |
| IFMT-08 | An unassigned pair has the same major and a larger minor than a supported release | No support claim; no inferred same-major or numeric roll-forward decoding, and no handling behavior is prescribed |
| IFMT-09 | A released pair is known but its exact schema is unsupported | No support claim for that encoding; catalog recognition is not consumer support, so reject affected use |
| IFMT-10 | A released pair is reassigned to another schema | Release publication violates the versioning contract; reject affected use under the conflicting schema |
| IFMT-11 | A consumer tries to infer preview versus release from the pair alone | No such encoded distinction; use the explicitly selected exchange schema |
| IFMT-12 | Producer emits a truncated magic/header/directory or nonzero reserved header byte | Producer violates the selected framing schema; verification rejects before affected use |
| IFMT-13 | Dependencies use separately supported schemas and are later packed | Admit each separately; packing needs one supported destination representing all retained obligations |
| IFMT-14 | Apply only the versioning policy to current registry rows, opcode allocations, widths and artifact vectors | The policy itself changes no physical assignment or byte; separate manifest/schema changes retain `(1, 0)` under exact draft agreement |
| IFMT-15 | A selected schema is offered as proof of arbitrary Module, feature, binding, target or body support | Document-only: establish complete retained semantic admission independently |
| IFMT-16 | Historical bytes are relabeled or generally transformed into released/current input | Document-only: relabeling does not preserve producer correctness; transformations must preserve schema and meaning, and the resulting input is verified before affected use |
