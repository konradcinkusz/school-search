import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

// Every location the harness reads or writes. Tests pass their own temporary tree instead.
export function defaultPaths() {
  const evalsDir = dirname(dirname(fileURLToPath(import.meta.url)));
  return {
    evalsDir,
    datasetsDir: join(evalsDir, 'datasets'),
    fixturesDir: join(evalsDir, 'fixtures'),
    featuresDir: join(evalsDir, 'features'),
    thresholdsFile: join(evalsDir, 'thresholds.json'),
    outDir: join(evalsDir, 'out'),
  };
}
