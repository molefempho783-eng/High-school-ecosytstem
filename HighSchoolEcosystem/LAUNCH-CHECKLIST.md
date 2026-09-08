# Launch checklist

The bar for this release: **a teacher or student can use it correctly the first
time, with nobody showing them how.** Everything below is measured against that,
plus the usual production concerns.

Status as of the last working session. Items are ordered by what actually blocks
a launch, not by how interesting they are.

---

## Part 1 — What's done

### Platform and data

- [x] Firebase REST on every platform, so the app runs on Windows as well as
      Android and iOS (native SDKs are mobile-only and crashed on Windows)
- [x] `firestore.rules` as the real authorisation boundary, with the client
      treated as a convenience layer
- [x] `storage.rules` written and scoped to `classes/{id}/materials`, within the
      two-Firestore-read budget cross-service rules allow
- [x] Multi-role model — a primary role plus an additive list, so someone can be
      Teacher and Admin, or Student and Journalist
- [x] Roster / invite system so a role can be pre-assigned by email before
      someone signs up
- [x] Email verification gate on privileged roles
- [x] Auto-marking Cloud Function for multiple-choice work, with a loop guard
      and safe handling of missing answer keys

### Features

- [x] Classes, enrolment by search, weekly timetable
- [x] Assignments and homework, with multiple-choice questions and a separate
      teacher-only answer key
- [x] Student submission with radio-button answers, resubmission, and grades
- [x] Course content: learning units → themes → materials, where a material is
      an in-app chapter, an external link, or an uploaded file
- [x] Per-student progress tracking driving the "1/4 Topics Completed" bars
- [x] School announcements (admin) and class notices (teacher), merged into one
      reader feed with expiry dates
- [x] Sport: fixtures, results and news, with a coach's "needs a result" list
- [x] Five role dashboards — student, teacher, admin, parent, coach — all on
      shared chrome so they can't drift apart

### Design

- [x] ARC-derived design system in three layered files, with a six-value brand
      block so recolouring a client school is a one-file edit
- [x] Tile grids as the default presentation, with four deliberate exceptions
- [x] Shared navigation rail and header used by every role

---

## Part 2 — Blocking. Do not launch without these.

### 2.1 It has never been compiled

**This is the single biggest risk.** Everything was written without a
.NET/MAUI toolchain. Structure, bindings, resource keys and rule logic have been
checked by script, but no compiler or device has ever seen it.

- [ ] `dotnet build` clean on every target
- [ ] Runs on a real Android device, not just Windows
- [ ] Every screen opened at least once by hand
- [ ] Fix whatever that surfaces before anything else on this list

### 2.2 Destructive actions have no confirmation

Nine delete commands fire immediately. A teacher taps "Delete unit" and a term's
structure is gone with no warning and no undo. For an untrained user this is the
most likely way to lose real work.

- [ ] Confirmation dialog on every delete: unit, material, announcement, class
      notice, assignment, question, sports post, invite, class
- [ ] Say what will be lost, in counts — "This unit has 6 items. They'll move to
      Module information." — rather than a generic "Are you sure?"
- [ ] Consider soft-delete with a 30-day window for units and classes, which is
      the only real protection against a confident mis-tap

### 2.3 Teachers can't mark written answers in the app — DONE

- [x] Teacher marking screen: every student's submission for one assignment,
      with the auto-marked score shown, the written answer, and any attached file
- [x] "4 of 26 still need marking", and unmarked work sorted to the top
- [x] Per-student save, so a dropped connection doesn't lose a class's marks
- [x] `TeacherMarkedAt` distinguishes a human grade from the auto-marker's, so
      the loop guard can't overwrite one

Still worth doing when convenient:

- [ ] Bulk actions — "give everyone who submitted full marks for effort"
- [ ] Keyboard-friendly tabbing between students on desktop

### 2.4 Registration is open

Anyone who installs the app can create a Student account against the project.

- [ ] Close it: change `createAsDefaultStudent()` in `firestore.rules` to
      `return false;` so only rostered people can create a profile
- [ ] Give the app a friendly "your school hasn't added you yet" screen for that
      case — currently it shows a generic profile-load failure

### 2.5 No crash reporting

`CrashLogger` writes to the device only. Once the app is on someone else's
phone, nothing reaches you.

- [ ] Wire up Sentry, Crashlytics or App Center
- [ ] Verify a deliberate crash actually arrives

### 2.6 App Check is not configured

Without it, anyone with the public Web API key can call your Firebase project
directly, bypassing the app entirely. The rules still hold, but the abuse
surface is wide open.

