using System.Runtime.CompilerServices;

// The persistence layer owns audit-timestamp bookkeeping (see CtmsDbContext), so it
// needs write access to the internal timestamp setters on Entity.
[assembly: InternalsVisibleTo("CTMS.Infrastructure")]
