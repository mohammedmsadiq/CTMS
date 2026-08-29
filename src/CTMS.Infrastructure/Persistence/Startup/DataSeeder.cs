using CTMS.Domain.Locales;
using CTMS.Domain.Projects;
using CTMS.Domain.Translations;
using CTMS.Infrastructure.Persistence.Mongo;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace CTMS.Infrastructure.Persistence.Startup;

/// <summary>
/// Populates a sample project on startup, but only in the Development environment and only
/// when <c>Seed:Enabled</c> is <c>true</c>. Idempotent: it does nothing if the sample project
/// already exists.
/// </summary>
public sealed class DataSeeder : IHostedService
{
    private const string SampleSlug = "marketing-site";
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

        var alreadySeeded = await _context.Projects.Find(p => p.Slug == SampleSlug).AnyAsync(cancellationToken);
        if (alreadySeeded)
        {
            return;
        }

        _logger.LogInformation("Seeding sample project '{Slug}'.", SampleSlug);

        var project = new Project("Marketing Site", SampleSlug, "en", "Sample data for local development.");
        await _context.Projects.InsertOneAsync(project.StampCreated(), cancellationToken: cancellationToken);

        var english = new Locale(project.Id, "en", "English");
        var french = new Locale(project.Id, "fr", "French");
        var arabic = new Locale(project.Id, "ar", "Arabic", isRtl: true);
        await _context.Locales.InsertManyAsync(
            new[] { english.StampCreated(), french.StampCreated(), arabic.StampCreated() },
            cancellationToken: cancellationToken);

        var seedKeys = new (string Key, string Value, ReviewState State)[]
        {
            ("home.hero.title", "Ship translations faster", ReviewState.Approved),
            ("home.hero.subtitle", "One source of truth for every locale", ReviewState.Approved),
            ("home.cta.primary", "Start free trial", ReviewState.Approved),
            ("nav.pricing", "Pricing", ReviewState.NeedsReview),
            ("footer.copyright", "© Marketing Site", ReviewState.Draft),
        };

        foreach (var (keyName, value, state) in seedKeys)
        {
            var key = new TranslationKey(project.Id, keyName);
            await _context.TranslationKeys.InsertOneAsync(key.StampCreated(), cancellationToken: cancellationToken);

            var str = new TranslationString(key.Id, english.Id, value, Seeder);
            MoveTo(str, state);
            await _context.TranslationStrings.InsertOneAsync(str.StampCreated(), cancellationToken: cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static void MoveTo(TranslationString str, ReviewState target)
    {
        switch (target)
        {
            case ReviewState.Draft:
                break;
            case ReviewState.NeedsReview:
                str.ChangeReviewState(ReviewState.NeedsReview, Seeder);
                break;
            case ReviewState.Approved:
                str.ChangeReviewState(ReviewState.NeedsReview, Seeder);
                str.ChangeReviewState(ReviewState.Approved, Seeder);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(target), target, "Unsupported seed state.");
        }
    }
}
