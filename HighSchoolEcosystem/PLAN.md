# HighSchoolEcosystem — Build Plan

## 1. What this is

A single-school LMS. **One build = one school, one Firebase project, one
database.** There is no runtime tenant switching: to onboard a new client
school, you edit branding in `Configuration/appsettings.json`, swap in that
school's `google-services.json` / `GoogleService-Info.plist`, point
`Firebase:ProjectId` at their Firebase project, deploy `firestore.rules` to
that project, and ship a new build. Built as a .NET MAUI app (Android / iOS /
Windows / MacCatalyst) backed by Firebase (Auth, Firestore, Storage), with a
currently-empty ASP.NET Core API project reserved for anything Firestore
security rules can't handle on their own.

**Roles:** Student, Teacher, Parent/Guardian, School Admin, Coach, Journalist,
Visitor. Roles are **additive** — a primary role decides the landing dashboard,
and extra roles are held alongside it (Admin+Teacher, Student+Journalist).
Assigned at
sign-up from the admin-managed roster at `/invites/{email}` (§5); unrostered
sign-ups default to Student. People with no account at all browse as guests and
see the same public content as a Visitor.

**MVP feature set (in priority order):**
1. Announcements & messaging
2. Timetable + sports match schedule/results/news
3. Courses & assignments
4. Grades & gradebook

## 2. What already exists

- MAUI app shell (`HighSchoolEcosystem/`) targeting Android, iOS, Windows, MacCatalyst.
- Auth: `FirebaseAuthService` wraps `Plugin.Firebase.Auth` (sign in, register,
  password reset, email verification). **Only implemented for Android/iOS** —
  Windows and MacCatalyst throw `PlatformNotSupportedException`. This is a real
  gap we need to solve or consciously scope out (see §6).
- `FirestoreClient` — a thin Firestore REST wrapper (get/set documents), used
  instead of a native SDK so the same code works on all 4 MAUI targets.
- `UserProfileService` reads/bootstraps a signed-in user's role from Firestore;
  `DashboardPage` routes Student/Teacher/Parent/Admin to distinct placeholder
  home views.
- `ISchoolInfoProvider` reads this build's branding (name, colors, logo) from
  the `School` section of `Configuration/appsettings.json`.
- `firestore.rules` enforces role-based access (see §5).
- `HighSchoolEcosystem.Api` project exists but is an empty ASP.NET Core scaffold.

## 3. Architecture decisions

- **Firestore-first.** The MAUI app talks to Firestore directly for reads/writes
  (via REST, so it works uniformly across all four MAUI targets, including
  Windows/MacCatalyst where the native Firebase SDKs aren't wired up). Security
  is enforced by Firestore security rules based on the caller's auth token and
  role.
- **The `.Api` project stays minimal**, used only for things Firestore rules
  genuinely can't do server-side: e.g. sending transactional email, generating
  invite codes, admin bulk-import, scheduled digest jobs, or aggregations. We
  do not build a parallel REST CRUD backend.
- **Cloud Functions handle anything that needs to see data the caller can't.**
  So far that is exactly one thing: auto-marking. The answer key is deliberately
  unreadable by students, which is what stops them looking up answers before
  submitting — and that same rule means the marking cannot happen on their
  device. `functions/index.js` runs with Admin SDK credentials and is the only
  code that sees both the key and the submission. The cost is that Cloud
  Functions require the **Blaze plan**; without it the app still works and
  multiple-choice work simply waits for a teacher.
- **Single school per build.** Collections live flat at the database root
  (`/users/{uid}`, `/classes/{classId}`, ...) — no `schoolId` prefix, because
  each deployment already has its own dedicated Firebase project. Onboarding a
  new client is an ops task (new Firebase project + new build), not a runtime
  feature.

## 4. Data model (Firestore)

```
/meta/config                                     school-wide settings (optional Firestore mirror of appsettings.json branding)

/invites/{emailLowercased}                       admin-managed roster; decides a user's role at sign-up
    Role: "Student" | "Teacher" | "Parent" | "Admin" | "Visitor"
    ClassIds: []                (optional pre-enrolment)
    LinkedStudentIds: []        (required for Parent)

/users/{uid}                                     profile + roles
    Role:  primary role, decides the landing dashboard
    Roles: [] additional roles held alongside it
    DisplayName, Email
    ClassIds: []                (students & teachers)
    LinkedStudentIds: []        (parents only)

/classes/{classId}
    Name, Subject, TeacherId, StudentIds[], Period/Room

/classes/{classId}/assignments/{assignmentId}
    Title, Description, DueDate, Points, Attachments[]

/classes/{classId}/assignments/{assignmentId}/submissions/{studentId}
    SubmittedAt, Attachments[], Grade, Feedback

/announcements/{announcementId}
    Title, Body, Audience: "public" | "school" | classId, PublishedAt, AuthorId

/timetable/{entryId}
    ClassId, DayOfWeek, Period, StartTime, EndTime, Room

/sportsEvents/{eventId}
    Team, Opponent, Date, Location, HomeAway, Result

/sportsNews/{postId}
    Title, Body, PublishedAt, AuthorId
```

**Naming convention:** field names are PascalCase, matching the C# model's
property names exactly. `FirestoreClient`/`FirestoreValueConverter` write
Firestore field names as `property.Name` verbatim — no camelCase conversion —
so every new model class and every `firestore.rules` field reference must use
the same PascalCase name or reads will silently miss the field.

Rationale: a flat schema keeps every rule and query simple since there's
exactly one school's data in the database.

## 5. Security model (Firestore rules)

- **Guest** (unauthenticated): read `/meta/config`, announcements marked
  `Audience: "public"`, and the sports schedule/news.
- **Visitor** (signed in, `Role: "Visitor"`): the same public surface as a
  guest. Deliberately *not* counted as a school member by the rules — no
  timetable, classes, grades or school-only announcements. See
  `isSchoolCommunity()` in `firestore.rules`.
- **Student**: read their own user doc, classes they're enrolled in,
  assignments/timetable for those classes, their own submissions/grades;
  write their own submissions.
- **Teacher**: read/write classes they teach, assignments, grades for their
  classes.
- **Parent**: read-only access to data for `LinkedStudentIds`.
- **Admin**: full read/write.

**Admin console (done).** Admins land on their own dashboard and can change
any registered user's role, link parents to students, manage the roster and edit
school settings in-app. Every write is still gated by the rules below, so the UI
is a convenience rather than the security boundary.

**Role assignment at sign-up.** Roles come from the admin-managed roster at
`/invites/{email}`. On first sign-in `UserProfileService` reads the invite for
the user's email and creates their profile with that role; no invite means
`Student`. Admins maintain the roster; `Visitor` is available there for people
outside the school community.

**Privilege boundary (important).** The client bootstraps its own
`users/{uid}` document on first sign-in, so `create` must be self-service —
which means the rules have to stop a user handing themselves a privileged role.
Enforced in `firestore.rules`:

- self-`create` must either match this user's roster entry exactly (role,
  classes, linked students) or, when they aren't rostered, be a bare `Student`
  with no classes or linked students;
