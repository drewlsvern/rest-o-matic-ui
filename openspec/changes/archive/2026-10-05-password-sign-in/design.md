## Context

See `proposal.md` for why, and the `sign-in`, `user-accounts` and
`app-hosting` specs for the required behaviour.

What the skeleton gives this change to work with:

- The whole app is interactive. `App.razor` renders
  `<Routes @rendermode="InteractiveServer">`, so after the first response
  each page runs over a SignalR circuit, and a component cannot set a
  cookie.
- Data-protection keys already persist in `/data/keys`, so cookies survive
  a restart. This design still ends sessions on restart, on purpose; see
  "Sessions".
- `AppDbContext` has no entities and there are no migrations. There is no
  `dotnet-ef` tool installed.
- `/healthz` is mapped outside the Blazor components so that it can stay
  open.
- The app serves plain HTTP on 8080. Caddy terminates TLS in front of it.

## Goals / Non-Goals

**Goals:**

- Sign-in uses MudBlazor's own inputs, with validation and a show-password
  toggle. This rules out a static, server-rendered form, because MudBlazor's
  inputs do not post their values from one.
- One place where any sign-in method turns "this is user X" into a session.
  OIDC then adds a table, a callback endpoint and a button.
- Sessions can be ended from the server: at sign-out, on password change,
  and on reset from the container.
- Use the framework's cookie authentication and authorization, not a
  hand-written scheme.

**Non-Goals:**

- Full ASP.NET Core Identity: its UI, its stores, its two-factor support.
- Persisting sessions across restarts.
- Any OIDC code. Only the seams for it.

## Decisions

### Slice layout

```
Features/Accounts/
  AccountsRegistration.cs      AddAccounts, MapAccounts, RunAccountsCommandAsync
  User.cs, PasswordCredential.cs, *Configuration.cs   entities + EF mapping
  Passwords.cs                 hashing, rules, constant-time verify
  SignInThrottle.cs            per-user back-off, per-client limiter
  Sessions/                    ticket store, session index, auth state provider
  SignIn/                      SignInPage.razor, PasswordSignInForm.razor,
                               SignInHandler.cs, SignInTickets.cs, endpoints
  Setup/                       SetupPage.razor, SetupToken.cs
  Users/                       UsersPage.razor, handlers
  Profile/                     ProfileMenu.razor, ProfileDialog.razor,
                               UserAvatar.razor, ProfilePicture.cs, endpoint
  ResetPasswordCommand.cs
Shell/
  CentredLayout.razor          no app bar; used by sign-in and setup
  MainLayout.razor             renders <ProfileMenu /> top right
  Routes.razor                 AuthorizeRouteView + redirect to sign-in
Infrastructure/Hosting/
  ReverseProxyRegistration.cs  forwarded headers from configured proxies
wwwroot/images/rest-o-matic.svg        the logo, with its wordmark
wwwroot/images/rest-o-matic-mark.svg   the machine alone, for the app bar
```

The profile menu belongs to the slice, because it opens the slice's profile
dialog and posts to its sign-out endpoint. `MainLayout` only places it in the
app bar. That one reference from the frame to a slice is accepted: it is what
a layout is for, and no slice references another.

The menu is a `MudMenu` whose activator is a button holding the user's
avatar and display name, or the user name when there is none. In MudBlazor 9
a custom activator opens the menu itself, through the `MenuContext` it is
given. It holds "Profile", "Users" and
"Sign out". "Profile" opens `ProfileDialog`, a `MudDialog` shown through
`IDialogService`, with two tabs (`MudTabs`):

- **Profile:** picture (upload or remove), display name and email. It saves
  through the same handler and rules as the users page.
- **Password:** current password, new password and confirmation. It changes
  the stamp and keeps the session the dialog runs in, which it finds from
  the circuit's `sid` claim.

On save, the menu's avatar and name update without a reload. There are no
profile or change-password pages.

The forwarded-headers setup goes in `Infrastructure/` because it is about
hosting, not a feature.

### Sessions: cookie authentication with a server-side ticket store

ASP.NET Core's cookie handler can keep the authentication ticket on the
server. `CookieAuthenticationOptions.SessionStore` takes an `ITicketStore`,
and the cookie then holds only the store's key. The slice provides an
in-memory store: a `ConcurrentDictionary` from a random 256-bit key to the
ticket and its last-seen time.

- **Idle limit (12 h):** `ExpireTimeSpan` with `SlidingExpiration`, and the
  store drops entries idle longer than that.
- **Absolute limit (7 d):** checked in `OnValidatePrincipal` against the
  ticket's issue time.
