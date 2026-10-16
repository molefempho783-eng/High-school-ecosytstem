/**
 * Auto-marking for multiple-choice homework and assignments.
 *
 * WHY THIS RUNS ON A SERVER, NOT THE APP:
 * the answer key lives at
 *   /classes/{classId}/assignments/{assignmentId}/answerKeys/{questionId}
 * and the security rules deny it to students — that's what stops them reading
 * the answers out of Firestore before submitting. So the client physically
 * cannot mark its own work. This function runs with Admin SDK credentials,
 * which bypass the rules, and is the only thing that sees both the key and the
 * submission.
 *
 * Deploy:  firebase deploy --only functions
 * Requires the Blaze plan — Cloud Functions cannot be deployed on Spark.
 */

import { onDocumentWritten } from 'firebase-functions/v2/firestore';
import { logger } from 'firebase-functions';
import { initializeApp } from 'firebase-admin/app';
import { getFirestore } from 'firebase-admin/firestore';

// AI textbook import (Gemini on Vertex AI). Re-exported so `firebase deploy`
// finds them; see textbooks.js.
export { generateTextbookOutline, generateTextbookChapter } from './textbooks.js';

// Push notifications for announcements, class notices, homework, marks, the
// Newspaper, sport, team notices, class chat and daily due reminders.
export {
  notifyAnnouncement, notifyClassNotice, notifyNewAssignment, notifyWorkMarked,
  notifyArticle, notifySport, notifyTeamNotice, notifyChatMessage, remindDueTomorrow,
  notifyDirectMessage,
} from './notifications.js';

initializeApp();
const db = getFirestore();

// Must match Submission.Separator in the C# model. Answers are stored as
// "questionId<US>optionText" — the ASCII unit separator, chosen because it
// can't appear in text a teacher typed.
const SEPARATOR = '\u001F';

/** Pulls the chosen option text for a question out of the encoded answer list. */
function answerFor(answers, questionId) {
  const prefix = questionId + SEPARATOR;
  const entry = (answers ?? []).find(a => typeof a === 'string' && a.startsWith(prefix));
  return entry ? entry.slice(prefix.length) : null;
}

function millis(value) {
  // Firestore Timestamps have toMillis(); guard against a missing field.
  return value && typeof value.toMillis === 'function' ? value.toMillis() : 0;
}

export const markSubmission = onDocumentWritten(
  'classes/{classId}/assignments/{assignmentId}/submissions/{studentId}',
  async (event) => {
    const after = event.data?.after;
    if (!after?.exists) return; // deleted

    const submission = after.data();
    const { classId, assignmentId, studentId } = event.params;

    // Loop guard. Writing the grade below re-triggers this function, so only
    // act when the SUBMISSION itself is newer than the last mark. A teacher
    // editing Grade or Feedback also lands here and is correctly ignored.
    if (millis(submission.AutoMarkedAt) >= millis(submission.SubmittedAt)) {
      return;
    }

    const base = db
      .collection('classes').doc(classId)
      .collection('assignments').doc(assignmentId);

    const [questionSnap, keySnap] = await Promise.all([
      base.collection('questions').get(),
      base.collection('answerKeys').get(),
    ]);

    if (questionSnap.empty) {
      // Written-only assignment. Nothing to mark automatically — leave it for
      // the teacher rather than stamping a misleading 0.
      logger.info(`No questions on ${classId}/${assignmentId}; skipping.`);
      return;
    }

    const keys = new Map();
    keySnap.forEach(doc => keys.set(doc.id, doc.data().CorrectIndex));

    let earned = 0;
    let available = 0;
    let correct = 0;
    let unmarkable = 0;

    const questions = questionSnap.docs
      .map(doc => ({ id: doc.id, ...doc.data() }))
      .sort((a, b) => (a.Order ?? 0) - (b.Order ?? 0));

    for (const question of questions) {
      const points = question.Points ?? 1;
      const options = question.Options ?? [];
      const correctIndex = keys.get(question.id);

      // A question whose key is missing or out of range can't be marked. Don't
      // count it against the student — that would penalise them for a teacher's
      // half-saved question.
      if (typeof correctIndex !== 'number' || correctIndex < 0 || correctIndex >= options.length) {
        unmarkable += 1;
        logger.warn(`No usable answer key for question ${question.id}; excluded from marking.`);
        continue;
      }

      available += points;

      // Both sides compared as TEXT. The submission stores the option text and
      // the key stores an index, so a teacher reordering options after a
      // student answered still resolves to the right comparison.
      if (answerFor(submission.Answers, question.id) === options[correctIndex]) {
        earned += points;
        correct += 1;
      }
    }

    const markable = questions.length - unmarkable;

    // Every question was unmarkable, so there is nothing to say. Writing
    // Grade: 0 here would show the student a zero for a teacher's mistake, and
    // stamping AutoMarkedAt would stop us retrying once the keys are saved.
    if (markable === 0) {
      logger.warn(`No usable answer keys at all on ${classId}/${assignmentId}; leaving unmarked.`);
      return;
    }

    const written = (submission.Text ?? '').trim();

    let feedback = `Auto-marked: ${correct} of ${markable} correct (${earned}/${available}).`;
    if (unmarkable > 0) {
      feedback += ` ${unmarkable} question(s) had no answer key and were left out.`;
    }
    if (written.length > 0) {
      // Be explicit rather than letting a student assume the total covers
      // everything they wrote.
      feedback += ' Your written answer still needs to be marked by your teacher.';
    }

    await after.ref.update({
      Grade: earned,
      Feedback: feedback,
      AutoMarkedAt: submission.SubmittedAt,
      AutoScore: earned,
      AutoScoreMax: available,
    });

    logger.info(`Marked ${studentId} on ${classId}/${assignmentId}: ${earned}/${available}`);
  }
);
