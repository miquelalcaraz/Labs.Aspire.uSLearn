

using MediatR;

using Microsoft.EntityFrameworkCore;

using uSLearn.Accounts.Application.IntegrationEvents;
using uSLearn.Accounts.Infrastructure;
using uSLearn.Core.EventBus.Extensions;

public class TransactionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    private readonly ILogger<TransactionBehavior<TRequest, TResponse>> _logger;
    private readonly AccountContext _dbContext;
    private readonly IAccountIntegrationEventService _accountIntegrationEventService;

    public TransactionBehavior(AccountContext dbContext,
        IAccountIntegrationEventService accountIntegrationEventService,
        ILogger<TransactionBehavior<TRequest, TResponse>> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentException(nameof(AccountContext));
        _accountIntegrationEventService = accountIntegrationEventService ?? throw new ArgumentException(nameof(accountIntegrationEventService));
        _logger = logger ?? throw new ArgumentException(nameof(ILogger));
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var response = default(TResponse);
        var typeName = request.GetGenericTypeName();

        try
        {
            if (_dbContext.HasActiveTransaction)
            {
                return await next(cancellationToken);
            }

            var strategy = _dbContext.Database.CreateExecutionStrategy();
            var attempt = 0;
            Guid transactionId = default;

            await strategy.ExecuteAsync(async () =>
            {
                // Entities tracked by a failed attempt would collide with the ones the retry adds again
                if (attempt++ > 0)
                {
                    _dbContext.ChangeTracker.Clear();
                }

                await using var transaction = await _dbContext.BeginTransactionAsync();
                using (_logger.BeginScope(new List<KeyValuePair<string, object>> { new("TransactionContext", transaction.TransactionId) }))
                {
                    try
                    {
                        _logger.LogInformation("Begin transaction {TransactionId} for {CommandName} ({@Command})", transaction.TransactionId, typeName, request);

                        response = await next(cancellationToken);

                        _logger.LogInformation("Commit transaction {TransactionId} for {CommandName}", transaction.TransactionId, typeName);

                        await _dbContext.CommitTransactionAsync(transaction);
                    }
                    catch
                    {
                        // Clears the context's current transaction so a retry can begin a new one
                        _dbContext.RollbackTransaction();
                        throw;
                    }

                    transactionId = transaction.TransactionId;
                }
            });

            // Publish outside the execution strategy: a transient failure here must not re-run the committed command
            await _accountIntegrationEventService.PublishEventsThroughEventBusAsync(transactionId);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error Handling transaction for {CommandName} ({@Command})", typeName, request);

            throw;
        }
    }
}
