using CTMS.Application.Translations;
using CTMS.Domain.Languages;
using CTMS.Domain.Projects;
using CTMS.Domain.Translations;

namespace CTMS.Application.Tests.Infrastructure;

/// <summary>Direct-to-repository seed helpers so tests can arrange state without the HTTP surface.</summary>
internal static class Seed
{
    public static async Task<Language> LanguageAsync(
        CtmsTestHarness harness,
        string code,
        string? name = null,
        string? fallbackCode = null,
        bool isRtl = false,
        bool active = true)
    {
        var language = new Language(code, name ?? code, fallbackCode, isRtl, active);
        await harness.Languages.AddAsync(language);
        return language;
    }

    public static async Task<Project> ApplicationAsync(
        CtmsTestHarness harness,
        string slug,
        string baseLanguageCode = "en-GB",
        IEnumerable<string>? enabledLanguages = null,
        bool isShared = false,
        bool active = true)
    {
        var project = new Project(slug, slug, baseLanguageCode, description: null, isShared: isShared, active: active);
        if (enabledLanguages is not null)
        {
            project.SetEnabledLanguages(enabledLanguages);
        }

        await harness.Projects.AddAsync(project);
        return project;
    }

    public static async Task<TranslationKey> KeyAsync(
        CtmsTestHarness harness,
        Guid projectId,
        string keyName,
        string category = "Common",
        bool active = true)
    {
        var key = new TranslationKey(projectId, keyName, category, "seed");
        if (!active)
        {
            key.SetActive(false);
        }

        await harness.Keys.AddAsync(key);
        return key;
    }

    /// <summary>Creates a string in the given <paramref name="state"/> directly (bypassing services).</summary>
    public static async Task<TranslationString> StringAsync(
        CtmsTestHarness harness,
        Guid keyId,
        string languageCode,
        string value,
        ReviewState state = ReviewState.Draft)
    {
        var str = new TranslationString(keyId, languageCode, value, "seed");
        switch (state)
        {
            case ReviewState.Draft:
                break;
            case ReviewState.NeedsReview:
                str.ChangeReviewState(ReviewState.NeedsReview, "seed");
                break;
            case ReviewState.Approved:
                str.ChangeReviewState(ReviewState.NeedsReview, "seed");
                str.ChangeReviewState(ReviewState.Approved, "seed");
                break;
            case ReviewState.Published:
                str.ChangeReviewState(ReviewState.NeedsReview, "seed");
                str.ChangeReviewState(ReviewState.Approved, "seed");
                str.ChangeReviewState(ReviewState.Published, "seed");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(state), state, null);
        }

        await harness.Strings.AddAsync(str);
        return str;
    }

    /// <summary>Drives a value from nothing to <see cref="ReviewState.Published"/> through the service.</summary>
    public static async Task PublishViaServiceAsync(
        CtmsTestHarness harness,
        string applicationCode,
        Guid keyId,
        string languageCode,
        string value)
    {
        await harness.TranslationStringService.UpsertAsync(
            applicationCode, keyId, languageCode, new UpsertTranslationStringRequest(value, UpdatedBy: "alice"));
        await harness.TranslationStringService.ReviewAsync(applicationCode, keyId, languageCode, "submit", "alice");
        await harness.TranslationStringService.ReviewAsync(applicationCode, keyId, languageCode, "approve", "lead");
        await harness.TranslationStringService.ReviewAsync(applicationCode, keyId, languageCode, "publish", "lead");
    }
}
