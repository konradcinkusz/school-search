// Shared test helpers. Not a test file (the test glob is test/*.test.mjs).
import { mkdirSync, mkdtempSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

export const EVALS_DIR = resolve(dirname(fileURLToPath(import.meta.url)), '..');
export const REGISTRY_URL = pathToFileURL(join(EVALS_DIR, 'lib', 'registry.mjs')).href;

// 40 synthetic cases, every class present, ids <feature>-001 .. -040.
export function makeCases(feature, count = 40) {
  return Array.from({ length: count }, (_, index) => {
    let scenarioClass = 'Degradation';
    if (index < 10) scenarioClass = 'Happy path';
    else if (index < 15) scenarioClass = 'Ambiguity';
    else if (index < 20) scenarioClass = 'Denied path';
    else if (index < 30) scenarioClass = 'Adversarial';
    return {
      id: `${feature}-${String(index + 1).padStart(3, '0')}`,
      class: scenarioClass,
      input: { text: `synthetic question ${index + 1}` },
      expect: { status: 200 },
      notes: 'synthetic case for tests',
    };
  });
}

export function casesToJsonl(cases) {
  return `${cases.map((c) => JSON.stringify(c)).join('\n')}\n`;
}

// A feature module whose grader passes when status is 200 and the body does not contain the word BAD.
export function featureModuleSource(feature, { target = 'api', grader = true } = {}) {
  return `import { registerGrader } from ${JSON.stringify(REGISTRY_URL)};
export const feature = ${JSON.stringify(feature)};
export const target = ${JSON.stringify(target)};
export function buildRequest(evalCase) {
  return { method: 'POST', path: '/api/v1/ai/${feature}', body: { text: evalCase.input.text } };
}
function grade(evalCase, response) {
  const checks = [
    { name: 'status-200', pass: response.status === 200, detail: 'status ' + response.status },
    { name: 'no-BAD-word', pass: !JSON.stringify(response.body ?? {}).includes('BAD'), detail: 'body checked' },
  ];
  return { pass: checks.every((check) => check.pass), checks };
}
${grader ? `registerGrader(feature, grade);` : ''}
`;
}

// Builds a complete temporary evals tree with the given features. Returns the paths object used by the harness.
export function makeTree(features, { thresholds, cases = null, goodFixture = true } = {}) {
  const root = mkdtempSync(join(tmpdir(), 'evals-test-'));
  const paths = {
    evalsDir: root,
    datasetsDir: join(root, 'datasets'),
    fixturesDir: join(root, 'fixtures'),
    featuresDir: join(root, 'features'),
    thresholdsFile: join(root, 'thresholds.json'),
    outDir: join(root, 'out'),
  };
  for (const dir of [paths.datasetsDir, paths.fixturesDir, paths.featuresDir, paths.outDir]) {
    mkdirSync(dir, { recursive: true });
  }
  for (const feature of features) {
    const dataset = join(paths.datasetsDir, feature);
    mkdirSync(dataset, { recursive: true });
    writeFileSync(join(dataset, 'cases.jsonl'), casesToJsonl(cases ?? makeCases(feature)), 'utf8');
    writeFileSync(join(dataset, 'README.md'), `# ${feature}\n`, 'utf8');
    writeFileSync(join(paths.featuresDir, `${feature}.mjs`), featureModuleSource(feature), 'utf8');
    const fixtures = join(paths.fixturesDir, feature);
    mkdirSync(fixtures, { recursive: true });
    writeFileSync(join(fixtures, 'good.json'), JSON.stringify({
      caseId: `${feature}-001`, shouldPass: true, response: { status: 200, body: { answer: 'fine' } },
    }), 'utf8');
    writeFileSync(join(fixtures, 'bad.json'), JSON.stringify({
      caseId: `${feature}-002`, shouldPass: false, response: { status: goodFixture ? 500 : 200, body: { answer: 'BAD' } },
    }), 'utf8');
  }
  writeFileSync(paths.thresholdsFile, JSON.stringify(thresholds ?? {}), 'utf8');
  return paths;
}

// A fetch stand-in. handler(url, init) returns { status, body } (body is JSON-encoded) or throws.
export function fakeFetch(handler) {
  const calls = [];
  const fetchImpl = async (url, init) => {
    calls.push({ url, init });
    const answer = await handler(url, init);
    return new Response(typeof answer.body === 'string' ? answer.body : JSON.stringify(answer.body ?? null), {
      status: answer.status,
      headers: { 'content-type': 'application/json' },
    });
  };
  return { fetchImpl, calls };
}
