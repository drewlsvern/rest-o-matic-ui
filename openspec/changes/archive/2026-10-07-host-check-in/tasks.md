## 1. Contract copy

- [x] 1.1 Copy `../rest-o-matic/contract/checkin/v1` to `contract/checkin/v1/` unchanged, with a `PROVENANCE.md` naming the source commit
- [x] 1.2 Add `scripts/sync-contract.sh` to copy it again from `../rest-o-matic` and update the provenance note
- [x] 1.3 Link the contract folder into the test project's output, and add `JsonSchema.Net` to the tests (version in `Directory.Packages.props`)
- [x] 1.4 Test support: load a schema or example by name, and validate a JSON reply against a schema

## 2. Hosts and their parts

- [x] 2.1 Add `Host` and `HostPart` with their `IEntityTypeConfiguration` classes in `Features/Hosts/`: unique normalised name, unique token and credential hashes, composite key and cascade delete for parts
- [x] 2.2 Add `Secrets`: enrol token (26 base32 characters), credential (`rom1_` + 32 bytes base64url), SHA-256 hashing
- [x] 2.3 Generate the migration and confirm a database from the previous change upgrades
- [x] 2.4 Add the host-name rules (1–64 of letters, digits, `.`, `_`, `-`; unique regardless of case)
- [x] 2.5 Tests: name rules, token and credential shape, hashes stored and secrets not

## 3. Contract messages

- [x] 3.1 Add records for the enrol request and reply, the check-in request (envelope, host, parts) and reply, and the error body, with `required` members and the contract's JSON names
- [x] 3.2 Add the request reader: body up to the limit, JSON parse, `format_version` check first, deserialization errors turned into `invalid_request` naming the field, and 413 turned into `request_too_large`
- [x] 3.3 Tests: every request example parses; `format_version` 2 gives `unsupported_format`; missing `parts`, `host` or `token` gives `invalid_request` naming it; non-JSON gives `invalid_request`; every error body matches `error.schema.json`

## 4. Enrolment

- [x] 4.1 Add `HostRegistry` (or handler) operations for the UI: add a host and issue a token, issue a new token, remove a host, list hosts
- [x] 4.2 Add `EnrolHandler` and `POST /api/v1/enrol` (anonymous): token lookup by hash, expiry, spend, new credential, public key and description, reply with an empty `recovery_recipients`
- [x] 4.3 Add the enrol rate limit (10 per minute per client address, 429 with the contract's error body)
- [x] 4.4 Tests: enrol with the example request; reply matches the schema and carries the given name; used, expired, unknown and replaced tokens give `token_rejected` and store nothing; re-enrol replaces the credential and keeps the parts; the 11th request in a minute gets 429

## 5. Check-in

- [x] 5.1 Add `HostCredentialAuthenticationHandler` (Bearer, hash lookup, enrolled hosts only) with a challenge that writes `credential_rejected`, and the `Host` policy naming only this scheme
- [x] 5.2 Add `CheckInHandler` and `POST /api/v1/checkin`: `host_id` match, per-host lock, host description and last check-in time, keep each part unless a later `sent_at` is held, withheld config, job count, `resend` by fingerprint, reply
- [x] 5.3 Add request decompression (gzip), the `CheckIn:MaxRequestBytes` setting (default 33554432, validated on start) as the check-in endpoint's size limit, and a warning naming the host when it is exceeded
- [x] 5.4 Wire `AddHosts`, `UseHosts` and `MapHosts` into `Program.cs` in the order in `design.md`
- [x] 5.5 Tests: quiet, full and withheld examples (stored content and fingerprints, replies match the schema); overlapping `sent_at`; withheld then not withheld; `resend` for a missing and for an outdated copy, not for a withheld config; no, unknown, removed and old (after re-enrol) credentials, and another host's ID, give `credential_rejected`; a session cookie does not authenticate a check-in and a credential does not reach a page; gzip body accepted; a body over the limit gives 413 `request_too_large` and logs a warning naming the host (with a small configured limit); a raised limit accepts a larger body; `0` or a non-number stops the app from starting

## 6. Hosts page

- [x] 6.1 Add `HostsPage` at `/hosts` with a `MudTable`: name, hostname, last check-in, versions, jobs, waiting-to-enrol state with token expiry
- [x] 6.2 Add the add-host dialog: name, then the enrol command with a copy button; a note when the address is plain HTTP
- [x] 6.3 Add "New enrol command" and "Remove" (with confirmation) per host
- [x] 6.4 Address in the command: `PublicUrl` when set, otherwise the browser's address as the circuit saw it
- [x] 6.5 Add a "Hosts" link to the app bar
- [x] 6.6 Tests: the page lists an enrolled host's details and a waiting host's expiry; the command uses `PublicUrl` when set; the token is not in the page after it was shown

## 7. Finishing

- [x] 7.1 README: adding a host and enrolling it, `PublicUrl`, `CheckIn:MaxRequestBytes` (and when to raise it), and that hosts need HTTPS (or `--allow-http` for testing)
- [x] 7.2 `docs/handoff.md`: what now exists and what the landing page change can use; the sign-in note about a separate bearer scheme is now done; a TODO to pin the contract copy to a host release with a weekly drift check in CI (see design), and a note to run `scripts/sync-contract.sh` whenever the host changes the contract
- [x] 7.3 `dotnet build` (Release) and `dotnet test` pass with no warnings
- [x] 7.4 In a container, enrol a real host built from `../rest-o-matic` (`v0.2.0-rc.1`) with `--allow-http`, run `rest-o-matic checkin`, and see it on the hosts page
