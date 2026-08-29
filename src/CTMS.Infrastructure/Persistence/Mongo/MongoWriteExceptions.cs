using MongoDB.Driver;

namespace CTMS.Infrastructure.Persistence.Mongo;

internal static class MongoWriteExceptions
{
    private const int DuplicateKey = 11000;

    /// <summary>True when the exception is a unique-index violation (E11000).</summary>
    public static bool IsDuplicateKey(this MongoWriteException exception)
        => exception.WriteError?.Category == ServerErrorCategory.DuplicateKey
            || exception.WriteError?.Code == DuplicateKey;

    public static bool IsDuplicateKey(this MongoBulkWriteException exception)
        => exception.WriteErrors.Any(e => e.Category == ServerErrorCategory.DuplicateKey || e.Code == DuplicateKey);
}
