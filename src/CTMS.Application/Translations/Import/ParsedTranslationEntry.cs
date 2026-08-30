namespace CTMS.Application.Translations.Import;

/// <summary>One <c>(key, value)</c> pair produced by a translation-file parser.</summary>
public sealed record ParsedTranslationEntry(string Key, string Value);
