using ChatSystem.Application.Interfaces;
using ChatSystem.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ChatSystem.Infrastructure.Persistence.Repositories;

public sealed class ConversationRepository : IConversationRepository
{
    private readonly AppDbContext _context;

    public ConversationRepository(AppDbContext context)
    {
        _context = context;
    }

    // ── Conversations ─────────────────────────────────────────────────────────

    public async Task<Conversation?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        // Tracking ON — callers (ChatService, GroupService) mutate and call UpdateAsync.
        return await _context.Conversations
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<Conversation>> GetByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        // Inbox query: all conversations this user participates in,
        // most recently active first.
        // Uses IX_CP_UserId_ConversationId + IX_Conversations_LastMessageAt.
        return await _context.ConversationParticipants
            .AsNoTracking()
            .Where(cp => cp.UserId == userId)
            .Join(
                _context.Conversations,
                cp => cp.ConversationId,
                c => c.Id,
                (cp, c) => c)
            .OrderByDescending(c => c.LastMessageAt)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(
        Conversation conversation,
        CancellationToken cancellationToken = default)
    {
        await _context.Conversations.AddAsync(conversation, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(
        Conversation conversation,
        CancellationToken cancellationToken = default)
    {
        _context.Conversations.Update(conversation);
        await _context.SaveChangesAsync(cancellationToken);
    }

    // ── Participants ──────────────────────────────────────────────────────────

    public async Task AddParticipantAsync(
        ConversationParticipant participant,
        CancellationToken cancellationToken = default)
    {
        await _context.ConversationParticipants.AddAsync(participant, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveParticipantAsync(
        Guid conversationId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        // ExecuteDeleteAsync (EF Core 7+): issues DELETE directly without loading
        // the entity. No round-trip to hydrate the row just to delete it.
        await _context.ConversationParticipants
            .Where(cp => cp.ConversationId == conversationId && cp.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<ConversationParticipant?> GetParticipantAsync(
        Guid conversationId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        // Tracking ON — callers may call MarkAsRead() or PromoteToAdmin()
        // then UpdateParticipantAsync.
        return await _context.ConversationParticipants
            .FirstOrDefaultAsync(
                cp => cp.ConversationId == conversationId && cp.UserId == userId,
                cancellationToken);
    }

    public async Task<IReadOnlyList<ConversationParticipant>> GetParticipantsAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        return await _context.ConversationParticipants
            .AsNoTracking()
            .Where(cp => cp.ConversationId == conversationId)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> IsParticipantAsync(
        Guid conversationId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        // Called on every message send — must be as fast as possible.
        // AnyAsync → EXISTS query, uses UQ_ConversationParticipants index.
        return await _context.ConversationParticipants
            .AnyAsync(
                cp => cp.ConversationId == conversationId && cp.UserId == userId,
                cancellationToken);
    }

    public async Task UpdateParticipantAsync(
        ConversationParticipant participant,
        CancellationToken cancellationToken = default)
    {
        _context.ConversationParticipants.Update(participant);
        await _context.SaveChangesAsync(cancellationToken);
    }
}