- a rostered **Teacher/Parent/Admin** role is honoured only once the user's
  email is verified. Firebase lets anyone register any unclaimed address
  *without* proving ownership, so without this a stranger who guessed a
  rostered address (`principal@school.org`) could register it first and inherit
  the role. Students are exempt — the role grants no access to anyone else's
  data, so the friction isn't worth it for the largest group;
- self-`update` may not change `Role`, `ClassIds` or `LinkedStudentIds`
  (admin-only), which also blocks attaching yourself to another student as a
  "parent";
- students may not write `Grade`/`Feedback` on their own submissions;
- `/invites` is admin-write; a user may read only their own entry.

Note the rules read the roster via `request.auth.token.email.lower()`, so
invite document IDs must be lower-cased.

Any new client-writable collection needs the same scrutiny: assume the client
is hostile and ask what a user could grant themselves.

## 6. Auth: REST on every platform (resolved)

Originally `FirebaseAuthService` wrapped `Plugin.Firebase.Auth`, which only
supports Android and iOS — so on the Windows target every auth call threw
`PlatformNotSupportedException` and you couldn't create an account at all.

Resolved by dropping the native plugin entirely and talking to the **Firebase
Identity Toolkit REST API** on all four targets, matching how `FirestoreClient`
already works. Consequences:

- `Plugin.Firebase.Auth` is gone from the csproj, along with the
  `GoogleServicesJson` / `BundleResource` items and the
  `google-services.json` / `GoogleService-Info.plist` files — no native
  Firebase SDK is linked, so they're unused. (Add them back if FCM push
  notifications land in M6.) This also permanently kills the
  `google-services.json` deserialization build error.
- `Firebase:WebApiKey` in `appsettings.json` is now **required** against real
  Firebase (the emulator ignores it). `FirebaseEndpoints` throws a clear
  message at startup if it's missing.
- Sessions persist via the refresh token in `SecureStorage`
  (`SecureAuthTokenStore`); ID tokens stay in memory and auto-refresh ~1 minute
  before expiry.
- Cold start now goes through `StartupPage`, which restores the session
  asynchronously before routing to the dashboard or the sign-in screen.
- Firebase error codes map to readable messages via `FirebaseAuthException`,
  so failures like `EMAIL_EXISTS` show something useful instead of a generic
  "we couldn't sign you in".

Trade-off accepted: no native Google/Apple social sign-in, and token refresh is
ours to manage rather than the SDK's. Email/password is all the MVP needs.

## 7. Roadmap

- **M1 (done):** Data model + Firestore security rules + a generic Firestore
  REST client + role-aware profile fetch after login + a role-based dashboard
  shell (routes Student/Teacher/Parent/Admin to distinct home views).
- **M1.5 (done):** Dropped the multi-tenant/school-selection layer in favor of
  one school per build; fixed broken Firebase config placeholder files and a
  NuGet version conflict.
- **M1.6 (done):** Replaced native Firebase Auth with the REST API so sign-in
  and registration work on Windows/MacCatalyst as well as Android/iOS (see §6);
  added session persistence, cold-start restore, and real auth error messages.
