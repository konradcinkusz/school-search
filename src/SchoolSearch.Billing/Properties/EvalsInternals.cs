using System.Runtime.CompilerServices;

// Exposes internal AI boundary types (the Anthropic*Client seams and their guards) to the offline and live eval
// project (tests/SchoolSearch.Evals.Tests). Nothing else is exposed; production code does not depend on this.
[assembly: InternalsVisibleTo("SchoolSearch.Evals.Tests")]
