# rest-o-matic-ui

The central app for [rest-o-matic](https://github.com/drewlsvern/rest-o-matic):
one web UI to see and manage restic backups across many hosts. The host CLI
is a separate repository, checked out at `../rest-o-matic`.

Before proposing or building anything, read:

1. [docs/handoff.md](docs/handoff.md): what this app is, the decisions
   already made, what the host side provides today, and what is still
   missing.
2. [docs/design/central-management.md](docs/design/central-management.md):
   the full architecture. It is a copy; the original is in `../rest-o-matic`.

## Decisions already made

- Blazor Server with MudBlazor on .NET, SQLite, Monaco for the config
  editor. Runs as a container on the tailnet, behind a login.
- Hosts call this app. Nothing here ever connects to a host.
- Config is edited as YAML, not through forms.
- Config is validated by running the `rest-o-matic` binary, not by
  reimplementing its rules in C#.
- Secrets are write-only. A secret is locked in the browser by JavaScript
  and must never be bound to a Blazor input, which would send it to the
  server in plain text.

## Working here

- Changes go through OpenSpec (`/opsx:explore`, `/opsx:propose`,
  `/opsx:apply`, `/opsx:archive`).
- The format hosts and this app exchange is owned by `../rest-o-matic`. Do
  not invent or change it here; propose the change there.
