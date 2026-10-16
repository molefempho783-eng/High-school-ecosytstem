# Running HighSchoolEcosystem

The app is configured to talk to the **real** Firebase project
`high-school-ecosystem-dev` (`UseEmulator: false`). No emulators needed.

## Prerequisites

- **.NET 10 SDK** with the MAUI workload (`dotnet workload install maui`)
- **Visual Studio 2022/2026** with the ".NET Multi-platform App UI development" workload
- **Node.js 20** + **Firebase CLI** (`npm install -g firebase-tools`) — for deploying
  rules and the auto-marking Cloud Function
- **Windows Developer Mode** enabled (Settings → System → For developers) — the app
  builds unpackaged (`WindowsPackageType=None`)

## One-time Firebase setup

### 1. Enable Email/Password sign-in

Firebase console → **Authentication** → **Sign-in method** → enable
**Email/Password**. Without this, registration fails with
`OPERATION_NOT_ALLOWED`.

### 2. Create the Firestore database

Firebase console → **Firestore Database** → **Create database**. Pick the
`nam5` location to match `firebase.json`. Start in **production mode** — the
rules in this repo replace the defaults in the next step.

### 3. Paste in your Web API Key

Firebase console → **Project settings** → **General** → **Web API Key**.
Copy it into `HighSchoolEcosystem/Configuration/appsettings.json`:

```json
"WebApiKey": "AIza...your-key-here"
```

This is a public client identifier — it ships inside every Firebase app and is
not a secret — but the app can't authenticate without it. If you leave it
blank, the sign-in screen will tell you so rather than failing silently.

### 4. Deploy the security rules

From the repo root:

```bash
firebase login
firebase deploy --only firestore:rules,firestore:indexes --project high-school-ecosystem-dev
```

**Do this before anyone signs in.** The rules are what stop a user from making
themselves an admin. See "Security model" below.

## Running the app

### Visual Studio

1. Open `HighSchoolEcosystem.slnx`, set **HighSchoolEcosystem** as startup project.
2. Pick **Windows Machine** (or an Android emulator) from the debug dropdown.
3. Press **F5**.

### Command line

```bash
# Windows
dotnet build HighSchoolEcosystem/HighSchoolEcosystem.csproj -t:Run -f net10.0-windows10.0.19041.0

# Android
dotnet build HighSchoolEcosystem/HighSchoolEcosystem.csproj -t:Run -f net10.0-android
```

## Assigning roles

Admins get their **own dashboard** (separate from the student/teacher one) with
three tools: **Manage users**, **Roster / invites**, and **School settings**.
Everything below can now be done in-app — the Firebase console is only needed to
create the very first admin.

### Someone who has already registered → Manage users

Admin dashboard → **Manage users**. Search, tap a person, then:

- **Role** — pick any role; saves immediately to their profile.
- **Linked students** — appears when the role is *Parent*. Tick the children
  they're allowed to view grades and attendance for.

Changes take effect the next time that person's profile loads (sign out/in, or
reopening the app).

Editing your own account shows a warning before you remove your own admin
access — if you're the only admin, getting it back means editing Firestore
directly.

## The roster: assigning roles before someone signs up

Roles are assigned **at sign-up** from an admin-managed roster in the
`invites` collection. When someone registers, the app looks for
`invites/{their-email-lowercased}` and creates their profile with the role
recorded there. No roster entry means they come in as a **Student**.

The Firestore rules verify this independently — a tampered client cannot give
itself a role that isn't on the roster.

### Adding someone to the roster

Easiest path is in-app: Admin dashboard → **Roster / invites** → enter the email,
pick the role, **Add to roster**.

Or via Firebase console → **Firestore Database** → `invites` → **Add document**:

- **Document ID:** their email address, **lower-cased** (e.g. `jane.doe@school.org`)
- **`Role`** (string): `Student`, `Teacher`, `Parent`, `Admin`, `Coach`, `Journalist` or `Visitor`
- **`Roles`** (array of strings, optional): additional roles held alongside the
  primary one, e.g. `["Journalist"]`
- **`ClassIds`** (array of strings, optional): classes to enrol/assign them to
- **`LinkedStudentIds`** (array of strings, required for `Parent`): the student
  UIDs this parent may view

Field names are PascalCase (`Role`, not `role`) — the rules match the exact
casing the C# models produce. See PLAN.md §4.

