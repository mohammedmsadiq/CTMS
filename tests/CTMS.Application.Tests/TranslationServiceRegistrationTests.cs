using CTMS.Application.Languages;
using CTMS.Application.Projects;
using CTMS.Application.Tests.Infrastructure;
using CTMS.Application.Translations;
using CTMS.Domain.Languages;
using CTMS.Domain.Projects;
using CTMS.Domain.Translations;
using CTMS.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace CTMS.Application.Tests;

/// <summary>
/// Proves the spec's headline requirement (§2, §12–14, §51): an internal .NET consumer can
/// register the translation engine into a plain <see cref="ServiceCollection"/> with
/// <see cref="DependencyInjection.AddTranslationServices"/>, resolve <see cref="ITranslationService"/>
/// and get the same merged / published-only / fallback-resolved bundle the REST API returns —
/// with no <c>WebApplicationFactory</c>, no HTTP.
/// </summary>
[Collection("mongo")]
public sealed class TranslationServiceRegistrationTests
{
    private readonly MongoFixture _fixture;

    public TranslationServiceRegistrationTests(MongoFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Internal_consumer_resolves_ITranslationService_and_gets_the_merged_bundle_with_no_http()
    {
        var databaseName = "ctms_notest_" + Guid.NewGuid().ToString("N");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:CtmsDatabase"] = _fixture.ConnectionString,
                ["Mongo:Database"] = databaseName,
                // ConnectionStrings:Redis intentionally unset -> in-memory distributed cache.
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTranslationServices(configuration);

        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var sp = scope.ServiceProvider;

        await SeedAsync(sp);

        var translations = sp.GetRequiredService<ITranslationService>();

        var bundle = await translations.GetTranslationsAsync("icoach", "fr-FR");

        Assert.Equal("icoach", bundle.Project);
        Assert.Equal("fr-FR", bundle.Language);

        // Common value merged in, project value present, fallback fills fr-FR from en-GB,
        // the Draft and Archived strings are not served.
        Assert.Equal("Enregistrer", bundle.Translations["common.save"]);
        Assert.Equal("Commencer", bundle.Translations["course.start"]);
        Assert.Equal("Resume", bundle.Translations["course.resume"]); // en-GB fallback
        Assert.False(bundle.Translations.ContainsKey("course.draft"));
        Assert.False(bundle.Translations.ContainsKey("course.retired"));

        Assert.False(string.IsNullOrWhiteSpace(bundle.ETag));

        // The ETag is stable across calls when nothing changed.
        var again = await translations.GetTranslationsAsync("icoach", "fr-FR");
        Assert.Equal(bundle.ETag, again.ETag);

        await new MongoClient(_fixture.ConnectionString).DropDatabaseAsync(databaseName);
    }

    private static async Task SeedAsync(IServiceProvider sp)
    {
        var languages = sp.GetRequiredService<ILanguageRepository>();
        var projects = sp.GetRequiredService<IProjectRepository>();
        var keys = sp.GetRequiredService<ITranslationKeyRepository>();
        var strings = sp.GetRequiredService<ITranslationStringRepository>();

        await languages.AddAsync(new Language("en-GB", "English"));
        await languages.AddAsync(new Language("fr-FR", "French", fallbackCode: "en-GB"));

        var common = new Project("Common", "common", "en-GB", isCommon: true);
        common.SetEnabledLanguages(["en-GB", "fr-FR"]);
        await projects.AddAsync(common);

        var icoach = new Project("iCoach", "icoach", "en-GB");
        icoach.SetEnabledLanguages(["en-GB", "fr-FR"]);
        await projects.AddAsync(icoach);

        await PublishAsync(keys, strings, common.Id, "common.save", "fr-FR", "Enregistrer");
        await PublishAsync(keys, strings, icoach.Id, "course.start", "fr-FR", "Commencer");
        await PublishAsync(keys, strings, icoach.Id, "course.resume", "en-GB", "Resume");

        var draftKey = new TranslationKey(icoach.Id, "course.draft", "Course", "seed");
        await keys.AddAsync(draftKey);
        await strings.AddAsync(new TranslationString(draftKey.Id, "fr-FR", "brouillon", "seed"));

        var retiredKey = new TranslationKey(icoach.Id, "course.retired", "Course", "seed");
        await keys.AddAsync(retiredKey);
        var retired = new TranslationString(retiredKey.Id, "fr-FR", "retiré", "seed");
        retired.ChangeReviewState(ReviewState.Archived, "seed");
        await strings.AddAsync(retired);
    }


    private static async Task PublishAsync(
        ITranslationKeyRepository keys,
        ITranslationStringRepository strings,
        Guid projectId,
        string keyName,
        string language,
        string value)
    {
        var key = new TranslationKey(projectId, keyName, "Common", "seed");
        await keys.AddAsync(key);

        var str = new TranslationString(key.Id, language, value, "seed");
        str.ChangeReviewState(ReviewState.InReview, "seed");
        str.ChangeReviewState(ReviewState.Approved, "seed");
        str.ChangeReviewState(ReviewState.Published, "seed");
        await strings.AddAsync(str);
    }
}
