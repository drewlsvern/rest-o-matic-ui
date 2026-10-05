## MODIFIED Requirements

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

## ADDED Requirements

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
