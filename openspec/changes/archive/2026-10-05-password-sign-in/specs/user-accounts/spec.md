## Purpose

Who can use the central app: what is recorded about each user, creating the
first user on a new install, adding and removing users, editing profiles,
and setting or recovering passwords. Every user has the same rights.

## ADDED Requirements

### Requirement: First-run setup with a one-time token
While no user exists, the app SHALL generate a random setup token at
startup, write it to its log together with the address of the setup page,
and accept it on the setup page to create the first user. The token SHALL be
usable once. Once a user exists, the setup page SHALL be unavailable and no
token SHALL be written. Pages other than setup SHALL redirect to the setup
page while no user exists.

#### Scenario: First start
- **WHEN** the app starts with no users
- **THEN** its log contains a setup token and the setup page address

#### Scenario: Create the first user
- **WHEN** someone enters the correct setup token and an acceptable user name, email address and password, with a matching confirmation, on the setup page
- **THEN** the user is created and signed in

#### Scenario: Wrong token
- **WHEN** someone enters a wrong setup token
- **THEN** no user is created and the page says the token is wrong

#### Scenario: Setup after a user exists
- **WHEN** a user exists and someone requests the setup page
- **THEN** they are sent to the sign-in page and no user can be created there

#### Scenario: Restart before setup
- **WHEN** the app restarts while still no user exists
- **THEN** it logs a new setup token and the previous one no longer works

### Requirement: What is recorded about a user
Each user SHALL have a user name, an email address and a password, and MAY
have a display name.

- A user name SHALL be 1 to 64 characters of letters, digits, `.`, `_`, `-`
  and `@`, SHALL be unique without regard to letter case, and SHALL NOT
  change once the user exists.
- An email address SHALL be required, SHALL pass the platform's standard
  email address validation, and SHALL be unique without regard to letter
  case. The app SHALL NOT treat it as
  verified, and SHALL NOT use it to identify a user when signing in.
- A display name SHALL be optional, at most 100 characters, and free text.
  Where the app shows who a user is, it SHALL show the display name if there
  is one and the user name otherwise.
- A password SHALL be 12 to 255 characters long, SHALL contain at least one
  lower-case letter, one upper-case letter, one digit and one special
  character (any character that is not a letter or a digit), and SHALL NOT
  be the same as the user name. The app SHALL store only a salted, slow hash of
  each password.

#### Scenario: Display name shown
- **WHEN** user `alice` has the display name "Alice Smith" and is signed in
- **THEN** the profile menu shows "Alice Smith"

#### Scenario: No display name
- **WHEN** user `bob` has no display name and is signed in
- **THEN** the profile menu shows `bob`

#### Scenario: Email missing or malformed
- **WHEN** a user is created with no email address, or with `bob.example.com`
- **THEN** it is refused with a message about the email address

#### Scenario: Email taken
- **WHEN** a user is created with `Bob@Example.com` while another user has `bob@example.com`
- **THEN** it is refused because the email address is in use

#### Scenario: Email is not a sign-in name
- **WHEN** someone enters a user's email address in the user name field when signing in
- **THEN** they are not signed in

#### Scenario: Short password
- **WHEN** a password of 11 characters is chosen
- **THEN** it is refused with a message giving the minimum length

#### Scenario: Long password
- **WHEN** a password of 256 characters is chosen
- **THEN** it is refused with a message giving the maximum length

#### Scenario: Missing a character type
- **WHEN** a 12-character password has no digit, or no upper-case letter, or no lower-case letter, or no special character
- **THEN** it is refused with a message naming each kind of character it lacks

#### Scenario: Acceptable password
- **WHEN** the password `Correct-horse7` is chosen for user `alice`
- **THEN** it is accepted

#### Scenario: Name taken
- **WHEN** a user `Alice` is added while `alice` exists
- **THEN** it is refused because the name is taken

#### Scenario: Password not stored
- **WHEN** the database is inspected after a user is created
- **THEN** it does not contain the password in plain text

