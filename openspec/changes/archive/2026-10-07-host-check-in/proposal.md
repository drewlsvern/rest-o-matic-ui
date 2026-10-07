## Why

The app can sign people in but knows nothing about backups yet. Hosts
running rest-o-matic `v0.2.0-rc.1` can already enrol with a central app and
check in on every tick, following the contract in
`../rest-o-matic/contract/checkin/v1`. This change lets them. It is the first
time real backup data reaches the app, and the landing page, config editing
and actions all build on it.

## What Changes

- A signed-in user adds a host by name and gets a one-time command to run
  on it: `rest-o-matic enrol <address> --token <token>`. The token works
  once, for 24 hours. A new command can be issued for an existing host,
  which then enrols again in place of the old enrolment.
- `POST /api/v1/enrol` accepts the token, records the host's age public key
  and description, and returns the host's ID, its name and a credential.
  The credential is stored only as a hash.
- `POST /api/v1/checkin`, authenticated with `Authorization: Bearer
  <credential>`, records when the host last checked in and the latest
  content of each part it sends (status, snapshot lists, config or the
  fields that keep it withheld). It asks for a part again when the app has
  no copy of it.
- Errors follow the contract's error body and codes (`token_rejected`,
  `credential_rejected`, `unsupported_format`, `invalid_request`).
  gzip-compressed request bodies are accepted.
- A hosts page lists every host (name, hostname, last check-in, versions,
  number of jobs) with add, new enrol command, and remove. Removing a host
  stops its credential from working.
- A copy of the contract (schemas and examples) is kept in this repository
  so tests here, and CI, check against it. The original stays in
  `rest-o-matic`.

Not in this change: the landing page's statuses (overdue, failed, needs
attention), history of reports, config sent from the app, queued actions,
and recovery keys (`recovery_recipients` is empty for now).

## Capabilities

### New Capabilities

- `host-enrolment`: adding hosts, the one-time enrol command and token,
  the enrol endpoint, re-enrolling, removing hosts, and the hosts page.
- `host-check-in`: the check-in endpoint, the host credential, what is kept
  from each check-in, asking for parts again, and the contract's errors.

### Modified Capabilities

- `sign-in`: the host endpoints are not for signed-in users. They are
  authenticated by an enrolment token or a host credential instead, and
  their errors are the contract's, not a redirect.

## Impact

- **Code:** a new `Features/Hosts/` slice with its entities, endpoints,
  handlers, pages and its own authentication scheme for host credentials.
  The sign-in slice's fallback policy stays; the host endpoints declare
  what they need.
- **Database:** a migration adding hosts, enrol tokens and the latest part
  of each kind per host.
- **Configuration:** an optional `PublicUrl` setting for the address in the
  enrol command, for when the address the browser uses is not the one hosts
  should use; and `CheckIn:MaxRequestBytes`, the largest check-in accepted
  (default 32 MiB).
- **Contract copy:** `contract/checkin/v1/` at the repository root, with a
  note on where it comes from.
- **Packages:** a JSON Schema validator for the tests only.
- **Docs:** `README.md` (adding a host) and `docs/handoff.md`.
