## Context

See `proposal.md` for why. The stack is already decided in
`docs/handoff.md`: Blazor Server, MudBlazor, SQLite, one process, a container
on the tailnet. The two workflows in `.github/workflows/` fix some of the
shape: a solution at the repository root, a container build file at the root
that takes a `VERSION` build argument, and a build stage that cross-compiles.
They were written to expect that file to be called `Dockerfile`.

The development machine has .NET SDK 10.0.200 and Podman, not Docker. There
is no MudBlazor project template installed.

## Goals / Non-Goals

**Goals:**

- A solution that builds, tests and containerises with no warnings, so the
  workflows have something real to run.
- Settings that are painful to add later are on from the first commit:
  nullable reference types, warnings as errors, central package versions.
- The places later changes need are present and empty: a layout shell, a
  `Features/` folder, a database context, and a test project that can start
  the app.
- The code is organised as vertical slices from the first commit, so the first
  real feature has a pattern to copy and not one to invent.

**Non-Goals:**

- Any entity, migration or page beyond a placeholder.
- Login and accounts, which are a later change.
- TLS, reverse-proxy headers and how the container is deployed on the
  tailnet.
- A `HEALTHCHECK` instruction in the image. The runtime image has no `curl`,
  and Podman ignores the instruction for OCI-format images. The deployment can
  probe `/healthz`.

## Decisions

### Layout

```
rest-o-matic-ui.slnx
global.json  Directory.Build.props  Directory.Packages.props
.gitignore  .editorconfig  .dockerignore  Containerfile  README.md
src/RestOMatic.Web/            Blazor Server app, namespace RestOMatic.Web
tests/RestOMatic.Web.Tests/    xUnit
```

One web project, not a split into `Core`/`Data`/`Web`. The app is small and
has one deployable, and layer projects cut across the slices described next.

The solution uses the `.slnx` format, which is the .NET 10 default and which
`ci.yml` already looks for.

### Vertical slices

Code is grouped by feature, not by technical layer. There are no top-level
`Pages/`, `Services/` or `Models/` folders.

```
src/RestOMatic.Web/
├── Program.cs            composition only: one line per slice and per piece
│                         of infrastructure
├── Features/
│   ├── Home/             the placeholder page
│   └── Health/           the /healthz endpoint and its checks
├── Shell/                App.razor, Routes.razor, the layout, the theme,
│                         the version display
└── Infrastructure/
    ├── Storage/          the data directory, data-protection keys
    └── Persistence/      AppDbContext, migrations
```

The rules, which later changes follow:

- **A slice owns everything for one feature**: its pages and components, the
  code that handles its requests, its endpoints, its entities and their
  database mapping, and any types only it uses.
- **A slice registers itself.** Each exposes an `Add<Feature>` and, if it has
  endpoints, a `Map<Feature>` extension method. `Program.cs` calls them and
  holds no feature logic.
- **Slices do not reference each other.** Code moves to a shared place only
  when a second slice actually needs it, not in anticipation.
- **`Shell/` and `Infrastructure/` are not slices.** They hold what every
  slice stands on: the app frame, and storage. Neither contains feature
  behaviour.
- **One database context, shared.** Each slice keeps its entity mappings in
  its own folder, and the context picks them up by scanning the assembly, so
  adding a feature does not mean editing the context. Migrations stay in one
  place because they describe one database.
- **Tests mirror the slices**: `tests/RestOMatic.Web.Tests/Features/Health/`
  tests `Features/Health/`.

Blazor finds routable pages by scanning the assembly, so pages work from
`Features/` with no extra configuration.

**No mediator library.** A page injects its slice's handler class and calls
it. Vertical slices are often built on MediatR, but in Blazor Server the page
and the handler are already in the same process and the same slice, so a
dispatcher adds indirection without separating anything. MediatR also now
needs a commercial licence.

- *Alternative: layers (`Components/`, `Services/`, `Data/`).* It is the
  template's default, and it spreads each feature across the tree, so a
  change such as "add the landing page" touches every folder.
- *Alternative: a project per slice.* Far more structure than a handful of
  features needs.

### Start from the stock Blazor template, add MudBlazor by hand

`dotnet new blazor --interactivity Server --all-interactive --empty`, then add
the MudBlazor package, its services, its stylesheet and script, and its
providers in the layout.

- *Alternative: the MudBlazor template.* It is not installed, and it brings
  sample pages and a layout that would mostly be deleted.
- **Interactivity is global** (`--all-interactive`), not per page. MudBlazor
  components need an interactive render mode nearly everywhere, and every
  screen in the design is interactive, so per-page opt-in would only add a
  way to get it wrong.

### No assets from other origins

MudBlazor's documented setup loads the Roboto font from Google Fonts. This app
is a control plane on a tailnet and should not make its users' browsers call a
third party, and it should work on a network with no internet. The theme uses
a system font stack instead. MudBlazor's icons are inline SVG and need
nothing.

### Data directory

One configuration key, `DataDirectory`, names the directory. Under it:

- `rest-o-matic-ui.db`, the SQLite database.
- `keys/`, the ASP.NET Core data-protection keys.

The default is `./data` relative to the working directory, which suits local
development and is ignored by git. The image sets `DataDirectory=/data`.

Data-protection keys are persisted because Blazor Server uses them for
antiforgery tokens and, later, login cookies. Kept in the container's
filesystem they would be lost on every restart, and ASP.NET Core logs a
warning about exactly that.

