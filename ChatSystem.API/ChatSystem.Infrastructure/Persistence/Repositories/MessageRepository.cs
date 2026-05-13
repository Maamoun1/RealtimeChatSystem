using ChatSystem.Application.Interfaces;
using ChatSystem.Domain.Entities;
using ChatSystem.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ChatSystem.Infrastructure.Persistence.Repositories;


public sealed class MessageRepository : IMessageRepository
{
    private readonly AppDbContext _context;

    public MessageRepository(AppDbContext context)
    {
        _context = context;
    }

    // ── Add ───────────────────────────────────────────────────────────────────

    public async Task AddAsync(
        Message message,
        CancellationToken cancellationToken = default)
    {
        await _context.Messages.AddAsync(message, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    // ── Get by conversation (cursor-paginated) ────────────────────────────────

    public async Task<IReadOnlyList<Message>> GetByConversationIdAsync(
        Guid conversationId,
        int pageSize,
        DateTime? cursorSentAt = null,
        CancellationToken cancellationToken = default)
    {
        // AsNoTracking: these entities are read for projection — not for mutation.
        var query = _context.Messages
            .AsNoTracking()
            .Where(m => m.ConversationId == conversationId);

        // Cursor: only load messages older than the caller's oldest loaded message.
        // First page has no cursor (cursorSentAt == null) — load most recent N.
        if (cursorSentAt.HasValue)
            query = query.Where(m => m.SentAt < cursorSentAt.Value);

        return await query
            .OrderByDescending(m => m.SentAt)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
    }

    // ── Get single ────────────────────────────────────────────────────────────

    public async Task<Message?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        // Tracking ON — the caller (ChatService.DeleteMessageAsync) will call
        // domain mutation methods then UpdateAsync. EF Core needs to track changes.
        return await _context.Messages
            .FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
    }

    // ── Update ────────────────────────────────────────────────────────────────

    public async Task UpdateAsync(
        Message message,
        CancellationToken cancellationToken = default)
    {
        // The entity is already tracked if it was loaded via GetByIdAsync (tracked).
        // If it arrived from a different context scope, this marks it as Modified.
        _context.Messages.Update(message);
        await _context.SaveChangesAsync(cancellationToken);
    }

    // ── Undelivered sweep ─────────────────────────────────────────────────────

    public async Task<IReadOnlyList<Message>> GetUndeliveredMessagesForUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        // Find all messages in conversations the user participates in,
        // where the message was NOT sent by this user, and Status is still Sent.
        // This drives the reconnect delivery sweep in ChatService.
        //
        // NOTE: Tracking is ON here because ChatService will mutate each message
        // (MarkAsDelivered) then call UpdateAsync. Tracked entities reflect
        // mutations without re-fetching.
        var userConversationIds = await _context.ConversationParticipants
            .AsNoTracking()
            .Where(cp => cp.UserId == userId)
            .Select(cp => cp.ConversationId)
            .ToListAsync(cancellationToken);

        return await _context.Messages
            .Where(m =>
                userConversationIds.Contains(m.ConversationId) &&
                m.SenderId != userId &&
                m.Status == MessageStatus.Sent)
            .ToListAsync(cancellationToken);
    }
}