# Entering dummy data by hand

Two steps per account. **Do it in this order** — the second step needs the UID
from the first.

## Why not just register through the app?

The sign-up form now asks for a **full name**, so registering through the app
does give a proper `DisplayName` rather than the email address. (It didn't
before — accounts created earlier show up as `student1@demo.school`; those can
be fixed on the Account page, which now has a rename box.)

But for anything except a plain Student it still won't work with fake emails.
Privileged roles (Teacher, Parent, Admin, Coach, Journalist) are only granted to
a **verified** email address, and `teacher1@demo.school` has no mailbox to
receive the verification link.

Creating the profile document in the Firestore console sidesteps this entirely:
that gate only applies to a profile a user creates *for themselves*. Console
writes bypass the security rules completely.

## Step 1 — create the login

Firebase console → **Authentication** → **Users** → **Add user**

- Email: `student1@demo.school`
- Password: `Passw0rd!2026` (any password ≥ 6 chars; keep it the same for all)

Then **copy the User UID** from the list. You need it for step 2.

## Step 2 — create the profile

Firebase console → **Firestore Database** → `users` collection → **Add document**

- **Document ID:** paste the UID from step 1 — *not* the email.

| Field | Type | Value |
|---|---|---|
| `Uid` | string | the same UID |
| `DisplayName` | string | e.g. `Amara Dlamini` |
| `Email` | string | `student1@demo.school` |
| `Role` | string | `Student` |
| `Roles` | array | leave empty, or add strings like `Journalist` |
| `ClassIds` | array | empty |
| `LinkedStudentIds` | array | empty (parents only — see below) |

### Three things that will silently break it

1. **Field names are PascalCase.** `Role`, not `role`. The security rules match
   the exact casing, and a lower-case field reads as missing — the user gets
   denied everywhere with no obvious cause.
2. **Role values are case-sensitive and must match exactly:** `Student`,
   `Teacher`, `Parent`, `Admin`, `Coach`, `Journalist`, `Visitor`.
3. **The document ID must be the Auth UID.** The app looks up
   `users/{uid}`, so an email-keyed document is invisible to it.

## Do the students first

`LinkedStudentIds` holds **student UIDs**, not emails. So create all the
students, note their UIDs, then create the parents with the matching UID in the
array.

## The 28 accounts

Password for all: `Passw0rd!2026`

| Email | DisplayName | Role | Roles |
|---|---|---|---|
| student1@demo.school | Amara Dlamini | Student | `["Journalist"]` |
| student2@demo.school | Ben Nkosi | Student | — |
| student3@demo.school | Chloe van Wyk | Student | — |
| student4@demo.school | Daniel Mokoena | Student | — |
| student5@demo.school | Eshe Adebayo | Student | — |
| student6@demo.school | Farai Moyo | Student | — |
| student7@demo.school | Grace Botha | Student | — |
| student8@demo.school | Hassan Patel | Student | — |
| student9@demo.school | Imani Zulu | Student | — |
| student10@demo.school | Jonas Meyer | Student | — |
| teacher1@demo.school | Naledi Khumalo | Teacher | `["Admin"]` |
| teacher2@demo.school | Peter Steyn | Teacher | `["Journalist"]` |
| teacher3@demo.school | Rina Naidoo | Teacher | — |
| parent1@demo.school … parent10@ | Parent of *(student N)* | Parent | — · set `LinkedStudentIds` to `[studentN-uid]` |
| visitor1@demo.school | Sam Visitor | Visitor | — |
| visitor2@demo.school | Tessa Guest | Visitor | — |
| visitor3@demo.school | Uche Prospect | Visitor | — |
| coach1@demo.school | Vusi Ndlovu | Coach | — |
| coach2@demo.school | Wendy Fourie | Coach | — |

## Shortcut: you probably don't need all 28

Six accounts cover every code path:

| Account | Role | What it tests |
|---|---|---|
| teacher1 | Teacher + `["Admin"]` | The dual dashboard, admin console, class creation |
| teacher3 | Teacher | A plain teacher who *can't* see admin tools |
| student1 | Student + `["Journalist"]` | Additive roles, newspaper permissions |
| student2 | Student | Class enrolment, a plain student view |
| parent1 | Parent, linked to student2 | Parent-child visibility |
| coach1 | Coach | Sports content permissions |

Create those six first. Once **teacher1** exists you can add everyone else from
inside the app — Admin dashboard → **Manage users** — without touching the
console again, because an admin can set anyone's roles directly.

That's also the fastest route overall: make **teacher1** by hand, then do the
rest in the UI.
