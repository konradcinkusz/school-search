// Command-line parsing for run.mjs. Kept out of run.mjs so tests can import it without starting a run.
import { ConfigError } from './errors.mjs';

export function parseArgs(argv) {
  const options = { selftest: false, live: false, features: null, help: false };
  for (let i = 0; i < argv.length; i += 1) {
    const arg = argv[i];
    if (arg === '--selftest') options.selftest = true;
    else if (arg === '--live') options.live = true;
    else if (arg === '--help' || arg === '-h') options.help = true;
    else if (arg === '--feature') {
      const value = argv[i + 1];
      if (value === undefined || value.startsWith('--')) throw new ConfigError('--feature needs a value');
      options.features = value.split(',').map((name) => name.trim()).filter((name) => name !== '');
      i += 1;
    } else {
      throw new ConfigError(`unknown argument ${JSON.stringify(arg)}`);
    }
  }
  return options;
}
