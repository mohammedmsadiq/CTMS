namespace CTMS.Infrastructure.Persistence.Mongo;

/// <summary>Bound from the <c>Mongo</c> configuration section.</summary>
public sealed class MongoOptions
{
    public const string SectionName = "Mongo";

    /// <summary>Database name inside the MongoDB server. Defaults to <c>ctms</c>.</summary>
    public string Database { get; set; } = "ctms";
}
