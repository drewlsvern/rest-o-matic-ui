## MODIFIED Requirements

### Requirement: Pages require a signed-in user
Every page and endpoint of the app SHALL require a signed-in user, except the
sign-in page and its completion step, the first-run setup page, static
assets, `GET /healthz`, and the host endpoints under `/api/v1/`, which are
authenticated by an enrolment token or a host credential instead. A request
without a signed-in user for a page SHALL be redirected to the sign-in page,
which SHALL return the user to the page they asked for after they sign in. A
request without a signed-in user for anything other than a page SHALL be
answered with status 401 and not redirected.

#### Scenario: Page without signing in
- **WHEN** a browser that is not signed in requests `/`
- **THEN** it is redirected to the sign-in page

#### Scenario: Return to the requested page
- **WHEN** a browser that is not signed in requests a page, is redirected, and then signs in
- **THEN** it is taken to the page it originally requested

#### Scenario: Return address on another site
- **WHEN** the sign-in page is given a return address on another origin
- **THEN** after signing in the browser is taken to `/` and not to that address

#### Scenario: Health stays open
- **WHEN** a client that is not signed in requests `GET /healthz`
- **THEN** the app responds as it would to a signed-in client

#### Scenario: Host endpoints do not use the sign-in session
- **WHEN** a host calls `/api/v1/checkin` with no session cookie
- **THEN** it is not redirected to the sign-in page, and is answered according to its credential
