namespace uSLearn.Core.Application.Abstractions;

/// <summary>
/// Provides access to request context information (RequestId, CorrelationId)
/// regardless of the execution context (HTTP, messaging, background jobs, etc.)
/// </summary>
public interface IRequestContextAccessor
{
    /// <summary>
    /// Gets the unique identifier for the current request.
    /// Used for idempotency and request tracking.
    /// </summary>
    string? RequestId { get; }

    /// <summary>
    /// Gets the correlation identifier that spans multiple requests/operations.
    /// Used for distributed tracing across services.
    /// </summary>
    string? CorrelationId { get; }
}
