using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace uSLearn.Core.IntegrationEventLogEF.Utilities;

/// <summary>
/// Provides resilient transaction execution with automatic retry strategy.
/// Ensures atomicity across multiple operations within a single database context.
/// </summary>
/// <remarks>
/// Retries only happen when the provider is configured with a retrying execution strategy
/// (e.g. <c>EnableRetryOnFailure</c> for SQL Server). On each retry the whole action runs again,
/// so it must be safe to re-execute.
/// </remarks>
public class ResilientTransaction
{
    private readonly DbContext _context;

    private ResilientTransaction(DbContext context) =>
        _context = context ?? throw new ArgumentNullException(nameof(context));

    /// <summary>
    /// Creates a new resilient transaction instance.
    /// </summary>
    public static ResilientTransaction New(DbContext context) => new(context);

    /// <summary>
    /// Executes an action within a resilient database transaction.
    /// </summary>
    /// <param name="action">The business logic to execute within the transaction</param>
    public Task ExecuteAsync(Func<Task> action) =>
        ExecuteAsync(async () =>
        {
            await action();
            return true;
        });

    /// <summary>
    /// Executes an action with a callback after successful commit (e.g. publish events).
    /// </summary>
    /// <param name="action">The business logic to execute within the transaction</param>
    /// <param name="onCommitted">Callback to execute after successful commit</param>
    public async Task ExecuteAsync(Func<Task> action, Func<Task> onCommitted)
    {
        await ExecuteAsync(action);
        await onCommitted();
    }

    /// <summary>
    /// Executes an action within a resilient transaction and returns a result.
    /// </summary>
    /// <typeparam name="T">The return type</typeparam>
    /// <param name="action">The business logic to execute</param>
    /// <returns>The result of the action</returns>
    public async Task<T> ExecuteAsync<T>(Func<Task<T>> action)
    {
        // Use of an EF Core resiliency strategy when using an explicit BeginTransaction()
        // See: https://learn.microsoft.com/ef/core/miscellaneous/connection-resiliency
        var strategy = _context.Database.CreateExecutionStrategy();
        var attempt = 0;

        return await strategy.ExecuteAsync(async () =>
        {
            // Entities tracked by a failed attempt would collide with the ones the retry adds again
            if (attempt++ > 0)
            {
                _context.ChangeTracker.Clear();
            }

            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var result = await action();
                await transaction.CommitAsync();
                return result;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        });
    }

    /// <summary>
    /// Gets the current transaction or null if no transaction is active.
    /// </summary>
    public IDbContextTransaction? GetCurrentTransaction() =>
        _context.Database.CurrentTransaction;
}
