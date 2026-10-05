# rest-o-matic-ui

The central app for [rest-o-matic](https://github.com/drewlsvern/rest-o-matic):
one web UI to see and manage restic backups across many hosts.

It is at an early stage. The app starts, serves a placeholder page and a
health endpoint, and creates an empty database. It does not show hosts yet.
[docs/handoff.md](docs/handoff.md) describes what it will do and what is
still missing, and
[docs/design/central-management.md](docs/design/central-management.md) is the
full architecture.

## Run it locally

You need the .NET 10 SDK.

```sh
dotnet run --project src/RestOMatic.Web
```

The app is then at <http://localhost:5129>, and `/healthz` reports whether it
and its database are working. It keeps its database and keys in
`src/RestOMatic.Web/data`, which git ignores. Set `DataDirectory` to put them
somewhere else:

```sh
DataDirectory=/tmp/rest-o-matic-ui dotnet run --project src/RestOMatic.Web
```

Build and test:

```sh
dotnet build rest-o-matic-ui.slnx
dotnet test rest-o-matic-ui.slnx
```

## Run it as a container

The build file is `Containerfile`. With Podman:

```sh
podman build --build-arg VERSION=0.0.0-local -t rest-o-matic-ui .
podman run -d --name rest-o-matic-ui -p 8080:8080 -v rest-o-matic-ui-data:/data rest-o-matic-ui
```

With Docker, add `-f Containerfile` to the build command.

The container:

- listens on port 8080 over plain HTTP. It does not do TLS itself; put it
  behind something that does.
- keeps everything it must not lose in `/data`. Back that volume up.
- runs as a non-root user (UID 1654).

A named volume, as above, needs nothing more. To use a directory on the host
under rootless Podman, add `:U` so the directory is owned by the container's
user:

```sh
podman run -d -p 8080:8080 -v /srv/rest-o-matic-ui:/data:U rest-o-matic-ui
```

There is no login yet. Do not expose the app beyond a network you trust.
