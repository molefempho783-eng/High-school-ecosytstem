/**
 * Push notifications (Firebase Cloud Messaging) for everything people need to
 * hear about. The app only registers device tokens at
 * /users/{uid}/devices/{deviceId}; every notification is decided and sent here,
 * server-side, because only the server can read who's in a class, who a
 * parent's children are, and which homework is still not handed in.
 *
 *   school announcement   -> whole school (public ones also reach visitors)
 *   class notice          -> the class's students and their parents
 *   new homework/test     -> the class's students
 *   work marked           -> that student
 *   Newspaper article     -> whole school
 *   sports post           -> whole school
 *   team notice           -> the squad and their parents
 *   class chat message    -> the class (students + teacher), not the sender
 *   teacher-parent message -> the other person in the conversation
 *   16:00 daily           -> each student: what's due or being assessed tomorrow
 *
 * Nobody is ever notified about something they did themselves.
 *
 * Android notifications use the "school_updates" channel — the app creates it
 * (MainActivity.NotificationChannelId) and the manifest names it as default.
 * Dead tokens (app uninstalled, signed out elsewhere) are deleted as FCM
 * reports them, so the device list cleans itself up.
 */

import { onDocumentCreated, onDocumentUpdated } from 'firebase-functions/v2/firestore';
import { onSchedule } from 'firebase-functions/v2/scheduler';
import { logger } from 'firebase-functions';
import { getFirestore, Timestamp } from 'firebase-admin/firestore';
import { getMessaging } from 'firebase-admin/messaging';

// One school per Firebase project, so one time zone. Used for "due Friday" and
// for when the daily reminder runs.
const TIME_ZONE = 'Africa/Johannesburg';
const ANDROID_CHANNEL = 'school_updates';

const COMMUNITY_ROLES = ['Student', 'Teacher', 'Parent', 'Admin', 'Coach', 'Journalist'];

// Lazy: index.js calls initializeApp() after importing this module.
const db = () => getFirestore();

// ------------------------------------------------------------- recipients

function rolesOf(user) {
  return [user?.Role, ...(Array.isArray(user?.Roles) ? user.Roles : [])].filter(Boolean);
}

/** Every token for a set of users. Per-user reads: cheap for a class. */
async function devicesForUsers(uids) {
  const unique = [...new Set(uids)].filter(Boolean);
  const snaps = await Promise.all(
    unique.map(uid => db().collection('users').doc(uid).collection('devices').get()));
  return snaps.flatMap(s => s.docs);
}

/**
 * Devices for the whole school. One collection-group read finds every device;
 * only the owners of those devices are then read, to check their roles — far
 * fewer reads than scanning every user when most never installed the app.
 */
async function devicesForSchool({ includeVisitors = false } = {}) {
  const all = await db().collectionGroup('devices').get();
  const owners = [...new Set(all.docs.map(d => d.ref.parent.parent?.id).filter(Boolean))];

  const allowed = new Set();
  for (let i = 0; i < owners.length; i += 100) {
    const refs = owners.slice(i, i + 100).map(id => db().collection('users').doc(id));
    const users = await db().getAll(...refs);
    for (const u of users) {
      if (!u.exists) continue;
      const roles = rolesOf(u.data());
      if (includeVisitors || roles.some(r => COMMUNITY_ROLES.includes(r))) allowed.add(u.id);
    }
  }
  return all.docs.filter(d => allowed.has(d.ref.parent.parent?.id));
}

/** Parents linked to any of these students. array-contains-any takes 30 at a time. */
async function parentsOf(studentIds) {
  const ids = [...new Set(studentIds)].filter(Boolean);
  const parents = new Set();
  for (let i = 0; i < ids.length; i += 30) {
    const snap = await db().collection('users')
      .where('LinkedStudentIds', 'array-contains-any', ids.slice(i, i + 30)).get();
    snap.docs.forEach(d => parents.add(d.id));
  }
  return [...parents];
}

// ------------------------------------------------------------------ send

/**
 * Sends one notification to a set of device documents, skipping the person
 * who caused it. Deletes tokens FCM says are dead.
 */
