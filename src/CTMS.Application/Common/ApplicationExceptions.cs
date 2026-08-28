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
