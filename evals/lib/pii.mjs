// Deterministic PII shapes for dataset and fixture validation. A hit is reported by kind only, never by value,
// so a validation error cannot echo the personal data it found.
const PATTERNS = [
  ['email', /[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)*\.[A-Za-z]{2,}/],
  ['phone', /(?<!\d)(?:\+?48[ -]?)?(?:\d{3}[ -]\d{3}[ -]\d{3}|\d{9})(?!\d)/],
  ['pesel', /(?<!\d)\d{11}(?!\d)/],
  ['postal-code', /(?<!\d)\d{2}-\d{3}(?!\d)/],
];

export const PII_KINDS = Object.freeze(PATTERNS.map(([kind]) => kind));

export function findPii(text) {
  const kinds = [];
  for (const [kind, pattern] of PATTERNS) {
    if (pattern.test(text)) kinds.push(kind);
  }
  return kinds;
}

export function looksLikePii(text) {
  return findPii(text).length > 0;
}

// Every string inside a JSON value, with a path for error messages. Object keys are not scanned.
export function stringLeaves(value, path = '$', out = []) {
  if (typeof value === 'string') {
    out.push({ path, text: value });
  } else if (Array.isArray(value)) {
    value.forEach((item, index) => stringLeaves(item, `${path}[${index}]`, out));
  } else if (value !== null && typeof value === 'object') {
    for (const [key, item] of Object.entries(value)) stringLeaves(item, `${path}.${key}`, out);
  }
  return out;
}
