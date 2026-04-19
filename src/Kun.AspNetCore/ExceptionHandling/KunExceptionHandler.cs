using Kun.AspNetCore.Mapping;
using Kun.Core.Domain;
using Kun.Core.Results;
using Kun.Validation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Kun.AspNetCore.ExceptionHandling;

/// <summary>
/// Global exception handler that translates Kun-specific exceptions into
/// RFC 7807 ProblemDetails responses.
/// <list type="bullet">
///   <item><see cref="ValidationException"/> → 400 Bad Request with validation errors.</item>
///   <item><see cref="ResultFailureException"/> → mapped via <see cref="ErrorToProblemDetailsExtensions.ToProblemDetails"/>.</item>
///   <item><see cref="DomainException"/> → 400 Bad Request with the domain error code.</item>
///   <item>All other exceptions → rethrown for the default handler to produce 500.</item>
/// </list>
/// </summary>
public sealed class KunExceptionHandler : IExceptionHandler
{
    private static readonly Action<ILogger, string, Exception> s_domainError =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(1, "DomainException"),
            "Domain exception caught: {Code}");

    private static readonly Action<ILogger, string, Exception> s_resultError =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(2, "ResultFailure"),
            "Result failure exception caught: {Code}");

    private static readonly Action<ILogger, Exception> s_validationError =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(3, "ValidationFailure"),
            "Validation exception caught");

    private readonly ILogger<KunExceptionHandler> _logger;

    /// <summary>Initializes the exception handler.</summary>
    /// <param name="logger">Logger for diagnostics.</param>
    public KunExceptionHandler(ILogger<KunExceptionHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        Microsoft.AspNetCore.Mvc.ProblemDetails? pd = exception switch
        {
            ValidationException ve => BuildValidationProblemDetails(ve),
            ResultFailureException rfe => HandleResultFailure(rfe),
            DomainException de => HandleDomainException(de),
            _ => null,
        };

        if (pd is null)
            return false; // let default middleware handle it

        httpContext.Response.StatusCode = pd.Status ?? 500;
        httpContext.Response.ContentType = "application/problem+json";
        await httpContext.Response.WriteAsJsonAsync(pd, cancellationToken);
        return true;
    }

    private Microsoft.AspNetCore.Mvc.ProblemDetails BuildValidationProblemDetails(ValidationException ve)
    {
        s_validationError(_logger, ve);
        var pd = Error.Validation("validation.failed", ve.Message).ToProblemDetails();
        pd.Extensions["errors"] = ve.Errors.Select(e => new
        {
            property = e.PropertyName,
            code = e.Code,
            message = e.Message,
        }).ToArray();
        return pd;
    }

    private Microsoft.AspNetCore.Mvc.ProblemDetails HandleResultFailure(ResultFailureException rfe)
    {
        s_resultError(_logger, rfe.Error.Code, rfe);
        return rfe.Error.ToProblemDetails();
    }

    private Microsoft.AspNetCore.Mvc.ProblemDetails HandleDomainException(DomainException de)
    {
        s_domainError(_logger, de.Code, de);
        return Error.Failure(de.Code, de.Message).ToProblemDetails();
    }
}
