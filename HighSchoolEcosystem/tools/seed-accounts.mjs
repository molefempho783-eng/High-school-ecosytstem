/**
 * Seeds dummy accounts into a Firebase project for development and demos.
 *
 * Creates 28 accounts (10 students, 3 teachers, 10 parents, 3 visitors,
 * 2 coaches), marks their emails verified, writes each /users/{uid} profile
 * with the right roles, and links each parent to a student.
 *
 * WHY THE ADMIN SDK: creating auth users and writing profiles with privileged
 * roles both bypass the client security rules, which is exactly what the Admin
 * SDK is for. Doing this over the client REST API would be blocked by the
 * email-verification gate on privileged roles.
 *
 * ⚠️  This writes real accounts to whichever project the credentials point at.
 *     Run it against a DEV project only. It is idempotent: re-running updates
 *     existing accounts rather than duplicating them.
 *
 * Setup:
 *   1. Firebase console → Project settings → Service accounts →
 *      "Generate new private key". Save the JSON somewhere OUTSIDE this repo.
 *   2. npm install firebase-admin
 *   3. Run it, passing the key path. Works in PowerShell, cmd and bash alike:
 *
 *        node tools/seed-accounts.mjs --key "C:\\keys\\serviceAccountKey.json"
 *
 *      The project ID is read from the key file, so there is nothing else to set.
 *
 * Remove everything again:
 *        node tools/seed-accounts.mjs --key "C:\\keys\\serviceAccountKey.json" --delete
 */

import { readFileSync } from 'node:fs';
import { initializeApp, cert } from 'firebase-admin/app';
import { getAuth } from 'firebase-admin/auth';
import { getFirestore } from 'firebase-admin/firestore';

// Shared password for every seeded account. Dev only — these are throwaway
// logins for a test project, never for anything holding real student data.
const PASSWORD = 'Passw0rd!2026';
const DOMAIN = 'demo.school';

/** Reads `--name value` from argv. */
function arg(name) {
  const i = process.argv.indexOf(`--${name}`);
  return i !== -1 && process.argv[i + 1] && !process.argv[i + 1].startsWith('--')
    ? process.argv[i + 1]
    : undefined;
}

const keyPath = arg('key') ?? process.env.GOOGLE_APPLICATION_CREDENTIALS;
let projectId = arg('project') ?? process.env.FIREBASE_PROJECT_ID;

if (!keyPath) {
  console.error(`
No service account key given.

Pass it directly (works in PowerShell, cmd and bash):

  node tools/seed-accounts.mjs --key "C:\\path\\to\\serviceAccountKey.json"

Get the file from: Firebase console → Project settings → Service accounts →
"Generate new private key". Save it OUTSIDE this repo — it grants full admin
access to the project.
`);
  process.exit(1);
}

let credential;
try {
  // Read the file ourselves rather than relying on applicationDefault(), so a
  // bad path fails here with a clear message instead of deep inside the SDK.
  const key = JSON.parse(readFileSync(keyPath, 'utf8'));
  projectId ??= key.project_id;
  credential = cert(key);
} catch (err) {
  console.error(`\nCouldn't read the service account key at:\n  ${keyPath}\n\n${err.message}\n`);
  console.error('Check the path is correct and quoted if it contains spaces.\n');
  process.exit(1);
}

if (!projectId) {
  console.error('The key file has no project_id. Pass --project <id> explicitly.');
  process.exit(1);
}

initializeApp({ credential, projectId });
const auth = getAuth();
const db = getFirestore();