- [ ] Enable App Check with Play Integrity (Android) and DeviceCheck (iOS)
- [ ] Enforce on Firestore, Storage and Functions

### 2.7 Separate production project

- [ ] New `-prod` Firebase project, so test data never mixes with real student
      records
- [ ] `appsettings.json` per configuration
- [ ] Seed accounts and dummy classes exist **only** in dev

### 2.8 Legal basis for holding minors' data

Not optional and not a technicality. South Africa's POPIA applies; GDPR or FERPA
may too depending on the school.

- [ ] Confirm which regime applies
- [ ] Parental consent mechanism where required
- [ ] Privacy policy and retention period agreed with the school
- [ ] A documented way to export and delete a pupil's data on request

---

## Part 3 — Usable without training

This is the stated bar, so it gets its own section. Each item is a place where a
first-time user currently has to guess.

### 3.1 First run teaches nothing

- [ ] A short first-run walkthrough per role — three screens, skippable, shown
      once. Teachers especially need to be told that content goes
      **class → unit → material**, because nothing on screen explains it
- [ ] Empty states that instruct rather than just report. "No classes yet" should
      offer the button that creates one; several already do, not all
- [ ] A "?" on each dashboard opening a one-page explanation of that screen

### 3.2 The teacher's mental model isn't obvious

Units and themes are our vocabulary, not a teacher's.

- [ ] Rename in the UI to what teachers say: "Topic" or "Section" rather than
      "Learning unit", unless the school uses ARC's wording
- [ ] Show a live preview of what students will see while building a unit
- [ ] Offer a starter structure on an empty course — "Create 5 topics" — so the
      first screen isn't a blank page with a button

### 3.3 No undo anywhere

- [ ] Undo toast on delete, at minimum for materials and notices
- [ ] Draft state for announcements, so a half-written notice isn't lost by
      tapping away

### 3.4 Errors say what went wrong, not what to do

Mostly good now, but audit the rest.

- [ ] Every error message names the next action
- [ ] No raw status codes or exception text reaches a user

### 3.5 Accessibility

Untested. A school will have pupils who need this.

- [ ] `SemanticProperties` on every interactive element
- [ ] Screen reader pass on the main flows
- [ ] Respects OS font scaling without clipping — the fixed tile heights are the
      likely failure point
- [ ] Colour contrast checked; the amber meta text on white is the one to verify
- [ ] Colour is never the only signal — grade bands and work urgency both need
      their text to carry the same meaning

### 3.6 Real-user testing

Nothing replaces this.

- [ ] Watch three teachers set up a course, unaided, without asking questions
- [ ] Watch three students find and submit homework
- [ ] Fix everything that made someone hesitate for more than a few seconds

---

## Part 4 — Important, not blocking

### Reliability

- [ ] Offline read cache — schools have poor connectivity and the app currently
      shows an error rather than last-known data
- [ ] Retry with backoff on transient network failures
- [ ] Pull-to-refresh on every list (some pages have it, not all)

### Features teachers will ask for within a week

- [ ] Push notifications for new work, marks and notices
- [ ] Bulk enrolment — CSV import of a class list
- [ ] Duplicate a class or unit into the next term
- [x] Students attach a file to a submission, when the teacher allows it
- [ ] Teachers attach a file to the assignment brief itself, not just to course
      material
- [ ] Real gradebook: weighted categories and term marks (M5). The current
      average is an unweighted mean and is labelled as such

### Operations

- [ ] Firestore backup schedule
- [ ] Budget alerts on the Blaze plan
- [ ] Monitoring on the auto-marking function
- [ ] A documented rollback: how to revert rules and app version quickly

---

## Part 5 — Pre-flight, day of launch

- [ ] Rules deployed to prod: `firebase deploy --only firestore:rules,storage --project <prod>`
- [ ] Functions deployed and a test submission auto-marks correctly
- [ ] `Firebase:StorageBucket` matches the prod bucket exactly
- [ ] One admin account created and verified
- [ ] Roster loaded for the real staff and pupils
- [ ] Seed and dummy data confirmed absent from prod
- [ ] Signed release build tested on a real device from the store listing
- [ ] Someone on call for the first week

---

## Honest assessment

The **feature surface is broad and the design is consistent**. What's missing is
mostly the difference between a working prototype and a product people depend
on: it has never been compiled, teachers can't mark written work without the
Firestore console, and a mis-tap deletes a term's content with no warning.

Sections 2.1, 2.2 and 2.3 are the ones that would embarrass you in front of a
real school. Everything else in Part 2 is standard production hygiene, and
Part 3 is what separates "works" from "works without being taught".
