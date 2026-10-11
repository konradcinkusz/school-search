// Feature modules: evals/features/<feature>.mjs, discovered by file name. Files starting with "_" are skipped.
//
// Contract (see evals/features/_example.mjs and evals/README.md):
//   export const feature = '<same as file name>';
//   export const target = 'api' | 'agent';         // which base URL: EVAL_API_BASE or EVAL_AGENT_BASE
//   export function buildRequest(evalCase) { return { method: 'POST', path: '/...', body: {...} }; }
//   registerGrader(feature, grader);                // at module top level
import { existsSync, readdirSync } from 'node:fs';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';
import { hasGrader } from './registry.mjs';

// Imports every feature module. An import failure is returned as an error, not thrown, so the caller can report all.
export async function discoverFeatures(featuresDir) {
  const modules = [];
  const errors = [];
  if (!existsSync(featuresDir)) return { modules, errors };
  const files = readdirSync(featuresDir, { withFileTypes: true })
    .filter((entry) => entry.isFile() && entry.name.endsWith('.mjs') && !entry.name.startsWith('_'))
    .map((entry) => entry.name)
    .sort();
  for (const file of files) {
    const name = file.slice(0, -'.mjs'.length);
    try {
      const module = await import(pathToFileURL(join(featuresDir, file)).href);
      modules.push({ name, module });
    } catch (error) {
      errors.push(`features/${file}: import failed (${error.name})`);
    }
  }
  return { modules, errors };
}

// Problems with one feature module. Empty means it can be used.
export function validateFeatureModule(name, module) {
  const errors = [];
  if (module.feature !== name) errors.push(`features/${name}.mjs: export feature must be "${name}"`);
  if (module.target !== 'api' && module.target !== 'agent') {
    errors.push(`features/${name}.mjs: export target must be 'api' or 'agent'`);
  }
  if (typeof module.buildRequest !== 'function') errors.push(`features/${name}.mjs: buildRequest must be a function`);
  if (!hasGrader(name)) errors.push(`features/${name}.mjs: no grader registered (call registerGrader('${name}', fn))`);
  return errors;
}
