## Purpose

How a host becomes known to the central app: a signed-in user adds it and
gets a one-time command, the host runs it and enrols, and the user can later
enrol it again or remove it. Follows `contract/checkin/v1`.

## ADDED Requirements

### Requirement: Adding a host
A signed-in user SHALL be able to add a host by giving it a name. A host name
SHALL be 1 to 64 characters of letters, digits, `.`, `_` and `-`, and SHALL
be unique without regard to letter case. Adding a host SHALL show a command
to run on it, `rest-o-matic enrol <address> --token <token>`, that the user
can copy.

#### Scenario: Add a host
- **WHEN** a signed-in user adds a host named `prd-podman-01`
- **THEN** the host is listed as waiting to enrol, and the page shows a command that contains the app's address and a token

#### Scenario: Name taken
- **WHEN** a host named `PRD-podman-01` is added while `prd-podman-01` exists
- **THEN** it is refused because the name is taken

### Requirement: The enrol token
An enrol token SHALL be random, usable once, and valid for 24 hours. The app
SHALL store only a hash of it and SHALL show it only when it is issued.
Issuing a new token for a host SHALL make any earlier unused token for that
host stop working.

#### Scenario: Shown once
- **WHEN** a user leaves the page that showed a host's enrol command and comes back
- **THEN** the token is not shown again, and a new command can be issued instead

#### Scenario: New command replaces the old one
- **WHEN** a new enrol command is issued for a host whose earlier token was not used
- **THEN** the earlier token is refused

### Requirement: The address in the enrol command
The address in the enrol command SHALL be the `PublicUrl` setting when it is
set, and otherwise the scheme and host the user's browser used to reach the
app, as seen through a configured reverse proxy.

#### Scenario: Behind a proxy
- **WHEN** a user reaches the app as `https://backups.example.com` through a configured proxy and no `PublicUrl` is set
- **THEN** the command contains `https://backups.example.com`

#### Scenario: Setting overrides
- **WHEN** `PublicUrl` is `https://backups.internal` and a user reaches the app as `http://localhost:8180`
- **THEN** the command contains `https://backups.internal`

### Requirement: Enrolling
`POST /api/v1/enrol` SHALL accept a request as defined by
`enrol-request.schema.json`, without authentication. For a valid, unused,
unexpired token it SHALL spend the token, record the host's public key and
description, issue a new random credential, and reply as defined by
`enrol-response.schema.json` with the host's ID, the name the user gave it,
the credential and an empty list of recovery recipients. The app SHALL store
only a hash of the credential.

#### Scenario: Enrol
- **WHEN** a host posts the contract's example enrol request with a valid token
- **THEN** the reply is 200, matches `enrol-response.schema.json`, and its `host_name` is the name the user gave

#### Scenario: Token used, expired or unknown
- **WHEN** a host posts a token that was already used, is more than 24 hours old, or was never issued
- **THEN** the reply is 401 with error code `token_rejected` and nothing is stored

#### Scenario: Enrolling again
- **WHEN** an enrolled host enrols again with a new token issued for it
- **THEN** it gets a new credential, its old credential stops working, and what it reported before is kept

#### Scenario: Too many attempts
- **WHEN** one client address makes more than 10 enrol requests in a minute
- **THEN** further requests from it are answered with 429 until the minute has passed

### Requirement: The hosts page
A signed-in user SHALL see every host with its name, its hostname, when it
last checked in, its rest-o-matic and restic versions, and the number of
jobs it reported. A host that has not enrolled yet SHALL be shown as waiting
to enrol, with when its token expires.

#### Scenario: Enrolled host
- **WHEN** a host has checked in reporting two jobs
- **THEN** the hosts page shows its hostname, versions, last check-in time and two jobs

#### Scenario: Waiting host
- **WHEN** a host was added and has not enrolled
- **THEN** it is shown as waiting to enrol, with its token's expiry

### Requirement: Removing a host
A signed-in user SHALL be able to remove a host after confirming. Removing a
host SHALL delete what it reported and SHALL make its credential and any
unused token stop working.

#### Scenario: Remove
- **WHEN** a user removes an enrolled host
- **THEN** it is no longer listed, and its next check-in is answered with 401 `credential_rejected`
