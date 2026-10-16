/**
 * AI textbook import: a teacher uploads a textbook PDF and Gemini (on Vertex
 * AI) drafts units, study notes and practice quizzes from it.
 *
 * FLOW
 *   1. The app uploads the PDF to classes/{classId}/textbooks/{file} and
 *      creates classes/{classId}/textbookJobs/{jobId} with Status "Queued".
 *   2. generateTextbookOutline (this file) reads the whole PDF once and writes
 *      one chapters/{chapterId} document per chapter, Status "Pending".
 *   3. Each new chapter document triggers generateTextbookChapter, which reads
 *      that chapter's pages and writes notes plus draft quiz questions.
 *      Chapters run in parallel, one function invocation each, so no single
 *      invocation comes near the 9-minute limit however long the book is.
 *   4. The teacher reviews and edits the drafts in the app, then publishes
 *      each chapter, which copies it into the class's real units, materials
 *      and assignments. NOTHING here is visible to students until then.
 *
 * WHY VERTEX AI AND NOT AN API KEY
 *   The function authenticates as the project's own service account, so there
 *   is no key to store, rotate or leak, and usage is billed to this Firebase
 *   project. Vertex AI also doesn't use prompts or files to train models.
 *
 * SETUP (once per Firebase project) — see RUNNING.md, "AI textbook import":
 *   - enable the Vertex AI API (aiplatform.googleapis.com)
 *   - npm install --prefix functions
 *   - firebase deploy --only functions
 *
 * Model and location are parameters, so switching to a newer Gemini model (or
 * to Pro for a diagram-heavy book) is a deploy-time setting, not a code change.
 */

import { onDocumentCreated, onDocumentWritten } from 'firebase-functions/v2/firestore';
import { defineString } from 'firebase-functions/params';
import { logger } from 'firebase-functions';
import { getFirestore, FieldValue } from 'firebase-admin/firestore';
import { getStorage } from 'firebase-admin/storage';
import { GoogleGenAI, Type } from '@google/genai';

const GEMINI_MODEL = defineString('GEMINI_MODEL', {
  default: 'gemini-3.8-flash',
  description: 'Gemini model used to read textbooks.',
});

const GEMINI_LOCATION = defineString('GEMINI_LOCATION', {
  default: 'global',
  description: 'Vertex AI location for Gemini calls.',
});

// Gemini reads PDFs of up to 50 MB / 1000 pages. storage.rules enforces the
// size on upload; this is the server-side backstop.
const MAX_PDF_BYTES = 50 * 1024 * 1024;
const MAX_CHAPTERS = 30;
const QUESTIONS_PER_CHAPTER = 8;

const RUN_OPTIONS = {
  timeoutSeconds: 540,
  memory: '512MiB',
  // One textbook = a handful of invocations. Retrying automatically on a
  // model error would just spend the same money on the same failure; the
  // teacher retries a failed chapter from the app instead.
  retry: false,
};

// ---------------------------------------------------------------- helpers

// Lazy: index.js calls initializeApp() after importing this module.
const db = () => getFirestore();

function projectId() {
  if (process.env.GCLOUD_PROJECT) return process.env.GCLOUD_PROJECT;
  try { return JSON.parse(process.env.FIREBASE_CONFIG ?? '{}').projectId; } catch { return undefined; }
}

let client;
function gemini() {
  client ??= new GoogleGenAI({
    vertexai: true,
    project: projectId(),
    location: GEMINI_LOCATION.value(),
  });
  return client;
}

/** The PDF as Gemini sees it: a gs:// URI it reads directly from the bucket. */
function pdfPart(storagePath) {
  const bucket = getStorage().bucket().name;
  return { fileData: { fileUri: `gs://${bucket}/${storagePath}`, mimeType: 'application/pdf' } };
}

async function askForJson(storagePath, prompt, schema) {
  const response = await gemini().models.generateContent({
    model: GEMINI_MODEL.value(),
    contents: [{ role: 'user', parts: [pdfPart(storagePath), { text: prompt }] }],
    config: {
      responseMimeType: 'application/json',
      responseSchema: schema,
      // Low: this is summarising a textbook, not creative writing.
      temperature: 0.3,
    },
  });

  const text = response.text;
  if (!text) throw new Error('Gemini returned an empty response.');
  return JSON.parse(text);
}

/**
 * Turns an exception into something a teacher can act on. The full error
 * always goes to the function log; only this line reaches the app.
 */
