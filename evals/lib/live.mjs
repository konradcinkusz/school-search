// Live run: calls the real endpoints (only with --live). Writes one .json and one .md report per feature.
import { ConfigError } from './errors.mjs';
import { discoverFeatures, validateFeatureModule } from './features.mjs';
import { parseBaseUrl, sleep as defaultSleep, TokenBudget } from './http.mjs';
import { runCases } from './runner.mjs';
import { listDatasetFeatures, loadDataset } from './schema.mjs';
import { buildReport, stampFor, writeReport } from './report.mjs';
import { loadThresholdsFile } from './thresholds.mjs';

export const DEFAULT_TOKEN_BUDGET = 200000;
export const DEFAULT_PACE_MS = 1000;
export const DEFAULT_TIMEOUT_MS = 30000;

function parseInteger(raw, name, fallback, min) {
  if (raw === undefined || raw === '') return fallback;
  if (!/^\d+$/.test(raw)) throw new ConfigError(`${name} must be a whole number`);
  const value = Number(raw);
  if (!Number.isSafeInteger(value) || value < min) throw new ConfigError(`${name} must be at least ${min}`);
  return value;
}

function optionalText(raw) {
  return raw === undefined || raw === '' ? null : String(raw);
}

// Reads the EVAL_* settings. Throws ConfigError on a malformed value.
export function readLiveConfig(env) {
  return {
    apiBase: parseBaseUrl(env.EVAL_API_BASE, 'EVAL_API_BASE'),
    agentBase: parseBaseUrl(env.EVAL_AGENT_BASE, 'EVAL_AGENT_BASE'),
    tokenBudget: parseInteger(env.EVAL_TOKEN_BUDGET, 'EVAL_TOKEN_BUDGET', DEFAULT_TOKEN_BUDGET, 0),
    maxCases: parseInteger(env.EVAL_MAX_CASES, 'EVAL_MAX_CASES', null, 1),
    paceMs: parseInteger(env.EVAL_PACE_MS, 'EVAL_PACE_MS', DEFAULT_PACE_MS, 0),
    timeoutMs: parseInteger(env.EVAL_TIMEOUT_MS, 'EVAL_TIMEOUT_MS', DEFAULT_TIMEOUT_MS, 1),
    modelId: optionalText(env.EVAL_MODEL_ID),
    promptVersion: optionalText(env.EVAL_PROMPT_VERSION),
  };
}

// paths: see lib/paths.mjs. selected: feature names, or null for every dataset.
export async function runLive({
  paths,
  selected = null,
  env = process.env,
  fetchImpl = globalThis.fetch,
  sleep = defaultSleep,
  now = () => new Date(),
  log = () => {},
}) {
  const config = readLiveConfig(env);
  const discovered = await discoverFeatures(paths.featuresDir);
  if (discovered.errors.length > 0) throw new ConfigError(discovered.errors.join('; '));
  const modules = new Map(discovered.modules.map(({ name, module }) => [name, module]));
  const names = selected ?? listDatasetFeatures(paths.datasetsDir);
  if (names.length === 0) throw new ConfigError('no datasets found under evals/datasets/');

  const problems = [];
  for (const name of names) {
    if (!modules.has(name)) {
      problems.push(`${name}: no feature module evals/features/${name}.mjs`);
      continue;
    }
    problems.push(...validateFeatureModule(name, modules.get(name)));
  }
  const datasets = new Map();
  for (const name of names) {
    const dataset = loadDataset(paths.datasetsDir, name);
    problems.push(...dataset.errors);
    datasets.set(name, dataset);
  }
  if (problems.length > 0) throw new ConfigError(`validation failed:\n  ${problems.join('\n  ')}`);

  const thresholds = loadThresholdsFile(paths.thresholdsFile);
  const budget = new TokenBudget(config.tokenBudget);
  const stamp = stampFor(now());
  const outcomes = [];

  for (const name of names) {
    const module = modules.get(name);
    const dataset = datasets.get(name);
    const target = module.target;
    const base = target === 'agent' ? config.agentBase : config.apiBase;
    const baseVar = target === 'agent' ? 'EVAL_AGENT_BASE' : 'EVAL_API_BASE';
    const startedAt = now().toISOString();
    const cases = config.maxCases === null ? dataset.cases : dataset.cases.slice(0, config.maxCases);
    if (!base) log(`${name}: ${baseVar} is not set, every case is NOT RUN`);
    const caseResults = await runCases({
      feature: name,
      cases,
      buildRequest: module.buildRequest,
      base,
      timeoutMs: config.timeoutMs,
      paceMs: config.paceMs,
      budget,
      fetchImpl,
      sleep,
    });
    const report = buildReport({
      feature: name,
      target,
      baseHost: base ? new URL(base).host : null,
      startedAt,
      finishedAt: now().toISOString(),
      modelId: config.modelId,
      promptVersion: config.promptVersion,
      budget,
      datasetTotal: dataset.cases.length,
      caseResults,
      thresholdEntry: thresholds[name],
    });
    const written = writeReport({ outDir: paths.outDir, feature: name, stamp, report });
    outcomes.push({ feature: name, verdict: report.verdict, partial: report.partial, ...written });
    log(`${name}: ${report.verdict}${report.partial ? ' (partial)' : ''}, `
      + `${report.summary.passed} passed of ${report.summary.evaluated} evaluated, ${report.summary.notRun} not run`);
    log(`  ${written.jsonPath}`);
    log(`  ${written.mdPath}`);
  }

  // NOT RUN exits 0 here (the spec says 1 only for a failed threshold). The verdict in the report says NOT RUN.
  const exitCode = outcomes.some((outcome) => outcome.verdict === 'FAIL') ? 1 : 0;
  return { exitCode, outcomes };
}