- **Restart ends sessions:** the store is in memory. The data-protection
  keys still decrypt the old cookie, but its key is not in the store, so it
  is treated as not signed in.
- **Ending a user's sessions:** each `User` has a `SecurityStamp` (a GUID).
  It changes on password change, reset and removal, and is copied into the
  session's claims at sign-in. `OnValidatePrincipal` compares it with the
  database on every request. A session whose stamp no longer matches is
  rejected and removed. This also covers `reset-password`, which runs in
  another process and cannot reach the in-memory store.
- **Own password change keeps the session:** after changing the stamp, the
  handler updates the stamp in that one session's ticket.

*Alternative: a plain cookie with the claims in it.* Sign-out would not
reach a stolen or copied cookie, and nothing could end sessions on demand.
*Alternative: sessions in SQLite.* They would survive restarts, which nobody
asked for, at the cost of a table and clean-up. An in-memory store keeps
that option open, since `ITicketStore` is the seam.

The stamp check costs one indexed SQLite read per HTTP request. Circuits do
not make HTTP requests, so interactive pages rely on revalidation (next
section).

### Open pages notice when the session ends

A circuit holds the authentication state it started with. The slice
replaces the default provider with a
`RevalidatingServerAuthenticationStateProvider` that:

- checks every 60 seconds that its session key is still in the store and
  its stamp still matches, and
- subscribes to the store's "session removed" event, so a sign-out in
  another tab takes effect at once rather than at the next check.

When the state becomes anonymous, `AuthorizeRouteView` renders its
not-authorised content, which navigates to the sign-in page with
`forceLoad: true`. The circuit learns its session key from a `sid` claim
that the slice puts in the principal when it creates the ticket.

### Database contexts in a circuit

A circuit lives as long as its tab, so a scoped `AppDbContext` would live
that long too, collecting tracked entities and failing if two operations
overlap. Pages and dialogs therefore take a fresh `UserAccounts`, with its
own context, for each operation (`IServiceScopeFactory.WithAccountsAsync`).
Handlers used by HTTP endpoints take the request's scope as usual.

### Forms before the page is interactive

The sign-in and setup pages are prerendered, so their form is visible a
moment before the circuit starts. Anything typed in that moment is replaced
when the page becomes interactive, and a submit would post the form to the
server rather than the circuit. Their submit buttons stay disabled until
`RendererInfo.IsInteractive`.

### The sign-in page is interactive; a one-time ticket sets the cookie

```
SignInPage (interactive, CentredLayout)
  PasswordSignInForm: MudTextField name, MudTextField password (adornment
                      toggles InputType), MudButton submit
        │  SignInHandler.SignInAsync(name, password, clientAddress)
        ▼
  throttle check → verify hash → SignInTickets.Issue(userId, returnUrl,
                                                     browserBinding)
        │  ticket: 256-bit random, single use, 30 s, kept in memory
        ▼
  NavigationManager.NavigateTo("/account/complete?ticket=…", forceLoad: true)
        │
GET /account/complete (minimal API, anonymous)
  redeem ticket → check browser binding → AccountSessions.SignInAsync(ctx, user)
        │  HttpContext.SignInAsync → cookie with session key only
        ▼
  302 to returnUrl (local URLs only, otherwise "/")
```

`AccountSessions.SignInAsync(HttpContext, User)` is the single place where a
session starts. A future OIDC callback is already an HTTP request, so it
calls the same method directly and needs no ticket.

**Browser binding (stops login CSRF).** Without it, someone could sign in to
their own account, get a ticket, and send the completion link to a victim,
who would then be signed in as the attacker. To prevent this, the first
(prerendered) HTTP response of the sign-in page sets a short-lived
`SameSite=Strict`, `HttpOnly` cookie containing a random value. The circuit
reads that value from the `HttpContext` available while prerendering,
through a cascading parameter, and the ticket records a hash of it.
`/account/complete` accepts the ticket only if the request carries the same
cookie. A cross-site link does not carry a `Strict` cookie, so it fails.

**The password passes through the circuit.** A bound `MudTextField` sends
its value to the server over SignalR. The rule against binding secrets is
about host secrets that the server must never see. A sign-in password has to
reach the server anyway, to be checked. The form clears the field after each
attempt, and nothing logs it.

*Alternative: a static SSR form with `[ExcludeFromInteractiveRouting]`, as in
the .NET Identity template.* It needs no ticket, but MudBlazor's inputs do not
post from a static form, so the page would use plain inputs styled to look
like MudBlazor, with no show-password toggle or live validation. Rejected for
that reason.
*Alternative: a JavaScript `fetch` POST that sets the cookie.* It works, but
it adds a hand-written JS file and an antiforgery dance, and gains nothing
over the ticket.

