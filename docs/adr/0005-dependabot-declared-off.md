# ADR-0005: Dependency update automation declared, not opening pull requests

- **Status:** Accepted
- **Date:** 2026-10-11

## Context

REPO-BASELINE §1 lists dependency-update automation as a baseline item. The repository has no maintainer yet
who triages automated pull requests. Automation that opens unowned pull requests produces a queue that is
closed unread, after which the repository has worse hygiene than before, because it looks covered.

## Decision

`.github/dependabot.yml` declares every ecosystem the repository has (nuget, npm, github-actions, docker), each
with a schedule and **`open-pull-requests-limit: 0`**. Version-update pull requests are therefore not opened.
This is a deliberate deviation and is recorded in the register in
[`00-ARCHITECTURE.md`](../architecture/00-ARCHITECTURE.md).

Vulnerability detection is not affected by this setting. `NuGetAuditMode=all` with `NuGetAuditLevel=low` in
`Directory.Build.props` fails restore on a vulnerable package, transitive ones included. The CodeQL workflow
runs the dependency audit.

**Repository setting (not in this file):** Dependabot security updates and vulnerability alerts are controlled in
the GitHub repository settings. This repository does not set them, and their current state was **not verified
from the build environment**. The owner must check it and record the result here: _state: OPEN, to verify_.

**Trigger to turn this on:** a maintainer is named to triage dependency pull requests, or the repository goes
public. At that point raise `open-pull-requests-limit` per ecosystem, one line each.

## Options considered

| Option | For | Against |
|---|---|---|
| Declared, limit 0 | Reviewable config exists; turning it on is one line per ecosystem | Version freshness is not tracked |
| Enabled now | Freshness tracked from day one | Unowned queue; false sense of coverage |
| Absent | Nothing to maintain | Looks like an omission; reversal is a research task |

## Consequences

Stale versions are possible until the trigger is met. Security advisories still block restore and show in CodeQL.
The register row has an exit condition, so this does not become permanent by forgetting.
