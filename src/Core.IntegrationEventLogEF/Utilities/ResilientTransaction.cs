using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace uSLearn.Core.IntegrationEventLogEF.Utilities;

/// <summary>
/// Provides resilient transaction execution with automatic retry strategy.
/// Ensures atomicity across multiple operations within a single database context.
/// </summary>
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
    /// Automatically retries on transient failures using EF Core's execution strategy.
    /// </summary>
    /// <param name="action">The business logic to execute within the transaction</param>
    public async Task ExecuteAsync(Func<Task> action)
    {
        // Use of an EF Core resiliency strategy when using multiple DbContexts within an explicit BeginTransaction()
        // See: https://docs.microsoft.com/en-us/ef/core/miscellaneous/connection-resiliency
        var strategy = _context.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                await action();
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        });
    }

    /// <summary>
    /// Executes an action within a resilient transaction and returns a result.
    /// </summary>
    /// <typeparam name="T">The return type</typeparam>
    /// <param name="action">The business logic to execute</param>
    /// <returns>The result of the action</returns>
    public async Task<T> ExecuteAsync<T>(Func<Task<T>> action)
    {
        var strategy = _context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
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
    /// Executes an action with a callback after successful commit (useful for Outbox pattern).
    /// </summary>
    /// <param name="action">The business logic to execute within the transaction</param>
    /// <param name="onCommitted">Callback to execute after successful commit (e.g., publish events)</param>
    public async Task ExecuteAsync(Func<Task> action, Func<Task> onCommitted)
    {
        var strategy = _context.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                await action();
                await transaction.CommitAsync();

                // Execute callback only after successful commit
                await onCommitted();
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
    /// Useful for sharing transactions across multiple operations.
    /// </summary>
    public IDbContextTransaction? GetCurrentTransaction() => 
        _context.Database.CurrentTransaction;
}
