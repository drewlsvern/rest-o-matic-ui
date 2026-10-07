## Context

See `proposal.md` for why and the `host-enrolment`, `host-check-in` and
`sign-in` specs for the behaviour. The contract is
`../rest-o-matic/contract/checkin/v1`: its README, five JSON Schemas (draft
2020-12) and nine examples. `v0.2.0-rc.1` of the host implements it.

What this change builds on:

- The `Accounts` slice sets a fallback authorization policy: everything
  needs a signed-in user unless it opts out. The cookie scheme is the
  default authentication scheme.
- Forwarded headers are trusted from configured proxies, so a request's
  scheme, host and client address are the real ones behind Caddy.
- `AppDbContext` finds each slice's entity mappings by scanning, and
  migrations live in `Infrastructure/Persistence/Migrations/`.

## Goals / Non-Goals

**Goals:**

- A real host on `v0.2.0-rc.1` can enrol and check in against this app with
  no change on its side.
- Every message this app sends, and its parsing of every message it
  receives, is tested against the contract's schemas and examples, here and
  in CI.
- What a check-in carries is kept in a form the landing page can read next,
  without deciding the landing page's model now.

**Non-Goals:**

- Interpreting the status: overdue, failed, needs attention. That is the
  landing page's job.
- History. Only the latest copy of each part is kept.
- Anything the reply reserves for later (`config`, `actions`), and recovery
  recipients.

## Decisions

### Slice layout

```
Features/Hosts/
  HostsRegistration.cs         AddHosts, UseHosts, MapHosts
  Host.cs, HostPart.cs, *Configuration.cs
  Secrets.cs                   tokens, credentials, hashing
  Contract/                    request and reply records, error body
  Enrolment/                   EnrolHandler, EnrolEndpoint
  CheckIn/                     CheckInHandler, CheckInEndpoint,
                               HostCredentialAuthenticationHandler
  Pages/                       HostsPage.razor, AddHostDialog.razor,
                               EnrolCommand.razor
contract/checkin/v1/           the contract copy, at the repository root
```

The slice does not reference `Accounts`. The host endpoints declare their own
authentication. The hosts page is an ordinary page under the fallback
policy.

### Data model

```
Hosts
  Id                 GUID PK (the contract's host_id)
  Name               text,  NormalizedName unique
  CreatedAt          timestamp
  EnrolTokenHash     blob, null, unique     pending token, one per host
  EnrolTokenExpires  timestamp, null
  CredentialHash     blob, null, unique     null until enrolled
  PublicKey          text, null             age1...
  EnrolledAt         timestamp, null
  Hostname, Os, Arch, RestOMaticVersion, ResticVersion   from the last message
  LastCheckInAt      timestamp, null
  JobCount           int, null              from the latest status

HostParts                                   latest copy of each part
  HostId             GUID, FK → Hosts (cascade)  ┐ PK
  Kind               text: status | snapshots | config ┘
  Fingerprint        text
  Content            text                   raw JSON, or the config's text
  SentAt             timestamp              the check-in it came from
  ReceivedAt         timestamp
  IsCurrent          bool                   config only: false while withheld
  WithheldReason     text, null             config only
  WithheldFields     text, null             config only, JSON array
```

`status` and `snapshots` are kept as the raw JSON the host sent, not mapped
to tables. The landing page change will decide what it needs to query and
can parse the JSON or add columns then. The only value pulled out now is the
number of jobs, which the hosts page shows.

The pending token lives on the host row, so "a new command replaces the old
one" is a single overwrite, and removing the host removes its token.

*Alternative: a table of reports kept over time.* It is out of scope, and a
later change can add one beside `HostParts` without changing it.

### Secrets: tokens and credentials

- **Enrol token:** 26 characters of base32 (130 bits). That is short
  enough to paste and print, and it is case-insensitive when typed.
- **Credential:** `rom1_` followed by 32 random bytes, base64url-encoded.
  The prefix makes a leaked credential recognisable.
- **Both are stored as SHA-256 hashes** in unique indexed columns and found
  by hash. A slow password hash is for secrets people choose; these are
  random and long, so a fast hash is enough, and a lookup by hash needs no
  comparison of secret values.

### Two kinds of caller, two schemes

```
browser ── cookie ──▶ pages, /account/*        default scheme, fallback policy
host ── Bearer ────▶ /api/v1/checkin           "HostCredential" scheme, "Host" policy
host ── token ─────▶ /api/v1/enrol             AllowAnonymous; token checked in the handler
```

