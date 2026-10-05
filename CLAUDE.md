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
- The container build file is `Containerfile`. There is no `Dockerfile`.
- Config is edited as YAML, not through forms.
- Config is validated by running the `rest-o-matic` binary, not by
  reimplementing its rules in C#.
- Secrets are write-only. A secret is locked in the browser by JavaScript
  and must never be bound to a Blazor input, which would send it to the
  server in plain text.
- File listings are stored unencrypted. Encrypting them for a "viewer"
  key was dropped on 2026-10-05 and is deferred, so nothing constrains how
  users sign in. See "Browsing a snapshot" in `docs/handoff.md`.
- A snapshot's contents are browsed from one full listing, uploaded by the
  host on request and cached here, not fetched a folder at a time.
- Users sign in with a password; OIDC may be added later as a second method.
  Sessions live on the server. Every page and endpoint needs a signed-in
  user unless it opts out with `AllowAnonymous`, so a new endpoint is closed
  by default. Host endpoints will need their own bearer scheme, not the
  cookie.

## Code layout: vertical slices

Code is grouped by feature, not by technical layer. There are no top-level
`Pages/`, `Services/` or `Models/` folders. The reasoning is in the
`project-skeleton` change's design.

```
src/RestOMatic.Web/
├── Program.cs         composition only
├── Features/<Name>/   one folder per feature
├── Shell/             the app frame: App.razor, layout, theme, version
└── Infrastructure/    storage and the database context
```

- A slice owns everything for one feature: its pages and components, the
  code that handles its requests, its endpoints, its entities and their
  database mapping, and any types only it uses.
- A slice registers itself through `Add<Feature>` and, if it has endpoints,
  `Map<Feature>` extension methods. `Program.cs` calls them and holds no
  feature logic.
- Slices do not reference each other. Move code to a shared place only when
  a second slice actually needs it.
- `Shell/` and `Infrastructure/` are not slices and hold no feature
  behaviour.
- There is one `AppDbContext`. It declares no entities; a slice adds an
  `IEntityTypeConfiguration` in its own folder and the context finds it.
  Migrations live in `Infrastructure/Persistence/`.
- No mediator library. A page injects its slice's handler class directly.
- Tests mirror the slices: `tests/RestOMatic.Web.Tests/Features/<Name>/`.
- The UI must load nothing from another origin (no CDN fonts or scripts).

## Build, test, run

```sh
dotnet build rest-o-matic-ui.slnx
dotnet test rest-o-matic-ui.slnx
dotnet run --project src/RestOMatic.Web        # http://localhost:5129
podman build --build-arg VERSION=0.0.0-local -t rest-o-matic-ui .
```

Warnings are errors. Package versions go in `Directory.Packages.props`, not
in a project file. The app keeps its database and keys in the directory named
by the `DataDirectory` setting (`./data` locally, `/data` in the image).

## Working here

- Commits follow conventional commits (`feat(scope): ...`, `fix(scope)!: ...`,
  `docs: ...`). Release notes are grouped by them.
- CI and release workflows exist in `.github/workflows/` but have never run.
  `docs/handoff.md` lists what they expect: the solution and a
  `Containerfile` at the repository root.
- Changes go through OpenSpec (`/opsx:explore`, `/opsx:propose`,
  `/opsx:apply`, `/opsx:archive`).
- The format hosts and this app exchange is owned by `../rest-o-matic`. Do
  not invent or change it here; propose the change there. It is written down
  in `../rest-o-matic/contract/`: `checkin/v1` for enrolment and check-ins,
  `validate/v1` for `validate --json`. Each has JSON Schemas and examples;
  test this app's messages and parsing against those files.
