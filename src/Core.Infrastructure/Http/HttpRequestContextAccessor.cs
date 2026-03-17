using System.Diagnostics;

using Microsoft.AspNetCore.Http;

using uSLearn.Core.Application.Abstractions;

namespace uSLearn.Core.Infrastructure.Http;

/// <summary>
/// HTTP-based implementation of IRequestContextAccessor.
/// Extracts RequestId and CorrelationId from HTTP headers.
/// </summary>
public class HttpRequestContextAccessor : IRequestContextAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpRequestContextAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string? RequestId
    {
        get
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext == null) return null;

            if (httpContext.Request.Headers.TryGetValue("x-requestid", out var requestId))
            {
                return requestId.ToString();
            }

            return null;
        }
    }

    public string? CorrelationId
    {
        get
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext == null) return null;

            if (httpContext.Request.Headers.TryGetValue("x-correlation-id", out var correlationId))
            {
                return correlationId.ToString();
            }

            // Fallback to OpenTelemetry Activity ID or ASP.NET Core TraceIdentifier
            return Activity.Current?.Id ?? httpContext.TraceIdentifier;
        }
    }
}
