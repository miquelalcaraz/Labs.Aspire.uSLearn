using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace uSLearn.Core.Application.Telemetry;

/// <summary>
/// Central diagnostics configuration for Core.Application telemetry.
/// Provides ActivitySource for distributed tracing and Meter for custom metrics.
/// </summary>
public static class ApplicationDiagnostics
{
    /// <summary>
    /// Application name used as source name for tracing and metrics.
    /// </summary>
    public const string SourceName = "uSLearn.Core.Application";

    /// <summary>
    /// Version of the application (used for telemetry versioning).
    /// </summary>
    public const string Version = "1.0.0";

    /// <summary>
    /// ActivitySource for creating custom spans in distributed traces.
    /// </summary>
    public static readonly ActivitySource ActivitySource = new(SourceName, Version);

    /// <summary>
    /// Meter for creating custom metrics (counters, histograms, gauges).
    /// </summary>
    public static readonly Meter Meter = new(SourceName, Version);

    // === Counters ===

    /// <summary>
    /// Counts the total number of commands/queries processed.
    /// Tags: command_type, success (true/false)
    /// </summary>
    public static readonly Counter<long> CommandsProcessed = Meter.CreateCounter<long>(
        name: "application.commands.processed",
        unit: "{command}",
        description: "Total number of commands/queries processed");

    /// <summary>
    /// Counts the total number of validation failures.
    /// Tags: command_type, error_count
    /// </summary>
    public static readonly Counter<long> ValidationFailures = Meter.CreateCounter<long>(
        name: "application.validation.failures",
        unit: "{failure}",
        description: "Total number of validation failures");

    /// <summary>
    /// Counts the total number of domain events published.
    /// Tags: event_type
    /// </summary>
    public static readonly Counter<long> DomainEventsPublished = Meter.CreateCounter<long>(
        name: "application.domain_events.published",
        unit: "{event}",
        description: "Total number of domain events published");

    /// <summary>
    /// Counts the total number of integration events published.
    /// Tags: event_type, success (true/false)
    /// </summary>
    public static readonly Counter<long> IntegrationEventsPublished = Meter.CreateCounter<long>(
        name: "application.integration_events.published",
        unit: "{event}",
        description: "Total number of integration events published");

    /// <summary>
    /// Counts the total number of integration events received.
    /// Tags: event_type, success (true/false)
    /// </summary>
    public static readonly Counter<long> IntegrationEventsReceived = Meter.CreateCounter<long>(
        name: "application.integration_events.received",
        unit: "{event}",
        description: "Total number of integration events received");

    // === Histograms ===

    /// <summary>
    /// Records the duration of command/query execution.
    /// Tags: command_type, success (true/false)
    /// </summary>
    public static readonly Histogram<double> CommandDuration = Meter.CreateHistogram<double>(
        name: "application.commands.duration",
        unit: "ms",
        description: "Duration of command/query execution in milliseconds");

    /// <summary>
    /// Records the duration of validation.
    /// Tags: command_type, validator_count
    /// </summary>
    public static readonly Histogram<double> ValidationDuration = Meter.CreateHistogram<double>(
        name: "application.validation.duration",
        unit: "ms",
        description: "Duration of validation in milliseconds");

    /// <summary>
    /// Records the duration of database transactions.
    /// Tags: command_type, success (true/false)
    /// </summary>
    public static readonly Histogram<double> TransactionDuration = Meter.CreateHistogram<double>(
        name: "application.transactions.duration",
        unit: "ms",
        description: "Duration of database transactions in milliseconds");

    /// <summary>
    /// Records the number of validation errors per request.
    /// Tags: command_type
    /// </summary>
    public static readonly Histogram<int> ValidationErrorCount = Meter.CreateHistogram<int>(
        name: "application.validation.error_count",
        unit: "{error}",
        description: "Number of validation errors per request");
}
