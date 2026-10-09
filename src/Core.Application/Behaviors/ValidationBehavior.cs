using System.Diagnostics;

using FluentValidation;

using MediatR;

using uSLearn.Core.Application.Telemetry;

namespace uSLearn.Core.Application.Behaviors;

/// <summary>
/// MediatR pipeline behavior that validates commands/queries using FluentValidation validators.
/// Throws ValidationException if validation fails.
/// </summary>
/// <typeparam name="TRequest">The request type (Command or Query)</typeparam>
/// <typeparam name="TResponse">The response type</typeparam>
public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
    {
        _validators = validators;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (!_validators.Any())
        {
            // No validators registered for this request type, skip validation
            return await next(cancellationToken);
        }

        var commandName = typeof(TRequest).Name;
        var validatorCount = _validators.Count();

        // Start validation activity
        using var activity = ApplicationDiagnostics.ActivitySource.StartActivity(
            name: $"Validation {commandName}",
            kind: ActivityKind.Internal);

        activity?.SetTag("validation.command", commandName);
        activity?.SetTag("validation.validator_count", validatorCount);

        var stopwatch = Stopwatch.StartNew();

        var context = new ValidationContext<TRequest>(request);

        var validationResults = await Task.WhenAll(
            _validators.Select(v => v.ValidateAsync(context, cancellationToken)));

        stopwatch.Stop();

        var failures = validationResults
            .Where(r => r.Errors.Any())
            .SelectMany(r => r.Errors)
            .ToList();

        var elapsedMs = stopwatch.Elapsed.TotalMilliseconds;

        // Record validation duration
        ApplicationDiagnostics.ValidationDuration.Record(elapsedMs,
            new KeyValuePair<string, object?>("command_type", commandName),
            new KeyValuePair<string, object?>("validator_count", validatorCount));

        if (failures.Any())
        {
            var errorCount = failures.Count;

            // Record validation failure metrics
            ApplicationDiagnostics.ValidationFailures.Add(1,
                new KeyValuePair<string, object?>("command_type", commandName),
                new KeyValuePair<string, object?>("error_count", errorCount));

            ApplicationDiagnostics.ValidationErrorCount.Record(errorCount,
                new KeyValuePair<string, object?>("command_type", commandName));

            activity?.SetTag("validation.failed", true);
            activity?.SetTag("validation.error_count", errorCount);
            activity?.SetStatus(ActivityStatusCode.Error, "Validation failed");
            activity?.AddEvent(new ActivityEvent("ValidationFailed",
                tags: new ActivityTagsCollection
                {
                    { "errors", string.Join("; ", failures.Select(f => $"{f.PropertyName}: {f.ErrorMessage}")) }
                }));

            throw new Exceptions.ValidationException(failures);
        }

        activity?.SetTag("validation.success", true);
        activity?.SetTag("validation.duration_ms", elapsedMs);

        return await next(cancellationToken);
    }

}
