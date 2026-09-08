/**
 * Sanity check for the answer encoding shared between the C# client and the
 * marking Cloud Function. Run with: node functions/verify-encoding.mjs
 *
 * Worth having because the separator is an invisible control character. If the
 * two sides ever disagree about it, every answer silently marks wrong and
 * nothing in the logs explains why.
 *
 * Built with String.fromCharCode rather than a literal so the value can't be
 * mangled by copy-paste or an editor that strips control characters.
 */

const SEPARATOR = String.fromCharCode(0x1f);

function answerFor(answers, questionId) {
  const prefix = questionId + SEPARATOR;
  const entry = (answers ?? []).find(a => typeof a === 'string' && a.startsWith(prefix));
  return entry ? entry.slice(prefix.length) : null;
}

const encoded = [
  `q1${SEPARATOR}Paris`,
  `q2${SEPARATOR}A rock: 3:4 ratio`, // colons would break a naive separator
];

const checks = [
  ['separator is U+001F', SEPARATOR.charCodeAt(0) === 0x1f],
  ['decodes a simple answer', answerFor(encoded, 'q1') === 'Paris'],
  ['decodes an answer containing colons', answerFor(encoded, 'q2') === 'A rock: 3:4 ratio'],
  ['returns null for an unanswered question', answerFor(encoded, 'q3') === null],
  ['returns null for an empty list', answerFor([], 'q1') === null],
  ['tolerates non-string entries', answerFor([null, 42, `q1${SEPARATOR}Paris`], 'q1') === 'Paris'],
];

let failed = 0;
for (const [name, ok] of checks) {
  console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}`);
  if (!ok) failed++;
}

console.log(failed === 0 ? '\nAll checks passed.' : `\n${failed} check(s) failed.`);
process.exit(failed === 0 ? 0 : 1);