async function send(deviceDocs, { title, body, data = {}, exceptUid = '', group = '' }) {
  const targets = deviceDocs.filter(d => d.ref.parent.parent?.id !== exceptUid && d.get('Token'));
  if (targets.length === 0) return;

  const message = {
    notification: { title: clip(title, 120), body: clip(body, 240) },
    // FCM data values must all be strings.
    data: Object.fromEntries(Object.entries(data).map(([k, v]) => [k, String(v ?? '')])),
    android: {
      priority: 'high',
      // A tag replaces the previous notification with the same tag, so a busy
      // class chat shows its latest message instead of forty.
      notification: { channelId: ANDROID_CHANNEL, ...(group ? { tag: group } : {}) },
    },
    apns: { payload: { aps: { sound: 'default', ...(group ? { 'thread-id': group } : {}) } } },
  };

  for (let i = 0; i < targets.length; i += 500) {
    const batch = targets.slice(i, i + 500);
    const result = await getMessaging().sendEachForMulticast({
      ...message,
      tokens: batch.map(d => d.get('Token')),
    });

    const dead = [];
    result.responses.forEach((r, j) => {
      const code = r.error?.code ?? '';
      if (code === 'messaging/registration-token-not-registered'
          || code === 'messaging/invalid-registration-token'
          || code === 'messaging/invalid-argument') {
        dead.push(batch[j].ref.delete());
      }
    });
    await Promise.all(dead);

    if (result.failureCount > 0) {
      logger.info(`Push: ${result.successCount} sent, ${result.failureCount} failed, ${dead.length} dead tokens removed.`);
    }
  }
}

function clip(text, max) {
  const t = String(text ?? '').replace(/\s+/g, ' ').trim();
  return t.length > max ? t.slice(0, max - 1) + '…' : t;
}

function dayLabel(timestamp) {
  if (!timestamp?.toDate) return '';
  return new Intl.DateTimeFormat('en-ZA', {
    weekday: 'short', day: 'numeric', month: 'short', timeZone: TIME_ZONE,
  }).format(timestamp.toDate());
}

async function classInfo(classId) {
  const snap = await db().collection('classes').doc(classId).get();
  const c = snap.exists ? snap.data() : {};
  return {
    name: c.Subject || c.Name || 'Your class',
    teacherId: c.TeacherId || '',
    studentIds: Array.isArray(c.StudentIds) ? c.StudentIds : [],
  };
}

/** Wraps a trigger so a notification failure is logged, never retried into a storm. */
function safely(name, handler) {
  return async (event) => {
    try { await handler(event); }
    catch (error) { logger.error(`Push ${name} failed`, error); }
  };
}

// --------------------------------------------------------------- triggers

export const notifyAnnouncement = onDocumentCreated('announcements/{id}',
  safely('announcement', async (event) => {
    const a = event.data?.data();
    if (!a) return;
    const devices = await devicesForSchool({ includeVisitors: a.IsPublic === true });
    await send(devices, {
      title: 'School announcement',
      body: a.Title,
      data: { type: 'announcement' },
      exceptUid: a.AuthorId,
    });
  }));

export const notifyClassNotice = onDocumentCreated('classes/{classId}/announcements/{id}',
  safely('class notice', async (event) => {
    const n = event.data?.data();
    if (!n) return;
    const { classId } = event.params;
    const cls = await classInfo(classId);
    const parents = await parentsOf(cls.studentIds);
    await send(await devicesForUsers([...cls.studentIds, ...parents]), {
      title: `${cls.name}: new notice`,
      body: n.Title,
      data: { type: 'classNotice', classId },
      exceptUid: n.AuthorId,
    });
  }));

export const notifyNewAssignment = onDocumentCreated('classes/{classId}/assignments/{id}',
  safely('assignment', async (event) => {
    const a = event.data?.data();
    if (!a) return;
    const { classId } = event.params;
    const cls = await classInfo(classId);
    const kind = { Test: 'test', Exam: 'exam', Homework: 'homework' }[a.Kind] ?? 'assignment';
    const when = dayLabel(a.DueDate);
    const isAssessment = a.Kind === 'Test' || a.Kind === 'Exam';
    await send(await devicesForUsers(cls.studentIds), {
      title: `New ${kind} in ${cls.name}`,
      body: when ? `${a.Title} · ${isAssessment ? 'on' : 'due'} ${when}` : a.Title,
      data: { type: 'assignment', classId },
    });
  }));

