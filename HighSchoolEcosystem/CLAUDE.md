# HighSchoolEcosystem — working notes

A .NET MAUI LMS for a single South African high school, backed by Firebase.
One school per build; onboarding a new client means a new Firebase project and
a new build, not a runtime tenant switch.

## Environment (read this first)

**The developer works on Windows in PowerShell 5.1.** This has bitten us twice:

- `&&` is **not** a valid statement separator. Never chain commands with it.
  Write each command on its own line, or use `;` if they're genuinely
  independent.
- `export VAR=value` does not exist. It's `$env:VAR = "value"` — but prefer
  designing scripts to take a `--flag` argument so no env var is needed at all
  (that's why `tools/seed-accounts.mjs` takes `--key <path>`).
- Avoid `cd`-then-command patterns. npm takes `--prefix`, which sidesteps it:
  `npm install --prefix functions`.

Every command in RUNNING.md is written to be PowerShell-safe. Keep it that way.

## Architecture

- **Firebase REST only**, on every platform. Native SDKs (`Plugin.Firebase.*`)
  are Android/iOS-only and threw `PlatformNotSupportedException` on Windows, so
  they were removed. Identity Toolkit for auth, Firestore REST for data.
- **`firestore.rules` is the security boundary.** The client is a convenience
  layer, not a guard. Any new feature needs its rule written and reasoned
  about, not just a ViewModel.
- **Cloud Functions only for work that needs data the caller can't read.**
  Currently one: auto-marking, because students can't read the answer key.
  Requires the Blaze plan.

## Things that will trip you up

- **Firestore fields are PascalCase.** `FirestoreValueConverter` writes
  `property.Name` verbatim. Rules, the C# models, and `functions/index.js` all
  have to agree.
- **`SetDocumentAsync` serialises *every* property.** A student saving a
  `Submission` that way would include `Grade`/`Feedback` and be denied by the
  rules. Student writes use `PatchFieldsAsync` with an explicit field list —
  see `ClassService.SubmitAsync`.
- **The answer separator is U+001F**, an invisible control character shared by
  `Models/Submission.cs` and `functions/index.js`. Write it as an escape
  (`'\u001f'`) or `String.fromCharCode(0x1f)` — pasting it literally strips it,
  which has happened more than once. `node functions/verify-encoding.mjs`
  checks it.
- **`CollectionView` nested in a `ScrollView` collapses on mobile.** Use
  `BindableLayout` inside a `VerticalStackLayout` instead.
- **`Options` is ambiguous** — CommunityToolkit.Maui and
  Microsoft.Extensions.Options both export it. Qualify or avoid.
- **HttpClient timeouts throw `TaskCanceledException`,** not
  `HttpRequestException`. Catch `OperationCanceledException`.
- **`appsettings.json` must be an `EmbeddedResource`**, not
  `CopyToOutputDirectory` — the latter doesn't get packaged into an APK.

## Design system

The UI is modelled on **ARC**, the D2L Brightspace portal The IIE uses (Emeris
branding). Structurally: a grey page canvas where every panel is a white card
with a header, a chevron, sections divided by a rule with a count badge, and
list rows of `icon + link-coloured title + meta line`.

Three files, layered — **don't put a raw hex value in a view**:

- `Resources/Styles/Colors.xaml` — a brand block (six values) and a semantic
  layer derived from it. **Recolouring a client school is an edit to the brand
  block only.** Also mirror it in
  `Platforms/Android/Resources/values/colors.xml`, which Android reads before
  MAUI starts and which no XAML sweep will catch.
- `Resources/Styles/Styles.xaml` — stock MAUI control styles, retargeted. Old
  template keys (`Primary`, `Gray400`) are remapped rather than removed, so
  nothing silently falls back to purple.
- `Resources/Styles/Widgets.xaml` — the ARC vocabulary: `Widget`,
  `WidgetTitle`, `SectionHeading`, `SectionRule`, `CountBadge`, `RowTitle`,
  `RowMeta`, `Chip`, `ClassCard`, `ToolRowTitle`, `BrandButton`. Merged after
  the other two, since it builds on both.

Converters live in `Converters/CommonConverters.cs` and are registered app-wide
in `App.xaml`: `StringNotEmpty`, `InvertedBool`, `HasItems`, `NoItems`. Use
these instead of adding another `Has*` bool to a view model.

Class colours come from `Banner1`–`Banner6`, assigned in `DashboardViewModel`
by ordering class IDs — so adding a class doesn't reshuffle every other
subject's colour, and the card grid, timetable spines and today rail agree.

Announcements and sport are **dashboard sections, not pages**. Only sport keeps
a separate page, because posting a fixture needs more fields than a widget can
hold; reading happens inline. The rail scrolls to sections rather than
navigating, except for that one page.

**The rail floats.** It's overlaid on the content, not given a Grid column, so
expanding it covers the page rather than squeezing it. Pages put it last in a
single-cell Grid — last so it draws on top — and inset their content by 50 on
the left. `DashboardShellViewModel.CollapsedRailWidth` is 44, the platform
minimum touch target.

**Tile width is arithmetic, not taste.** 132 is what lets two fit on a 360dp
phone once the rail inset, page padding and widget padding are subtracted; the
sum is written out in `Widgets.xaml`. It was 168, which needed 344 and so
collapsed every grid to one column on every phone. `Widget` padding (14 a side),
the tile margin (8) and the content inset (50) are all part of that sum — change
one and re-do the arithmetic. `UnitCardRow.TrackWidth` (114) follows the tile
width too.

**Collections are tile grids by default, not row lists.** `FlexLayout` with
`Style="{StaticResource TileGrid}"` and fixed-width `Tile` children that wrap —
two per row on a phone, more on a wide window. The vocabulary is `Tile` /
`TileWide`, `TileAccent` (the coloured strip carrying category or urgency),
`TileBody`, `TileKicker`, `TileTitle`, `TileMeta`, `TileValue` (the large
figure), `TileSubtle`.

Four places stay **lists**, deliberately:

- the contents tree on a unit page — depth and order carry meaning
- the teacher's material rows — they have up/down reordering, which a wrapped
  grid makes ambiguous
- the assignment paper — questions are numbered and sequential, and radio
  options belong in one vertical group
- admin user and enrolment `CollectionView`s — long, searchable and virtualised

Accent colours are computed on the row type, not chosen in XAML:
`StudentWorkRow.AccentColor` (red late, amber within a day, brand otherwise),
`GradeRow.BandColor` (banded by percentage), `AssessmentRow.AccentColor`.

Progress bars compute their fill in **device units, not proportions**
(`UnitCardRow.FillWidth`) — MAUI can't bind a Grid's star-sizing. Keep
`TrackWidth` in step with the card width in `StudentClassPage.xaml`.

The contents tree on the unit page is a **flat list with an indent spacer per
row** (`TocRow.Depth`), not nested layouts — nesting three deep would hit the
`CollectionView`-in-`ScrollView` collapse. A bound `Margin` would be read as a
uniform `Thickness`, so the indent is a `BoxView` width instead.

## Content model

Three collections beyond classes, each with a different write audience — the
distinction is enforced in `firestore.rules`, and mirrored (only as a UI
convenience) by the `Can*` properties on `IContentService`:

| Collection | Read | Write |
|---|---|---|
| `/announcements` | school; visitors see `IsPublic` only | **Admin only** |
| `/classes/{id}/announcements` | enrolled students, their parents, the teacher | that class's teacher, Admin |
| `/classes/{id}/units` | enrolled students | that class's teacher, Admin |
| `/classes/{id}/progress/{uid}` | the student, their parent, the teacher | **only that student** |

A linked parent reads their children's work through `isParentOfClassMember()`,
which asks whether the class's `StudentIds` intersects the parent's
`LinkedStudentIds`. It exists because `isLinkedParent()` needs a studentId and
paths like `/classes/{id}/assignments` don't have one — without it a parent
could read a grade but not the assignment it belonged to. `/users/{uid}` also
allows `isLinkedParent` so the parent dashboard can show a name rather than a
UID.
| `/sports` | public | Coach, Admin, Journalist — journalists only their own |
| `/classes/{id}/materials` | enrolled students | that class's teacher, Admin |

`AuthorId` is pinned to `request.auth.uid` on create and immutable on update in
all of them, so a byline can't be forged or reassigned.

**Two announcement collections, deliberately.** School-wide stays admin-only.
Class notices are a *subcollection* rather than a `ClassId` field on the
school-wide one: the path carries the class, so `isClassTeacher(classId)` gates
the write and enrolment gates the read, with no client-side filtering. Tagging
one collection would have let a list query hand one class's notices to the whole
school. Teachers get a wider write audience but a narrower reach — they can only
ever address a class they teach.

Student and parent dashboards merge both feeds into one list, sorted pinned-first
then newest across sources, with `AnnouncementRow.ScopeLabel` naming the class
so a merged feed isn't ambiguous.

Announcements carry an **optional** `ExpiresAt` (default = never). Readers stop
seeing a notice past its date; **authors keep seeing it**, dimmed and chipped
"Expired", so they can tell why it vanished and extend it — hence the
`includeExpired` flag on both `GetAnnouncementsAsync` and
`GetClassAnnouncementsAsync`. Filtering is client-side on purpose: an expired
notice isn't secret, just stale, and a rule denying them would fail the whole
list query rather than trimming it. The picked date resolves to the **end** of
that day, so "until 12 September" means still up on the 12th.

Course content is three levels deep, matching ARC:

    class → LearningUnit → CourseMaterial (grouped by a free-text Theme)

`/classes/{id}/units/{unitId}` holds the units. A material's `UnitId` says
which one it belongs to; **empty means loose**, and those surface under a
"Module information" card rather than vanishing — which is also where material
created before units existed ends up. Deleting a unit *detaches* its material
instead of deleting it.

Progress is one document per student per class at
`/classes/{id}/progress/{studentId}`, holding completed material IDs. One read
builds every progress bar. The teacher can **read** it but deliberately cannot
**write** it — it's the student's own record; grades are the teacher's channel.

Material `Order` is **per unit, not per class** — `MoveMaterialAsync` reorders
within the material's own unit. Ordering across the whole class would let "up"
from the top of one unit move a document into the unit above it.

Course material has three kinds in one collection: `Chapter` (written and read
in-app, `Body`), `Link` (external URL), `File` (Firebase Storage, `Url` +
`StoragePath`). Deleting a File material deletes the Storage object **first** —
the document holds the only pointer to it.

`storage.rules` is no longer deny-all. Uploads are capped at 25 MB and a
content-type allowlist, both enforced server-side because a client check is
only a suggestion. `Firebase:StorageBucket` gates uploads: unset means links
and chapters still work and the upload option is hidden.

**Storage rules get TWO Firestore reads per evaluation, total.** Cross-service
rules (`firestore.get` inside `storage.rules`) silently *deny* when you exceed
that — it looks exactly like a permissions bug. So the rules read one document,
the class: `TeacherId` says who writes, `StudentIds` says who reads. Don't add
a role check on top; it costs a second read and proves nothing, since an admin
had to put them in those fields anyway. Count your `firestore.get()` calls
before adding a helper there.

Uploads POST to the object **collection** (`/o?name=path`), not to the object's
own URL — `/o/{path}` is the metadata endpoint and posting there silently does
nothing.

`AssignmentKind` gained `Test` and `Exam`. **Append only** — the converter
writes the enum name, so reordering retypes existing documents. Use
`.IsSubmittable()` / `.IsAssessment()` rather than comparing kinds by hand;
tests and exams show as dates on the class page, not as work to hand in.

## Shared chrome — keep every role identical

Five dashboards (student, teacher, admin, parent, coach) all derive from
`DashboardShellViewModel` and render `Controls/NavigationRail.xaml` +
`Controls/DashboardHeader.xaml`. **Don't hand-roll a header or a rail on a new
screen** — that's how the roles drifted apart the first time.

The rail is **data-driven**: the view model builds `RailItems` (which
destinations exist, based on roles actually held), and the page sets
`OnRailAction` to say what each one does. Scrolling and navigation are view
concerns; the view model doesn't know about either. Adding a destination is one
line of C#.

The controls carry no BindableProperties — a `ContentView` inherits its
parent's `BindingContext`, so dropping either into a page whose view model
derives from the shell base just works.

`HomePageResolver` routes every role to its own landing page. Parent and Coach
used to fall through to the student dashboard and be told they weren't enrolled
in any classes.

## Submissions and marking

An assignment carries a `SubmissionMode` the teacher picks — Written, File,
Either or Questions. For the first three, multiple-choice questions are
**independent** of the mode: they're always auto-marked, and the file is the
part a person reads. `Questions` is the exception — it means the questions
*are* the paper, so `AcceptsWritten` and `AcceptsFile` are both false and the
student's "Your answer" card is hidden entirely.

Picking `Questions` and tapping Add navigates straight to the question editor.
It has to: a multiple-choice paper is not finished when it's saved, and a
teacher who doesn't know the next step is hidden behind tapping the row will set
work with no questions on it. The view model raises `QuestionsRequested`; the
page does the pushing.

**`SaveAssignmentAsync` and `GetAssignmentsAsync` rebuild the object field by
field, so every new property has to be added to BOTH.** `SubmissionMode` was
missed in both for a while, which meant the teacher's choice silently never
round-tripped and every assignment read back as Written. If you add a field to
`Assignment`, grep those two projections before believing it works.

**The orange badge is `CountBadge` and it costs a read per assignment.**
`GetNeedsMarkingCountsAsync` counts `NeedsTeacherMarking` per assignment;
`GetMarkingQueueAsync` does it across a teacher's classes for the dashboard, but
only for submittable work whose deadline has passed within 45 days — without
that window a dashboard opens dozens of connections on a school network. Both
callers start the fetch **without awaiting it**, so the badge fills in after the
screen is already usable. Zero hides the badge: an empty orange circle reads as
"none marked" rather than "nothing to mark".

Student submission files live at
`classes/{id}/submissions/{assignmentId}/{studentId}/{file}`, so the path itself
carries "only your own" and the rule needs no extra lookup. Upload happens
**before** the document write — a submission pointing at a file that isn't there
is worse than no submission.

**The rules protect more than Grade and Feedback.** A student write must not
touch `TeacherMarkedAt` (would hide the work from the marking queue),
`AutoMarkedAt` (the function's loop guard skips anything where
`AutoMarkedAt >= SubmittedAt`, so a future value blocks auto-marking forever), or
`AutoScore`/`AutoScoreMax`. The Cloud Function bypasses rules, so this costs it
nothing.

`Submission.NeedsTeacherMarking` checks `TeacherMarkedAt`, **not** `IsMarked` —
the auto-marker sets `Grade`, so `IsMarked` goes true while a written answer sits
unread. That distinction is the whole point of the marking queue.

Work already submitted is dropped from the dashboard's "Work to do". That costs
one read per open assignment and is the most expensive thing on that screen.

## Roles

Primary `Role` plus an additive `Roles` list, so someone can be both a Teacher
and a Journalist, or an Admin and a Teacher. The primary role decides the
landing dashboard; every dashboard cross-links to the others the user holds.
Roles: Student, Teacher, Parent, Admin, Coach, Visitor, Journalist.

## Secrets

`Firebase:WebApiKey` is a public client identifier, not a secret — it's fine in
`appsettings.json`. **Service account keys are secrets** and are gitignored
(`serviceAccountKey*.json`, `*-firebase-adminsdk-*.json`). Never commit one.

## Docs

`PLAN.md` (architecture, data model, roadmap, known limitations) ·
`RUNNING.md` (setup and operations) · `PRODUCTION.md` ·
`RESEARCH-ALIGNMENT.md` (where this build diverges from the research report).
Keep §8 of PLAN.md honest — it's the list of what's actually still broken.
