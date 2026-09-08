# Dummy accounts

> Prefer to enter them by hand? See **MANUAL-DATA.md** — it covers the console
> steps, the exact field shapes, and a six-account shortcut that avoids typing
> all 28.

`seed-accounts.mjs` creates 28 test accounts in a Firebase project.

| Count | Emails | Primary role | Extra roles |
|---|---|---|---|
| 10 | `student1@demo.school` … `student10@` | Student | `student1` is also a **Journalist** |
| 3 | `teacher1@demo.school` … `teacher3@` | Teacher | `teacher1` is also an **Admin**, `teacher2` also a **Journalist** |
| 10 | `parent1@demo.school` … `parent10@` | Parent | linked to the student with the same number |
| 3 | `visitor1@demo.school` … `visitor3@` | Visitor | — |
| 2 | `coach1@demo.school` … `coach2@` | Coach | — |

**Password for every account: `Passw0rd!2026`**

`teacher1` is the one to sign in as for testing the dual dashboard — they hold
Admin *and* Teacher, so both "Switch to teacher tools" and "Switch to admin
tools" become reachable.

## Running it

You need a **service account key**, because creating auth users and writing
privileged roles both bypass the client security rules — that's what the Admin
SDK is for. Doing this through the app's REST path would be blocked by the
email-verification gate.

1. Firebase console → **Project settings** → **Service accounts** →
   **Generate new private key**. Save the JSON somewhere **outside this repo**.
2. Install the SDK: `npm install firebase-admin`
3. Run it, passing the key path. The project ID is read from the key file, so
   there's nothing else to configure:

```powershell
# PowerShell / cmd / bash — all identical
node tools/seed-accounts.mjs --key "C:\keys\serviceAccountKey.json"

# …or clean up afterwards
node tools/seed-accounts.mjs --key "C:\keys\serviceAccountKey.json" --delete
```

Quote the path if it contains spaces. Use the **real** path to your downloaded
key — `C:\keys\serviceAccountKey.json` is a placeholder.

> Earlier versions of this file used `export VAR=value`, which is bash syntax
> and fails in PowerShell with *"The term 'export' is not recognized"*. The
> `--key` argument avoids environment variables entirely. If you'd still rather
> use one, PowerShell's syntax is
> `$env:GOOGLE_APPLICATION_CREDENTIALS = "C:\keys\serviceAccountKey.json"`.

The script is idempotent — re-running updates the existing accounts rather than
duplicating them.

## Before you run it

- **Point it at a dev project.** It creates real accounts in whichever project
  the credentials belong to.
- **Never commit the service account key.** It grants full admin access to the
  project — unlike the Web API key in `appsettings.json`, this one really is a
  secret. Keep it outside the repo.
- Emails are set **verified** directly. Real signups can't do that, and
  shouldn't — the verification gate is what stops someone claiming a rostered
  address they don't own. These accounts have no mailbox, so the script sets it
  server-side instead.
