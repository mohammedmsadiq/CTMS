using CTMS.Domain.Common;

namespace CTMS.Infrastructure.Persistence.Mongo;

/// <summary>
/// Audit-timestamp bookkeeping. The former ORM context did this while saving; with MongoDB
/// the repositories stamp entities just before a write.
/// </summary>
internal static class EntityStamps
{
    public static T StampCreated<T>(this T entity)
        where T : Entity
    {
        var now = DateTime.UtcNow;
        entity.CreatedAt = now;
        entity.UpdatedAt = now;
        return entity;
    }

    public static T StampUpdated<T>(this T entity)
        where T : Entity
    {
        entity.UpdatedAt = DateTime.UtcNow;
        return entity;
    }
}
