using ChatSystem.Domain.Entities;

namespace ChatSystem.Application.Interfaces;


public interface IUserRepository
{

    // Returns null when no user with that ID exists or the user is inactive.
    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    // Cheaper than GetByIdAsync when the caller only needs a membership check
    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default);

    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);

    Task AddAsync(User user, CancellationToken cancellationToken = default);

    Task UpdateAsync(User user, CancellationToken cancellationToken = default);
}