# app-hosting Specification

## Purpose
Defines how the central app runs as a process and as a container: the port it
serves on, its health endpoint, where it keeps its data, how it reports its
version, and what the container image guarantees to whoever deploys it.
## Requirements
### Requirement: One process serves the web UI
The app SHALL run as a single process that serves the web UI over HTTP on one
port. It SHALL NOT redirect to HTTPS or require TLS itself; TLS is provided by
whatever sits in front of it.

#### Scenario: Home page loads
- **WHEN** a signed-in browser requests `/`
- **THEN** the app responds with status 200 and a page that shows the app's name

#### Scenario: Plain HTTP is not redirected
- **WHEN** a client requests `/healthz` over plain HTTP
- **THEN** the app answers the request directly and does not respond with a redirect to HTTPS

### Requirement: UI assets are served by the app
Every script, stylesheet and font the UI needs SHALL be served by the app
itself. A page SHALL NOT cause the browser to request anything from another
origin.

#### Scenario: No third-party requests
- **WHEN** the home page is loaded
- **THEN** the returned HTML references no script, stylesheet or font on another origin

### Requirement: Health endpoint
The app SHALL expose `GET /healthz`, which reports whether the app is running
and can reach its database.

#### Scenario: Healthy
- **WHEN** the app is running and its database can be opened
- **THEN** `GET /healthz` responds with status 200

#### Scenario: Database unavailable
- **WHEN** the app is running and its database cannot be opened
- **THEN** `GET /healthz` responds with status 503

### Requirement: Data lives in one configurable directory
The app SHALL keep everything it must not lose (its database and its
data-protection keys) under a single data directory. The location of that
directory SHALL be set by configuration, and the app SHALL write persistent
data nowhere else.

#### Scenario: First start
- **WHEN** the app starts with a data directory that is empty or does not exist
- **THEN** the app creates the directory if needed, creates its database in it, and starts

#### Scenario: Restart keeps data
- **WHEN** the app is stopped and started again with the same data directory
- **THEN** it uses the existing database and keys and does not recreate them

#### Scenario: Data directory cannot be written
- **WHEN** the app starts and cannot create or write to the data directory
- **THEN** the app fails to start and logs an error that names the directory

### Requirement: The app reports its version
The app SHALL show the version it was built as in the web UI. A build that was
not given a version SHALL show a version that is recognisably a development
build.

#### Scenario: Released build
- **WHEN** the app is built with version `1.2.3`
- **THEN** the web UI shows `1.2.3`

#### Scenario: Development build
- **WHEN** the app is built without a version
- **THEN** the web UI shows a development version, not a release number

### Requirement: Container image
The app SHALL be available as a container image for `linux/amd64` and
`linux/arm64`. The image SHALL run the app as a non-root user, listen on port
8080, and keep its data under `/data`. The image SHALL take its version from a
`VERSION` build argument.

#### Scenario: Runs with only a data volume
- **WHEN** the image is run with a writable volume mounted at `/data` and port 8080 published
- **THEN** the app starts, `GET /healthz` responds with status 200, and the database is created under `/data`

#### Scenario: Not root
- **WHEN** the container is running
- **THEN** the app's process does not run as root

#### Scenario: Version from the build argument
- **WHEN** the image is built with `VERSION=1.2.3`
- **THEN** the web UI of a container started from it shows `1.2.3`

### Requirement: Behind a reverse proxy
The app SHALL accept `X-Forwarded-For` and `X-Forwarded-Proto` only from
proxy addresses set in configuration, and SHALL then use them as the
client's address and the request's scheme. From any other address it SHALL
ignore them. With no proxy addresses configured, it SHALL ignore them from
everyone.

#### Scenario: Configured proxy
- **WHEN** a request arrives from a configured proxy address with `X-Forwarded-Proto: https` and `X-Forwarded-For: 100.64.0.7`
- **THEN** the app treats the request as HTTPS from `100.64.0.7`

#### Scenario: Unknown sender
- **WHEN** a request arrives from an address that is not a configured proxy, carrying `X-Forwarded-For: 100.64.0.7`
- **THEN** the app uses the address the request actually came from
