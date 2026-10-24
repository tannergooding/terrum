# Module semantic-manifest conformance

These cases apply [semantic manifests](../representation/semantic-manifests.md),
[requirements and admission](../representation/requirements-and-admission.md),
the [registry](../representation/registry.md) and
[metadata framing](../il-metadata.md#header-and-directory).
The current schema is `trrm/1-draft-register-il-1`, format `(1, 0)`.

[Fixed vectors](il-semantic-manifest-vectors.json) contain actual complete
current-schema bytes. Edition `1` with no extra Feature is interpreted under the accepted TRRM 1.0
baseline, subject to all other declared requirements and contracts. Empty
Feature ranges do not establish implementation support or create a reduced GA
floor. A known-shape Feature row can be inspected but its unregistered
required identity does not establish a support claim.

The additional prerequisite, support and compatibility catalogs are explicit
schematic supplied facts. They test closure and mapping behavior without
claiming additional Feature registrations or Terrum editions. Feature IDs
9-12 are assigned in the registry, while their complete capability
registrations remain pending. Cases describing malformed manifests require
verification to reject affected use; they do not prescribe diagnostic wording
or verifier organization.

## 1. Physical manifests and ordinary encodings

| Case | Situation | Required result |
|---|---|---|
| SMNF-01 | Decode the complete baseline vector | Recover its 48-byte Module, identity, governing edition `1` and empty Feature set |
| SMNF-02 | Producer emits an earlier 44-byte Module under the current schema | Producer violates the selected stride; no same-pair draft decoder is assigned |
| SMNF-03 | Producer truncates a complete vector at a byte boundary | Producer violates the selected schema; verification rejects before affected use |
| SMNF-04 | Module is absent | Producer omits a required manifest; verification rejects before binding or use |
| SMNF-05 | Pair differs, a header reserved byte is nonzero, or required directory kind is unknown | No current-schema meaning is assigned; verification rejects before affected use |
| SMNF-06 | `governingRuleEdition=0` | Producer omits the exact edition; the consumer's default does not supply it |
| SMNF-07 | An exact nonzero governing edition is unknown | Known-shape inspection may be supported; the edition does not establish a semantic support claim |
| SMNF-08 | Edition/Feature/revision fields have high bits set | Treat all 32 bits as semantic identity, not a 24-bit row or DefId |
| SMNF-09 | Producer emits a Feature section whose length differs from `8 * recordCount` | Producer violates the fixed-row extent; verification rejects before affected use |
| SMNF-10 | Feature range is `(0, 0)`, `(nonzero, 0)` or `(0, nonzero)` | Only `(0, 0)` is a valid empty range; producers do not emit the other encodings |
| SMNF-11 | Range exceeds actual rows, has a high-byte first row or overflows a 32-bit sum | Producer violates the range contract; verification rejects before affected use |
| SMNF-12 | Current edition `1` has no explicit Feature | Retain the exact accepted semantic edition; do not infer a reduced conformance floor or full implementation support from the empty range |
| SMNF-13 | Current Module requires an unknown or incompletely registered Feature pair | Its words do not complete semantics or establish a support claim; reject affected use |
| SMNF-14 | Two Modules share one immutable Feature list | Recover requirements independently; sharing does not merge Module contexts |
| SMNF-15 | Explicit list is reordered or repeats identical pairs | Ordinary meaning is unchanged; canonical list audit rejects order/duplicates |
| SMNF-16 | One Module explicitly requires two revisions of the same Feature | Producer violates the nonconflicting-closure rule; do not take a maximum or invent an alternate branch |

## 2. Complete requirements and support

| Case | Situation | Required result |
|---|---|---|
| SMNF-17 | Explicit Feature has transitive exact prerequisites absent from its physical list | Derive and admit the complete catalog closure; no duplicate wire rows required |
| SMNF-18 | A complete finite prerequisite graph has repeated paths or cycles | Visit exact obligations once and reach finite closure |
| SMNF-19 | A prerequisite's exact revision is unknown | No support claim for the incomplete required closure |
| SMNF-20 | Feature revision has no defined interpretation under the Module's edition | No support claim for that interpretation |
| SMNF-21 | Consumer supports a larger or smaller revision but not the required one | No support claim; `ruleRevision` is not a minimum |
| SMNF-22 | Applicable directed support mapping is supplied, or only its reverse is supplied | Use only the defined direction and preserve the required identity |
| SMNF-23 | Retained Modules require different exact editions/revisions | Keep each context; support all retained requirements without taking maxima |
| SMNF-24 | A retained provider carries unsupported requirements in uncalled code | No support claim for that retained closure; selected entry behavior is not the complete requirement set |
| SMNF-25 | Unretained Module has unsupported known-shape requirements | Do not claim its execution; selected access need not decode its Feature list or Body |
| SMNF-26 | A producer emits a zero Feature identity or invalid range | Producer violates the manifest contract; verification rejects before affected use |
| SMNF-27 | Named Feature lacks a required representation layer | No support claim for affected use despite numeric allocation or semantic support |
| SMNF-28 | Optional section/name is offered instead of a registered requirement | It cannot activate the capability or supply a missing required definition |
| SMNF-29 | A producer emits unknown Module flags or target/runtime values | Producer violates the schema; verification rejects before affected use and no hidden manifest selector is assigned |
| SMNF-30 | Feature row is not referenced by any Module | Producer violates the ownership rule; verification detects the orphaned required metadata before support is claimed |

## 3. Re-encoding and retained contexts

| Case | Situation | Required result |
|---|---|---|
| SMNF-31 | Re-encode supplied earlier complete artifacts with explicit governing edition `1` | Grow each Module by four bytes, update exact lengths/directory offsets and preserve non-Module payloads |
| SMNF-32 | Opcode-looking bytes occur in values, row fields or optional payloads during that rewrite | Leave every non-Module payload byte unchanged |
| SMNF-33 | Packing changes Module/list row order | Preserve identities, editions and effective requirements while rebuilding ranges |
| SMNF-34 | A genuinely unretained Module/list is trimmed | Retain surviving Module obligations; remove only its discharged rows/requirements |
| SMNF-35 | A rewriter relabels an edition or chooses the greatest Feature revision | This changes the manifest; it is not the original required interpretation |
| SMNF-36 | Provider is selected through ModuleRef or a packed direct edge | Provider keeps its own manifest; the consumer's edition is not substituted |
| SMNF-37 | Current draft adopts these rows while retaining `(1, 0)` | Explicit draft schema changes to `trrm/1-draft-register-il-1`; no new header/schema-ID or preview compatibility requirement |
| SMNF-38 | A provisional feature number, witness/member name or assigned opcode is presented as a complete capability | No support claim; exact registration, prerequisite closure and every required codec must be present |
| SMNF-39 | Infer every actual requirement from arbitrary source, values, ownership or private bodies | Document-only: complete publication and source-obligation inference need independent evidence |
| SMNF-40 | Prove arbitrary binding, effects, unsafe/body facts or native execution from a support decision | Document-only: support does not establish those independent contracts |
| SMNF-41 | Perform arbitrary schema conversion, semantic upgrade or dependency discharge | Document-only: preserve actual meaning, rebuild affected facts and revalidate; supplied-byte insertion is not that proof |
| SMNF-42 | Manifest is offered as a full native/managed/view/task/foreign profile | Document-only: exact ABI, runtime, owner/root and missing capability codecs remain independently gated |