`HostCredentialAuthenticationHandler` reads `Authorization: Bearer`, hashes
the value and finds the enrolled host. On success the principal carries the
host's ID. Its challenge writes the contract's 401 body with
`credential_rejected`, so a missing or wrong credential never becomes a
redirect. The `Host` policy names only this scheme, so a session cookie does
not satisfy it. The fallback policy uses the default (cookie) scheme, so a
bearer credential does not satisfy anything else either.

### Reading requests

Each endpoint reads the body itself rather than relying on minimal-API
binding, so that every failure gets the contract's error body:

1. Read the body (decompressed already, see below), up to the size limit.
2. Parse it as a `JsonDocument`. If it is not JSON → `invalid_request`.
3. Check `format_version` before anything else. If it is not 1 →
   `unsupported_format`, even if the rest no longer matches this version's
   schema.
4. Deserialize into records with `required` members and
   `RespectNullableAnnotations`. A missing required field → `invalid_request`,
   with the JSON path from the exception in the message.

Part content is kept as `JsonElement` and stored with `GetRawText()`, so
fields this app does not know about are preserved, as the contract says
each side must ignore and keep them. The server does not validate the job
schema in depth. It needs the envelope; the content is the host's report.

*Alternative: validate every request against the JSON Schema at runtime.*
It is stricter than the contract asks ("each side ignores fields it doesn't
know") and would add a runtime dependency. Schema validation is used in the
tests instead.

### Compression and size

`AddRequestDecompression` with its gzip provider, and `UseRequestDecompression`
early in the pipeline. The check-in endpoint carries a request size limit
(`RequestSizeLimitAttribute` metadata, set when the endpoint is mapped). The
decompression middleware applies it to the decompressed body, so a small
gzip bomb cannot exhaust memory. Reading past the limit throws a
`BadHttpRequestException` (413). The endpoint turns that into the contract's
error body with code `request_too_large`, and logs a warning naming the host.
Authentication has already run, so the host is known.

**The reader also counts the bytes itself** and stops as soon as the limit is
passed. Not every server applies the endpoint's limit: the in-memory test
server does not. Counting in the reader makes the limit hold everywhere and
lets it be tested. The enrol endpoint reads at most 64 KiB, since an enrol
request is a few hundred bytes.

**The limit is a setting, `CheckIn:MaxRequestBytes`, defaulting to 32 MiB.**
The value is not derived from the contract, which sets no limit. The host's
design document asks for "a high default limit [that] protects the central
app, and can be raised". 32 MiB is just above Kestrel's default (30,000,000
bytes), which the endpoint has to override anyway. Estimated sizes:

| Part | Bounded by | Size |
|---|---|---|
| `status` | 20 runs kept per job (`MaxRuns` on the host) | ~10 KB per job |
| `config` | the YAML file | a few KB |
| `snapshots` | nothing: every snapshot of every repository | ~450 bytes per snapshot |

A typical host (10 jobs, 2 repositories, 50 snapshots each) sends about
450 KB. The default fits about 70,000 snapshots, which only a schedule with
no retention reaches. For such a host the warning names it, and the setting
lets the limit be raised without a new release. It is read and validated at
startup through `IOptions` with `ValidateOnStart`, so a bad value fails
straight away rather than on the first check-in.

### Check-in

```
authenticate (HostCredential) ─▶ parse ─▶ host_id matches? ─▶ per-host lock
  ─▶ update host description, LastCheckInAt = now
  ─▶ for each part with content: keep it unless a later sent_at is held
  ─▶ config withheld: record reason/fields, IsCurrent = false, keep content
  ─▶ JobCount from status.jobs when status content arrived
  ─▶ save ─▶ resend = parts whose reported fingerprint ≠ held fingerprint
                     (except a withheld config)
  ─▶ 200 { format_version, server_time, resend, config: null, actions: [] }
```

- **Resend by fingerprint, not by "has content".** That covers both a lost
  database (nothing held) and an older backup restored (an older copy held).
- **The per-host lock** (an in-process `SemaphoreSlim` per host ID) keeps
  two overlapping check-ins from one host from interleaving their
  read-compare-write of `sent_at`. There is one process, so an in-process
  lock is enough.
- **`host_id` mismatch → `credential_rejected`**, as the contract tells the
  host to enrol again, which is the right advice when a credential and an ID
  disagree.

### Enrolment

The handler finds the host by token hash. It refuses with `token_rejected`
when there is none or the token has expired, without saying which. Then it
clears the token, sets a new credential hash, the public key and the host
description, and replies. Re-enrolling replaces the credential and key and
keeps the parts. The host's old credential stops working at that moment.

