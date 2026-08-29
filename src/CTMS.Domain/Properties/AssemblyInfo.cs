using System.Runtime.CompilerServices;

// The persistence layer owns audit-timestamp bookkeeping and advances the optimistic
// concurrency token on TranslationString, so it needs write access to the internal setters
// on Entity and TranslationString.Version.
[assembly: InternalsVisibleTo("CTMS.Infrastructure")]
