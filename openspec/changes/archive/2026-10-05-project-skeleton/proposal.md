## Why

This repository holds design documents and two workflows but no application.
Every later change (the data model, the landing page, the check-in endpoints)
needs a project to land in, and the CI and release workflows have never run
because there is no solution or container build file for them to build. Getting the
skeleton in first means those changes are about behaviour, not plumbing.

## What Changes

- Add a solution at the repository root with one web project,
  `src/RestOMatic.Web`, and one test project, `tests/RestOMatic.Web.Tests`.
- The web project is a Blazor Server app with MudBlazor: a layout shell and a
  placeholder home page, with the template's sample pages left out.
- Organise the code as vertical slices: one folder per feature, holding
  everything that feature needs.
- Wire up SQLite through EF Core with an empty database context. The database
  lives in a data directory whose location comes from configuration, and is
  created at first start. It is added now, with nothing in it, because the
  very next change needs it.
- Add a `/healthz` endpoint that reports whether the app and its database are
  working.
- Show the app's version in the UI, taken from the build.
- Add a `Containerfile` at the repository root: a build stage that cross-compiles
  for the target architecture, a `VERSION` build argument, a non-root runtime
  user, and `/data` as the data volume.
- Add repository-wide build settings: a pinned SDK (`global.json`), nullable
  reference types and warnings as errors (`Directory.Build.props`), package
  versions in one file (`Directory.Packages.props`), and `.gitignore`,
  `.editorconfig` and `.dockerignore`.
- Switch `ci.yml` to read the SDK version from `global.json`, so CI and local
  builds cannot differ.
- Point `ci.yml` and `release.yml` at `Containerfile`. They were written to
  expect a `Dockerfile`, which this repository does not have.
- Add a short `README.md` and build commands in `CLAUDE.md`.

Not in this change:

- **Entities and migrations.** The database context is empty. The data model
  change adds the first entities and the first migration.
- **Login and accounts.** These come later as their own change.
- The landing page, Monaco and the check-in endpoints.
- The `rest-o-matic` binary in the image. It is left for the config editing
  change, because no release of `rest-o-matic` yet has JSON output from
  `validate` or understands `!locked`.

## Capabilities

### New Capabilities

- `app-hosting`: how the central app runs as a process and as a container:
  the port it serves on, its health endpoint, where it keeps its data, how it
  reports its version, and what the container image guarantees.

### Modified Capabilities

None. There are no existing specs.

## Impact

- **Code**: new `src/` and `tests/` trees, a solution file, and build
  configuration files at the repository root. No existing code is changed,
  because there is none.
- **Dependencies**: MudBlazor, EF Core with the SQLite provider, the EF Core
  health check package, xUnit and the ASP.NET Core test host. Container base
  images are Microsoft's .NET 10 SDK and ASP.NET runtime images.
- **Workflows**: `ci.yml` stops skipping and starts building, testing and
  building the image. `release.yml` becomes able to publish. Neither has run
  before, so their first run may need fixes that are outside this change's
  code.
- **Docs**: `docs/handoff.md` says the image includes `rest-o-matic`
  binaries. After this change that is still to come, and the handoff is
  updated to say so.
