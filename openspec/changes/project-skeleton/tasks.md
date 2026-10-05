## 1. Repository build settings

- [x] 1.1 Add `global.json` pinning SDK `10.0.100` with `rollForward: latestFeature`
- [x] 1.2 Add `.gitignore` (from `dotnet new gitignore`, plus the local `data/` directory) and `.editorconfig` (from `dotnet new editorconfig`)
- [x] 1.3 Add `Directory.Build.props`: `net10.0`, nullable, implicit usings, `TreatWarningsAsErrors`, default version `0.0.0-dev`
- [x] 1.4 Add `Directory.Packages.props` with central package management enabled

## 2. Solution and projects

- [x] 2.1 Create `src/RestOMatic.Web` from the Blazor template (`--interactivity Server --all-interactive --empty`) and remove settings that `Directory.Build.props` now owns
- [x] 2.2 Create `tests/RestOMatic.Web.Tests` from the xUnit template, referencing the web project
- [x] 2.3 Create `rest-o-matic-ui.slnx` at the repository root containing both projects
- [x] 2.4 Move every package version into `Directory.Packages.props`
- [x] 2.5 Remove HTTPS redirection and HSTS from `Program.cs`
- [x] 2.6 Rearrange the template's output into the slice layout: `Shell/` for `App.razor`, `Routes.razor` and the layout; `Features/` and `Infrastructure/` folders; no `Components/Pages` folder; namespaces and `_Imports.razor` updated to match
- [x] 2.7 Confirm `dotnet build` of the solution in Release succeeds with no warnings

## 3. MudBlazor shell

- [x] 3.1 Add the MudBlazor package, register its services, and reference its stylesheet and script from `App.razor`
- [x] 3.2 Build the layout in `Shell/`: MudBlazor providers, an app bar with the app's name, and the main content area
- [x] 3.3 Set the theme's typography to a system font stack, with no reference to Google Fonts or any other origin
- [x] 3.4 In `Shell/`, read the informational version at runtime, drop the `+commit` suffix, and show it in the layout

## 4. Data directory and database

- [x] 4.1 In `Infrastructure/Storage/`, add the `DataDirectory` configuration key, defaulting to `./data`, behind one `AddStorage` registration method
- [x] 4.2 At startup, create the data directory and fail with an error naming it if it cannot be created or written
- [x] 4.3 Persist data-protection keys to `keys/` in the data directory
- [x] 4.4 In `Infrastructure/Persistence/`, add EF Core with the SQLite provider and an `AppDbContext` using `rest-o-matic-ui.db` in the data directory, which has no entities of its own and applies entity mappings found by scanning the assembly, behind one `AddPersistence` registration method
- [x] 4.5 Apply migrations at startup, and confirm that with no migrations this creates the database file without error (see the fallback in `design.md` if it does not)

## 5. Slices

- [x] 5.1 Add the `Home` slice in `Features/Home/`: a placeholder page at `/` that shows the app's name
- [x] 5.2 Add the `Health` slice in `Features/Health/`: an `AddHealth` method that registers the database context check and a `MapHealth` method that maps `GET /healthz`, returning 200 when healthy and 503 when not
- [x] 5.3 Reduce `Program.cs` to composition: framework setup and calls to the infrastructure and slice registration methods, with no feature logic

## 6. Tests

- [x] 6.1 Add a test fixture that starts the app with `WebApplicationFactory` against a temporary data directory and cleans it up; place each test below in a folder mirroring the slice, shell or infrastructure code it tests
- [x] 6.2 Test: `/` responds 200 and contains the app's name, and plain HTTP is not redirected
- [x] 6.3 Test: the home page HTML references no script, stylesheet or font on another origin
- [x] 6.4 Test: the page shows the development version when the build was given none, and the `+commit` suffix is not shown
- [x] 6.5 Test: `/healthz` responds 200, and the database file exists in the data directory after startup
- [x] 6.6 Test: `/healthz` responds 503 when the database cannot be opened
- [x] 6.7 Test: starting with an unwritable data directory fails with an error that names the directory
- [x] 6.8 Test: restarting against the same data directory keeps the existing database and keys
- [x] 6.9 Confirm `dotnet test` of the solution in Release passes

## 7. Container image

- [x] 7.1 Add `.dockerignore` (the one name both Podman and Buildx read) excluding build output, `.git`, `data/`, `docs/` and `openspec/`
- [x] 7.2 Add the `Containerfile`: cross-compiling build stage with a separate restore layer, `VERSION` build argument passed to publish, ASP.NET runtime stage as the non-root `app` user, port 8080, `DataDirectory=/data`, `/data` owned by that user and declared as a volume
- [x] 7.3 Build with `podman build --build-arg VERSION=1.2.3` and run with a named volume on `/data`; confirm `/healthz` responds 200, the UI shows `1.2.3`, the database is under `/data`, and the process is not root
- [x] 7.4 Build once with `--platform linux/arm64` to confirm the cross-compiling build stage works

## 8. Workflows and docs

- [x] 8.1 Switch `ci.yml` from `dotnet-version: 10.0.x` to `global-json-file: global.json`
- [x] 8.2 Point the workflows at `Containerfile`: `file: Containerfile` on the build step in `ci.yml` and `release.yml`, `hashFiles('Containerfile')` in `ci.yml`, and the comment in `release.yml`
- [x] 8.3 Add `README.md`: what the app is, how to run it locally, how to build and run the image (named volume, and `:U` for a bind mount under rootless Podman)
- [x] 8.4 Add the build, test and run commands to `CLAUDE.md`, change its mention of `Dockerfile` to `Containerfile`, and add the vertical slice rules from `design.md` to its decisions so later changes follow them
- [x] 8.5 Update `docs/handoff.md`: the skeleton exists, the build file is `Containerfile`, the SDK version comes from `global.json`, and the `rest-o-matic` binary is not in the image yet
