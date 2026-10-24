# Consumer-verification conformance

These cases apply the verification contract in
[consumer verification](../representation/reader-trust.md) to the metadata and Body
formats. They specify producer truth obligations, mandatory structural
safe-consumption checks before affected use, and the separate scope of
optional semantic audits. Consumers are not required to re-prove producer
assertions. These checks do not prove arbitrary foreign-provider behavior or
authored unsafe promises.

| Case | Input or observation | Required outcome |
|---|---|---|
| ITRU-01 | A producer emits an artifact under the selected schema | Every emitted section, row, reference, Body, index and declared requirement satisfies the schema; consumers check the structure, bounds, references and support needed for safe affected use |
| ITRU-02 | A producer publishes semantic facts about a Body | Facts are true of the emitted Body and declared contracts; consumers may rely on them without re-proving represented semantics |
| ITRU-03 | A consumer uses producer-published facts after structural checks and support admission | Permitted; producer assertions remain the semantic authority, and no duplicate or independent semantic proof is required |
| ITRU-04 | A structurally plausible internal assertion is semantically false | The producer violates its truth obligation; the consumer is not required to detect or reject the assertion, though an optional semantic audit may |
| ITRU-05 | An artifact has malformed framing, an out-of-range/wrong-namespace reference, an unknown required form or structurally inconsistent records | Reject affected use before unsafe consumption; diagnostic wording and internal check order are implementation choices |
| ITRU-06 | Structural checks run during decoding, binding, reconstruction, lowering or in a dedicated pass | Any organization is permitted if required safe-consumption checks and support admission complete before affected use |
| ITRU-07 | An optional deeper audit examines facts outside mandatory verification | It claims only the scope and properties it actually examined; it does not prove arbitrary external behavior |
| ITRU-08 | A consumer lacks support for a required schema or semantic feature | It rejects affected use and makes no support claim for that input |
| ITRU-09 | A signature, checksum, producer identity or trust flag accompanies an artifact | It does not establish semantic truth or replace producer obligations or consumer verification |
