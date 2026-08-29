namespace CTMS.Application.Common;

/// <summary>Raised when a request fails application/domain validation.</summary>
public class ValidationException : Exception
{
    public ValidationException(string message)
        : base(message)
    {
    }
}

/// <summary>Raised when creating a project whose slug is already taken.</summary>
public sealed class SlugAlreadyInUseException : Exception
{
    public SlugAlreadyInUseException(string slug)
        : base($"A project with the slug '{slug}' already exists.")
        => Slug = slug;

    public string Slug { get; }
}

/// <summary>Raised when a referenced resource does not exist.</summary>
public sealed class NotFoundException : Exception
{
    public NotFoundException(string message)
        : base(message)
    {
    }
}

/// <summary>Raised when a create or update would break a uniqueness constraint.</summary>
public sealed class ConflictException : Exception
{
    public ConflictException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Raised when an update is rejected because the caller's expected version no longer
/// matches the stored one.
/// </summary>
public sealed class ConcurrencyException : Exception
{
    public ConcurrencyException(long currentVersion)
        : base("The translation string was changed by someone else; reload and retry.")
        => CurrentVersion = currentVersion;

    public long CurrentVersion { get; }
}