/** Builds the account list. Parents are matched to students by index. */
function buildAccounts() {
  const students = [
    'Amara Dlamini', 'Ben Nkosi', 'Chloe van Wyk', 'Daniel Mokoena', 'Eshe Adebayo',
    'Farai Moyo', 'Grace Botha', 'Hassan Patel', 'Imani Zulu', 'Jonas Meyer',
  ];
  const teachers = ['Naledi Khumalo', 'Peter Steyn', 'Rina Naidoo'];
  const visitors = ['Sam Visitor', 'Tessa Guest', 'Uche Prospect'];
  const coaches = ['Vusi Ndlovu', 'Wendy Fourie'];

  const accounts = [];

  students.forEach((name, i) => {
    accounts.push({
      key: `student${i + 1}`,
      email: `student${i + 1}@${DOMAIN}`,
      name,
      role: 'Student',
      // Give one student the additive Journalist role, so the multi-role path
      // and the school newspaper have something to exercise.
      roles: i === 0 ? ['Journalist'] : [],
    });
  });

  teachers.forEach((name, i) => {
    accounts.push({
      key: `teacher${i + 1}`,
      email: `teacher${i + 1}@${DOMAIN}`,
      name,
      role: 'Teacher',
      // One teacher is also an admin, to exercise the dual dashboard, and one
      // is also a journalist (newspaper supervisor).
      roles: i === 0 ? ['Admin'] : i === 1 ? ['Journalist'] : [],
    });
  });

  students.forEach((studentName, i) => {
    accounts.push({
      key: `parent${i + 1}`,
      email: `parent${i + 1}@${DOMAIN}`,
      name: `Parent of ${studentName.split(' ')[0]}`,
      role: 'Parent',
      roles: [],
      linkTo: `student${i + 1}`,
    });
  });

  visitors.forEach((name, i) => {
    accounts.push({ key: `visitor${i + 1}`, email: `visitor${i + 1}@${DOMAIN}`, name, role: 'Visitor', roles: [] });
  });

  coaches.forEach((name, i) => {
    accounts.push({ key: `coach${i + 1}`, email: `coach${i + 1}@${DOMAIN}`, name, role: 'Coach', roles: [] });
  });

  return accounts;
}

/** Creates the auth user, or reuses and updates it if the email already exists. */
async function upsertUser({ email, name }) {
  try {
    const existing = await auth.getUserByEmail(email);
    await auth.updateUser(existing.uid, {
      password: PASSWORD,
      displayName: name,
      emailVerified: true,
    });
    return { uid: existing.uid, created: false };
  } catch (err) {
    if (err.code !== 'auth/user-not-found') throw err;
    const created = await auth.createUser({
      email,
      password: PASSWORD,
      displayName: name,
      // Set directly: privileged roles are gated on a verified email, and these
      // accounts have no real mailbox to receive a verification link.
      emailVerified: true,
    });
    return { uid: created.uid, created: true };
  }
}

async function seed() {
  const accounts = buildAccounts();
  const uids = {};

  console.log(`Seeding ${accounts.length} accounts into "${projectId}"…\n`);

  // Pass 1 — auth users and base profiles.
  for (const account of accounts) {
    const { uid, created } = await upsertUser(account);
    uids[account.key] = uid;

    await db.doc(`users/${uid}`).set({
      Uid: uid,
      DisplayName: account.name,
      Email: account.email,
      Role: account.role,
      Roles: account.roles,
      ClassIds: [],
      LinkedStudentIds: [],
    }, { merge: true });

    const extra = account.roles.length ? ` (+${account.roles.join(', ')})` : '';
    console.log(`  ${created ? 'created' : 'updated'}  ${account.email.padEnd(28)} ${account.role}${extra}`);
  }

  // Pass 2 — parent links, which need the student UIDs from pass 1.
  for (const account of accounts.filter(a => a.linkTo)) {
    const studentUid = uids[account.linkTo];
    if (!studentUid) continue;
    await db.doc(`users/${uids[account.key]}`).set(
      { LinkedStudentIds: [studentUid] }, { merge: true });
  }

  // Roster entries, so the same emails get the right roles if the profile is
  // ever deleted and the account signs in again.
  for (const account of accounts) {
    await db.doc(`invites/${account.email.toLowerCase()}`).set({
      Role: account.role,
      Roles: account.roles,
      ClassIds: [],
      LinkedStudentIds: [],
    }, { merge: true });
  }

  console.log(`\nDone. Every account uses the password: ${PASSWORD}`);
  console.log('Parents are linked to the student with the matching number.');
}

async function remove() {
  const accounts = buildAccounts();
  console.log(`Deleting ${accounts.length} seeded accounts from "${projectId}"…\n`);

  for (const account of accounts) {
    try {
      const user = await auth.getUserByEmail(account.email);
      await auth.deleteUser(user.uid);
      await db.doc(`users/${user.uid}`).delete();
      console.log(`  deleted  ${account.email}`);
    } catch (err) {
      if (err.code !== 'auth/user-not-found') throw err;
    }
    await db.doc(`invites/${account.email.toLowerCase()}`).delete();
  }
  console.log('\nDone.');
}

const run = process.argv.includes('--delete') ? remove : seed;
run().then(() => process.exit(0)).catch(err => {
  console.error('\nFailed:', err);
  process.exit(1);
});