**Rate limit:** an ASP.NET rate-limiter policy, a fixed window of 10 per
minute per client address, on the enrol endpoint only. Its rejection writes
the contract's error body with code `rate_limited` and status 429, which the
host treats as any other error.

### The enrol command's address

`PublicUrl`, when configured. Otherwise `NavigationManager.BaseUri` in the
hosts page. It comes from the request that started the circuit, after
forwarded-header processing, so it is the address the user's browser used.
The command is
`rest-o-matic enrol <address> --token <token>`. The page shows a note when
the address is `http://`: the host refuses plain HTTP unless enrolled with
`--allow-http`. The copy button copies the command with a leading space, as
rest-o-matic's docs suggest, so bash keeps the token out of its history where
`HISTCONTROL` ignores such commands. Times on the page are shown in UTC and
labelled so, because a server-rendered page knows only the server's clock.

### The hosts page

`/hosts`, a `MudDataGrid` with name, hostname, last check-in (relative, with
the time in a tooltip), versions, jobs, and actions: "New enrol command" and
"Remove". Every column but the actions sorts. A `PropertyColumn` sorts by
default, but a `TemplateColumn` defaults to `Sortable="false"`, so each one
opts in. The grid starts sorted by hostname. A host that has not enrolled has
no hostname, versions or jobs, so it shows "—" in those columns and sorts
after the others. Its "Waiting to enrol" state sits in the last check-in
column, so every row keeps the same columns. The grid applies its initial
sort only once the page is interactive, so the page also hands it the hosts
already in that order, and the first paint does not jump. "Add host" opens a dialog that takes the name, then shows the
command with a copy button. The copy button uses the browser's clipboard
through `IJSRuntime`, so no script file is added. A waiting host shows its
token's expiry, or "expired". The app bar gets a "Hosts" link. The landing
page change will replace `/` with the real host list.

### The contract copy and its tests

`contract/checkin/v1/` holds the README, schemas and examples, copied
byte for byte. `contract/PROVENANCE.md`, beside rather than inside the copy so
the folder stays identical to the original, names the commit that last
changed the contract (`82c75ef`) and the first release with it
(`v0.2.0-rc.1`). The
original stays in `rest-o-matic`. `scripts/sync-contract.sh` copies it again
from `../rest-o-matic`. Keeping the copy current is manual for now.

**Later (decided 2026-10-05 to leave for now):** pin the copy to a host
release instead of the sibling checkout (`sync-contract.sh <tag>` fetching
from GitHub, with the tag and commit in `PROVENANCE.md`), and add a weekly
CI job that fails when the latest host release's contract differs from the
copy. Considered and set aside: a git submodule (pulls in the whole host
repository) and fetching at test time (tests depend on the network and
change without a commit here).

The test project links the folder in as content. Tests:

- parse every request example and check what is stored;
- validate every reply this app produces (enrol reply, check-in replies,
  each error) against its schema with JsonSchema.Net, a test-only
  dependency;
- post each example through the real endpoints, including gzip-compressed.

### Pipeline

```
UseReverseProxy → UseShell → UseHosts (decompression, rate limiter)
  → UseAccounts → MapShell → MapHealth → MapAccounts → MapHosts
```

`UseHosts` adds request decompression and the rate limiter, and Program.cs
calls it before `UseAccounts`. Decompression has to come before anything
reads the body. Both need the endpoint, which they have: `WebApplication`
adds routing at the start of the pipeline.

## Risks / Trade-offs

- [Status kept as raw JSON] → The landing page must parse it. It is small
  per host, and the contract's schema describes it. Columns can be added
  when queries need them.
- [A fast hash for credentials] → Fine for 256-bit random values. A stolen
  database still cannot be used to check in, because the hashes cannot be
  reversed.
- [Plain-HTTP enrol commands] → Shown with a warning. The host requires
  `--allow-http` for them, which is meant for testing.
- [Token in the page] → It is shown once, over the same TLS as everything
  else, and expires in 24 hours. Losing it means issuing a new one.
- [Per-host lock is in-process] → Correct for one process, which is how the
  app runs.
- [The contract copy drifts from the original] → Within v1 the contract
  only adds fields, which both sides ignore, so a stale copy breaks nothing.
  It only means new fields go untested here. An incompatible change is a new
  `v2` folder, which needs deliberate work regardless. Re-run
  `sync-contract.sh` when the host changes the contract. Automatic detection
  is listed above as later work.

## Migration Plan

A new migration adds `Hosts` and `HostParts`. Nothing existing changes.
Rolling back to the previous image leaves the tables unused.

## Open Questions

- Whether removing a host should keep its last reports for a while. For
  now removal deletes them, and the landing page change may revisit it.
