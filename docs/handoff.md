# Handoff: starting the central app

Written 2026-10-02, at the end of the design session held in the
`rest-o-matic` repository. It is for whoever starts work here, with no memory
of that session. Read this first, then
[design/central-management.md](design/central-management.md) for the full
architecture.

## Since this was written

The project skeleton now exists (the `project-skeleton` change): a solution
at the repository root, a Blazor Server and MudBlazor app in
`src/RestOMatic.Web` organised as vertical slices, SQLite through EF Core
with an empty database context, a `/healthz` endpoint, tests, and a
`Containerfile`. It has no entities, no pages beyond a placeholder and no
login (since added, see below), and the image does not yet contain a
`rest-o-matic` binary. The sections below have been updated where the
skeleton changed them.

**Host side, as of 2026-10-05.** The check-in now exists, with a written
contract, and so do JSON output from `validate` and a JSON Schema of the
config. All of it is in the pre-release **`v0.2.0-rc.1`**.
[What the host side provides today](#what-the-host-side-provides-today) and
[What does not exist yet](#what-does-not-exist-yet) are updated; the
check-in endpoints can now be built against the contract.

**Viewer key dropped, 2026-10-05.** File listings are no longer encrypted for
a viewer key; encrypting them is deferred. Sign-in was password-based only
because of that key, so how users sign in is open again: a password, an
OIDC provider, Tailscale identity, or a mix. See
[Browsing a snapshot](#browsing-a-snapshot) and
[Open questions](#open-questions).

**Sign-in exists, 2026-10-05** (the `password-sign-in` change). Users sign
in with a user name and password. Sessions are held on the server, in
memory: the cookie carries only a session key, a restart signs everyone out,
and sign-out or a password change ends sessions at once. Every page and
endpoint needs a signed-in user unless it is explicitly opened (`/healthz`,
static assets, the sign-in flow). A new install creates its first user with
a one-time token from the log, and `reset-password` run in the container
recovers a lost password. Users are all equal and have a user name, an email
(stored, not verified, never used to sign in), an optional display name and
a profile picture. The app trusts forwarded headers only from configured
proxy addresses (Caddy for now). The design of that change, in
`openspec/changes/`, explains how OIDC fits in later: every sign-in method
ends in `AccountSessions.SignInAsync`, and external logins get a table of
their own beside the password one. **The check-in endpoints must not use the
cookie:** they need their own bearer scheme, and the fallback policy means
they must declare which.

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

Everything below is in the pre-release `v0.2.0-rc.1` of `../rest-o-matic`;
see [State of the host repository](#state-of-the-host-repository).

- [Enrolment and the check-in](#enrolment-and-the-check-in): the contract for
  the endpoints this app serves.
- [`rest-o-matic status --json`](#rest-o-matic-status---json): the shape of
  each job in a check-in's *status* part.
- [Locked values, `!plain`, and withheld configs](#locked-values-in-the-config).
- [`validate --json` and the config schema](#validate---json-and-the-config-schema):
  for the config editor.

### Enrolment and the check-in

**The contract is in `../rest-o-matic/contract/checkin/v1/`**: a README
describing the exchange, a JSON Schema for each message, and examples. The
host's tests check every message it sends against those schemas; this app's
tests should check its replies against the same files, and parse the
examples. That folder is the source of truth; what follows is a summary.

**Enrolment.** The user creates a host here and is shown a one-time command:

```sh
 rest-o-matic enrol https://backups.example.com --token <token>
```

Show it with the leading space, which keeps it out of bash history where
`HISTCONTROL` includes `ignorespace` (not Fedora's default; the host docs
say so). The host sends `POST /api/v1/enrol` with the token, its age public
key and what it is (hostname, OS, architecture, rest-o-matic and restic
versions). This app checks and spends the token, and replies with:

- `host_id` and `host_name`;
- `credential`: a long random secret the host sends as
  `Authorization: Bearer` on every check-in. **Store only a hash of it.**
  Deleting the host here is how it is revoked;
- `recovery_recipients`: recovery public keys to offer the host. The host
  shows each new one and adds it only once the user confirms it.

A bad token gets `401` with error code `token_rejected`.

**The check-in.** Every `tick` of an enrolled host starts with
`POST /api/v1/checkin`. It is a heartbeat carrying the host's ID, `sent_at`,
the same host description, and three **parts**, each with a `fingerprint`
(`sha256:<hex>`) and a `content` that is null unless the part changed since
this app last acknowledged it:

| Part | Content |
|---|---|
| `status` | `{"jobs": [...]}`: each job as `status <job> --json` reports it, with its runs, and snapshot lists as summaries only |
| `snapshots` | `{"jobs": {"<job>": {"<repository>": {"listed_at", "snapshots": [...]}}}}`: every recorded snapshot list in full |
| `config` | The config file's exact text, as a string |

- Treat fingerprints as opaque. Keep the last one received per part and
  compare; never compute one.
- A `200` reply acknowledges every part the request included. Its `resend`
  lists parts the host must include in full next time, for example after
  this app loses its database. `config` (null) and `actions` (empty) in the
  reply are reserved for config delivery and the action queue.
- Two overlapping check-ins may both carry a part; keep the one with the
  later `sent_at`.
- Use this app's own clock for "last seen" and overdue. `sent_at` only
  orders reports from one host.
- Bodies over 32 KiB arrive gzip-compressed (`Content-Encoding: gzip`);
  enable ASP.NET Core's request decompression.
- The host gives up after 10 seconds (60 when it carries content), so reply
  quickly and do slow work afterwards.
- Errors are `{"error": {"code": "...", "message": "..."}}`. Use
  `credential_rejected` (401) for an unknown credential: the host then says
  it must be enrolled again. `unsupported_format` (400) and
  `invalid_request` (400) are the other codes the host recognises.
- The host doesn't follow redirects, and requires HTTPS unless it was
  enrolled with `--allow-http`.

**Withheld config.** The config part carries no text while any repository
password or `env` value is plain text (see below). Instead `withheld` names
the fields, and is sent on every check-in while it applies. Show it on the
host page: the config isn't backed up here until those values are locked.

**The rest-o-matic version** is in every check-in (`host.rest_o_matic_version`),
which is what selects the binary to validate that host's config with.

On the host, `rest-o-matic status --json` also gains a `checkin` key
(enrolment and last check-in). It describes the host's view of this link
and is not part of what is sent.

### `rest-o-matic status --json`

One JSON document describing every job. Each job in a check-in's *status*
part has this shape:

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
- `format_version` changes only for an incompatible change. Keys are added
  without changing it, so ignore keys you don't know.

Two keys have been added to each job since the example above was captured:

- `failing_since`: when the first run of the job's current run of failures
  started, or `null` if its last run succeeded. Use it for "failing for 3
  days".
- `snapshot_lists`: one entry per repository the job backs up to, in config
  order. Not in `v0.1.0-rc.1`; see the state table below.

```json
"failing_since": null,
"snapshot_lists": [
  {"repository": "nas", "listed_at": "2026-10-02T02:01:17Z", "count": 3, "newest": "2026-10-02T02:00:05Z"},
  {"repository": "offsite", "listed_at": null, "count": 0, "newest": null}
]
```

`listed_at` is when the host last recorded that list, which is each time
the job finishes a run against that repository. It is `null` when none has
been recorded. With `status <job> --json`, each entry also has `snapshots`,
newest first:

```json
{"id": "49bcad91e0f1...", "short_id": "49bcad91", "time": "2026-10-02T02:00:05Z",
 "hostname": "prd-podman-01", "paths": ["/home/me/gitea"], "tags": ["gitea"],
 "size": 1288490188, "files": 4212,
 "files_new": 12, "files_changed": 3, "data_added": 60000000, "data_added_packed": 47185920}
```

`size` and `files` are what the backup processed. `files_new`,
`files_changed`, `data_added` and `data_added_packed` are what that snapshot
changed compared with the one before it (bytes added before and after
compression). All six are `null` on hosts running restic older than 0.17.

Errors (`error` on a run or a repository) are plain sentences since
`v0.1.0-rc.1`, for example `restic backup failed: exit status 12: wrong
password or no key found`. They can contain file paths.

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
- `rest-o-matic secret relock` locks every locked value in a config again
  for the current host key and recovery keys, keeping everything else in
  the file byte for byte. A host runs it after a recovery key is added
  (enrolment offers it), or on a replacement host. Secrets stay
  write-only: this app never needs it.

**`!plain` and withheld configs.** A config is not sent to this app while it
holds a secret in plain text, which means any repository `password` or `env`
value not written `!locked`, with two exceptions:

- `env` names on a fixed list of harmless settings: `AWS_DEFAULT_REGION`,
  `AWS_REGION`, `RESTIC_COMPRESSION`, `RESTIC_PACK_SIZE`,
  `RESTIC_READ_CONCURRENCY`, `RESTIC_CACHE_DIR`, `TMPDIR`, `GOMAXPROCS`;
- `env` values written `!plain "..."`, which marks a value as deliberately
  not secret. It is only valid on `env` values.

Account identifiers such as `AWS_ACCESS_KEY_ID` count as secrets.
`password_file` and `password_command` don't. The config editor here has to
keep to the same rule, and should offer to lock a value or mark it `!plain`.

### `validate --json` and the config schema

For the config editor. The contract is in
`../rest-o-matic/contract/validate/v1/`: a README, the output's JSON Schema,
and examples captured from real output.

**Validating.** Write the candidate config to a temporary file and run the
binary matching the host's `rest_o_matic_version`:

```sh
rest-o-matic --config /tmp/candidate.yaml validate --json
```

```json
{
  "format_version": 1,
  "rest_o_matic_version": "v0.2.0",
  "valid": false,
  "problems": [
    {"severity": "error", "message": "references undefined policy \"hott\"",
     "job": "docs", "repository": null, "path": "backups.docs.policy", "line": 11, "column": 13},
    {"severity": "warning", "message": "\"hook\" is not a key rest-o-matic reads, and is ignored; did you mean \"hooks\"?",
     "job": "docs", "repository": null, "path": "backups.docs.hook", "line": 12, "column": 5}
  ]
}
```

- Always exactly one document on standard output, even for a YAML syntax
  error or a missing file; nothing on standard error. Exit status 0 when
  `valid`, 1 when not.
- `problems` map straight onto Monaco markers. `line` and `column` are
  1-based, columns in characters; either can be null.
- It needs no host key, state directory or source paths. Locked values are
  only checked for being well-formed. The one machine-specific check is
  `read_as: podman-unshare`, judged against the OS validate runs on, so a
  macOS host's error for it won't show here; the host still rejects it.
- **Unknown keys are warnings, not errors**, and the host ignores them. A
  misspelt `hooks:` means the hooks don't run, so make these warnings hard
  to miss in the editor.

**The schema.** `rest-o-matic schema` prints a draft-07 JSON Schema of the
config, for monaco-yaml's completion, hover descriptions and structural
checks. Take it from the same binary that validates, so it matches the
host's version. The schema sees `!locked` and `!plain` values as strings;
tell monaco-yaml about the tags with
`customTags: ["!locked scalar", "!plain scalar"]`, or it flags them. The
schema rejects unknown keys, where the host only warns, so the editor is
stricter than the host on that one point.

## What does not exist yet

Enrolment, the check-in, snapshot lists in the report, `validate --json` and
the config schema all exist now (above). Still missing on the host side,
none designed in detail:

| Missing | Needed for | Where it will be defined |
|---|---|---|
| Config sent from this app to a host, applied and acknowledged or rejected | Editing config centrally | `rest-o-matic`; the check-in reply's `config` key is reserved for it |
| The action queue, and an upload endpoint for action results | `exec`, "initialise", file listings | `rest-o-matic`; the reply's `actions` key is reserved for it |
| Listing the files inside a snapshot | Browsing a snapshot and restoring from it | `rest-o-matic`, as a queued action. See [Browsing a snapshot](#browsing-a-snapshot) |
| Locking an existing config's plain-text secrets in place | Enrolling a host that already has a plain-text config, so its config can be sent | `rest-o-matic`. Today the user locks each value by hand with `secret lock` |

The two contracts both repositories test against are in
`../rest-o-matic/contract/`: `checkin/v1` and `validate/v1`. Because Go and C#
cannot share types, a format change is proposed there first.

## State of the host repository

As of 2026-10-05, in `../rest-o-matic`:

Everything below is merged to `main` and in the pre-release
**`v0.2.0-rc.1`** (2026-10-05). The last column says which rows were already
in `v0.1.0-rc.1`.

| What | Pull request | In `v0.1.0-rc.1` |
|---|---|---|
| A job never runs twice at once; a job waits for a busy repository instead of failing | #16 | Yes |
| The `status` command, run history, last tick time | #17 | Yes |
| Locked config values and the `secret` commands | #18 | Yes |
| Notifications: a top-level `notify` block (`failure`, `recovery`, `success`), and `failing_since` in `status` | #20 | Yes |
| A leading `~` in a source path is expanded | #23 | Yes |
| restic's errors are reported as plain sentences, not JSON | #24 | Yes |
| Snapshot lists: recorded per job and repository, shown in `status` | #26 | No |
| `secret relock`, and the `!plain` marker for `env` values | #27 | No |
| Enrolment and the check-in: `enrol`, `unenrol`, `checkin`; the contract in `contract/checkin/v1` | #28 | No |
| `validate --json` with lines and columns, `rest-o-matic schema`, warnings for unknown keys; the contract in `contract/validate/v1` | #29 | No |

A container image for this app can now download a `rest-o-matic` binary with
everything this app needs so far: `status`, `!locked`, `validate --json`,
`schema` and the check-in. Use `v0.2.0-rc.1` or later. Its release assets
are `rest-o-matic_<version>_linux_<amd64|arm64>.tar.gz`, with a
`checksums.txt` to verify them against.

The host's documentation now lives in `../rest-o-matic/docs/`, one page per
topic; the README there is a short overview and quick start.

Still to come on the host side: config delivery and the action queue (steps
5 and 6 of the delivery sequence in the design document).

## Browsing a snapshot

Agreed in the host design session on 2026-10-02, and amended on 2026-10-05
to drop the viewer key. The full text is in
[design/central-management.md](design/central-management.md); this is what
it means for the app.

**How it works.** Listing the files in a snapshot needs the repository and
its password, so the host does it. The whole listing is loaded once:

1. The UI queues a "list files" action for a snapshot.
2. At its next check-in the host runs `restic ls` and uploads the complete
   listing, compressed.
3. This app stores it against the snapshot's ID.
4. Browsing folders, searching by name and sorting by size are then
   immediate. A snapshot never changes, so a listing never goes stale.

The first view of a snapshot waits about a minute. Restoring a chosen file
or folder to a scratch directory on the host is the follow-on, as another
queued action.

**What this app has to do.**

- **Store each listing compressed, against the snapshot's ID.** Listings
  are not encrypted, so anyone with the database or a backup of it can read
  the file names, like the rest of what hosts report.
- **Treat a listing as a cache.** It can be deleted at any time; the host
  lists the snapshot again on demand.

**Encrypting listings is deferred.** The design was a "viewer" key whose
private half the user's password unlocked at sign-in. It was dropped on
2026-10-05 because it forced password sign-in, ruling out OIDC providers
and Tailscale identity, for a small gain: paths and error messages already
arrive in plain text, and anyone controlling this app can already run
commands on every host. If it comes back, prefer one viewer key per user,
with hosts locking each listing for every user's public key (as they do
for `recovery_recipients`), so adding a user never needs another user to be
signed in. See "Deferred" in the design document.

**For the contract.** An action's result is uploaded by the host in a
request of its own, as soon as the action finishes. It is not carried in the
next check-in: a listing runs to megabytes, and waiting would double the
delay. Plan for an upload endpoint that accepts a large opaque blob.

Source paths, snapshot paths and error messages still arrive in plain text.

## Build and release workflows

Two GitHub workflows are already in `.github/workflows/`, written to mirror
the ones in `rest-o-matic`. **Neither has ever run**: the repository has no
GitHub remote yet. Treat them as a starting point and fix them against the
first real run.

**`ci.yml`** runs on every pull request into the default branch.

- `build` restores, builds and tests the solution in Release configuration.
  It looks for a `.sln` or `.slnx` file at the repository root, and skips
  with a notice while there is none.
- `container` builds the image from `Containerfile` without pushing it, so
  a pull request can't quietly break the release build. It skips while there
  is no `Containerfile`.

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

What the workflows expect from the project. The skeleton meets all of these
except the `rest-o-matic` binaries:

- **The solution file is at the repository root.**
- **A `Containerfile` is at the repository root** and accepts a `VERSION`
  build argument, which the release sets to the tag without its `v`. There is
  no `Dockerfile`; the workflows pass `file: Containerfile` because Buildx
  does not look for that name by itself. The ignore file is still
  `.dockerignore`, the one name both Podman and Buildx read.
- **The Containerfile's build stage cross-compiles.** It starts with
  `FROM --platform=$BUILDPLATFORM ...` and publishes with
  `dotnet publish -a $TARGETARCH`. Building .NET under emulation is slow and
  unreliable. The runtime stage runs no commands, so nothing is emulated.
- **The image includes released `rest-o-matic` binaries,** matching the
  image's architecture, because config is validated by running them. **Not
  done yet**, but now possible: `v0.2.0-rc.1` has `validate --json` and
  `schema`. It is left for the config editing change.
- **Commits follow conventional commits** (`feat(scope): ...`,
  `fix(scope)!: ...`), which the release notes are grouped by.
- **The .NET SDK version** comes from `global.json`, which `ci.yml` reads, so
  CI and a local build can't differ.

Not set up, and worth doing once the repository is on GitHub: a branch
protection rule that requires the CI check before merging. The local default
branch is `master`, where `rest-o-matic` uses `main`; the workflows accept
either name.

## A sensible order of work here

Nothing here is decided; it is a suggestion.

1. **Project skeleton.** Done. Blazor Server and MudBlazor on .NET 10, SQLite
   through EF Core, a container build, and CI.
2. **Password sign-in and users.** Done. Signing in through an OIDC provider
   is the natural next sign-in method, when one is wanted.
3. **Data model.** Hosts, the latest report per host, config versions,
   queued actions, an audit trail (which can now record the user).
4. **The landing page against fixture data.** The host list can be built and
   reviewed from hand-written reports shaped like `status --json`, before any
   host can check in.
5. **The check-in endpoints.** The contract exists
   (`../rest-o-matic/contract/checkin/v1`), so this can start now, tested
   against a real host running `v0.2.0-rc.1`.
6. **Config editing,** then **actions.**

The landing page's statuses (overdue, failed, needs attention) can be
derived from check-ins: overdue from this app's own record of when each host
last checked in, failed and failing-since from the *status* part.

## Open questions

- The layout of the host page and every screen after the landing page.
- Which OIDC provider, if any, to add as a second sign-in method (a
  provider on the tailnet such as Pocket ID or Authelia, or Tailscale
  identity headers). Password sign-in stays as the fallback. Roles, if ever
  needed, are also open; today every user can do everything.
- How the recovery key is set up and tested from the UI. The design document
  describes the test; nothing is designed in detail.
- How long a host may go without checking in before it counts as overdue.
  Hosts check in every minute; the threshold is this app's to choose.

## Vocabulary

| Term | Meaning |
|---|---|
| Check-in | A host's call to the central app at the start of each `tick` |
| Report | What a host sends: the state of every job, snapshot lists, action results. Together a full picture, not a stream of events |
| Heartbeat | A check-in that carries only fingerprints, because nothing changed: well under a kilobyte |
| Part | One of the three things a check-in reports: status, snapshots, config |
| Fingerprint | A hash of a part's content, so the host can tell whether this app already has it |
| Enrolment token | The one-time value in the enrol command, spent on use |
| Credential | The secret a host sends with every check-in; stored here only as a hash |
| Withheld config | A config not sent because it holds a plain-text secret |
| Action | A one-off command queued here and run by the host at its next check-in |
| Drift | A host's config file no longer matches what the central app last sent, because someone edited it on the host |
| Template | A saved backend or policy that is copied onto a host |
| Locked | Encrypted so that only chosen keys can read it |
| Host key | A host's private key, which unlocks values locked for that host |
| Recovery key | An offline key that can also unlock them, for when a host is lost |
| Snapshot list | A host's record of the snapshots a job has in a repository, taken each time the job runs |
| Overdue | A host that has stopped checking in, or a job that is past due and has not run |
