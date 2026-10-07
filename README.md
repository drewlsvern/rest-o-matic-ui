# rest-o-matic-ui

> **This project is still in development and is not ready for use.** Today
> you can sign in, manage the app's users, and enrol hosts so they report to
> the app. The rest of what is described below does not exist yet.

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

Done so far:

- The project skeleton: the app builds, runs, has tests and can be built as
  a container image.
- Signing in with a user name and password, and managing who can sign in.
  Every user has the same rights. Signing in through an identity provider
  (OIDC) is planned as a second way in.

- Enrolling hosts and receiving their check-ins: each host's latest status,
  snapshot lists and config are kept, and the Hosts page lists them. See
  [Adding a host](#adding-a-host).

Not built yet: the rest of [What it will do](#what-it-will-do), starting
with a landing page that shows which hosts' backups need attention.

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

In Development the app accepts sign-in over plain HTTP. See
[First run](#first-run) to create the first user.

Build and test:

```sh
dotnet build rest-o-matic-ui.slnx
dotnet test rest-o-matic-ui.slnx
```

## Run it as a container

To try it out locally with Podman, `scripts/container-up.sh` builds the
image, starts it on <http://localhost:8180> with its data in
`~/containers/rest-o-matic-ui/data`, and prints the setup token.
`scripts/container-down.sh` removes the container and keeps the data. `PORT`
and `DATA_DIR` change the defaults.

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

Signing in sets a cookie marked `Secure`, which browsers only keep over
HTTPS, with one exception: they treat `http://localhost` as secure. So
`http://localhost:8080` works for trying the image out on your own machine,
and anywhere else the app needs HTTPS in front of it. See
[Behind Caddy](#behind-caddy).

## First run

The first time the app starts, no user exists. It writes a one-time setup
token to its log, and every page leads to `/setup`:

```sh
podman logs rest-o-matic-ui 2>&1 | grep "setup token"
```

Open `/setup`, enter the token, and create the first user. That user is
signed in straight away. Until setup is done the token is replaced each time
the app starts, and once a user exists `/setup` is closed. Anyone who can
read the log during that window could claim the app, so finish setup right
after the first start.

More users are added from **Users** in the profile menu (top right). Each
user has a user name (which cannot change), an email address, an optional
display name and a password. A password needs at least 12 characters, with
a lower-case letter, an upper-case letter, a digit and a special character.

Sessions are held in the app's memory: one ends after 12 hours without use,
after 7 days at most, or when the app restarts. Restarting signs everyone
out.

## Behind Caddy

TLS is not the app's job. Put a reverse proxy in front of it; Caddy is the
one used so far. The app trusts the `X-Forwarded-For` and
`X-Forwarded-Proto` headers that tell it the client's address and that the
request came over HTTPS only from addresses you list. Without them every
request seems to come from the proxy, and sign-in throttling would treat
all users as one.

With Caddy and the app in one pod, Caddy reaches the app on `127.0.0.1`:

```
# Caddyfile
backups.example.com {
    reverse_proxy 127.0.0.1:8080
}
```

```sh
podman pod create --name rest-o-matic -p 443:443
podman run -d --pod rest-o-matic --name rest-o-matic-ui \
    -v rest-o-matic-ui-data:/data \
    -e ReverseProxy__KnownProxies__0=127.0.0.1 \
    rest-o-matic-ui
podman run -d --pod rest-o-matic --name caddy \
    -v ./Caddyfile:/etc/caddy/Caddyfile:Z -v caddy-data:/data \
    docker.io/library/caddy:2
```

If Caddy runs in a container of its own, set `ReverseProxy__KnownProxies__0`
to its address, or `ReverseProxy__KnownNetworks__0` to the network it is on
(for example `10.89.0.0/24`). Headers from any other address are ignored.

## Forgotten password

Any signed-in user can set a new password for another user from **Users**.
If nobody can sign in, reset a password inside the container:

```sh
podman exec rest-o-matic-ui dotnet RestOMatic.Web.dll reset-password alice
```

It prints a new password once. That user's
existing sessions end. Sign in with the new password, then change it from
**Profile** in the profile menu.

## Adding a host

A host needs [rest-o-matic](https://github.com/drewlsvern/rest-o-matic)
`v0.2.0-rc.1` or later.

1. In the app, open **Hosts** and choose **Add host**. Give it a name.
2. The app shows a one-time command. It works once, for 24 hours:
   ```sh
    rest-o-matic enrol https://backups.example.com --token 7GxK2MPQ9SVW4TYB...
   ```
3. Run it on the host as the user that runs its backups, with the same
   `--config` and `--state-dir` its scheduler uses. See rest-o-matic's
   [central app guide](https://github.com/drewlsvern/rest-o-matic/blob/main/docs/central-app.md).

From then on, each `tick` on the host checks in. The Hosts page shows when it
last did, its versions and how many jobs it has. A host's config is sent
only while every secret in it is locked; until then the host reports which
fields keep it back.

A lost command, or a host to be enrolled again, gets **New enrol command**.
Once the host enrols with it, its old credential stops working. **Remove**
deletes the host and what it reported, and its credential stops working.

**The address in the command** is the one your browser used to reach the
app. If hosts reach it by a different address, set `PublicUrl`, for example
`-e PublicUrl=https://backups.example.com`. Hosts refuse plain `http://`
unless enrolled with `--allow-http`, which is meant for testing only: the
credential then travels unencrypted.

**Large hosts.** A check-in is refused with `413` when it is larger than
`CheckIn:MaxRequestBytes` (default 32 MiB, after decompression), and the
app logs a warning naming the host. That fits about 70,000 snapshots. Raise
it (`-e CheckIn__MaxRequestBytes=67108864`) if a host keeps more.

## Licence

[GPL-3.0](LICENSE).
