using CTMS.Domain.Languages;
using CTMS.Domain.Projects;
using CTMS.Domain.Translations;
using CTMS.Infrastructure.Persistence.Mongo;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace CTMS.Infrastructure.Persistence.Startup;

/// <summary>
/// Populates a language catalogue and two sample projects on startup, but only in the
/// Development environment and only when <c>Seed:Enabled</c> is <c>true</c>. Idempotent: it does
/// nothing if the <c>common</c> project already exists.
/// </summary>
public sealed class DataSeeder : IHostedService
{
    public const string SharedApplicationSlug = "common";
    public const string SampleApplicationSlug = "nimbus";
    private const string Seeder = "seeder";

    private readonly IMongoContext _context;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<DataSeeder> _logger;

    public DataSeeder(
        IMongoContext context,
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger<DataSeeder> logger)
    {
        _context = context;
        _configuration = configuration;
        _environment = environment;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_environment.IsDevelopment() || !_configuration.GetValue("Seed:Enabled", false))
        {
            return;
        }

        var alreadySeeded = await _context.Projects.Find(p => p.Slug == SharedApplicationSlug)
            .AnyAsync(cancellationToken);
        if (alreadySeeded)
        {
            return;
        }

        _logger.LogInformation("Seeding language catalogue and sample applications.");

        await SeedLanguagesAsync(cancellationToken);
        await SeedSharedApplicationAsync(cancellationToken);
        await SeedSampleApplicationAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static readonly string[] AllLanguageCodes =
        ["en-GB", "fr-FR", "fr-CA", "de-DE", "es-ES", "ar-AE", "it-IT"];

    private async Task SeedLanguagesAsync(CancellationToken cancellationToken)
    {
        var languages = new[]
        {
            new Language("en-GB", "English (UK)"),
            new Language("fr-FR", "French", fallbackCode: "en-GB"),
            new Language("fr-CA", "French (Canada)", fallbackCode: "fr-FR"),
            new Language("de-DE", "German", fallbackCode: "en-GB"),
            new Language("es-ES", "Spanish", fallbackCode: "en-GB"),
            new Language("ar-AE", "Arabic", fallbackCode: "en-GB", isRtl: true),
            new Language("it-IT", "Italian", fallbackCode: "en-GB"),
        };

        // fr-CA -> fr-FR -> en-GB is the reference fallback chain.

        await _context.Languages.InsertManyAsync(
            languages.Select(l => l.StampCreated()),
            cancellationToken: cancellationToken);
    }

    private async Task SeedSharedApplicationAsync(CancellationToken cancellationToken)
    {
        var common = new Project(
            "Common", SharedApplicationSlug, "en-GB", "Common strings merged into every project.", isCommon: true);
        common.SetEnabledLanguages(["en-GB", "fr-FR", "de-DE", "es-ES", "ar-AE", "it-IT"]);
        await _context.Projects.InsertOneAsync(common.StampCreated(), cancellationToken: cancellationToken);

        await SeedKeyAsync(common.Id, "common.save", "Common", new (string, string, ReviewState)[]
        {
            ("en-GB", "Save", ReviewState.Published),
            ("fr-FR", "Enregistrer", ReviewState.Published),
            ("de-DE", "Speichern", ReviewState.Published),
        }, cancellationToken);

        // A project (nimbus) publishes its own "common.cancel" that overrides this one — see §22.
        await SeedKeyAsync(common.Id, "common.cancel", "Common", new (string, string, ReviewState)[]
        {
            ("en-GB", "Cancel", ReviewState.Published),
            ("fr-FR", "Annuler", ReviewState.Published),
        }, cancellationToken);

        await SeedKeyAsync(common.Id, "common.delete", "Common", new (string, string, ReviewState)[]
        {
            ("en-GB", "Delete", ReviewState.Published),
        }, cancellationToken);

        await SeedKeyAsync(common.Id, "common.legacy", "Common", new (string, string, ReviewState)[]
        {
            ("en-GB", "Legacy string", ReviewState.Archived),
        }, cancellationToken);
    }

    private async Task SeedSampleApplicationAsync(CancellationToken cancellationToken)
    {
        var nimbus = new Project("Nimbus", SampleApplicationSlug, "en-GB", "Sample application for local development.");
        nimbus.SetEnabledLanguages(AllLanguageCodes);
        await _context.Projects.InsertOneAsync(nimbus.StampCreated(), cancellationToken: cancellationToken);

        await SeedKeyAsync(nimbus.Id, "course.start", "Course", new (string, string, ReviewState)[]
        {
            ("en-GB", "Start course", ReviewState.Published),
            ("fr-FR", "Commencer le cours", ReviewState.Published),
            ("de-DE", "Kurs starten", ReviewState.Approved),
            ("es-ES", "Empezar", ReviewState.Draft),
        }, cancellationToken);

        await SeedKeyAsync(nimbus.Id, "course.resume", "Course", new (string, string, ReviewState)[]
        {
            ("en-GB", "Resume course", ReviewState.Published),
            ("fr-FR", "Reprendre le cours", ReviewState.Approved),
        }, cancellationToken);

        await SeedKeyAsync(nimbus.Id, "course.complete", "Course", new (string, string, ReviewState)[]
        {
            ("en-GB", "Complete course", ReviewState.Draft),
        }, cancellationToken);

        await SeedKeyAsync(nimbus.Id, "nav.home", "Navigation", new (string, string, ReviewState)[]
        {
            ("en-GB", "Home", ReviewState.Published),
            ("fr-FR", "Accueil", ReviewState.Published),
        }, cancellationToken);

        // Project-level override of the common "common.cancel" (see §22).
        await SeedKeyAsync(nimbus.Id, "common.cancel", "Common", new (string, string, ReviewState)[]
        {
            ("en-GB", "Exit course", ReviewState.Published),
            ("fr-FR", "Quitter le cours", ReviewState.Published),
        }, cancellationToken);

        await SeedKeyAsync(nimbus.Id, "course.retired", "Course", new (string, string, ReviewState)[]
        {
            ("en-GB", "Retired copy", ReviewState.Archived),
        }, cancellationToken);
    }

    private async Task SeedKeyAsync(
        Guid projectId,
        string keyName,
        string category,
        IReadOnlyList<(string Language, string Value, ReviewState State)> values,
        CancellationToken cancellationToken)
    {
        var key = new TranslationKey(projectId, keyName, category, Seeder);
        await _context.TranslationKeys.InsertOneAsync(key.StampCreated(), cancellationToken: cancellationToken);

        foreach (var (language, value, state) in values)
        {
            var str = new TranslationString(key.Id, language, value, Seeder);
            MoveTo(str, state);
            await _context.TranslationStrings.InsertOneAsync(str.StampCreated(), cancellationToken: cancellationToken);
        }
    }

    private static void MoveTo(TranslationString str, ReviewState target)
    {
        switch (target)
        {
            case ReviewState.Draft:
                break;
            case ReviewState.InReview:
                str.ChangeReviewState(ReviewState.InReview, Seeder);
                break;
            case ReviewState.Approved:
                str.ChangeReviewState(ReviewState.InReview, Seeder);
                str.ChangeReviewState(ReviewState.Approved, Seeder);
                break;
            case ReviewState.Published:
                str.ChangeReviewState(ReviewState.InReview, Seeder);
                str.ChangeReviewState(ReviewState.Approved, Seeder);
                str.ChangeReviewState(ReviewState.Published, Seeder);
                break;
            case ReviewState.Archived:
                str.ChangeReviewState(ReviewState.Archived, Seeder);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(target), target, "Unsupported seed state.");
        }
    }
}
