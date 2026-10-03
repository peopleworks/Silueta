# Compatibility and persistence

Silueta is preparing its first stable release. The packages are still previews; this document records
the contracts protected on `main` and the migration from `0.3.0-preview.3`. Package stability commits
to supported interfaces and stored data. Detection quality remains measured separately in the README.

## Public interfaces

The Core package is validated against the published `0.3.0-preview.3` NuGet package whenever it is
packed. CI packs it after building and testing. SDK Package Validation checks the two framework
assets, and APICompat checks the public surface, including parameter names used by named arguments.
The baseline is a build-time download, not a dependency of the shipped library.

Before 1.0 is published, this baseline protects existing consumers while allowing additions. Once
1.0 is released, the baseline must advance to that published package. Within 1.x, existing public
types, members, parameter names and supported signatures will remain compatible. APICompat does
not prove equivalent detection, thread safety or persisted JSON: those have separate tests.

Core supports .NET 9 and 10; CLI and MCP require .NET 10. A framework-support change will be announced
with migration instructions. CLI option names and exit meanings, MCP tool names and argument schemas,
and JSON field names are also integration contracts. The existing processing-limit/cancellation
tests and protocol probes cover these boundaries. This is preparation for 1.0, not a published 1.x
support policy or a promise to retain an unsupported runtime indefinitely.

## Stored formats

Readers recognize JSON property names without regard to case. Writers use camelCase. Subject ids and
codes are case-sensitive; invented-name lookup ignores case. Codes and ids are kept as supplied,
and stored invented names are trimmed. Files are written as UTF-8 without a BOM; the vault reader
recognizes a BOM when present. Dictionary order and indentation carry no meaning.

| File | Contract |
| --- | --- |
| Vault | `version: "2"`, required `subjects` object; each opaque subject has a nonempty unique `pseudonym`, optional `surrogate`, and optional `retired` string array (absent means empty). A code-only entry remains valid. |
| Lineage | Required nonempty `lineage` and `version`. `version` is the lineage author's content revision, not the vault format version. Existing pools, labels, generalizations, patterns, values, lists and policies retain their meanings. |
| Manifest | Existing field names and values survive deserialization/re-serialization. Hashes bind it to input/output; policy, lineage, detector and compiled-rule fingerprints describe the run. `engineVersion` describes the implementation, rather than a schema version. |

Additive manifest fields can be introduced with documented defaults. Consumers should ignore fields
they do not recognize. Breaking format changes need an explicit format transition and migration;
existing fields will not be silently reinterpreted. These typed readers are not generic JSON editors:
unrecognized fields are not preserved when data is reserialized. Future essential vault state must
use a new vault format version, which older readers refuse.

The compatibility fixtures were generated with the published preview.3 package, rather than with
the implementation under test. They pin code/name/history associations, lineage/policy fingerprints,
all existing manifest fields and the original example's output. See
[`Fixtures/preview.3`](../tests/Silueta.Core.Tests/Fixtures/preview.3/README.md).
Algorithm changes may change detector fingerprints and evaluation results; stored historical
manifests remain readable without being rewritten to describe the newer algorithm.

### Invalid vaults

An absent file starts an empty vault. An access error, directory, malformed JSON, `null`, missing
subjects, null entry/history, blank code, duplicate property/subject/code, or a name claimed by two
subjects does not. Loading fails completely, without returning a partially loaded vault. Parse and
integrity errors contain no private ids/names or inner JSON diagnostic exception.
File-format failures throw `InvalidOperationException`; filesystem access/I/O errors still propagate.

An empty vault is `{ "version": "2", "subjects": {} }`. Version 1 and unknown versions are refused.
Version 1 stored codes without the invented names needed to reconstruct the corpus; there is no
automatic migration that can recover those names. Keep that file and regenerate the corpus with a
new vault path if regeneration is appropriate for the caller's workflow.

## Saving one vault

