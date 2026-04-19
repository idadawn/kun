using Kun.Core.Results;
using Microsoft.AspNetCore.Mvc;

namespace Kun.AspNetCore.Mapping;

/// <summary>Extension methods for mapping <see cref="Error"/> to <see cref="ProblemDetails"/>.</summary>
public static class ErrorToProblemDetailsExtensions
{
    /// <summary>
    /// Maps a Kun <see cref="Error"/> to an RFC 7807 <see cref="ProblemDetails"/> response,
    /// translating <see cref="ErrorType"/> to the appropriate HTTP status code.
    /// </summary>
    /// <param name="error">The error to convert.</param>
    /// <returns>A <see cref="ProblemDetails"/> populated from the error.</returns>
    public static ProblemDetails ToProblemDetails(this Error error)
    {
        var (status, title) = error.Type switch
        {
            ErrorType.Validation => (400, "Bad Request"),
            ErrorType.NotFound => (404, "Not Found"),
            ErrorType.Conflict => (409, "Conflict"),
            ErrorType.Unauthorized => (401, "Unauthorized"),
            ErrorType.Forbidden => (403, "Forbidden"),
            ErrorType.Unavailable => (503, "Service Unavailable"),
            _ => (500, "Internal Server Error"),
        };

        return new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = error.Message,
            Extensions = { ["code"] = error.Code },
        };
    }
}
