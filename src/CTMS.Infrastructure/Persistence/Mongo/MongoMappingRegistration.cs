using CTMS.Domain.Audit;
using CTMS.Domain.Common;
using CTMS.Domain.Locales;
using CTMS.Domain.Projects;
using CTMS.Domain.Translations;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Bson.Serialization.Options;
using MongoDB.Bson.Serialization.Serializers;

namespace CTMS.Infrastructure.Persistence.Mongo;

/// <summary>
/// Single place where BSON class maps and conventions are registered. Call <see cref="Register"/>
/// once during composition; it is idempotent.
/// </summary>
public static class MongoMappingRegistration
{
    private static readonly object Gate = new();
    private static bool _registered;

    public static void Register()
    {
        if (_registered)
        {
            return;
        }

        lock (Gate)
        {
            if (_registered)
            {
                return;
            }

            // Guids are stored as the standard UUID BSON subtype everywhere, including _id.
            BsonSerializer.TryRegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));

            // camelCase element names, tolerate unknown fields, and store every enum as a string.
            var conventions = new ConventionPack
            {
                new CamelCaseElementNameConvention(),
                new IgnoreExtraElementsConvention(true),
                new EnumRepresentationConvention(BsonType.String),
            };
            ConventionRegistry.Register(
                "ctms",
                conventions,
                type => type.Namespace?.StartsWith("CTMS.", StringComparison.Ordinal) == true);

            RegisterEntity<Project>();
            RegisterEntity<Locale>();
            RegisterEntity<TranslationKey>();
            RegisterEntity<TranslationString>();

            // AuditEntry is append-only and does not derive from Entity (no CreatedAt/UpdatedAt);
            // it still auto-maps cleanly — Id maps to _id via the default id-member convention.
            if (!BsonClassMap.IsClassMapRegistered(typeof(AuditEntry)))
            {
                BsonClassMap.RegisterClassMap<AuditEntry>(cm => cm.AutoMap());
            }

            if (!BsonClassMap.IsClassMapRegistered(typeof(TranslationBundle)))
            {
                BsonClassMap.RegisterClassMap<TranslationBundle>(cm =>
                {
                    cm.AutoMap();

                    // Bundle keys are dotted translation-key paths, so store the map as an
                    // array of { k, v } documents rather than as element names.
                    cm.MapMember(x => x.Entries).SetSerializer(
                        new ImpliedImplementationInterfaceSerializer<IReadOnlyDictionary<string, string>, Dictionary<string, string>>(
                            new DictionaryInterfaceImplementerSerializer<Dictionary<string, string>, string, string>(
                                DictionaryRepresentation.ArrayOfDocuments)));
                });
            }

            _registered = true;
        }
    }

    private static void RegisterEntity<T>()
        where T : Entity
    {
        if (!BsonClassMap.IsClassMapRegistered(typeof(T)))
        {
            BsonClassMap.RegisterClassMap<T>(cm => cm.AutoMap());
        }
    }
}
