using CTMS.Application.Common;

namespace CTMS.Application.Translations.Import;

/// <summary>
/// Raised when <see cref="TranslationFileParser"/> cannot parse the supplied content for the
/// declared format. Derives from <see cref="ValidationException"/> so the API maps it to
/// <c>400 Bad Request</c>. <see cref="Line"/> is the 1-based line number when one is known.
/// </summary>
public sealed class ImportFormatException : ValidationException
{
    public ImportFormatException(string message, int? line = null)
        : base(line is { } n ? $"Line {n}: {message}" : message)
        => Line = line;

    public int? Line { get; }
}