### Sign-in methods are listed from DI

```csharp
public sealed record SignInMethod(string Id, int Order, Type Component);
```

The slice registers the password method:
`SignInMethod("password", 0, typeof(PasswordSignInForm))`. The sign-in page
renders every registered method with `DynamicComponent`, in order. OIDC
would later register a `SignInWithProviderButton` per configured provider,
and the `ExternalLogin` table (provider, subject, user ID) beside
`PasswordCredential`. That is the whole interface. Anything bigger would be
guesswork about an OIDC design that does not exist yet.

### Authorization: a fallback policy and explicit openings

`AddAuthorization` sets a fallback policy that requires an authenticated
user, so anything not marked otherwise is protected, including endpoints
that future changes add. These are opened with `AllowAnonymous`:

- `MapStaticAssets()`, so the logo, CSS and scripts load before sign-in
- the Blazor hub (`/_blazor`), so the sign-in page can open a circuit;
  pages are still checked by their component's authorization
- `/_framework/*` endpoints that `MapRazorComponents` adds, which serve
  Blazor's script when it is not among the static assets. In Development it
  is served from there.
- `/healthz`, `/account/complete`, and the sign-out endpoint (which itself
  needs no user)
- `SignInPage` and `SetupPage`, through `[AllowAnonymous]` on the component

The cookie handler's `OnRedirectToLogin` redirects only `GET` requests that
accept `text/html`, and answers everything else with 401. This is how the
spec's "pages redirect, other requests get 401" is met.

Both openings are made with an endpoint convention on `MapRazorComponents`
that matches the route prefixes, since the framework adds those endpoints
itself. Tests load every script, stylesheet and image the sign-in page
refers to without a cookie, in both Development and Production, and open a
circuit (`/_blazor/negotiate`) without one.

### Sign-out

`POST /account/sign-out`, protected by antiforgery. The profile menu renders a
small `<form method="post">` with `<AntiforgeryToken />`, as the .NET
template's logout form does. The endpoint checks the token itself with
`IAntiforgery.IsRequestValidAsync`: for a minimal API endpoint that binds no
form, the antiforgery middleware only records the outcome and nothing
refuses the request. It then calls `SignOutAsync`, which
removes the store entry and raises "session removed", then redirects to the
sign-in page. A `GET` is not used, so a link or image elsewhere cannot sign
anyone out.

### Passwords

`PasswordHasher<User>` from `Microsoft.Extensions.Identity.Core`, which is
part of the ASP.NET Core shared framework in .NET 10, so no package is
added (the SDK rejects a reference to it as redundant). PBKDF2
with HMAC-SHA512 and 100,000 iterations in its v3 format. When it reports
`SuccessRehashNeeded`, the hash is upgraded at sign-in. For an unknown user
name the handler verifies against a fixed dummy hash, so a missing user
takes as long as a wrong password.

*Alternative: Argon2id.* It would be better against GPU guessing, but .NET
has no built-in implementation, so it would mean a third-party package.
Passwords of 12 characters or more with mixed character types, a
tailnet-only app and throttling make
PBKDF2 enough here. The hash format is versioned, so a later change can
switch.

### Throttling

Both limits are in memory, and a restart resets them.

- **Per user name:** consecutive failures, keyed by the normalised name
  whether the user exists or not, so throttling reveals nothing. From the
  5th failure, the name is locked for 30 s, doubling each time up to
  15 min.
- **Per client address:** a `PartitionedRateLimiter` (fixed window, 10
  attempts per minute) keyed by the client address. Sign-in runs over the
  circuit, not an HTTP endpoint, so ASP.NET's rate-limiting middleware never
  sees it. The handler calls the limiter directly, using the remote address
  captured when the circuit started. That address has already been through
  forwarded-header processing.

### First run

At startup, if the `Users` table is empty, a singleton `SetupToken` makes a
random token (base32, 26 characters) and logs it at `Warning` level with
the setup URL. A middleware sends page requests (GET, `text/html`) to
`/setup` while no user exists; a cached flag turns it off once the first
user is created. The setup page creates the user, clears the token and
starts the session through the same ticket flow. Because the token is held
in memory, a restart issues a new one.

### Reset from the container

`Program.cs` checks `args` before `app.Run()`:

```csharp
if (await app.RunAccountsCommandAsync(args)) return;
```

`reset-password <name>` builds the same host, so it uses the same data
directory, migrates, sets a random 20-character password that meets the
password rules (at least one of each kind of character), changes the
stamp, prints the password and exits. It never starts Kestrel. Usage:
`podman exec rest-o-matic-ui dotnet RestOMatic.Web.dll reset-password alice`.