export const notifyWorkMarked = onDocumentUpdated('classes/{classId}/assignments/{assignmentId}/submissions/{studentId}',
  safely('marked', async (event) => {
    const before = event.data?.before?.data() ?? {};
    const after = event.data?.after?.data() ?? {};
    const was = before.TeacherMarkedAt?.toMillis?.() ?? 0;
    const now = after.TeacherMarkedAt?.toMillis?.() ?? 0;
    // Only when a teacher has just marked it (or re-marked it). Auto-marking
    // is skipped: the student is looking at the result as it happens.
    if (now <= 0 || now === was) return;

    const { classId, assignmentId, studentId } = event.params;
    const [cls, assignment] = await Promise.all([
      classInfo(classId),
      db().collection('classes').doc(classId).collection('assignments').doc(assignmentId).get(),
    ]);
    await send(await devicesForUsers([studentId]), {
      title: `Marked: ${assignment.get('Title') || 'your work'}`,
      body: `${cls.name} · tap to see your mark and feedback`,
      data: { type: 'marked', classId },
    });
  }));

export const notifyArticle = onDocumentCreated('newspaper/{id}',
  safely('article', async (event) => {
    const a = event.data?.data();
    if (!a) return;
    await send(await devicesForSchool(), {
      title: 'New in the Newspaper',
      body: a.AuthorName ? `${a.Title} — by ${a.AuthorName}` : a.Title,
      data: { type: 'newspaper' },
      exceptUid: a.AuthorId,
    });
  }));

export const notifySport = onDocumentCreated('sports/{id}',
  safely('sport', async (event) => {
    const s = event.data?.data();
    if (!s) return;
    const label = { Fixture: 'Fixture', Result: 'Result', News: 'Sports news' }[s.Kind] ?? 'Sport';
    const team = [s.Sport, s.Team].filter(Boolean).join(' ');
    await send(await devicesForSchool(), {
      title: team ? `${label}: ${team}` : label,
      body: s.Kind === 'Fixture' && s.EventDate ? `${s.Title} · ${dayLabel(s.EventDate)}` : s.Title,
      data: { type: 'sport' },
      exceptUid: s.AuthorId,
    });
  }));

export const notifyTeamNotice = onDocumentCreated('teams/{teamId}/notices/{id}',
  safely('team notice', async (event) => {
    const n = event.data?.data();
    if (!n) return;
    const team = (await db().collection('teams').doc(event.params.teamId).get()).data() ?? {};
    const members = Array.isArray(team.MemberIds) ? team.MemberIds : [];
    // Parents too: they're usually the lift to practice.
    const parents = await parentsOf(members);
    await send(await devicesForUsers([...members, ...parents]), {
      title: `${team.Name || 'Your team'}: new notice`,
      body: n.Title,
      data: { type: 'teamNotice', teamId: event.params.teamId },
      exceptUid: n.AuthorId,
    });
  }));

export const notifyChatMessage = onDocumentCreated('classes/{classId}/messages/{id}',
  safely('chat', async (event) => {
    const m = event.data?.data();
    if (!m) return;
    const { classId } = event.params;
    const cls = await classInfo(classId);
    const what = m.Text?.trim()
      || (m.AttachmentKind === 'Image' ? 'sent a picture' : m.AttachmentKind === 'File' ? `sent ${m.AttachmentName || 'a file'}` : '');
    const separator = m.Text?.trim() ? ': ' : ' ';
    await send(await devicesForUsers([...cls.studentIds, cls.teacherId]), {
      title: cls.name,
      body: `${m.AuthorName || 'Someone'}${separator}${what}`,
      data: { type: 'chat', classId, className: cls.name },
      exceptUid: m.AuthorId,
      group: `chat-${classId}`,
    });
  }));

