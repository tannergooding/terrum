# TRRM format identity and version stability

## 1. Format and schema

These rules define `trrm-format-versioning/1`. The
[metadata header](../il-metadata.md#header-and-directory) carries the `TRRM`
signature and the little-endian `u16` major/minor pair. There is no separate
on-wire encoding-schema identifier, preview-build identifier or release marker.
The pair is an encoding identity, not a semantic edition, Feature revision,
target requirement or runtime policy.

One artifact uses one exact encoding schema for all its sections, rows and
Bodies. Separately supplied artifacts can use different supported schemas;
packing them requires one destination schema capable of representing all
retained obligations under [requirements and admission](requirements-and-admission.md).

## 2. Beta, draft and preview artifacts

The current format is pre-release and uses `(1, 0)`. Beta, draft and preview
schema changes do not require a major/minor update. There is no expectation
of compatibility between drafts or preview builds, and no obligation to
retain their readers, writers, artifacts or migration shims.

Exchange requires exact agreement on the selected draft schema, including its
governing semantic catalog and chapters, not only the
[allocation registry](registry.md). The [Module manifest](semantic-manifests.md)
does not replace this draft agreement. That agreement is
out of band: equal version bytes, a registry digest, a build number, a filename
or a native container do not establish it. A consumer must not discover an
alternate draft decoder by trying plausible row layouts or opcode meanings.

The current-schema consumer accepts `(1, 0)` and rejects other pairs. This
does not make every earlier `(1, 0)` draft current-schema input. A tool may
explicitly select a historical draft schema without making that schema an
alias, compatibility mode or support obligation of the current specification.

## 3. Released encoding schemas

For released versions, each assigned `(major, minor)` pair identifies one
stable, exact encoding schema. An assignment is not reinterpreted or reused
for a different schema. Releasing a different encoding schema requires a
different pair; this rule does not impose pair changes on pre-release drafts.
Source semantic editions and Feature revisions remain independent where the
unchanged encoding can represent them under its specified rules.

A consumer uses the exact released-schema assignment and declares which
schemas it supports. Equal major versions, a higher minor version or numeric
ordering do not imply compatible decoding, backward support or roll-forward.
An unsupported pair rejects; a known pair does not supply implementation
support for its required semantics.

The pair does not reveal whether bytes came from a released producer or a
preview that reused that pair. Exchange must select the intended schema;
claimed provenance does not convert preview bytes into released-schema input.
This specification assigns no released schema yet.

## 4. Admission and transformation

Schema selection precedes interpretation of schema-specific payloads. The
selected schema's header/directory framing, reserved values, reference bounds
and natural access checks remain mandatory under [reader trust](reader-trust.md).
Neither exact version agreement nor producer trust requires a preliminary
all-Body scan or proves arbitrary body facts.

The encoding identity does not replace a Module's governing semantic edition,
required Feature revisions, retained dependencies, complete binding contracts,
target or runtime requirements. Those remain separate before-use obligations.
Current governing edition `1` with empty Feature ranges means only the explicitly
bounded interchange baseline, not support for the complete source language.

Rewriting only the version pair is not an encoding conversion or semantic
upgrade. A conversion explicitly selects its source and destination schemas,
preserves decoded meaning, and rebuilds affected fields, references and indexes.
Optional source/debug data cannot carry missing required semantics. No such
conversion is required for unpublished drafts.

[Conformance cases](../conformance/il-format-versioning.md) distinguish
draft agreement, stable released assignments, numeric noncompatibility and
unchanged physical admission. They do not assign a release or activate a
semantic manifest, Feature ID, opcode or richer value/owner/handler codec.
