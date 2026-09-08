# Production readiness

An honest assessment. The app is **solid enough for a pilot with real staff on
a controlled roster** — it is **not yet ready to hold a school's live student
records**. The gaps below are not hypothetical; each is something that will
cause a real incident or a real support burden.

Nothing here has been compiled or run in the environment it was written in, so
treat "done" as "written and reviewed", not "verified in production".

---

## Done in code

| Area | State |
|---|---|
| Auth | Firebase Identity Toolkit REST, all four MAUI targets. Sessions persist via refresh token in `SecureStorage`; ID tokens auto-refresh ~1 min before expiry |
| Role assignment | Admin-managed roster at `/invites/{email}`, applied at sign-up, cross-checked by security rules |
| Privilege boundary | Users cannot self-assign a role, change their own `Role`/`ClassIds`/`LinkedStudentIds`, or write their own `Grade`/`Feedback` |
| Identity proof | `Teacher`/`Parent`/`Admin` roster roles require a verified email before they're granted |
| Error handling | Firebase error codes mapped to readable messages; timeouts, offline, and misconfiguration each have distinct copy |
| Crash safety | Global `UnhandledException` / `UnobservedTaskException` logging; no unguarded `async void` paths left |
| Network | 30s timeouts on all Firebase HTTP clients |
| Config | `appsettings.json` embedded (packages correctly into APK/app bundle) |

---

## Blocking — do not onboard a real school without these

### 1. App Check is not configured

Your Web API key is public by design and ships in the app. Without **App
Check**, anyone who extracts it can call your Firebase project directly from
`curl` — they're still bound by the security rules, but they can create
accounts, enumerate whatever is world-readable, and burn your quota at will.

App Check attests that requests come from your genuine app binary. Set it up
(Play Integrity on Android, DeviceCheck/App Attest on iOS) and enable
enforcement for Firestore and Authentication. Note this needs work in the app:
REST calls must send an `X-Firebase-AppCheck` header, which currently nothing
does.

### 2. Registration is completely open

Anyone who installs the app can create a Student account against your project.
For a school this is usually unacceptable — and it's also how you get spam
accounts and quota abuse.

Options, cheapest first:
- Restrict `createAsDefaultStudent()` in `firestore.rules` to `return false;`
  so only rostered people can create a profile. The app already shows a
  reasonable message, though it's worth making it friendlier.
- Add an email-domain check in the rules (e.g. must end `@yourschool.org`).
- Block signups server-side with a Cloud Function `beforeCreate` blocking
  trigger — the only option that also stops account *squatting* (see below).

### 3. No crash or error reporting

`CrashLogger` writes to a local file on the device. Once the app is on someone
else's phone you will never see it. You need Crashlytics, Sentry or App Center
before you have users you can't walk over to.

### 4. Legal basis for handling minors' data

This stores identifiable information about children. Depending on where the
school operates that means **FERPA/COPPA** (US), **GDPR** (EU/UK), or **POPIA**
(South Africa) — and in most cases you need, at minimum: a privacy policy, a
documented retention period, a data-processing agreement with the school, and a
defined route for parents to request access or deletion.

I'm not a lawyer and this isn't legal advice — but shipping this to a school
without having answered those questions is a real risk, not a formality. Worth
a conversation with someone qualified before your first client.

### 5. Separate dev and production projects

`high-school-ecosystem-dev` is named as a dev project. Test data and real
student records must not share a database. Create a `-prod` project, deploy the
rules there, and keep credentials separate.

---

## Important, not blocking

- **Account squatting.** A stranger can register a rostered address before the
  real teacher does. They get no role (verification stops that), but the real
  teacher then hits `EMAIL_EXISTS` and you must delete the squatted account by
  hand. A `beforeCreate` blocking function fixes this properly.
- **No transient retry.** A single dropped packet surfaces as an error the user
  must retry manually. `Microsoft.Extensions.Http.Resilience` + a standard retry
  handler would smooth this over.
- **No offline support.** Every screen needs live network. Firestore's native
  SDKs give offline caching for free; the REST approach does not. If schools
  have patchy wifi, consider a local cache layer.
- **Password policy.** The client requires 8 characters. Firebase's own default
  minimum is 6 — set a stronger policy in the console (Authentication →
  Settings → Password policy) so it's enforced server-side too.
- **No admin console.** Roster entries and role changes are Firebase console
  edits. Fine for one school, painful at three. This is M6 in PLAN.md.
- **Branding is still the .NET template.** `appicon.svg`, `splash.svg` and
  `placeholder_school_logo.svg` are placeholders.
- **Rules have no automated tests.** The privilege boundary is the most
  security-critical code here and nothing verifies it. The Firebase emulator
  supports rules unit tests (`@firebase/rules-unit-testing`) — worth writing
  before you change them again.
- **Firestore document-access budget.** `allow create` on `/users` costs ~7 of
  the 10 permitted `get`/`exists` calls per request. Adding another lookup to
  that path will break it.

---

## Pre-launch checklist

```
[ ] Separate -prod Firebase project created
[ ] App Check enabled and enforced (Firestore + Auth)
[ ] X-Firebase-AppCheck header sent from the app
[ ] Registration closed to non-rostered users
[ ] Crash reporting wired up
[ ] Password policy set in the Firebase console
[ ] Security rules unit tests written and passing
[ ] Firestore backups / PITR enabled
[ ] Budget alerts set on the Firebase project
[ ] Privacy policy published; retention period documented
[ ] Data-processing agreement with the school signed
[ ] Real app icon, splash and school logo
[ ] Release builds signed (Android keystore, iOS provisioning)
[ ] Tested on a physical Android device and a physical iOS device
```