### Requirement: A typed password is confirmed
Wherever a person types a new password (first-run setup, adding a user,
setting another user's password, changing their own), the form SHALL ask for
it twice and SHALL refuse to save it unless both entries match.

#### Scenario: Entries differ
- **WHEN** the new password and its confirmation differ
- **THEN** nothing is saved and the form says the passwords do not match

### Requirement: Managing users
A signed-in user SHALL be able to see every user with their user name,
display name and email address, add a user with an initial password, change
another user's display name and email address, set a new password for
another user, and remove another user.
A user SHALL NOT be able to remove themselves, and the last user SHALL NOT
be removable.

#### Scenario: Add a user
- **WHEN** a signed-in user adds `bob` with an email address and an acceptable, confirmed password
- **THEN** `bob` can sign in with that password

#### Scenario: Reset another user's password
- **WHEN** a signed-in user sets a new password for `bob`
- **THEN** `bob` can sign in only with the new password

#### Scenario: Remove a user
- **WHEN** a signed-in user removes `bob`
- **THEN** `bob` can no longer sign in

#### Scenario: Remove yourself
- **WHEN** a signed-in user tries to remove their own account
- **THEN** the app does not offer or allow it

### Requirement: Edit your own profile
A signed-in user SHALL be able to change their own display name, email
address and profile picture, and their own password, in one dialog opened
from the profile menu, without leaving the page they are on. Changing the
display name, email address or picture SHALL NOT end any session.

#### Scenario: Change display name
- **WHEN** a user opens "Profile" from the profile menu and sets their display name to "Al"
- **THEN** the dialog closes, the user is still on the same page, and the profile menu shows "Al"

#### Scenario: Clear display name
- **WHEN** a user clears their display name
- **THEN** the profile menu shows their user name

### Requirement: Profile picture
A user SHALL be able to upload a profile picture for themselves, replace it
or remove it. The picture SHALL be a PNG, JPEG or WebP image of at most 1 MB.
The type SHALL be judged from the file's content, not its name. Any other
file, including SVG, SHALL be refused. Where the app shows a user (the
profile menu and the users list), it SHALL show their picture, or their
initials when they have none. Pictures SHALL be visible only to signed-in
users. A user SHALL NOT be able to change another user's picture.

#### Scenario: Upload
- **WHEN** a user uploads a 200 KB JPEG from the profile dialog
- **THEN** the profile menu shows that picture without a page reload

#### Scenario: Too large
- **WHEN** a user uploads a 2 MB PNG
- **THEN** it is refused with a message giving the 1 MB limit, and the previous picture is kept

#### Scenario: Not an allowed image
- **WHEN** a user uploads an SVG file, or a text file renamed to `me.png`
- **THEN** it is refused with a message listing the allowed formats

#### Scenario: Remove
- **WHEN** a user removes their picture
- **THEN** their initials are shown in its place

#### Scenario: Replaced picture is not shown from cache
- **WHEN** a user replaces their picture
- **THEN** other pages that load afterwards show the new picture, not the old one

#### Scenario: Not signed in
- **WHEN** a client that is not signed in requests a user's picture
- **THEN** it is not given the picture

### Requirement: Change your own password
A signed-in user SHALL be able to change their own password in the profile
dialog by entering their current password and a new acceptable one,
confirmed.

#### Scenario: Correct current password
- **WHEN** a user enters their current password and a new acceptable password
- **THEN** the new password works for signing in and the old one does not

#### Scenario: Wrong current password
- **WHEN** a user enters a wrong current password
- **THEN** the password is not changed

### Requirement: Reset a password from the container
The app SHALL provide a `reset-password <user name>` command, run with the
same data directory as the app, that sets a new random password, meeting the
password rules, for an
existing user, prints it once, and ends that user's sessions the next time
the app checks them. It SHALL NOT start the web server.

#### Scenario: Reset
- **WHEN** an operator runs `reset-password alice` inside the container
- **THEN** a new password is printed and `alice` can sign in with it

#### Scenario: Unknown user
- **WHEN** an operator runs `reset-password nobody`
- **THEN** the command fails with a message naming the user and changes nothing

#### Scenario: App is running
- **WHEN** the command is run while the app is running and `alice` is signed in
- **THEN** `alice`'s sessions end no later than their next request
