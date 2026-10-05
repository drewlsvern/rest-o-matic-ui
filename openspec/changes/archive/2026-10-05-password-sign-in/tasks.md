## 1. Groundwork

- [x] 1.1 Archive `project-skeleton` so `openspec/specs/app-hosting/` exists, and confirm `openspec validate password-sign-in` passes
- [x] 1.2 Add `Microsoft.Extensions.Identity.Core`, `Microsoft.EntityFrameworkCore.Design` (private assets) and `bunit` to `Directory.Packages.props` and the projects
- [x] 1.3 Add a local tool manifest with `dotnet-ef` at the EF Core version in use, and confirm `dotnet tool restore` and `dotnet ef migrations list` work

## 2. Reverse proxy

- [x] 2.1 Add `Infrastructure/Hosting/ReverseProxyRegistration.cs`: forwarded `For` and `Proto` from `ReverseProxy:KnownProxies` and `ReverseProxy:KnownNetworks`, defaults cleared, middleware first in the pipeline
- [x] 2.2 Tests: forwarded headers honoured from a configured proxy, and ignored from any other address and when nothing is configured

## 3. Users and passwords

- [x] 3.1 Add `User` (user name, email, optional display name, stamp) and `PasswordCredential` with their `IEntityTypeConfiguration` classes in `Features/Accounts/`, including the unique normalised name and email and the cascade delete
- [x] 3.2 Generate the first migration into `Infrastructure/Persistence/Migrations/` and confirm a fresh data directory migrates at startup
- [x] 3.3 Add `Passwords`: password rules and confirmation match from the spec, hashing with `PasswordHasher<User>`, rehash on `SuccessRehashNeeded`, and a dummy-hash check for unknown names
- [x] 3.4 Add the user-name, email and display-name rules from the spec, in one place shared by setup, the users page and the profile dialog
- [x] 3.5 Tests: rule edge cases (passwords of 11, 12, 255 and 256 characters, a password missing each kind of character in turn (with every missing kind named in the message), non-ASCII letters counting as upper or lower case, password equal to the name, confirmation mismatch, invalid name characters, a name or email taken in another letter case, missing email and one `EmailAddressAttribute` rejects, a 101-character display name), and that the hash is not the password

## 4. Sessions

- [x] 4.1 Add the in-memory `ITicketStore` with random 256-bit keys, last-seen tracking, idle eviction and a "session removed" event
- [x] 4.2 Register cookie authentication: `rom.session`, the session store, `HttpOnly`, `SameSite=Strict`, `Secure` always outside Development, 12 h sliding expiry
- [x] 4.3 In `OnValidatePrincipal`, reject tickets older than 7 days or with a stale security stamp, removing them from the store
- [x] 4.4 In `OnRedirectToLogin`, redirect only `GET` requests that accept `text/html`, carrying a local return URL, and answer everything else with 401
- [x] 4.5 Add `AccountSessions.SignInAsync(HttpContext, User)` as the single place that builds the principal (with `sid` and stamp claims) and signs in
- [x] 4.6 Add the revalidating authentication state provider: a 60-second stamp and store check, plus an immediate update on "session removed" for its own `sid`
- [x] 4.7 Tests: idle and absolute expiry (with an injected `TimeProvider`), restart ends sessions, stamp change ends sessions, the cookie contains no user data

## 5. Authorization

- [x] 5.1 Set a fallback policy requiring an authenticated user, and add the authentication and authorization middleware in `Program.cs` through the slice's registration
- [x] 5.2 Open the static assets, the Blazor hub, `/healthz`, `/account/complete` and `/account/sign-out` with `AllowAnonymous`
- [x] 5.3 Change `Shell/Routes.razor` to `AuthorizeRouteView` with a not-authorised component that navigates to the sign-in page with `forceLoad`, and add `CascadingAuthenticationState`
- [x] 5.4 Tests: `/` redirects to sign-in, a non-page request gets 401, `/healthz` is open, and the sign-in page's circuit connects without a cookie

## 6. Sign-in

- [x] 6.1 Add `SignInThrottle`: per-name back-off (5 failures, 30 s doubling up to 15 min, reset on success) and a per-client fixed-window limiter of 10 per minute
- [x] 6.2 Add `SignInTickets`: single use, 30-second lifetime, holding the user ID, return URL and the hash of the browser-binding value
- [x] 6.3 Add `SignInHandler`: throttle, verify, issue the ticket, and return the same failure message for unknown names and wrong passwords
- [x] 6.4 Add the browser-binding cookie, set on the sign-in page's prerender and read through the cascading `HttpContext`
- [x] 6.5 Add `GET /account/complete`: redeem the ticket, check the binding, call `AccountSessions.SignInAsync`, and redirect to a local return URL or `/`
- [x] 6.6 Add `SignInMethod` and register the password method; the sign-in page renders methods with `DynamicComponent`
- [x] 6.7 Build `SignInPage` and `PasswordSignInForm` with MudBlazor (`MudTextField`, a show-password adornment, `MudButton`, `MudAlert` for errors), clearing the password after each attempt
- [x] 6.8 Tests: correct, wrong and unknown sign-ins; letter case; throttling messages; a ticket that is reused, expired or redeemed without the binding cookie; a foreign return URL

