# sign-in Specification

## Purpose
How a person signs in to the central app, how long a session lasts and how
it ends, and which parts of the app need a signed-in user. Password sign-in
is the first method; others are expected to join it.

## Requirements
### Requirement: Pages require a signed-in user
Every page and endpoint of the app SHALL require a signed-in user, except the
sign-in page and its completion step, the first-run setup page, static
assets, and `GET /healthz`. A request without a signed-in user for a page
SHALL be redirected to the sign-in page, which SHALL return the user to the
page they asked for after they sign in. A request without a signed-in user
for anything other than a page SHALL be answered with status 401 and not
redirected.

#### Scenario: Page without signing in
- **WHEN** a browser that is not signed in requests `/`
- **THEN** it is redirected to the sign-in page

#### Scenario: Return to the requested page
- **WHEN** a browser that is not signed in requests a page, is redirected, and then signs in
- **THEN** it is taken to the page it originally requested

#### Scenario: Return address on another site
- **WHEN** the sign-in page is given a return address on another origin
- **THEN** after signing in the browser is taken to `/` and not to that address

#### Scenario: Health stays open
- **WHEN** a client that is not signed in requests `GET /healthz`
- **THEN** the app responds as it would to a signed-in client

### Requirement: Sign in with a name and password
A user SHALL sign in by entering their user name and password on the sign-in
page. User names SHALL be compared without regard to letter case. A failed
sign-in SHALL show the same message whether the user name does not exist or
the password is wrong.

#### Scenario: Correct password
- **WHEN** a user enters their user name and correct password
- **THEN** they are signed in and taken to the page they asked for, or `/`

#### Scenario: Wrong password
- **WHEN** a user enters an existing user name with a wrong password
- **THEN** they are not signed in and the page shows "Incorrect user name or password"

#### Scenario: Unknown user
- **WHEN** someone enters a user name that does not exist
- **THEN** they are not signed in and the page shows the same message as for a wrong password

#### Scenario: Different letter case
- **WHEN** a user named `alice` signs in as `Alice` with the correct password
- **THEN** they are signed in as `alice`

### Requirement: The session lives on the server
Signing in SHALL start a session held by the app. The browser's cookie SHALL
carry only a random session identifier, and SHALL be `HttpOnly`,
`SameSite=Strict` and, outside the Development environment, `Secure`. A
session SHALL end after 12 hours without a request from it, or 7 days after
it started, whichever comes first. All sessions SHALL end when the app
restarts.

#### Scenario: Idle too long
- **WHEN** a session has made no request for more than 12 hours
- **THEN** its next request is treated as not signed in

#### Scenario: Absolute limit
- **WHEN** a session has been in use continuously for more than 7 days
- **THEN** its next request is treated as not signed in

#### Scenario: Restart
- **WHEN** the app is restarted while a user is signed in
- **THEN** that user's next request is treated as not signed in

#### Scenario: Cookie holds no user data
- **WHEN** a user signs in
- **THEN** the cookie set by the app contains no user name, user ID or password material

### Requirement: Sign out
A signed-in user SHALL be able to sign out from the profile menu in the top
right corner of the app bar. Signing out
SHALL end the session on the server at once, and any page still open on
that session SHALL stop working and show the sign-in page.

#### Scenario: Sign out
- **WHEN** a signed-in user chooses "Sign out"
- **THEN** their session ends and they are shown the sign-in page

#### Scenario: Old cookie after signing out
- **WHEN** a request is made with the cookie of a session that was signed out
- **THEN** it is treated as not signed in

#### Scenario: Other tab
- **WHEN** a user signs out in one tab while another tab on the same session is open
- **THEN** the other tab shows the sign-in page no later than its next interaction

### Requirement: Ending a user's sessions
Removing a user or changing or resetting a user's password SHALL end every
session that user has, except that a user changing their own password SHALL
stay signed in on the session they changed it from.

#### Scenario: Password reset by another user
- **WHEN** a user's password is reset while they are signed in
- **THEN** their sessions end and they must sign in with the new password

#### Scenario: Own password change
- **WHEN** a user changes their own password
- **THEN** the session they changed it from stays signed in and their other sessions end

### Requirement: Wrong passwords are slowed down
After 5 consecutive failed sign-ins for one user name, the app SHALL refuse
further attempts for that user name for a period that grows with each
further failure, up to 15 minutes, and SHALL say when to try again. A
successful sign-in SHALL reset the count. The app SHALL also limit how many
sign-in attempts one client address can make per minute, whatever user names
it tries.

#### Scenario: Five failures
- **WHEN** a user name has had 5 failed sign-ins in a row
- **THEN** the next attempt for that user name is refused, even with the correct password, and the page says when to try again

#### Scenario: Many user names from one client
- **WHEN** one client address makes more sign-in attempts in a minute than the limit allows
- **THEN** further attempts from that address are refused until the minute has passed

#### Scenario: Success resets
- **WHEN** a user signs in successfully after fewer than 5 failures
- **THEN** their failure count starts again from zero

### Requirement: The sign-in page
The sign-in page SHALL show the rest-o-matic logo above a sign-in form,
centred horizontally and vertically on the screen, without the app's
navigation. The form SHALL let the user show the password as they type it.
The page SHALL offer only the sign-in methods that are enabled; in this
change that is the password form.

#### Scenario: Layout
- **WHEN** the sign-in page is shown on a desktop or a phone screen
- **THEN** the logo and the form are centred, and no app bar or navigation is shown

#### Scenario: Show password
- **WHEN** the user turns on "show password"
- **THEN** the password field shows its text, and turning it off hides it again

### Requirement: The logo is original
The rest-o-matic logo SHALL be an original drawing, served by the app itself.
It SHALL NOT use, copy or imitate the restic logo, mascot, colours or
lettering, and SHALL NOT contain the word "restic".

#### Scenario: Served locally
- **WHEN** the sign-in page is loaded
- **THEN** the logo is served by the app and no request is made to another origin

#### Scenario: No restic branding
- **WHEN** the logo file is inspected
- **THEN** it does not contain the text "restic"
