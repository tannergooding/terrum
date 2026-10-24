# Interchange requirement and admission conformance

These cases apply [requirements and admission](../representation/requirements-and-admission.md),
the [registry](../representation/registry.md) and the
[metadata boundaries](../il-metadata.md).
`E1/E2`, `R1/R2`, `F@1/F@2` and Module names below are **schematic resolved
identities**, not physical schema names, Feature IDs or compatibility entries.
A stated support mapping or validation result is supplied evidence, not a
certificate whose truth is proved by this corpus.

Finite models can assess identity, prerequisite/dependency closure and admission
decisions over those facts. Actual decoding, arbitrary body/contract truth,
source-obligation inference, replacement and target implementation need their
own evidence. Cases about malformed or unsafe-to-consume structures require consumer
rejection before affected use. A semantically false but structurally
consumable producer assertion violates producer obligations, but does not
require consumer re-proof or rejection.
Unsupported or incompatible input does not establish a support/compatibility
claim. The current schema is `trrm/1-draft-layout-queries-1`; the
[manifest companion](il-semantic-manifests.md) supplies actual edition/Feature
bytes. Feature 13 `LayoutQueries` is registered at revision 1; it does not
replace the independent target, generic-identity, witness or constant
prerequisites of retained operations.

## 1. Exact identity and ownership

| Case | Situation | Required result |
|---|---|---|
| IREQ-01 | Artifact selects supported exact E1 and its Module's exact R1 is supported | Identity/support checks succeed, subject to the remaining admission gates |
| IREQ-02 | Preview header version pair matches but exact agreed encoding schema differs | Decline unsupported draft exchange; matching `(1, 0)` is insufficient |
| IREQ-03 | A Module tries to select E2 inside an E1 artifact | Producer violates the one-schema rule; shared tables and Bodies do not select another schema |
| IREQ-04 | A registry digest or container/target name is offered as complete draft-schema agreement | Decline exchange unless the exact governing schema/semantics are otherwise established |
| IREQ-05 | Module omits its governing edition and asks for the reader's R2 default | Producer omits a required identity; the consumer's default does not fill it |
| IREQ-06 | Reader supports R2 but Module requires R1, without a defined mapping | No support claim for R1; revision ordering does not establish roll-forward |
| IREQ-07 | Reader's implementation explicitly supports both R1 and R2 | Preserve the Module's selected edition; either required semantics can be supported |
| IREQ-08 | Supplied directed compatibility mapping preserves R1 using implementation R2 | Support required R1 in that context; retain R1 as the required identity |
| IREQ-09 | Only the inverse mapping is defined | Do not infer symmetry or claim support for the undefined direction |
| IREQ-10 | R3 supports R2 and R2 supports R1, but no R3-to-R1 mapping is defined | Do not infer transitivity or revision-max support |
| IREQ-11 | One Module requires F@1 and another F@2 | Keep module-owned revisions distinct; a consumer must support both when retained |
| IREQ-12 | A same-spelled attribute/package label claims the required feature identity | The spelling does not establish the exact resolved registered identity |
| IREQ-13 | A required record's rules conflict with its Module's governing semantics | Producer violates the governing-edition relationship; the record is not an edition override |
| IREQ-14 | Producer marks a requirement supported or already accepted | A producer flag does not establish consumer support |
| IREQ-15 | A required feature's numeric revision is greater than a supported one, or vice versa | Neither ordering proves compatibility; compare exact supported semantics or the applicable explicit mapping |
| IREQ-16 | Dependencies arrive in separate E1/E2 artifacts | Admit each artifact under its own supported schema; packing them later requires one complete destination schema |

## 2. Complete retained requirements

| Case | Situation | Required result |
|---|---|---|
| IRCL-01 | R1 defines baseline F@1 and the Module declares no extra requirements | Effective requirements still include F@1 |
| IRCL-02 | Explicit G@1 requires F@1, which requires H@1 | Check all three exact requirements |
| IRCL-03 | Two paths reach the same exact prerequisite | One logical obligation, not a new identity or repeated unbounded expansion |
| IRCL-04 | A prerequisite refers to an unknown required revision | No support claim for the incomplete required semantics |
| IRCL-05 | One Module's baseline and declared closure require conflicting F@1/F@2 | Producer violates the requirement-closure contract; do not take max revision or relabel either requirement |
| IRCL-06 | Complete finite prerequisite or Module graphs contain cycles | Reach a finite closure; separately apply storage/initialization/execution rules |
| IRCL-07 | Entry Module A retains provider B, which retains provider C | Admit A, B and C with their own editions/requirements |
| IRCL-08 | A packed direct reference replaces A's import of B | B's retained requirements and binding obligations remain |
| IRCL-09 | A retained declaration, value, witness, layout or cleanup needs B but no machine Call names B | B remains in the dependency closure |
| IRCL-10 | A compiler-only parser leaves no retained runtime/value/contract dependency | No runtime import is fabricated solely for compilation history |
| IRCL-11 | One selected Module identity is unresolved or selects conflicting producer revisions | No support claim; missing resolution is not an empty dependency |
| IRCL-12 | A's closure excludes an unrelated recognized Module D | D's execution support is not claimed; no validation of its unretained payload is required |
| IRCL-13 | Required operations sit in an uncalled method of a retained Module | Admit the Module's complete requirements, not only its anticipated path |
| IRCL-14 | Infer requirements for arbitrary source/runtime features from private bodies or feature names | Not sufficient; complete publication and source-obligation inference require independent evidence |