### Data model and the first migration

```
Users                       PasswordCredentials
  Id            GUID PK       UserId     GUID PK, FK → Users (cascade)
  UserName      text          Hash       text
  NormalizedUserName unique   ChangedAt  timestamp
  Email         text
  NormalizedEmail    unique   ProfilePictures
  DisplayName   text, null      UserId      GUID PK, FK → Users (cascade)
  SecurityStamp GUID            ContentType text
  CreatedAt     timestamp       Data        blob
  HasPicture    bool
  PictureVersion int (bumped on every upload and removal, never reset)
```

The picture's version lives on `Users`, not with the picture, so that it
survives a removal: a picture uploaded after one was removed never reuses an
earlier URL, which a browser might still have cached. It also lets the menu
and the users table build picture URLs without loading any picture bytes.

Credentials have their own table, so a user who signs in only through OIDC
later has no password row, and `ExternalLogins` sits beside it in the same
way. Mappings live in the slice; the migration lives in
`Infrastructure/Persistence/Migrations/`. The `dotnet-ef` tool is pinned in a
local tool manifest (`dotnet-tools.json` at the repository root, where
`dotnet new tool-manifest` puts it), and `Microsoft.EntityFrameworkCore.Design`
is a private asset of the web project.

Names and emails are normalised with `ToUpperInvariant()` for the unique
indexes, as Identity does, so lookups do not depend on SQLite's collation.

### Email and display name

Email is required now so that every user already has one when password reset
by email and notifications arrive. Nothing sends email in this change, so an
address is **not verified**. Two consequences:

- It is never a sign-in name and never used to find an account during sign-in.
- A later OIDC change must not link an external login to a user by matching
  email, because an unverified address would let anyone who can set it claim
  the account. External logins link by provider and subject only.

A later change that sends email will need a verified flag. Adding the column
then is cheap; nothing in this change depends on it.

Validation uses .NET's built-in `EmailAddressAttribute`, through MudBlazor's
data-annotations validation on the forms and the same attribute in the
handlers. It is deliberately loose: exactly one `@`, not first or last, and
no line breaks. Anything stricter rejects real addresses, and the real check
is a mail that arrives. The column has no length limit beyond SQLite's.

The display name is free text and optional. Where the app shows who a user
is (the profile menu, the users table, and later config history), it uses
`DisplayName ?? UserName`. History will record the user ID, so renaming
updates past entries.

### Password rules

12 to 255 characters, with at least one lower-case letter, one upper-case
letter, one digit and one special character, and not equal to the user name
(case-insensitive). "Lower-case" and "upper-case" use `char.IsLower` and
`char.IsUpper`, so letters outside ASCII count. "Special" is anything that is
neither a letter nor a digit, including a space. A failed check lists every
rule the password breaks, not just the first.

The rules are functions in `AccountRules`, used by the handlers and, through
each form model's `IValidatableObject`, by every form, so the form and the
server cannot disagree. Identity's `PasswordValidator` is not used: it
needs a `UserManager`, which this design does not have.

### Profile pictures

A user uploads their own picture from the profile dialog, or removes it.
Without one, a `MudAvatar` shows the initials of the display name, or of the
user name. The avatar appears in the app bar's profile menu, in the dialog,
and in the users table. Other users' pictures can be seen but not changed.
Removing a user removes their picture.

- **Formats:** PNG, JPEG and WebP only. The type is decided by the file's
  first bytes, not by its name or the browser's claimed type, and anything
  else is refused. **SVG is refused** because it can carry script. GIF is
  refused because it is not needed.
- **Size:** at most 1 MB, enforced by `MudFileUpload`'s
  `OpenReadStream(maxAllowedSize)` so a larger file is never read whole.
- **No resizing on the server.** .NET has no built-in cross-platform image
  library. `System.Drawing` is Windows-only, ImageSharp's licence needs
  checking, and SkiaSharp adds native dependencies to the image. Pictures
  are stored as uploaded and shown with `object-fit: cover`, so they look
  right at 32 px. The 1 MB limit bounds the cost. Resizing in the browser
  with a canvas before upload is a possible later improvement.
- **Stored in the database,** in its own table, so the data directory stays
  one file plus keys, a picture is never loaded with the user row, and
  delete cascades.
- **Served by `GET /account/users/{id}/picture?v={version}`.** This needs a
  signed-in user, like everything else, and sets the stored `Content-Type`,
  `X-Content-Type-Options: nosniff`, `Content-Disposition: inline` and
  `Cache-Control: private, max-age=31536000, immutable`. The version in the
  URL changes on each upload, so the cache never shows an old picture. It
  returns 404 when there is no picture.