`SaveTo` acquires an exclusive handle on an adjacent, empty `<vault-path>.lock` file, checks the
current vault, writes and flushes a temporary file in the same directory, then replaces the vault.
Readers can hold a complete previous generation while that replacement occurs. On Windows, a
program that holds the vault open without delete sharing (an editor, a backup) blocks the replacement:
that save is a conflict, and the vault is left as it was. Handled write failures clean up the temporary
file when filesystem permissions allow it.

The proposed state must retain every existing subject/code association, every active or retired
name's owner, and all retirement history. A remint can change the active name while retaining the
old name as retired. A stale copy, unrelated replacement vault or changed code is refused. Two
writers that read the same state therefore cannot silently discard each other's updates.

Contention and destructive saves throw `VaultWriteConflictException`. The message carries no private
values. Reload the vault and repeat the **complete redaction or remint**, including its checks,
before emitting any output. Saving an already-redacted transcript after independently choosing new
names would detach the transcript from its vault. The library does not automatically merge or retry.

CLI reports conflicts with exit **5**; MCP reports a sanitized tool error. Both save the vault before
writing output. A conflict emits no new transcript or manifest and preserves existing destinations.
CLI also refuses aliases between writable destinations and input/configuration files; vault and lock
paths must be distinct from emitted artifacts. MCP refuses a transcript/output that aliases its vault
or lock. A nonempty pre-existing lock-path file is refused and preserved.

The empty lock file stays after the handle closes. Its presence does not mean a writer is active.
Process death releases the handle, and the next writer can reuse the file. Keep the sidecar in place
while the vault is in use; deleting it can split the lock into two file identities on Unix.

These guards support cooperating updated writers using the same canonical path on a local filesystem,
with Windows and Linux probes. They do not coordinate older Silueta versions, external file editors,
path aliases/symlinks or writers on different hosts. Use an application-level transaction/lock for
those deployments. A mutable `PseudonymVault` instance is not thread-safe; concurrent runs need separate
instances and the save guard, or external serialization of the whole operation.

Vault, transcript and manifest writes are not one transaction. After the vault commits, a later output
write may fail; the vault's reservations are retained. In-memory assignments made before a processing
failure/cancellation are also retained. Flushing the temporary file does not establish a guarantee
against every filesystem/device failure or power loss at the rename.

Run the cross-process probe:

```powershell
dotnet run --project tools/Silueta.PersistenceProbe -c Release
```

It uses explicit barriers to exercise contention, holder-process death, two stale writers and
reload/retry. It uses synthetic assignments and deletes only its own temporary directory.

## Package consumption

CI also [validates the three installed packages](../tools/package-validation/README.md) with a local
feed, isolated caches and external Core consumers on both runtimes. The release build runs this
check on the exact packages that await approval, and includes their hashes and validation report
with the artifacts. This complements API validation by exercising bundled resources, tool entry
points, historical files and the MCP protocol outside the repository.

Gold-corpus documents may now include an optional `recordedOn` date (`yyyy-MM-dd`). The evaluator
uses it as the age reference, matching normal engine contexts. Missing dates retain the previous
run-date behavior. Existing `GoldDocument` constructors/deconstruction remain unchanged; its new
`RecordedOn` property is additive. New blinded evaluation sets should freeze a record date.

## Migrating from preview.3

Existing valid version-2 vaults and lineage files remain usable. Preserve their files and retired names;
do not create an empty vault over an existing corpus. Upgrade every writer, reserve the `.lock` path,
handle exit 5/the conflict exception, and rerun the whole operation after reloading on a conflict.
Files that were previously accepted by discarding invalid/ambiguous entries now fail instead.
Malformed JSON now produces a sanitized `InvalidOperationException`, rather than exposing a
`JsonException` diagnostic; callers that caught only `JsonException` should update that handling.

Transcript processing also has defaults of 1,000,000 UTF-16 characters and 100,000 candidate detections
per pass. Larger supported workloads can configure `RedactionLimits`, CLI limit flags or MCP operator
settings. A limit fails the run completely; it does not truncate input. Cancellation is cooperative,
with a final checkpoint before persistence; once persistence begins, the vault-first sequence completes.
