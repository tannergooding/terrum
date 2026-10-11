# Native package identity conformance cases

These cases apply to the [native identity contract](../native.md#native-package-identity-and-filename-collisions).
Package syntax/capacity and filesystem relations are explicitly supplied
profile facts, not inference from a private container or a host string comparer.

| Case | Situation | Required outcome |
|---|---|---|
| NPKI-01 | Exact valid ASCII identity matches its retained spelling | Ordinal match |
| NPKI-02 | A claimed identity contains non-ASCII text | Reject this native identity domain |
| NPKI-03 | Empty identity or violation of the selected packager's name grammar | Reject, not a path/default identity |
| NPKI-04 | Foo and foo are compared semantically | Distinct |
| NPKI-05 | Resolver sees an equal-folded but differently cased candidate | Not a match; no argument-order preference |
| NPKI-06 | Duplicate/conflicting provider identity claims | Reject ambiguity |
| NPKI-07 | Foo/foo map to colliding ordinary Windows DLL basenames | Reject deployment separately from semantic equality |
| NPKI-08 | A supplied target relation keeps those load paths distinct | No collision from portable ASCII folding alone |
| NPKI-09 | Closure preflight finds a filename collision | No staging, overwrite or launch effect |
| NPKI-10 | Distinct filenames or equal exports are used as identity/compatibility proof | Insufficient; retain exact semantic/provider contracts |
| NPKI-11 | All actual target path aliases, filesystem/loader behavior and staging | Document-only: supplied filename relations are not a portable filesystem implementation |
| NPKI-12 | Actual private package readers, resolver/replacement and native launch | Document-only: name checks do not prove complete admission or deployment |
