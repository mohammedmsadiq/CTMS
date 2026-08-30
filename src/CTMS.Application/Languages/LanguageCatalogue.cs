namespace CTMS.Application.Languages;

/// <summary>
/// A fixed table of ~40 common BCP-47 languages the Admin UI wizard offers as a starting point.
/// This is a constant in code — it is never persisted and never queried from the store. RTL
/// entries are the Arabic locales plus Hebrew and Persian.
/// </summary>
public static class LanguageCatalogue
{
    /// <summary>The catalogue, in a stable presentation order.</summary>
    public static IReadOnlyList<LanguageSuggestionDto> Suggestions { get; } =
    [
        new("en-GB", "English (United Kingdom)", false),
        new("en-US", "English (United States)", false),
        new("fr-FR", "French (France)", false),
        new("fr-CA", "French (Canada)", false),
        new("de-DE", "German (Germany)", false),
        new("es-ES", "Spanish (Spain)", false),
        new("es-MX", "Spanish (Mexico)", false),
        new("it-IT", "Italian (Italy)", false),
        new("pt-PT", "Portuguese (Portugal)", false),
        new("pt-BR", "Portuguese (Brazil)", false),
        new("nl-NL", "Dutch (Netherlands)", false),
        new("sv-SE", "Swedish (Sweden)", false),
        new("da-DK", "Danish (Denmark)", false),
        new("nb-NO", "Norwegian Bokmål (Norway)", false),
        new("fi-FI", "Finnish (Finland)", false),
        new("pl-PL", "Polish (Poland)", false),
        new("cs-CZ", "Czech (Czechia)", false),
        new("sk-SK", "Slovak (Slovakia)", false),
        new("ro-RO", "Romanian (Romania)", false),
        new("hu-HU", "Hungarian (Hungary)", false),
        new("el-GR", "Greek (Greece)", false),
        new("tr-TR", "Turkish (Türkiye)", false),
        new("ru-RU", "Russian (Russia)", false),
        new("uk-UA", "Ukrainian (Ukraine)", false),
        new("ar-AE", "Arabic (United Arab Emirates)", true),
        new("ar-SA", "Arabic (Saudi Arabia)", true),
        new("he-IL", "Hebrew (Israel)", true),
        new("fa-IR", "Persian (Iran)", true),
        new("hi-IN", "Hindi (India)", false),
        new("th-TH", "Thai (Thailand)", false),
        new("vi-VN", "Vietnamese (Vietnam)", false),
        new("id-ID", "Indonesian (Indonesia)", false),
        new("ms-MY", "Malay (Malaysia)", false),
        new("ja-JP", "Japanese (Japan)", false),
        new("ko-KR", "Korean (South Korea)", false),
        new("zh-CN", "Chinese (Simplified, China)", false),
        new("zh-TW", "Chinese (Traditional, Taiwan)", false),
        new("zh-HK", "Chinese (Traditional, Hong Kong)", false),
    ];
}