## 7. Sign-out and the frame

- [x] 7.1 Add `POST /account/sign-out` with antiforgery: sign out, remove the session, redirect to the sign-in page
- [x] 7.2 Add `Shell/CentredLayout.razor` (MudBlazor providers, content centred both ways, no app bar) and use it for the sign-in and setup pages
- [x] 7.3 Add `ProfileMenu` (a `MudMenu` in the slice) and place it top right in `MainLayout`'s app bar: avatar (picture or initials) and display name or user name, with "Profile", "Users" and the sign-out form
- [x] 7.4 Tests: sign-out ends the session and the old cookie no longer works; another circuit on the same session becomes anonymous; the menu shows the display name, or the user name when there is none

## 8. Logo

- [x] 8.1 Draw `wwwroot/images/rest-o-matic.svg` as described in `design.md` (cabinet, dial with a circular arrow, indicator lights, wordmark), with no external references
- [x] 8.2 Show it above the sign-in and setup cards and, small, in the app bar
- [x] 8.3 Compare it side by side with restic's logo and confirm no shared mascot, shape, colours or lettering; test that the SVG contains no "restic" text and no external URL

## 9. First run

- [x] 9.1 Add `SetupToken`: generated at startup only when no user exists, and logged at `Warning` level with the setup URL
- [x] 9.2 Add the middleware that sends page requests to `/setup` while no user exists, using a cached flag
- [x] 9.3 Build `SetupPage` with MudBlazor: token, user name, email, optional display name, password and confirmation; create the user, spend the token, and sign in through the ticket flow
- [x] 9.4 Tests: token logged on an empty database, not logged once a user exists, a wrong token is refused, the token works once, and a restart issues a new token

## 10. Managing users

- [x] 10.1 Build `UsersPage` with a `MudTable` of users (avatar, user name, display name, email) and dialogs to add a user (with confirmed password), edit a user's display name and email, set a password (confirmed), and remove a user (never yourself, never the last user)
- [x] 10.2 Build `ProfileDialog` (a `MudDialog` with `MudTabs`, opened from the profile menu). The Profile tab edits your own picture, display name and email; the menu updates on save without a reload, and no session ends
- [x] 10.3 Add the Password tab to `ProfileDialog`: current password, new password and confirmation; change the stamp and keep the session the dialog runs in (from the circuit's `sid`)
- [x] 10.4 Add `ProfilePicture`: check PNG, JPEG or WebP by content (magic bytes), refuse SVG and everything else, limit 1 MB through `OpenReadStream`, and store, replace or remove with a version bump
- [x] 10.5 Add `GET /account/users/{id}/picture?v=`: signed-in only, stored content type, `nosniff`, inline, private immutable caching, 404 when there is none
- [x] 10.6 Add `UserAvatar` (`MudAvatar`: picture, or initials of the display name or user name) and use it in the profile menu, the dialog and the users table
- [x] 10.7 Tests: add, edit, reset and remove (sessions ended on reset and remove only), removing yourself refused, profile edits keep every session, an email cannot be used as a sign-in name, own password change keeps this session and ends others; picture upload, replace and remove; 2 MB refused; SVG and a renamed text file refused; the picture URL changes after replacing; the picture endpoint refuses anonymous requests and sends `nosniff`

## 11. Reset from the container

- [x] 11.1 Add `ResetPasswordCommand` and `RunAccountsCommandAsync`; `Program.cs` calls it before `app.Run()`
- [x] 11.2 Tests: resets a known user and prints a password that works and meets the password rules, fails for an unknown user and changes nothing, and does not start the web server
- [x] 11.3 Build the image and run `podman exec … dotnet RestOMatic.Web.dll reset-password <name>` against a running container

## 12. Finishing

- [x] 12.1 Update `HomePageTests` and the other existing tests to sign in where they now need to
- [x] 12.2 README: first run and the setup token, running behind Caddy (example Caddyfile and `ReverseProxy__KnownProxies__0`), password reset, and that sign-in needs HTTPS outside Development
- [x] 12.3 `docs/handoff.md`: replace the login open question with what was built, and note OIDC as the next sign-in method
- [x] 12.4 `dotnet build` and `dotnet test` pass with no warnings; sign in, sign out and run setup by hand in the running app
