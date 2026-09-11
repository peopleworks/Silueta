# Security policy

## Reporting

Report privately through **[GitHub Security Advisories](https://github.com/peopleworks/Silueta/security/advisories/new)**.
Not in an issue, and not in a pull request.

**Never send real data.** Not a transcript, not a roster, not a vault. If you found a leak, reproduce it
with invented names — the library is deterministic, so a made-up example that fails here will fail there.
A report containing real personal data will be deleted and answered with this paragraph.

## What counts as a vulnerability

A missed identifier is the bug this library exists to have fewer of, so it is a **bug**, not a
vulnerability: open an issue with a synthetic reproduction. What we treat as a security report:

- Identifiable text reaching somewhere the policy says it cannot — a log, an exception message, a
  manifest, a serialised detection, a surrogate that echoes the original.
- Anything that makes a pseudonym reversible without the vault: a code derived from the person, a seed
  that leaks, a surrogate that is a function of the real name rather than of the subject id.
- Denial of service through a crafted pattern pack or input (catastrophic backtracking is a live risk in
  any regex-driven detector; the packs run with a timeout, and a way around it is a finding).

## Threat model, stated plainly

Silueta removes identifiers from text and measures how often it fails. It does not, and will not,
claim to:

- **certify compliance** with HIPAA, GDPR or anything else — that is a decision for counsel, and an
  expert determination is a person signing their name;
- **protect the paths around it** — logs, retry queues, caches, crash reports, provider-side copies and
  backups carry identifiable text past any redactor. The de-identifier belongs behind a single egress
  point that denies by default;
- **de-identify audio** — a voice is an identifier. Silueta works on text.

## Supported versions

While the project is pre-1.0, only the latest published version is supported.