## 3. Admission boundaries

| Case | Situation | Required result |
|---|---|---|
| IRAD-01 | A selected Module has malformed framing or a schema-required unknown directory form | Verification rejects the affected input before binding or use |
| IRAD-02 | An uncalled Body in the selected Module contains an unknown/reserved opcode | Verification rejects the required Body before module use; an uncalled method does not waive verification |
| IRAD-03 | A required access exposes an index/stream contradiction | Verification rejects the indexed-fact inconsistency before affected use |
| IRAD-04 | A recognized required semantic feature is unsupported in the retained closure | No support claim for the affected binding, evaluation, lowering or execution |
| IRAD-05 | Unsupported known semantics are only in an unrelated Module and the selected schema permits structural inspection | Do not claim D's execution; no check of unretained records is required |
| IRAD-06 | A retained current Module requires an unregistered Feature pair | No support claim; recognizing its two-word codec does not register a capability |
| IRAD-07 | Empty current feature range is presented as proof of support for every accepted capability | Admit only if all actual required contracts and prerequisites are available; the empty range creates neither a reduced GA floor nor a full-support claim |
| IRAD-08 | A known Param/TypeSpec/Effects shape lacks the required output, loan or pure-form codec | No support claim for the affected retained use; shape recognition is not feature completion |
| IRAD-09 | A feature name is declared but required records, values, operations or dependencies are missing | No complete support claim; private-body or attribute fallback does not supply the contracts |
| IRAD-10 | Feature/rule support succeeds but a retained expected binding is incompatible | No compatibility claim for the binding; support is not contract compatibility |
| IRAD-11 | Target or runtime policy differs incompatibly across a retained edge | No compatibility claim; packing or semantic support cannot erase the mismatch |
| IRAD-12 | Producer trust or a passing integrity check accompanies unsupported/missing requirements | These facts do not establish support for missing requirements |
| IRAD-13 | Required admission exhausts its applicable declared resource envelope | Explicitly decline admission; do not skip checks or fabricate support |
| IRAD-14 | Arbitrary signatures, effects, access, layout or lifetime contracts are compared | Apply their complete binding relation; finite support facts do not prove it |
| IRAD-15 | A producer publishes ownership/effects/CFG or unsafe facts about an internal Body | Producer is responsible for their truth; consumers structurally validate the Body and may rely on those facts without semantic re-proof. Optional audits may examine them; they do not prove external provider behavior or authored unsafe promises |
| IRAD-16 | A getter, static initializer, parser or cleanup could be run to discover requirements | Admission uses declared supported records; no source behavior is an inference probe |
| IRAD-17 | Required constant interpretation depends on unsupported rules/program/value codec | No support claim for that interpretation; no runtime constant fallback is equivalent |
| IRAD-18 | Module feature admission succeeds but a runtime caller violates its precondition | The ordinary invocation rule still applies; admission supplied no valid-input proof |
| IRAD-19 | An implementation uses a fixed scalar/fail-fast cap despite having the complete required capability closure | The cap does not define TRRM 1.0 semantics; disclose only genuine unsupported prerequisites or explicit resource-budget exhaustion |
| IRAD-20 | An implementation omits a required contract but claims full TRRM 1.0 GA conformance | Reject the conformance claim; partial capability support must be identified without redefining the accepted floor |

## 4. Packing and retained decisions

| Case | Situation | Required result |
|---|---|---|
| IRRW-01 | Packing reorders Module/definition rows | Preserve each semantic identity, edition, requirement and retained edge; remap physical references |
| IRRW-02 | Packing replaces two module-owned revisions with their maximum or the entry Module's edition | The rewrite does not preserve the original semantics and cannot claim compatibility |
| IRRW-03 | A supported source encoding is converted to another schema | Retain meaning only if the destination represents all retained obligations |
| IRRW-04 | Destination schema cannot encode a retained required contract | No compatibility claim for the conversion; do not erase the requirement |
| IRRW-05 | A genuinely unretained Module is removed under valid dependency/rewrite facts | Its requirements can be removed with it; surviving requirements remain unchanged |
| IRRW-06 | Inlining removes a machine import but retained layout/cleanup/value/code obligations still depend on B | Retain B's remaining obligations; instruction absence is not dependency discharge |
| IRRW-07 | Module identity is unchanged but admitted artifact/dependency/support/environment facts change | Revalidate affected admission; identity alone does not validate a cached decision |
| IRRW-08 | All facts relied on by an admission decision remain unchanged | Decision reuse is permitted under the applicable contract |
| IRRW-09 | Support is represented by a known revision label or producer checksum after replacement | Do not reuse admission solely on that basis |
| IRRW-10 | An arbitrary transformation upgrades semantics, clones shared context or discharges dependencies | Prove preservation and revalidate under complete transformation/binding rules; a finite graph model is not that proof |
| IRRW-11 | An arbitrary native runtime executes the admitted feature revisions together | Establish target/runtime/ABI and observational conformance independently |
| IRRW-12 | Optional debug data is removed or remapped | Requirements and governing semantics remain; optional data cannot own them |
