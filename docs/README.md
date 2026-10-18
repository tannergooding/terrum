# Terrum documentation

This directory is the project-facing guide to Terrum. It separates concise
project summaries from normative target contracts and unapproved proposals.

| Start here | Purpose |
|---|---|
| [Language overview](language.md) | Language direction and where to find the accepted contracts |
| [Implementation support](implementation.md) | What the hosted compiler and current self-host components implement |
| [Development roadmap](roadmap.md) | Pending work and its dependencies |
| [Build guide](building.md) | Repository build and test commands |
| [CLI machine protocol](specs/cli-protocol.md) | Command results, JSON schema, and diagnostics |
| [Sample catalog](samples.md) | Runnable examples and design sketches |
| [Scanner](scanner.md) | Executable scanner component and its boundary |
| [Native exception probe](native-seh.md) | Windows x64 exception-unwind evidence |
| [Normative specifications](specs/README.md) | Accepted target contracts and their scope |
| [Proposals](proposals/README.md) | Unapproved alternatives and decisions requiring review |

Specifications define target behavior; implementation support is tracked
separately. [Current acceptance is not stabilization](specs/overview/conformance.md):
proposals may recommend replacing accepted rules, reorganizing contracts or
reassigning draft IDs without preserving unpublished compatibility. A proposal
does not amend an accepted contract unless it is explicitly approved and
incorporated into the relevant specification.
