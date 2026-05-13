using ChatSystem.Application.Interfaces;
using ChatSystem.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ChatSystem.Infrastructure.Persistence.Repositories;

public sealed class UserRepository : IUserRepository
{
    private readonly AppDbContext _context;

    public UserRepository(AppDbContext context)
    {
        _context = context;
    }

    // ── Get by ID ─────────────────────────────────────────────────────────────

    public async Task<User?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == id && u.IsActive, cancellationToken);
    }

    // ── Exists ────────────────────────────────────────────────────────────────

    public async Task<bool> ExistsAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        // AnyAsync translates to SELECT CASE WHEN EXISTS(...) THEN 1 ELSE 0 END
        // — no row materialisation, no entity allocation.
        return await _context.Users
            .AnyAsync(u => u.Id == id && u.IsActive, cancellationToken);
    }

    // ── Get by email ─────────────────────────────────────────────────────────

    public async Task<User?> GetByEmailAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        
        return await _context.Users
            .FirstOrDefaultAsync(
                u => u.Email == email && u.IsActive,
                cancellationToken);
    }

    // ── Add ───────────────────────────────────────────────────────────────────

    public async Task AddAsync(
        User user,
        CancellationToken cancellationToken = default)
    {
        await _context.Users.AddAsync(user, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    // ── Update ────────────────────────────────────────────────────────────────

    public async Task UpdateAsync(
        User user,
        CancellationToken cancellationToken = default)
    {
        _context.Users.Update(user);
        await _context.SaveChangesAsync(cancellationToken);
    }
}