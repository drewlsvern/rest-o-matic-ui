# Handoff: starting the central app

Written 2026-10-02, at the end of the design session held in the
`rest-o-matic` repository. It is for whoever starts work here, with no memory
of that session. Read this first, then
[design/central-management.md](design/central-management.md) for the full
architecture.

## What this repository is

The **central app** for [rest-o-matic](https://github.com/drewlsvern/rest-o-matic),
a restic wrapper that runs on each server and pushes backups to that server's
own repositories.

rest-o-matic is a command-line tool with no daemon. With ten or more hosts
that causes three problems, which this app exists to solve:

1. Checking that backups work means SSHing into each host.
2. There is no single view of every host's backups.
3. Each host's config is saved nowhere else unless someone remembers to
   copy it.

It should also let config be managed from one place: adding and removing
jobs, backends and backup locations on any host, including hooks.

The host CLI lives in a separate repository, checked out beside this one at
`../rest-o-matic`.

## How the two fit together

Hosts call the central app. Nothing ever connects to a host.

```
host (cron → rest-o-matic tick)                      central app (this repo)
┌──────────────────────────────┐                    ┌────────────────────────┐
│ 1. check in                  │ ── report ───────▶ │ latest report per host │
│                              │ ◀─ config vN ───── │ config per host,       │
│                              │ ◀─ actions ─────── │   with history         │
│ 2. apply config (if newer)   │                    │ action queue           │
│ 3. run queued actions        │                    │ web UI                 │
│ 4. run due jobs              │                    │                        │
│ 5. report results            │ ── report ───────▶ │                        │
└──────────────────────────────┘                    └────────────────────────┘
```

Each host checks in about once a minute, from cron. So anything the UI shows
must come from what the central app already holds, and anything it asks a
host to do takes effect at that host's next check-in.

## Decisions that bind this repository

These were agreed with the project owner. Do not reopen them without a
reason.

**Stack**

- Blazor Server with MudBlazor. Not Blazor WebAssembly.
- SQLite for storage to begin with.
- Monaco as the config editor, for its built-in diff view.
- Runs as a container, on the tailnet only, behind a login.
- One process serves both the web UI and the check-in endpoints.

**Behaviour**

- The central app is the source of truth for each enrolled host's config and
  keeps every version, with who changed what and a diff before sending.
- Config is edited as YAML, not through forms. Forms would drop comments and
  YAML anchors, which existing configs use. The one guided flow is "add a
  backend".
- Hooks are editable here. A hook is a shell command, so this app can act as
  the backup user on every enrolled host. It is fully trusted and must be
  protected accordingly.
- The UI can queue arbitrary `rest-o-matic exec` commands for a host, to
  begin with. Buttons such as "initialise" are presets for that.
- Every host has its own restic repository on each backend. Repositories are
  never shared between hosts. A "template" is a saved backend or policy that
  is copied onto a host, not linked.
- Config is validated by running the real `rest-o-matic` binary inside this
  app's container, not by reimplementing the rules in C# and not through a
  Go shared library.

**Secrets are write-only**

- The central app stores secrets but can never read them. Each secret is
  encrypted ("locked") for the host that needs it and for an offline
  recovery key.
- A secret typed into the UI must be locked **in the browser by JavaScript**.
  In Blazor Server a bound input sends its value to the server, so a secret
  field must not be bound to Blazor at all.
- The app needs no .NET encryption library. It never unlocks anything.

**The landing page**

A list of hosts. Problem hosts are pinned first, in the order overdue,
failed, needs attention. Healthy hosts follow in alphabetical order. Column
headers are clickable and sort within each status group. A summary line shows
counts, with a "problems only" filter. The design document has the mock-up
and the status definitions.

## What the host side provides today

Two things the central app will consume already exist in `rest-o-matic`.
Neither is in a release yet (see [State of the host repository](#state-of-the-host-repository)).

### `rest-o-matic status --json`

One JSON document describing every job. This is the per-job part of what a
host will report at check-in:

```json
{
  "format_version": 1,
  "generated_at": "2026-10-02T05:38:25Z",
  "last_tick": "2026-10-02T05:38:23Z",
  "jobs": [
    {
      "name": "postgres",
      "schedule": "hourly",
      "repositories": ["nas"],
      "due": false,
      "next_due": "2026-10-02T06:00:00Z",
      "waiting": false,
      "running": null,
      "last_run": {
        "started": "2026-10-02T05:38:20Z",
        "finished": "2026-10-02T05:38:22Z",
        "outcome": "success",
        "trigger": "run",
        "error": null,
        "repositories": [
          {"name": "nas", "result": "ok", "error": null, "snapshot_id": "e33dd190f251..."}
        ]
      }
    }
  ]
}
```

- Times are UTC, RFC 3339. Values that don't apply are `null`, never missing.
- `outcome` is `success` or `failed`. `trigger` is `tick` or `run`.
- A repository `result` is `ok`, `backup_failed` or `forget_failed`.
- `running` is `{"started": ..., "trigger": ...}` while a job runs. `waiting`
  is true while it is queued behind a busy repository or slot.
- `due` and `next_due` say what the host's scheduler will do. A job that has
  never run has `due: true` and `next_due: null`.
- `status <job> --json` adds a `runs` array: that job's last 20 runs.
- `format_version` changes only for an incompatible change.

`status` reports facts and makes no judgement. Deciding that a host is
"overdue" is this app's job, since only it knows how often a host should be
checking in.

### Locked values in the config

A repository's `password` and its `env` values can be locked:

```yaml
repositories:
  offsite:
    backend: s3
    url: "s3:https://s3.example.com/bucket/restic/prd-podman-01"
    password: !locked "YWdlLWVuY3J5cHRpb24ub3JnL3YxCi0+IFgyNTUxOSA..."
    env:
      AWS_ACCESS_KEY_ID: AKIA...
      AWS_SECRET_ACCESS_KEY: !locked "YWdlLWVuY3J5cHRpb24ub3JnL3YxCi0+..."
```

- The value is a standard [age](https://age-encryption.org) encrypted file,
  base64-encoded (standard alphabet, with padding) onto one line, after the
  YAML tag `!locked`.
- It is encrypted to one or more X25519 age recipients (`age1...`): the
  host's public key, plus a recovery public key.
- To lock a secret in the browser, this app has to produce exactly that:
  age-encrypt the text to the host's and the recovery public keys, then
  base64-encode the binary result. The host can then unlock it.
- `!locked` is only valid on `password` and `env` values.
- `rest-o-matic validate` checks that locked values are well-formed without
  needing any key, so it works in this app's container.

## What does not exist yet

The central app cannot be built end to end until these exist on the host
side. None has been designed in detail.

| Missing | Needed for | Where it will be defined |
|---|---|---|
| The check-in itself: enrolment, the report payload, config delivery, the action queue | Everything beyond a mock | A written contract in `rest-o-matic`, with example payloads as fixtures |
| JSON output from `rest-o-matic validate` | Showing validation errors in the editor | `rest-o-matic` |
| A JSON Schema for the config file | Autocomplete and structural checks in Monaco | `rest-o-matic`, generated from its Go types |
| Snapshot lists in the report | The snapshot views | `rest-o-matic` |
| Rewriting an existing config's plain-text secrets into locked form | Enrolling a host that already has a config | `rest-o-matic` |

**The check-in contract is the first thing the two sides must agree on.** It
is planned as the next change in `rest-o-matic` ("enrolment and reporting").
The design document describes what it has to carry; nothing more specific
exists. Because Go and C# cannot share types, it needs a written description
and example payloads that both repositories test against.

## State of the host repository

As of 2026-10-02, in `../rest-o-matic`:

| Branch | Pull request | Contents |
|---|---|---|
| `fix/job-concurrency` | [#16](https://github.com/drewlsvern/rest-o-matic/pull/16) | A job never runs twice at once; a job waits for a busy repository instead of failing |
| `feat/job-status-reporting` | [#17](https://github.com/drewlsvern/rest-o-matic/pull/17) | The `status` command, run history, last tick time; the design document |
| `feat/locked-secrets` | [#18](https://github.com/drewlsvern/rest-o-matic/pull/18) | Locked config values and the `secret` commands |

Each branch is stacked on the one above it, and all three pull requests are
open and unmerged. None of this is on `main` or in a release. A container image for this app
cannot yet download a `rest-o-matic` binary that has `status` or understands
`!locked`. Until it is released, build the binary from those branches.

## Build and release workflows

Two GitHub workflows are already in `.github/workflows/`, written to mirror
the ones in `rest-o-matic`. **Neither has ever run**: when they were written
this repository had no .NET project, no Dockerfile and no GitHub remote. Treat
them as a starting point and fix them against the first real run.

**`ci.yml`** runs on every pull request into the default branch.

- `build` restores, builds and tests the solution in Release configuration.
  It looks for a `.sln` or `.slnx` file at the repository root, and skips
  with a notice while there is none.
- `container` builds the image from `Dockerfile` without pushing it, so a
  pull request can't quietly break the release build. It skips while there
  is no `Dockerfile`.

**`release.yml`** runs when a tag starting with `v` is pushed. Releasing is a
deliberate act, as it is in `rest-o-matic`: nothing is published because
something merged.

- The tag must be `vMAJOR.MINOR.PATCH`, optionally with a `-prerelease`
  suffix, and its commit must be reachable from the default branch.
- It builds a `linux/amd64` and `linux/arm64` image and pushes it to
  `ghcr.io/<owner>/<repository>`, tagged with the version. `latest` moves
  only for a release without a pre-release suffix.
- It creates a GitHub release whose notes list the commits since the
  previous tag, grouped into features, fixes and others by their
  conventional-commit prefix. A hyphenated tag is marked as a pre-release.

What the workflows expect from the project, so build it this way:

- **The solution file is at the repository root.**
- **A `Dockerfile` is at the repository root** and accepts a `VERSION` build
  argument, which the release sets to the tag without its `v`.
- **The Dockerfile's build stage cross-compiles.** Start it with
  `FROM --platform=$BUILDPLATFORM ...` and publish with
  `dotnet publish -a $TARGETARCH`, so only the small runtime stage runs
  under emulation for arm64. Building .NET under emulation is slow and
  unreliable.
- **The image includes released `rest-o-matic` binaries,** matching the
  image's architecture, because config is validated by running them.
- **Commits follow conventional commits** (`feat(scope): ...`,
  `fix(scope)!: ...`), which the release notes are grouped by.
- **The .NET SDK version** is `10.0.x` in `ci.yml`. If a `global.json` is
  added, switch the workflow to `global-json-file` so the two can't differ.

Not set up, and worth doing once the repository is on GitHub: a branch
protection rule that requires the CI check before merging. The local default
branch is `master`, where `rest-o-matic` uses `main`; the workflows accept
either name.

## A sensible order of work here

Nothing here is decided; it is a suggestion.

1. **Project skeleton.** Blazor Server and MudBlazor on .NET 10, SQLite
   through EF Core, a container build, and CI.
2. **Data model.** Hosts, the latest report per host, config versions,
   queued actions, an audit trail.
3. **The landing page against fixture data.** The host list can be built and
   reviewed from hand-written reports shaped like `status --json`, before any
   host can check in.
4. **The check-in endpoints,** once the contract exists.
5. **Config editing,** then **actions.**

Agreeing the check-in contract can happen in parallel with steps 1 to 3 and
is the item most worth starting early.

## Open questions

- The layout of the host page and every screen after the landing page.
- Login and user accounts.
- How the recovery key is set up and tested from the UI. The design document
  describes the test; nothing is designed in detail.
- How a host reports its rest-o-matic version, so the app can validate that
  host's config with the matching binary.

## Vocabulary

| Term | Meaning |
|---|---|
| Check-in | A host's call to the central app at the start of each `tick` |
| Report | What a host sends: the state of every job, snapshot lists, action results. A full picture each time, not a stream of events |
| Action | A one-off command queued here and run by the host at its next check-in |
| Drift | A host's config file no longer matches what the central app last sent, because someone edited it on the host |
| Template | A saved backend or policy that is copied onto a host |
| Locked | Encrypted so that only chosen keys can read it |
| Host key | A host's private key, which unlocks values locked for that host |
| Recovery key | An offline key that can also unlock them, for when a host is lost |
| Overdue | A host that has stopped checking in, or a job that is past due and has not run |
