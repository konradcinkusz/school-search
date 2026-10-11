#!/usr/bin/env node
// Tier A eval runner. Usage:
//   node evals/run.mjs --selftest                 offline checks, no network, no key (what CI runs)
//   node evals/run.mjs --live [--feature a,b]     calls EVAL_API_BASE / EVAL_AGENT_BASE; requires --live
// Exit codes: 0 ok (including NOT RUN, see report), 1 selftest failure or a threshold FAIL, 2 usage or config error.
import { defaultPaths } from './lib/paths.mjs';
import { ConfigError } from './lib/errors.mjs';
import { runSelftest } from './lib/selftest.mjs';
import { runLive } from './lib/live.mjs';
import { parseArgs } from './lib/args.mjs';

const USAGE = `usage:
  node evals/run.mjs --selftest
  node evals/run.mjs --live [--feature <name>[,<name>...]]

live settings (environment): EVAL_API_BASE, EVAL_AGENT_BASE, EVAL_TOKEN_BUDGET (default 200000 estimated tokens),
  EVAL_MAX_CASES, EVAL_PACE_MS (default 1000), EVAL_TIMEOUT_MS (default 30000), EVAL_MODEL_ID, EVAL_PROMPT_VERSION
`;

async function main(argv) {
  const options = parseArgs(argv);
  if (options.help) {
    process.stdout.write(USAGE);
    return 0;
  }
  if (options.selftest === options.live) {
    process.stderr.write(USAGE);
    throw new ConfigError('choose exactly one of --selftest or --live (live calls the network and needs --live)');
  }
  const paths = defaultPaths();
  const log = (line) => process.stdout.write(`${line}\n`);

  if (options.selftest) {
    if (options.features !== null) throw new ConfigError('--feature applies to --live only');
    const result = await runSelftest(paths);
    for (const row of result.summary) {
      const classes = Object.entries(row.classes).map(([name, count]) => `${name} ${count}`).join(', ');
      log(`${row.feature}: ${row.cases} cases (${classes}); fixtures pass ${row.fixturesPass}, fail ${row.fixturesFail}`);
    }
    for (const warning of result.warnings) log(`warning: ${warning}`);
    for (const error of result.errors) log(`error: ${error}`);
    log(result.ok ? 'selftest: OK' : `selftest: FAILED (${result.errors.length} problem(s))`);
    return result.ok ? 0 : 1;
  }

  const outcome = await runLive({ paths, selected: options.features, log });
  return outcome.exitCode;
}

main(process.argv.slice(2)).then(
  (code) => {
    process.exitCode = code;
  },
  (error) => {
    const kind = error instanceof ConfigError ? 'configuration error' : 'harness error';
    process.stderr.write(`${kind}: ${error.message}\n`);
    process.exitCode = 2;
  },
);