function friendlyError(error) {
  const message = String(error?.message ?? error);
  if (/PERMISSION_DENIED|aiplatform|SERVICE_DISABLED|has not been used/i.test(message)) {
    return 'The AI service isn\'t switched on for this school\'s Firebase project yet. '
      + 'Ask whoever manages the app to enable Vertex AI (see RUNNING.md).';
  }
  if (/RESOURCE_EXHAUSTED|429|quota/i.test(message)) {
    return 'The AI service is busy right now. Try again in a few minutes.';
  }
  if (/not found|NOT_FOUND|404/i.test(message) && /model/i.test(message)) {
    return 'The configured Gemini model isn\'t available. Check GEMINI_MODEL (see RUNNING.md).';
  }
  if (/JSON/i.test(message)) {
    return 'The AI\'s answer couldn\'t be read. Retrying usually fixes this.';
  }
  return 'Something went wrong while reading the textbook. Try again, and if it keeps '
    + 'failing, the PDF may be scanned at too low a quality or protected.';
}

// The app shows notes in a plain label, so any Markdown the model slips in
// would appear as literal asterisks and hashes.
function plainText(value) {
  return String(value ?? '')
    .replace(/\r\n/g, '\n')
    .replace(/^#{1,6}\s*/gm, '')
    .replace(/\*\*(.+?)\*\*/g, '$1')
    .replace(/__(.+?)__/g, '$1')
    .replace(/`+/g, '')
    .replace(/^\s*[-*]\s+/gm, '• ')
    .replace(/\n{3,}/g, '\n\n')
    .trim();
}

function clean(value, max) {
  return String(value ?? '').replace(/\s+/g, ' ').trim().slice(0, max);
}

/**
 * Keeps only questions the app and the marking function can actually use:
 * 2–6 distinct options and a correct answer that's one of them. The options
 * are then shuffled so the right answer isn't always in the same position.
 */
function usableQuestions(raw) {
  const out = [];
  for (const q of Array.isArray(raw) ? raw : []) {
    const text = clean(q?.text, 500);
    const options = (Array.isArray(q?.options) ? q.options : []).map(o => clean(o, 200));
    const correct = Number.isInteger(q?.correctIndex) ? q.correctIndex : -1;

    if (!text || options.length < 2 || options.length > 6) continue;
    if (options.some(o => !o) || new Set(options).size !== options.length) continue;
    if (correct < 0 || correct >= options.length) continue;

    const answer = options[correct];
    const shuffled = [...options];
    for (let i = shuffled.length - 1; i > 0; i--) {
      const j = Math.floor(Math.random() * (i + 1));
      [shuffled[i], shuffled[j]] = [shuffled[j], shuffled[i]];
    }
    out.push({ text, options: shuffled, correctIndex: shuffled.indexOf(answer) });
  }
  return out;
}

/** Marks the job Ready once no chapter is still waiting or being written. */
async function updateJobProgress(jobRef) {
  const chapters = await jobRef.collection('chapters').get();
  const statuses = chapters.docs.map(d => d.get('Status'));
  const busy = statuses.filter(s => s === 'Pending' || s === 'Generating').length;
  const failed = statuses.filter(s => s === 'Failed').length;

  await jobRef.update({
    ChaptersDone: statuses.length - busy,
    ChaptersFailed: failed,
    Status: busy === 0 ? 'Ready' : 'Generating',
    UpdatedAt: FieldValue.serverTimestamp(),
  });
}

// ------------------------------------------------------------ the outline

const OUTLINE_SCHEMA = {
  type: Type.OBJECT,
  properties: {
    bookTitle: { type: Type.STRING },
    chapters: {
      type: Type.ARRAY,
      items: {
        type: Type.OBJECT,
        properties: {
          title: { type: Type.STRING },
          startPage: { type: Type.INTEGER },
          endPage: { type: Type.INTEGER },
          summary: { type: Type.STRING },
        },
        required: ['title', 'startPage', 'endPage', 'summary'],
      },
    },
  },
  required: ['bookTitle', 'chapters'],
};

const OUTLINE_PROMPT = `You are helping a South African high-school teacher turn the attached textbook into an online course.

Identify the book's title and its teaching chapters, in order.

Rules:
- Page numbers are positions in this PDF FILE, where 1 is the first page of the file. Do not use the page numbers printed on the pages.
- Leave out front and back matter: cover, contents, preface, how-to-use pages, answer sections, glossary, index, and acknowledgements.
- Return chapters (or the book's own top-level units/modules), not every small section. Return at most ${MAX_CHAPTERS}.
- Use the chapter's own title, without its number unless the number is part of how students refer to it.
- summary: two sentences, written for students, saying what the chapter covers.
- If the file is not a textbook or contains no teaching content, return an empty chapters list.`;

export const generateTextbookOutline = onDocumentCreated(
  { document: 'classes/{classId}/textbookJobs/{jobId}', ...RUN_OPTIONS },
  async (event) => {
    const snapshot = event.data;
    if (!snapshot) return;

    const job = snapshot.data();
    const jobRef = snapshot.ref;
    const { classId, jobId } = event.params;

    if (job.Status !== 'Queued') return;

    try {
      // The rules already restrict who can create a job; this guards the
      // storage path, which the client chose, against pointing at another
      // class's files.
      const prefix = `classes/${classId}/textbooks/`;
      if (typeof job.StoragePath !== 'string' || !job.StoragePath.startsWith(prefix)) {
        throw new Error(`Job ${jobId} points outside its class: ${job.StoragePath}`);
      }

      const [metadata] = await getStorage().bucket().file(job.StoragePath).getMetadata();
      if (Number(metadata.size) > MAX_PDF_BYTES) {
        await jobRef.update({
          Status: 'Failed',
          Error: 'That PDF is over 50 MB, which is the most the AI can read. Try splitting it into parts.',
          UpdatedAt: FieldValue.serverTimestamp(),
        });
        return;
      }

      await jobRef.update({ Status: 'ReadingOutline', UpdatedAt: FieldValue.serverTimestamp() });

      const outline = await askForJson(job.StoragePath, OUTLINE_PROMPT, OUTLINE_SCHEMA);

      const chapters = (Array.isArray(outline?.chapters) ? outline.chapters : [])
        .filter(c => clean(c?.title, 200))
        .slice(0, MAX_CHAPTERS);

      if (chapters.length === 0) {
        await jobRef.update({
          Status: 'Failed',
          BookTitle: clean(outline?.bookTitle, 200),
          Error: 'No chapters were found in that PDF. Check it is a textbook with readable text.',
          UpdatedAt: FieldValue.serverTimestamp(),
        });
        return;
      }

      // Job first, chapters second: each chapter write fires its own function,
      // which updates the job's progress — so the job must already say how many
      // chapters to expect.
      await jobRef.update({
        Status: 'Generating',
        BookTitle: clean(outline.bookTitle, 200),
        ChapterCount: chapters.length,
        ChaptersDone: 0,
        ChaptersFailed: 0,
        Error: '',
        UpdatedAt: FieldValue.serverTimestamp(),
      });

      const batch = db().batch();
      chapters.forEach((c, i) => {
        const start = Math.max(1, Math.round(Number(c.startPage) || 1));
        const end = Math.max(start, Math.round(Number(c.endPage) || start));
        // Zero-padded so the documents list in book order.
        const id = String(i + 1).padStart(2, '0');
        batch.set(jobRef.collection('chapters').doc(id), {
          Index: i + 1,
          Title: clean(c.title, 200),
          Summary: clean(c.summary, 600),
          StartPage: start,
          EndPage: end,
          Status: 'Pending',
          Notes: '',
          EstimatedMinutes: 0,
          QuestionCount: 0,
          Error: '',
          PublishedUnitId: '',
          PublishedMaterialId: '',
          PublishedAssignmentId: '',
          UpdatedAt: FieldValue.serverTimestamp(),
        });
      });
      await batch.commit();

      logger.info(`Outlined ${classId}/${jobId}: ${chapters.length} chapters.`);
    } catch (error) {
      logger.error(`Outline failed for ${classId}/${jobId}`, error);
      await jobRef.update({
        Status: 'Failed',
        Error: friendlyError(error),
        UpdatedAt: FieldValue.serverTimestamp(),
      });
    }
  }
);

// ------------------------------------------------------------ a chapter

const CHAPTER_SCHEMA = {
  type: Type.OBJECT,
  properties: {
    notes: { type: Type.STRING },
    estimatedMinutes: { type: Type.INTEGER },
    questions: {
      type: Type.ARRAY,
      items: {
        type: Type.OBJECT,
        properties: {
          text: { type: Type.STRING },
          options: { type: Type.ARRAY, items: { type: Type.STRING } },
          correctIndex: { type: Type.INTEGER },
        },
        required: ['text', 'options', 'correctIndex'],
      },
    },
  },
  required: ['notes', 'estimatedMinutes', 'questions'],
};

function chapterPrompt(chapter) {
  return `You are helping a South African high-school teacher build an online course from the attached textbook.

Work ONLY from pages ${chapter.StartPage} to ${chapter.EndPage} of this PDF file (1 is the first page of the file, not the printed page number). That is the chapter titled "${chapter.Title}".

1. notes: study notes for students on this chapter.
   - PLAIN TEXT ONLY. No Markdown: no #, *, **, underscores or backticks.
   - Put these headings on their own lines, in capitals, in this order: OVERVIEW, KEY IDEAS, KEY TERMS, WORKED EXAMPLE (only if the chapter has calculations or step-by-step procedures), SUMMARY.
   - Use "• " at the start of a line for bullet points. Under KEY TERMS write "• Term: meaning".
   - Explain in your own words, at the level of the textbook. Do not copy long passages from it.
   - Stay faithful to the textbook. Do not add facts it doesn't support.
   - About 600 to 1,200 words.

2. questions: ${QUESTIONS_PER_CHAPTER} multiple-choice questions testing understanding of THIS chapter only.
   - Exactly 4 options each, one correct, with plausible wrong options.
   - No "all of the above" or "none of the above". All options must be different.
   - correctIndex is the 0-based position of the correct option.
   - Mix recall questions with questions that apply the ideas.

3. estimatedMinutes: how long a typical student needs to study the notes and do the quiz.`;
}

export const generateTextbookChapter = onDocumentWritten(
  { document: 'classes/{classId}/textbookJobs/{jobId}/chapters/{chapterId}', ...RUN_OPTIONS },
  async (event) => {
    const after = event.data?.after;
    if (!after?.exists) return; // deleted

    const chapter = after.data();
    const before = event.data?.before?.exists ? event.data.before.data() : null;

    // Only on the transition INTO Pending: when the outline creates it, or when
    // the teacher taps Retry. Every write below changes Status away from
    // Pending, which is what stops this triggering itself in a loop.
    if (chapter.Status !== 'Pending' || before?.Status === 'Pending') return;

    const { classId, jobId, chapterId } = event.params;
    const chapterRef = after.ref;
    const jobRef = chapterRef.parent.parent;

    try {
      const job = (await jobRef.get()).data();
      if (!job?.StoragePath) throw new Error(`Job ${jobId} has no StoragePath.`);

      await chapterRef.update({ Status: 'Generating', Error: '', UpdatedAt: FieldValue.serverTimestamp() });

      const result = await askForJson(job.StoragePath, chapterPrompt(chapter), CHAPTER_SCHEMA);

      const notes = plainText(result?.notes);
      if (notes.length < 200) throw new Error('Gemini returned notes that were too short to use (JSON).');

      const questions = usableQuestions(result?.questions);

      // A retry replaces the previous attempt's questions rather than adding
      // to them.
      const existing = await chapterRef.collection('questions').get();
      const batch = db().batch();
      existing.docs.forEach(d => batch.delete(d.ref));
      questions.forEach((q, i) => {
        batch.set(chapterRef.collection('questions').doc(String(i + 1).padStart(2, '0')), {
          Text: q.text,
          Options: q.options,
          CorrectIndex: q.correctIndex,
          Order: i,
        });
      });
      batch.update(chapterRef, {
        Status: 'Ready',
        Notes: notes,
        EstimatedMinutes: Math.min(240, Math.max(5, Math.round(Number(result?.estimatedMinutes) || 30))),
        QuestionCount: questions.length,
        Error: '',
        UpdatedAt: FieldValue.serverTimestamp(),
      });
      await batch.commit();

      logger.info(`Chapter ${classId}/${jobId}/${chapterId}: ${notes.length} chars, ${questions.length} questions.`);
    } catch (error) {
      logger.error(`Chapter failed: ${classId}/${jobId}/${chapterId}`, error);
      await chapterRef.update({
        Status: 'Failed',
        Error: friendlyError(error),
        UpdatedAt: FieldValue.serverTimestamp(),
      });
    }

    try {
      await updateJobProgress(jobRef);
    } catch (error) {
      logger.error(`Couldn't update progress on ${classId}/${jobId}`, error);
    }
  }
);
