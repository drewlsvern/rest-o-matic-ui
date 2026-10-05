# rest-o-matic-ui

> **This project is still in development and is not ready for use.** Today
> the app only starts, serves a placeholder page and reports its own health.
> None of the features described below exist yet. There is no login, so do
> not expose it beyond a network you trust.

A web app for watching and managing the backups of many servers from one
place.

## The problem it solves

[rest-o-matic](https://github.com/drewlsvern/rest-o-matic) is a command-line
tool that backs up a server on a schedule, using
[restic](https://restic.net/). It is installed on each server, reads one
YAML config file, and has no central component. That works well for a few
servers. With ten or more it gets awkward:

- To check that backups are working, you have to SSH into each server.
- There is no single place to see every server's backups.
- Each server's config lives only on that server, unless someone remembers
  to copy it.

rest-o-matic-ui is the central piece that is missing. It is a web app you run
once, and every server running rest-o-matic reports to it.

## What it will do

- **Show every server on one page**, with the ones that need attention
  first: backups that are overdue, that failed, or that need a look.
- **Show each server's backup jobs**: when they last ran, whether they
  worked, the error if they did not, and the snapshots they have made.
- **Keep each server's config**, with every past version, who changed it and
  a diff of each change. You edit the config in the browser and the server
  picks it up.
- **Run commands on a server** from the browser, such as initialising a new
  backup repository.
- **Browse the files in a snapshot** and restore from it.
- **Store passwords without being able to read them.** A password is
  encrypted in your browser for the server that needs it, so the app only
  ever holds the encrypted form.

## How it works

Servers call the app. The app never connects to a server, so no server has
to accept incoming connections.

```
server (runs rest-o-matic from cron)              rest-o-matic-ui
┌───────────────────────────────┐                ┌──────────────────────────┐
│ 1. check in                   │ ── report ───▶ │ latest report per server │
│                               │ ◀─ config ──── │ config per server,       │
│                               │ ◀─ actions ─── │   with history           │
│ 2. apply the config if newer  │                │ queue of actions         │
│ 3. run the queued actions     │                │ web UI                   │
│ 4. run the backups that are   │                │                          │
│    due                        │                │                          │
│ 5. report the results         │ ── report ───▶ │                          │
└───────────────────────────────┘                └──────────────────────────┘
```

Each server checks in about once a minute. So the app shows what servers last
reported, and anything you ask a server to do happens at its next check-in,
not immediately.

The app is meant to run as a single container on a private network, behind a
login. It can change the commands that servers run around their backups, so
whoever can sign in to it can run commands on every server it manages. Treat
it as you would treat SSH access to all of them.

## Status

Done so far: the project skeleton. The app builds, runs, has tests, creates
an empty database and can be built as a container image.

Not built yet: everything under [What it will do](#what-it-will-do),
including the login.

[docs/handoff.md](docs/handoff.md) lists what is planned and what is still
missing. [docs/design/central-management.md](docs/design/central-management.md)
is the full architecture.

It is built with Blazor Server and MudBlazor on .NET 10, and stores its data
in SQLite.

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

## Licence

[GPL-3.0](LICENSE).
