## Purpose

What the central app does with an enrolled host's check-in: who may send
one, what it keeps from it, and what it replies. Follows
`contract/checkin/v1`.

## ADDED Requirements

### Requirement: Checking in
`POST /api/v1/checkin` SHALL accept a request as defined by
`checkin-request.schema.json` from a host that sends its credential as
`Authorization: Bearer <credential>`. It SHALL reply 200 as defined by
`checkin-response.schema.json`, with the app's current time, the list of
parts to send again, `config` null and `actions` empty. It SHALL record the
time it received the check-in as the host's last check-in, and the host
description the request carries.

#### Scenario: Quiet check-in
- **WHEN** an enrolled host posts the contract's quiet example and the app already holds every part with the same fingerprints
- **THEN** the reply is 200, matches `checkin-response.schema.json`, has an empty `resend`, and the host's last check-in time is updated

#### Scenario: Versions change
- **WHEN** a host checks in reporting rest-o-matic `v0.2.1` after enrolling with `v0.2.0`
- **THEN** the hosts page shows `v0.2.1`

### Requirement: The host credential
A check-in SHALL be accepted only with the credential of a host that is
currently enrolled. When the request's `host_id` is not null, it SHALL be the
ID of the host the credential belongs to. A host credential SHALL NOT give
access to anything other than the host endpoints, and a signed-in user's
session SHALL NOT be accepted in place of a credential.

#### Scenario: No or unknown credential
- **WHEN** a check-in has no `Authorization` header, or a credential that matches no host
- **THEN** the reply is 401 with error code `credential_rejected`

#### Scenario: Another host's ID
- **WHEN** a check-in carries a valid credential and the ID of a different host
- **THEN** the reply is 401 with error code `credential_rejected` and nothing is stored

#### Scenario: Credential elsewhere
- **WHEN** a request for a page or another endpoint carries a host credential and no session cookie
- **THEN** it is treated as not signed in

### Requirement: Keeping the latest of each part
For each part (`status`, `snapshots`, `config`) whose `content` is not null,
the app SHALL keep that content and its fingerprint as the host's latest
copy, unless it already holds a copy from a check-in with a later `sent_at`.
It SHALL treat fingerprints as opaque and SHALL NOT compute them.

#### Scenario: First full check-in
- **WHEN** an enrolled host posts the contract's full example
- **THEN** the app holds its status, snapshot lists and config text with their fingerprints

#### Scenario: Overlapping check-ins
- **WHEN** a check-in with an earlier `sent_at` arrives after one with a later `sent_at`, both carrying status content
- **THEN** the app keeps the status from the later `sent_at`

### Requirement: Withheld config
When the config part has `withheld` set, the app SHALL record that the
host's current config is withheld, with the reason and the fields named, and
SHALL keep the last config text it received, if any, marked as no longer
current. When a later check-in carries config content, it SHALL clear the
withheld record.

#### Scenario: Withheld
- **WHEN** a host posts the contract's withheld-config example
- **THEN** the app records the config as withheld because of `repositories.offsite.password`, and does not ask for the config again

#### Scenario: No longer withheld
- **WHEN** a host whose config was withheld later sends config content
- **THEN** the app holds that config as current and no longer records it as withheld

### Requirement: Asking for parts again
The reply's `resend` SHALL list every part whose fingerprint in the request
differs from the fingerprint of the copy the app holds, including parts the
app holds no copy of, except a config part that is withheld.

#### Scenario: App lost its data
- **WHEN** an enrolled host sends only fingerprints and the app holds no snapshot lists for it
- **THEN** `resend` contains `snapshots`

#### Scenario: Copy is current
- **WHEN** the fingerprints in a check-in match the copies the app holds
- **THEN** `resend` is empty

### Requirement: Errors and limits
Every error reply from the host endpoints SHALL have the body defined by
`error.schema.json`. A request whose `format_version` is not 1 SHALL be
answered with 400 `unsupported_format`. A request that is not valid JSON or
lacks a field the schema requires SHALL be answered with 400
`invalid_request`, naming the problem. A request body sent with
`Content-Encoding: gzip` SHALL be accepted. A check-in body larger than the
`CheckIn:MaxRequestBytes` setting, measured after decompression, SHALL be
refused with 413 and error code `request_too_large`, and the app SHALL log a
warning naming the host and the limit. The setting SHALL default to
33554432 bytes (32 MiB), and the app SHALL refuse to start when it is not a
positive whole number.

#### Scenario: Wrong format version
- **WHEN** a check-in has `"format_version": 2`
- **THEN** the reply is 400 with error code `unsupported_format`

#### Scenario: Missing field
- **WHEN** a check-in has no `parts`
- **THEN** the reply is 400 with error code `invalid_request` and a message that names `parts`

#### Scenario: Compressed body
- **WHEN** a host posts the full example gzip-compressed with `Content-Encoding: gzip`
- **THEN** it is accepted as if it had been sent uncompressed

#### Scenario: Error body
- **WHEN** any host endpoint replies with a 4xx status it produced itself
- **THEN** the body matches `error.schema.json`

#### Scenario: Over the limit
- **WHEN** an enrolled host posts a check-in whose decompressed body is larger than the limit
- **THEN** the reply is 413 with error code `request_too_large`, nothing is stored, and the log has a warning naming the host and the limit

#### Scenario: Raised limit
- **WHEN** `CheckIn:MaxRequestBytes` is set to 67108864 and a host posts a 40 MiB check-in
- **THEN** it is accepted

#### Scenario: Invalid setting
- **WHEN** `CheckIn:MaxRequestBytes` is `0` or not a number
- **THEN** the app does not start, and its log names the setting