### Password confirmation

Every form that sets a typed password has a second field. The form model's
`IValidatableObject` compares the two with the shared rules, and the
handler also takes both values and refuses a mismatch, so a test against the
handler covers it without the UI. `reset-password` generates the password,
so it has no confirmation.

### Reverse proxy and cookie flags

`ForwardedHeadersOptions` with `XForwardedFor | XForwardedProto`, and
`KnownProxies` and `KnownNetworks` filled from configuration
(`ReverseProxy:KnownProxies`, `ReverseProxy:KnownNetworks`). The defaults are
cleared, because the framework otherwise trusts loopback, and the spec says
nothing is trusted unless configured. The middleware runs first.

The cookie's `SecurePolicy` is `Always` outside Development, and
`SameAsRequest` in Development so that `dotnet run` on plain HTTP works.
The cookie name is `rom.session`.

### The logo

A hand-written SVG at `wwwroot/images/rest-o-matic.svg`: a rounded 1950s
appliance cabinet with short legs, a large round dial holding a circular
arrow, and three indicator lights, with the "rest-o-matic" wordmark in the
app's system font stack below it. The colours are its own: a cream cabinet,
a burnt-orange dial and a charcoal outline. It is used on the sign-in and
setup pages. The app bar shows the machine alone
(`rest-o-matic-mark.svg`) beside the name as text, because the wordmark is
unreadable at app-bar size. Every shape is drawn for this
project, nothing is traced, and no animal or mascot appears.

### Tests

- `AppFactory` gains `CreateUserAsync(name, password)` and
  `CreateSignedInClientAsync(name)`. The second issues a sign-in ticket
  through the app's services and redeems it over HTTP, so tests go through
  the real cookie path.
- Handler-level tests cover the password rules, throttling, ticket expiry
  and single use, the stamp checks and the command.
- HTTP tests cover the redirects, 401 for non-pages, the anonymous openings,
  cookie flags, forwarded headers and the setup flow.
- bUnit (new test dependency) covers the sign-in form: the error message,
  the show-password toggle, and that the logo is rendered. Centring is
  checked as the layout's classes, not visually.
- `HomePageTests` signs in first.

## Risks / Trade-offs

- [A restart signs everyone out] → Intended. Restarts are rare, and it keeps
  sessions simple. `ITicketStore` lets a later change persist them.
- [Per-request stamp lookup] → One indexed read on a local SQLite file per
  HTTP request. Circuits are not affected. Cache it if it ever shows up in
  profiling.
- [Login CSRF through the ticket link] → Browser binding cookie, as above.
- [The password crosses the circuit] → Over the same TLS connection as a
  form post would use. The field is cleared after each attempt and never
  logged.
- [`/_blazor` opened to anonymous users] → Authorization is still enforced
  per component and on every HTTP endpoint. An anonymous circuit can render
  only anonymous pages.
- [The setup token is in the log, so anyone who can read the logs during
  first run could claim the install] → It works only while no user exists
  and only once. The README says to finish setup straight after the first
  start.
- [Behind Caddy, a missing `KnownProxies` makes every request look like
  HTTP from the proxy] → The `Secure` cookie is still set, because the
  policy is `Always`. But throttling then counts all clients as one, so the
  README's Caddy section sets `KnownProxies`.
- [Without TLS, no one can sign in outside Development] → Stated in the
  proposal as breaking and documented in the README.

### The container image ships Blazor's script

Found while checking this change in the container: the image never
contained `_framework/blazor.web.js`. The `Containerfile` restored from the
project file alone (for layer caching) and published with `--no-restore`.
The SDK adds the package holding that script only for a project with Razor
components, which a restore without the source cannot see. Before login the
missing script was a 404 nobody noticed, so no page in the container was
ever interactive. Publish now restores again with the source present, and
the early restore still caches the packages.

## Migration Plan

1. Archive `project-skeleton`, so that `app-hosting` exists as a main spec.
2. Deploy. The first migration adds `Users`, `PasswordCredentials` and
   `ProfilePictures`.
3. Read the setup token from `podman logs rest-o-matic-ui`, open
   `https://<host>/setup` and create the first user.
4. Configure Caddy to proxy to the container, and set
   `ReverseProxy__KnownProxies__0` to Caddy's address.

Rollback: the previous image ignores the new tables. EF only applies pending
migrations, so a migration it does not know about does no harm.

## Open Questions

- Exact Caddy setup: same pod, or a separate container and network. Only
  the README example and the configured proxy address depend on it.
