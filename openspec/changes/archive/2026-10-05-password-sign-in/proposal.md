## Why

The app is meant to run "on the tailnet, behind a login", and it has no
login. Every later change needs one. The landing page shows every host's
backups, config history must record who changed what, and config editing
lets whoever uses the app run commands on every enrolled host. Dropping the
viewer key on 2026-10-05 removed the only constraint on how users sign in.
So login can start with the simplest method, a password, without closing
the door on OIDC providers later.

## What Changes

- Users sign in with a name and password on a MudBlazor sign-in page,
  centred on the screen under an original rest-o-matic logo.
- Every page and endpoint requires a signed-in user, except the sign-in
  flow itself, first-run setup, static assets and `/healthz`.
- Sessions are held on the server. The browser's cookie carries only a
  random session ID. Sign-out ends the session at once, sessions expire
  after 12 hours idle or 7 days in total, and restarting the app signs
  everyone out.
- Repeated wrong passwords are slowed down, per user and per client.
- **First run:** while no user exists, the app writes a one-time setup token
  to its log, and a setup page accepts that token to create the first user.
- Each user has a user name, an email address, a password (typed twice
  wherever it is set) and an optional display name. Email is stored for
  password reset and notifications in later changes; nothing sends email
  yet, and it is not verified.
- Signed-in users can add other users, edit their display name and email,
  reset another user's password and remove users. All users have the same
  rights.
- Users can edit their own display name, email and profile picture, and
  change their own password. A command run inside the container resets a password when nobody
  can sign in.
- A profile menu in the top right of the app bar shows the user's picture
  (or initials) and display name (or user name), and holds "Profile",
  "Users" and "Sign out". "Profile" opens one dialog for editing your own
  picture, display name and email and for changing your password. There are
  no separate pages for these.
- Users can upload a profile picture (PNG, JPEG or WebP, up to 1 MB) or
  remove it. Pictures are shown in the profile menu and the users list.
- Sign-in is built so that an OIDC provider can be added later as a second
  method: every method ends by starting a session for a known user, and the
  user model has room for logins from external providers.
- The app trusts `X-Forwarded-For` and `X-Forwarded-Proto` from configured
  reverse-proxy addresses (Caddy for now), and marks its cookies `Secure`.
- **BREAKING:** `/` and every other page now redirect to the sign-in page
  when nobody is signed in. A deployment without TLS in front can no longer
  sign in, except in the Development environment.

Not in this change: OIDC itself, roles or permissions, two-factor
authentication or passkeys, encrypting file listings, and the bearer
credential hosts use to check in.

## Capabilities

### New Capabilities

- `sign-in`: signing in with a password, sessions and their expiry,
  signing out, throttling wrong passwords, the rule that pages require a
  signed-in user, and the sign-in page with its logo.
- `user-accounts`: what is recorded about a user (user name, email, optional
  display name, password), first-run setup with a one-time token, adding,
  editing, removing and resetting users from the UI, editing your own
  profile and password, and resetting a password from inside the container.

### Modified Capabilities

- `app-hosting` (from `project-skeleton`, not yet archived): the home page
  needs a signed-in user, and the app honours forwarded headers from a
  configured reverse proxy.

## Impact

- **Code:** a new `Features/Accounts/` slice. `Program.cs` gains the
  authentication and authorization middleware and the slice's
  registration. `Shell/` changes the layout's app bar and a route guard. The
  first EF Core entities and the first migration go in
  `Infrastructure/Persistence/`.
- **Packages:** none for passwords: Identity's password hasher is in the
  shared framework. Full ASP.NET Core Identity is not used.
  `Microsoft.EntityFrameworkCore.Design` and the `dotnet-ef` tool for the
  migration; bUnit and `Microsoft.Extensions.TimeProvider.Testing` for
  tests.
- **Containerfile:** publish restores again with the source present, which
  makes the image include Blazor's script. Without it no page in the
  container is interactive (see design).
- **Database:** the first migration, adding users (with email and display
  name), password credentials and profile pictures.
- **Container:** a `reset-password` command runs the app's entry point in a
  second mode. Deployments need TLS in front (Caddy) and the proxy's address
  set in configuration.
- **Tests:** the existing home-page test now has to sign in first.
  `AppFactory` gains a way to create a user and a signed-in client.
- **Docs:** `README.md` (first run, Caddy, password reset) and the open
  login question in `docs/handoff.md`.
- **Ordering:** `project-skeleton` should be archived first, so that
  `app-hosting` exists as a main spec for this change's delta to modify.
