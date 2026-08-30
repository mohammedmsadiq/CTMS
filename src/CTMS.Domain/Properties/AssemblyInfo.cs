using System.Runtime.CompilerServices;

// The persistence layer owns audit-timestamp bookkeeping (CreatedAt / UpdatedAt), so it needs
// write access to the internal setters on Entity.
[assembly: InternalsVisibleTo("CTMS.Infrastructure")]
