# Module semantic-manifest conformance

These cases apply [semantic manifests](../representation/semantic-manifests.md),
[requirements and admission](../representation/requirements-and-admission.md),
the [registry](../representation/registry.md) and
[metadata framing](../il-metadata.md#header-and-directory).
The current schema is `trrm/1-draft-unified-3`, format `(1, 0)`.

[Fixed vectors](il-semantic-manifest-vectors.json) contain actual complete
current-schema bytes. Edition `1` with no extra Feature is admitted subject to
the other baseline contracts. A known-shape Feature row can be inspected but
its unregistered required identity rejects semantic use.

The additional prerequisite, support and compatibility catalogs are explicit
schematic supplied facts. They test the protocol without registering those
Feature IDs or another Terrum edition. Finite graph and byte-rewrite assessments
do not prove the facts' semantic truth, source-obligation inference, arbitrary
binding, general transformation or native execution.

## 1. Physical manifests and ordinary encodings

| Case | Situation | Required result |
|---|---|---|
| SMNF-01 | Decode the complete baseline vector | Recover its 48-byte Module, identity, governing edition `1` and empty Feature set |
| SMNF-02 | Decode an earlier 44-byte Module under the current schema | Reject wrong stride; no same-pair draft decoder inference |
| SMNF-03 | Truncate a complete vector at every byte boundary | Reject before out-of-extent access or successful partial admission |
| SMNF-04 | Module is absent | Reject; no artifact-wide default manifest |
| SMNF-05 | Pair differs, a header reserved byte is nonzero, or required directory kind is unknown | Reject at schema/framing admission regardless of selected Module |
| SMNF-06 | `governingRuleEdition=0` | Reject missing exact edition, not the consumer's default |
| SMNF-07 | An exact nonzero governing edition is unknown | Permit known-shape inspection; reject its retained semantic use |
| SMNF-08 | Edition/Feature/revision fields have high bits set | Treat all 32 bits as semantic identity, not a 24-bit row or DefId |
| SMNF-09 | Feature section length does not equal `8 * recordCount` | Reject fixed-row extent |
| SMNF-10 | Feature range is `(0, 0)`, `(nonzero, 0)` or `(0, nonzero)` | Only `(0, 0)` is empty; the other encodings reject |
| SMNF-11 | Range exceeds actual rows, has a high-byte first row or overflows a 32-bit sum | Reject by checked bounds; do not wrap, truncate or allocate from the unchecked count |
| SMNF-12 | Current edition `1` has no explicit Feature | Retain its defined bounded baseline; this is not complete source-floor codec support |
| SMNF-13 | Current Module requires a nonzero unregistered Feature pair | Recover its two words for inspection; reject unsupported required semantics |
| SMNF-14 | Two Modules share one immutable Feature list | Recover requirements independently; sharing does not merge Module contexts |
| SMNF-15 | Explicit list is reordered or repeats identical pairs | Ordinary meaning is unchanged; canonical list audit rejects order/duplicates |
| SMNF-16 | One Module explicitly requires two revisions of the same Feature | Reject conflict, not a maximum or alternate branch |

## 2. Complete requirements and support

| Case | Situation | Required result |
|---|---|---|
| SMNF-17 | Explicit Feature has transitive exact prerequisites absent from its physical list | Derive and admit the complete catalog closure; no duplicate wire rows required |
| SMNF-18 | A complete finite prerequisite graph has repeated paths or cycles | Visit exact obligations once and reach finite closure |
| SMNF-19 | A prerequisite's exact revision is unknown | Reject the incomplete required closure |
| SMNF-20 | Feature revision has no defined interpretation under the Module's edition | Reject incompatible interpretation |
| SMNF-21 | Consumer supports a larger or smaller revision but not the required one | Reject; `ruleRevision` is not a minimum |
| SMNF-22 | Applicable directed support mapping is supplied, or only its reverse is supplied | Use only the defined direction and preserve the required identity |
| SMNF-23 | Retained Modules require different exact editions/revisions | Keep each context; support all retained requirements without taking maxima |
| SMNF-24 | A retained provider carries unsupported requirements in uncalled code | Reject before affected use; selected entry behavior is not the requirement closure |
| SMNF-25 | Unretained Module has unsupported known-shape requirements | Do not claim its execution; selected trusted access need not decode its Feature list or Body |
| SMNF-26 | An actual selected access exposes a zero Feature identity or invalid range | Reject exposed invalidity even under producer trust |
| SMNF-27 | Named Feature lacks a required representation layer | Reject affected use despite numeric allocation or semantic support |
| SMNF-28 | Optional section/name is offered instead of a registered requirement | It cannot activate the capability or supply a missing required definition |
| SMNF-29 | Module flags or target/runtime fields have unknown values | Reject accessed schema invalidity; no hidden manifest selector |
| SMNF-30 | Feature row is not referenced by any Module | Reject a complete structural-validity claim; trusted selected use adds no preliminary global ownership scan |

## 3. Re-encoding and retained contexts

| Case | Situation | Required result |
|---|---|---|
| SMNF-31 | Re-encode supplied earlier complete artifacts with explicit governing edition `1` | Grow each Module by four bytes, update exact lengths/directory offsets and preserve non-Module payloads |
| SMNF-32 | Opcode-looking bytes occur in values, row fields or optional payloads during that rewrite | Leave every non-Module payload byte unchanged |
| SMNF-33 | Packing changes Module/list row order | Preserve identities, editions and effective requirements while rebuilding ranges |
| SMNF-34 | A genuinely unretained Module/list is trimmed | Retain surviving Module obligations; remove only its discharged rows/requirements |
| SMNF-35 | A rewriter relabels an edition or chooses the greatest Feature revision | This changes the manifest; it is not the original required interpretation |
| SMNF-36 | Provider is selected through ModuleRef or a packed direct edge | Provider keeps its own manifest; the consumer's edition is not substituted |
| SMNF-37 | Current draft adopts these rows while retaining `(1, 0)` | Explicit draft schema changes to `unified-3`; no new header/schema-ID or preview compatibility requirement |
| SMNF-38 | Provisional output profile `1`, a witness/member name or a held opcode is presented as a capability | Reject unregistered/incomplete use; no richer API/value/program/handler/root codec is activated |
| SMNF-39 | Infer every actual requirement from arbitrary source, values, ownership or private bodies | Document-only: complete publication and source-obligation inference need independent evidence |
| SMNF-40 | Prove arbitrary binding, effects, unsafe/body facts or native execution from a support decision | Document-only: support does not establish those independent contracts |
| SMNF-41 | Perform arbitrary schema conversion, semantic upgrade or dependency discharge | Document-only: preserve actual meaning, rebuild affected facts and revalidate; supplied-byte insertion is not that proof |
| SMNF-42 | Manifest is offered as a full native/managed/view/task/foreign profile | Document-only: exact ABI, runtime, owner/root and missing capability codecs remain independently gated |