- **M1.7 (done):** Admin console — separate admin dashboard, role assignment for
  registered users, parent↔student linking, roster management, school settings.

The remaining milestones are reordered to match the research report's priority
lists (see RESEARCH-ALIGNMENT.md). Three features below — To-Do/Calendar, the
School Newspaper, and the Meeting Scheduler — are named in the research but were
missing from this plan entirely until that review.

- **M2 (largely done): Courses & assignments.** Teacher creates classes,
  enrols students by search, sets a timetable, and posts homework/assignments
  with multiple-choice questions and an answer key. Students see all of it on
  their dashboard and submit against it, and multiple-choice answers are marked
  automatically by a Cloud Function. Outstanding: a teacher-facing screen for
  reviewing and marking written submissions, and parent visibility into their
  children's work (the rules gap is noted in §8).
- **M3: Announcements + School Newspaper.** Shared content model; rich-text
  articles drafted by admins, audience targeting, visitor-facing. Cheapest of
  the research's differentiators and it demos well.
- **M4: Campus Life hub.** Timetable + the research's `ExtracurricularEvent`
  (EventType, TeamRosters, Venue, MatchTimeline), sports fixtures/results/news,
  and a unified To-Do/Calendar month view. Includes **venue double-booking and
  student availability conflict detection**, which the research calls for.
- **M5: Gradebook.** Teacher entry, weighted averages, student/parent read
  views. Needs M2's assignments to exist.
- **M6: Meeting scheduler.** Teachers publish slots; parents/students request;
  confirmation writes to the device calendar with 24h and 1h reminders.
- **M7: Notifications & offline.** FCM push (requires re-adding a native
  Firebase SDK — see RESEARCH-ALIGNMENT.md) plus a local cache and background
  sync. The research treats *both* as competitor weaknesses we'd otherwise be
  repeating, so these are differentiators rather than polish.
- **M8: Learning analytics.** Grade trends and attendance charts
  (research Priority 3).

### Divergences from the research report

The report specifies **SQLite-net/EF Core for local persistence**, **Refit** for
typed REST, and a **custom read-only API layer with rate-limiting** for public
data. This build instead went Firebase-first: Firestore over REST with security
rules as the authorisation layer, no local database and no custom backend.

That removed a whole backend from the critical path and the rules give a
stronger privilege boundary than a hand-written API would have at this stage —
but it is a genuine departure from a document that names those libraries
explicitly, and it's why offline support and push notifications are both absent
today. If the research doubles as an academic deliverable, this needs either
justifying in the planning document or correcting. See RESEARCH-ALIGNMENT.md.

The **Admin role** is also an addition: the research lists only Student,
Teacher, Parent and Guest, but someone has to administer the roster.

## 8. Known limitations (follow up on these)

**See `LAUNCH-CHECKLIST.md` for the full, prioritised list.** This section keeps
only the architectural notes; the checklist is the working document.

- **Never compiled.** Written without a .NET/MAUI toolchain. Structure,
  bindings, resource keys and rules have been checked by script, but no compiler
  or device has seen it. Build before trusting anything else here.
- **No teacher screen for marking written answers.** The Cloud Function scores
  the multiple-choice part; written answers need `Grade` and `Feedback` edited
  in the Firestore console, which no teacher will do. Blocking for launch.
- **Destructive actions have no confirmation.** Nine delete commands fire
  immediately, including "delete unit", which detaches a term's material.
  Blocking for launch.
- **Auto-marking requires the Blaze plan.** On Spark the function never
  deploys; nothing else breaks, but submissions stay unmarked.
- **Registration is open.** Anyone can create a Student account against the
  project. Close `createAsDefaultStudent()` before a real school onboards.
- **Email verification is enforced only for privileged roles.** Students and
  unrostered signups can use the app unverified.
- **The class average is an unweighted mean of percentages**, not a term mark. A
  weighted gradebook is M5. It's labelled as an average, not presented as a mark.
- **Student data carries legal obligations** (POPIA here; FERPA/GDPR may also
  apply). Confirm what applies before handling real records, and use a separate
  `-prod` project.

Resolved since this list was first written: storage rules are no longer
deny-all; parents can read their children's assignments through
`isParentOfClassMember()`; parents and coaches have their own dashboards rather
than falling through to the student one.

## 9. Onboarding a new client school (once you're past the MVP)

1. Create a new Firebase project for the school (Auth + Firestore + Storage),
   and enable Email/Password under Authentication → Sign-in method.
2. In `Configuration/appsettings.json`, set `Firebase:ProjectId` and
   `Firebase:WebApiKey` (Project settings → General → Web API Key) to the new
   project's values, and set `Firebase:UseEmulator` to `false`.
3. Edit the `School` section of the same file: `DisplayName`, `PrimaryColor`,
   `SecondaryColor`, `AccentColor`, `LogoUrl`.
4. Deploy `firestore.rules` (and `firestore.indexes.json`) to the new project
   (`firebase deploy --only firestore:rules,firestore:indexes --project
   <new-project-id>`).
5. Build and ship.

No `google-services.json` / `GoogleService-Info.plist` needed — auth and
Firestore both go over REST, configured entirely from `appsettings.json`.