// ------------------------------------------------- daily due reminders

/** Midnight at the start of the day `offset` days from now, in the school's time zone, as a Date. */
function schoolMidnight(offsetDays) {
  const parts = new Intl.DateTimeFormat('en-CA', {
    timeZone: TIME_ZONE, year: 'numeric', month: '2-digit', day: '2-digit',
  }).formatToParts(new Date());
  const get = type => Number(parts.find(p => p.type === type).value);
  // Midnight UTC on the school's calendar date, then shifted by the zone's
  // offset at that moment. South Africa has no daylight saving, but this
  // stays correct for zones that do.
  const utcMidnight = Date.UTC(get('year'), get('month') - 1, get('day') + offsetDays);
  const zoneHour = Number(new Intl.DateTimeFormat('en-GB', {
    timeZone: TIME_ZONE, hour: '2-digit', hourCycle: 'h23',
  }).format(new Date(utcMidnight)));
  const offsetHours = zoneHour >= 12 ? zoneHour - 24 : zoneHour;
  return new Date(utcMidnight - offsetHours * 3600 * 1000);
}

export const remindDueTomorrow = onSchedule(
  { schedule: '0 16 * * *', timeZone: TIME_ZONE, retryCount: 0 },
  async () => {
    const from = Timestamp.fromDate(schoolMidnight(1));
    const to = Timestamp.fromDate(schoolMidnight(2));

    // student uid -> list of "Title (Class)" lines
    const byStudent = new Map();
    const add = (uid, line) => {
      if (!byStudent.has(uid)) byStudent.set(uid, []);
      byStudent.get(uid).push(line);
    };

    const classes = await db().collection('classes').get();
    for (const classDoc of classes.docs) {
      const c = classDoc.data();
      const students = Array.isArray(c.StudentIds) ? c.StudentIds : [];
      if (students.length === 0) continue;
      const className = c.Subject || c.Name || 'Class';

      const due = await classDoc.ref.collection('assignments')
        .where('DueDate', '>=', from).where('DueDate', '<', to).get();

      for (const a of due.docs) {
        const kind = a.get('Kind');
        const title = a.get('Title') || 'Untitled';

        if (kind === 'Test' || kind === 'Exam') {
          // Nothing to hand in — everyone sits it.
          students.forEach(uid => add(uid, `${kind}: ${title} (${className})`));
          continue;
        }

        // Homework: only remind those who haven't handed it in.
        const refs = students.map(uid => a.ref.collection('submissions').doc(uid));
        const subs = await db().getAll(...refs);
        subs.forEach((s, i) => { if (!s.exists) add(students[i], `${title} (${className})`); });
      }
    }

    for (const [uid, lines] of byStudent) {
      await send(await devicesForUsers([uid]), {
        title: lines.length === 1 ? 'Due tomorrow' : `${lines.length} things due tomorrow`,
        body: lines.join(' · '),
        data: { type: 'dueTomorrow' },
      });
    }
    logger.info(`Due-tomorrow reminders: ${byStudent.size} students.`);
  });

// ------------------------------------------------ teacher-parent messages

export const notifyDirectMessage = onDocumentCreated('conversations/{conversationId}/messages/{id}',
  safely('direct message', async (event) => {
    const m = event.data?.data();
    if (!m) return;
    const { conversationId } = event.params;
    const convo = (await db().collection('conversations').doc(conversationId).get()).data();
    if (!convo) return;

    const toTeacher = m.AuthorId === convo.ParentId;
    const recipient = toTeacher ? convo.TeacherId : convo.ParentId;
    if (!recipient || recipient === m.AuthorId) return;

    await send(await devicesForUsers([recipient]), {
      title: m.AuthorName || (toTeacher ? 'A parent' : 'A teacher'),
      // Says which child it's about: a parent with two children at the school,
      // or a teacher with thirty families, needs that before opening it.
      body: `${toTeacher ? `Parent of ${convo.StudentName}` : `About ${convo.StudentName}`}: ${m.Text}`,
      data: { type: 'dm', conversationId },
      group: `dm-${conversationId}`,
    });
  }));