Add the invite **before** the person registers. If they've already signed up,
their profile exists and the roster is no longer consulted — change their
`Role` on the `users/{uid}` document instead.

### Staff and parents must verify their email

`Teacher`, `Parent` and `Admin` invites only take effect once the person has
**verified their email address**. Students are exempt and can start immediately.

This matters because Firebase lets anyone register any address that isn't
already taken, *without* proving they own it. Without this check, a stranger
who guessed a rostered address (say `principal@school.org`) could register it
first and inherit that role. Requiring verification means they'd also need
access to the mailbox.

What a rostered staff member sees: they register, land on the dashboard, and
get a **"Verify your email to finish setting up"** panel with a resend button.
Once they click the link they must **sign out and sign in again** — the role is
read from their ID token, which only picks up the verified flag on a fresh
sign-in. Their profile is created at that point with the role from the roster.

### Creating the first admin

Chicken-and-egg: only admins can write invites, and you have no admin yet. So
bootstrap the first one by hand:

1. Register in the app (you'll come in as a Student).
2. Firebase console → **Firestore Database** → `users` → find your document
   (the ID is your Auth UID, from Authentication → Users).
3. Change **`Role`** to `Admin`.
4. Sign out and back in.

From then on everything is in-app: use **Manage users** for people who have
already registered, and **Roster / invites** for people who haven't.

## What each role gets

### Roles are additive

Everyone has one **primary** role and may hold **additional** roles alongside
it. Any combination works — admin *and* teacher, student *and* teacher, student
*and* journalist. Holding a role never removes what another one grants.

The **primary role decides which dashboard you land on**, and every dashboard
links to the others you qualify for:

- Land on the **teacher** console but also hold Student? A *"Switch to student
  view"* button takes you to your own classes and grades.
- Land on the **content dashboard** but also hold Teacher or Admin? *"Switch to
  teacher/admin tools"* buttons appear.

Those are pushed, not root swaps, so the back button returns you where you were.

Set the primary role and tick the extras under **Manage users**. The roster can
grant Journalist at sign-up; any other combination is assigned after they
register.

> Someone who is both a Student and a Teacher of the same class can grade their
> own submission — the rules grant teacher rights on classes they own, and don't
> exclude them from their own roster. That's an unusual arrangement to set up
> deliberately, but worth knowing it isn't blocked.

| Role | Lands on | Can do |
|---|---|---|
| **Admin** | Admin dashboard | Manage users, roster, school settings. Full read/write |
| **Teacher** | Teacher dashboard | Create classes, enrol students, set assignments & homework, build the timetable for their own classes |
| **Coach** | Content dashboard | School-community access plus publishing sports fixtures, results and sports news |
| **Student** | Content dashboard | Their classes, assignments, timetable, own grades |
| **Parent** | Content dashboard | Read-only view of their linked children's data |
| **Journalist** | *(additive)* | Writes school newspaper articles. Held alongside Student or Teacher — grants nothing else, and deliberately **not** school-wide announcement rights |
| **Visitor / guest** | Content dashboard | Public announcements, sports fixtures and news only |

### Teacher tools

Teacher dashboard → **My classes** to create a class (name, subject, room). Open
a class to:

- **Enrol students** — tick from the list of registered students, then save.
- **Set assignments and homework** — title, instructions, type, due date/time
  and points. The type picker is what separates an Assignment from Homework.
- **Add multiple-choice questions** — tap an assignment to open its question
  list. Each question takes text, two to four options, the correct answer and a
  points value. Tap a question in the list to load it back into the form and
  edit it; the heading and button switch to edit mode, and **Cancel edit**
  returns to adding. Editing keeps the question's position in the list.

> **Answers are stored separately from questions on purpose.** Students have to
> be able to read the questions to answer them, so if the correct option were a
> field on the question document every student could read the answer key.
> Questions live in `.../assignments/{id}/questions`, answers in
> `.../assignments/{id}/answerKeys` — the latter is teacher/admin-only.
> Don't merge them.
>
> A consequence: **auto-grading can't happen on the student's device**, because
> the device can't read the key. Grading has to run either on the teacher's
> device (which can) or in a Cloud Function. Student answering and grading
> aren't built yet.

Teacher dashboard → **Timetable** adds weekly periods to classes you own. It
warns if a new period overlaps one of your own existing periods.

> The clash check only covers *your* periods. It can't see another teacher
> booking the same room at the same time — school-wide venue conflict detection
> is a later milestone (PLAN.md M4).

A teacher can only create a class owned by themselves, and can only edit or
schedule classes where they are the `TeacherId`. The Firestore rules enforce
that, not the UI.

## Test accounts

`tools/seed-accounts.mjs` creates 28 dummy accounts (10 students, 3 teachers,
10 parents, 3 visitors, 2 coaches) with roles and parent links already wired up:

```powershell
npm install firebase-admin
node tools/seed-accounts.mjs --key "C:\path\to\serviceAccountKey.json"
```

Every account uses the password `Passw0rd!2026`. Sign in as
**teacher1@demo.school** to exercise the Admin+Teacher dual dashboard. Full
details, including how to remove them again, in `tools/README.md`.

To enter them by hand instead, `tools/MANUAL-DATA.md` has the console steps and
the exact field shapes. Note that a hand-made account needs its profile created
in the **Firestore** console too — creating only the Auth user leaves the app
with no role to read.

### What a student sees

The student dashboard shows their real **timetable** (class, subject, room, only
for classes they're enrolled in) and **homework & assignments** across every
class, sorted by due date with overdue items in red.

Tapping a piece of work opens it: instructions, any multiple-choice questions
as lettered radio buttons (A/B/C/D), a written-answer box, and a submit button.
Students can resubmit — the page switches to "Update submission" — and the
grade and feedback appear at the top once it's marked.

> Students never receive the answer key. It lives in a separate collection the
> rules deny them, so the questions arrive without answers attached. That also
> means **marking cannot run on the student's device** — which is exactly why
> it runs in a Cloud Function. See "Automatic marking" below.

> Answers are stored as the option *text*, not its position, so a teacher
> reordering the choices later can't silently change what a student answered.

## File uploads (course material)

Teachers can attach a chapter, a link, or an uploaded file to a learning unit.
Only the upload path needs setup — chapters and links work with no extra config,
and the upload option is hidden entirely when Storage isn't configured.

Three things have to be true, and missing any one of them is the usual cause of
"nothing happens when I choose a file":

**1. Storage has to be enabled on the project.** Firebase console → Build →
Storage → Get started. Note the bucket name it shows you.

**2. `Firebase:StorageBucket` must match that bucket exactly.** In
`Configuration/appsettings.json`:

```json
"Firebase": {
  "StorageBucket": "high-school-ecosystem-dev.firebasestorage.app"
}
```

> **Check the suffix.** Projects created from about late 2024 use
> `.firebasestorage.app`; older ones use `.appspot.com`. The app guesses
> `.firebasestorage.app` when the setting is blank, so an older project silently
> points at a bucket that doesn't exist. The upload now says so explicitly rather
> than failing quietly.

**3. The Storage rules have to be deployed.** They are not deployed by the
Firestore command — `storage` is a separate target:

```powershell
firebase deploy --only storage --project high-school-ecosystem-dev
```

Without this the bucket keeps its default rules, which deny everything, and
every upload comes back 403.

### What the rules allow

`storage.rules` permits writes only to
`classes/{classId}/materials/{fileName}`, only from that class's own teacher,
capped at **25 MB** and restricted by content type. Both limits are enforced
server-side, because a client check is only a suggestion — anyone can call the
Storage API directly.

Reads are limited to the class's teacher and its enrolled students, so
unenrolling a student revokes their file access with no second list to maintain.

### The two-document budget (why uploads failed even with rules deployed)

Cross-service rules — `firestore.get()` inside Storage rules — allow **only two
Firestore document reads per evaluation**. Go over and the rule doesn't report
an error; it just denies, which is indistinguishable from a permissions problem.

The first version of these rules checked the user's `Role`, their `Roles` array,
*and* the class document through several layers of helper, which could exceed
that budget and deny every upload. The rules now read **one** document — the
class — because it already answers both questions that matter: `TeacherId` says
who may write, `StudentIds` says who may read. Checking the user's role on top
would cost a second read and prove nothing, since nobody lands in either field
without an admin putting them there.

**If you add a helper to `storage.rules`, count the `firestore.get()` calls.**

Admins are deliberately not special-cased, for the same reason. An admin who
needs to attach material can make themselves the class's teacher, which is a
visible act rather than a silent superpower.

### If an upload still fails

The error now shows **what Storage actually said**, not a guess. Work through
these in order:

1. **Are you the class's teacher?** Open the class in Firestore and check that
   `TeacherId` equals your user's UID. A class created by an admin often has
   someone else — or nobody — in that field, and the rule checks it exactly.
2. **Is the bucket name right?** A 404 means the bucket doesn't exist. Firebase
   console → Storage shows the real name; pre-2024 projects end `.appspot.com`.
3. **Did the rules deploy to the right project?** `firebase deploy --only
   storage --project high-school-ecosystem-dev`, and check the console's Rules
   tab shows your version.
4. **Check the crash log.** The full server response is recorded there on every
   refusal, including the specific line the rules rejected.

## Automatic marking

Multiple-choice answers are marked server-side, within a second or two of the
student submitting. The student submits, the page says the score is coming, and
reopening it shows the grade.

**This requires the Blaze (pay-as-you-go) plan.** Cloud Functions cannot deploy
on Spark. Blaze has a free tier that covers a school's volume comfortably, but
it does need a billing account attached to the project.

```powershell
npm install --prefix functions
firebase deploy --only functions --project high-school-ecosystem-dev
```

> Commands in this file are written for **PowerShell**, since that's what the
> project is developed on. PowerShell 5.1 rejects `&&` as a statement
> separator, so nothing here chains commands — run them one line at a time.
> `--prefix` avoids needing to `cd` at all.

### How it works

`functions/index.js` defines `markSubmission`, triggered on any write to
`classes/{classId}/assignments/{assignmentId}/submissions/{studentId}`. It runs
with Admin SDK credentials, which bypass the security rules — so it is the only
thing in the system that can see both the answer key and the submission.

It writes back `Grade`, `Feedback`, `AutoScore`, `AutoScoreMax`, and
`AutoMarkedAt`.

A few behaviours worth knowing, because each one was a decision rather than an
accident:

- **It won't loop.** Writing the grade re-triggers the function. It compares
  `AutoMarkedAt` against `SubmittedAt` and stops unless the submission is newer.
  A teacher editing `Grade` or `Feedback` by hand also lands here and is
  correctly ignored — the function will not overwrite them.
- **Written-only assignments are skipped**, not marked 0. Nothing to mark
  automatically means it's left for the teacher.
- **A question with a missing or out-of-range answer key is excluded** from the
  total, not counted wrong. A student shouldn't lose marks for a half-saved
  question. If *every* question is unmarkable, the whole submission is left
  alone so it can be re-marked once the teacher fixes the keys.
- **`AutoScoreMax` is the multiple-choice total**, which is lower than the
  assignment total whenever there's also a written answer. The student's screen
  says "Auto-marked: 7 / 10" and the feedback tells them the written part still
  needs the teacher.
- **Comparison is by option text**, resolved through the question's current
  `Options` array. The key stores an index; the submission stores text. That
  survives a teacher reordering the options.

### The separator

Answers are encoded as `questionId<US>optionText`, where `<US>` is the ASCII
unit separator (U+001F) — chosen because it can't appear in text a teacher
typed, unlike a colon. Both `Models/Submission.cs` and `functions/index.js`
have to agree on it, and a mismatch would silently mark every answer wrong with
nothing in the logs to explain why.

```bash
node functions/verify-encoding.mjs
```

Run that after touching either side. It's fast and it's caught this already —
the character is invisible, so it doesn't survive careless copy-paste.

### Watching it run

```bash
firebase functions:log --only markSubmission
```

Or run it against the emulators without deploying:

```powershell
npm run serve --prefix functions
```

## AI textbook import

A teacher uploads a textbook PDF from a class's **Course material** page
("Create from a textbook (AI)"). Gemini, running on **Vertex AI**, reads it and
drafts one unit per chapter: study notes plus an 8-question practice quiz. The
teacher reviews and edits the drafts, then publishes each chapter. **Students
see nothing until a chapter is published.** Publishing also attaches the
original PDF to the class, once.

Code: `functions/textbooks.js` (the AI), `Services/TextbookService.cs`
(upload, drafts, publish), `TeacherTextbooksPage` and `TeacherTextbookReviewPage`.

### One-time setup

It runs in Cloud Functions, so it needs the **Blaze plan** like auto-marking.
There is **no API key**: the function signs in to Vertex AI as the project's own
service account, and usage is billed to the same Firebase project.

1. Enable the Vertex AI API for the project:
   Google Cloud console → APIs & Services → enable **Vertex AI API**
   (`aiplatform.googleapis.com`), or:

   ```powershell
   gcloud services enable aiplatform.googleapis.com --project high-school-ecosystem-dev
   ```

2. Install the Gemini library and deploy the functions **and** both rules files
   (the textbook rules live in both):

   ```powershell
   npm install --prefix functions
   firebase deploy --only functions --project high-school-ecosystem-dev
   firebase deploy --only firestore:rules --project high-school-ecosystem-dev
   firebase deploy --only storage --project high-school-ecosystem-dev
   ```

   The first deploy asks for `GEMINI_MODEL` and `GEMINI_LOCATION`. Press Enter
   to accept the defaults (`gemini-3.8-flash`, `global`). They're saved to
   `functions/.env.<project>`, so changing model later — say to a Pro model for
   a diagram-heavy book — is an edit there and a redeploy, not a code change.

### Limits and cost

- PDFs up to **50 MB** (enforced in `storage.rules` and again in the function).
  Gemini reads up to 1,000 pages.
- Up to 30 chapters per book. Front and back matter is skipped.
- Each chapter is its own function run, so long books don't hit the 9-minute
  limit. Nothing retries automatically — a failed chapter shows **Retry**.
- Rough cost on Gemini 3.8 Flash: well under US$1 per textbook. Each chapter
  run re-reads the whole PDF (Gemini counts 258 tokens per page), so a 300-page,
  12-chapter book is about 1M input tokens. Check current prices on
  Google's Vertex AI pricing page before rolling out.

### If it fails

The teacher sees a plain-language reason on the job; the detail is in
`firebase functions:log`.

- **"The AI service isn't switched on"** — the Vertex AI API isn't enabled, or
  the functions service account lacks the **Vertex AI User** role.
- **"The configured Gemini model isn't available"** — `GEMINI_MODEL` names a
  model Vertex AI doesn't offer in `GEMINI_LOCATION`.
- **Upload refused** — `storage.rules` wasn't deployed, the file is over 50 MB,
  or it isn't a PDF.
- **Job stays "Queued"** — the functions weren't deployed after
  `npm install --prefix functions`.

## Class group chat

Every class has a group chat for its students and its teacher (class page →
**Class chat**). There is no private messaging. The teacher can remove any
message; students can remove their own. Messages can carry a picture or a file
(PDF, Word, Excel, PowerPoint, text; up to 15 MB). It needs both rules files
deployed:

```powershell
firebase deploy --only firestore:rules --project high-school-ecosystem-dev
firebase deploy --only storage --project high-school-ecosystem-dev
```

New messages appear within about 5 seconds while the chat is open. With the
chat closed, members get a push notification (see below), and each class card
shows a circle with the number of unread messages. "Unread" is tracked per
device, so reading on a phone doesn't clear the circle on a laptop.

## Teacher-parent messages

Teachers and parents can message each other privately about a child, from the
**Messages** section on their dashboards. A parent can message the teachers of
their linked children's classes; a teacher can message the parents of the
students in their classes. The database rules check that relationship, so
nobody can open a thread with a family or teacher they aren't connected to.

**Link parents to children first**, in Manage users. Either open a parent and
tick their children under *Linked students*, or open a student and tick their
parents under *Parents*. Once a child is enrolled in a class, their parents can
message that class's teacher.

Messages are kept as a record: neither side can edit or delete them, and school
administrators can read every conversation (both sides are told this). Deploy
the rules and functions for it to work:

```powershell
firebase deploy --only firestore:rules --project high-school-ecosystem-dev
firebase deploy --only functions --project high-school-ecosystem-dev
```

## Push notifications

Android and iOS get push notifications for: school announcements, class
notices, new homework/tests/exams, work being marked, new Newspaper articles,
sports posts, team notices, class chat messages, and a 16:00 reminder of
anything due or being assessed tomorrow. Nobody is notified about something
they did themselves. Windows and Mac don't get push.

The sending is all in `functions/notifications.js`. The app only registers each
device under `/users/{uid}/devices` after sign-in and removes it on sign-out.

### One-time setup

**1. Android** — in the Firebase console, Project settings → *Your apps* → add an
Android app with package name `com.dayly.highschoolecosystem`. Download
`google-services.json` and save it as:

    HighSchoolEcosystem\Platforms\Android\google-services.json

**2. iOS** — needs a paid Apple Developer account and a Mac to build:

- In Apple Developer → Identifiers, enable **Push Notifications** on the App ID
  `com.dayly.highschoolecosystem`, and regenerate its provisioning profile.
- In Apple Developer → Keys, create an **APNs key** (.p8). In the Firebase
  console → Project settings → Cloud Messaging → *Apple app configuration*,
  upload it with its Key ID and your Team ID.
- In the Firebase console, add an iOS app with bundle ID
  `com.dayly.highschoolecosystem`, download `GoogleService-Info.plist` and save it as:

      HighSchoolEcosystem\Platforms\iOS\GoogleService-Info.plist

- `Platforms/iOS/Entitlements.plist` has `aps-environment` set to
  `development`. App Store builds need `production`.
- Push does not work in the iOS Simulator for every setup; test on a real iPhone.

Both files are optional at build time: without them the app still builds and
runs, push just stays off (the csproj only defines `PUSH_CONFIGURED` when the
file is there). They hold no secrets, but they are project-specific — each
school's build needs its own.

**3. Server** — deploy the functions (the daily reminder also needs the Cloud
Scheduler API, which the first deploy offers to enable) and the rules:

```powershell
firebase deploy --only functions --project high-school-ecosystem-dev
firebase deploy --only firestore:rules --project high-school-ecosystem-dev
```

### Testing

Sign in on a phone and allow notifications when asked. Check
`users/{your uid}/devices` in the Firestore console has a document with a
`Token`. Then post an announcement from another account. Notifications sent to
yourself are skipped on purpose — always test with two accounts.

If nothing arrives: `firebase functions:log` shows each send and how many
tokens failed. A "registration-token-not-registered" error means the device
entry was stale and has been deleted; sign out and in again on the phone.

## Visitors and guests

`Visitor` is a real role for people connected to the school but outside it —
prospective families, alumni, the wider community. Visitors see only
announcements marked `Audience: "public"` plus sports fixtures and news; they
**cannot** see the timetable, classes, grades or school-only announcements.

The sign-in screen also offers **Continue as a visitor**, which browses that
same public content without an account at all. Both paths are enforced by the
Firestore rules, not by the UI.

## Security model

The rules enforce a real privilege boundary, so a few things are deliberately
impossible from the client:

- A user can create **only their own** `users/{uid}` document, and only if it
  matches their roster entry exactly — or, with no roster entry, is a bare
  `Role: "Student"` account with empty `ClassIds`/`LinkedStudentIds`.
- A roster entry for `Teacher`, `Parent` or `Admin` is honoured **only if the
  user's email is verified**, so claiming a rostered address requires access to
  that mailbox.
- A user can edit their own profile but **cannot** change their own `Role`,
  `ClassIds`, or `LinkedStudentIds` — admin only. This is what prevents
  self-promotion to Admin or attaching yourself to another student as a "parent".
- Students can write their own submissions but **cannot** set or alter `Grade`
  or `Feedback` — teacher/admin only.
- Teachers can only touch classes where they are the `TeacherId`, and can only
  schedule timetable periods for those classes.
- **A self-created profile's `Roles` must match the roster exactly**, and any
  privileged role in it — primary or additional — requires a verified email.
  Without that check a user could self-assign `Roles: ["Admin"]` on top of a
  Student primary role and walk through every admin check.
- **Newspaper articles go live as soon as a journalist publishes** — there is
  no approval step. Journalists can only edit or delete their **own** articles;
  admins can edit or delete any; **teachers can delete (take down) any article
  but cannot edit it**, so nobody rewrites a student's words under their byline.
  The Newspaper is readable by the school community and journalists, not by
  visitors or guests.
- **Staff (Teacher, Coach, Admin) can read the user directory** — a teacher
  needs student names to build a roster. Writing user documents stays
  admin-only, so this grants visibility, not control.
- **Class documents are readable by the whole school community**, not just their
  own teacher and students. This is a deliberate trade-off: a teacher has to be
  able to list `/classes` to find their own, and a per-document rule fails the
  entire query as soon as one document is denied. Names, subjects and rosters
  are directory-level information; grades and submissions stay locked down.

If you change the C# models, re-check `firestore.rules` — a field-name mismatch
fails open-ended reads silently rather than erroring.

## Before you call it production

Still outstanding — see PLAN.md §8:

- **Auto-marking needs the Blaze plan.** Without it, `firebase deploy --only
  functions` fails and multiple-choice work simply stays unmarked — the app
  still works, students just wait for a teacher. See "Automatic marking".
- **Written answers are still marked by hand,** and there's no teacher-facing
  screen for it yet. The function grades the multiple-choice part and says so
  in the feedback.
- **Storage rules are deny-all.** Fine today (nothing uploads yet), but
  assignment attachments in M4 will need real rules in `storage.rules`.
- **Admin console covers users, roster and settings** but not classes,
  announcements or the timetable — those arrive with their features (M2–M5).
- **Email verification is enforced only for privileged roles.** Students (and
  unrostered signups) can use the app unverified. Tighten by adding
  `emailVerified()` to `createAsDefaultStudent()` in `firestore.rules` if you
  want that closed too.
- **Enrolment is open by design.** Anyone can register; unrostered people
  become Students. To close it, change `createAsDefaultStudent()` in
  `firestore.rules` to `return false;` — then only rostered people can create a
  profile. Note the app currently shows them a generic "couldn't load your
  profile" message in that case, so it needs a friendlier error first.
- **`high-school-ecosystem-dev` is a dev project.** Consider a separate
  `-prod` project so test data never mixes with real student records — student
  data carries real legal obligations (FERPA/GDPR/POPIA depending on your
  jurisdiction). Worth checking what applies to you before onboarding a school.

## Troubleshooting

| Symptom | Cause / fix |
|---|---|
| Sign-in says `Firebase:WebApiKey is not set` | Step 3 above |
| `OPERATION_NOT_ALLOWED` | Email/Password not enabled (step 1) |
| `PERMISSION_DENIED` on Firestore | Rules not deployed (step 4), or a field-casing mismatch — see "Permission denied" below |
| `Firebase:ProjectId is not set` at launch | `appsettings.json` isn't embedded — confirm the csproj has `<EmbeddedResource Include="Configuration\appsettings.json" />` and rebuild |
| Dashboard shows no name/role | The `users/{uid}` doc wasn't created — check rules deployed and look for `PERMISSION_DENIED` in the debug output |
| Staff member stuck on "Verify your email" | Expected until they click the link **and** sign out/in again — the role comes from the ID token, which refreshes on sign-in |
| Rostered user came in as Student anyway | The invite doc ID must be their email **lower-cased**, and must exist *before* they register |
| "Your roster entry is invalid" | The `Role` on the invite must be exactly `Student`, `Teacher`, `Parent`, `Admin` or `Visitor` — case-sensitive |
| Real teacher can't register (`EMAIL_EXISTS`) | Someone already registered that address. They hold no role, but you'll need to delete the account under Authentication → Users |
| Windows build won't launch | Enable Developer Mode |

## "Permission denied" when a teacher saves questions or answers

**Redeploy the rules first.** Every new collection needs a rule, and a
collection with no matching rule is denied by default. `questions`,
`answerKeys`, `classes` and `timetable` were all added after the first
deployment, so a project still running older rules will reject them:

```bash
firebase deploy --only firestore:rules --project high-school-ecosystem-dev
```

Watch the output. If it reports a compile error the deploy **does not happen**
and your project silently keeps the previous rules — which looks identical to
the rules being wrong. Confirm the new version is live in the Firebase console
under Firestore → Rules, where the published timestamp should be from just now.

If it still fails after a successful deploy, work through these in order:

1. **Is the account a Teacher or Admin?** The error message in the app now
   tells you which role you're signed in as. A `Student` or `Visitor` is
   correctly denied.
2. **Do you own the class?** `isClassTeacher` compares the class's `TeacherId`
   to your UID. A class created by an *admin*, or created by hand in the
   console, can have a `TeacherId` that is empty or someone else's — and then
   no teacher can write to it. Check `classes/{id}` → `TeacherId` in the
   console and fix it to the intended teacher's UID.
3. **Did the role change take effect?** Roles are read from the profile at load
   time, so sign out and back in after any role change.

## Switching back to emulators

Set `"UseEmulator": true` in `appsettings.json` and run `firebase emulators:start`
(auth 9099, Firestore 8081, UI at localhost:4000). A JDK is required.
