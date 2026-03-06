using uSLearn.Accounts.API.Domain.Exceptions;
using uSLearn.Accounts.API.Infrastructure;

namespace uSLearn.Accounts.API.Infrastructure.Idempotency;

public class RequestManager : IRequestManager
{
    private readonly AccountContext _context;

    public RequestManager(AccountContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }


    public async Task<bool> ExistAsync(Guid id)
    {
        var request = await _context.
            FindAsync<ClientRequest>(id);

        return request != null;
    }

    public async Task CreateRequestForCommandAsync<T>(Guid id)
    {
        // NOTE: Potential race condition between ExistAsync and SaveChangesAsync.
        // The unique index on ClientRequest.Id in the database provides the final enforcement.
        // In production, handle DbUpdateException for unique constraint violation:
        //
        // try {
        //     await _context.SaveChangesAsync();
        // } catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex)) {
        //     // Duplicate request - this is expected for idempotency
        //     _logger.LogWarning("Duplicate request {RequestId} ignored", id);
        // }

        var exists = await ExistAsync(id);

        var request = exists ?
            throw new AccountDomainException($"Request with {id} already exists") :
            new ClientRequest()
            {
                Id = id,
                Name = typeof(T).Name,
                Time = DateTime.UtcNow
            };

        _context.Add(request);

        await _context.SaveChangesAsync();
    }
}
