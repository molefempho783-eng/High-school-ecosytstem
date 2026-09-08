# Research alignment

Maps *High School Ecosystem Platform – Competitive Technical Research Report*
against what is actually built. Honest scoring: **Built** means working code,
**Partial** means the foundation exists but the feature doesn't, **Not started**
means nothing.

Short version: the **platform foundation the research asks for is built** —
MAUI, MVVM, and a real RBAC system with a working guest/visitor portal. Almost
none of the **features** are. Both of the research's Priority 1 and Priority 2
lists are still ahead, and three named features weren't in our plan at all.

---

## §6.1 Technology stack

| Research says | Built | Note |
|---|---|---|
| C# + .NET MAUI, single codebase for Android/iOS/Windows/macOS | ✅ Built | All four targets compile from one codebase |
| MVVM via CommunityToolkit.MVVM, `ObservableCollection` | ✅ Built | Used throughout |
| RBAC rendering the UI shell per role | ✅ Built | Roles drive dashboard content; admins get a separate shell |
| **SQLite-net or EF Core for local persistence** | ❌ Not started | **Divergence — see below** |
| **Refit for typed REST** | ❌ Not started | We hand-rolled `FirestoreClient` / `FirebaseAuthService` |
| **"read-only API layer with rate-limiting" for public data** | ❌ Not started | Firestore rules serve public data directly instead |

### Stack divergences worth a decision

The research never mentions Firebase. It describes a conventional
client + custom REST API + local SQLite cache. We went **Firebase-first**:
Firestore over REST, security rules as the authorisation layer, no local
database, no custom backend.

That's defensible — it removed a whole backend from the critical path, and the
rules give a stronger privilege boundary than a hand-written API would have by
now. But it is a real departure from a document that names SQLite/EF Core and
Refit explicitly. If the research doubles as an academic deliverable, the
divergence needs either justifying in the planning document or correcting.

Two consequences follow from it, both of which the research treats as
differentiators:

- **No offline support.** The research calls for SQLite caching and background
  sync twice. Firestore's *native* SDKs give offline for free; the REST path we
  took does not. On patchy school wifi this is felt immediately.
- **No push notifications.** "Notification reliability" is the single loudest
  competitor failure in the report — unreliable in Google Classroom and
  Schoology, merely "moderate" in Canvas. We removed the native Firebase SDK
  when moving auth to REST, so FCM has to be re-added to deliver this at all.

---

## §6.2 Priority 1 — Core Academic LMS

| Feature | Status |
|---|---|
| Course & Assignment management (Repository pattern, `ObservableCollection`) | ❌ Not started — planned M4 |
| Grade tracking, weighted averages, local cache + background sync | ❌ Not started — planned M5, no caching or sync designed |
| **To-Do & Calendar (unified month-grid view)** | ❌ **Not started, and was missing from PLAN.md entirely** |

## §6.2 Priority 2 — Ecosystem differentiators

| Feature | Status |
|---|---|
| Visitor & public portal, guest role, academic tabs filtered out | ✅ **Built** — the one differentiator that's done. Guest *and* signed-in Visitor both work, enforced by rules rather than UI |
| Extracurricular & Sports (`EventType`, `TeamRosters`, `Venue`, `MatchTimeline`) | ⚠️ Partial — `sportsEvents`/`sportsNews` collections are designed but the entity is much thinner than specified, and **venue double-booking / availability conflict detection isn't designed at all** |
| **School Newspaper / Digital Bulletin (rich-text editor, image handling, push on publish)** | ❌ **Not started, and was missing from PLAN.md entirely** |
| **Teacher–Parent/Student Meeting Scheduler (slots, request/confirm, device calendar, 24h + 1h reminders)** | ❌ **Not started, and was missing from PLAN.md entirely** |

## §6.2 Priority 3

| Feature | Status |
|---|---|
| Learning Analytics Dashboard (LiveCharts2) | ❌ Not started |

---

## §7 UX principles

| Principle | Status |
|---|---|
| Eliminate gesture conflicts (Schoology's failure) | ✅ By construction — no horizontal tab-swiping anywhere. But we also have **no bottom-tab shell**; navigation is a stack with root swaps. A tab shell is what the research's "dynamically filter out academic tabs" implies, so this needs revisiting when there are enough features to warrant tabs |
| Notification reliability | ❌ Not started — nothing sends notifications |
| Seamless multi-role experience | ✅ Built |
| Avoid Canvas's re-login pain | ✅ Sessions persist via refresh token in `SecureStorage` |

---

## Scope additions beyond the research

- **Admin role.** The research lists Student, Teacher, Parent, Guest — no
  administrator. Someone has to assign roles and run the roster, so we added
  Admin with its own dashboard. Worth reflecting back into the planning
  document as a deliberate extension.
- **Visitor vs guest split.** The research says "Guest role". We implemented
  both a signed-in `Visitor` role and unauthenticated guest browsing, which is
  a superset.
- **Email-verification gate** on privileged roles, and a roster-based
  invitation system. Neither is in the research; both were needed to make RBAC
  safe against self-promotion.

---

## Recommended order from here

The research is explicit that the MVP should ship academic core *and*
differentiators together, "positioning the application as a comprehensive
campus operating system rather than just a gradebook."

1. **Courses & assignments** — everything academic depends on this entity.
2. **Announcements + School Newspaper** — cheapest differentiator, and shares
   a content model. Read-heavy, visitor-facing, demos well.
3. **Campus Life hub: sports + timetable + calendar** — the research's
   `ExtracurricularEvent`, including conflict detection.
4. **Gradebook** — needs assignments to exist first.
5. **Meeting scheduler** — self-contained, high user value, needs notifications.
6. **Push notifications + offline cache** — cross-cutting, and the two things
   the research flags as competitor weaknesses we'd be repeating.
