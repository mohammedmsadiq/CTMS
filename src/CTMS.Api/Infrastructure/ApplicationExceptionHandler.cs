using CTMS.Application.Common;
using CTMS.Domain.Translations;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CTMS.Api.Infrastructure;

/// <summary>Translates known application exceptions into RFC 7807 problem responses.</summary>
internal sealed class ApplicationExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetailsService;

    public ApplicationExceptionHandler(IProblemDetailsService problemDetailsService)
        => _problemDetailsService = problemDetailsService;

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (statusCode, title) = exception switch
        {
            ValidationException => (StatusCodes.Status400BadRequest, "Invalid request"),
            NotFoundException => (StatusCodes.Status404NotFound, "Resource not found"),
            SlugAlreadyInUseException => (StatusCodes.Status409Conflict, "Slug already in use"),
            ConflictException => (StatusCodes.Status409Conflict, "Conflict"),
            ConcurrencyException => (StatusCodes.Status409Conflict, "Concurrency conflict"),
            InvalidReviewTransitionException => (StatusCodes.Status409Conflict, "Invalid review transition"),
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "Concurrency conflict"),
            _ => (0, string.Empty),
        };

        if (statusCode == 0)
        {
            return false;
        }

        httpContext.Response.StatusCode = statusCode;

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = exception.Message,
        };

        switch (exception)
        {
            case ConcurrencyException concurrency:
                problemDetails.Extensions["currentVersion"] = concurrency.CurrentVersion;
                break;
            case DbUpdateConcurrencyException dbConcurrency when TryReadCurrentVersion(dbConcurrency) is { } current:
                problemDetails.Extensions["currentVersion"] = current;
                break;
            default:
                break;
        }

        return await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problemDetails,
        });
    }

    private static uint? TryReadCurrentVersion(DbUpdateConcurrencyException exception)
    {
        var entry = exception.Entries.FirstOrDefault();
        var databaseValues = entry?.GetDatabaseValues();
        if (databaseValues is null)
        {
            return null;
        }

        var hasVersion = databaseValues.Properties.Any(p => p.Name == nameof(TranslationString.Version));
        return hasVersion ? databaseValues.GetValue<uint>(nameof(TranslationString.Version)) : null;
    }
}