At startup the app creates the directory, and fails with an error naming it if
that is not possible. This is checked before anything else touches the
database, so the failure is one clear message and not an SQLite error.

### EF Core now, with an empty context

EF Core and SQLite are wired up in this change although nothing is stored yet.
The data model is the next change and needs them, and the wiring (the data
directory, the volume, startup, the health check, the test fixture) is
boilerplate that is better reviewed here than mixed in with entities.

The context has no entities of its own. It applies whatever entity mappings it
finds in the assembly, so slices add theirs without touching it.

### Migrations from the start, applied at startup

The app calls `Database.Migrate()` at startup. With no migrations yet, this
creates the database file with only EF Core's history table. The data model
change then adds the first migration and nothing about startup changes.

- *Alternative: `EnsureCreated()`.* It cannot be combined with migrations
  later; a database it created has no history table and has to be thrown
  away.
- *Alternative: apply migrations as a separate deploy step.* That is right
  for several instances sharing a database. This is one process and one file.

### Health endpoint

`/healthz` uses ASP.NET Core health checks with EF Core's database context
check: 200 when the database can be opened, 503 when it cannot.

It is the `Health` slice, and the first to show the pattern: it registers its
own checks and maps its own endpoint. The endpoint is outside the Blazor
components, so that a later login change can leave it open explicitly.

### Version

The `Containerfile` passes `VERSION` to `dotnet publish` as `-p:Version=...`.
The app reads its informational version at runtime, drops the `+commit`
suffix the SDK appends, and shows it in the layout. `Directory.Build.props`
sets the default to `0.0.0-dev`, so a local build is recognisably not a
release.

### Containerfile, not Dockerfile

The build file is named `Containerfile` and there is no `Dockerfile`. Podman
finds it by default. Buildx does not, so both workflows pass
`file: Containerfile` to the build step, and `ci.yml`'s "skip while there is
no build file" check looks for `Containerfile`.

The ignore file keeps the name `.dockerignore`. Podman reads either
`.containerignore` or `.dockerignore`, but Buildx, which CI and the release
use, reads only `.dockerignore`. Using `.containerignore` would mean the
release build silently ignores it and sends the whole repository as context.

- Build stage: `FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0`,
  restore and publish with `-a $TARGETARCH`. Only the runtime stage runs under
  emulation for arm64.
- Restore is a separate layer that copies only the project and props files,
  so a source change does not re-download packages.
- Runtime stage: `mcr.microsoft.com/dotnet/aspnet:10.0`, running as the
  image's built-in non-root `app` user, listening on 8080 (the image's
  default). `/data` is created and owned by that user, and declared as a
  volume.
- Tests do not run in the image build. CI runs them in its own job, and
  running them again per architecture would double the build time.
- Framework-dependent, not self-contained or trimmed. Trimming and Blazor
  Server with reflection-heavy component libraries is a known source of
  runtime failures, and image size does not matter here.

### Build settings

- `global.json` pins SDK `10.0.100` with `rollForward: latestFeature`, so any
  10.0.x SDK is accepted and an 11 SDK is not. `ci.yml` switches to
  `global-json-file`.
- `Directory.Build.props`: `net10.0`, nullable enabled, implicit usings,
  `TreatWarningsAsErrors`.
- `Directory.Packages.props`: central package management, so both projects
  take each package at one version.
- `.editorconfig` from `dotnet new editorconfig`, unmodified.

### Tests

xUnit, with the ASP.NET Core test host (`WebApplicationFactory`) starting the
real app against a temporary data directory. The first tests cover the spec's
scenarios that need no browser: `/` responds and is not redirected, the home
page references no other origin, the page shows the development version,
`/healthz` responds, the database file appears in the data directory, a
restart keeps it, and an unwritable data directory fails startup.

Test files follow the slice they test. The shared fixture that starts the app
is test infrastructure and sits outside `Features/`.

bUnit for component tests is left until there is a component worth testing.

The container scenarios in the spec are checked by hand with `podman build`
and `podman run` in this change, and by the `container` job in CI for the
build itself.

## Risks / Trade-offs

- [The workflows have never run] → Their first run is on the pull request for
  this change. Expect to fix them there; that is in scope.
- [Warnings as errors can block a build when an SDK update adds a new
  analyzer warning] → `rollForward: latestFeature` limits this to feature
  bands, and a single warning can be exempted in `Directory.Build.props`.
- [With only two trivial slices, the structure looks heavier than the code in
  it] → Accepted. The landing page is the next change and is the first slice
  with real content.
- [One shared database context is a point where slices meet] → Mappings live
  in the slices and are found by scanning, so the context itself rarely
  changes.
- [`Migrate()` with zero migrations is an unusual state] → If EF Core
  complains about it, verified during implementation, fall back to creating
  the database file by opening a connection, and keep `Migrate()` for when the
  first migration exists.
- [A bind-mounted `/data` under rootless Podman may not be writable by the
  image's `app` user] → A named volume works without extra flags. The README
  shows the named-volume form and notes the `:U` option for bind mounts.
- [Migrating at startup means a bad migration stops the app starting] →
  Accepted for a single-instance app; the database is one file and can be
  copied before an upgrade.
- [`TARGETARCH`/`BUILDPLATFORM` behave slightly differently in Buildah and
  Buildx] → The image is built locally with Podman and in CI with Buildx, so
  both are exercised.

## Open Questions

- How TLS reaches the app on the tailnet (Tailscale serve, a sidecar, a
  reverse proxy). It decides whether forwarded-header handling is needed, and
  belongs with the deployment or login change.
