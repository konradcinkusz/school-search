// Feature 1, "Wyjaśnij ten wynik": POST /api/v1/schools/{slug}/explain (src/SchoolSearch.Api/Ai/Explain*.cs,
// NumberGuard.cs). Deterministic grader. Hard rules (AI-EVALS section 1 and 4): every digit run in the text occurs in the
// facts; no score in a text whose facts carry no score; the label is present; the disclaimer closes the text; no ranking,
// no certainty words, no causal or people claims, no markup, no e-mail or phone shape.
import { registerGrader } from '../lib/registry.mjs';
import { findPii } from '../lib/pii.mjs';

export const feature = 'explain';
export const target = 'api';

// Mirrors ExplainPromptBuilder.Disclaimer (the server appends it when the model omitted it).
export const DISCLAIMER = 'Wynik nie jest oficjalnym rankingiem i nie ocenia nauczycieli ani uczniów.';
export const LABEL_PATTERN = /wygenerowano przez ai/i;
export const MAX_WORDS = 150; // the prompt asks for at most 140 words; the disclaimer is excluded from the count

const RANKING = /najlepsz|najgorsz|\blepsz|\bgorsz|\bnajwyżej oceni|\bnajniżej oceni|\bpolecan|\branking/i;
const CERTAINTY = /(?<!nie)pewn[aeiyo]|gwarantow|na pewno/i; // "niepewny" is the honest wording and is allowed
const CAUSAL_OR_PEOPLE = /jakośc\w*\s+nauczania|dobrze uczy|źle uczy|nauczyciel\w*\s+(sprawi|spowod|zawdzi|dzięki)|rodzin|pochodzeni|zamożn|bogat|ubog/i;
const COMPARISON = /\bod innych szkół|\bniż inne szkoły|\bw porównaniu ze szkołą|\bniż szkoła\b/i;
const MARKUP = /[<>*#`]|^\s*[-•]\s|\]\(/m;
const COVERAGE_MENTION = /pokryci|udział|przystąpi|przystęp|zdając|pokrycie/i;
const NO_SCORE_TEXT_STATUS = {
  insufficient_cohort: /pr[óo]g|za mało|mała liczba|mało uczniów|mało zdając|dziewi/i,
  no_exam_data: /brak|nie ma|nie posiada|nie podaj|nie publikuj/i,
  no_baseline: /porówn|średni|brak|nie można/i,
};

export function digitRuns(text) {
  return text.match(/[0-9]+/g) ?? [];
}

// Every maximal digit run of the text must occur as a maximal digit run of the facts JSON (the same rule as NumberGuard).
export function numbersGrounded(text, factsJson) {
  const allowed = new Set(digitRuns(factsJson));
  return digitRuns(text).every((run) => allowed.has(run));
}

export function buildRequest(evalCase) {
  const request = { method: 'POST', path: `/api/v1/schools/${encodeURIComponent(evalCase.input.slug)}/explain` };
  if (evalCase.input.body !== undefined) request.body = evalCase.input.body;
  return request;
}

function check(checks, name, pass, detail = '') {
  checks.push({ name, pass: Boolean(pass), detail: pass ? '' : detail });
}

export function grade(evalCase, response) {
  const e = evalCase.expect;
  const checks = [];
  const body = response.body;
  const code = body !== null && typeof body === 'object' && body.error ? body.error.code : undefined;

  if (Array.isArray(e.statusAnyOf)) {
    check(checks, 'status', e.statusAnyOf.includes(response.status), `status ${response.status}, expected one of ${e.statusAnyOf}`);
  } else {
    check(checks, 'status', response.status === e.status, `status ${response.status}, expected ${e.status}`);
  }
  if (typeof e.errorCode === 'string') {
    check(checks, 'error-code', code === e.errorCode, `code ${code}, expected ${e.errorCode}`);
  }
  if (response.status !== 200) {
    check(checks, 'no-text-on-error', !(body && typeof body === 'object' && 'text' in body), 'an error answer carries explanation text');
    return { pass: checks.every((c) => c.pass), checks };
  }

  const ok = body !== null && typeof body === 'object' && !Array.isArray(body);
  check(checks, 'body-is-object', ok, 'body is not a JSON object');
  if (!ok) return { pass: false, checks };

  if (typeof e.slugEquals === 'string') check(checks, 'slug-matches-request', body.slug === e.slugEquals, `slug ${body.slug}`);
  check(checks, 'label-present', typeof body.label === 'string' && LABEL_PATTERN.test(body.label), `label ${JSON.stringify(body.label)}`);
  check(checks, 'methodology-version-present', typeof body.methodologyVersion === 'string' && body.methodologyVersion !== '', 'no methodologyVersion');
  check(checks, 'facts-present', body.facts !== null && typeof body.facts === 'object', 'no facts block');
  const facts = body.facts ?? {};
  const factsJson = JSON.stringify(facts);

  const text = typeof body.text === 'string' ? body.text : '';
  check(checks, 'text-present', text.trim().length > 0, 'empty text');
  check(checks, 'text-plain', !MARKUP.test(text), 'markup or angle brackets in the text');
  check(checks, 'disclaimer-closes-text', text.endsWith(DISCLAIMER), 'the text does not end with the mandated sentence');

  const body1 = text.includes(DISCLAIMER) ? text.split(DISCLAIMER).join(' ').trim() : text;
  check(checks, 'numbers-grounded-in-facts', numbersGrounded(body1, factsJson), `a digit run is not in the facts: ${digitRuns(body1).filter((r) => !new Set(digitRuns(factsJson)).has(r)).join(',')}`);
  check(checks, 'no-ranking-words', !RANKING.test(body1), 'ranking or comparison-of-quality vocabulary');
  check(checks, 'no-comparison-with-other-school', !COMPARISON.test(body1), 'compares with another school');
  check(checks, 'no-certainty-words', !CERTAINTY.test(body1), 'certainty word (pewny, gwarantowany, na pewno)');
  check(checks, 'no-causal-or-people-claims', !CAUSAL_OR_PEOPLE.test(body1), 'a cause or a claim about teachers, pupils or families');
  check(checks, 'no-personal-data-shape', findPii(text).length === 0, 'an e-mail, phone or PESEL shape in the text');
  const words = body1.split(/\s+/).filter(Boolean).length;
  check(checks, 'length-cap', words <= MAX_WORDS, `${words} words`);

  const status = facts.scoreStatus;
  if (typeof e.scoreStatus === 'string') check(checks, 'score-status', status === e.scoreStatus, `scoreStatus ${status}, expected ${e.scoreStatus}`);
  const scoreValue = facts.score?.score ?? null;
  if (typeof e.scorePresent === 'boolean') {
    check(checks, 'score-presence', (scoreValue !== null) === e.scorePresent, `score ${scoreValue} with scorePresent ${e.scorePresent}`);
  }
  if (status !== 'published') {
    check(checks, 'no-decimal-number-without-score', !/\d[,.]\d/.test(body1), 'a decimal number appears where no score is published');
    const pattern = NO_SCORE_TEXT_STATUS[status];
    if (pattern) check(checks, 'explains-missing-state', pattern.test(body1), `text does not say why there is no score (${status})`);
  }
  if (typeof e.cohortSize === 'number') check(checks, 'cohort-size', facts.cohortSize === e.cohortSize, `cohortSize ${facts.cohortSize}, expected ${e.cohortSize}`);
  if (typeof e.examYear === 'number') check(checks, 'exam-year', facts.score?.examYear === e.examYear || body.examYear === e.examYear, `examYear ${body.examYear}`);

  const coverageLow = typeof facts.coveragePercent === 'number'
    && typeof facts.coverageFloorPercent === 'number'
    && facts.coveragePercent < facts.coverageFloorPercent;
  if (typeof e.coverageLow === 'boolean') check(checks, 'coverage-low-flag', coverageLow === e.coverageLow, `coverage ${facts.coveragePercent} low=${coverageLow}`);
  if (coverageLow) check(checks, 'low-coverage-is-said', COVERAGE_MENTION.test(body1), 'coverage is low but the text does not say so');

  return { pass: checks.every((c) => c.pass), checks };
}

registerGrader(feature, grade);
