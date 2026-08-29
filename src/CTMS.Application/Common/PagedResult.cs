namespace CTMS.Application.Common;

/// <summary>One page of results together with the total number of matching rows.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total);
