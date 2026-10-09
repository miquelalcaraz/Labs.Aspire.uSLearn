using System.Net;
using System.Text.Json;

using uSLearn.Core.Application.Exceptions;

namespace uSLearn.Accounts.Infrastructure.Middleware;

/// <summary>
/// Global exception handling middleware that catches all unhandled exceptions and returns appropriate HTTP responses.
/// - ValidationException: 400 Bad Request with structured validation errors
/// - BadHttpRequestException (e.g. missing x-requestid header): its own status code, usually 400
/// - Other exceptions: 500 Internal Server Error with RFC 9110 format
/// </summary>
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger,
        IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning(ex, "Validation failed for {RequestMethod} {RequestPath}",
                context.Request.Method, context.Request.Path);
            await HandleValidationExceptionAsync(context, ex);
        }
        catch (BadHttpRequestException ex)
        {
            _logger.LogWarning(ex, "Bad request for {RequestMethod} {RequestPath}",
                context.Request.Method, context.Request.Path);
            await HandleBadRequestExceptionAsync(context, ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception for {RequestMethod} {RequestPath}: {ExceptionType}",
                context.Request.Method, context.Request.Path, ex.GetType().FullName);
            await HandleGenericExceptionAsync(context, ex);
        }
    }

    private static async Task HandleValidationExceptionAsync(HttpContext context, ValidationException exception)
    {
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)HttpStatusCode.BadRequest;

        var response = new
        {
            type = "https://tools.ietf.org/html/rfc9110#section-15.5.1",
            title = "One or more validation errors occurred.",
            status = 400,
            errors = exception.Errors,
            traceId = context.TraceIdentifier
        };

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            // Error keys match the camelCase request fields (taxIdNumber, not TaxIdNumber)
            DictionaryKeyPolicy = JsonNamingPolicy.CamelCase
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(response, options));
    }

    private static async Task HandleBadRequestExceptionAsync(HttpContext context, BadHttpRequestException exception)
    {
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = exception.StatusCode;

        var response = new
        {
            type = "https://tools.ietf.org/html/rfc9110#section-15.5.1",
            title = "The request is invalid.",
            status = exception.StatusCode,
            detail = exception.Message,
            traceId = context.TraceIdentifier
        };

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(response, options));
    }

    private async Task HandleGenericExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;

        var response = new
        {
            type = "https://tools.ietf.org/html/rfc9110#section-15.6.1",
            title = "An error occurred while processing your request.",
            status = 500,
            traceId = context.TraceIdentifier,
            // Only include details in Development
            detail = _environment.IsDevelopment() ? exception.Message : null,
            stackTrace = _environment.IsDevelopment() ? exception.StackTrace : null
        };

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(response, options));
    }
}
