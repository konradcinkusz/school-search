// Errors that mean "the harness or its configuration is wrong", not "a case failed".
// run.mjs maps ConfigError to exit code 2.
export class ConfigError extends Error {
  constructor(message) {
    super(message);
    this.name = 'ConfigError';
  }
}
